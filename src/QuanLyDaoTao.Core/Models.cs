using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuanLyDaoTao.Core;

public sealed record Employee
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public string Title { get; set; } = "";
    public string Degree { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public string SocialInsuranceNumber { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string PracticeLicenseNumber { get; set; } = "";
    public DateTime? PracticeLicenseDate { get; set; }
    public bool HasPracticeLicense
    {
        get
        {
            // Imported cells may contain status notes instead of an issued license number.
            if (string.IsNullOrWhiteSpace(PracticeLicenseNumber) || !PracticeLicenseNumber.Any(char.IsDigit)) return false;
            var text = new string(PracticeLicenseNumber.Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .Select(character => character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character)).ToArray());
            text = Regex.Replace(text, @"[^\p{L}\p{N}]+", " ").Trim();
            text = Regex.Replace(text, @"^(?:so )?(?:cchn|gphn|chung chi hanh nghe|giay phep hanh nghe)(?: so)?\b *", "");
            // Dates in a waiting/missing-status note do not constitute a license number.
            return !Regex.IsMatch(text, @"^(?:(?:dang|hien|van) )*(?:cho|chua|khong|ko|k co|n a|none|null|pending|waiting)(?: |$)");
        }
    }
    public string ProfessionalCertificates { get; set; } = "";
    public string ConferenceHistory { get; set; } = "";
    public string ScientificActivityHistory { get; set; } = "";
    public string DisplayName => $"{Code} · {Name}";
}

public sealed record TrainingActivity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EmployeeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "SHKHKT";
    public string Provider { get; set; } = "";
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
    public DateTime? IssuedDate { get; set; }
    public string StudyPeriod { get; set; } = "";
    public decimal Hours { get; set; }
    public string CertificateNumber { get; set; } = "";
    public string Notes { get; set; } = "";
    public string SourceReference { get; set; } = "";
}

/// <summary>Imported certificate details awaiting confirmation of credited training.</summary>
public sealed record TrainingSupplement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EmployeeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "Chứng chỉ";
    public DateTime? IssuedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? Hours { get; set; }
    public string Provider { get; set; } = "";
    public string StudyPeriod { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public string LinkedSourceReference { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public int SourceRow { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record TrainingReportRow
{
    public Employee Employee { get; init; } = new();
    public string Code => Employee.Code;
    public string Name => Employee.Name;
    public string Department => Employee.Department;
    public string Degree => Employee.Degree;
    public decimal FirstYearHours { get; init; }
    public decimal SecondYearHours { get; init; }
    public decimal ShkhktHours { get; init; }
    public decimal CmeHours { get; init; }
    public decimal TotalHours { get; init; }
    public decimal TargetHours { get; init; }
    public decimal MissingHours => Math.Max(0, TargetHours - TotalHours);
    public bool Passed => TotalHours >= TargetHours;
    public string Status => Passed ? "Đạt" : "Chưa đạt";
}

public sealed record DepartmentReportRow
{
    public string Department { get; init; } = "";
    public int EmployeeCount { get; init; }
    public int AnnualPassed { get; init; }
    public int BiennialPassed { get; init; }
    public decimal AnnualRate => EmployeeCount == 0 ? 0 : Math.Round(100m * AnnualPassed / EmployeeCount, 1);
    public decimal BiennialRate => EmployeeCount == 0 ? 0 : Math.Round(100m * BiennialPassed / EmployeeCount, 1);
}
