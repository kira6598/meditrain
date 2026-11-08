using System.Windows;
using System.Windows.Controls;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class ActivitiesView : UserControl
{
    private readonly AppState _state;
    private readonly DataGrid _table = FormUi.Table();
    private readonly TextBox _search = new() { Width = 235, Padding = new Thickness(10, 8, 10, 8), ToolTip = "Tìm khóa học, nhân viên, mã NV hoặc đơn vị tổ chức" };
    private readonly ComboBox _type = new() { Width = 145 }, _year = new() { Width = 115 };
    private readonly TextBlock _count = FormUi.Text("", 13, FormUi.Muted), _empty = FormUi.Text("", 14, FormUi.Muted);
    private readonly Button _edit, _delete;

    public ActivitiesView(AppState state)
    {
        _state = state;
        var layout = new DockPanel();
        var header = FormUi.Header("Đào tạo & SHKHKT", "Quản lý số tiết, thời gian, đơn vị đào tạo và chứng chỉ của từng nhân viên.");
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 18), LastChildFill = false };
        var add = FormUi.Button("+  Thêm hồ sơ", (_, _) => OpenEditor(), true); DockPanel.SetDock(add, Dock.Right); toolbar.Children.Add(add);
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(Filter("Tìm khóa học, nhân viên, đơn vị", _search));
        filters.Children.Add(Filter("Loại hình", _type, true)); filters.Children.Add(Filter("Năm bắt đầu", _year, true));
        _type.ItemsSource = new[] { "Tất cả loại hình", "SHKHKT", "CME" }; _type.SelectedIndex = 0;
        toolbar.Children.Add(filters); DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _edit = FormUi.Button("Sửa hồ sơ", (_, _) => OpenEditor(Selected)); _delete = FormUi.Button("Xóa", (_, _) => Delete(), danger: true);
        actions.Children.Add(_edit); actions.Children.Add(_delete); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        _count.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(_count); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        FormUi.Column(_table, "Nhân viên", "EmployeeName", 200); FormUi.Column(_table, "Khóa đào tạo / Bài sinh hoạt", "Title", -1);
        FormUi.Column(_table, "Loại", "Type", 90); FormUi.Column(_table, "Số tiết", "Hours", 75, "0.##");
        FormUi.Column(_table, "Bắt đầu", "StartDate", 105, "dd/MM/yyyy"); FormUi.Column(_table, "Hoàn thành", "EndDate", 105, "dd/MM/yyyy");
        FormUi.Column(_table, "Đơn vị tổ chức", "Provider", 175); FormUi.Column(_table, "Số chứng chỉ", "CertificateNumber", 145);
        var tablePanel = new Grid(); tablePanel.Children.Add(_table); _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center; _empty.IsHitTestVisible = false; tablePanel.Children.Add(_empty);
        var card = FormUi.Card(tablePanel); card.Padding = new Thickness(0); layout.Children.Add(card); Content = layout;
        _search.TextChanged += (_, _) => RefreshRows(); _type.SelectionChanged += (_, _) => RefreshRows(); _year.SelectionChanged += (_, _) => RefreshRows();
        _table.SelectionChanged += (_, _) => UpdateActions();
        _table.MouseDoubleClick += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_table, source) is DataGridRow row && row.Item is ActivityRow activity) OpenEditor(activity.Activity); };
        Loaded += (_, _) => { _state.Changed += StateChanged; Refresh(); };
        Unloaded += (_, _) => _state.Changed -= StateChanged; UpdateActions();
    }

    private static StackPanel Filter(string name, Control control, bool indent = false)
    {
        var panel = new StackPanel { Margin = new Thickness(indent ? 10 : 0, 0, 0, 0) };
        panel.Children.Add(FormUi.Text(name, 11, FormUi.Muted)); control.Margin = new Thickness(0, 5, 0, 0); control.MinHeight = 36; panel.Children.Add(control); return panel;
    }

    private TrainingActivity? Selected => (_table.SelectedItem as ActivityRow)?.Activity;
    private void StateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var year = _year.SelectedItem as string;
        var years = new[] { "Tất cả năm" }.Concat(_state.Activities.Select(a => a.StartDate.Year).Append(DateTime.Today.Year).Distinct().OrderDescending().Select(y => y.ToString())).ToArray();
        _year.ItemsSource = years; _year.SelectedItem = years.Contains(year) ? year : years[0]; RefreshRows();
    }

    private void RefreshRows()
    {
        var selectedId = Selected?.Id;
        var employees = _state.Employees.ToDictionary(e => e.Id);
        var type = _type.SelectedIndex > 0 ? _type.SelectedItem as string : null;
        var year = _year.SelectedIndex > 0 && int.TryParse(_year.SelectedItem?.ToString(), out var y) ? y : (int?)null;
        var rows = _state.Activities.Where(a => (type is null || a.Type == type) && (year is null || a.StartDate.Year == year))
            .Select(a => new ActivityRow(a, employees.GetValueOrDefault(a.EmployeeId)))
            .Where(r => FormUi.Matches(_search.Text, r.Title, r.EmployeeName, r.EmployeeCode, r.Provider))
            .OrderByDescending(r => r.StartDate).ThenBy(r => r.EmployeeName).ToArray();
        _table.ItemsSource = rows; _table.SelectedItem = rows.FirstOrDefault(r => r.Activity.Id == selectedId);
        _count.Text = $"{rows.Length} / {_state.Activities.Count} hồ sơ · {rows.Sum(r => r.Hours):0.##} tiết theo bộ lọc";
        _empty.Text = _state.Activities.Count == 0 ? "Chưa có hồ sơ đào tạo. Chọn “Thêm hồ sơ” để bắt đầu." : "Không có hồ sơ phù hợp với bộ lọc.";
        _empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed; UpdateActions();
    }

    private void UpdateActions() { _edit.IsEnabled = Selected is not null; _delete.IsEnabled = Selected is not null; }

    private void OpenEditor(TrainingActivity? activity = null)
    {
        if (_state.Employees.Count == 0)
        { MessageBox.Show(Window.GetWindow(this), "Vui lòng thêm ít nhất một nhân viên trước khi tạo hồ sơ đào tạo.", "Chưa có nhân viên", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new ActivityDialog(_state, activity) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Delete()
    {
        if (Selected is not TrainingActivity activity) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Xóa hồ sơ “{activity.Title}”?\nThao tác này không thể hoàn tác.", "Xác nhận xóa hồ sơ", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _state.Repository.DeleteActivity(activity.Id); _state.Reload(); }
        catch (Exception error) { FormUi.Error(Window.GetWindow(this), error); }
    }

    private sealed record ActivityRow(TrainingActivity Activity, Employee? Employee)
    {
        public string EmployeeName => Employee?.Name ?? "Không xác định";
        public string EmployeeCode => Employee?.Code ?? "";
        public string Title => Activity.Title;
        public string Type => Activity.Type;
        public decimal Hours => Activity.Hours;
        public DateTime StartDate => Activity.StartDate;
        public DateTime EndDate => Activity.EndDate;
        public string Provider => Activity.Provider;
        public string CertificateNumber => Activity.CertificateNumber;
    }
}
