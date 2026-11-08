using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyDaoTao.Core;
using QuanLyDaoTao.Desktop;
using QuanLyDaoTao.Desktop.Views;

namespace QuanLyDaoTao.SmokeTests;

internal static class PracticeLicenseVerifier
{
    public static void Verify(Window owner, string temporaryDirectory, string output)
    {
        var repository = new TrainingRepository(Path.Combine(temporaryDirectory, "practice-license-qa.db"));
        repository.Initialize();
        Employee[] people =
        [
            new() { Code = "CC01", Name = "Nguyễn An", Department = "Khoa Nội", PracticeLicenseNumber = "CCHN-001" },
            new() { Code = "CC02", Name = "Trần Bình", Department = "Khoa Ngoại", PracticeLicenseNumber = "GPHN-002" },
            new() { Code = "CC03", Name = "Lê Khang", Department = "Khoa Nội", PracticeLicenseNumber = "Chờ cấp (dự kiến 01/12/2026)" },
            new() { Code = "CC04", Name = "Võ Dung", Department = "Khoa Ngoại", PracticeLicenseNumber = "Không có" },
            new() { Code = "CC05", Name = "Phạm Em", Department = "Khoa Khác", PracticeLicenseDate = new DateTime(2020, 1, 1) }
        ];
        foreach (var person in people) repository.SaveEmployee(person);
        void Course(Employee person, int year, decimal hours, string type = "CME") => repository.SaveActivity(new TrainingActivity
        {
            EmployeeId = person.Id, Title = $"Khóa {person.Code} {year}", Type = type, Hours = hours, Provider = "Bệnh viện kiểm thử",
            StartDate = new DateTime(year, 6, 1), EndDate = new DateTime(year, 6, 1)
        });
        Course(people[0], 2025, 36); Course(people[0], 2026, 12, "SHKHKT");
        Course(people[2], 2026, 12); Course(people[3], 2026, 48); Course(people[4], 2026, 48);
        var state = new AppState(repository);
        var window = new Window { Owner = owner, Width = 1100, Height = 800 };
        Program.PrepareWindow(window); window.Show();
        try
        {
            var dashboard = new DashboardView(state, _ => { }); Show(window, dashboard);
            All(dashboard).OfType<ComboBox>().Single().SelectedItem = 2026; Program.Pump(window);
            AssertDashboard(dashboard, "DashboardAll", 5, "60%", "80%", 156);
            AssertDashboard(dashboard, "DashboardLicensed", 2, "50%", "50%", 48);
            All(dashboard).OfType<ComboBox>().Single().SelectedItem = 2025; Program.Pump(window);
            AssertDashboard(dashboard, "DashboardAll", 5, "0%", "20%", 156);
            AssertDashboard(dashboard, "DashboardLicensed", 2, "0%", "50%", 48);
            All(dashboard).OfType<ComboBox>().Single().SelectedItem = 2026; Program.Pump(window);
            Control<FrameworkElement>(dashboard, "DashboardLicensedEmployeesGroup").BringIntoView(); Program.Pump(window);
            Capture(window, output, "cchn-dashboard-licensed");

            var employees = new EmployeesView(state); Show(window, employees);
            var filter = Control<ComboBox>(employees, "EmployeesPracticeLicenseFilter");
            var table = Control<DataGrid>(employees, "EmployeesTable");
            Require(filter.SelectedIndex == 1, "employee list defaults to licensed employees");
            AssertEmployees(table, people[0], people[1]);
            Select(filter, 0, window); AssertEmployees(table, people);
            Select(filter, 2, window); AssertEmployees(table, people[2..]);
            var department = Control<ComboBox>(employees, "EmployeesDepartmentFilter");
            department.SelectedItem = "Khoa Nội"; Program.Pump(window); AssertEmployees(table, people[2]);
            var search = Control<TextBox>(employees, "EmployeesSearch");
            search.Text = "le khang"; Program.Pump(window); AssertEmployees(table, people[2]);
            search.Text = "không tồn tại"; Program.Pump(window); AssertEmployees(table);
            search.Text = ""; department.SelectedIndex = 0; Program.Pump(window);
            repository.SaveEmployee(people[4] with { PracticeLicenseNumber = "CC05-MOI" }); state.Reload(); Program.Pump(window);
            Require(filter.SelectedIndex == 2, "employee filter persists through refresh");
            AssertEmployees(table, people[2], people[3]);
            repository.SaveEmployee(people[4]); state.Reload(); Program.Pump(window);
            window.Width = 800; window.Height = 650; Program.Pump(window);
            AssertWithin(window, filter); Capture(window, output, "cchn-employees-minimum");
            window.Width = 1100; window.Height = 800;

            foreach (var biennial in new[] { false, true })
            {
                var report = new ReportView(state, biennial); Show(window, report);
                Control<ComboBox>(report, "ReportYear").SelectedItem = biennial ? 2025 : 2026; Program.Pump(window);
                filter = Control<ComboBox>(report, "ReportPracticeLicenseFilter");
                table = Control<DataGrid>(report, "ReportTable");
                Require(filter.SelectedIndex == 1, "report defaults to licensed employees");
                AssertReport(table, people[0], people[1]);
                var status = Control<ComboBox>(report, "ReportStatusFilter");
                Select(status, 1, window); AssertReport(table, people[0]);
                Select(status, 0, window); Select(filter, 0, window); AssertReport(table, people);
                Select(filter, 2, window); AssertReport(table, people[2..]);
                department = Control<ComboBox>(report, "ReportDepartmentFilter");
                department.SelectedItem = "Khoa Nội"; Program.Pump(window); AssertReport(table, people[2]);
                search = Control<TextBox>(report, "ReportSearch");
                search.Text = "le khang"; Program.Pump(window); AssertReport(table, people[2]);
                search.Text = "không tồn tại"; Program.Pump(window); AssertReport(table);
                search.Text = ""; department.SelectedIndex = 0; state.Reload(); Program.Pump(window);
                Require(filter.SelectedIndex == 2, "report filter persists through refresh"); AssertReport(table, people[2..]);
                window.Width = 800; window.Height = 650; Program.Pump(window); AssertWithin(window, filter);
                Capture(window, output, biennial ? "cchn-biennial-minimum" : "cchn-annual-minimum");
                window.Width = 1100; window.Height = 800;
            }

            var analytics = new AnalyticsView(state); Show(window, analytics);
            Control<ComboBox>(analytics, "AnalyticsStartYear").SelectedItem = 2025;
            Control<ComboBox>(analytics, "AnalyticsAnnualYear").SelectedItem = 2026; Program.Pump(window);
            filter = Control<ComboBox>(analytics, "AnalyticsPracticeLicenseFilter");
            Require(filter.SelectedIndex == 1, "department analytics defaults to licensed employees");
            AssertDepartments(analytics, 2, 1, 1);
            Select(filter, 0, window); AssertDepartments(analytics, 5, 3, 4);
            Select(filter, 2, window); AssertDepartments(analytics, 3, 2, 3);
            state.Reload(); Program.Pump(window); Require(filter.SelectedIndex == 2, "analytics filter persists through refresh");
            window.Width = 800; window.Height = 650; Program.Pump(window); AssertWithin(window, filter);
            Capture(window, output, "cchn-analytics-minimum");

            foreach (var person in people.Take(2)) repository.SaveEmployee(person with { PracticeLicenseNumber = "Chờ cấp" });
            state.Reload(); Select(filter, 1, window); AssertDepartments(analytics, 0, 0, 0);
            Show(window, dashboard);
            AssertDashboard(dashboard, "DashboardLicensed", 0, "0%", "0%", 0);
            AssertDashboard(dashboard, "DashboardAll", 5, "60%", "80%", 156);
            Show(window, new EmployeesView(state)); AssertEmployees(Control<DataGrid>(window, "EmployeesTable"));
            Show(window, new ReportView(state, false)); AssertReport(Control<DataGrid>(window, "ReportTable"));
            Show(window, new ReportView(state, true)); AssertReport(Control<DataGrid>(window, "ReportTable"));
            Console.WriteLine("PASS: CCHN dashboard cards, scoped hours/rates, default/three-way filters, combined filters, reloads, minimum layout and empty licensed group");
        }
        finally { window.Close(); Program.Pump(owner); }
    }

    private static void AssertDashboard(DependencyObject root, string prefix, int count, string biennial, string annual, decimal hours)
    {
        Require(Control<TextBlock>(root, prefix + "EmployeeCount").Text == count.ToString(), prefix + " employee count");
        Require(Control<TextBlock>(root, prefix + "BiennialRate").Text == biennial, prefix + " biennial rate");
        Require(Control<TextBlock>(root, prefix + "AnnualRate").Text == annual, prefix + " annual rate");
        Require(Control<TextBlock>(root, prefix + "TrainingHours").Text == hours.ToString("0.##"), prefix + " scoped hours");
        foreach (var suffix in new[] { "BiennialChart", "AnnualChart" })
        {
            var chart = Control<FrameworkElement>(root, prefix + suffix);
            var ring = All(chart).Single(item => item.GetType().Name == "ComplianceRing");
            var name = AutomationProperties.GetName(ring);
            var rate = suffix == "BiennialChart" ? biennial : annual;
            Require(count == 0 ? name == "Chưa có dữ liệu" : name.Contains($"/ {count} nhân viên đạt") && name.EndsWith(", " + rate), prefix + suffix + " scoped chart denominator and rate");
        }
    }

    private static void AssertEmployees(DataGrid table, params Employee[] expected) =>
        AssertIds(table.Items.OfType<Employee>().Select(person => person.Id), expected);

    private static void AssertReport(DataGrid table, params Employee[] expected) =>
        AssertIds(table.Items.OfType<TrainingReportRow>().Select(row => row.Employee.Id), expected);

    private static void AssertIds(IEnumerable<string> actual, Employee[] expected) =>
        Require(actual.Order().SequenceEqual(expected.Select(person => person.Id).Order()), "filtered rows match expected employee identities");

    private static void AssertDepartments(DependencyObject root, int count, int biennial, int annual)
    {
        var rows = Control<DataGrid>(root, "AnalyticsTable").Items.OfType<DepartmentReportRow>().ToArray();
        Require(rows.Sum(row => row.EmployeeCount) == count && rows.Sum(row => row.BiennialPassed) == biennial && rows.Sum(row => row.AnnualPassed) == annual,
            "department table uses selected license group for all counts");
    }

    private static void Show(Window window, UserControl view) { window.Content = view; Program.Pump(window); }
    private static void Select(ComboBox filter, int index, Window window) { filter.SelectedIndex = index; Program.Pump(window); }
    private static void Capture(Window window, string output, string name) => Program.SaveVisual(window, Path.Combine(output, name + ".png"));
    private static void AssertWithin(Window window, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, "CCHN filter stays within minimum viewport");
    }
    private static T Control<T>(DependencyObject root, string id) where T : FrameworkElement => All(root).OfType<T>()
        .FirstOrDefault(item => item.Name == id || AutomationProperties.GetAutomationId(item) == id)
        ?? throw new InvalidOperationException("CCHN smoke control missing: " + id);
    private static IEnumerable<DependencyObject> All(DependencyObject node)
    {
        yield return node;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in All(VisualTreeHelper.GetChild(node, index))) yield return child;
    }
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("CCHN smoke assertion failed: " + description);
    }
}
