using Microsoft.Data.Sqlite;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.Tests", Guid.NewGuid().ToString("N"));
    private readonly TrainingRepository _repository;

    public RepositoryTests()
    {
        _repository = new TrainingRepository(Path.Combine(_directory, "training.db"));
        _repository.Initialize();
    }

    [Fact]
    public void FirstRunIsEmptyAndInitializationPreservesExistingData()
    {
        Assert.Empty(_repository.GetEmployees());
        Assert.Empty(_repository.GetActivities());
        var employee = Person();
        _repository.SaveEmployee(employee);
        _repository.Initialize();
        Assert.Equal(employee, Assert.Single(_repository.GetEmployees()));
    }

    [Fact]
    public void UnicodeAndSqlLiteralsRoundTripAndUpdatesSurviveReopen()
    {
        var employee = Person() with { Name = "ĐD. Nguyễn Thị Ánh '); DROP TABLE Employees; --", StartDate = new DateTime(2020, 2, 29) };
        _repository.SaveEmployee(employee);
        var activity = Activity(employee.Id) with { Hours = 11.500000000001m, Notes = "Đã nộp bản sao <gốc> & 'xác nhận'", CertificateNumber = "CME-ĐT/2026", IssuedDate = new DateTime(2026, 6, 15), StudyPeriod = "01/06/2026 – 02/06/2026" };
        _repository.SaveActivity(activity);
        _repository.SaveEmployee(employee with { Name = "Nguyễn Thị Ánh", Degree = "Cử nhân Điều dưỡng" });
        _repository.SaveActivity(activity with { Provider = "Đại học Y Dược TP.HCM" });

        var reopened = new TrainingRepository(_repository.DatabasePath);
        Assert.Equal("Nguyễn Thị Ánh", Assert.Single(reopened.GetEmployees()).Name);
        Assert.Equal("Cử nhân Điều dưỡng", Assert.Single(reopened.GetEmployees()).Degree);
        Assert.Equal(employee.StartDate, Assert.Single(reopened.GetEmployees()).StartDate);
        var savedActivity = Assert.Single(reopened.GetActivities());
        Assert.Equal(activity.Hours, savedActivity.Hours);
        Assert.Equal(activity.Notes, savedActivity.Notes);
        Assert.Equal(activity.IssuedDate, savedActivity.IssuedDate);
        Assert.Equal(activity.StudyPeriod, savedActivity.StudyPeriod);
        Assert.Equal("Đại học Y Dược TP.HCM", savedActivity.Provider);
    }

    [Fact]
    public void VersionTwoMigrationPreservesRawNotesAndActivitiesAndPersistsSourceReference()
    {
        var employee = Person() with { ProfessionalCertificates = "- Chứng chỉ mẫu (01/01/2026)\nDòng chưa rõ ngày", ConferenceHistory = "Hội nghị mẫu" };
        var activity = Activity(employee.Id);
        _repository.SaveEmployee(employee);
        _repository.SaveActivity(activity);
        using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX IX_TrainingActivities_SourceReference; ALTER TABLE TrainingActivities DROP COLUMN SourceReference; ALTER TABLE TrainingActivities DROP COLUMN IssuedDate; ALTER TABLE TrainingActivities DROP COLUMN StudyPeriod; DROP TABLE TrainingSupplements; PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }

        _repository.Initialize();
        Assert.Equal(employee, Assert.Single(_repository.GetEmployees()));
        Assert.Equal(activity, Assert.Single(_repository.GetActivities()));
        var source = EmployeeTrainingHistory.Build(employee, []).Single(row => row.Title == "Chứng chỉ mẫu").SourceReference;
        _repository.SaveActivity(activity with { SourceReference = source });
        var reopened = new TrainingRepository(_repository.DatabasePath);
        reopened.Initialize();
        Assert.Equal(source, Assert.Single(reopened.GetActivities()).SourceReference);
        var history = EmployeeTrainingHistory.Build(Assert.Single(reopened.GetEmployees()), reopened.GetActivities());
        var remainingImported = history.Where(row => row.IsImported).ToArray();
        Assert.Equal(new[] { "Dòng chưa rõ ngày", "Hội nghị mẫu" }, remainingImported.Select(row => row.Title));
        Assert.DoesNotContain(remainingImported, row => row.SourceReference == source || row.Title == "Chứng chỉ mẫu");
        var promoted = Assert.Single(history, row => !row.IsImported);
        Assert.Equal(activity.Id, promoted.Id);
        Assert.Equal(source, promoted.SourceReference);
        using var verification = new SqliteConnection($"Data Source={_repository.DatabasePath};Pooling=False");
        verification.Open();
        using var version = verification.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, version.ExecuteScalar());
    }

    [Fact]
    public void SameImportedSourceCannotBeCreditedTwiceForOneEmployeeButUnlinkedCoursesRemainIndependent()
    {
        var employee = Person();
        var other = Person() with { Code = "NV002" };
        _repository.SaveEmployee(employee);
        _repository.SaveEmployee(other);
        var linked = Activity(employee.Id) with { SourceReference = "excel:original-row" };
        _repository.SaveActivity(linked);
        _repository.SaveActivity(linked with { Hours = 16 });
        Assert.Throws<InvalidOperationException>(() => _repository.SaveActivity(Activity(employee.Id) with { SourceReference = linked.SourceReference }));
        _repository.SaveActivity(Activity(other.Id) with { SourceReference = linked.SourceReference });
        _repository.SaveActivity(Activity(employee.Id));
        _repository.SaveActivity(Activity(employee.Id));
        Assert.Equal(4, _repository.GetActivities().Count);
        Assert.Equal(16, _repository.GetActivities().Single(course => course.Id == linked.Id).Hours);
    }

    [Fact]
    public void DuplicateCodeIsCaseInsensitiveAndFailedSavePreservesOriginal()
    {
        var original = Person() with { Code = "NV001" };
        _repository.SaveEmployee(original);
        Assert.Throws<InvalidOperationException>(() => _repository.SaveEmployee(Person() with { Code = " nv001 " }));
        Assert.Equal(original, Assert.Single(_repository.GetEmployees()));
    }

    [Fact]
    public void DeleteActivityAndCascadeEmployeeDeletionPersist()
    {
        var first = Person();
        var second = Person() with { Code = "NV002" };
        _repository.SaveEmployee(first);
        _repository.SaveEmployee(second);
        var directDelete = Activity(first.Id);
        _repository.SaveActivity(directDelete);
        _repository.SaveActivity(Activity(first.Id));
        _repository.SaveActivity(Activity(second.Id));
        _repository.DeleteActivity(directDelete.Id);
        Assert.Equal(2, _repository.GetActivities().Count);
        _repository.DeleteEmployee(first.Id);
        Assert.Equal(second.Id, Assert.Single(_repository.GetActivities()).EmployeeId);
        Assert.Equal(second.Id, Assert.Single(_repository.GetEmployees()).Id);
    }

    [Theory]
    [InlineData("", "Nguyễn An", "Khoa Nội")]
    [InlineData("NV001", " ", "Khoa Nội")]
    [InlineData("NV001", "Nguyễn An", "")]
    public void MissingEmployeeFieldsAreRejected(string code, string name, string department)
    {
        Assert.Throws<ArgumentException>(() => _repository.SaveEmployee(Person() with { Code = code, Name = name, Department = department }));
        Assert.Empty(_repository.GetEmployees());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void NonPositiveHoursAreRejected(decimal hours)
    {
        var employee = Person();
        _repository.SaveEmployee(employee);
        Assert.Throws<ArgumentException>(() => _repository.SaveActivity(Activity(employee.Id) with { Hours = hours }));
        Assert.Empty(_repository.GetActivities());
    }

    [Fact]
    public void InvalidDatesTypeAndEmployeeReferenceAreRejected()
    {
        var employee = Person();
        _repository.SaveEmployee(employee);
        var activity = Activity(employee.Id);
        Assert.Throws<ArgumentException>(() => _repository.SaveActivity(activity with { EndDate = activity.StartDate.AddDays(-1) }));
        Assert.Throws<ArgumentException>(() => _repository.SaveActivity(activity with { Type = "OTHER" }));
        Assert.Throws<InvalidOperationException>(() => _repository.SaveActivity(activity with { EmployeeId = "missing" }));
        Assert.Empty(_repository.GetActivities());
    }

    [Fact]
    public void LoadingSamplesRequiresExplicitReplacement()
    {
        var employee = Person();
        _repository.SaveEmployee(employee);
        Assert.Throws<InvalidOperationException>(() => _repository.SeedSampleData());
        Assert.Equal(employee, Assert.Single(_repository.GetEmployees()));
        _repository.SeedSampleData(true);
        Assert.Equal(7, _repository.GetEmployees().Count);
        Assert.Equal(14, _repository.GetActivities().Count);
        Assert.DoesNotContain(_repository.GetEmployees(), candidate => candidate.Id == employee.Id);
    }

    [Fact]
    public void FailedSampleReplacementRollsBackDeletesAndPartialInserts()
    {
        var employee = Person();
        var activity = Activity(employee.Id);
        _repository.SaveEmployee(employee);
        _repository.SaveActivity(activity);
        using (var connection = new SqliteConnection($"Data Source={_repository.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER FailSeed BEFORE INSERT ON Employees WHEN NEW.Code = 'NV002' BEGIN SELECT RAISE(ABORT, 'simulated seed failure'); END;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => _repository.SeedSampleData(true));
        Assert.Equal(employee, Assert.Single(_repository.GetEmployees()));
        Assert.Equal(activity, Assert.Single(_repository.GetActivities()));
    }

    [Fact]
    public void BackupIsReopenableIndependentSnapshotAndCannotOverwriteSource()
    {
        _repository.SeedSampleData();
        var backupPath = Path.Combine(_directory, "backup", "snapshot.db");
        _repository.Backup(backupPath);
        var backup = new TrainingRepository(backupPath);
        backup.Initialize();
        Assert.Equal(_repository.GetEmployees(), backup.GetEmployees());
        Assert.Equal(_repository.GetActivities(), backup.GetActivities());
        _repository.DeleteEmployee(_repository.GetEmployees()[0].Id);
        Assert.Equal(7, backup.GetEmployees().Count);
        Assert.Equal(14, backup.GetActivities().Count);
        Assert.Throws<ArgumentException>(() => _repository.Backup(_repository.DatabasePath));
        Assert.Equal(6, _repository.GetEmployees().Count);
    }

    private static Employee Person() => new() { Code = "NV001", Name = "Nguyễn An", Department = "Khoa Nội" };
    private static TrainingActivity Activity(string employeeId) => new()
    {
        EmployeeId = employeeId, Title = "Cập nhật điều trị", Provider = "Bệnh viện Đa khoa", Type = "CME", Hours = 12m,
        StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 2)
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
