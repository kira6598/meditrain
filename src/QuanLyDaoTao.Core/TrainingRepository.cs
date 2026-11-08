using System.Globalization;
using Microsoft.Data.Sqlite;

namespace QuanLyDaoTao.Core;

/// <summary>A small local SQLite store. Every operation uses its own connection.</summary>
public sealed class TrainingRepository
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly string _connectionString;
    public string DatabasePath { get; }

    public TrainingRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            DefaultTimeout = 10,
            Pooling = false
        }.ToString();
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) > 4)
            throw new InvalidOperationException("Dữ liệu được tạo bởi phiên bản mới hơn. Vui lòng cập nhật ứng dụng.");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Employees (
                Id TEXT NOT NULL PRIMARY KEY,
                Code TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(Code)) > 0),
                Name TEXT NOT NULL CHECK(length(trim(Name)) > 0),
                Department TEXT NOT NULL CHECK(length(trim(Department)) > 0),
                Title TEXT NOT NULL DEFAULT '',
                Degree TEXT NOT NULL DEFAULT '',
                StartDate TEXT NULL,
                SocialInsuranceNumber TEXT NOT NULL DEFAULT '',
                DateOfBirth TEXT NULL,
                PracticeLicenseNumber TEXT NOT NULL DEFAULT '',
                PracticeLicenseDate TEXT NULL,
                ProfessionalCertificates TEXT NOT NULL DEFAULT '',
                ConferenceHistory TEXT NOT NULL DEFAULT '',
                ScientificActivityHistory TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS TrainingActivities (
                Id TEXT NOT NULL PRIMARY KEY,
                EmployeeId TEXT NOT NULL REFERENCES Employees(Id) ON DELETE CASCADE,
                Title TEXT NOT NULL CHECK(length(trim(Title)) > 0),
                Type TEXT NOT NULL CHECK(Type IN ('SHKHKT','CME')),
                Provider TEXT NOT NULL CHECK(length(trim(Provider)) > 0),
                StartDate TEXT NOT NULL,
                EndDate TEXT NOT NULL CHECK(EndDate >= StartDate),
                IssuedDate TEXT NULL,
                StudyPeriod TEXT NOT NULL DEFAULT '',
                Hours TEXT NOT NULL CHECK(CAST(Hours AS REAL) > 0),
                CertificateNumber TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                SourceReference TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS TrainingSupplements (
                Id TEXT NOT NULL PRIMARY KEY,
                EmployeeId TEXT NOT NULL REFERENCES Employees(Id) ON DELETE CASCADE,
                Title TEXT NOT NULL CHECK(length(trim(Title)) > 0),
                Type TEXT NOT NULL DEFAULT 'Chứng chỉ',
                IssuedDate TEXT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL CHECK(StartDate IS NULL OR EndDate IS NULL OR EndDate >= StartDate),
                Hours TEXT NULL CHECK(Hours IS NULL OR CAST(Hours AS REAL) > 0),
                Provider TEXT NOT NULL DEFAULT '',
                StudyPeriod TEXT NOT NULL DEFAULT '',
                SourceReference TEXT NOT NULL CHECK(length(trim(SourceReference)) > 0),
                LinkedSourceReference TEXT NOT NULL DEFAULT '',
                SourceSheet TEXT NOT NULL DEFAULT '',
                SourceRow INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS IX_TrainingActivities_EmployeeDate ON TrainingActivities(EmployeeId, StartDate);
            CREATE INDEX IF NOT EXISTS IX_Employees_Department ON Employees(Department);
            """;
        command.ExecuteNonQuery();
        // Version 1 databases are upgraded in place: IDs and all training references stay intact.
        command.CommandText = "PRAGMA table_info(Employees);";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) columns.Add(reader.GetString(1));
        foreach (var (name, definition) in new[]
        {
            ("SocialInsuranceNumber", "TEXT NOT NULL DEFAULT ''"), ("DateOfBirth", "TEXT NULL"),
            ("PracticeLicenseNumber", "TEXT NOT NULL DEFAULT ''"), ("PracticeLicenseDate", "TEXT NULL"),
            ("ProfessionalCertificates", "TEXT NOT NULL DEFAULT ''"), ("ConferenceHistory", "TEXT NOT NULL DEFAULT ''"),
            ("ScientificActivityHistory", "TEXT NOT NULL DEFAULT ''")
        })
        {
            if (columns.Contains(name)) continue;
            command.CommandText = $"ALTER TABLE Employees ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }
        command.CommandText = "PRAGMA table_info(TrainingActivities);";
        var activityColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) activityColumns.Add(reader.GetString(1));
        foreach (var (name, definition) in new[]
        {
            ("SourceReference", "TEXT NOT NULL DEFAULT ''"), ("IssuedDate", "TEXT NULL"), ("StudyPeriod", "TEXT NOT NULL DEFAULT ''")
        })
        {
            if (activityColumns.Contains(name)) continue;
            command.CommandText = $"ALTER TABLE TrainingActivities ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_TrainingActivities_SourceReference
                ON TrainingActivities(EmployeeId, SourceReference) WHERE SourceReference <> '';
            CREATE UNIQUE INDEX IF NOT EXISTS IX_TrainingSupplements_SourceReference
                ON TrainingSupplements(EmployeeId, SourceReference) WHERE SourceReference <> '';
            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<Employee> GetEmployees()
    {
        using var connection = Open();
        return GetEmployees(connection, null);
    }

    private static IReadOnlyList<Employee> GetEmployees(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, Code, Name, Department, Title, Degree, StartDate, SocialInsuranceNumber, DateOfBirth,
                PracticeLicenseNumber, PracticeLicenseDate, ProfessionalCertificates, ConferenceHistory, ScientificActivityHistory
            FROM Employees ORDER BY Code COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<Employee>();
        while (reader.Read())
            result.Add(new Employee
            {
                Id = reader.GetString(0), Code = reader.GetString(1), Name = reader.GetString(2),
                Department = reader.GetString(3), Title = reader.GetString(4), Degree = reader.GetString(5),
                StartDate = reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                SocialInsuranceNumber = reader.GetString(7), DateOfBirth = reader.IsDBNull(8) ? null : ParseDate(reader.GetString(8)),
                PracticeLicenseNumber = reader.GetString(9), PracticeLicenseDate = reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)),
                ProfessionalCertificates = reader.GetString(11), ConferenceHistory = reader.GetString(12), ScientificActivityHistory = reader.GetString(13)
            });
        return result;
    }

    public IReadOnlyList<TrainingActivity> GetActivities()
    {
        using var connection = Open();
        return GetActivities(connection, null);
    }

    private static IReadOnlyList<TrainingActivity> GetActivities(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, EmployeeId, Title, Type, Provider, StartDate, EndDate, Hours, CertificateNumber, Notes, SourceReference, IssuedDate, StudyPeriod
            FROM TrainingActivities ORDER BY StartDate DESC, Title;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<TrainingActivity>();
        while (reader.Read())
            result.Add(new TrainingActivity
            {
                Id = reader.GetString(0), EmployeeId = reader.GetString(1), Title = reader.GetString(2),
                Type = reader.GetString(3), Provider = reader.GetString(4), StartDate = ParseDate(reader.GetString(5)),
                EndDate = ParseDate(reader.GetString(6)), Hours = decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                CertificateNumber = reader.GetString(8), Notes = reader.GetString(9), SourceReference = reader.GetString(10),
                IssuedDate = reader.IsDBNull(11) ? null : ParseDate(reader.GetString(11)), StudyPeriod = reader.GetString(12)
            });
        return result;
    }

    public IReadOnlyList<TrainingSupplement> GetSupplements()
    {
        using var connection = Open();
        return GetSupplements(connection, null);
    }

    private static IReadOnlyList<TrainingSupplement> GetSupplements(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, EmployeeId, Title, Type, IssuedDate, StartDate, EndDate, Hours, Provider, StudyPeriod,
                SourceReference, LinkedSourceReference, SourceSheet, SourceRow, Notes
            FROM TrainingSupplements ORDER BY EndDate DESC, IssuedDate DESC, Title, Id;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<TrainingSupplement>();
        while (reader.Read())
            result.Add(new TrainingSupplement
            {
                Id = reader.GetString(0), EmployeeId = reader.GetString(1), Title = reader.GetString(2), Type = reader.GetString(3),
                IssuedDate = reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
                StartDate = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
                EndDate = reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
                Hours = reader.IsDBNull(7) ? null : decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                Provider = reader.GetString(8), StudyPeriod = reader.GetString(9), SourceReference = reader.GetString(10),
                LinkedSourceReference = reader.GetString(11), SourceSheet = reader.GetString(12), SourceRow = reader.GetInt32(13), Notes = reader.GetString(14)
            });
        return result;
    }

    public void SaveEmployee(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ValidateEmployee(employee);
        using var connection = Open();
        try { SaveEmployee(connection, null, employee); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        { throw new InvalidOperationException("Mã nhân viên đã tồn tại. Vui lòng sử dụng mã khác.", exception); }
    }

    public void DeleteEmployee(string id)
    {
        Require(id, "Mã định danh nhân viên");
        Delete("DELETE FROM Employees WHERE Id = $id;", id);
    }

    /// <summary>Writes one complete, validated import as an atomic transaction.</summary>
    public void ImportEmployees(IEnumerable<Employee> employees)
        => ImportData(employees, []);

    /// <summary>Atomically imports assigned employee identities and their detailed certificate rows.</summary>
    public void ImportData(IEnumerable<Employee> employees, IEnumerable<TrainingSupplement> supplements)
    {
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(supplements);
        var rows = employees.ToArray();
        var details = supplements.ToArray();
        foreach (var employee in rows) { ArgumentNullException.ThrowIfNull(employee); ValidateEmployee(employee); }
        foreach (var supplement in details) { ArgumentNullException.ThrowIfNull(supplement); ValidateSupplement(supplement); }
        if (rows.Select(e => e.Id.Trim()).Distinct(StringComparer.Ordinal).Count() != rows.Length
            || rows.Select(e => e.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length)
            throw new ArgumentException("Danh sách nhập có mã hoặc định danh nhân viên bị trùng.");
        if (details.Select(row => row.Id.Trim()).Distinct(StringComparer.Ordinal).Count() != details.Length
            || details.Select(row => (row.EmployeeId.Trim(), row.SourceReference.Trim())).Distinct().Count() != details.Length)
            throw new ArgumentException("Danh sách chứng chỉ nhập có định danh hoặc dòng nguồn bị trùng.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var employee in rows) SaveEmployee(connection, transaction, employee);
            foreach (var supplement in details) SaveSupplement(connection, transaction, supplement);
            transaction.Commit();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        { throw new InvalidOperationException("Không nhập được dữ liệu do mã nhân viên bị trùng hoặc thông tin không hợp lệ. Chưa có thay đổi nào được lưu.", exception); }
    }

    public void SaveSupplement(TrainingSupplement supplement)
    {
        ArgumentNullException.ThrowIfNull(supplement);
        ValidateSupplement(supplement);
        using var connection = Open();
        try { SaveSupplement(connection, null, supplement); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        { throw new InvalidOperationException("Không lưu được chứng chỉ bổ sung. Nhân viên không tồn tại hoặc thông tin không hợp lệ.", exception); }
    }

    public void SaveActivity(TrainingActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ValidateActivity(activity);
        using var connection = Open();
        try { SaveActivity(connection, null, activity); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        { throw new InvalidOperationException("Không lưu được hồ sơ. Nhân viên không tồn tại hoặc thông tin không hợp lệ.", exception); }
    }

    public void DeleteActivity(string id)
    {
        Require(id, "Mã định danh hồ sơ");
        Delete("DELETE FROM TrainingActivities WHERE Id = $id;", id);
    }

    /// <summary>Deletes a visible certificate and its linked source records as one transaction.</summary>
    public bool DeleteTrainingHistoryRow(string employeeId, string rowId)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(rowId)) return false;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var employee = GetEmployees(connection, transaction).FirstOrDefault(person => person.Id == employeeId);
        if (employee is null) return false;
        var activities = GetActivities(connection, transaction).Where(activity => activity.EmployeeId == employee.Id).ToArray();
        var supplements = GetSupplements(connection, transaction).Where(supplement => supplement.EmployeeId == employee.Id).ToArray();
        var selected = EmployeeTrainingHistory.Build(employee, activities, supplements).FirstOrDefault(row => row.Id == rowId);
        if (selected is null) return false;

        var references = new HashSet<string>(StringComparer.Ordinal);
        var activityIds = new HashSet<string>(StringComparer.Ordinal);
        var supplementIds = new HashSet<string>(StringComparer.Ordinal);
        AddReference(selected.SourceReference);
        if (selected.Activity is { } selectedActivity)
        {
            activityIds.Add(selectedActivity.Id);
            AddReference(selectedActivity.SourceReference);
        }
        if (selected.Supplement is { } selectedSupplement)
        {
            supplementIds.Add(selectedSupplement.Id);
            AddReference(selectedSupplement.SourceReference);
            AddReference(selectedSupplement.LinkedSourceReference);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var supplement in supplements)
            {
                if (!supplementIds.Contains(supplement.Id) && !HasReference(supplement.SourceReference) && !HasReference(supplement.LinkedSourceReference)) continue;
                changed |= supplementIds.Add(supplement.Id);
                changed |= AddReference(supplement.SourceReference);
                changed |= AddReference(supplement.LinkedSourceReference);
            }
            foreach (var activity in activities)
            {
                if (!activityIds.Contains(activity.Id) && !HasReference(activity.SourceReference)) continue;
                changed |= activityIds.Add(activity.Id);
                changed |= AddReference(activity.SourceReference);
            }
        } while (changed);

        foreach (var id in activityIds)
            DeleteOwned("DELETE FROM TrainingActivities WHERE EmployeeId = $employee AND Id = $id;", id);
        foreach (var id in supplementIds)
            DeleteOwned("DELETE FROM TrainingSupplements WHERE EmployeeId = $employee AND Id = $id;", id);
        var updated = EmployeeTrainingHistory.RemoveSourceRows(employee, references);
        if (updated.ProfessionalCertificates != employee.ProfessionalCertificates || updated.ConferenceHistory != employee.ConferenceHistory
            || updated.ScientificActivityHistory != employee.ScientificActivityHistory)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Employees SET ProfessionalCertificates = $certificates, ConferenceHistory = $conferences,
                    ScientificActivityHistory = $scientific WHERE Id = $employee;
                """;
            command.Parameters.AddWithValue("$employee", employee.Id);
            command.Parameters.AddWithValue("$certificates", updated.ProfessionalCertificates);
            command.Parameters.AddWithValue("$conferences", updated.ConferenceHistory);
            command.Parameters.AddWithValue("$scientific", updated.ScientificActivityHistory);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;

        bool AddReference(string? reference) => !string.IsNullOrWhiteSpace(reference) && references.Add(reference);
        bool HasReference(string? reference) => !string.IsNullOrWhiteSpace(reference) && references.Contains(reference);
        void DeleteOwned(string sql, string id)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$employee", employee.Id);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Replaces existing data only when the caller explicitly opts in.</summary>
    public void SeedSampleData(bool replaceExisting = false)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Employees) + (SELECT COUNT(*) FROM TrainingActivities);";
        if (!replaceExisting && Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            throw new InvalidOperationException("Cơ sở dữ liệu đã có dữ liệu. Hãy sao lưu trước khi nạp lại dữ liệu mẫu.");
        command.CommandText = "DELETE FROM TrainingActivities; DELETE FROM Employees;";
        command.ExecuteNonQuery();
        foreach (var employee in SampleData.Employees) SaveEmployee(connection, transaction, employee);
        foreach (var activity in SampleData.Activities) SaveActivity(connection, transaction, activity);
        transaction.Commit();
    }

    /// <summary>Uses SQLite's online backup API for a consistent snapshot.</summary>
    public void Backup(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var fullPath = Path.GetFullPath(targetPath);
        if (string.Equals(fullPath, DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tệp sao lưu phải khác tệp dữ liệu đang sử dụng.", nameof(targetPath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var source = Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = fullPath, Pooling = false, DefaultTimeout = 10 }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 10000;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private void Delete(string sql, string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.Trim());
        command.ExecuteNonQuery();
    }

    private static void SaveEmployee(SqliteConnection connection, SqliteTransaction? transaction, Employee employee)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Employees (Id, Code, Name, Department, Title, Degree, StartDate, SocialInsuranceNumber, DateOfBirth,
                PracticeLicenseNumber, PracticeLicenseDate, ProfessionalCertificates, ConferenceHistory, ScientificActivityHistory)
            VALUES ($id, $code, $name, $department, $title, $degree, $date, $insurance, $birth,
                $license, $licenseDate, $certificates, $conferences, $scientific)
            ON CONFLICT(Id) DO UPDATE SET Code=excluded.Code, Name=excluded.Name, Department=excluded.Department,
                Title=excluded.Title, Degree=excluded.Degree, StartDate=excluded.StartDate,
                SocialInsuranceNumber=excluded.SocialInsuranceNumber, DateOfBirth=excluded.DateOfBirth,
                PracticeLicenseNumber=excluded.PracticeLicenseNumber, PracticeLicenseDate=excluded.PracticeLicenseDate,
                ProfessionalCertificates=excluded.ProfessionalCertificates, ConferenceHistory=excluded.ConferenceHistory,
                ScientificActivityHistory=excluded.ScientificActivityHistory;
            """;
        command.Parameters.AddWithValue("$id", employee.Id.Trim());
        command.Parameters.AddWithValue("$code", employee.Code.Trim());
        command.Parameters.AddWithValue("$name", employee.Name.Trim());
        command.Parameters.AddWithValue("$department", employee.Department.Trim());
        command.Parameters.AddWithValue("$title", employee.Title?.Trim() ?? "");
        command.Parameters.AddWithValue("$degree", employee.Degree?.Trim() ?? "");
        command.Parameters.AddWithValue("$date", employee.StartDate is { } date ? FormatDate(date) : DBNull.Value);
        command.Parameters.AddWithValue("$insurance", employee.SocialInsuranceNumber?.Trim() ?? "");
        command.Parameters.AddWithValue("$birth", employee.DateOfBirth is { } birth ? FormatDate(birth) : DBNull.Value);
        command.Parameters.AddWithValue("$license", employee.PracticeLicenseNumber?.Trim() ?? "");
        command.Parameters.AddWithValue("$licenseDate", employee.PracticeLicenseDate is { } licenseDate ? FormatDate(licenseDate) : DBNull.Value);
        command.Parameters.AddWithValue("$certificates", employee.ProfessionalCertificates ?? "");
        command.Parameters.AddWithValue("$conferences", employee.ConferenceHistory ?? "");
        command.Parameters.AddWithValue("$scientific", employee.ScientificActivityHistory ?? "");
        command.ExecuteNonQuery();
    }

    private static void SaveActivity(SqliteConnection connection, SqliteTransaction? transaction, TrainingActivity activity)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO TrainingActivities (Id, EmployeeId, Title, Type, Provider, StartDate, EndDate, Hours, CertificateNumber, Notes, SourceReference, IssuedDate, StudyPeriod)
            VALUES ($id, $employee, $title, $type, $provider, $start, $end, $hours, $certificate, $notes, $source, $issued, $period)
            ON CONFLICT(Id) DO UPDATE SET EmployeeId=excluded.EmployeeId, Title=excluded.Title, Type=excluded.Type,
                Provider=excluded.Provider, StartDate=excluded.StartDate, EndDate=excluded.EndDate, Hours=excluded.Hours,
                CertificateNumber=excluded.CertificateNumber, Notes=excluded.Notes, SourceReference=excluded.SourceReference,
                IssuedDate=excluded.IssuedDate, StudyPeriod=excluded.StudyPeriod;
            """;
        command.Parameters.AddWithValue("$id", activity.Id.Trim());
        command.Parameters.AddWithValue("$employee", activity.EmployeeId.Trim());
        command.Parameters.AddWithValue("$title", activity.Title.Trim());
        command.Parameters.AddWithValue("$type", activity.Type.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$provider", activity.Provider.Trim());
        command.Parameters.AddWithValue("$start", FormatDate(activity.StartDate));
        command.Parameters.AddWithValue("$end", FormatDate(activity.EndDate));
        command.Parameters.AddWithValue("$hours", activity.Hours.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$certificate", activity.CertificateNumber?.Trim() ?? "");
        command.Parameters.AddWithValue("$notes", activity.Notes?.Trim() ?? "");
        command.Parameters.AddWithValue("$source", activity.SourceReference?.Trim() ?? "");
        command.Parameters.AddWithValue("$issued", activity.IssuedDate is { } issued ? FormatDate(issued) : DBNull.Value);
        command.Parameters.AddWithValue("$period", activity.StudyPeriod?.Trim() ?? "");
        command.ExecuteNonQuery();
    }

    private static void SaveSupplement(SqliteConnection connection, SqliteTransaction? transaction, TrainingSupplement supplement)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO TrainingSupplements (Id, EmployeeId, Title, Type, IssuedDate, StartDate, EndDate, Hours, Provider, StudyPeriod,
                SourceReference, LinkedSourceReference, SourceSheet, SourceRow, Notes)
            VALUES ($id, $employee, $title, $type, $issued, $start, $end, $hours, $provider, $period, $source, $linked, $sheet, $row, $notes)
            ON CONFLICT(Id) DO UPDATE SET EmployeeId=excluded.EmployeeId, Title=excluded.Title, Type=excluded.Type,
                IssuedDate=excluded.IssuedDate, StartDate=excluded.StartDate, EndDate=excluded.EndDate, Hours=excluded.Hours,
                Provider=excluded.Provider, StudyPeriod=excluded.StudyPeriod, SourceReference=excluded.SourceReference,
                LinkedSourceReference=excluded.LinkedSourceReference, SourceSheet=excluded.SourceSheet, SourceRow=excluded.SourceRow, Notes=excluded.Notes
            ON CONFLICT(EmployeeId, SourceReference) WHERE SourceReference <> '' DO UPDATE SET Title=excluded.Title, Type=excluded.Type,
                IssuedDate=excluded.IssuedDate, StartDate=excluded.StartDate, EndDate=excluded.EndDate, Hours=excluded.Hours,
                Provider=excluded.Provider, StudyPeriod=excluded.StudyPeriod, LinkedSourceReference=excluded.LinkedSourceReference,
                SourceSheet=excluded.SourceSheet, SourceRow=excluded.SourceRow, Notes=excluded.Notes;
            """;
        command.Parameters.AddWithValue("$id", supplement.Id.Trim());
        command.Parameters.AddWithValue("$employee", supplement.EmployeeId.Trim());
        command.Parameters.AddWithValue("$title", supplement.Title.Trim());
        command.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(supplement.Type) ? "Chứng chỉ" : supplement.Type.Trim());
        command.Parameters.AddWithValue("$issued", supplement.IssuedDate is { } issued ? FormatDate(issued) : DBNull.Value);
        command.Parameters.AddWithValue("$start", supplement.StartDate is { } start ? FormatDate(start) : DBNull.Value);
        command.Parameters.AddWithValue("$end", supplement.EndDate is { } end ? FormatDate(end) : DBNull.Value);
        command.Parameters.AddWithValue("$hours", supplement.Hours is { } hours ? hours.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$provider", supplement.Provider?.Trim() ?? "");
        command.Parameters.AddWithValue("$period", supplement.StudyPeriod?.Trim() ?? "");
        command.Parameters.AddWithValue("$source", supplement.SourceReference.Trim());
        command.Parameters.AddWithValue("$linked", supplement.LinkedSourceReference?.Trim() ?? "");
        command.Parameters.AddWithValue("$sheet", supplement.SourceSheet?.Trim() ?? "");
        command.Parameters.AddWithValue("$row", supplement.SourceRow);
        command.Parameters.AddWithValue("$notes", supplement.Notes ?? "");
        command.ExecuteNonQuery();
    }

    private static void ValidateEmployee(Employee employee)
    {
        Require(employee.Id, "Mã định danh nhân viên");
        Require(employee.Code, "Mã nhân viên");
        Require(employee.Name, "Họ và tên");
        Require(employee.Department, "Khoa / phòng");
    }

    private static void ValidateActivity(TrainingActivity activity)
    {
        Require(activity.Id, "Mã định danh hồ sơ");
        Require(activity.EmployeeId, "Nhân viên");
        Require(activity.Title, "Tên bài học / khóa đào tạo");
        Require(activity.Provider, "Đơn vị tổ chức");
        if (activity.Type?.Trim().ToUpperInvariant() is not ("SHKHKT" or "CME"))
            throw new ArgumentException("Loại đào tạo phải là SHKHKT hoặc CME.");
        if (activity.Hours <= 0) throw new ArgumentException("Số tiết phải lớn hơn 0.");
        if (activity.EndDate.Date < activity.StartDate.Date)
            throw new ArgumentException("Ngày hoàn thành phải bằng hoặc sau ngày bắt đầu.");
    }

    private static void ValidateSupplement(TrainingSupplement supplement)
    {
        Require(supplement.Id, "Mã định danh chứng chỉ");
        Require(supplement.EmployeeId, "Nhân viên");
        Require(supplement.Title, "Tên chứng chỉ / khóa đào tạo");
        Require(supplement.SourceReference, "Dòng nguồn chứng chỉ");
        if (supplement.Hours is <= 0) throw new ArgumentException("Số tiết phải lớn hơn 0 khi đã được ghi nhận.");
        if (supplement.StartDate is { } start && supplement.EndDate is { } end && end.Date < start.Date)
            throw new ArgumentException("Ngày hoàn thành phải bằng hoặc sau ngày bắt đầu.");
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Vui lòng nhập {label.ToLowerInvariant()}.");
    }

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
    private static DateTime ParseDate(string value) => DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
}
