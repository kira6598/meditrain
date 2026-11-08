using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class ReportTests
{
    [Theory]
    [InlineData("11.5", false, "0.5")]
    [InlineData("12", true, "0")]
    [InlineData("12.5", true, "0")]
    public void AnnualThresholdHandlesFractionalHours(string hours, bool passed, string missing)
    {
        var employee = Person();
        var result = Assert.Single(ReportService.BuildAnnual([employee], [Activity(employee.Id, 2026, Parse(hours))], 2026));
        Assert.Equal(passed, result.Passed);
        Assert.Equal(Parse(missing), result.MissingHours);
        Assert.Equal(Parse(hours), result.TotalHours);
    }

    [Theory]
    [InlineData("47.5", false, "0.5")]
    [InlineData("48", true, "0")]
    [InlineData("48.5", true, "0")]
    public void BiennialThresholdIsIndependentFromAnnualMinimum(string hours, bool passed, string missing)
    {
        var employee = Person();
        var result = Assert.Single(ReportService.BuildBiennial([employee], [Activity(employee.Id, 2025, Parse(hours))], 2025));
        Assert.Equal(passed, result.Passed);
        Assert.Equal(Parse(missing), result.MissingHours);
        Assert.Equal(0, result.SecondYearHours);
    }

    [Fact]
    public void CrossYearCoursesBelongWhollyToStartYearAndExcludeOutsideDates()
    {
        var employee = Person();
        var activities = new[]
        {
            Activity(employee.Id, 2024, 100),
            Activity(employee.Id, 2025, 20) with { StartDate = new DateTime(2025, 12, 31), EndDate = new DateTime(2026, 1, 2), Type = "SHKHKT" },
            Activity(employee.Id, 2026, 28),
            Activity(employee.Id, 2027, 100)
        };
        var annual = Assert.Single(ReportService.BuildAnnual([employee], activities, 2026));
        Assert.Equal(28, annual.TotalHours);
        Assert.Equal(0, annual.ShkhktHours);
        Assert.Equal(28, annual.CmeHours);
        var biennial = Assert.Single(ReportService.BuildBiennial([employee], activities, 2025));
        Assert.Equal(20, biennial.FirstYearHours);
        Assert.Equal(28, biennial.SecondYearHours);
        Assert.Equal(48, biennial.TotalHours);
        Assert.True(biennial.Passed);
    }

    [Fact]
    public void EmployeeWithNoActivityRemainsVisibleAndHasFullMissingHours()
    {
        var employee = Person();
        var annual = Assert.Single(ReportService.BuildAnnual([employee], [], 2026));
        Assert.False(annual.Passed);
        Assert.Equal(12, annual.MissingHours);
        var biennial = Assert.Single(ReportService.BuildBiennial([employee], [], 2025));
        Assert.Equal(48, biennial.MissingHours);
        Assert.Empty(ReportService.BuildDepartments([], [], 2025, 2026));
    }

    [Fact]
    public void DepartmentRatesUseAllEmployeesIncludingPeopleWithZeroHours()
    {
        var first = Person();
        var second = Person() with { Code = "NV002" };
        var third = Person() with { Code = "NV003", Department = "Khoa Ngoại" };
        var rows = ReportService.BuildDepartments([first, second, third], [Activity(first.Id, 2025, 30), Activity(first.Id, 2026, 18), Activity(third.Id, 2026, 12)], 2025, 2026);
        var internalMedicine = Assert.Single(rows, row => row.Department == "Khoa Nội");
        Assert.Equal(2, internalMedicine.EmployeeCount);
        Assert.Equal(1, internalMedicine.AnnualPassed);
        Assert.Equal(1, internalMedicine.BiennialPassed);
        Assert.Equal(50, internalMedicine.AnnualRate);
        Assert.Equal(50, internalMedicine.BiennialRate);
        var surgery = Assert.Single(rows, row => row.Department == "Khoa Ngoại");
        Assert.Equal(100, surgery.AnnualRate);
        Assert.Equal(0, surgery.BiennialRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void InvalidYearsAreRejected(int year) => Assert.Throws<ArgumentOutOfRangeException>(() => ReportService.BuildAnnual([], [], year));

    [Fact]
    public void BiennialLastYearCannotOverflowDateRange() => Assert.Throws<ArgumentOutOfRangeException>(() => ReportService.BuildBiennial([], [], 9999));

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static Employee Person() => new() { Code = "NV001", Name = "Nguyễn Thị An", Department = "Khoa Nội" };
    private static TrainingActivity Activity(string employeeId, int year, decimal hours) => new() { EmployeeId = employeeId, Type = "CME", Hours = hours, StartDate = new DateTime(year, 6, 1), EndDate = new DateTime(year, 6, 2) };
}
