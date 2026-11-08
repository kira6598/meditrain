using Microsoft.Data.Sqlite;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class CertificateDeletionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.Tests", Guid.NewGuid().ToString("N"));
    private readonly TrainingRepository repository;

    public CertificateDeletionTests()
    {
        repository = new TrainingRepository(Path.Combine(directory, "delete-certificates.db"));
        repository.Initialize();
    }

    [Fact]
    public void RawOnlyDeletionRemovesAllMatchingSourceLinesAndPreservesOtherTextNewlinesAndFreshMetadata()
    {
        var employee = Person() with
        {
            ProfessionalCertificates = "  Giữ đầu \r\n- Chứng chỉ xóa (01/01/2026)\n\tGiữ giữa\r• Chứng chỉ   xóa (01/01/2026)\r\nGiữ cuối",
            ConferenceHistory = "- Chứng chỉ xóa (01/01/2026)\r\nHội nghị khác",
            ScientificActivityHistory = "T1: 4\nT2: 2\n"
        };
        repository.SaveEmployee(employee);
        var selected = History(employee.Id).Single(row => row.SourceCategory == "Chứng chỉ" && row.Title == "Chứng chỉ xóa");
        var fresh = employee with
        {
            Name = "Tên mới đã lưu", Department = "Khoa mới", Title = "Bác sĩ", Degree = "Bác sĩ CKII",
            SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1980, 2, 10),
            PracticeLicenseNumber = "CCHN-MỚI", PracticeLicenseDate = new DateTime(2020, 1, 2),
            ProfessionalCertificates = employee.ProfessionalCertificates + "\nThêm trong lúc mở hồ sơ"
        };
        repository.SaveEmployee(fresh);

        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, selected.Id));

        var saved = Assert.Single(repository.GetEmployees());
        Assert.Equal(fresh with { ProfessionalCertificates = "  Giữ đầu \r\n\tGiữ giữa\rGiữ cuối\nThêm trong lúc mở hồ sơ" }, saved);
        Assert.DoesNotContain(History(employee.Id), row => row.Id == selected.Id);
        Assert.Contains(History(employee.Id), row => row.SourceCategory == "Hội thảo" && row.Title == "Chứng chỉ xóa");
        Assert.Empty(repository.GetActivities());
        Assert.Empty(repository.GetSupplements());
        Assert.False(repository.DeleteTrainingHistoryRow(employee.Id, selected.Id));
    }

    [Fact]
    public void PendingSupplementDeletionAlsoRemovesLinkedRawWithoutDeletingSameTitleIndependentRows()
    {
        var employee = Person() with { ProfessionalCertificates = "Cùng tên (01/01/2026)\r\nCùng tên (01/01/2025)" };
        repository.SaveEmployee(employee);
        var selectedRaw = History(employee.Id).Single(row => row.EndDate == new DateTime(2026, 1, 1));
        var supplement = Detail(employee.Id, "detail:selected", selectedRaw.SourceReference) with { Title = "Cùng tên" };
        var unrelated = Detail(employee.Id, "detail:independent") with { Title = "Cùng tên", Hours = 7 };
        repository.SaveSupplement(supplement);
        repository.SaveSupplement(unrelated);
        var selected = History(employee.Id).Single(row => row.Id == supplement.Id);
        Assert.True(selected.IsImported);

        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, selected.Id));

        Assert.Equal(unrelated, Assert.Single(repository.GetSupplements()));
        Assert.Equal("Cùng tên (01/01/2025)", Assert.Single(repository.GetEmployees()).ProfessionalCertificates);
        var reopened = Reopen();
        var history = EmployeeTrainingHistory.Build(Assert.Single(reopened.GetEmployees()), reopened.GetActivities(), reopened.GetSupplements());
        Assert.Equal(2, history.Count);
        Assert.DoesNotContain(history, row => row.Id == supplement.Id || row.SourceReference == selectedRaw.SourceReference);
        Assert.Contains(history, row => row.Id == unrelated.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedCertificateDeletionRemovesActivitySupplementAndRawAcrossOldAndNewSourceAliases(bool confirmedFromOldRaw)
    {
        var employee = Person() with { ProfessionalCertificates = "Chứng chỉ cần xóa (01/01/2026)" };
        repository.SaveEmployee(employee);
        var raw = Assert.Single(History(employee.Id));
        var supplement = Detail(employee.Id, "detail:confirmed", raw.SourceReference);
        repository.SaveSupplement(supplement);
        var activity = Course(employee.Id) with { SourceReference = confirmedFromOldRaw ? raw.SourceReference : supplement.SourceReference };
        repository.SaveActivity(activity);
        var selected = Assert.Single(History(employee.Id));
        Assert.Equal(activity.Id, selected.Id);

        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, selected.Id));

        var reopened = Reopen();
        var saved = Assert.Single(reopened.GetEmployees());
        Assert.Equal(employee with { ProfessionalCertificates = "" }, saved);
        Assert.Empty(reopened.GetActivities());
        Assert.Empty(reopened.GetSupplements());
        Assert.Empty(EmployeeTrainingHistory.Build(saved, reopened.GetActivities(), reopened.GetSupplements()));
    }

    [Fact]
    public void DeletionFollowsTransitiveNonemptySourceLinksButKeepsUnlinkedRecords()
    {
        var employee = Person() with { ProfessionalCertificates = "Khóa học liên kết (01/01/2026)\nKhóa học độc lập" };
        repository.SaveEmployee(employee);
        var raw = History(employee.Id).Single(row => row.Title == "Khóa học liên kết");
        var first = Detail(employee.Id, "detail:one", raw.SourceReference);
        var second = Detail(employee.Id, "detail:two", first.SourceReference);
        var third = Detail(employee.Id, "detail:three", second.SourceReference);
        var unrelatedDetail = Detail(employee.Id, "detail:independent");
        repository.SaveSupplement(first);
        repository.SaveSupplement(second);
        repository.SaveSupplement(third);
        repository.SaveSupplement(unrelatedDetail);
        var linkedCourse = Course(employee.Id) with { SourceReference = second.SourceReference };
        var anotherLinkedCourse = Course(employee.Id) with { SourceReference = third.SourceReference };
        var unrelatedCourse = Course(employee.Id);
        repository.SaveActivity(linkedCourse);
        repository.SaveActivity(anotherLinkedCourse);
        repository.SaveActivity(unrelatedCourse);
        Assert.Contains(History(employee.Id), row => row.Id == first.Id);

        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, first.Id));

        Assert.Equal(unrelatedCourse, Assert.Single(repository.GetActivities()));
        Assert.Equal(unrelatedDetail, Assert.Single(repository.GetSupplements()));
        Assert.Equal("Khóa học độc lập", Assert.Single(repository.GetEmployees()).ProfessionalCertificates);
        Assert.Equal(3, History(employee.Id).Count);
    }

    [Fact]
    public void StandaloneActivityDeletionNeverUsesTitleOrEmptyReferencesToDeleteOthers()
    {
        var employee = Person() with { ProfessionalCertificates = "Trùng tên" };
        var otherEmployee = Person() with { Code = "NV002", ProfessionalCertificates = "Trùng tên" };
        repository.SaveEmployee(employee);
        repository.SaveEmployee(otherEmployee);
        var selected = Course(employee.Id) with { Title = "Trùng tên" };
        var sameTitleCourse = Course(employee.Id) with { Title = "Trùng tên" };
        var otherEmployeeCourse = Course(otherEmployee.Id) with { Title = "Trùng tên" };
        var sameTitleDetail = Detail(employee.Id, "detail:independent") with { Title = "Trùng tên" };
        repository.SaveActivity(selected);
        repository.SaveActivity(sameTitleCourse);
        repository.SaveActivity(otherEmployeeCourse);
        repository.SaveSupplement(sameTitleDetail);

        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, selected.Id));

        Assert.Equal(2, repository.GetActivities().Count);
        Assert.Contains(repository.GetActivities(), row => row == sameTitleCourse);
        Assert.Contains(repository.GetActivities(), row => row == otherEmployeeCourse);
        Assert.Equal(sameTitleDetail, Assert.Single(repository.GetSupplements()));
        Assert.Equal(employee, repository.GetEmployees().Single(row => row.Id == employee.Id));
        Assert.Equal(otherEmployee, repository.GetEmployees().Single(row => row.Id == otherEmployee.Id));
        Assert.Equal(3, History(employee.Id).Count);
    }

    [Fact]
    public void CrossEmployeeMissingAndStaleSelectionsReturnFalseWithoutChangingAnyData()
    {
        var employee = Person() with { ProfessionalCertificates = "Chứng chỉ nguồn" };
        var other = Person() with { Code = "NV002" };
        repository.SaveEmployee(employee);
        repository.SaveEmployee(other);
        var oldRaw = Assert.Single(History(employee.Id));
        var supplement = Detail(employee.Id, "detail:current", oldRaw.SourceReference);
        repository.SaveSupplement(supplement);
        var course = Course(employee.Id);
        repository.SaveActivity(course);
        var employeesBefore = repository.GetEmployees();
        var coursesBefore = repository.GetActivities();
        var supplementsBefore = repository.GetSupplements();

        Assert.False(repository.DeleteTrainingHistoryRow(other.Id, supplement.Id));
        Assert.False(repository.DeleteTrainingHistoryRow(other.Id, course.Id));
        Assert.False(repository.DeleteTrainingHistoryRow(employee.Id, oldRaw.Id));
        Assert.False(repository.DeleteTrainingHistoryRow("missing-employee", supplement.Id));
        Assert.False(repository.DeleteTrainingHistoryRow(employee.Id, "missing-row"));
        Assert.False(repository.DeleteTrainingHistoryRow("", supplement.Id));

        Assert.Equal(employeesBefore, repository.GetEmployees());
        Assert.Equal(coursesBefore, repository.GetActivities());
        Assert.Equal(supplementsBefore, repository.GetSupplements());
    }

    [Fact]
    public void FailedRawUpdateRollsBackEarlierActivityAndSupplementDeletes()
    {
        var employee = Person() with { ProfessionalCertificates = "Chứng chỉ phải xóa nguyên tử (01/01/2026)" };
        repository.SaveEmployee(employee);
        var raw = Assert.Single(History(employee.Id));
        var supplement = Detail(employee.Id, "detail:atomic", raw.SourceReference);
        var activity = Course(employee.Id) with { SourceReference = supplement.SourceReference };
        repository.SaveSupplement(supplement);
        repository.SaveActivity(activity);
        ExecuteSql("CREATE TRIGGER RejectSourceRemoval BEFORE UPDATE OF ProfessionalCertificates, ConferenceHistory, ScientificActivityHistory ON Employees BEGIN SELECT RAISE(ABORT, 'simulated source write failure'); END;");

        Assert.Throws<SqliteException>(() => repository.DeleteTrainingHistoryRow(employee.Id, activity.Id));

        Assert.Equal(employee, Assert.Single(repository.GetEmployees()));
        Assert.Equal(activity, Assert.Single(repository.GetActivities()));
        Assert.Equal(supplement, Assert.Single(repository.GetSupplements()));
        Assert.Equal(activity.Id, Assert.Single(History(employee.Id)).Id);
        ExecuteSql("DROP TRIGGER RejectSourceRemoval;");
        Assert.True(repository.DeleteTrainingHistoryRow(employee.Id, activity.Id));
        Assert.Empty(History(employee.Id));
    }

    private IReadOnlyList<EmployeeTrainingHistoryRow> History(string employeeId)
        => EmployeeTrainingHistory.Build(repository.GetEmployees().Single(employee => employee.Id == employeeId), repository.GetActivities(), repository.GetSupplements());
    private TrainingRepository Reopen() { var reopened = new TrainingRepository(repository.DatabasePath); reopened.Initialize(); return reopened; }
    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static Employee Person() => new() { Code = "NV001", Name = "Nhân viên mẫu", Department = "Khoa mẫu" };
    private static TrainingSupplement Detail(string employeeId, string source, string linked = "") => new()
    {
        EmployeeId = employeeId, Title = "Chứng chỉ mẫu", SourceReference = source, LinkedSourceReference = linked,
        SourceSheet = "Sheet2", SourceRow = 4, Hours = 16, IssuedDate = new DateTime(2026, 1, 1), Provider = "Đơn vị mẫu"
    };
    private static TrainingActivity Course(string employeeId) => new()
    {
        EmployeeId = employeeId, Title = "Khóa học mẫu", Provider = "Đơn vị mẫu", Type = "CME", Hours = 12,
        StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 2)
    };
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
