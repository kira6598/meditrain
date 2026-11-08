using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace QuanLyDaoTao.Core;

internal sealed record XlsxCell(string Text, bool IsNumeric = false, string? Error = null, string? IdentifierText = null);
internal sealed record XlsxRow(int Number, IReadOnlyDictionary<int, XlsxCell> Cells);
internal sealed record XlsxMerge(int FirstRow, int LastRow, int FirstColumn, int LastColumn);
internal sealed record XlsxSheet(string Name, IReadOnlyList<XlsxRow> Rows, IReadOnlyList<XlsxMerge> Merges);
internal sealed record XlsxWorkbook(bool Date1904, IReadOnlyList<XlsxSheet> Sheets);

/// <summary>Reads package values only; never calculates formulas or opens external relationships.</summary>
internal static class XlsxReader
{
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private const long MaxExpandedBytes = 80 * 1024 * 1024;
    private const int MaxRows = 50000;
    private const int MaxCells = 500000;

    internal static XlsxWorkbook Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chỉ hỗ trợ tệp Excel .xlsx. Hãy lưu tệp theo định dạng .xlsx trước khi nhập.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy tệp Excel đã chọn.", path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("Tệp Excel vượt giới hạn 20 MB.");
        try
        {
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > MaxExpandedBytes)
                throw new InvalidDataException("Tệp Excel vượt giới hạn dung lượng giải nén hoặc số thành phần.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
                if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("Tệp Excel có thành phần trùng lặp không hợp lệ.");
            XDocument Load(string name)
            {
                if (!entries.TryGetValue(name, out var entry)) throw new InvalidDataException($"Tệp Excel thiếu thành phần {name}.");
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxExpandedBytes, IgnoreComments = true
                });
                return XDocument.Load(reader);
            }
            var workbook = Load("xl/workbook.xml");
            var relationships = Load("xl/_rels/workbook.xml.rels").Root?.Elements()
                .Where(e => e.Name.LocalName == "Relationship" && (string?)e.Attribute("TargetMode") != "External")
                .ToDictionary(e => (string?)e.Attribute("Id") ?? "", e => (string?)e.Attribute("Target") ?? "")
                ?? throw new InvalidDataException("Không đọc được danh sách trang tính.");
            var strings = entries.ContainsKey("xl/sharedStrings.xml")
                ? Load("xl/sharedStrings.xml").Root!.Elements().Where(e => e.Name.LocalName == "si").Select(RichText).ToArray()
                : [];
            if (strings.Length > 100000) throw new InvalidDataException("Tệp Excel có quá nhiều chuỗi văn bản.");
            var zeroPadding = new Dictionary<int, int>();
            if (entries.ContainsKey("xl/styles.xml"))
            {
                var styles = Load("xl/styles.xml");
                var formats = styles.Descendants().Where(e => e.Name.LocalName == "numFmt")
                    .Where(e => int.TryParse((string?)e.Attribute("numFmtId"), out _))
                    .ToDictionary(e => int.Parse(e.Attribute("numFmtId")!.Value, CultureInfo.InvariantCulture), e => (string?)e.Attribute("formatCode") ?? "");
                var cellFormats = styles.Descendants().FirstOrDefault(e => e.Name.LocalName == "cellXfs")?.Elements().ToArray() ?? [];
                for (var index = 0; index < cellFormats.Length; index++)
                    if (int.TryParse((string?)cellFormats[index].Attribute("numFmtId"), out var formatId)
                        && formats.TryGetValue(formatId, out var format) && format.Length is > 1 and <= 30 && format.All(c => c == '0'))
                        zeroPadding[index] = format.Length;
            }
            var date1904 = workbook.Descendants().FirstOrDefault(e => e.Name.LocalName == "workbookPr")?.Attribute("date1904")?.Value is "1" or "true";
            var sheets = new List<XlsxSheet>();
            var totalCells = 0;
            foreach (var sheet in workbook.Descendants().Where(e => e.Name.LocalName == "sheet"))
            {
                if (sheets.Count >= 20) throw new InvalidDataException("Chỉ hỗ trợ tối đa 20 trang tính trong một tệp.");
                var relationId = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                if (relationId is null || !relationships.TryGetValue(relationId, out var target))
                    throw new InvalidDataException("Trang tính không có liên kết dữ liệu nội bộ hợp lệ.");
                var document = Load(PackagePath(target));
                var rows = new List<XlsxRow>();
                var priorNumber = 0;
                foreach (var row in document.Descendants().Where(e => e.Name.LocalName == "sheetData").Elements().Where(e => e.Name.LocalName == "row"))
                {
                    if (rows.Count >= MaxRows) throw new InvalidDataException("Trang tính vượt giới hạn 50.000 dòng.");
                    var number = int.TryParse((string?)row.Attribute("r"), out var parsed) ? parsed : priorNumber + 1;
                    if (number <= priorNumber || number > 1048576) throw new InvalidDataException("Thứ tự dòng Excel không hợp lệ.");
                    priorNumber = number;
                    var cells = new Dictionary<int, XlsxCell>();
                    var column = 0;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        if (++totalCells > MaxCells) throw new InvalidDataException("Tệp Excel vượt giới hạn 500.000 ô dữ liệu.");
                        var reference = (string?)cell.Attribute("r");
                        column = reference is null ? column + 1 : ColumnNumber(reference);
                        if (column is < 1 or > 256 || !cells.TryAdd(column, ReadCell(cell, strings, zeroPadding)))
                            throw new InvalidDataException("Cột Excel vượt giới hạn 256 cột hoặc bị trùng.");
                    }
                    rows.Add(new XlsxRow(number, cells));
                }
                var merges = document.Descendants().Where(e => e.Name.LocalName == "mergeCell")
                    .Select(e => ParseMerge((string?)e.Attribute("ref") ?? "")).ToArray();
                sheets.Add(new XlsxSheet((string?)sheet.Attribute("name") ?? $"Sheet{sheets.Count + 1}", rows, merges));
            }
            if (sheets.Count == 0) throw new InvalidDataException("Tệp Excel không có trang tính.");
            return new XlsxWorkbook(date1904, sheets);
        }
        catch (XmlException exception) { throw new InvalidDataException("Nội dung XML trong tệp Excel không hợp lệ.", exception); }
    }

    private static XlsxCell ReadCell(XElement cell, string[] strings, IReadOnlyDictionary<int, int> zeroPadding)
    {
        var type = (string?)cell.Attribute("t") ?? "n";
        var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
        var hasFormula = cell.Elements().Any(e => e.Name.LocalName == "f");
        if (hasFormula && value is null)
            return new XlsxCell("", Error: "Ô công thức chưa có giá trị đã lưu. Hãy mở, tính lại và lưu tệp bằng Excel trước khi nhập.");
        if (type == "e") return new XlsxCell(value ?? "", Error: "Ô Excel đang chứa lỗi công thức.");
        if (type == "s")
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= strings.Length)
                return new XlsxCell("", Error: "Tham chiếu văn bản Excel không hợp lệ.");
            return new XlsxCell(strings[index]);
        }
        if (type == "inlineStr") return new XlsxCell(RichText(cell));
        if (type == "n" && int.TryParse((string?)cell.Attribute("s"), out var style) && zeroPadding.TryGetValue(style, out var width)
            && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0 && number == decimal.Truncate(number))
            return new XlsxCell(value ?? "", true, IdentifierText: number.ToString("0", CultureInfo.InvariantCulture).PadLeft(width, '0'));
        return new XlsxCell(value ?? "", type == "n");
    }

    private static string RichText(XElement element) => string.Concat(element.Descendants()
        .Where(e => e.Name.LocalName == "t" && !e.Ancestors().Any(a => a.Name.LocalName == "rPh")).Select(e => e.Value));

    private static int ColumnNumber(string reference)
    {
        var column = 0;
        foreach (var character in reference)
        {
            if (character is >= 'A' and <= 'Z') column = checked(column * 26 + character - 'A' + 1);
            else if (character is >= 'a' and <= 'z') column = checked(column * 26 + character - 'a' + 1);
            else if (char.IsDigit(character)) break;
            else throw new InvalidDataException("Địa chỉ ô Excel không hợp lệ.");
            if (column > 256) return column;
        }
        return column;
    }

    private static XlsxMerge ParseMerge(string reference)
    {
        var endpoints = reference.Split(':');
        if (endpoints.Length is < 1 or > 2) throw new InvalidDataException("Vùng ô gộp Excel không hợp lệ.");
        static (int Row, int Column) Address(string value)
        {
            var split = value.TakeWhile(char.IsLetter).Count();
            if (split == 0 || !int.TryParse(value[split..], NumberStyles.None, CultureInfo.InvariantCulture, out var row)
                || row is < 1 or > 1048576) throw new InvalidDataException("Địa chỉ vùng ô gộp Excel không hợp lệ.");
            var column = ColumnNumber(value);
            if (column is < 1 or > 256) throw new InvalidDataException("Cột vùng ô gộp Excel vượt giới hạn.");
            return (row, column);
        }
        var first = Address(endpoints[0]); var last = Address(endpoints[^1]);
        if (first.Row > last.Row || first.Column > last.Column) throw new InvalidDataException("Thứ tự vùng ô gộp Excel không hợp lệ.");
        return new XlsxMerge(first.Row, last.Row, first.Column, last.Column);
    }

    private static string PackagePath(string target)
    {
        if (target.Contains(':') || target.Contains('\\')) throw new InvalidDataException("Liên kết trang tính không hợp lệ.");
        var components = (target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target).Split('/');
        var path = new List<string>();
        foreach (var component in components)
        {
            if (component is "" or ".") continue;
            if (component == "..") { if (path.Count == 0) throw new InvalidDataException("Liên kết trang tính vượt phạm vi tệp."); path.RemoveAt(path.Count - 1); }
            else path.Add(component);
        }
        return string.Join('/', path);
    }
}
