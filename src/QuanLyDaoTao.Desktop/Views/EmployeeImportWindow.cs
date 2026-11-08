using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class EmployeeImportWindow : Window
{
    private readonly AppState state;
    private readonly TextBox filePath = new() { IsReadOnly = true, MinWidth = 200 };
    private readonly ComboBox department = new() { IsEditable = true, MinWidth = 220 };
    private readonly ComboBox mode = new() { MinWidth = 250 };
    private readonly DataGrid table = FormUi.Table();
    private readonly DataGrid supplementTable = FormUi.Table();
    private readonly TabControl previewTabs = new();
    private readonly TabItem employeeTab = new() { Header = "Nhân viên" };
    private readonly TabItem supplementTab = new() { Header = "Chứng chỉ bổ sung" };
    private readonly TextBlock summary = Ui.Text("Chọn tệp Excel để xem trước các hồ sơ sẽ nhập.", 13, "#607784");
    private readonly TextBlock error = Ui.Text("", 12, "#BE4054");
    private readonly TextBlock empty = Ui.Text("Chọn tệp .xlsx của bạn để bắt đầu.", 14, "#738793");
    private readonly TextBlock supplementEmpty = Ui.Text("Chưa có dữ liệu chứng chỉ bổ sung.", 14, "#738793");
    private readonly TextBox detail = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Brush("#F7FAFC"), Height = 130, Text = "Chọn một dòng để xem nội dung chứng chỉ, hội thảo và SHKHKT đầy đủ." };
    private readonly Button import;
    private EmployeeImportPreview? preview;
    private string loadedDepartment = "";

    public EmployeeImportWindow(AppState state)
    {
        this.state = state;
        Title = "Import Excel · Nhân viên và chứng chỉ";
        Width = 1240; Height = 860; MinWidth = 960; MinHeight = 690;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        department.ItemsSource = new[] { "Chưa phân khoa" }.Concat(state.Employees.Select(e => e.Department).Distinct().Order()).Distinct().ToArray();
        department.Text = "Chưa phân khoa";
        mode.ItemsSource = new[] { "Bỏ qua nhân viên đã tồn tại", "Cập nhật thông tin có giá trị từ file" };
        mode.SelectedIndex = 0;

        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = FormUi.Header("Nhập nhân viên và chứng chỉ từ Excel", "Xem trước danh sách nhân viên và từng chứng chỉ bổ sung trước khi lưu.");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var filePanel = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var choose = Ui.Button("Chọn tệp Excel…", ChooseFile, true);
        choose.Margin = new Thickness(12, 0, 0, 0); choose.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(choose, Dock.Right); filePanel.Children.Add(choose);
        filePanel.Children.Add(Ui.Field("Tệp dữ liệu (.xlsx)", filePath));
        DockPanel.SetDock(filePanel, Dock.Top); root.Children.Add(filePanel);

        var settings = new WrapPanel();
        settings.Children.Add(Ui.Field("Khoa / phòng khi file không có cột này", department));
        settings.Children.Add(Ui.Field("Khi nhân viên đã tồn tại", mode));
        var refresh = Ui.Button("Xem trước lại", ReloadPreview); refresh.VerticalAlignment = VerticalAlignment.Bottom; settings.Children.Add(refresh);
        var settingsCard = Ui.Card(settings, 14); settingsCard.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(settingsCard, Dock.Top); root.Children.Add(settingsCard);
        var note = Ui.Text("Tệp thiếu mã nhân viên sẽ được cấp mã tự động. Chứng chỉ bổ sung được lưu kèm ngày cấp, ngày học, số tiết và nơi cấp; xác nhận loại hình CME / SHKHKT trong hồ sơ đào tạo để tính báo cáo.", 12, "#526D7A");
        var notice = Ui.Card(note, 12); notice.Background = Ui.Brush("#EEF8F6"); notice.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(notice, Dock.Top); root.Children.Add(notice);
        summary.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        error.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(error, Dock.Top); root.Children.Add(error);

        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = Ui.Button("Đóng", Close); close.IsCancel = true;
        import = Ui.Button("Nhập dữ liệu", Import, true); import.IsEnabled = false;
        actions.Children.Add(close); actions.Children.Add(import); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var footerText = Ui.Text("Dữ liệu hiện có và nhật ký đào tạo được giữ lại.", 11, "#6C808C"); footerText.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(footerText);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var details = new Expander { Header = "Chi tiết dòng đang chọn", Content = detail, Margin = new Thickness(0, 12, 0, 0), IsExpanded = false };
        DockPanel.SetDock(details, Dock.Bottom); root.Children.Add(details);

        table.Columns.Add(Ui.Column("Dòng", "Source", 92));
        table.Columns.Add(Ui.Column("Mã NV", "Code", 110));
        table.Columns.Add(Ui.Column("Họ và tên", "Employee.Name", 205));
        table.Columns.Add(Ui.Column("Mã số BHXH", "Employee.SocialInsuranceNumber", 145));
        table.Columns.Add(Ui.Column("Ngày sinh", "Employee.DateOfBirth", 115, "dd/MM/yyyy"));
        table.Columns.Add(Ui.Column("Chức danh", "Employee.Title", 175));
        table.Columns.Add(Ui.Column("Khoa / phòng", "Employee.Department", 170));
        table.Columns.Add(Ui.Column("Giấy phép hành nghề", "Employee.PracticeLicenseNumber", 185));
        table.Columns.Add(Ui.Column("Ngày cấp", "Employee.PracticeLicenseDate", 115, "dd/MM/yyyy"));
        table.Columns.Add(Ui.Column("Kiểm tra dữ liệu", "Validation", 330));
        table.SelectionChanged += (_, _) => ShowDetail();
        var tableHost = new Grid(); tableHost.Children.Add(table);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.IsHitTestVisible = false; tableHost.Children.Add(empty);
        employeeTab.Content = Ui.Card(tableHost, 0);
        supplementTable.Columns.Add(Ui.Column("Dòng", "Source", 92));
        supplementTable.Columns.Add(Ui.Column("Họ và tên", "Row.EmployeeName", 190));
        supplementTable.Columns.Add(Ui.Column("Chứng chỉ / Khóa học", "Row.Title", 320));
        supplementTable.Columns.Add(Ui.Column("Ngày cấp", "Row.IssuedDate", 115, "dd/MM/yyyy"));
        supplementTable.Columns.Add(Ui.Column("Ngày học theo Excel", "Row.StudyPeriod", 235));
        supplementTable.Columns.Add(Ui.Column("Ngày bắt đầu", "Row.StartDate", 115, "dd/MM/yyyy"));
        supplementTable.Columns.Add(Ui.Column("Ngày hoàn thành", "Row.EndDate", 130, "dd/MM/yyyy"));
        supplementTable.Columns.Add(Ui.Column("Số tiết", "Row.Hours", 95, "0.##"));
        supplementTable.Columns.Add(Ui.Column("Nơi cấp", "Row.Provider", 260));
        supplementTable.Columns.Add(Ui.Column("Phân loại", "Row.Type", 110));
        supplementTable.Columns.Add(Ui.Column("Kiểm tra dữ liệu", "Validation", 330));
        supplementTable.SelectionChanged += (_, _) => ShowDetail();
        var supplementHost = new Grid(); supplementHost.Children.Add(supplementTable);
        supplementEmpty.HorizontalAlignment = HorizontalAlignment.Center; supplementEmpty.VerticalAlignment = VerticalAlignment.Center; supplementEmpty.IsHitTestVisible = false; supplementHost.Children.Add(supplementEmpty);
        supplementTab.Content = Ui.Card(supplementHost, 0);
        previewTabs.Items.Add(employeeTab); previewTabs.Items.Add(supplementTab);
        previewTabs.SelectionChanged += (_, _) => ShowDetail();
        AutomationProperties.SetAutomationId(table, "EmployeeImportEmployeeTable");
        AutomationProperties.SetAutomationId(supplementTable, "EmployeeImportSupplementTable");
        AutomationProperties.SetAutomationId(previewTabs, "EmployeeImportPreviewTabs");
        AutomationProperties.SetAutomationId(summary, "EmployeeImportSummary");
        AutomationProperties.SetAutomationId(import, "EmployeeImportApply");
        root.Children.Add(previewTabs); Content = root;
        department.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (preview == null || department.Text.Trim() == loadedDepartment) return;
            import.IsEnabled = false;
            summary.Text = "Khoa / phòng đã thay đổi. Chọn “Xem trước lại” để cập nhật dữ liệu trước khi nhập.";
        }));
    }

    private void ChooseFile()
    {
        var picker = new OpenFileDialog { Title = "Chọn danh sách nhân viên", Filter = "Excel Workbook (*.xlsx)|*.xlsx", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == true) LoadFile(picker.FileName);
    }

    public void LoadFile(string path)
    {
        filePath.Text = Path.GetFullPath(path); filePath.ToolTip = filePath.Text;
        preview = null; import.IsEnabled = false; error.Text = ""; table.ItemsSource = null; supplementTable.ItemsSource = null; detail.Text = "";
        try
        {
            if (string.IsNullOrWhiteSpace(department.Text)) throw new ArgumentException("Vui lòng nhập khoa / phòng mặc định, hoặc chọn “Chưa phân khoa”.");
            preview = EmployeeImportService.Read(filePath.Text, department.Text.Trim());
            loadedDepartment = department.Text.Trim();
            var rows = preview.Rows.Select(row => new PreviewRow(row)).ToArray();
            var supplements = preview.SupplementRows.Select(row => new SupplementPreviewRow(row)).ToArray();
            table.ItemsSource = rows;
            supplementTable.ItemsSource = supplements;
            var invalid = rows.Count(row => !row.Row.IsValid);
            var warnings = rows.Count(row => row.Row.Warnings.Count > 0);
            var supplementInvalid = supplements.Count(row => !row.Row.IsValid);
            var supplementWarnings = supplements.Count(row => row.Row.Warnings.Count > 0);
            summary.Text = $"Nhân viên: {rows.Length}  ·  Hợp lệ: {rows.Length - invalid}  ·  Lỗi: {invalid}  ·  Có ghi chú: {warnings}\nChứng chỉ bổ sung: {supplements.Length}  ·  Hợp lệ: {supplements.Length - supplementInvalid}  ·  Lỗi: {supplementInvalid}  ·  Có ghi chú: {supplementWarnings}";
            employeeTab.Header = $"Nhân viên ({rows.Length})";
            supplementTab.Header = $"Chứng chỉ bổ sung ({supplements.Length})";
            error.Text = invalid + supplementInvalid > 0 ? "Hãy sửa các dòng có lỗi trong file Excel rồi xem trước lại. Chưa có dữ liệu nào được lưu." : "";
            import.IsEnabled = rows.Length + supplements.Length > 0 && invalid + supplementInvalid == 0;
            if (rows.Length > 0) table.SelectedIndex = 0;
            if (supplements.Length > 0) supplementTable.SelectedIndex = 0;
            empty.Text = "Không tìm thấy dòng nhân viên trong tệp này.";
            empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            supplementEmpty.Text = "Không tìm thấy dòng chứng chỉ bổ sung trong tệp này.";
            supplementEmpty.Visibility = supplements.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            previewTabs.SelectedIndex = supplements.Length > 0 ? 1 : 0;
            ShowDetail();
        }
        catch (Exception ex)
        {
            error.Text = ex.Message;
            summary.Text = "Chưa thể đọc danh sách nhân viên.";
            empty.Text = "Kiểm tra tệp Excel rồi chọn lại."; empty.Visibility = Visibility.Visible;
            supplementEmpty.Text = "Kiểm tra tệp Excel rồi chọn lại."; supplementEmpty.Visibility = Visibility.Visible;
            detail.Text = "";
        }
    }

    private void ReloadPreview()
    {
        if (string.IsNullOrWhiteSpace(filePath.Text)) { ChooseFile(); return; }
        LoadFile(filePath.Text);
    }

    private void ShowDetail()
    {
        if (previewTabs.SelectedIndex == 1)
        {
            if (supplementTable.SelectedItem is not SupplementPreviewRow supplement) { detail.Text = "Chọn một chứng chỉ để xem đầy đủ thông tin."; return; }
            var source = supplement.Row;
            detail.Text = $"{source.EmployeeName} — {supplement.Source}\n{source.Title}\nNgày cấp: {source.IssuedDate?.ToString("dd/MM/yyyy") ?? "Chưa rõ"}\nNgày học: {Display(source.StudyPeriod)}\nSố tiết: {source.Hours?.ToString("0.##") ?? "Chưa rõ"}\nNơi cấp: {Display(source.Provider)}\nPhân loại: {source.Type}\nKiểm tra: {supplement.Validation}";
            return;
        }
        if (table.SelectedItem is not PreviewRow row) return;
        detail.Text = string.Join("\n\n", new[]
        {
            $"{row.Employee.Name} — {row.Source}",
            $"Kiểm tra: {row.Validation}",
            $"Các chứng chỉ chuyên môn:\n{Display(row.Employee.ProfessionalCertificates)}",
            $"Hội thảo / Tập huấn / Hội nghị:\n{Display(row.Employee.ConferenceHistory)}",
            $"SHKHKT:\n{Display(row.Employee.ScientificActivityHistory)}"
        });
    }

    private void Import()
    {
        if (preview == null || !import.IsEnabled) return;
        import.IsEnabled = false;
        try
        {
            string? backup = null;
            if (state.Employees.Count > 0 && (mode.SelectedIndex == 1 || preview.SupplementRows.Count > 0))
            {
                backup = Path.Combine(Path.GetDirectoryName(state.Repository.DatabasePath)!, $"backup-before-import-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db");
                state.Repository.Backup(backup);
            }
            var result = EmployeeImportService.Apply(state.Repository, preview, mode.SelectedIndex == 1 ? ExistingEmployeeImportMode.UpdateNonEmpty : ExistingEmployeeImportMode.Skip);
            state.Reload();
            MessageBox.Show(this, $"Đã hoàn tất nhập Excel.\n\nNhân viên — Thêm: {result.Added} · Cập nhật: {result.Updated} · Bỏ qua: {result.Skipped}\nChứng chỉ — Thêm: {result.SupplementsAdded} · Cập nhật: {result.SupplementsUpdated} · Bỏ qua: {result.SupplementsSkipped}\n\nXem thông tin tại Quản lý nhân viên → Hồ sơ đào tạo → Nhật ký đào tạo." + (backup == null ? "" : $"\n\nBản sao trước khi cập nhật:\n{backup}"), "Nhập dữ liệu thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            error.Text = $"Không thể nhập dữ liệu: {ex.Message}";
            import.IsEnabled = true;
        }
    }

    private static string Display(string text) => string.IsNullOrWhiteSpace(text) ? "(Chưa có)" : text;
    private sealed record PreviewRow(EmployeeImportRow Row)
    {
        public Employee Employee => Row.Employee;
        public string Source => $"{Row.SheetName}:{Row.RowNumber}";
        public string Code => string.IsNullOrWhiteSpace(Employee.Code) ? "(Tự cấp mã)" : Employee.Code;
        public string Validation => Row.Errors.Count > 0 ? "Lỗi: " + string.Join("; ", Row.Errors) : Row.Warnings.Count > 0 ? string.Join("; ", Row.Warnings) : "Hợp lệ";
    }
    private sealed record SupplementPreviewRow(TrainingSupplementImportRow Row)
    {
        public string Source => $"{Row.SheetName}:{Row.RowNumber}";
        public string Validation => Row.Errors.Count > 0 ? "Lỗi: " + string.Join("; ", Row.Errors) : Row.Warnings.Count > 0 ? string.Join("; ", Row.Warnings) : "Hợp lệ";
    }
}
