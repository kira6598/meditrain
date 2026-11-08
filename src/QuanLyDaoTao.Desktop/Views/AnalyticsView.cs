using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class AnalyticsView : UserControl
{
    private readonly AppState _state;
    private readonly ComboBox _startYear = new() { Name = "AnalyticsStartYear", Width = 150 }, _annualYear = new() { Name = "AnalyticsAnnualYear", Width = 125 };
    private readonly ComboBox _practiceLicense = new() { Name = "AnalyticsPracticeLicenseFilter", Width = 165, ItemsSource = new[] { "Tất cả", "Có CCHN", "Không có CCHN" }, SelectedIndex = 1, ToolTip = "Có CCHN khi đã có số chứng chỉ / giấy phép hành nghề. Chờ cấp, chưa có và để trống thuộc nhóm Không có CCHN." };
    private readonly StackPanel _body = new();
    private IReadOnlyList<DepartmentReportRow> _rows = [];
    private bool _refreshing;

    public AnalyticsView(AppState state)
    {
        _state = state;
        var layout = new DockPanel();
        var header = FormUi.Header("Tỷ lệ & Biểu đồ", "So sánh tiến độ đào tạo giữa các khoa / phòng theo từng giai đoạn.");
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 20), LastChildFill = false };
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom }; actions.Children.Add(Ui.Button("Xuất Excel", Export, true)); actions.Children.Add(Ui.Button("In / PDF", Print));
        DockPanel.SetDock(actions, Dock.Right); toolbar.Children.Add(actions);
        var selectors = new WrapPanel();
        selectors.Children.Add(Ui.Field("GIAI ĐOẠN 48 TIẾT, NĂM ĐẦU", _startYear)); selectors.Children.Add(Ui.Field("NĂM 12 TIẾT", _annualYear)); selectors.Children.Add(Ui.Field("CHỨNG CHỈ HÀNH NGHỀ", _practiceLicense)); toolbar.Children.Add(selectors);
        foreach (FrameworkElement field in selectors.Children) field.Margin = new Thickness(0, 0, 16, 8);
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        layout.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = layout;
        _startYear.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        _annualYear.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        _practiceLicense.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        Loaded += (_, _) => { _state.Changed += StateChanged; Refresh(); }; Unloaded += (_, _) => _state.Changed -= StateChanged;
    }

    private void StateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        var startYear = _startYear.SelectedItem as int? ?? DateTime.Today.Year - 1;
        var annualYear = _annualYear.SelectedItem as int? ?? DateTime.Today.Year;
        var years = Ui.Years(_state).Append(startYear).Append(annualYear).Distinct().OrderDescending().ToArray();
        _startYear.ItemsSource = years.Where(y => y >= 1 && y <= 9998).ToArray(); _annualYear.ItemsSource = years.Where(y => y >= 1 && y <= 9999).ToArray();
        _startYear.SelectedItem = startYear; _annualYear.SelectedItem = annualYear;
        _refreshing = false; Render();
    }

    private void Render()
    {
        if (_startYear.SelectedItem is not int startYear || _annualYear.SelectedItem is not int annualYear) return;
        _body.Children.Clear();
        var employees = _state.Employees.Where(e => _practiceLicense.SelectedIndex switch { 1 => e.HasPracticeLicense, 2 => !e.HasPracticeLicense, _ => true });
        _rows = ReportService.BuildDepartments(employees, _state.Activities, startYear, annualYear);
        var total = _rows.Sum(r => r.EmployeeCount); var biennialPassed = _rows.Sum(r => r.BiennialPassed); var annualPassed = _rows.Sum(r => r.AnnualPassed);
        var charts = new Grid { Margin = new Thickness(0, 0, 0, 18) }; charts.ColumnDefinitions.Add(new ColumnDefinition()); charts.ColumnDefinitions.Add(new ColumnDefinition());
        var first = ChartUi.ComplianceCard("Tỷ lệ đạt 48 tiết / 2 năm", $"Giai đoạn {startYear} – {startYear + 1}", biennialPassed, total, "#6366F1"); first.Margin = new Thickness(0, 0, 8, 0);
        var second = ChartUi.ComplianceCard("Tỷ lệ đạt 12 tiết / năm", $"Năm {annualYear}", annualPassed, total, "#0D9488"); second.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(second, 1);
        charts.Children.Add(first); charts.Children.Add(second); _body.Children.Add(charts);
        var compare = new StackPanel();
        compare.Children.Add(Ui.Text("So sánh tỷ lệ đạt theo khoa / phòng", 17, bold: true));
        var legends = new WrapPanel { Margin = new Thickness(0, 9, 0, 18) };
        var firstLegend = ChartUi.LegendRow($"48 tiết ({startYear}–{startYear + 1})", "", "#6366F1"); firstLegend.Margin = new Thickness(0, 0, 25, 0);
        legends.Children.Add(firstLegend); legends.Children.Add(ChartUi.LegendRow($"12 tiết ({annualYear})", "", "#0D9488")); compare.Children.Add(legends);
        if (_rows.Count == 0)
        {
            var empty = Ui.Text("Không có nhân viên phù hợp với bộ lọc để thống kê theo khoa / phòng.", 13, "#6C808C"); empty.Margin = new Thickness(0, 18, 0, 24); compare.Children.Add(empty);
        }
        else
        {
            foreach (var row in _rows) compare.Children.Add(DepartmentBars(row));
            var scale = new Grid { Margin = new Thickness(190, 2, 54, 0) };
            for (var i = 0; i < 3; i++) scale.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 3; i++) { var tick = Ui.Text($"{i * 50}%", 10, "#94A3B8"); tick.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Center; Grid.SetColumn(tick, i); scale.Children.Add(tick); }
            compare.Children.Add(scale);
        }
        var comparisonCard = Ui.Card(compare); comparisonCard.Margin = new Thickness(0, 0, 0, 18); _body.Children.Add(comparisonCard);
        var tablePanel = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 15) };
        var subtitle = Ui.Text($"{_rows.Count} khoa / phòng · {total} nhân viên", 12, "#6C808C"); subtitle.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(subtitle, Dock.Right); heading.Children.Add(subtitle);
        heading.Children.Add(Ui.Text("Bảng tổng hợp khoa / phòng", 17, bold: true)); tablePanel.Children.Add(heading);
        var table = FormUi.Table(); table.Name = "AnalyticsTable"; table.MaxHeight = 400; table.MinHeight = _rows.Count == 0 ? 70 : 90;
        FormUi.Column(table, "Khoa / Phòng", "Department", -1); FormUi.Column(table, "Nhân viên", "EmployeeCount", 90);
        FormUi.Column(table, "Đạt 48T", "BiennialPassed", 90); FormUi.Column(table, "Tỷ lệ 48T (%)", "BiennialRate", 125, "0.#");
        FormUi.Column(table, "Đạt 12T", "AnnualPassed", 90); FormUi.Column(table, "Tỷ lệ 12T (%)", "AnnualRate", 125, "0.#");
        table.ItemsSource = _rows; tablePanel.Children.Add(table); _body.Children.Add(Ui.Card(tablePanel));
        var note = Ui.Text("Mỗi nhân viên thuộc khoa / phòng hiện tại trong hồ sơ. Hai ngưỡng được đánh giá độc lập; tiết học được tính vào năm bắt đầu khóa học.", 11, "#6C808C"); note.Margin = new Thickness(0, 18, 0, 10); _body.Children.Add(note);
    }

    private static Grid DepartmentBars(DepartmentReportRow row)
    {
        var layout = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        labels.Children.Add(Ui.Text(row.Department, 12, bold: true)); var count = Ui.Text($"{row.EmployeeCount} nhân viên", 10, "#6C808C"); count.Margin = new Thickness(0, 4, 0, 0); labels.Children.Add(count); layout.Children.Add(labels);
        var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var first = ChartUi.Bar(row.BiennialRate, "#6366F1"); first.Margin = new Thickness(0, 0, 0, 10); bars.Children.Add(first); bars.Children.Add(ChartUi.Bar(row.AnnualRate, "#0D9488"));
        Grid.SetColumn(bars, 1); layout.Children.Add(bars);
        var values = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var biennial = Ui.Text($"{row.BiennialRate:0.#}%", 11, "#6366F1", true); biennial.Margin = new Thickness(0, 0, 0, 5); values.Children.Add(biennial);
        values.Children.Add(Ui.Text($"{row.AnnualRate:0.#}%", 11, "#0D9488", true)); Grid.SetColumn(values, 2); layout.Children.Add(values); return layout;
    }

    private void Export()
    {
        if (_startYear.SelectedItem is not int startYear || _annualYear.SelectedItem is not int annualYear) return;
        var dialog = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", FileName = $"ThongKeKhoaPhong_{startYear}-{startYear + 1}_{annualYear}.xlsx", Title = "Xuất thống kê khoa / phòng" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Ui.Try(Window.GetWindow(this), () =>
        {
            ExportService.ExportDepartments(dialog.FileName, _rows, startYear, annualYear);
            MessageBox.Show(Window.GetWindow(this), $"Đã xuất thống kê khoa / phòng:\n{dialog.FileName}", "Xuất Excel thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void Print()
    {
        if (_startYear.SelectedItem is not int startYear || _annualYear.SelectedItem is not int annualYear) return;
        var printedRows = _rows.ToArray();
        var biennialPeriod = $"{startYear} – {startYear + 1}";
        var annualPeriod = annualYear.ToString();
        Ui.Try(Window.GetWindow(this), () => PrintService.PrintTable(Window.GetWindow(this), "THỐNG KÊ ĐÀO TẠO THEO KHOA / PHÒNG",
            ["Khoa / Phòng", "Nhân viên", "Đạt 48T", "Tỷ lệ 48T", "Đạt 12T", "Tỷ lệ 12T"],
            printedRows.Select(r => new[] { r.Department, r.EmployeeCount.ToString(), r.BiennialPassed.ToString(), $"{r.BiennialRate:0.#}%", r.AnnualPassed.ToString(), $"{r.AnnualRate:0.#}%" }),
            $"Giai đoạn 48 tiết: {startYear}–{startYear + 1} · Năm 12 tiết: {annualYear} · CCHN: {_practiceLicense.SelectedItem}",
            PrintChartFactory.ForDepartments(printedRows, biennialPeriod, annualPeriod)));
    }
}
