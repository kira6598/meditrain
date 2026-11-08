namespace QuanLyDaoTao.Core;

public sealed record EmployeeImportRow
{
    public int RowNumber { get; init; }
    public string SheetName { get; init; } = "";
    public Employee Employee { get; init; } = new();
    public IReadOnlyList<string> PresentFields { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
}

public sealed record EmployeeImportPreview
{
    public string FileName { get; init; } = "";
    public IReadOnlyList<EmployeeImportRow> Rows { get; init; } = [];
    public IReadOnlyList<TrainingSupplementImportRow> SupplementRows { get; init; } = [];
}

public sealed record TrainingSupplementImportRow
{
    public int RowNumber { get; init; }
    public string SheetName { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string EmployeeCode { get; init; } = "";
    public string SocialInsuranceNumber { get; init; } = "";
    public DateTime? DateOfBirth { get; init; }
    public string Title { get; init; } = "";
    public string Type { get; init; } = "Chứng chỉ";
    public DateTime? IssuedDate { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string StudyPeriod { get; init; } = "";
    public decimal? Hours { get; init; }
    public string Provider { get; init; } = "";
    public string Notes { get; init; } = "";
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
}

public enum ExistingEmployeeImportMode { Skip, UpdateNonEmpty }

public sealed record EmployeeImportResult
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public int SupplementsAdded { get; init; }
    public int SupplementsUpdated { get; init; }
    public int SupplementsSkipped { get; init; }
}
