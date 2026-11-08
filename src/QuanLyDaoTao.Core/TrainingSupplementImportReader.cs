using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuanLyDaoTao.Core;

internal static class TrainingSupplementImportReader
{
    private static readonly Regex Bullet = new(@"^\s*['’]?\s*[-•*–—]+\s*", RegexOptions.CultureInvariant);
    private static readonly Regex Period = new(@"^\s*(?<start>\d{1,2}[/\.]\d{1,2}[/\.]\d{4}|\d{4}-\d{1,2}-\d{1,2})\s*[-–—]\s*(?<end>\d{1,2}[/\.]\d{1,2}[/\.]\d{4}|\d{4}-\d{1,2}-\d{1,2})\s*$", RegexOptions.CultureInvariant);

    internal sealed record Layout(int LastHeader, IReadOnlyDictionary<string, int> Columns);

    internal static Layout? FindLayout(XlsxSheet sheet)
    {
        var nameHeader = sheet.Rows.Take(20).FirstOrDefault(row => row.Cells.Values.Any(cell => Field(cell.Text) == "Name"));
        if (nameHeader is null) return null;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var last = nameHeader.Number;
        foreach (var header in sheet.Rows.Where(row => row.Number >= nameHeader.Number - 2 && row.Number <= nameHeader.Number + 2))
            foreach (var (column, cell) in header.Cells)
                if (Field(cell.Text) is { } field)
                {
                    if (columns.TryGetValue(field, out var prior) && prior != column)
                        throw new InvalidDataException($"Trang “{sheet.Name}” có nhiều cột {cell.Text}. Vui lòng giữ một cột cho mỗi trường.");
                    columns[field] = column;
                    last = Math.Max(last, header.Number);
                }
        return columns.ContainsKey("Name") && columns.ContainsKey("Title")
            && new[] { "IssuedDate", "StudyPeriod", "Hours", "Provider" }.Count(columns.ContainsKey) >= 2
            ? new Layout(last, columns) : null;
    }

    internal static IReadOnlyList<TrainingSupplementImportRow> Read(XlsxSheet sheet, Layout layout, bool date1904, IReadOnlyList<EmployeeImportRow> profiles)
    {
        var result = new List<TrainingSupplementImportRow>();
        var byNumber = sheet.Rows.ToDictionary(row => row.Number);
        var inferredBlocks = CorroboratedBlocks(sheet, layout, profiles);
        foreach (var row in sheet.Rows.Where(row => row.Number > layout.LastHeader))
        {
            XlsxCell? Cell(string field) => layout.Columns.TryGetValue(field, out var column) ? row.Cells.GetValueOrDefault(column) : null;
            if (new[] { "Title", "IssuedDate", "StudyPeriod", "Hours", "Provider" }.Select(Cell)
                .All(cell => string.IsNullOrWhiteSpace(cell?.Text) && cell?.Error is null)) continue;
            var errors = new List<string>(); var warnings = new List<string>();
            string Text(string field)
            {
                var cell = Cell(field);
                if (cell?.Error is { } error) errors.Add($"{Label(field)}: {error}");
                if (cell?.Text.Length > 32767) errors.Add($"{Label(field)} vượt giới hạn 32.767 ký tự.");
                return cell?.Text.Trim() ?? "";
            }
            var nameColumn = layout.Columns["Name"];
            var name = Text("Name");
            var block = sheet.Merges.FirstOrDefault(merge => merge.FirstColumn == nameColumn && merge.LastColumn == nameColumn
                && merge.FirstRow > layout.LastHeader && row.Number >= merge.FirstRow && row.Number <= merge.LastRow);
            var inferred = inferredBlocks.FirstOrDefault(merge => row.Number >= merge.FirstRow && row.Number <= merge.LastRow);
            if (block is null && inferred is not null)
            {
                block = inferred;
                warnings.Add("Ô họ tên chưa gộp: đã đối chiếu mã nhận diện duy nhất trong cùng nhóm với danh sách nhân viên.");
            }
            if (name.Length == 0 && block is not null && byNumber.TryGetValue(block.FirstRow, out var first))
            {
                var nameCell = first.Cells.GetValueOrDefault(nameColumn);
                if (nameCell?.Error is { } error) errors.Add($"Họ và tên: {error}");
                name = nameCell?.Text.Trim() ?? "";
            }
            if (name.Length == 0) errors.Add("Thiếu họ tên. Chỉ nối các dòng nằm trong vùng ô họ tên được gộp; không tự lấy tên của dòng trước.");
            XlsxCell? IdentityCell(string field)
            {
                if (!layout.Columns.TryGetValue(field, out var column)) return null;
                var candidates = sheet.Rows.Where(item => item.Number >= (block?.FirstRow ?? row.Number)
                    && item.Number <= (block?.LastRow ?? row.Number)).Select(item => item.Cells.GetValueOrDefault(column))
                    .Where(cell => cell is not null && (!string.IsNullOrWhiteSpace(cell.Text) || cell.Error is not null)).ToArray();
                if (candidates.Select(cell => cell!.IdentifierText ?? cell.Text.Trim()).Distinct(StringComparer.Ordinal).Count() > 1)
                    errors.Add($"{Label(field)} mâu thuẫn giữa các dòng trong cùng vùng họ tên.");
                var candidate = candidates.FirstOrDefault();
                if (candidate?.Error is { } error) errors.Add($"{Label(field)}: {error}");
                return candidate;
            }
            string Identifier(string field)
            {
                var cell = IdentityCell(field);
                if (cell is null) return "";
                if (!cell.IsNumeric) return cell.Text.Trim();
                if (!decimal.TryParse(cell.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0 || decimal.Truncate(number) != number)
                { errors.Add($"{Label(field)} không phải mã số nguyên hợp lệ."); return cell.Text.Trim(); }
                return cell.IdentifierText ?? number.ToString("0", CultureInfo.InvariantCulture);
            }
            DateTime? ParseCellDate(XlsxCell? cell, string field)
            {
                if (IsEmpty(cell?.Text)) return null;
                if (TryDate(cell!, date1904, out var date)) return date;
                errors.Add($"{Label(field)} không hợp lệ: “{cell!.Text}”. Dùng ngày/tháng/năm hoặc ô ngày Excel.");
                return null;
            }
            var title = Bullet.Replace(Text("Title"), "").Trim();
            if (title.Length == 0) errors.Add("Thiếu tên chứng chỉ / khóa học.");
            _ = Text("IssuedDate");
            var issued = ParseCellDate(Cell("IssuedDate"), "IssuedDate");
            var study = Text("StudyPeriod");
            DateTime? start = null, end = null;
            if (!IsEmpty(study))
            {
                if (TryDate(Cell("StudyPeriod")!, date1904, out var single)) start = end = single;
                else if (Period.Match(study) is { Success: true } range)
                {
                    if (TryDateText(range.Groups["start"].Value, out var firstDate) && TryDateText(range.Groups["end"].Value, out var lastDate))
                    {
                        start = firstDate; end = lastDate;
                        if (end < start) errors.Add("Ngày kết thúc học phải bằng hoặc sau ngày bắt đầu.");
                    }
                    else errors.Add("Khoảng ngày học không hợp lệ.");
                }
                else warnings.Add("Thời gian học chưa xác định được ngày bắt đầu/kết thúc; giữ nguyên nội dung để bổ sung.");
            }
            decimal? hours = null;
            var hourText = Text("Hours");
            if (!IsEmpty(hourText))
            {
                if (decimal.TryParse(hourText.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0) hours = value;
                else errors.Add("Số tiết phải là số lớn hơn 0; hỗ trợ số lẻ như 1,5.");
            }
            var type = Text("Type");
            if (type.Length == 0) type = "Chứng chỉ";
            else if (type.Equals("CME", StringComparison.OrdinalIgnoreCase)) type = "CME";
            else if (type.Equals("SHKHKT", StringComparison.OrdinalIgnoreCase)) type = "SHKHKT";
            else if (Normalize(type) is not ("chung chi" or "hoi thao")) warnings.Add("Phân loại chưa được xác nhận là CME/SHKHKT.");
            if (hours is null) warnings.Add("Chưa có số tiết.");
            if (start is null) warnings.Add("Chưa có ngày học; ngày cấp được lưu riêng.");
            if (issued is { } issuedDate && end is { } endDate && issuedDate < endDate)
                warnings.Add("Ngày cấp trong nguồn sớm hơn ngày kết thúc học; cần đối chiếu chứng chỉ.");
            var provider = Text("Provider");
            result.Add(new TrainingSupplementImportRow
            {
                RowNumber = row.Number, SheetName = sheet.Name, EmployeeName = name,
                EmployeeCode = Identifier("Code"), SocialInsuranceNumber = Identifier("Insurance"),
                DateOfBirth = ParseCellDate(IdentityCell("Birth"), "Birth"), Title = title, Type = type,
                IssuedDate = issued, StartDate = start, EndDate = end, StudyPeriod = study,
                Hours = hours, Provider = provider,
                Notes = $"Nguồn: {sheet.Name}, dòng {row.Number}.\nTên gốc: {Cell("Title")?.Text ?? ""}\nNgày cấp gốc: {Cell("IssuedDate")?.Text ?? ""}\nNgày học gốc: {study}\nSố tiết gốc: {hourText}\nNơi cấp: {provider}"
                    + (warnings.Count == 0 ? "" : "\nGhi chú đối chiếu: " + string.Join("; ", warnings.Distinct())),
                Errors = errors.Distinct().ToArray(), Warnings = warnings.Distinct().ToArray()
            });
        }
        return result;
    }

    internal static string SourceKey(string employeeId, TrainingSupplementImportRow row) => "detail:" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', employeeId, Normalize(row.Title), row.IssuedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.StudyPeriod.Length > 0 && row.IssuedDate is null ? Normalize(row.StudyPeriod) : ""))));

    private static IReadOnlyList<XlsxMerge> CorroboratedBlocks(XlsxSheet sheet, Layout layout, IReadOnlyList<EmployeeImportRow> profiles)
    {
        var result = new List<XlsxMerge>();
        var nameColumn = layout.Columns["Name"];
        var named = sheet.Rows.Where(row => row.Number > layout.LastHeader && !string.IsNullOrWhiteSpace(row.Cells.GetValueOrDefault(nameColumn)?.Text)).ToArray();
        for (var index = 0; index < named.Length; index++)
        {
            var first = named[index].Number;
            var last = index + 1 < named.Length ? named[index + 1].Number - 1 : sheet.Rows[^1].Number;
            if (last <= first || sheet.Merges.Any(merge => merge.FirstColumn == nameColumn && merge.LastColumn == nameColumn && merge.FirstRow == first)) continue;
            var name = named[index].Cells[nameColumn].Text.Trim().Normalize(NormalizationForm.FormC);
            var matches = profiles.Where(profile => profile.IsValid && string.Equals(profile.Employee.Name.Trim().Normalize(NormalizationForm.FormC), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) continue;
            var target = matches[0].Employee;
            var confirmed = false;
            foreach (var field in new[] { "Code", "Insurance", "PracticeLicense" })
            {
                if (!layout.Columns.TryGetValue(field, out var column)) continue;
                string Identity(Employee employee) => field switch { "Code" => employee.Code, "Insurance" => employee.SocialInsuranceNumber, _ => employee.PracticeLicenseNumber };
                var expected = Normalize(Identity(target)).Replace(" ", "");
                if (expected.Length == 0 || expected.Contains("chocap", StringComparison.Ordinal)) continue;
                if (profiles.Count(profile => Normalize(Identity(profile.Employee)).Replace(" ", "") == expected) != 1) continue;
                var values = sheet.Rows.Where(row => row.Number >= first && row.Number <= last).Select(row => row.Cells.GetValueOrDefault(column))
                    .Where(cell => cell is not null && (!string.IsNullOrWhiteSpace(cell.Text) || cell.Error is not null)).ToArray();
                if (values.Length > 0 && values.All(cell => cell!.Error is null && Normalize(cell.IdentifierText ?? cell.Text).Replace(" ", "") == expected)) confirmed = true;
            }
            if (confirmed) result.Add(new XlsxMerge(first, last, nameColumn, nameColumn));
        }
        return result;
    }

    internal static string TitleKey(string title)
    {
        var key = Normalize(title);
        return key.StartsWith("qlcl ", StringComparison.Ordinal) ? key[5..] : key;
    }

    private static bool IsEmpty(string? text) => string.IsNullOrWhiteSpace(text) || text.Trim() is "-" or "—";
    private static bool TryDate(XlsxCell cell, bool date1904, out DateTime date)
    {
        date = default;
        if (cell.IsNumeric && double.TryParse(cell.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                if (!double.IsFinite(serial) || serial < 0 || (!date1904 && serial is >= 60 and < 61)) return false;
                date = (date1904 ? new DateTime(1904, 1, 1).AddDays(serial) : DateTime.FromOADate(serial < 60 ? serial + 1 : serial)).Date;
                return true;
            }
            catch (ArgumentException) { return false; }
        }
        return TryDateText(cell.Text, out date);
    }
    private static bool TryDateText(string text, out DateTime date) => DateTime.TryParseExact(text.Trim(),
        ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd"],
        CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);

    private static string? Field(string value) => Normalize(value) switch
    {
        "ho va ten" or "ho ten" or "ten nhan vien" or "name" or "full name" => "Name",
        "ma nv" or "ma nhan vien" or "employee code" or "code" => "Code",
        "ma so bhxh" or "ma bhxh" or "bhxh" or "so bhxh" or "social insurance number" => "Insurance",
        "ngay sinh" or "date of birth" or "dob" => "Birth",
        "giay phep hanh nghe so" or "so giay phep hanh nghe" or "so cchn" or "practice license number" => "PracticeLicense",
        "cac chung chi chuyen mon" or "chung chi chuyen mon" or "ten chung chi" or "ten khoa hoc" or "ten khoa dao tao" or "professional certificates" => "Title",
        "ngay cap" or "ngay cap chung chi" or "issued date" => "IssuedDate",
        "ngay hoc" or "thoi gian hoc" or "thoi gian dao tao" or "study period" => "StudyPeriod",
        "so tiet" or "tong so tiet" or "hours" => "Hours",
        "noi cap" or "don vi cap" or "don vi to chuc" or "provider" => "Provider",
        "phan loai" or "loai hinh" or "type" => "Type",
        _ => null
    };
    private static string Label(string field) => field switch
    {
        "Name" => "Họ và tên", "Title" => "Tên chứng chỉ", "IssuedDate" => "Ngày cấp", "StudyPeriod" => "Ngày học",
        "Hours" => "Số tiết", "Provider" => "Nơi cấp", "Birth" => "Ngày sinh", "Insurance" => "BHXH", "Code" => "Mã nhân viên", _ => field
    };
    private static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(char.IsLetterOrDigit(character) ? character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
