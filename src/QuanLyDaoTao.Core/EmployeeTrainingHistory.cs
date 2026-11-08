using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuanLyDaoTao.Core;

/// <summary>A display row; incomplete Excel notes never become credited training automatically.</summary>
public sealed record EmployeeTrainingHistoryRow
{
    public string Id { get; init; } = "";
    public TrainingActivity? Activity { get; init; }
    public TrainingSupplement? Supplement { get; init; }
    public bool IsImported => Activity is null;
    public string SourceReference { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string SourceCategory { get; init; } = "";
    public string Title { get; init; } = "";
    public string Type { get; init; } = "";
    public string Provider { get; init; } = "";
    public string CertificateNumber { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime? IssuedDate { get; init; }
    public string StudyPeriod { get; init; } = "";
    public decimal? Hours { get; init; }
}

public static class EmployeeTrainingHistory
{
    private static readonly Regex Bullet = new(@"^\s*(?:[-•*–—]\s*)+", RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex Date = new(@"(?<![\d/])\d{1,2}/\d{1,2}/\d{4}(?![\d/])", RegexOptions.CultureInvariant);
    private static readonly Regex Hours = new(@"(?<![\d.,+-])(?<hours>[+-]?\d+(?:[.,]\d+)?)\s*tiết\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Cme = new(@"\bCME\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<EmployeeTrainingHistoryRow> Build(Employee employee, IEnumerable<TrainingActivity> activities,
        IEnumerable<TrainingSupplement>? supplements = null)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(activities);
        var ownActivities = activities.Where(activity => activity.EmployeeId == employee.Id).ToArray();
        var ownSupplements = (supplements ?? []).Where(supplement => supplement.EmployeeId == employee.Id).ToArray();
        var references = ownActivities.Where(activity => !string.IsNullOrWhiteSpace(activity.SourceReference))
            .Select(activity => activity.SourceReference.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var supplement in ownSupplements)
        {
            if (!string.IsNullOrWhiteSpace(supplement.LinkedSourceReference)) references.Add(supplement.LinkedSourceReference.Trim());
        }
        var rows = new List<EmployeeTrainingHistoryRow>();
        foreach (var activity in ownActivities)
        {
            var matching = ownSupplements.Where(supplement => Matches(activity, supplement))
                .OrderByDescending(supplement => activity.SourceReference == supplement.SourceReference)
                .ThenBy(supplement => supplement.SourceRow).ThenBy(supplement => supplement.Id, StringComparer.Ordinal).ToArray();
            var supplement = matching.FirstOrDefault();
            rows.Add(new EmployeeTrainingHistoryRow
            {
                Id = activity.Id, Activity = activity, Supplement = supplement, SourceReference = activity.SourceReference,
                SourceText = supplement is null ? "" : DescribeSource(supplement), SourceCategory = supplement?.Type ?? "",
                Title = activity.Title, Type = activity.Type,
                Provider = string.IsNullOrWhiteSpace(activity.Provider) ? matching.Select(detail => detail.Provider).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "" : activity.Provider,
                CertificateNumber = activity.CertificateNumber, Notes = activity.Notes,
                StartDate = activity.StartDate, EndDate = activity.EndDate, Hours = activity.Hours,
                IssuedDate = activity.IssuedDate ?? matching.Select(detail => detail.IssuedDate).FirstOrDefault(value => value is not null),
                StudyPeriod = string.IsNullOrWhiteSpace(activity.StudyPeriod) ? matching.Select(detail => detail.StudyPeriod).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "" : activity.StudyPeriod
            });
        }
        foreach (var supplement in ownSupplements)
        {
            if (ownActivities.Any(activity => Matches(activity, supplement))) continue;
            rows.Add(new EmployeeTrainingHistoryRow
            {
                Id = supplement.Id, Supplement = supplement, SourceReference = supplement.SourceReference,
                SourceText = DescribeSource(supplement), SourceCategory = supplement.Type,
                Title = supplement.Title, Type = supplement.Type, Provider = supplement.Provider,
                Notes = supplement.Notes, StartDate = supplement.StartDate, EndDate = supplement.EndDate,
                IssuedDate = supplement.IssuedDate, StudyPeriod = supplement.StudyPeriod, Hours = supplement.Hours
            });
        }
        AddSource(nameof(Employee.ProfessionalCertificates), "Chứng chỉ", employee.ProfessionalCertificates);
        AddSource(nameof(Employee.ConferenceHistory), "Hội thảo", employee.ConferenceHistory);
        AddSource(nameof(Employee.ScientificActivityHistory), "SHKHKT", employee.ScientificActivityHistory);
        return rows.OrderByDescending(row => row.EndDate).ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();

        void AddSource(string field, string category, string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var originalLine in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var text = CleanSourceLine(originalLine);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var sourceKey = SourceReferenceFor(employee.Id, field, originalLine)!;
                if (!seen.Add(sourceKey) || references.Contains(sourceKey)) continue;

                DateTime? date = null;
                var title = text;
                var dates = Date.Matches(text);
                if (dates.Count == 1 && DateTime.TryParseExact(dates[0].Value, ["d/M/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    date = parsedDate;
                    // Only remove a date suffix; embedded dates may be part of the course's name.
                    var suffix = text[(dates[0].Index + dates[0].Length)..].Trim();
                    if (suffix is "" or ")")
                    {
                        var candidate = text[..dates[0].Index].TrimEnd();
                        if (suffix == ")" && candidate.EndsWith('(')) candidate = candidate[..^1].TrimEnd();
                        candidate = candidate.TrimEnd(' ', '-', '–', '—', ':');
                        if (!string.IsNullOrWhiteSpace(candidate)) title = candidate;
                    }
                }
                decimal? hours = null;
                var hourMatches = Hours.Matches(text);
                if (hourMatches.Count == 1 && decimal.TryParse(hourMatches[0].Groups["hours"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var parsedHours) && parsedHours > 0)
                    hours = parsedHours;
                rows.Add(new EmployeeTrainingHistoryRow
                {
                    Id = sourceKey, SourceReference = sourceKey, SourceText = originalLine, SourceCategory = category,
                    Title = title, Type = Cme.IsMatch(text) ? "CME" : category,
                    StartDate = date, EndDate = date, Hours = hours
                });
            }
        }
    }

    /// <summary>Removes only source lines whose existing stable references have been selected.</summary>
    internal static Employee RemoveSourceRows(Employee employee, IReadOnlySet<string> sourceReferences)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        return employee with
        {
            ProfessionalCertificates = Remove(nameof(Employee.ProfessionalCertificates), employee.ProfessionalCertificates),
            ConferenceHistory = Remove(nameof(Employee.ConferenceHistory), employee.ConferenceHistory),
            ScientificActivityHistory = Remove(nameof(Employee.ScientificActivityHistory), employee.ScientificActivityHistory)
        };

        string Remove(string field, string? source)
        {
            if (string.IsNullOrEmpty(source) || sourceReferences.Count == 0) return source ?? "";
            var result = new StringBuilder(source.Length);
            var position = 0;
            while (position < source.Length)
            {
                var start = position;
                while (position < source.Length && source[position] is not ('\r' or '\n')) position++;
                var line = source[start..position];
                if (position < source.Length)
                {
                    if (source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n') position += 2;
                    else position++;
                }
                var reference = SourceReferenceFor(employee.Id, field, line);
                if (reference is null || !sourceReferences.Contains(reference)) result.Append(source, start, position - start);
            }
            return result.ToString();
        }
    }

    private static string CleanSourceLine(string originalLine) => Bullet.Replace(originalLine, "").Trim();

    private static string? SourceReferenceFor(string employeeId, string field, string originalLine)
    {
        var text = CleanSourceLine(originalLine);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = Space.Replace(text, " ").Normalize(NormalizationForm.FormC);
        return "excel:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(employeeId + "\n" + field + "\n" + normalized)));
    }

    private static bool Matches(TrainingActivity activity, TrainingSupplement supplement)
        => !string.IsNullOrWhiteSpace(activity.SourceReference)
            && (activity.SourceReference == supplement.SourceReference
                || (!string.IsNullOrWhiteSpace(supplement.LinkedSourceReference) && activity.SourceReference == supplement.LinkedSourceReference));

    private static string DescribeSource(TrainingSupplement supplement)
    {
        var location = string.IsNullOrWhiteSpace(supplement.SourceSheet) ? "Excel" : supplement.SourceSheet;
        if (supplement.SourceRow > 0) location += $" · Dòng {supplement.SourceRow}";
        return string.IsNullOrWhiteSpace(supplement.Notes) ? location : location + Environment.NewLine + supplement.Notes;
    }
}
