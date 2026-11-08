using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class EmployeesView : UserControl
{
    private readonly AppState _state;
    private readonly DataGrid _table = FormUi.Table();
    private readonly TextBox _search = new() { Name = "EmployeesSearch", Width = 330, Padding = new Thickness(10, 8, 10, 8), ToolTip = "Tìm tên, mã nhân viên, mã số BHXH hoặc số chứng chỉ / giấy phép hành nghề (hỗ trợ không dấu)" };
    private readonly ComboBox _department = new() { Name = "EmployeesDepartmentFilter", Width = 205, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ComboBox _practiceLicense = new() { Name = "EmployeesPracticeLicenseFilter", Width = 165, MinHeight = 36, ItemsSource = new[] { "Tất cả", "Có CCHN", "Không có CCHN" }, SelectedIndex = 1, ToolTip = "Có CCHN khi đã có số chứng chỉ / giấy phép hành nghề. Chờ cấp, chưa có và để trống thuộc nhóm Không có CCHN." };
    private readonly TextBlock _count = FormUi.Text("", 13, FormUi.Muted);
    private readonly TextBlock _empty = FormUi.Text("", 14, FormUi.Muted);
    private readonly Button _edit, _delete, _history;

    public EmployeesView(AppState state)
    {
        _state = state;
        var layout = new DockPanel();
        var header = FormUi.Header("Quản lý nhân viên", "Hồ sơ nhân viên y tế, khoa / phòng và lịch sử đào tạo tập trung.");
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var toolbar = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var toolbarActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
        toolbarActions.Children.Add(FormUi.Button("Import Excel", (_, _) => new EmployeeImportWindow(_state) { Owner = Window.GetWindow(this) }.ShowDialog()));
        toolbarActions.Children.Add(FormUi.Button("+  Thêm nhân viên", (_, _) => OpenEditor(), true)); toolbar.Children.Add(toolbarActions);
        var filters = new WrapPanel();
        var searchField = new StackPanel(); searchField.Children.Add(FormUi.Text("Tìm tên, mã NV, BHXH, CCHN / GPHN", 11, FormUi.Muted)); _search.Margin = new Thickness(0, 5, 0, 0); searchField.Children.Add(_search);
        var deptField = new StackPanel(); var deptLabel = FormUi.Text("Khoa / Phòng", 11, FormUi.Muted); deptLabel.Margin = new Thickness(10, 0, 0, 0); deptField.Children.Add(deptLabel); _department.Margin = new Thickness(10, 5, 0, 0); _department.MinHeight = 36; deptField.Children.Add(_department);
        var licenseField = new StackPanel(); var licenseLabel = FormUi.Text("Chứng chỉ hành nghề", 11, FormUi.Muted); licenseLabel.Margin = new Thickness(10, 0, 0, 0); licenseField.Children.Add(licenseLabel); _practiceLicense.Margin = new Thickness(10, 5, 0, 0); licenseField.Children.Add(_practiceLicense);
        searchField.Margin = deptField.Margin = licenseField.Margin = new Thickness(0, 0, 0, 8);
        filters.Children.Add(searchField); filters.Children.Add(deptField); filters.Children.Add(licenseField); toolbar.Children.Add(filters);
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _history = FormUi.Button("Hồ sơ đào tạo", (_, _) => OpenHistory()); _history.ToolTip = "Xem CME, chứng chỉ, SHKHKT và tình trạng tuân thủ đào tạo"; _edit = FormUi.Button("Sửa", (_, _) => OpenEditor(Selected));
        _delete = FormUi.Button("Xóa", (_, _) => Delete(), danger: true);
        actions.Children.Add(_history); actions.Children.Add(_edit); actions.Children.Add(_delete);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); _count.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(_count);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        _table.Name = "EmployeesTable";
        FormUi.Column(_table, "Mã NV", "Code", 100); FormUi.Column(_table, "Họ và tên", "Name", 220);
        FormUi.Column(_table, "Ngày sinh", "DateOfBirth", 110, "dd/MM/yyyy"); FormUi.Column(_table, "Mã số BHXH", "SocialInsuranceNumber", 140); FormUi.Column(_table, "Số CCHN / GPHN", "PracticeLicenseNumber", 175);
        FormUi.Column(_table, "Khoa / Phòng", "Department", 190); FormUi.Column(_table, "Chức danh / Chức vụ", "Title", 170);
        FormUi.Column(_table, "Trình độ", "Degree", 175); FormUi.Column(_table, "Ngày vào làm", "StartDate", 120, "dd/MM/yyyy");
        var tablePanel = new Grid(); tablePanel.Children.Add(_table); _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center; _empty.IsHitTestVisible = false; tablePanel.Children.Add(_empty);
        var card = FormUi.Card(tablePanel); card.Padding = new Thickness(0); layout.Children.Add(card); Content = layout;
        _search.TextChanged += (_, _) => RefreshRows(); _department.SelectionChanged += (_, _) => RefreshRows(); _practiceLicense.SelectionChanged += (_, _) => RefreshRows();
        _table.SelectionChanged += (_, _) => UpdateActions();
        _table.MouseDoubleClick += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_table, source) is DataGridRow row && row.Item is Employee employee) { _table.SelectedItem = employee; OpenHistory(); } };
        Loaded += (_, _) => { _state.Changed += StateChanged; Refresh(); };
        Unloaded += (_, _) => _state.Changed -= StateChanged;
        UpdateActions();
    }

    private Employee? Selected => _table.SelectedItem as Employee;
    private void StateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var selectedDepartment = _department.SelectedItem as string;
        var departments = new[] { "Tất cả Khoa / Phòng" }.Concat(_state.Employees.Select(e => e.Department).Distinct().Order()).ToArray();
        _department.ItemsSource = departments; _department.SelectedItem = departments.Contains(selectedDepartment) ? selectedDepartment : departments[0];
        RefreshRows();
    }

    private void RefreshRows()
    {
        var selectedId = Selected?.Id;
        var department = _department.SelectedIndex > 0 ? _department.SelectedItem as string : null;
        var employees = _state.Employees.Where(e => _practiceLicense.SelectedIndex switch { 1 => e.HasPracticeLicense, 2 => !e.HasPracticeLicense, _ => true }).ToArray();
        var rows = employees.Where(e => (department is null || e.Department == department) && FormUi.Matches(_search.Text, e.Name, e.Code, e.SocialInsuranceNumber, e.PracticeLicenseNumber))
            .OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase).ToArray();
        _table.ItemsSource = rows; _table.SelectedItem = rows.FirstOrDefault(e => e.Id == selectedId);
        _count.Text = $"Hiển thị {rows.Length} / {employees.Length} nhân viên · {_practiceLicense.SelectedItem}";
        _empty.Text = _state.Employees.Count == 0 ? "Chưa có nhân viên. Chọn “Thêm nhân viên” để bắt đầu." : "Không có nhân viên phù hợp với bộ lọc.";
        _empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private void UpdateActions() { var enabled = Selected is not null; _edit.IsEnabled = enabled; _delete.IsEnabled = enabled; _history.IsEnabled = enabled; }

    private void OpenEditor(Employee? employee = null)
    {
        var dialog = new EmployeeDialog(_state, employee) { Owner = Window.GetWindow(this) }; dialog.ShowDialog();
    }

    private void OpenHistory()
    {
        if (Selected is not Employee employee) return;
        new EmployeeDetailWindow(_state, employee) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Delete()
    {
        if (Selected is not Employee employee) return;
        var activities = _state.Activities.Count(a => a.EmployeeId == employee.Id);
        var warning = activities == 0 ? "" : $"\n{activities} hồ sơ đào tạo liên quan cũng sẽ bị xóa.";
        if (MessageBox.Show(Window.GetWindow(this), $"Xóa nhân viên {employee.Name} ({employee.Code})?{warning}\nThao tác này không thể hoàn tác.", "Xác nhận xóa nhân viên", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _state.Repository.DeleteEmployee(employee.Id); _state.Reload(); }
        catch (Exception error) { FormUi.Error(Window.GetWindow(this), error); }
    }
}
