using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class ReportView : UserControl
{
    private readonly AppState state;
    private readonly bool biennial;
    private readonly ComboBox year = new() { Name = "ReportYear", Width = 128 };
    private readonly ComboBox department = new() { Name = "ReportDepartmentFilter", Width = 208 };
    private readonly ComboBox status = new() { Name = "ReportStatusFilter", Width = 145, ItemsSource = new[] { "Tất cả", "Đạt", "Chưa đạt" }, SelectedIndex = 0 };
    private readonly ComboBox practiceLicense = new() { Name = "ReportPracticeLicenseFilter", Width = 165, ItemsSource = new[] { "Tất cả", "Có CCHN", "Không có CCHN" }, SelectedIndex = 1, ToolTip = "Có CCHN khi đã có số chứng chỉ / giấy phép hành nghề. Chờ cấp, chưa có và để trống thuộc nhóm Không có CCHN." };
    private readonly TextBox search = new() { Name = "ReportSearch", Width = 200, ToolTip = "Tìm mã hoặc tên nhân viên (hỗ trợ không dấu)" };
    private readonly DataGrid table = new() { Name = "ReportTable" };
    private readonly TextBlock summary = Ui.Text("");
    private readonly TextBlock empty = Ui.Text("Chưa có dữ liệu phù hợp với bộ lọc.", 14, "#738793");
    private readonly TextBlock period = Ui.Text("", 13, "#667F8D");
    private IReadOnlyList<TrainingReportRow> rows = [];
    private bool refreshing;

    public ReportView(AppState state, bool biennial)
    {
        this.state = state; this.biennial = biennial;
        var root = new DockPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Ui.Button("In / PDF", Print));
        actions.Children.Add(Ui.Button("Xuất Excel", Export, true));
        DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
        var titles = new StackPanel();
        titles.Children.Add(Ui.Text(biennial ? "Theo dõi đào tạo trong 2 năm" : "Theo dõi đào tạo hằng năm", 24, bold: true));
        period.Margin = new Thickness(0, 8, 0, 0); titles.Children.Add(period); heading.Children.Add(titles);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var filters = new WrapPanel();
        filters.Children.Add(Ui.Field(biennial ? "Năm bắt đầu" : "Năm báo cáo", year));
        filters.Children.Add(Ui.Field("Khoa / phòng", department));
        filters.Children.Add(Ui.Field("Chứng chỉ hành nghề", practiceLicense));
        filters.Children.Add(Ui.Field("Kết quả", status));
        filters.Children.Add(Ui.Field("Tìm nhân viên", search));
        foreach (FrameworkElement field in filters.Children) field.Margin = new Thickness(0, 0, 16, 8);
        var filterCard = Ui.Card(filters, 16); filterCard.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(filterCard, Dock.Top); root.Children.Add(filterCard);
        var summaryCard = Ui.Card(summary, 16); summaryCard.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(summaryCard, Dock.Top); root.Children.Add(summaryCard);
        var note = Ui.Text("Số tiết được ghi nhận theo năm bắt đầu khóa học. Hai tiêu chí 12 tiết/năm và 48 tiết/2 năm được đánh giá độc lập.", 11, "#788F9B");
        note.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(note, Dock.Bottom); root.Children.Add(note);
        table.Columns.Add(Ui.Column("Mã NV", "Code", 90));
        table.Columns.Add(Ui.Column("Họ và tên", "Name", 225));
        table.Columns.Add(Ui.Column("Khoa / phòng", "Department", 180));
        if (biennial) { table.Columns.Add(Ui.Column("Năm 1", "FirstYearHours", 90, "{0:0.##}")); table.Columns.Add(Ui.Column("Năm 2", "SecondYearHours", 90, "{0:0.##}")); }
        else { table.Columns.Add(Ui.Column("SHKHKT", "ShkhktHours", 90, "{0:0.##}")); table.Columns.Add(Ui.Column("CME", "CmeHours", 90, "{0:0.##}")); }
        table.Columns.Add(Ui.Column("Tổng tiết", "TotalHours", 95, "{0:0.##}"));
        table.Columns.Add(Ui.Column("Còn thiếu", "MissingHours", 95, "{0:0.##}"));
        var result = Ui.Column("Kết quả", "Status", 115);
        var cellStyle = new Style(typeof(TextBlock));
        cellStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        cellStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Ui.Brush("#BE4054")));
        var passTrigger = new DataTrigger { Binding = new Binding("Passed"), Value = true };
        passTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Ui.Brush("#0D9488"))); cellStyle.Triggers.Add(passTrigger); result.ElementStyle = cellStyle;
        table.Columns.Add(result);
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is TrainingReportRow row) new EmployeeDetailWindow(state, row.Employee) { Owner = Window.GetWindow(this) }.ShowDialog(); };
        var tableHost = new Grid(); tableHost.Children.Add(table);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.IsHitTestVisible = false; tableHost.Children.Add(empty);
        root.Children.Add(Ui.Card(tableHost, 0)); Content = root;
        year.SelectionChanged += (_, _) => RefreshRows(); department.SelectionChanged += (_, _) => RefreshRows(); practiceLicense.SelectionChanged += (_, _) => RefreshRows(); status.SelectionChanged += (_, _) => RefreshRows(); search.TextChanged += (_, _) => RefreshRows();
        Loaded += (_, _) => { state.Changed += StateChanged; PopulateFilters(); };
        Unloaded += (_, _) => state.Changed -= StateChanged;
    }
    private void StateChanged(object? sender, EventArgs e) => PopulateFilters();
    private void PopulateFilters()
    {
        refreshing = true;
        var selectedYear = year.SelectedItem is int value ? value : DateTime.Today.Year - (biennial ? 1 : 0);
        year.ItemsSource = Ui.Years(state).Where(y => y >= 1 && y <= (biennial ? 9998 : 9999)); year.SelectedItem = selectedYear;
        var selectedDepartment = department.SelectedItem as string;
        var departments = new[] { "Tất cả khoa / phòng" }.Concat(state.Employees.Select(e => e.Department).Distinct().Order()).ToArray();
        department.ItemsSource = departments; department.SelectedItem = departments.Contains(selectedDepartment) ? selectedDepartment : departments[0];
        refreshing = false; RefreshRows();
    }
    private void RefreshRows()
    {
        if (refreshing || year.SelectedItem is not int selectedYear) return;
        var employees = state.Employees.Where(e => practiceLicense.SelectedIndex switch { 1 => e.HasPracticeLicense, 2 => !e.HasPracticeLicense, _ => true });
        var allRows = biennial ? ReportService.BuildBiennial(employees, state.Activities, selectedYear) : ReportService.BuildAnnual(employees, state.Activities, selectedYear);
        var dept = department.SelectedIndex > 0 ? department.SelectedItem as string : null;
        var query = Normalize(search.Text.Trim());
        rows = allRows.Where(r => (dept == null || r.Department == dept) && (status.SelectedIndex <= 0 || (status.SelectedIndex == 1 ? r.Passed : !r.Passed)) && (query.Length == 0 || Normalize(r.Name + " " + r.Code).Contains(query))).ToArray();
        table.ItemsSource = rows; empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var passed = rows.Count(r => r.Passed);
        summary.Text = $"Đang hiển thị {rows.Count} nhân viên     •     Đạt: {passed}     •     Chưa đạt: {rows.Count - passed}     •     Tỷ lệ đạt: {(rows.Count == 0 ? 0 : 100m * passed / rows.Count):0.#}%";
        period.Text = biennial ? $"Giai đoạn {selectedYear} – {selectedYear + 1}  ·  Mục tiêu tối thiểu 48 tiết" : $"Năm {selectedYear}  ·  Mục tiêu tối thiểu 12 tiết";
        if (biennial) { table.Columns[3].Header = $"{selectedYear}"; table.Columns[4].Header = $"{selectedYear + 1}"; }
    }
    private static string Normalize(string text) => string.Concat(text.ToLowerInvariant().Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark));
    private void Export()
    {
        if (year.SelectedItem is not int selectedYear) return;
        var dialog = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = biennial ? $"Bao-cao-{selectedYear}-{selectedYear + 1}.xlsx" : $"Bao-cao-{selectedYear}.xlsx" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Ui.Try(Window.GetWindow(this), () => { if (biennial) ExportService.ExportBiennial(dialog.FileName, rows, selectedYear); else ExportService.ExportAnnual(dialog.FileName, rows, selectedYear); MessageBox.Show(Window.GetWindow(this), $"Đã xuất {rows.Count} nhân viên theo bộ lọc hiện tại.\n\n{dialog.FileName}", "Xuất Excel thành công", MessageBoxButton.OK, MessageBoxImage.Information); });
    }
    private void Print()
    {
        if (year.SelectedItem is not int selectedYear) return;
        var printedRows = rows.Select(row => row with { Employee = row.Employee with { } }).ToArray();
        var selectedPeriod = biennial ? $"Giai đoạn {selectedYear} – {selectedYear + 1}" : $"Năm {selectedYear}";
        var filters = $"Khoa / phòng: {department.SelectedItem} · CCHN: {practiceLicense.SelectedItem} · Kết quả: {status.SelectedItem}"
            + (string.IsNullOrWhiteSpace(search.Text) ? "" : $" · Tìm nhân viên: {search.Text.Trim()}");
        var subtitle = summary.Text + "\n" + filters;
        Ui.Try(Window.GetWindow(this), () => PrintService.PrintTable(Window.GetWindow(this),
            biennial ? $"BÁO CÁO ĐÀO TẠO {selectedYear} – {selectedYear + 1}" : $"BÁO CÁO ĐÀO TẠO NĂM {selectedYear}",
            ["Mã NV", "Họ và tên", "Khoa / phòng", biennial ? selectedYear.ToString() : "SHKHKT", biennial ? (selectedYear + 1).ToString() : "CME", "Tổng tiết", "Còn thiếu", "Kết quả"],
            printedRows.Select(r => new[] { r.Code, r.Name, r.Department, (biennial ? r.FirstYearHours : r.ShkhktHours).ToString("0.##"), (biennial ? r.SecondYearHours : r.CmeHours).ToString("0.##"), r.TotalHours.ToString("0.##"), r.MissingHours.ToString("0.##"), r.Status }),
            subtitle, PrintChartFactory.ForReport(printedRows, selectedPeriod)));
    }
}
