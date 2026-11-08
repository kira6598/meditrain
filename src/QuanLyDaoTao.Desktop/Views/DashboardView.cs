using System.Windows;
using System.Windows.Controls;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class DashboardView : UserControl
{
    private readonly AppState _state;
    private readonly Action<int> _navigate;
    private readonly ComboBox _year = new() { Width = 115 };
    private readonly StackPanel _body = new();
    private bool _refreshing;

    public DashboardView(AppState state, Action<int> navigate)
    {
        _state = state; _navigate = navigate;
        var layout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var field = Ui.Field("NĂM THEO DÕI", _year); field.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(field, Dock.Right); header.Children.Add(field);
        var heading = FormUi.Header("Tổng quan đào tạo", "Theo dõi tiến độ cập nhật kiến thức của đội ngũ y tế."); heading.Margin = new Thickness(0); header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = layout;
        _year.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        Loaded += (_, _) => { _state.Changed += StateChanged; Refresh(); };
        Unloaded += (_, _) => _state.Changed -= StateChanged;
    }

    private void StateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        var selected = _year.SelectedItem as int? ?? DateTime.Today.Year;
        _year.ItemsSource = Ui.Years(_state).Append(selected).Where(y => y >= 2 && y <= 9999).Distinct().OrderDescending().ToArray(); _year.SelectedItem = selected;
        _refreshing = false; Render();
    }

    private void Render()
    {
        if (_year.SelectedItem is not int year) return;
        _body.Children.Clear();
        AddOverviewGroup("Toàn bộ nhân viên", "DashboardAll", _state.Employees, _state.Activities, year);
        var licensedEmployees = _state.Employees.Where(employee => employee.HasPracticeLicense).ToArray();
        var licensedIds = licensedEmployees.Select(employee => employee.Id).ToHashSet();
        var licensedActivities = _state.Activities.Where(activity => licensedIds.Contains(activity.EmployeeId)).ToArray();
        AddOverviewGroup("Nhân viên có CCHN", "DashboardLicensed", licensedEmployees, licensedActivities, year,
            "Chỉ tính nhân viên có số CCHN / GPHN; không tính chờ cấp, chưa có hoặc để trống.");
        if (_state.Employees.Count == 0)
        {
            var onboarding = new StackPanel(); onboarding.Children.Add(Ui.Text("Bắt đầu quản lý đào tạo của đơn vị", 19, bold: true));
            var text = Ui.Text("Thêm nhân viên, sau đó ghi nhận các khóa SHKHKT và CME để xem tiến độ trên báo cáo. Bạn cũng có thể nạp dữ liệu mẫu bằng nút ở thanh công cụ.", 13, "#6C808C"); text.Margin = new Thickness(0, 9, 0, 16); onboarding.Children.Add(text);
            var actions = new WrapPanel(); actions.Children.Add(Ui.Button("+  Thêm nhân viên", () => _navigate(1), true)); actions.Children.Add(Ui.Button("Hồ sơ đào tạo", () => _navigate(2))); onboarding.Children.Add(actions);
            var card = Ui.Card(onboarding, 24); card.Margin = new Thickness(0, 0, 0, 18); _body.Children.Add(card);
        }
        var recentPanel = new StackPanel();
        var recentHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var allActivities = Ui.Button("Xem tất cả  →", () => _navigate(2)); DockPanel.SetDock(allActivities, Dock.Right); recentHeader.Children.Add(allActivities);
        var title = Ui.Text("Hồ sơ đào tạo gần đây", 17, bold: true); title.VerticalAlignment = VerticalAlignment.Center; recentHeader.Children.Add(title); recentPanel.Children.Add(recentHeader);
        if (_state.Activities.Count == 0)
        {
            var empty = Ui.Text("Chưa có hồ sơ đào tạo. Các khóa học mới sẽ xuất hiện tại đây.", 13, "#6C808C"); empty.Margin = new Thickness(0, 18, 0, 18); recentPanel.Children.Add(empty);
        }
        else
        {
            var people = _state.Employees.ToDictionary(e => e.Id);
            var table = FormUi.Table(); table.MaxHeight = 280;
            FormUi.Column(table, "Nhân viên", "EmployeeName", 185); FormUi.Column(table, "Khóa đào tạo / Sinh hoạt", "Title", -1);
            FormUi.Column(table, "Bắt đầu", "StartDate", 105, "dd/MM/yyyy"); FormUi.Column(table, "Loại", "Type", 85); FormUi.Column(table, "Số tiết", "Hours", 75, "0.##");
            table.ItemsSource = _state.Activities.OrderByDescending(a => a.StartDate).Take(5).Select(a => new RecentActivity(a, people.GetValueOrDefault(a.EmployeeId)?.Name ?? "")).ToArray(); recentPanel.Children.Add(table);
        }
        _body.Children.Add(Ui.Card(recentPanel));
        var shortcuts = new WrapPanel { Margin = new Thickness(0, 18, 0, 12) };
        shortcuts.Children.Add(Ui.Button("Báo cáo 48 tiết / 2 năm", () => _navigate(3)));
        shortcuts.Children.Add(Ui.Button("Báo cáo 12 tiết / năm", () => _navigate(4))); _body.Children.Add(shortcuts);
        var note = Ui.Text("Mục tiêu theo dõi: 48 tiết / 2 năm và 12 tiết / năm, đánh giá độc lập. Số tiết được tính theo ngày bắt đầu khóa học. Tổng tiết đào tạo gồm tất cả các năm.", 11, "#6C808C"); note.Margin = new Thickness(0, 0, 0, 8); _body.Children.Add(note);
    }

    private void AddOverviewGroup(string title, string prefix, IReadOnlyList<Employee> employees,
        IReadOnlyList<TrainingActivity> activities, int year, string? description = null)
    {
        var group = new StackPanel { Name = $"{prefix}EmployeesGroup", Margin = new Thickness(0, 0, 0, 18) };
        System.Windows.Automation.AutomationProperties.SetName(group, title);
        var heading = Ui.Text(title, 17, bold: true); heading.Margin = new Thickness(0, 0, 0, description is null ? 14 : 5); group.Children.Add(heading);
        if (description is not null)
        {
            var subtitle = Ui.Text(description, 12, "#6C808C"); subtitle.Margin = new Thickness(0, 0, 0, 14); subtitle.TextWrapping = TextWrapping.Wrap; group.Children.Add(subtitle);
        }
        var annual = ReportService.BuildAnnual(employees, activities, year);
        var biennial = ReportService.BuildBiennial(employees, activities, year - 1);
        var count = employees.Count; var passedAnnual = annual.Count(row => row.Passed); var passedBiennial = biennial.Count(row => row.Passed);
        var shkhkt = activities.Where(activity => activity.Type == "SHKHKT").Sum(activity => activity.Hours);
        var cme = activities.Where(activity => activity.Type == "CME").Sum(activity => activity.Hours);
        var metrics = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (var index = 0; index < 4; index++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        AddMetric(metrics, 0, "TỔNG NHÂN VIÊN", count.ToString(), $"{employees.Select(employee => employee.Department).Distinct().Count()} khoa / phòng", "#2563EB", $"{prefix}EmployeeCount");
        AddMetric(metrics, 1, "ĐẠT 48 TIẾT / 2 NĂM", Percent(passedBiennial, count), $"{passedBiennial} / {count} NV · {year - 1}–{year}", "#6366F1", $"{prefix}BiennialRate");
        AddMetric(metrics, 2, "ĐẠT 12 TIẾT / NĂM", Percent(passedAnnual, count), $"{passedAnnual} / {count} NV · Năm {year}", "#0D9488", $"{prefix}AnnualRate");
        AddMetric(metrics, 3, "TỔNG TIẾT ĐÀO TẠO", $"{shkhkt + cme:0.##}", $"SHKHKT {shkhkt:0.##} · CME {cme:0.##}", "#D97706", $"{prefix}TrainingHours");
        group.Children.Add(metrics);
        var charts = new Grid(); charts.ColumnDefinitions.Add(new ColumnDefinition()); charts.ColumnDefinitions.Add(new ColumnDefinition());
        var firstChart = ChartUi.ComplianceCard("Tiến độ đào tạo 2 năm", $"Giai đoạn {year - 1} – {year} · Mục tiêu 48 tiết", passedBiennial, count, "#6366F1"); firstChart.Name = $"{prefix}BiennialChart"; firstChart.Margin = new Thickness(0, 0, 8, 0);
        var secondChart = ChartUi.ComplianceCard("Tiến độ đào tạo hằng năm", $"Năm {year} · Mục tiêu 12 tiết", passedAnnual, count, "#0D9488"); secondChart.Name = $"{prefix}AnnualChart"; secondChart.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(secondChart, 1);
        charts.Children.Add(firstChart); charts.Children.Add(secondChart); group.Children.Add(charts);
        _body.Children.Add(group);
    }

    private static string Percent(int passed, int total) => total == 0 ? "0%" : $"{100m * passed / total:0.#}%";

    private static void AddMetric(Grid grid, int column, string label, string value, string detail, string color, string name)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Text(label, 10, "#6C808C", true));
        var number = Ui.Text(value, 32, color, true); number.Name = name; number.Margin = new Thickness(0, 9, 0, 8); panel.Children.Add(number);
        panel.Children.Add(Ui.Text(detail, 11, "#6C808C")); var card = Ui.Card(panel, 18); card.Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0); Grid.SetColumn(card, column); grid.Children.Add(card);
    }

    private sealed record RecentActivity(TrainingActivity Activity, string EmployeeName)
    {
        public string Title => Activity.Title;
        public DateTime StartDate => Activity.StartDate;
        public string Type => Activity.Type;
        public decimal Hours => Activity.Hours;
    }
}
