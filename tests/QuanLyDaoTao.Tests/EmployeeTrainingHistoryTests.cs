using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class EmployeeTrainingHistoryTests
{
    [Fact]
    public void ImportedCertificatesConferencesAndMonthlyNotesBecomeSeparateRowsWithoutCreatingActivities()
    {
        var employee = Person() with
        {
            ProfessionalCertificates = "-\r\n- Phẫu thuật cắt dịch kính cơ bản (23/07/2025)\r\n- Chẩn đoán hình ảnh (21/10/2016)\r\n- Laser điều trị bệnh lý dịch kính và võng mạc",
            ConferenceHistory = "Hành trình hồi phục sau Phaco (22/06/2026)",
            ScientificActivityHistory = "T1: 4\nT2: 2"
        };
        var original = employee with { };
        var activities = new List<TrainingActivity>();

        var rows = EmployeeTrainingHistory.Build(employee, activities);

        Assert.Equal(6, rows.Count);
        Assert.All(rows, row => { Assert.True(row.IsImported); Assert.Null(row.Activity); Assert.Null(row.Hours); });
        Assert.Equal(new DateTime(2026, 6, 22), rows[0].EndDate);
        Assert.Equal("Hành trình hồi phục sau Phaco", rows[0].Title);
        Assert.Equal("Hội thảo", rows[0].Type);
        var surgery = Assert.Single(rows, row => row.Title == "Phẫu thuật cắt dịch kính cơ bản");
        Assert.Equal(new DateTime(2025, 7, 23), surgery.StartDate);
        Assert.Equal("Chứng chỉ", surgery.Type);
        Assert.Equal("- Phẫu thuật cắt dịch kính cơ bản (23/07/2025)", surgery.SourceText);
        var monthlyRows = rows.Where(row => row.SourceCategory == "SHKHKT").ToArray();
        Assert.Equal(new[] { "T1: 4", "T2: 2" }, monthlyRows.Select(row => row.Title));
        Assert.All(monthlyRows, row => { Assert.Null(row.StartDate); Assert.Null(row.EndDate); Assert.Equal("SHKHKT", row.Type); });
        Assert.Equal(original, employee);
        Assert.Empty(activities);
        Assert.Equal(0m, Assert.Single(ReportService.BuildAnnual([employee], activities, 2026)).TotalHours);
    }

    [Theory]
    [InlineData("Khóa học (31/02/2026)")]
    [InlineData("Khóa học (1/2/26)")]
    [InlineData("Khóa học từ 01/01/2026 đến 02/01/2026")]
    [InlineData("Khóa học (01/01/2026 - 03/01/2026)")]
    public void InvalidIncompleteAndRangedDatesRemainUnknown(string text)
    {
        var row = Assert.Single(EmployeeTrainingHistory.Build(Person() with { ProfessionalCertificates = text }, []));
        Assert.Null(row.StartDate);
        Assert.Null(row.EndDate);
        Assert.Equal(text, row.Title);
    }

    [Theory]
    [InlineData("CME Cập nhật chuyên môn 1,5 tiết (1/2/2026)", "CME", 1.5)]
    [InlineData("Khóa học 16 tiết", "Chứng chỉ", 16)]
    public void OnlyExplicitPositiveHoursAndExplicitCmeAreRecognized(string text, string type, decimal hours)
    {
        var row = Assert.Single(EmployeeTrainingHistory.Build(Person() with { ProfessionalCertificates = text }, []));
        Assert.Equal(type, row.Type);
        Assert.Equal(hours, row.Hours);
    }

    [Theory]
    [InlineData("T1: 4")]
    [InlineData("Khóa học 0 tiết")]
    [InlineData("Khóa học -4 tiết")]
    [InlineData("Khóa học 4 tiết hoặc 8 tiết")]
    public void AmbiguousOrNonPositiveHoursRemainUnknown(string text)
    {
        var row = Assert.Single(EmployeeTrainingHistory.Build(Person() with { ScientificActivityHistory = text }, []));
        Assert.Null(row.Hours);
    }

    [Fact]
    public void StableReferencesIgnoreBulletWhitespaceAndLineOrderAndDeduplicateWithinSourceOnly()
    {
        var employee = Person() with { ProfessionalCertificates = "- Khóa học   A (01/01/2026)\n• Khóa học A (01/01/2026)\n* Khóa học B\n- — •  " };
        var rows = EmployeeTrainingHistory.Build(employee, []);
        Assert.Equal(2, rows.Count);
        var reordered = EmployeeTrainingHistory.Build(employee with { ProfessionalCertificates = "Khóa học B\nKhóa học A (01/01/2026)", ConferenceHistory = "Khóa học B" }, []);
        Assert.Equal(3, reordered.Count);
        Assert.All(rows, row => Assert.Contains(reordered, other => row.Id == other.Id));
        Assert.Equal(3, reordered.Select(row => row.SourceReference).Distinct().Count());
    }

    [Fact]
    public void PromotedSourceIsReplacedByStructuredCourseAndOtherEmployeesNeverLeakOrSuppress()
    {
        var employee = Person() with { ProfessionalCertificates = "Khóa học mẫu (01/01/2026)\nChứng chỉ chưa hoàn thiện" };
        var source = EmployeeTrainingHistory.Build(employee, []).Single(row => row.Title == "Khóa học mẫu");
        var promoted = Course(employee.Id) with { SourceReference = source.SourceReference };
        var otherCourse = Course("employee-other") with { SourceReference = EmployeeTrainingHistory.Build(employee, []).Single(row => row.Title == "Chứng chỉ chưa hoàn thiện").SourceReference, Hours = 999 };
        var rows = EmployeeTrainingHistory.Build(employee, [promoted, otherCourse]);

        Assert.Equal(2, rows.Count);
        var structured = Assert.Single(rows, row => !row.IsImported);
        Assert.Same(promoted, structured.Activity);
        Assert.Equal(promoted.Id, structured.Id);
        Assert.Equal(promoted.Hours, structured.Hours);
        Assert.Equal("Chứng chỉ chưa hoàn thiện", Assert.Single(rows, row => row.IsImported).Title);
        Assert.DoesNotContain(rows, row => row.Activity?.EmployeeId == "employee-other");
    }

    [Fact]
    public void MergedRowsSortByCompletionDateThenTitleWithUnknownDatesLast()
    {
        var employee = Person() with { ProfessionalCertificates = "Z chưa rõ\nA chưa rõ\nChứng chỉ mới (20/06/2026)" };
        var structured = Course(employee.Id) with { StartDate = new DateTime(2025, 12, 31), EndDate = new DateTime(2026, 1, 3) };
        var rows = EmployeeTrainingHistory.Build(employee, [structured]);
        Assert.Equal(["Chứng chỉ mới", structured.Title, "A chưa rõ", "Z chưa rõ"], rows.Select(row => row.Title));
    }

    [Fact]
    public void DetailedSupplementReplacesOnlyLinkedRawRowAndKeepsIssueDateSeparateFromStudyEnd()
    {
        var employee = Person() with { ProfessionalCertificates = "Chứng chỉ A (20/03/2026)\nChứng chỉ B chưa bổ sung" };
        var raw = EmployeeTrainingHistory.Build(employee, []);
        var detail = new TrainingSupplement
        {
            EmployeeId = employee.Id, Title = "Chứng chỉ A", SourceReference = "detail:certificate-a",
            LinkedSourceReference = raw.Single(row => row.Title == "Chứng chỉ A").SourceReference,
            IssuedDate = new DateTime(2026, 3, 20), StartDate = new DateTime(2025, 12, 1), EndDate = new DateTime(2026, 1, 20),
            Hours = 16, Provider = "Trường mẫu", StudyPeriod = "01/12/2025 - 20/01/2026", SourceSheet = "Sheet2", SourceRow = 12, Notes = "Thông tin chứng chỉ bổ sung"
        };
        var otherEmployeeDetail = detail with { Id = "other-detail", EmployeeId = "other-employee", LinkedSourceReference = raw.Single(row => row.Title == "Chứng chỉ B chưa bổ sung").SourceReference };

        var rows = EmployeeTrainingHistory.Build(employee, [], [detail, otherEmployeeDetail]);

        Assert.Equal(2, rows.Count);
        var detailed = Assert.Single(rows, row => row.Supplement is not null);
        Assert.Same(detail, detailed.Supplement);
        Assert.True(detailed.IsImported);
        Assert.Null(detailed.Activity);
        Assert.Equal(detail.EndDate, detailed.EndDate);
        Assert.Equal(detail.IssuedDate, detailed.IssuedDate);
        Assert.NotEqual(detailed.IssuedDate, detailed.EndDate);
        Assert.Equal(detail.StudyPeriod, detailed.StudyPeriod);
        Assert.Equal(detail.Provider, detailed.Provider);
        Assert.Equal(16, detailed.Hours);
        Assert.Contains("Sheet2", detailed.SourceText);
        Assert.Contains("12", detailed.SourceText);
        Assert.Contains(detail.Notes, detailed.SourceText);
        Assert.Contains(rows, row => row.Title == "Chứng chỉ B chưa bổ sung" && row.Supplement is null);
        Assert.DoesNotContain(rows, row => row.SourceReference == detail.LinkedSourceReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupplementEnrichesConfirmedSourceWithoutDuplicatingOrChangingCreditedHoursAndType(bool confirmedFromOriginalRaw)
    {
        var employee = Person() with { ProfessionalCertificates = "Chứng chỉ mẫu (01/01/2026)" };
        var rawSource = Assert.Single(EmployeeTrainingHistory.Build(employee, [])).SourceReference;
        var detail = new TrainingSupplement
        {
            EmployeeId = employee.Id, SourceReference = "detail:confirmed-course", LinkedSourceReference = rawSource,
            Title = "Tên từ Excel", Type = "CME", Hours = 99, Provider = "Đơn vị trên Excel",
            IssuedDate = new DateTime(2026, 2, 1), StartDate = new DateTime(2025, 12, 1), EndDate = new DateTime(2025, 12, 31), StudyPeriod = "12/2025"
        };
        var activity = Course(employee.Id) with
        {
            SourceReference = confirmedFromOriginalRaw ? rawSource : detail.SourceReference,
            Type = "SHKHKT", Hours = 7, Provider = "Đơn vị đã xác nhận"
        };

        var row = Assert.Single(EmployeeTrainingHistory.Build(employee, [activity], [detail]));

        Assert.False(row.IsImported);
        Assert.Same(activity, row.Activity);
        Assert.Same(detail, row.Supplement);
        Assert.Equal(activity.Title, row.Title);
        Assert.Equal("SHKHKT", row.Type);
        Assert.Equal(7, row.Hours);
        Assert.Equal(activity.StartDate, row.StartDate);
        Assert.Equal(activity.EndDate, row.EndDate);
        Assert.Equal(activity.Provider, row.Provider);
        Assert.Equal(detail.IssuedDate, row.IssuedDate);
        Assert.Equal(detail.StudyPeriod, row.StudyPeriod);

        var amended = activity with { IssuedDate = new DateTime(2026, 2, 10), StudyPeriod = "Lịch học đã chỉnh sửa" };
        var amendedRow = Assert.Single(EmployeeTrainingHistory.Build(employee, [amended], [detail]));
        Assert.Equal(amended.IssuedDate, amendedRow.IssuedDate);
        Assert.Equal(amended.StudyPeriod, amendedRow.StudyPeriod);
    }

    [Fact]
    public void IssueOnlySupplementDoesNotInventStudyDatesAndEmptySourceDoesNotMatchUnrelatedActivity()
    {
        var employee = Person();
        var detail = new TrainingSupplement
        {
            EmployeeId = employee.Id, Title = "Chứng chỉ chỉ có ngày cấp", SourceReference = "detail:issue-only",
            IssuedDate = new DateTime(2026, 3, 20), StudyPeriod = "Chưa có lịch học chi tiết"
        };
        var unrelated = Course(employee.Id);
        var rows = EmployeeTrainingHistory.Build(employee, [unrelated], [detail]);

        Assert.Equal(2, rows.Count);
        var pending = Assert.Single(rows, row => row.IsImported);
        Assert.Null(pending.StartDate);
        Assert.Null(pending.EndDate);
        Assert.Null(pending.Hours);
        Assert.Equal(detail.IssuedDate, pending.IssuedDate);
        Assert.Equal(detail.StudyPeriod, pending.StudyPeriod);
        Assert.Null(Assert.Single(rows, row => !row.IsImported).Supplement);
    }

    private static Employee Person() => new() { Id = "employee", Code = "NV001", Name = "Nhân viên mẫu", Department = "Khoa mẫu" };
    private static TrainingActivity Course(string employeeId) => new() { EmployeeId = employeeId, Title = "Khóa học đã xác nhận", Type = "CME", Provider = "Đơn vị mẫu", Hours = 12, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 1) };
}
