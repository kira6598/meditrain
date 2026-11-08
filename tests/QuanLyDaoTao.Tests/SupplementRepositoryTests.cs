using Microsoft.Data.Sqlite;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class SupplementRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.Tests", Guid.NewGuid().ToString("N"));
    private readonly TrainingRepository repository;

    public SupplementRepositoryTests()
    {
        repository = new TrainingRepository(Path.Combine(directory, "supplements.db"));
        repository.Initialize();
    }

    [Fact]
    public void VersionThreeMigrationPreservesSourceReferencesAndAllowsNewCertificateFields()
    {
        var employee = Person();
        repository.SaveEmployee(employee);
        var activity = Course(employee.Id) with { SourceReference = "excel:old-raw-source" };
        repository.SaveActivity(activity);
        using (var connection = new SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE TrainingActivities DROP COLUMN IssuedDate; ALTER TABLE TrainingActivities DROP COLUMN StudyPeriod; DROP TABLE TrainingSupplements; PRAGMA user_version=3;";
            command.ExecuteNonQuery();
        }

        repository.Initialize();
        Assert.Equal(activity, Assert.Single(repository.GetActivities()));
        Assert.Equal(employee, Assert.Single(repository.GetEmployees()));
        Assert.Empty(repository.GetSupplements());
        repository.SaveSupplement(Detail(employee.Id));
        repository.SaveActivity(activity with { IssuedDate = new DateTime(2026, 2, 10), StudyPeriod = "Tháng 01/2026" });
        var reopened = new TrainingRepository(repository.DatabasePath);
        reopened.Initialize();
        Assert.Equal(new DateTime(2026, 2, 10), Assert.Single(reopened.GetActivities()).IssuedDate);
        Assert.Equal("Tháng 01/2026", Assert.Single(reopened.GetActivities()).StudyPeriod);
        Assert.Single(reopened.GetSupplements());
    }

    [Fact]
    public void SupplementPreservesSeparateIssueAndStudyDatesAndNullableMissingFields()
    {
        var employee = Person();
        repository.SaveEmployee(employee);
        var complete = Detail(employee.Id) with
        {
            Title = "Chứng chỉ phẫu thuật 'mẫu'", Hours = 16.500000000001m,
            IssuedDate = new DateTime(2026, 3, 20), StartDate = new DateTime(2025, 12, 1), EndDate = new DateTime(2026, 1, 20),
            StudyPeriod = "01/12/2025 đến 20/01/2026", SourceSheet = "Sheet2", SourceRow = 4,
            LinkedSourceReference = "excel:raw-course", Notes = "Dòng gốc\n Giữ nguyên để đối chiếu"
        };
        var incomplete = Detail(employee.Id) with { SourceReference = "detail:incomplete", IssuedDate = new DateTime(2026, 4, 1) };
        repository.SaveSupplement(complete);
        repository.SaveSupplement(incomplete);

        var reopened = new TrainingRepository(repository.DatabasePath);
        reopened.Initialize();
        Assert.Equal(complete, reopened.GetSupplements().Single(row => row.Id == complete.Id));
        Assert.Equal(incomplete, reopened.GetSupplements().Single(row => row.Id == incomplete.Id));
        Assert.Empty(reopened.GetActivities());
        Assert.Equal(0, Assert.Single(ReportService.BuildAnnual([employee], reopened.GetActivities(), 2026)).TotalHours);
    }

    [Fact]
    public void ReimportUsesEmployeeAndSourceIdentityWhileSavingByIdCanUpdateSource()
    {
        var employee = Person();
        repository.SaveEmployee(employee);
        var original = Detail(employee.Id);
        repository.SaveSupplement(original);
        var updated = original with { Id = Guid.NewGuid().ToString("N"), Hours = 8, Provider = "Đơn vị bổ sung" };
        repository.ImportData([], [updated]);
        var saved = Assert.Single(repository.GetSupplements());
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(8, saved.Hours);
        Assert.Equal(updated.Provider, saved.Provider);
        repository.SaveSupplement(saved with { SourceReference = "detail:revised-source" });
        Assert.Equal("detail:revised-source", Assert.Single(repository.GetSupplements()).SourceReference);
        Assert.Empty(repository.GetActivities());
    }

    [Fact]
    public void FailedCertificateBatchRollsBackEmployeeUpdatesInsertsAndEarlierCertificateWrites()
    {
        var original = Person();
        repository.SaveEmployee(original);
        var newEmployee = Person() with { Code = "NV002", Name = "Nhân viên mới" };
        var valid = Detail(newEmployee.Id);
        var invalidReference = Detail("missing-employee") with { SourceReference = "detail:missing-person" };

        Assert.Throws<InvalidOperationException>(() => repository.ImportData([original with { Name = "Không được lưu" }, newEmployee], [valid, invalidReference]));

        Assert.Equal(original, Assert.Single(repository.GetEmployees()));
        Assert.Empty(repository.GetSupplements());
        Assert.Empty(repository.GetActivities());
    }

    [Fact]
    public void InvalidSupplementDataAndDuplicateSourceBatchAreRejectedWithoutPartialWrites()
    {
        var employee = Person();
        repository.SaveEmployee(employee);
        var valid = Detail(employee.Id);
        Assert.Throws<ArgumentException>(() => repository.SaveSupplement(valid with { SourceReference = " " }));
        Assert.Throws<ArgumentException>(() => repository.SaveSupplement(valid with { Title = " " }));
        Assert.Throws<ArgumentException>(() => repository.SaveSupplement(valid with { Hours = 0 }));
        Assert.Throws<ArgumentException>(() => repository.SaveSupplement(valid with { StartDate = new DateTime(2026, 2, 2), EndDate = new DateTime(2026, 2, 1) }));
        Assert.Throws<InvalidOperationException>(() => repository.SaveSupplement(valid with { EmployeeId = "missing" }));
        Assert.Throws<ArgumentException>(() => repository.ImportData([], [valid, valid with { Id = Guid.NewGuid().ToString("N") }]));
        Assert.Empty(repository.GetSupplements());
    }

    [Fact]
    public void SupplementBackupAndEmployeeCascadeRemainIndependent()
    {
        var employee = Person();
        repository.ImportData([employee], [Detail(employee.Id)]);
        var backupPath = Path.Combine(directory, "backup.db");
        repository.Backup(backupPath);
        var backup = new TrainingRepository(backupPath);
        backup.Initialize();
        Assert.Equal(repository.GetSupplements(), backup.GetSupplements());
        repository.DeleteEmployee(employee.Id);
        Assert.Empty(repository.GetSupplements());
        Assert.Single(backup.GetSupplements());
        Assert.Single(backup.GetEmployees());
    }

    private static Employee Person() => new() { Code = "NV001", Name = "Nhân viên mẫu", Department = "Khoa mẫu" };
    private static TrainingSupplement Detail(string employeeId) => new() { EmployeeId = employeeId, Title = "Chứng chỉ mẫu", SourceReference = "detail:course-one" };
    private static TrainingActivity Course(string employeeId) => new() { EmployeeId = employeeId, Title = "Khóa đã ghi nhận", Provider = "Đơn vị mẫu", Type = "CME", Hours = 8, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 2) };
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
