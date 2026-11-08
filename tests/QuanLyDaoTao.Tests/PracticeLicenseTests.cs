using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Tests;

public sealed class PracticeLicenseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("Chờ cấp")]
    [InlineData("CHỜ CẤP")]
    [InlineData("  chờ   cấp  ")]
    [InlineData("Cho cap")]
    [InlineData("Đang chờ cấp")]
    [InlineData("DANG CHO CAP")]
    [InlineData("Chưa có")]
    [InlineData("CHUA CO")]
    [InlineData("Không có")]
    [InlineData("KHONG CO")]
    [InlineData("Chưa được cấp")]
    [InlineData("CHUA DUOC CAP")]
    [InlineData("Không")]
    [InlineData("KHONG")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("CCHN")]
    [InlineData("GPHN")]
    [InlineData("Chờ cấp (dự kiến 01/12/2026)")]
    [InlineData("Đang chờ cấp - nộp hồ sơ ngày 08/11/2026")]
    [InlineData("CHUA DUOC CAP, NGAY HEN 01/12/2026")]
    [InlineData("  KHONG CO (cap nhat 08/11/2026)  ")]
    public void EmptyStatusAndPlaceholderValuesAreNotIssuedLicenses(string? number)
    {
        var employee = new Employee { PracticeLicenseNumber = number! };

        Assert.False(employee.HasPracticeLicense);
    }

    [Theory]
    [InlineData("1636/CCHN-D-SYT-ST")]
    [InlineData("1644/CCHN-D-SYT-ST")]
    [InlineData("00123")]
    [InlineData("CCHN-001")]
    [InlineData("GPHN-002")]
    [InlineData("  1636/CCHN-D-SYT-ST  ")]
    [InlineData("00123 (cấp lại ngày 08/11/2026)")]
    [InlineData("CCHN-001 - CAP LAI 08/11/2026")]
    public void IssuedLicenseNumbersRemainRecognizedIncludingReissueNotes(string number)
    {
        var employee = new Employee { PracticeLicenseNumber = number };

        Assert.True(employee.HasPracticeLicense);
    }

    [Fact]
    public void LicenseSubsetOfFiftyPeopleContainsThirtyFourIssuedAndExcludesWaitingNotesFromReports()
    {
        var issued = Enumerable.Range(1, 34).Select(index => new Employee
        {
            Code = $"NV{index:000}", Name = $"Nhân viên {index}", Department = "Khoa Nội",
            PracticeLicenseNumber = $"{index:00000}/CCHN-D-SYT-ST"
        }).ToArray();
        var notIssued = Enumerable.Range(0, 15).Select(index => new[] { "Chờ cấp", "Đang chờ cấp", "CHUA DUOC CAP", "Chờ cấp ngày 01/12/2026" }[index % 4]).Append("")
            .Select((number, index) => new Employee
            {
                Code = $"NV{index + 35:000}", Name = $"Nhân viên chờ {index}", Department = "Khoa Nội",
                PracticeLicenseNumber = number
            }).ToArray();
        var employees = issued.Concat(notIssued).ToArray();
        var activities = issued.Take(30).Select(employee => Course(employee.Id, 2026, 12))
            .Concat(issued.Take(20).Select(employee => Course(employee.Id, 2025, 36)))
            .Concat(notIssued.Select(employee => Course(employee.Id, 2026, 48))).ToArray();

        var licensed = employees.Where(employee => employee.HasPracticeLicense).ToArray();

        Assert.Equal(50, employees.Length);
        Assert.Equal(34, licensed.Length);
        Assert.Equal(16, employees.Count(employee => !employee.HasPracticeLicense));
        Assert.All(notIssued, employee => Assert.False(employee.HasPracticeLicense));
        var annual = ReportService.BuildAnnual(licensed, activities, 2026);
        Assert.Equal(34, annual.Count);
        Assert.Equal(30, annual.Count(row => row.Passed));
        Assert.Equal(360m, annual.Sum(row => row.TotalHours));
        var biennial = ReportService.BuildBiennial(licensed, activities, 2025);
        Assert.Equal(34, biennial.Count);
        Assert.Equal(20, biennial.Count(row => row.Passed));
        Assert.Equal(1080m, biennial.Sum(row => row.TotalHours));
        var department = Assert.Single(ReportService.BuildDepartments(licensed, activities, 2025, 2026));
        Assert.Equal(34, department.EmployeeCount);
        Assert.Equal(88.2m, department.AnnualRate);
        Assert.Equal(58.8m, department.BiennialRate);
    }

    private static TrainingActivity Course(string employeeId, int year, decimal hours) => new()
    {
        EmployeeId = employeeId, Type = "CME", Hours = hours,
        StartDate = new DateTime(year, 1, 1), EndDate = new DateTime(year, 1, 1)
    };
}
