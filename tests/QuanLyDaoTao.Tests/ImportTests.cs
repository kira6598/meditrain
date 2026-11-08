using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class ImportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.ImportTests", Guid.NewGuid().ToString("N"));
    private static readonly string[] Headers = ["Mã NV", "Họ và tên", "Khoa / Phòng", "BHXH", "Ngày sinh", "Số CCHN", "Ngày cấp CCHN", "Trình độ", "Chứng chỉ chuyên môn", "Lịch sử hội nghị", "Sinh hoạt khoa học", "Ngày vào làm", "Chức danh"];

    public ImportTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void HeadersSplitAcrossMergedRowsMapAllActualWorkbookColumns()
    {
        var top = new Row { RowIndex = 1 };
        top.Append(Text("C1", "Mã số BHXH"), Text("D1", "Ngày sinh"), Text("E1", "Chức danh công việc"));
        var lower = new Row { RowIndex = 2 };
        lower.Append(Text("A2", "STT"), Text("B2", "Họ và tên"), Text("F2", "Giấy phép hành nghề số"), Text("G2", "Ngày cấp giấy phép hành nghề"), Text("H2", "Các chứng chỉ chuyên môn"), Text("I2", "Hội thảo/ Tập huấn/ hội nghị"), Text("J2", "SHKHKT"));
        var data = new Row { RowIndex = 4 };
        data.Append(Text("A4", "1"), Text("B4", "Nhân viên Mẫu"), Text("C4", "0012345678"), Text("D4", "02/03/1990"), Text("E4", "Điều dưỡng mẫu"), Text("F4", "CCHN-MẪU"), Text("G4", "05/06/2020"), Text("H4", "Chứng chỉ mẫu\nDòng hai"), Text("I4", "Hội nghị mẫu"), Text("J4", "Sinh hoạt mẫu"));
        var path = Workbook("split-headers.xlsx", [top, lower, new Row { RowIndex = 3 }, data], merges: ["A2:A3", "B2:B3", "C1:C3", "D1:D3", "E1:E3", "F2:F3", "G2:G3", "H2:H3", "I2:I3", "J2:J3"]);
        var row = Assert.Single(EmployeeImportService.Read(path).Rows);
        Assert.True(row.IsValid, string.Join("; ", row.Errors));
        Assert.Equal("Nhân viên Mẫu", row.Employee.Name);
        Assert.Equal("0012345678", row.Employee.SocialInsuranceNumber);
        Assert.Equal(new DateTime(1990, 3, 2), row.Employee.DateOfBirth);
        Assert.Equal("Điều dưỡng mẫu", row.Employee.Title);
        Assert.Equal("CCHN-MẪU", row.Employee.PracticeLicenseNumber);
        Assert.Equal(new DateTime(2020, 6, 5), row.Employee.PracticeLicenseDate);
        Assert.Equal("Chứng chỉ mẫu\nDòng hai", row.Employee.ProfessionalCertificates);
        Assert.Equal("Hội nghị mẫu", row.Employee.ConferenceHistory);
        Assert.Equal("Sinh hoạt mẫu", row.Employee.ScientificActivityHistory);
    }

    [Fact]
    public void MergedThreeRowHeadersRichSharedStringsAndMultilineNotesArePreserved()
    {
        var data = new Row { RowIndex = 4 };
        data.Append(Shared("B4", 0), Text("D4", "0012345678"), Text("I4", "Chứng chỉ mẫu 1\nChứng chỉ mẫu 2"), Text("J4", "Hội nghị mẫu A\nHội nghị mẫu B"), Text("K4", "Sinh hoạt mẫu 2025\nSinh hoạt mẫu 2026"));
        var path = Workbook("merged.xlsx", [
            new Row(Text("A1", "DANH SÁCH NHÂN VIÊN MẪU")) { RowIndex = 1 },
            new Row(Text("I2", "Đào tạo và sinh hoạt khoa học")) { RowIndex = 2 },
            HeaderRow(3), data],
            [new SharedStringItem(new Run(new Text("Nhân viên ")), new Run(new Text("Mẫu Một")))], ["A1:M1", "I2:K2"]);
        var preview = EmployeeImportService.Read(path, "Khoa Mẫu");
        var row = Assert.Single(preview.Rows);
        Assert.True(row.IsValid, string.Join("; ", row.Errors));
        Assert.Equal(4, row.RowNumber);
        Assert.Equal("Nhân viên Mẫu Một", row.Employee.Name);
        Assert.Equal("0012345678", row.Employee.SocialInsuranceNumber);
        Assert.Equal("Khoa Mẫu", row.Employee.Department);
        Assert.Equal("Chứng chỉ mẫu 1\nChứng chỉ mẫu 2", row.Employee.ProfessionalCertificates);
        Assert.Equal("Hội nghị mẫu A\nHội nghị mẫu B", row.Employee.ConferenceHistory);
        Assert.Equal("Sinh hoạt mẫu 2025\nSinh hoạt mẫu 2026", row.Employee.ScientificActivityHistory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericExcelDatesHonorWorkbookEpoch(bool date1904)
    {
        var birth = new DateTime(1994, 2, 28);
        var license = new DateTime(2021, 7, 15);
        var start = new DateTime(2022, 1, 2);
        double Serial(DateTime value) => date1904 ? (value - new DateTime(1904, 1, 1)).TotalDays : value.ToOADate();
        var row = new Row { RowIndex = 2 };
        row.Append(Text("B2", "Nhân viên Mẫu"), Number("E2", Serial(birth), 1), Number("G2", Serial(license), 1), Number("L2", Serial(start), 1));
        var path = Workbook("dates.xlsx", [HeaderRow(1), row], date1904: date1904);
        var employee = Assert.Single(EmployeeImportService.Read(path).Rows).Employee;
        Assert.Equal(birth, employee.DateOfBirth);
        Assert.Equal(license, employee.PracticeLicenseDate);
        Assert.Equal(start, employee.StartDate);
    }

    [Fact]
    public void VietnameseTextDatesAndBlankOptionalFieldsAreAccepted()
    {
        var row = new Row { RowIndex = 2 };
        row.Append(Text("B2", "Nhân viên Mẫu"), Text("E2", "29/02/2000"), Text("G2", "01/12/2020"));
        var path = Workbook("text-dates.xlsx", [HeaderRow(1), row, new Row { RowIndex = 3 }]);
        var imported = Assert.Single(EmployeeImportService.Read(path).Rows);
        Assert.True(imported.IsValid, string.Join("; ", imported.Errors));
        Assert.Equal(new DateTime(2000, 2, 29), imported.Employee.DateOfBirth);
        Assert.Equal(new DateTime(2020, 12, 1), imported.Employee.PracticeLicenseDate);
        Assert.Null(imported.Employee.StartDate);
        Assert.Equal("", imported.Employee.PracticeLicenseNumber);
    }

    [Fact]
    public void NumericInsuranceWithZeroPaddedFormatRetainsLeadingZeroes()
    {
        var row = new Row { RowIndex = 2 };
        row.Append(Text("B2", "Nhân viên Mẫu"), Number("D2", 12345678, 2));
        var path = Workbook("leading-zeroes.xlsx", [HeaderRow(1), row]);
        Assert.Equal("0012345678", Assert.Single(EmployeeImportService.Read(path).Rows).Employee.SocialInsuranceNumber);
    }

    [Fact]
    public void InvalidDateRemainsVisibleAsRowErrorAndApplyWritesNothing()
    {
        var row = new Row { RowIndex = 2 };
        row.Append(Text("B2", "Nhân viên Mẫu"), Text("E2", "31/02/2020"));
        var path = Workbook("invalid-date.xlsx", [HeaderRow(1), row]);
        var preview = EmployeeImportService.Read(path);
        Assert.NotEmpty(Assert.Single(preview.Rows).Errors);
        var repository = Repository();
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, preview));
        Assert.Empty(repository.GetEmployees());
    }

    [Fact]
    public void CachedFormulaUsesCachedTextAndUncachedFormulaReportsError()
    {
        var cached = new Row { RowIndex = 2 };
        cached.Append(Text("B2", "Nhân viên Mẫu Một"), Formula("I2", "Chứng chỉ mẫu"));
        var uncached = new Row { RowIndex = 3 };
        uncached.Append(Text("B3", "Nhân viên Mẫu Hai"), Formula("I3", null));
        var path = Workbook("formula.xlsx", [HeaderRow(1), cached, uncached]);
        var rows = EmployeeImportService.Read(path).Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Chứng chỉ mẫu", rows[0].Employee.ProfessionalCertificates);
        Assert.True(rows[0].IsValid, string.Join("; ", rows[0].Errors));
        Assert.False(rows[1].IsValid);
    }

    [Fact]
    public void UnrecognizedHeadersAndMalformedFilesFailClearly()
    {
        var unknown = Workbook("unknown.xlsx", [new Row(Text("A1", "Không phải danh sách nhân viên")) { RowIndex = 1 }, new Row(Text("A2", "Dữ liệu mẫu")) { RowIndex = 2 }]);
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Read(unknown));
        var malformed = Path.Combine(directory, "malformed.xlsx");
        File.WriteAllText(malformed, "This is not an Excel workbook.");
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Read(malformed));
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Read(Path.Combine(directory, "missing.xlsx")));
    }

    [Fact]
    public void ImportWithoutCodesGeneratesUniqueCodesAndRepeatedReadIsIdempotent()
    {
        var path = SimpleWorkbook("Nhân viên Mẫu Một", "Nhân viên Mẫu Hai");
        var repository = Repository();
        var first = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        Assert.Equal(2, first.Added);
        Assert.Equal(0, first.Updated);
        var people = repository.GetEmployees();
        Assert.All(people, employee => Assert.False(string.IsNullOrWhiteSpace(employee.Code)));
        Assert.Equal(2, people.Select(employee => employee.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(people, employee => Assert.Equal("Chưa phân khoa", employee.Department));
        var second = EmployeeImportService.Apply(repository, EmployeeImportService.Read(path));
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(people.Select(employee => employee.Id), repository.GetEmployees().Select(employee => employee.Id));
    }

    [Fact]
    public void DuplicateSourceNamesWithoutIdentityFieldsCannotBeMergedOrImported()
    {
        var path = SimpleWorkbook("Nhân viên Trùng Tên", "Nhân viên Trùng Tên");
        var preview = EmployeeImportService.Read(path);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Contains(preview.Rows, row => !row.IsValid);
        var repository = Repository();
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, preview));
        Assert.Empty(repository.GetEmployees());
    }

    [Fact]
    public void UpdatingNonEmptyFieldsPreservesEmployeeIdentityExistingDataAndTrainingLinks()
    {
        var repository = Repository();
        var employee = new Employee { Code = "NV001", Name = "Nhân viên Mẫu", Department = "Khoa Mẫu", SocialInsuranceNumber = "0012345678", Degree = "Cử nhân", ProfessionalCertificates = "Chứng chỉ cũ" };
        repository.SaveEmployee(employee);
        var activity = new TrainingActivity { EmployeeId = employee.Id, Title = "Khóa học mẫu", Provider = "Đơn vị mẫu", Hours = 12 };
        repository.SaveActivity(activity);
        var incoming = employee with { Id = Guid.NewGuid().ToString("N"), Code = "", Degree = "", ProfessionalCertificates = "Chứng chỉ mới\nGiữ nguyên ghi chú" };
        var result = EmployeeImportService.Apply(repository, Preview(incoming), ExistingEmployeeImportMode.UpdateNonEmpty);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);
        var saved = Assert.Single(repository.GetEmployees());
        Assert.Equal(employee.Id, saved.Id);
        Assert.Equal(employee.Code, saved.Code);
        Assert.Equal(employee.Degree, saved.Degree);
        Assert.Equal(incoming.ProfessionalCertificates, saved.ProfessionalCertificates);
        Assert.Equal(activity, Assert.Single(repository.GetActivities()));
    }

    [Fact]
    public void MissingDepartmentColumnDoesNotReplaceExistingDepartmentWithDefault()
    {
        var repository = Repository();
        var existing = new Employee { Code = "NV001", Name = "Nhân viên Mẫu", Department = "Khoa Đang Công Tác", SocialInsuranceNumber = "0012345678" };
        repository.SaveEmployee(existing);
        var headers = new Row(Text("A1", "Họ và tên"), Text("B1", "BHXH"), Text("C1", "Chứng chỉ chuyên môn")) { RowIndex = 1 };
        var data = new Row(Text("A2", existing.Name), Text("B2", existing.SocialInsuranceNumber), Text("C2", "Chứng chỉ mới")) { RowIndex = 2 };
        var preview = EmployeeImportService.Read(Workbook("no-department.xlsx", [headers, data]), "Chưa phân khoa");
        EmployeeImportService.Apply(repository, preview, ExistingEmployeeImportMode.UpdateNonEmpty);
        var saved = Assert.Single(repository.GetEmployees());
        Assert.Equal(existing.Department, saved.Department);
        Assert.Equal("Chứng chỉ mới", saved.ProfessionalCertificates);
    }

    [Fact]
    public void BlankDepartmentAndTitleCellsDoNotOverwriteExistingValues()
    {
        var repository = Repository();
        var existing = new Employee { Code = "NV001", Name = "Nhân viên Mẫu", Department = "Khoa Đang Công Tác", Title = "Chức danh đang giữ", SocialInsuranceNumber = "0012345678" };
        repository.SaveEmployee(existing);
        var headers = new Row(Text("A1", "Họ và tên"), Text("B1", "BHXH"), Text("C1", "Khoa / Phòng"), Text("D1", "Chức danh"), Text("E1", "Chứng chỉ chuyên môn")) { RowIndex = 1 };
        var data = new Row(Text("A2", existing.Name), Text("B2", existing.SocialInsuranceNumber), Text("C2", ""), Text("D2", ""), Text("E2", "Chứng chỉ mới")) { RowIndex = 2 };
        var preview = EmployeeImportService.Read(Workbook("blank-department.xlsx", [headers, data]), "Chưa phân khoa");
        EmployeeImportService.Apply(repository, preview, ExistingEmployeeImportMode.UpdateNonEmpty);
        var saved = Assert.Single(repository.GetEmployees());
        Assert.Equal(existing.Department, saved.Department);
        Assert.Equal(existing.Title, saved.Title);
        Assert.Equal("Chứng chỉ mới", saved.ProfessionalCertificates);
    }

    [Fact]
    public void ExportThenImportPreservesAllEmployeeProfileFields()
    {
        var employee = new Employee { Code = "NV-MẪU", Name = "Nhân viên Mẫu", Department = "Khoa Mẫu", Title = "Điều dưỡng mẫu", Degree = "Cử nhân mẫu", StartDate = new DateTime(2022, 1, 2), SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1990, 3, 4), PracticeLicenseNumber = "CCHN-MẪU", PracticeLicenseDate = new DateTime(2020, 5, 6), ProfessionalCertificates = "Chứng chỉ mẫu\nDòng hai", ConferenceHistory = "Hội nghị mẫu\nTập huấn mẫu", ScientificActivityHistory = "Sinh hoạt mẫu\nNội dung mẫu" };
        var path = Path.Combine(directory, "roundtrip.xlsx");
        ExportService.ExportEmployees(path, [employee]);
        var row = Assert.Single(EmployeeImportService.Read(path).Rows);
        Assert.True(row.IsValid, string.Join("; ", row.Errors));
        Assert.Equal(employee with { Id = row.Employee.Id }, row.Employee);
    }

    [Fact]
    public void ConflictingInsuranceIdentityCannotMergeDifferentPeople()
    {
        var repository = Repository();
        var original = new Employee { Code = "NV001", Name = "Nhân viên Mẫu Một", Department = "Khoa Mẫu", SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1990, 1, 1) };
        repository.SaveEmployee(original);
        var conflicting = original with { Id = Guid.NewGuid().ToString("N"), Code = "", Name = "Nhân viên Mẫu Hai", DateOfBirth = new DateTime(1995, 2, 2) };
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, Preview(conflicting), ExistingEmployeeImportMode.UpdateNonEmpty));
        Assert.Equal(original, Assert.Single(repository.GetEmployees()));
    }

    [Fact]
    public void AmbiguousNameWithoutInsuranceCannotMergeDistinctEmployees()
    {
        var repository = Repository();
        var first = new Employee { Code = "NV001", Name = "Nhân viên Trùng Tên", Department = "Khoa Mẫu", DateOfBirth = new DateTime(1990, 1, 1) };
        var second = first with { Id = Guid.NewGuid().ToString("N"), Code = "NV002", DateOfBirth = new DateTime(1995, 2, 2) };
        repository.SaveEmployee(first); repository.SaveEmployee(second);
        var incoming = first with { Id = Guid.NewGuid().ToString("N"), Code = "", DateOfBirth = null };
        Assert.ThrowsAny<Exception>(() => EmployeeImportService.Apply(repository, Preview(incoming), ExistingEmployeeImportMode.UpdateNonEmpty));
        Assert.Equal(2, repository.GetEmployees().Count);
    }

    [Fact]
    public void RepositoryBulkImportRollsBackWholeBatchOnConflictingCode()
    {
        var repository = Repository();
        var original = new Employee { Code = "NV001", Name = "Nhân viên Mẫu Gốc", Department = "Khoa Mẫu" };
        repository.SaveEmployee(original);
        var valid = new Employee { Code = "NV002", Name = "Nhân viên Mẫu Mới", Department = "Khoa Mẫu" };
        var conflict = new Employee { Code = "NV001", Name = "Nhân viên Mẫu Xung Đột", Department = "Khoa Mẫu" };
        Assert.ThrowsAny<Exception>(() => repository.ImportEmployees([valid, conflict]));
        Assert.Equal(original, Assert.Single(repository.GetEmployees()));
    }

    [Fact]
    public void VersionOneMigrationPreservesExistingEmployeeAndTraining()
    {
        var path = Path.Combine(directory, "legacy.db");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Employees (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL COLLATE NOCASE UNIQUE, Name TEXT NOT NULL, Department TEXT NOT NULL, Title TEXT NOT NULL DEFAULT '', Degree TEXT NOT NULL DEFAULT '', StartDate TEXT NULL);
                CREATE TABLE TrainingActivities (Id TEXT NOT NULL PRIMARY KEY, EmployeeId TEXT NOT NULL REFERENCES Employees(Id) ON DELETE CASCADE, Title TEXT NOT NULL, Type TEXT NOT NULL, Provider TEXT NOT NULL, StartDate TEXT NOT NULL, EndDate TEXT NOT NULL, Hours TEXT NOT NULL, CertificateNumber TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '');
                INSERT INTO Employees (Id,Code,Name,Department) VALUES ('legacy-person','NV001','Nhân viên Mẫu Cũ','Khoa Mẫu');
                INSERT INTO TrainingActivities (Id,EmployeeId,Title,Type,Provider,StartDate,EndDate,Hours) VALUES ('legacy-course','legacy-person','Khóa học mẫu','CME','Đơn vị mẫu','2026-01-01','2026-01-02','12.5');
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        var repository = new TrainingRepository(path);
        repository.Initialize();
        var employee = Assert.Single(repository.GetEmployees());
        Assert.Equal("legacy-person", employee.Id);
        Assert.Equal("", employee.SocialInsuranceNumber);
        Assert.Null(employee.DateOfBirth);
        Assert.Equal("legacy-person", Assert.Single(repository.GetActivities()).EmployeeId);
        Assert.Equal(12.5m, Assert.Single(repository.GetActivities()).Hours);
        Assert.Equal("", Assert.Single(repository.GetActivities()).SourceReference);
        repository.SaveEmployee(employee with { SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1990, 1, 1), PracticeLicenseNumber = "CCHN-MẪU", PracticeLicenseDate = new DateTime(2020, 2, 2), ProfessionalCertificates = "Chứng chỉ mẫu\nDòng hai", ConferenceHistory = "Hội nghị mẫu", ScientificActivityHistory = "Sinh hoạt mẫu" });
        var reopened = new TrainingRepository(path);
        reopened.Initialize();
        var migrated = Assert.Single(reopened.GetEmployees());
        Assert.Equal("0012345678", migrated.SocialInsuranceNumber);
        Assert.Equal(new DateTime(1990, 1, 1), migrated.DateOfBirth);
        Assert.Equal("CCHN-MẪU", migrated.PracticeLicenseNumber);
        Assert.Equal(new DateTime(2020, 2, 2), migrated.PracticeLicenseDate);
        Assert.Equal("Chứng chỉ mẫu\nDòng hai", migrated.ProfessionalCertificates);
        Assert.Equal("Hội nghị mẫu", migrated.ConferenceHistory);
        Assert.Equal("Sinh hoạt mẫu", migrated.ScientificActivityHistory);
        Assert.Single(reopened.GetActivities());
    }

    private TrainingRepository Repository()
    {
        var repository = new TrainingRepository(Path.Combine(directory, $"{Guid.NewGuid():N}.db"));
        repository.Initialize(); return repository;
    }

    private string SimpleWorkbook(params string[] names) => Workbook($"simple-{Guid.NewGuid():N}.xlsx", [HeaderRow(1), .. names.Select((name, index) => new Row(Text($"B{index + 2}", name)) { RowIndex = (uint)(index + 2) })]);
    private static EmployeeImportPreview Preview(params Employee[] employees) => new() { Rows = employees.Select((employee, index) => new EmployeeImportRow { RowNumber = index + 2, SheetName = "Mẫu", Employee = employee, PresentFields = typeof(Employee).GetProperties().Where(property => property.Name is not ("Id" or "DisplayName")).Select(property => property.Name).ToArray() }).ToArray() };
    private static Row HeaderRow(uint index) => new(Headers.Select((header, column) => Text($"{(char)('A' + column)}{index}", header))) { RowIndex = index };
    private static Cell Text(string reference, string value) => new() { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }) };
    private static Cell Shared(string reference, int index) => new() { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture)) };
    private static Cell Number(string reference, double value, uint style) => new() { CellReference = reference, StyleIndex = style, CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)) };
    private static Cell Formula(string reference, string? cached) => new() { CellReference = reference, DataType = CellValues.String, CellFormula = new CellFormula("\"Chứng chỉ mẫu\""), CellValue = cached is null ? null : new CellValue(cached) };

    private string Workbook(string name, IEnumerable<Row> rows, IEnumerable<SharedStringItem>? shared = null, IEnumerable<string>? merges = null, bool date1904 = false)
    {
        var path = Path.Combine(directory, name);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook(new WorkbookProperties { Date1904 = date1904 });
        var style = workbook.AddNewPart<WorkbookStylesPart>();
        style.Stylesheet = new Stylesheet(
            new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "0000000000" }) { Count = 1 },
            new Fonts(new Font()) { Count = 1 },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
            new Borders(new Border()) { Count = 1 },
            new CellStyleFormats(new CellFormat()) { Count = 1 },
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }, new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }) { Count = 3 });
        var worksheet = workbook.AddNewPart<WorksheetPart>();
        worksheet.Worksheet = new Worksheet(new SheetData(rows));
        if (merges is not null) worksheet.Worksheet.Append(new MergeCells(merges.Select(reference => new MergeCell { Reference = reference })));
        var sheets = workbook.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "Mẫu" });
        if (shared is not null) workbook.AddNewPart<SharedStringTablePart>().SharedStringTable = new SharedStringTable(shared);
        workbook.Workbook.Save();
        worksheet.Worksheet.Save();
        style.Stylesheet.Save();
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
