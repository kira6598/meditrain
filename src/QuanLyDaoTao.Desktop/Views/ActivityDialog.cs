using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class ActivityDialog : Window
{
    private readonly AppState _state;
    private readonly TrainingActivity _activity;
    private readonly ComboBox _employee = new() { DisplayMemberPath = "DisplayName", IsTextSearchEnabled = true };
    private readonly ComboBox _type = new();
    private readonly TextBox _title = new(), _hours = new(), _provider = new(), _certificate = new(), _notes = new() { AcceptsReturn = true, Height = 62, TextWrapping = TextWrapping.Wrap };
    private readonly DatePicker _start = new() { SelectedDateFormat = DatePickerFormat.Short }, _end = new() { SelectedDateFormat = DatePickerFormat.Short }, _issued = new() { SelectedDateFormat = DatePickerFormat.Short };
    private readonly TextBlock _error = FormUi.Text("", 13, Brushes.Firebrick);

    public ActivityDialog(AppState state, TrainingActivity? activity = null, string? employeeId = null, EmployeeTrainingHistoryRow? importedRow = null)
    {
        if (activity is not null && importedRow is not null) throw new ArgumentException("Chỉ chọn một hồ sơ để cập nhật.");
        _state = state; _activity = activity is null ? new TrainingActivity { EmployeeId = employeeId ?? "" } : activity with { };
        if (importedRow is not null)
        {
            _activity.Title = importedRow.Title;
            _activity.Type = importedRow.Type;
            _activity.Hours = importedRow.Hours ?? 0;
            _activity.Provider = importedRow.Provider;
            _activity.CertificateNumber = importedRow.CertificateNumber;
            _activity.SourceReference = importedRow.SourceReference;
            _activity.IssuedDate = importedRow.IssuedDate;
            _activity.StudyPeriod = importedRow.StudyPeriod;
            _activity.Notes = $"Thông tin nhập từ Excel: {importedRow.SourceText}";
        }
        var caption = importedRow is not null ? "Bổ sung thông tin đào tạo" : activity is null ? "Thêm hồ sơ đào tạo" : "Sửa hồ sơ đào tạo";
        FormUi.ConfigureDialog(this, caption, 770);
        _employee.ItemsSource = state.Employees.OrderBy(e => e.Name).ToArray();
        _employee.SelectedItem = state.Employees.FirstOrDefault(e => e.Id == _activity.EmployeeId);
        // A linked source row always belongs to the employee it was imported for.
        _employee.IsEnabled = string.IsNullOrEmpty(_activity.SourceReference);
        _type.ItemsSource = new[] { "SHKHKT", "CME" }; _type.SelectedItem = _activity.Type;
        _title.Text = _activity.Title; _provider.Text = _activity.Provider;
        _hours.Text = _activity.Hours > 0 ? _activity.Hours.ToString(CultureInfo.GetCultureInfo("vi-VN")) : "";
        _start.SelectedDate = importedRow is null ? _activity.StartDate : importedRow.StartDate;
        _end.SelectedDate = importedRow is null ? _activity.EndDate : importedRow.EndDate;
        _issued.SelectedDate = _activity.IssuedDate;
        _certificate.Text = _activity.CertificateNumber; _notes.Text = _activity.Notes;
        AutomationProperties.SetAutomationId(_type, "ActivityDialogType");
        AutomationProperties.SetAutomationId(_title, "ActivityDialogTitle");
        AutomationProperties.SetAutomationId(_hours, "ActivityDialogHours");
        AutomationProperties.SetAutomationId(_provider, "ActivityDialogProvider");
        AutomationProperties.SetAutomationId(_start, "ActivityDialogStartDate");
        AutomationProperties.SetAutomationId(_end, "ActivityDialogEndDate");
        AutomationProperties.SetAutomationId(_issued, "ActivityDialogIssuedDate");
        var layout = new DockPanel { Margin = new Thickness(26) };
        var header = FormUi.Header(caption, importedRow is null
            ? "SHKHKT nội viện và đào tạo liên tục CME ngoại viện."
            : "Xác nhận loại hình, ngày học và số tiết để tính vào tiến độ đào tạo.");
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = FormUi.Button("Hủy", (_, _) => DialogResult = false); cancel.IsCancel = true;
        var save = FormUi.Button("Lưu hồ sơ", Save, true); save.IsDefault = true;
        AutomationProperties.SetAutomationId(save, "ActivityDialogSave");
        footer.Children.Add(cancel); footer.Children.Add(save); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var form = new StackPanel();
        form.Children.Add(FormUi.Field("Nhân viên *", _employee));
        form.Children.Add(FormUi.Field("Tên bài sinh hoạt / Khóa đào tạo *", _title));
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Loại hình *", _type), FormUi.Field("Số tiết *", _hours)));
        form.Children.Add(FormUi.Field("Đơn vị tổ chức / Cấp chứng chỉ *", _provider));
        if (!string.IsNullOrWhiteSpace(_activity.StudyPeriod))
        {
            var period = new TextBox { Text = _activity.StudyPeriod, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent };
            AutomationProperties.SetAutomationId(period, "ActivityDialogStudyPeriod");
            form.Children.Add(FormUi.Field("Ngày học theo file Excel", period));
        }
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Ngày bắt đầu *", _start), FormUi.Field("Ngày hoàn thành *", _end)));
        form.Children.Add(FormUi.TwoColumns(FormUi.Field("Ngày cấp chứng chỉ", _issued), FormUi.Field("Số chứng chỉ / Quyết định", _certificate)));
        form.Children.Add(FormUi.Field("Ghi chú", _notes)); form.Children.Add(_error);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        Loaded += (_, _) => { if (_employee.SelectedItem is null) _employee.Focus(); else _title.Focus(); };
    }

    private void Save(object sender, RoutedEventArgs args)
    {
        try
        {
            if (_employee.SelectedItem is not Employee employee) { _employee.Focus(); throw new ArgumentException("Vui lòng chọn nhân viên."); }
            if (string.IsNullOrWhiteSpace(_title.Text)) { _title.Focus(); throw new ArgumentException("Vui lòng nhập tên bài sinh hoạt / khóa đào tạo."); }
            if (_type.SelectedItem is not string selectedType || selectedType is not ("CME" or "SHKHKT"))
            { _type.Focus(); throw new ArgumentException("Vui lòng xác nhận loại hình CME hoặc SHKHKT."); }
            if (string.IsNullOrWhiteSpace(_provider.Text)) { _provider.Focus(); throw new ArgumentException("Vui lòng nhập đơn vị tổ chức / cấp chứng chỉ."); }
            // Accept either decimal separator without treating punctuation as a thousands separator.
            var number = _hours.Text.Trim().Replace(',', '.');
            if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
            { _hours.Focus(); throw new ArgumentException("Số tiết phải lớn hơn 0 (ví dụ 4; 8; 1,5)."); }
            if (_start.SelectedDate is null || _end.SelectedDate is null) throw new ArgumentException("Vui lòng chọn đầy đủ ngày bắt đầu và ngày hoàn thành.");
            if (_end.SelectedDate < _start.SelectedDate) throw new ArgumentException("Ngày hoàn thành phải bằng hoặc sau ngày bắt đầu.");
            _activity.EmployeeId = employee.Id; _activity.Title = _title.Text.Trim(); _activity.Type = selectedType;
            _activity.Hours = hours; _activity.Provider = _provider.Text.Trim();
            _activity.StartDate = _start.SelectedDate.Value; _activity.EndDate = _end.SelectedDate.Value;
            _activity.IssuedDate = _issued.SelectedDate;
            _activity.CertificateNumber = _certificate.Text.Trim(); _activity.Notes = _notes.Text.Trim();
            _state.Repository.SaveActivity(_activity); _state.Reload(); DialogResult = true;
        }
        catch (Exception error) { _error.Text = error.Message; }
    }
}
