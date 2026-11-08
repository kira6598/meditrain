using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace QuanLyDaoTao.Core;

/// <summary>Exports genuine Office Open XML workbooks, without an installed copy of Excel.</summary>
public static class ExportService
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void ExportAnnual(string path, IEnumerable<TrainingReportRow> rows, int year) =>
        WriteWorkbook(path, $"Báo cáo {year}",
            ["Mã NV", "Họ và tên", "Khoa / Phòng", "Trình độ", "SHKHKT (tiết)", "CME (tiết)", "Tổng tiết", "Mục tiêu", "Còn thiếu", "Kết quả"],
            rows.Select(row => new object?[] { row.Code, row.Name, row.Department, row.Degree, row.ShkhktHours, row.CmeHours, row.TotalHours, row.TargetHours, row.MissingHours, row.Status }));

    public static void ExportBiennial(string path, IEnumerable<TrainingReportRow> rows, int startYear) =>
        WriteWorkbook(path, $"Báo cáo {startYear}-{startYear + 1}",
            ["Mã NV", "Họ và tên", "Khoa / Phòng", "Trình độ", $"Năm {startYear} (tiết)", $"Năm {startYear + 1} (tiết)", "Tổng tiết", "Mục tiêu", "Còn thiếu", "Kết quả"],
            rows.Select(row => new object?[] { row.Code, row.Name, row.Department, row.Degree, row.FirstYearHours, row.SecondYearHours, row.TotalHours, row.TargetHours, row.MissingHours, row.Status }));

    public static void ExportDepartments(string path, IEnumerable<DepartmentReportRow> rows, int startYear, int annualYear) =>
        WriteWorkbook(path, "Thống kê khoa phòng",
            ["Khoa / Phòng", "Tổng nhân viên", $"Đạt 48T ({startYear}-{startYear + 1})", "Tỷ lệ đạt 48T (%)", $"Đạt 12T ({annualYear})", "Tỷ lệ đạt 12T (%)"],
            rows.Select(row => new object?[] { row.Department, row.EmployeeCount, row.BiennialPassed, row.BiennialRate, row.AnnualPassed, row.AnnualRate }));

    public static void ExportEmployees(string path, IEnumerable<Employee> employees) =>
        WriteWorkbook(path, "Nhân viên",
            ["Mã NV", "Họ và tên", "Khoa / Phòng", "Chức danh / Chức vụ", "Trình độ", "Ngày vào làm", "Mã số BHXH", "Ngày sinh", "Giấy phép hành nghề số", "Ngày cấp giấy phép hành nghề", "Các chứng chỉ chuyên môn", "Hội thảo/ Tập huấn/ hội nghị", "SHKHKT"],
            employees.Select(employee => new object?[] { employee.Code, employee.Name, employee.Department, employee.Title, employee.Degree, employee.StartDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "", employee.SocialInsuranceNumber, employee.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "", employee.PracticeLicenseNumber, employee.PracticeLicenseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "", employee.ProfessionalCertificates, employee.ConferenceHistory, employee.ScientificActivityHistory }));

    public static void ExportActivities(string path, IEnumerable<TrainingActivity> activities, IEnumerable<Employee> employees)
    {
        var people = employees.ToDictionary(employee => employee.Id);
        WriteWorkbook(path, "Hồ sơ đào tạo",
            ["Mã NV", "Họ và tên", "Khoa / Phòng", "Khóa học / Bài sinh hoạt", "Loại", "Số tiết", "Đơn vị tổ chức", "Ngày bắt đầu", "Ngày hoàn thành", "Số chứng chỉ", "Ghi chú"],
            activities.Select(activity =>
            {
                people.TryGetValue(activity.EmployeeId, out var employee);
                return new object?[] { employee?.Code ?? "", employee?.Name ?? "", employee?.Department ?? "", activity.Title, activity.Type, activity.Hours, activity.Provider, activity.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), activity.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), activity.CertificateNumber, activity.Notes };
            }));
    }

    private static void WriteWorkbook(string path, string sheetName, string[] headers, IEnumerable<object?[]> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var data = source.ToList();
            if (data.Count > 1_048_575)
                throw new InvalidOperationException("Báo cáo vượt quá giới hạn 1.048.576 dòng của Excel.");
            if (data.SelectMany(row => row).OfType<string>().Any(value => value.Length > 32_767))
                throw new InvalidOperationException("Có nội dung vượt quá giới hạn 32.767 ký tự trong một ô Excel. Vui lòng rút ngắn nội dung trước khi xuất.");
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                WriteXml(archive, "[Content_Types].xml", ContentTypes());
                WriteXml(archive, "_rels/.rels", new XElement(PackageRelationships + "Relationships",
                    new XElement(PackageRelationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", Relationships.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
                WriteXml(archive, "xl/workbook.xml", new XElement(Spreadsheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", Relationships),
                    new XElement(Spreadsheet + "sheets", new XElement(Spreadsheet + "sheet", new XAttribute("name", sheetName), new XAttribute("sheetId", 1), new XAttribute(Relationships + "id", "rId1")))));
                WriteXml(archive, "xl/_rels/workbook.xml.rels", new XElement(PackageRelationships + "Relationships",
                    new XElement(PackageRelationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", Relationships.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
                    new XElement(PackageRelationships + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", Relationships.NamespaceName + "/styles"), new XAttribute("Target", "styles.xml"))));
                WriteXml(archive, "xl/styles.xml", Styles());
                WriteXml(archive, "xl/worksheets/sheet1.xml", Worksheet(headers, data));
            }
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static XElement ContentTypes()
    {
        XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XElement(contentTypes + "Types",
            new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
    }

    private static XElement Worksheet(string[] headers, List<object?[]> data)
    {
        var lastCell = $"{ColumnName(headers.Length - 1)}{data.Count + 1}";
        var rows = new XElement(Spreadsheet + "sheetData");
        rows.Add(new XElement(Spreadsheet + "row", new XAttribute("r", 1), new XAttribute("ht", 32), new XAttribute("customHeight", 1),
            headers.Select((value, column) => Cell($"{ColumnName(column)}1", value, true))));
        for (var index = 0; index < data.Count; index++)
        {
            var number = index + 2;
            rows.Add(new XElement(Spreadsheet + "row", new XAttribute("r", number),
                data[index].Select((value, column) => Cell($"{ColumnName(column)}{number}", value, false))));
        }
        return new XElement(Spreadsheet + "worksheet",
            new XElement(Spreadsheet + "dimension", new XAttribute("ref", $"A1:{lastCell}")),
            new XElement(Spreadsheet + "sheetViews", new XElement(Spreadsheet + "sheetView", new XAttribute("workbookViewId", 0),
                new XElement(Spreadsheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(Spreadsheet + "sheetFormatPr", new XAttribute("defaultRowHeight", 22)),
            new XElement(Spreadsheet + "cols", headers.Select((header, column) =>
            {
                var bodyWidth = data.Take(200).Select(row => column < row.Length ? Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Length ?? 0 : 0).DefaultIfEmpty().Max();
                return new XElement(Spreadsheet + "col", new XAttribute("min", column + 1), new XAttribute("max", column + 1), new XAttribute("width", Math.Clamp(Math.Max(header.Length + 2, bodyWidth + 2), 13, 50)), new XAttribute("customWidth", 1));
            })),
            rows,
            new XElement(Spreadsheet + "autoFilter", new XAttribute("ref", $"A1:{lastCell}")),
            new XElement(Spreadsheet + "pageMargins", new XAttribute("left", 0.3), new XAttribute("right", 0.3), new XAttribute("top", 0.5), new XAttribute("bottom", 0.5), new XAttribute("header", 0.2), new XAttribute("footer", 0.2)),
            new XElement(Spreadsheet + "pageSetup", new XAttribute("paperSize", 9), new XAttribute("orientation", "landscape")));
    }

    private static XElement Cell(string reference, object? value, bool header)
    {
        var cell = new XElement(Spreadsheet + "c", new XAttribute("r", reference));
        if (value is byte or short or int or long or float or double or decimal)
        {
            cell.Add(new XAttribute("s", 2), new XElement(Spreadsheet + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
        }
        else
        {
            cell.Add(new XAttribute("s", header ? 1 : 0), new XAttribute("t", "inlineStr"),
                new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value?.ToString() ?? "")));
        }
        return cell;
    }

    private static XElement Styles() => XElement.Parse("""
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="1"><numFmt numFmtId="164" formatCode="0.##" /></numFmts>
          <fonts count="2">
            <font><sz val="11"/><color rgb="FF1E293B"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0F766E"/><bgColor indexed="64"/></patternFill></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"><alignment vertical="center"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """);

    private static string ColumnName(int column)
    {
        var result = "";
        for (var value = column + 1; value > 0; value = (value - 1) / 26)
            result = (char)('A' + (value - 1) % 26) + result;
        return result;
    }

    private static void WriteXml(ZipArchive archive, string name, XElement root)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
    }
}
