using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyDaoTao.Core;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
string? Option(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
var database = Option("--database");
var input = Option("--preview") ?? Option("--apply");
if (input is null)
{
    if (database is null || !File.Exists(database)) throw new ArgumentException("Provide --preview/--apply workbook, or --database existing.db for a read-only audit.");
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(database), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    connection.Open();
    object? Query(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    var hasPracticeLicenseColumn = Convert.ToInt32(Query("SELECT COUNT(*) FROM pragma_table_info('Employees') WHERE name='PracticeLicenseNumber'")) > 0;
    var employees = new List<Employee>();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = hasPracticeLicenseColumn ? "SELECT PracticeLicenseNumber FROM Employees" : "SELECT '' FROM Employees";
        using var reader = command.ExecuteReader();
        while (reader.Read()) employees.Add(new Employee { PracticeLicenseNumber = reader.IsDBNull(0) ? "" : reader.GetString(0) });
    }
    // Only unlicensed status text is reported; employee names and actual license numbers stay private.
    var unlicensedStatuses = employees.Where(employee => !employee.HasPracticeLicense)
        .GroupBy(employee => string.IsNullOrWhiteSpace(employee.PracticeLicenseNumber) ? "(trống)"
            : employee.PracticeLicenseNumber.Any(char.IsDigit) ? "(giá trị chưa được nhận diện)"
            : employee.PracticeLicenseNumber.Trim())
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new { SourceText = group.Key, Employees = group.Count() }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Database = Path.GetFullPath(database), Schema = Query("PRAGMA user_version"), Employees = employees.Count,
        HasPracticeLicenseColumn = hasPracticeLicenseColumn,
        LicensedEmployees = employees.Count(employee => employee.HasPracticeLicense),
        UnlicensedEmployees = employees.Count(employee => !employee.HasPracticeLicense), UnlicensedStatuses = unlicensedStatuses,
        Activities = Query("SELECT COUNT(*) FROM TrainingActivities"), HasSupplements = Query("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TrainingSupplements'")
    }));
    if (Option("--backup") is { } target)
    {
        new TrainingRepository(database).Backup(target);
        Console.WriteLine($"BACKUP {Path.GetFullPath(target)}");
    }
    return;
}
var hash = SHA256.HashData(File.ReadAllBytes(input));
var preview = EmployeeImportService.Read(input);
Console.WriteLine(JsonSerializer.Serialize(new
{
    EmployeeRows = preview.Rows.Count, SupplementRows = preview.SupplementRows.Count,
    SupplementPeople = preview.SupplementRows.Select(row => row.EmployeeName).Distinct().Count(),
    EmployeeErrors = preview.Rows.Count(row => !row.IsValid), SupplementErrors = preview.SupplementRows.Count(row => !row.IsValid),
    KnownHours = preview.SupplementRows.Count(row => row.Hours is not null), TotalSourceHours = preview.SupplementRows.Sum(row => row.Hours ?? 0),
    KnownIssueDates = preview.SupplementRows.Count(row => row.IssuedDate is not null), KnownStudyDates = preview.SupplementRows.Count(row => row.StartDate is not null),
    Providers = preview.SupplementRows.Count(row => !string.IsNullOrWhiteSpace(row.Provider))
}));
foreach (var row in preview.Rows.Where(row => !row.IsValid)) Console.WriteLine($"ERROR {row.SheetName}:{row.RowNumber}: {string.Join("; ", row.Errors)}");
foreach (var row in preview.SupplementRows.Where(row => !row.IsValid)) Console.WriteLine($"ERROR {row.SheetName}:{row.RowNumber}: {string.Join("; ", row.Errors)}");
foreach (var row in preview.SupplementRows.Where(row => row.Warnings.Any(warning => warning.Contains("sớm hơn")))) Console.WriteLine($"REVIEW {row.SheetName}:{row.RowNumber}: {string.Join("; ", row.Warnings)}");
if (preview.Rows.Any(row => !row.IsValid) || preview.SupplementRows.Any(row => !row.IsValid)) throw new InvalidOperationException("Import contains validation errors; no database changed.");
if (Option("--apply") is not null)
{
    if (database is null) throw new ArgumentException("--apply requires explicit --database path.");
    var repository = new TrainingRepository(database);
    if (File.Exists(repository.DatabasePath))
    {
        var backup = Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, $"backup-before-supplement-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
        repository.Backup(backup);
        Console.WriteLine($"BACKUP {backup}");
    }
    repository.Initialize();
    var before = repository.GetActivities().OrderBy(item => item.Id).ToArray();
    var employeeIds = repository.GetEmployees().Select(item => item.Id).ToHashSet();
    var profiles = repository.GetEmployees().ToDictionary(item => item.Id);
    var result = EmployeeImportService.Apply(repository, preview, ExistingEmployeeImportMode.Skip);
    var after = repository.GetActivities().OrderBy(item => item.Id).ToArray();
    if (!before.SequenceEqual(after)) throw new InvalidOperationException("Confirmed training records changed unexpectedly.");
    var saved = repository.GetEmployees();
    if (!employeeIds.IsSubsetOf(saved.Select(item => item.Id))) throw new InvalidOperationException("Existing employee IDs were lost.");
    if (saved.Where(item => profiles.ContainsKey(item.Id)).Any(item => item != profiles[item.Id])) throw new InvalidOperationException("Existing employee profiles changed unexpectedly.");
    var supplements = repository.GetSupplements();
    var rows = saved.SelectMany(employee => EmployeeTrainingHistory.Build(employee, after, supplements)).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { Result = result, SavedEmployees = saved.Count, SavedSupplements = supplements.Count, UnifiedHistoryRows = rows.Length, ConfirmedActivities = after.Length, SourceUnchanged = hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))) }));
}
if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input)))) throw new InvalidOperationException("Source file changed.");
