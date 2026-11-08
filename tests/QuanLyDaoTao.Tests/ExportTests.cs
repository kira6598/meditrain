using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class ExportTests : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.Exports", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExcessiveCellTextIsRejectedAndExistingExportIsPreserved()
    {
        var path = Path.Combine(_directory, "preserved.xlsx");
        ExportService.ExportEmployees(path, [new Employee { Code = "NV001", Name = "Nguyễn An" }]);
        var original = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => ExportService.ExportEmployees(path, [new Employee { Name = new string('a', 32_768) }]));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedWorkbookPassesOpenXmlSchemaValidation(bool empty)
    {
        var path = Path.Combine(_directory, "validated.xlsx");
        var rows = empty ? Array.Empty<TrainingReportRow>() : [new TrainingReportRow { Employee = new Employee { Code = "NV001", Name = "Nguyễn Thị Ánh", Department = "Khoa Nội" }, TotalHours = 12.5m, CmeHours = 12.5m, TargetHours = 12m }];
        ExportService.ExportAnnual(path, rows, 2026);
        using var document = SpreadsheetDocument.Open(path, false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => $"{error.Part?.Uri}: {error.Description}")));
        Assert.Single(document.WorkbookPart!.WorksheetParts);
    }

    [Fact]
    public void AnnualWorkbookContainsOnlySuppliedRowsWithUnicodeAndNumericDecimals()
    {
        var path = Path.Combine(_directory, "annual.xlsx");
        var employee = new Employee { Code = "NV001", Name = "Nguyễn Thị Ánh <An> & Bình", Department = "Khoa Nội" };
        var rows = new[] { new TrainingReportRow { Employee = employee, ShkhktHours = 1.5m, CmeHours = 10m, TotalHours = 11.5m, TargetHours = 12m } };
        ExportService.ExportAnnual(path, rows, 2026);
        using var archive = ZipFile.OpenRead(path);
        var worksheet = Read(archive, "xl/worksheets/sheet1.xml");
        Assert.Equal(2, worksheet.Descendants(Ns + "row").Count());
        Assert.Equal(employee.Name, TextCell(worksheet, "B2"));
        var total = worksheet.Descendants(Ns + "c").Single(cell => (string?)cell.Attribute("r") == "G2");
        Assert.Null(total.Attribute("t"));
        Assert.Equal("11.5", total.Element(Ns + "v")?.Value);
        Assert.Equal("Chưa đạt", TextCell(worksheet, "J2"));
        Assert.Equal("A1:J2", (string?)worksheet.Root?.Element(Ns + "autoFilter")?.Attribute("ref"));
        Assert.Equal("frozen", (string?)worksheet.Descendants(Ns + "pane").Single().Attribute("state"));
        var workbook = Read(archive, "xl/workbook.xml");
        Assert.Equal("Báo cáo 2026", (string?)workbook.Descendants(Ns + "sheet").Single().Attribute("name"));
        var relationships = Read(archive, "xl/_rels/workbook.xml.rels");
        Assert.All(relationships.Root!.Elements(), relationship => Assert.StartsWith("http://schemas.openxmlformats.org/officeDocument/2006/relationships/", (string?)relationship.Attribute("Type")));
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
    }

    [Fact]
    public void UserTextBeginningWithEqualsIsStoredAsTextRatherThanFormula()
    {
        var path = Path.Combine(_directory, "people.xlsx");
        ExportService.ExportEmployees(path, [new Employee { Code = "=1+1", Name = "+SUM(1,1)", Department = "@Dữ liệu" }]);
        using var archive = ZipFile.OpenRead(path);
        var worksheet = Read(archive, "xl/worksheets/sheet1.xml");
        Assert.Empty(worksheet.Descendants(Ns + "f"));
        Assert.Equal("=1+1", TextCell(worksheet, "A2"));
        Assert.Equal("inlineStr", (string?)worksheet.Descendants(Ns + "c").Single(cell => (string?)cell.Attribute("r") == "A2").Attribute("t"));
    }

    [Fact]
    public void EmptyWorkbookStillHasValidHeadersAndCanBeOverwritten()
    {
        var path = Path.Combine(_directory, "empty.xlsx");
        ExportService.ExportBiennial(path, [], 2025);
        ExportService.ExportBiennial(path, [], 2026);
        using var archive = ZipFile.OpenRead(path);
        var worksheet = Read(archive, "xl/worksheets/sheet1.xml");
        Assert.Single(worksheet.Descendants(Ns + "row"));
        Assert.Equal("Năm 2026 (tiết)", TextCell(worksheet, "E1"));
        Assert.Equal("A1:J1", (string?)worksheet.Root?.Element(Ns + "dimension")?.Attribute("ref"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void DepartmentExportPreservesDifferentReportingPeriodsAndRates()
    {
        var path = Path.Combine(_directory, "departments.xlsx");
        ExportService.ExportDepartments(path, [new DepartmentReportRow { Department = "Khoa Nội", EmployeeCount = 3, AnnualPassed = 2, BiennialPassed = 1 }], 2024, 2026);
        using var archive = ZipFile.OpenRead(path);
        var worksheet = Read(archive, "xl/worksheets/sheet1.xml");
        Assert.Equal("Đạt 48T (2024-2025)", TextCell(worksheet, "C1"));
        Assert.Equal("Đạt 12T (2026)", TextCell(worksheet, "E1"));
        Assert.Equal("66.7", worksheet.Descendants(Ns + "c").Single(cell => (string?)cell.Attribute("r") == "F2").Element(Ns + "v")?.Value);
    }

    [Fact]
    public void ActivityExportJoinsEmployeesAndPreservesCertificateNotes()
    {
        var path = Path.Combine(_directory, "activities.xlsx");
        var employee = new Employee { Code = "NV009", Name = "Lê Hồng", Department = "Khoa Dược" };
        ExportService.ExportActivities(path, [new TrainingActivity { EmployeeId = employee.Id, Title = "Đào tạo CME", Hours = 0.5m, StartDate = new DateTime(2026, 10, 28), EndDate = new DateTime(2026, 10, 29), CertificateNumber = "CME-009", Notes = "Bản sao" }], [employee]);
        using var archive = ZipFile.OpenRead(path);
        var worksheet = Read(archive, "xl/worksheets/sheet1.xml");
        Assert.Equal("NV009", TextCell(worksheet, "A2"));
        Assert.Equal("28/10/2026", TextCell(worksheet, "H2"));
        Assert.Equal("CME-009", TextCell(worksheet, "J2"));
        Assert.Equal("Bản sao", TextCell(worksheet, "K2"));
    }

    private static XDocument Read(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private static string TextCell(XDocument worksheet, string reference) => worksheet.Descendants(Ns + "c").Single(cell => (string?)cell.Attribute("r") == reference).Descendants(Ns + "t").Single().Value;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
