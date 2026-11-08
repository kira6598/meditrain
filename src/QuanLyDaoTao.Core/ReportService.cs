namespace QuanLyDaoTao.Core;

/// <summary>Targets and start-date allocation follow the supplied draft, independently for each report.</summary>
public static class ReportService
{
    public const decimal AnnualTarget = 12m;
    public const decimal BiennialTarget = 48m;

    public static IReadOnlyList<TrainingReportRow> BuildAnnual(
        IEnumerable<Employee> employees, IEnumerable<TrainingActivity> activities, int year)
    {
        ValidateYear(year);
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(activities);
        var byEmployee = activities.Where(a => a.StartDate.Year == year).ToLookup(a => a.EmployeeId);
        return employees.Select(employee =>
        {
            var courses = byEmployee[employee.Id].ToArray();
            var shkhkt = courses.Where(a => a.Type == "SHKHKT").Sum(a => a.Hours);
            var cme = courses.Where(a => a.Type == "CME").Sum(a => a.Hours);
            return new TrainingReportRow
            {
                Employee = employee, ShkhktHours = shkhkt, CmeHours = cme,
                FirstYearHours = shkhkt + cme, TotalHours = shkhkt + cme, TargetHours = AnnualTarget
            };
        }).ToArray();
    }

    public static IReadOnlyList<TrainingReportRow> BuildBiennial(
        IEnumerable<Employee> employees, IEnumerable<TrainingActivity> activities, int startYear)
    {
        ValidateYear(startYear, true);
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(activities);
        var byEmployee = activities.Where(a => a.StartDate.Year == startYear || a.StartDate.Year == startYear + 1)
            .ToLookup(a => a.EmployeeId);
        return employees.Select(employee =>
        {
            var courses = byEmployee[employee.Id].ToArray();
            var first = courses.Where(a => a.StartDate.Year == startYear).Sum(a => a.Hours);
            var second = courses.Where(a => a.StartDate.Year == startYear + 1).Sum(a => a.Hours);
            return new TrainingReportRow
            {
                Employee = employee, FirstYearHours = first, SecondYearHours = second,
                ShkhktHours = courses.Where(a => a.Type == "SHKHKT").Sum(a => a.Hours),
                CmeHours = courses.Where(a => a.Type == "CME").Sum(a => a.Hours),
                TotalHours = first + second, TargetHours = BiennialTarget
            };
        }).ToArray();
    }

    public static IReadOnlyList<DepartmentReportRow> BuildDepartments(
        IEnumerable<Employee> employees, IEnumerable<TrainingActivity> activities, int startYear, int annualYear)
    {
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(activities);
        var employeeList = employees.ToArray();
        var activityList = activities.ToArray();
        var annual = BuildAnnual(employeeList, activityList, annualYear).ToDictionary(r => r.Employee.Id);
        var biennial = BuildBiennial(employeeList, activityList, startYear).ToDictionary(r => r.Employee.Id);
        return employeeList.GroupBy(e => e.Department).OrderBy(g => g.Key).Select(group => new DepartmentReportRow
        {
            Department = group.Key, EmployeeCount = group.Count(),
            AnnualPassed = group.Count(e => annual[e.Id].Passed),
            BiennialPassed = group.Count(e => biennial[e.Id].Passed)
        }).ToArray();
    }

    private static void ValidateYear(int year, bool pair = false)
    {
        if (year < 1 || year > (pair ? 9998 : 9999))
            throw new ArgumentOutOfRangeException(nameof(year), "Năm báo cáo không hợp lệ.");
    }
}
