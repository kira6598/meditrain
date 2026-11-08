using System.Globalization;
using System.Text;

namespace QuanLyDaoTao.Core;

public static class EmployeeImportService
{
    private static readonly Dictionary<string, string> HeaderFields = BuildHeaderFields();

    public static EmployeeImportPreview Read(string path, string defaultDepartment = "Chưa phân khoa")
    {
        var workbook = XlsxReader.Read(path);
        var rows = new List<EmployeeImportRow>();
        var supplementRows = new List<TrainingSupplementImportRow>();
        var detailSheets = new List<(XlsxSheet Sheet, TrainingSupplementImportReader.Layout Layout)>();
        var recognizedSheets = 0;
        defaultDepartment = string.IsNullOrWhiteSpace(defaultDepartment) ? "Chưa phân khoa" : defaultDepartment.Trim();
        foreach (var sheet in workbook.Sheets)
        {
            if (TrainingSupplementImportReader.FindLayout(sheet) is { } supplementLayout)
            {
                recognizedSheets++;
                detailSheets.Add((sheet, supplementLayout));
                continue;
            }
            var nameHeader = sheet.Rows.Take(20).FirstOrDefault(row => row.Cells.Values.Any(cell => Field(cell.Text) == nameof(Employee.Name)));
            if (nameHeader is null) continue;
            recognizedSheets++;
            var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
            var lastHeader = nameHeader.Number;
            foreach (var header in sheet.Rows.Where(row => row.Number >= nameHeader.Number - 2 && row.Number <= nameHeader.Number + 2))
                foreach (var (column, cell) in header.Cells)
                    if (Field(cell.Text) is { } field)
                    {
                        if (mapping.TryGetValue(field, out var prior) && prior != column)
                            throw new InvalidDataException($"Trang “{sheet.Name}” có nhiều cột cùng tiêu đề {cell.Text}. Vui lòng giữ một cột cho mỗi trường.");
                        mapping[field] = column;
                        lastHeader = Math.Max(lastHeader, header.Number);
                    }
            foreach (var row in sheet.Rows.Where(row => row.Number > lastHeader))
            {
                var sourceCells = mapping.Values.Select(column => row.Cells.GetValueOrDefault(column)).Where(cell => cell is not null).ToArray();
                if (sourceCells.All(cell => string.IsNullOrWhiteSpace(cell!.Text) && cell.Error is null)) continue;
                var errors = new List<string>();
                var warnings = new List<string>();
                XlsxCell? Cell(string field) => mapping.TryGetValue(field, out var column) ? row.Cells.GetValueOrDefault(column) : null;
                string Text(string field, bool preserveWhitespace = false)
                {
                    var cell = Cell(field);
                    if (cell?.Error is { } error) errors.Add($"{Label(field)}: {error}");
                    if (cell?.Text.Length > 32767) errors.Add($"{Label(field)} vượt giới hạn 32.767 ký tự.");
                    return preserveWhitespace ? cell?.Text ?? "" : cell?.Text.Trim() ?? "";
                }
                string Identifier(string field)
                {
                    var text = Text(field);
                    var cell = Cell(field);
                    if (text.Length == 0 || cell?.IsNumeric != true) return text;
                    if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value != decimal.Truncate(value))
                    { errors.Add($"{Label(field)} dạng số không nguyên hoặc vượt độ chính xác cho phép. Hãy định dạng cột là văn bản."); return text; }
                    var digits = value.ToString("0", CultureInfo.InvariantCulture);
                    if (digits.TrimStart('-').Length > 15) warnings.Add($"{Label(field)} có hơn 15 chữ số và đang lưu dạng số; hãy kiểm tra độ chính xác trong tệp gốc.");
                    return cell.IdentifierText ?? digits;
                }
                DateTime? Date(string field)
                {
                    var text = Text(field);
                    if (string.IsNullOrWhiteSpace(text) || text is "-" or "—") return null;
                    var cell = Cell(field);
                    if (cell?.IsNumeric == true && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                    {
                        try
                        {
                            if (!double.IsFinite(serial) || serial < 0 || (!workbook.Date1904 && serial is >= 60 and < 61)) throw new ArgumentException();
                            var date = workbook.Date1904 ? new DateTime(1904, 1, 1).AddDays(serial)
                                : DateTime.FromOADate(serial < 60 ? serial + 1 : serial);
                            return date.Date;
                        }
                        catch (ArgumentException) { }
                    }
                    else if (DateTime.TryParseExact(text,
                        ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"],
                        CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date.Date;
                    errors.Add($"{Label(field)} không hợp lệ: “{text}”. Dùng ngày/tháng/năm hoặc ô ngày Excel.");
                    return null;
                }
                var employee = new Employee
                {
                    Code = Identifier(nameof(Employee.Code)), Name = Text(nameof(Employee.Name)),
                    Department = mapping.ContainsKey(nameof(Employee.Department)) ? Text(nameof(Employee.Department)) : defaultDepartment,
                    Title = Text(nameof(Employee.Title)), Degree = Text(nameof(Employee.Degree)),
                    StartDate = Date(nameof(Employee.StartDate)), SocialInsuranceNumber = Identifier(nameof(Employee.SocialInsuranceNumber)),
                    DateOfBirth = Date(nameof(Employee.DateOfBirth)), PracticeLicenseNumber = Identifier(nameof(Employee.PracticeLicenseNumber)),
                    PracticeLicenseDate = Date(nameof(Employee.PracticeLicenseDate)),
                    ProfessionalCertificates = Text(nameof(Employee.ProfessionalCertificates), true),
                    ConferenceHistory = Text(nameof(Employee.ConferenceHistory), true),
                    ScientificActivityHistory = Text(nameof(Employee.ScientificActivityHistory), true)
                };
                if (string.IsNullOrWhiteSpace(employee.Name)) errors.Add("Thiếu họ và tên nhân viên.");
                if (string.IsNullOrWhiteSpace(employee.Department))
                { employee.Department = defaultDepartment; warnings.Add($"Khoa / phòng trống; dùng “{defaultDepartment}” cho nhân viên mới."); }
                if (!mapping.ContainsKey(nameof(Employee.Department))) warnings.Add($"Không có cột khoa / phòng; nhân viên mới dùng “{defaultDepartment}”, nhân viên cũ giữ khoa / phòng hiện tại.");
                if (employee.Code.Length == 0) warnings.Add("Chưa có mã nhân viên; hệ thống tự cấp mã khi thêm mới.");
                if (employee.DateOfBirth is null) warnings.Add("Chưa có ngày sinh; cần kiểm tra kỹ khi có người trùng họ tên.");
                rows.Add(new EmployeeImportRow
                {
                    RowNumber = row.Number, SheetName = sheet.Name, Employee = employee,
                    PresentFields = mapping.Keys.Where(field => field != nameof(Employee.Department)
                        || !string.IsNullOrWhiteSpace(Cell(nameof(Employee.Department))?.Text)).ToArray(),
                    Errors = errors.Distinct().ToArray(), Warnings = warnings.ToArray()
                });
            }
        }
        foreach (var (sheet, layout) in detailSheets)
            supplementRows.AddRange(TrainingSupplementImportReader.Read(sheet, layout, workbook.Date1904, rows));
        if (recognizedSheets == 0) throw new InvalidDataException("Không tìm thấy cột “Họ và tên” trong 20 dòng đầu của trang tính. Vui lòng kiểm tra tiêu đề tệp Excel.");
        if (rows.Count == 0 && supplementRows.Count == 0) throw new InvalidDataException("Không tìm thấy dòng nhân viên hoặc chứng chỉ nào bên dưới tiêu đề.");
        AddDuplicateDiagnostics(rows);
        return new EmployeeImportPreview { FileName = Path.GetFullPath(path), Rows = rows, SupplementRows = supplementRows };
    }

    public static EmployeeImportResult Apply(TrainingRepository repository, EmployeeImportPreview preview,
        ExistingEmployeeImportMode mode = ExistingEmployeeImportMode.Skip)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(preview);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (preview.Rows.Any(row => !row.IsValid) || preview.SupplementRows.Any(row => !row.IsValid))
            throw new InvalidOperationException("Tệp còn dòng lỗi. Vui lòng sửa toàn bộ lỗi và xem trước lại; chưa có dữ liệu nào được nhập.");
        foreach (var group in preview.Rows.GroupBy(row => PersonName(row.Employee.Name)).Where(group => group.Count() > 1))
            if (group.FirstOrDefault(row => string.IsNullOrWhiteSpace(row.Employee.Code)
                && string.IsNullOrWhiteSpace(row.Employee.SocialInsuranceNumber) && row.Employee.DateOfBirth is null) is { } ambiguous)
                throw Conflict(ambiguous, "Tệp có nhiều người trùng họ tên nhưng dòng này thiếu mã nhân viên, BHXH và ngày sinh để phân biệt.");
        var working = repository.GetEmployees().Select(employee => employee with { }).ToList();
        var usedCodes = new HashSet<string>(working.Select(employee => employee.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var row in preview.Rows.Where(row => !string.IsNullOrWhiteSpace(row.Employee.Code))) usedCodes.Add(row.Employee.Code.Trim());
        var writes = new Dictionary<string, Employee>(StringComparer.Ordinal);
        var addedIds = new HashSet<string>(StringComparer.Ordinal);
        var updatedIds = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        var nextCode = 1;
        foreach (var row in preview.Rows)
        {
            var source = row.Employee;
            if (string.IsNullOrWhiteSpace(source.Name)) throw Conflict(row, "Thiếu họ và tên nhân viên.");
            var match = FindMatch(working, source, row);
            if (match is not null)
            {
                if (mode == ExistingEmployeeImportMode.Skip) { skipped++; continue; }
                var merged = MergeNonEmpty(match, source, row.PresentFields);
                if (merged == match) { skipped++; continue; }
                working[working.IndexOf(match)] = merged;
                writes[match.Id] = merged;
                if (!addedIds.Contains(match.Id)) updatedIds.Add(match.Id);
                continue;
            }
            var code = source.Code.Trim();
            if (code.Length == 0)
            {
                while (usedCodes.Contains($"NV{nextCode:0000}")) nextCode++;
                code = $"NV{nextCode++:0000}";
            }
            usedCodes.Add(code);
            var employee = source with { Id = Guid.NewGuid().ToString("N"), Code = code, Name = source.Name.Trim(), Department = string.IsNullOrWhiteSpace(source.Department) ? "Chưa phân khoa" : source.Department.Trim() };
            working.Add(employee); writes[employee.Id] = employee; addedIds.Add(employee.Id);
        }
        var existingSupplements = repository.GetSupplements().ToDictionary(item => (item.EmployeeId, item.SourceReference));
        var supplementWrites = new Dictionary<(string EmployeeId, string SourceReference), TrainingSupplement>();
        var addedSupplements = new HashSet<(string, string)>();
        var updatedSupplements = new HashSet<(string, string)>();
        var skippedSupplements = 0;
        var linkedReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in preview.SupplementRows)
        {
            var identity = new Employee
            {
                Name = source.EmployeeName, Code = source.EmployeeCode,
                SocialInsuranceNumber = source.SocialInsuranceNumber, DateOfBirth = source.DateOfBirth
            };
            var context = new EmployeeImportRow { RowNumber = source.RowNumber, SheetName = source.SheetName, Employee = identity };
            var target = FindMatch(working, identity, context)
                ?? throw Conflict(context, "Không tìm thấy nhân viên tương ứng với dòng chứng chỉ. Hãy nhập hồ sơ nhân viên trước hoặc bổ sung thông tin nhận diện.");
            var reference = TrainingSupplementImportReader.SourceKey(target.Id, source);
            var key = (target.Id, reference);
            existingSupplements.TryGetValue(key, out var prior);
            if (supplementWrites.TryGetValue(key, out var earlier)) prior = earlier;
            var linked = prior?.LinkedSourceReference ?? "";
            var notes = source.Notes;
            if (linked.Length == 0)
            {
                var candidates = EmployeeTrainingHistory.Build(target, []).Where(row => row.IsImported
                    && TrainingSupplementImportReader.TitleKey(row.Title) == TrainingSupplementImportReader.TitleKey(source.Title)
                    && !linkedReferences.Contains(row.SourceReference)).ToArray();
                var dated = candidates.Where(row => source.IssuedDate is not null && row.EndDate == source.IssuedDate).ToArray();
                var sameTitles = preview.SupplementRows.Count(row => PersonName(row.EmployeeName) == PersonName(source.EmployeeName)
                    && TrainingSupplementImportReader.TitleKey(row.Title) == TrainingSupplementImportReader.TitleKey(source.Title));
                var match = dated.Length == 1 ? dated[0] : candidates.Length == 1 && sameTitles == 1 ? candidates[0] : null;
                if (match is not null)
                {
                    linked = match.SourceReference;
                }
            }
            var linkedRaw = linked.Length == 0 ? null : EmployeeTrainingHistory.Build(target, []).FirstOrDefault(row => row.SourceReference == linked);
            if (linkedRaw?.EndDate is not null && source.IssuedDate is not null && linkedRaw.EndDate != source.IssuedDate)
                notes += $"\nNgày cấp trong Sheet bổ sung khác ngày ghi trong nội dung cũ ({linkedRaw.EndDate:dd/MM/yyyy}); giữ riêng thông tin nguồn để đối chiếu.";
            if (linked.Length > 0) linkedReferences.Add(linked);
            var supplement = new TrainingSupplement
            {
                Id = prior?.Id ?? Guid.NewGuid().ToString("N"), EmployeeId = target.Id, Title = source.Title,
                Type = source.Type, IssuedDate = source.IssuedDate, StartDate = source.StartDate, EndDate = source.EndDate,
                StudyPeriod = source.StudyPeriod, Hours = source.Hours, Provider = source.Provider,
                SourceReference = reference, LinkedSourceReference = linked, SourceSheet = source.SheetName,
                SourceRow = source.RowNumber, Notes = notes
            };
            if (prior is not null && prior == supplement) { skippedSupplements++; continue; }
            if (prior is null) addedSupplements.Add(key);
            else if (!addedSupplements.Contains(key)) updatedSupplements.Add(key);
            supplementWrites[key] = supplement;
        }
        // Identity checks and detail validation finish before either kind of record is written.
        repository.ImportData(writes.Values, supplementWrites.Values);
        return new EmployeeImportResult
        {
            Added = addedIds.Count, Updated = updatedIds.Count, Skipped = skipped,
            SupplementsAdded = addedSupplements.Count, SupplementsUpdated = updatedSupplements.Count, SupplementsSkipped = skippedSupplements
        };
    }

    private static Employee? FindMatch(List<Employee> existing, Employee source, EmployeeImportRow row)
    {
        var code = source.Code.Trim();
        var insurance = source.SocialInsuranceNumber.Trim();
        var name = PersonName(source.Name);
        if (code.Length > 0)
        {
            var match = existing.SingleOrDefault(employee => string.Equals(employee.Code.Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                EnsurePersonCompatible(match, source, row);
                EnsureInsuranceCompatible(existing, match, source, row);
                return match;
            }
            if (existing.Any(employee => (insurance.Length > 0 && employee.SocialInsuranceNumber.Trim() == insurance)
                || (PersonName(employee.Name) == name && (employee.DateOfBirth is null || source.DateOfBirth is null || employee.DateOfBirth.Value.Date == source.DateOfBirth.Value.Date))))
                throw Conflict(row, "Mã nhân viên trong tệp khác mã của người có cùng thông tin nhận diện. Hãy kiểm tra mã trước khi nhập.");
            return null;
        }
        if (insurance.Length > 0)
        {
            var insured = existing.Where(employee => employee.SocialInsuranceNumber.Trim() == insurance).ToArray();
            if (insured.Length > 1) throw Conflict(row, "Mã BHXH trùng nhiều nhân viên hiện có; không thể xác định người cần cập nhật.");
            if (insured.Length == 1) { EnsurePersonCompatible(insured[0], source, row); return insured[0]; }
        }
        var named = existing.Where(employee => PersonName(employee.Name) == name).ToArray();
        if (named.Length == 0) return null;
        Employee? result;
        if (source.DateOfBirth is { } birth)
        {
            var dated = named.Where(employee => employee.DateOfBirth?.Date == birth.Date).ToArray();
            if (dated.Length > 1) throw Conflict(row, "Họ tên và ngày sinh khớp nhiều nhân viên. Hãy bổ sung mã nhân viên để nhập an toàn.");
            if (dated.Length == 1) result = dated[0];
            else if (named.Length == 1 && named[0].DateOfBirth is null) result = named[0];
            else if (named.All(employee => employee.DateOfBirth is not null)) return null;
            else throw Conflict(row, "Có nhiều người trùng họ tên và thiếu ngày sinh. Hãy bổ sung mã nhân viên.");
        }
        else
        {
            if (named.Length > 1) throw Conflict(row, "Có nhiều nhân viên trùng họ tên. Hãy bổ sung mã nhân viên, BHXH hoặc ngày sinh.");
            result = named[0];
        }
        EnsurePersonCompatible(result, source, row);
        EnsureInsuranceCompatible(existing, result, source, row);
        return result;
    }

    private static void EnsurePersonCompatible(Employee target, Employee source, EmployeeImportRow row)
    {
        if (PersonName(target.Name) != PersonName(source.Name)
            || (target.DateOfBirth is { } existingBirth && source.DateOfBirth is { } sourceBirth && existingBirth.Date != sourceBirth.Date))
            throw Conflict(row, "Mã nhân viên hoặc BHXH khớp nhưng họ tên/ngày sinh mâu thuẫn. Không tự gộp hai người khác nhau.");
    }

    private static void EnsureInsuranceCompatible(IEnumerable<Employee> existing, Employee target, Employee source, EmployeeImportRow row)
    {
        var insurance = source.SocialInsuranceNumber.Trim();
        if (insurance.Length == 0) return;
        if ((target.SocialInsuranceNumber.Length > 0 && target.SocialInsuranceNumber.Trim() != insurance)
            || existing.Any(employee => employee.Id != target.Id && employee.SocialInsuranceNumber.Trim() == insurance))
            throw Conflict(row, "Thông tin BHXH mâu thuẫn với nhân viên đang có. Hãy kiểm tra lại trước khi cập nhật.");
    }

    private static Employee MergeNonEmpty(Employee existing, Employee source, IReadOnlyList<string> presentFields)
    {
        bool Has(string field) => presentFields.Contains(field, StringComparer.Ordinal);
        string Text(string field, string current, string incoming) => Has(field) && !string.IsNullOrWhiteSpace(incoming) ? incoming : current;
        return existing with
        {
            Name = Text(nameof(Employee.Name), existing.Name, source.Name),
            Department = Text(nameof(Employee.Department), existing.Department, source.Department),
            Title = Text(nameof(Employee.Title), existing.Title, source.Title), Degree = Text(nameof(Employee.Degree), existing.Degree, source.Degree),
            StartDate = Has(nameof(Employee.StartDate)) ? source.StartDate ?? existing.StartDate : existing.StartDate,
            SocialInsuranceNumber = Text(nameof(Employee.SocialInsuranceNumber), existing.SocialInsuranceNumber, source.SocialInsuranceNumber),
            DateOfBirth = Has(nameof(Employee.DateOfBirth)) ? source.DateOfBirth ?? existing.DateOfBirth : existing.DateOfBirth,
            PracticeLicenseNumber = Text(nameof(Employee.PracticeLicenseNumber), existing.PracticeLicenseNumber, source.PracticeLicenseNumber),
            PracticeLicenseDate = Has(nameof(Employee.PracticeLicenseDate)) ? source.PracticeLicenseDate ?? existing.PracticeLicenseDate : existing.PracticeLicenseDate,
            ProfessionalCertificates = Text(nameof(Employee.ProfessionalCertificates), existing.ProfessionalCertificates, source.ProfessionalCertificates),
            ConferenceHistory = Text(nameof(Employee.ConferenceHistory), existing.ConferenceHistory, source.ConferenceHistory),
            ScientificActivityHistory = Text(nameof(Employee.ScientificActivityHistory), existing.ScientificActivityHistory, source.ScientificActivityHistory)
        };
    }

    private static void AddDuplicateDiagnostics(List<EmployeeImportRow> rows)
    {
        var errors = rows.Select(row => row.Errors.ToList()).ToArray();
        var warnings = rows.Select(row => row.Warnings.ToList()).ToArray();
        foreach (var group in rows.Select((row, index) => (row, index))
            .GroupBy(item => PersonName(item.row.Employee.Name)).Where(group => group.Count() > 1))
            foreach (var item in group.Where(item => item.row.Employee.Code.Length == 0
                && item.row.Employee.SocialInsuranceNumber.Length == 0 && item.row.Employee.DateOfBirth is null))
                errors[item.index].Add("Họ tên xuất hiện nhiều lần nhưng thiếu mã nhân viên, BHXH và ngày sinh để phân biệt; hãy bổ sung thông tin.");
        foreach (var group in rows.Select((row, index) => (row, index)).Where(item => item.row.Employee.Code.Length > 0)
            .GroupBy(item => item.row.Employee.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group) errors[item.index].Add("Mã nhân viên bị trùng trong tệp Excel.");
        foreach (var group in rows.Select((row, index) => (row, index)).Where(item => item.row.Employee.SocialInsuranceNumber.Length > 0)
            .GroupBy(item => item.row.Employee.SocialInsuranceNumber.Trim()).Where(group => group.Count() > 1))
        {
            var persons = group.Select(item => PersonName(item.row.Employee.Name)).Distinct().ToArray();
            var birthdays = group.Select(item => item.row.Employee.DateOfBirth?.Date).Where(date => date is not null).Distinct().ToArray();
            foreach (var item in group)
                if (persons.Length > 1 || birthdays.Length > 1) errors[item.index].Add("BHXH trùng nhưng họ tên/ngày sinh khác nhau trong tệp; cần kiểm tra lại.");
                else warnings[item.index].Add("BHXH xuất hiện nhiều lần trong tệp; các dòng cùng người sẽ được đối chiếu trước khi nhập.");
        }
        foreach (var group in rows.Select((row, index) => (row, index)).Where(item => item.row.Employee.PracticeLicenseNumber.Length > 0
            && !HeaderName(item.row.Employee.PracticeLicenseNumber).Contains("cho cap", StringComparison.Ordinal))
            .GroupBy(item => item.row.Employee.PracticeLicenseNumber.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var item in group) warnings[item.index].Add("Số giấy phép trùng với dòng khác trong tệp; giấy phép không được dùng để tự gộp nhân viên.");
        for (var index = 0; index < rows.Count; index++) rows[index] = rows[index] with { Errors = errors[index].Distinct().ToArray(), Warnings = warnings[index].Distinct().ToArray() };
    }

    private static InvalidOperationException Conflict(EmployeeImportRow row, string message) => new($"Trang “{row.SheetName}”, dòng {row.RowNumber}: {message} Chưa có thay đổi nào được lưu.");
    private static string PersonName(string value) => string.Join(' ', value.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string? Field(string header) => HeaderFields.GetValueOrDefault(HeaderName(header));
    private static string HeaderName(string value)
    {
        var output = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) output.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
            else output.Append(' ');
        }
        return string.Join(' ', output.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Label(string field) => field switch
    {
        nameof(Employee.Code) => "Mã nhân viên", nameof(Employee.Name) => "Họ và tên", nameof(Employee.Department) => "Khoa / phòng",
        nameof(Employee.Title) => "Chức danh", nameof(Employee.Degree) => "Trình độ", nameof(Employee.StartDate) => "Ngày vào làm",
        nameof(Employee.SocialInsuranceNumber) => "Mã số BHXH", nameof(Employee.DateOfBirth) => "Ngày sinh",
        nameof(Employee.PracticeLicenseNumber) => "Số giấy phép hành nghề", nameof(Employee.PracticeLicenseDate) => "Ngày cấp giấy phép",
        nameof(Employee.ProfessionalCertificates) => "Chứng chỉ chuyên môn", nameof(Employee.ConferenceHistory) => "Lịch sử hội nghị", _ => "SHKHKT"
    };

    private static Dictionary<string, string> BuildHeaderFields()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string field, params string[] names) { foreach (var name in names) result[HeaderName(name)] = field; }
        Add(nameof(Employee.Code), "Mã NV", "Mã nhân viên", "Code", "Employee code");
        Add(nameof(Employee.Name), "Họ và tên", "Họ tên", "Tên nhân viên", "Name", "Full name");
        Add(nameof(Employee.Department), "Khoa / Phòng", "Khoa phòng", "Khoa", "Phòng ban", "Department");
        Add(nameof(Employee.Title), "Chức danh / Chức vụ", "Chức danh công việc", "Chức danh", "Chức vụ", "Title");
        Add(nameof(Employee.Degree), "Trình độ", "Trình độ chuyên môn", "Degree");
        Add(nameof(Employee.StartDate), "Ngày vào làm", "Ngày bắt đầu công tác", "Start date");
        Add(nameof(Employee.SocialInsuranceNumber), "Mã số BHXH", "Mã BHXH", "BHXH", "Số BHXH", "Social insurance number");
        Add(nameof(Employee.DateOfBirth), "Ngày sinh", "Date of birth", "DOB");
        Add(nameof(Employee.PracticeLicenseNumber), "Giấy phép hành nghề số", "Số giấy phép hành nghề", "Số giấy phép", "Số CCHN", "Số chứng chỉ hành nghề", "Practice license number");
        Add(nameof(Employee.PracticeLicenseDate), "Ngày cấp giấy phép hành nghề", "Ngày cấp giấy phép", "Ngày cấp CCHN", "Ngày cấp chứng chỉ hành nghề", "Practice license date");
        Add(nameof(Employee.ProfessionalCertificates), "Các chứng chỉ chuyên môn", "Chứng chỉ chuyên môn", "Professional certificates");
        Add(nameof(Employee.ConferenceHistory), "Hội thảo/ Tập huấn/ hội nghị", "Hội thảo / Tập huấn / Hội nghị", "Lịch sử hội nghị", "Hội thảo tập huấn hội nghị", "Conference history");
        Add(nameof(Employee.ScientificActivityHistory), "SHKHKT", "Sinh hoạt khoa học", "Lịch sử SHKHKT", "Scientific activity history");
        return result;
    }
}
