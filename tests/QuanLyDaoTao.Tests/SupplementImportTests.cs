using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using QuanLyDaoTao.Core;
using Xunit;

namespace QuanLyDaoTao.Tests;

public sealed class SupplementImportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.SupplementTests", Guid.NewGuid().ToString("N"));
    private const string FirstName = "Nhân viên Mẫu Một";
    private const string SecondName = "Nhân viên Mẫu Hai";

    public SupplementImportTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void DetailSheetIsPreviewedSeparatelyAndOnlyExplicitMergedNameIsCarried()
    {
        var preview = EmployeeImportService.Read(Workbook());
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(3, preview.SupplementRows.Count);
        Assert.All(preview.Rows, row => Assert.Equal("Nhân viên", row.SheetName));
        Assert.All(preview.SupplementRows, row => Assert.True(row.IsValid, string.Join("; ", row.Errors)));
        Assert.Equal([FirstName, FirstName, SecondName], preview.SupplementRows.Select(row => row.EmployeeName));
        Assert.Equal([3, 4, 5], preview.SupplementRows.Select(row => row.RowNumber));
        var incomplete = preview.SupplementRows[1];
        Assert.Null(incomplete.StartDate); Assert.Null(incomplete.EndDate); Assert.Null(incomplete.Hours);
        Assert.Equal("2020-2021", incomplete.StudyPeriod);
        Assert.NotEmpty(incomplete.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IssueDateStudyRangeAndDecimalHoursRemainSeparate(bool date1904)
    {
        var preview = EmployeeImportService.Read(Workbook(date1904: date1904));
        var first = preview.SupplementRows[0];
        Assert.Equal(new DateTime(2025, 7, 23), first.IssuedDate);
        Assert.Equal(new DateTime(2025, 7, 15), first.StartDate);
        Assert.Equal(new DateTime(2025, 7, 20), first.EndDate);
        Assert.Equal("15/07/2025 - 20/07/2025", first.StudyPeriod);
        Assert.Equal(1.5m, first.Hours);
        Assert.Equal("Bệnh viện Minh Họa", first.Provider);
        var textHours = preview.SupplementRows[2];
        Assert.Equal(1.5m, textHours.Hours);
        Assert.Equal(new DateTime(2026, 2, 10), textHours.StartDate);
        Assert.Equal(new DateTime(2026, 2, 10), textHours.EndDate);
        Assert.Equal(new DateTime(2026, 2, 15), textHours.IssuedDate);
    }

    [Fact]
    public void ApplyingAndRepeatingDetailImportIsIdempotentWithoutGrantingCredits()
    {
        var path = Workbook();
        var repository = Repository();
        var first = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        Assert.Equal(2, first.Added); Assert.Equal(3, first.SupplementsAdded);
        Assert.Equal(0, first.SupplementsUpdated); Assert.Equal(0, first.SupplementsSkipped);
        var saved = repository.GetSupplements();
        Assert.Equal(3, saved.Count); Assert.Empty(repository.GetActivities());
        var employee = repository.GetEmployees().Single(row => row.Name == FirstName);
        var history = EmployeeTrainingHistory.Build(employee, repository.GetActivities(), saved);
        Assert.Equal(2, history.Count);
        Assert.All(history, row => Assert.True(row.IsImported));
        Assert.Equal(1.5m, history.Single(row => row.Title == "Khóa học minh họa").Hours);
        Assert.Equal(0, Assert.Single(ReportService.BuildAnnual([employee], repository.GetActivities(), 2025)).TotalHours);
        Assert.All(saved.Where(row => row.EmployeeId == employee.Id), row => Assert.False(string.IsNullOrWhiteSpace(row.LinkedSourceReference)));
        var second = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        Assert.Equal(0, second.Added); Assert.Equal(2, second.Skipped);
        Assert.Equal(0, second.SupplementsAdded); Assert.Equal(0, second.SupplementsUpdated); Assert.Equal(3, second.SupplementsSkipped);
        Assert.Equal(saved.OrderBy(row => row.Id), repository.GetSupplements().OrderBy(row => row.Id));
        Assert.Empty(repository.GetActivities());
    }

    [Fact]
    public void DetailUpdatesNeverOverwriteAConfirmedActivityOrRemoveOriginalNotes()
    {
        var path = Workbook();
        var repository = Repository();
        EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        var employee = repository.GetEmployees().Single(row => row.Name == FirstName);
        var supplement = repository.GetSupplements().Single(row => row.EmployeeId == employee.Id && row.Title == "Khóa học minh họa");
        var confirmed = new TrainingActivity
        {
            EmployeeId = employee.Id, Title = "Tên đã được người dùng xác nhận", Type = "CME", Provider = "Đơn vị đã xác nhận",
            StartDate = new DateTime(2025, 7, 15), EndDate = new DateTime(2025, 7, 20), Hours = 16,
            IssuedDate = new DateTime(2025, 7, 23), StudyPeriod = "15/07/2025 - 20/07/2025", SourceReference = supplement.SourceReference
        };
        repository.SaveActivity(confirmed);
        Workbook(firstHours: 2.5m);
        var result = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path), ExistingEmployeeImportMode.UpdateNonEmpty);
        Assert.Equal(0, result.SupplementsAdded); Assert.Equal(1, result.SupplementsUpdated);
        Assert.Equal(confirmed, Assert.Single(repository.GetActivities()));
        var refreshed = repository.GetSupplements().Single(row => row.SourceReference == supplement.SourceReference);
        Assert.Equal(supplement.Id, refreshed.Id); Assert.Equal(2.5m, refreshed.Hours);
        var retainedEmployee = repository.GetEmployees().Single(row => row.Id == employee.Id);
        Assert.Equal(employee.ProfessionalCertificates, retainedEmployee.ProfessionalCertificates);
        var history = EmployeeTrainingHistory.Build(retainedEmployee, repository.GetActivities(), repository.GetSupplements());
        Assert.Equal(2, history.Count);
        Assert.Single(history, row => row.Activity?.Id == confirmed.Id);
        Assert.Equal(16, Assert.Single(ReportService.BuildAnnual([retainedEmployee], repository.GetActivities(), 2025)).TotalHours);
    }

    [Fact]
    public void BlankEmployeeOutsideExplicitMergedRangeBlocksTheWholeImport()
    {
        var preview = EmployeeImportService.Read(Workbook(orphan: true));
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(4, preview.SupplementRows.Count);
        var orphan = preview.SupplementRows.Single(row => row.RowNumber == 6);
        Assert.False(orphan.IsValid); Assert.NotEmpty(orphan.Errors);
        var repository = Repository();
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, preview));
        Assert.Empty(repository.GetEmployees()); Assert.Empty(repository.GetSupplements()); Assert.Empty(repository.GetActivities());
    }

    [Fact]
    public void UnmatchedDetailIdentityCannotPartiallyImportEmployeeRows()
    {
        var preview = EmployeeImportService.Read(Workbook(unknownEmployee: true));
        var repository = Repository();
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, preview));
        Assert.Empty(repository.GetEmployees()); Assert.Empty(repository.GetSupplements());
    }

    [Fact]
    public void UnmergedNameBlockCanBeCorroboratedByAUniquePracticeLicenseInTheSameBlock()
    {
        var preview = EmployeeImportService.Read(Workbook(mergeNames: false, corroboratingLicense: "CCHN-MAU-001"));
        Assert.All(preview.SupplementRows, row => Assert.True(row.IsValid, string.Join("; ", row.Errors)));
        var continued = preview.SupplementRows.Single(row => row.RowNumber == 4);
        Assert.Equal(FirstName, continued.EmployeeName);
        Assert.Contains(continued.Warnings, warning => warning.Contains("đối chiếu", StringComparison.OrdinalIgnoreCase));
        var repository = Repository();
        var result = EmployeeImportService.Apply(repository, preview);
        Assert.Equal(2, result.Added); Assert.Equal(3, result.SupplementsAdded);
        var employee = repository.GetEmployees().Single(row => row.Name == FirstName);
        Assert.Equal(2, repository.GetSupplements().Count(row => row.EmployeeId == employee.Id));
        Assert.Empty(repository.GetActivities());
    }

    [Fact]
    public void AConflictingPracticeLicenseDoesNotCorroborateAnUnmergedNameBlock()
    {
        var preview = EmployeeImportService.Read(Workbook(mergeNames: false, corroboratingLicense: "CCHN-CONFLICT"));
        var orphan = preview.SupplementRows.Single(row => row.RowNumber == 4);
        Assert.False(orphan.IsValid); Assert.NotEmpty(orphan.Errors);
        var repository = Repository();
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, preview));
        Assert.Empty(repository.GetEmployees()); Assert.Empty(repository.GetSupplements());
    }

    [Fact]
    public void ReimportingACorrectedIssueDateKeepsRawLinkAndNotesStable()
    {
        var path = Workbook(firstIssuedDate: new DateTime(2025, 7, 24));
        var repository = Repository();
        EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        var employee = repository.GetEmployees().Single(row => row.Name == FirstName);
        var before = repository.GetSupplements().OrderBy(row => row.Id).ToArray();
        var corrected = before.Single(row => row.EmployeeId == employee.Id && row.Title == "Khóa học minh họa");
        Assert.Equal(new DateTime(2025, 7, 24), corrected.IssuedDate);
        Assert.False(string.IsNullOrWhiteSpace(corrected.LinkedSourceReference));
        Assert.Contains("23/07/2025", corrected.Notes);
        var repeated = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        Assert.Equal(0, repeated.SupplementsAdded); Assert.Equal(0, repeated.SupplementsUpdated); Assert.Equal(3, repeated.SupplementsSkipped);
        Assert.Equal(before, repository.GetSupplements().OrderBy(row => row.Id));
        Assert.Equal(2, EmployeeTrainingHistory.Build(employee, [], repository.GetSupplements()).Count);
        Assert.Equal("Khóa học minh họa (23/07/2025)\nChứng chỉ chưa rõ", repository.GetEmployees().Single(row => row.Id == employee.Id).ProfessionalCertificates);
    }

    private TrainingRepository Repository()
    {
        var repository = new TrainingRepository(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db"));
        repository.Initialize(); return repository;
    }

    private string Workbook(bool date1904 = false, bool orphan = false, bool unknownEmployee = false, decimal firstHours = 1.5m,
        bool mergeNames = true, string? corroboratingLicense = null, DateTime? firstIssuedDate = null)
    {
        var path = Path.Combine(directory, "supplements.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var part = document.AddWorkbookPart();
        part.Workbook = new Workbook(new WorkbookProperties { Date1904 = date1904 });
        var styles = part.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font()) { Count = 1 },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
            new Borders(new Border()) { Count = 1 }, new CellStyleFormats(new CellFormat()) { Count = 1 },
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }) { Count = 2 });
        var employees = new[]
        {
            Row(1, Text("A1", "Mã NV"), Text("B1", "Họ và tên"), Text("C1", "Khoa / phòng"), Text("D1", "Chứng chỉ chuyên môn"), Text("E1", "Số CCHN")),
            Row(2, Text("A2", "NV001"), Text("B2", FirstName), Text("C2", "Khoa Minh Họa"), Text("D2", "Khóa học minh họa (23/07/2025)\nChứng chỉ chưa rõ"), Text("E2", "CCHN-MAU-001")),
            Row(3, Text("A3", "NV002"), Text("B3", SecondName), Text("C3", "Khoa Minh Họa"), Text("E3", "CCHN-MAU-002"))
        };
        double Serial(DateTime date) => date1904 ? (date - new DateTime(1904, 1, 1)).TotalDays : date.ToOADate();
        var details = new List<Row>
        {
            Row(1, Text("B1", "Họ và tên"), Text("C1", "Tên chứng chỉ"), Text("D1", "Thời gian học"), Text("E1", "Số tiết"), Text("F1", "Ngày cấp"), Text("G1", "Nơi cấp")),
            Row(3, Text("B3", FirstName), Text("C3", "Khóa học minh họa"), Text("D3", "15/07/2025 - 20/07/2025"), Number("E3", (double)firstHours), Number("F3", Serial(firstIssuedDate ?? new DateTime(2025, 7, 23)), 1), Text("G3", "Bệnh viện Minh Họa")),
            Row(4, Text("C4", "Chứng chỉ chưa rõ"), Text("D4", "2020-2021"), Text("F4", "12/01/2022")),
            Row(5, Text("B5", unknownEmployee ? "Người không có trong danh sách" : SecondName), Text("C5", "Khóa cập nhật nửa ngày"), Text("D5", "10/02/2026"), Text("E5", "1,5"), Text("F5", "15/02/2026"), Text("G5", "Trung tâm Minh Họa"))
        };
        if (corroboratingLicense is not null)
        {
            details[0].Append(Text("H1", "Số CCHN"));
            details.Single(row => row.RowIndex?.Value == 4).Append(Text("H4", corroboratingLicense));
        }
        if (orphan) details.Add(Row(6, Text("C6", "Dòng không có nhân viên"), Text("E6", "4")));
        var sheets = part.Workbook.AppendChild(new Sheets());
        AddSheet("Nhân viên", employees, []); AddSheet("Chứng chỉ chi tiết", details, mergeNames ? ["B3:B4"] : []);
        part.Workbook.Save(); styles.Stylesheet.Save();
        return path;

        void AddSheet(string name, IEnumerable<Row> rows, IEnumerable<string> merges)
        {
            var sheet = part.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new Worksheet(new SheetData(rows));
            var merged = merges.ToArray();
            if (merged.Length > 0) sheet.Worksheet.Append(new MergeCells(merged.Select(reference => new MergeCell { Reference = reference })));
            sheets.Append(new Sheet { Id = part.GetIdOfPart(sheet), SheetId = (uint)sheets.ChildElements.Count + 1, Name = name });
            sheet.Worksheet.Save();
        }
    }

    private static Row Row(uint index, params Cell[] cells) => new(cells) { RowIndex = index };
    private static Cell Text(string reference, string value) => new() { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }) };
    private static Cell Number(string reference, double value, uint style = 0) => new() { CellReference = reference, StyleIndex = style, CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)) };

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
