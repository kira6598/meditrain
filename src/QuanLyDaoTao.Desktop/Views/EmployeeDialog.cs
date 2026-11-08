using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class EmployeeDialog : Window
{
    private readonly AppState _state;
    private readonly Employee _employee;
    private readonly TextBox _code = new(), _name = new(), _title = new();
    private readonly TextBox _insuranceNumber = new(), _licenseNumber = new();
    private readonly TextBox _certificates = Multiline(), _conferences = Multiline(), _scientificActivities = Multiline();
    private readonly ComboBox _department = new() { IsEditable = true }, _degree = new() { IsEditable = true };
    private readonly DatePicker _startDate = new() { SelectedDateFormat = DatePickerFormat.Short };
    private readonly DatePicker _dateOfBirth = new() { SelectedDateFormat = DatePickerFormat.Short }, _licenseDate = new() { SelectedDateFormat = DatePickerFormat.Short };
    private readonly TextBlock _error = FormUi.Text("", 13, Brushes.Firebrick);

    public EmployeeDialog(AppState state, Employee? employee = null)
    {
        _state = state; _employee = employee is null ? new Employee() : employee with { };
        FormUi.ConfigureDialog(this, employee is null ? "Thêm nhân viên" : "Sửa nhân viên", 780);
        Width = 720;
        _code.Text = _employee.Code; _name.Text = _employee.Name; _title.Text = _employee.Title;
        _department.ItemsSource = state.Employees.Select(e => e.Department).Distinct().Order().ToArray();
        _department.Text = _employee.Department;
        _degree.ItemsSource = new[] { "BSCKII / Tiến sĩ", "BSCKI / Thạc sĩ", "Bác sĩ Đa khoa", "Cử nhân Điều dưỡng", "Cao đẳng / Trung cấp", "Dược sĩ", "Khác" };
        _degree.Text = _employee.Degree; _startDate.SelectedDate = _employee.StartDate;
        _insuranceNumber.Text = _employee.SocialInsuranceNumber; _dateOfBirth.SelectedDate = _employee.DateOfBirth;
        _licenseNumber.Text = _employee.PracticeLicenseNumber; _licenseDate.SelectedDate = _employee.PracticeLicenseDate;
        _certificates.Text = _employee.ProfessionalCertificates; _conferences.Text = _employee.ConferenceHistory; _scientificActivities.Text = _employee.ScientificActivityHistory;

        var layout = new DockPanel { Margin = new Thickness(26) };
        var header = FormUi.Header(employee is null ? "Thêm nhân viên mới" : "Thông tin nhân viên", "Các trường có dấu * là bắt buộc.");
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = FormUi.Button("Hủy", (_, _) => DialogResult = false); cancel.IsCancel = true;
        var save = FormUi.Button("Lưu nhân viên", Save, true); save.IsDefault = true;
        footer.Children.Add(cancel); footer.Children.Add(save);
        var bottom = new StackPanel(); _error.Margin = new Thickness(0, 8, 0, 0); bottom.Children.Add(_error); bottom.Children.Add(footer); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        var form = new StackPanel();
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Mã nhân viên *", _code), FormUi.Field("Họ và tên *", _name)));
        form.Children.Add(FormUi.Field("Khoa / Phòng *", _department));
        form.Children.Add(FormUi.Field("Chức danh / Chức vụ", _title));
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Trình độ chuyên môn", _degree), FormUi.Field("Ngày vào làm", _startDate)));
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Mã số BHXH", _insuranceNumber), FormUi.Field("Ngày sinh", _dateOfBirth)));
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Số chứng chỉ / Giấy phép hành nghề", _licenseNumber), FormUi.Field("Ngày cấp CCHN / GPHN", _licenseDate)));
        var section = FormUi.Text("Thông tin chứng chỉ và đào tạo đã nhập", 16, bold: true); section.Margin = new Thickness(0, 8, 0, 10); form.Children.Add(section);
        var note = FormUi.Text("Nội dung ghi chú dưới đây được lưu nguyên văn. Để tính vào báo cáo số tiết, hãy thêm hồ sơ đào tạo với ngày học và số tiết cụ thể.", 12, FormUi.Muted); note.Margin = new Thickness(0, 0, 0, 16); form.Children.Add(note);
        form.Children.Add(FormUi.Field("Chứng chỉ chuyên môn / Đào tạo", _certificates));
        form.Children.Add(FormUi.Field("Lịch sử hội nghị / Hội thảo", _conferences));
        form.Children.Add(FormUi.Field("Lịch sử sinh hoạt khoa học kỹ thuật", _scientificActivities));
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        Loaded += (_, _) => _code.Focus();
    }

    private void Save(object sender, RoutedEventArgs args)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_code.Text)) { _code.Focus(); throw new ArgumentException("Vui lòng nhập mã nhân viên."); }
            if (string.IsNullOrWhiteSpace(_name.Text)) { _name.Focus(); throw new ArgumentException("Vui lòng nhập họ và tên."); }
            if (string.IsNullOrWhiteSpace(_department.Text)) { _department.Focus(); throw new ArgumentException("Vui lòng nhập khoa / phòng."); }
            if (!string.IsNullOrWhiteSpace(_startDate.Text) && _startDate.SelectedDate is null)
                throw new ArgumentException("Ngày vào làm chưa hợp lệ. Vui lòng chọn ngày từ lịch.");
            if (!string.IsNullOrWhiteSpace(_dateOfBirth.Text) && _dateOfBirth.SelectedDate is null)
                throw new ArgumentException("Ngày sinh chưa hợp lệ. Vui lòng chọn ngày từ lịch.");
            if (!string.IsNullOrWhiteSpace(_licenseDate.Text) && _licenseDate.SelectedDate is null)
                throw new ArgumentException("Ngày cấp CCHN / GPHN chưa hợp lệ. Vui lòng chọn ngày từ lịch.");
            _employee.Code = _code.Text.Trim(); _employee.Name = _name.Text.Trim();
            _employee.Department = _department.Text.Trim(); _employee.Title = _title.Text.Trim();
            _employee.Degree = _degree.Text.Trim(); _employee.StartDate = _startDate.SelectedDate;
            _employee.SocialInsuranceNumber = _insuranceNumber.Text.Trim(); _employee.DateOfBirth = _dateOfBirth.SelectedDate;
            _employee.PracticeLicenseNumber = _licenseNumber.Text.Trim(); _employee.PracticeLicenseDate = _licenseDate.SelectedDate;
            _employee.ProfessionalCertificates = _certificates.Text.Trim(); _employee.ConferenceHistory = _conferences.Text.Trim(); _employee.ScientificActivityHistory = _scientificActivities.Text.Trim();
            _state.Repository.SaveEmployee(_employee); _state.Reload(); DialogResult = true;
        }
        catch (Exception error) { _error.Text = error.Message; }
    }

    private static TextBox Multiline() => new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
}
