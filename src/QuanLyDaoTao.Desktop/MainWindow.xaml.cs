using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using QuanLyDaoTao.Desktop.Views;

namespace QuanLyDaoTao.Desktop;

public partial class MainWindow : Window
{
    private readonly AppState state;
    private readonly List<Button> navButtons = [];
    private static readonly string[] Titles = ["Tổng quan", "Quản lý nhân viên", "Đào tạo & SHKHKT", "Báo cáo 2 năm", "Báo cáo 1 năm", "Phân tích khoa / phòng"];
    public MainWindow(AppState state)
    {
        InitializeComponent(); this.state = state;
        var glyphs = new[] { "\uE80F", "\uE716", "\uE8F1", "\uE734", "\uE787", "\uE9D2" };
        Navigation.Children.Add(new TextBlock { Text = "QUẢN LÝ", FontSize = 10, Foreground = Ui.Brush("#709A9D"), Margin = new Thickness(12, 0, 0, 12) });
        for (var i = 0; i < Titles.Length; i++)
        {
            var index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyphs[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Width = 29, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = Titles[i], FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 13, 6, 13), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Ui.Brush("#BBD1D3") };
            button.Click += (_, _) => Navigate(index); navButtons.Add(button); Navigation.Children.Add(button);
        }
        state.Changed += (_, _) => UpdateStatus(); UpdateStatus(); Navigate(0);
    }
    public void Navigate(int index)
    {
        HeaderTitle.Text = Titles[index];
        for (var i = 0; i < navButtons.Count; i++) { navButtons[i].Background = i == index ? Ui.Brush("#21646A") : Brushes.Transparent; navButtons[i].Foreground = i == index ? Brushes.White : Ui.Brush("#BBD1D3"); }
        PageHost.Content = index switch { 0 => new DashboardView(state, Navigate), 1 => new EmployeesView(state), 2 => new ActivitiesView(state), 3 => new ReportView(state, true), 4 => new ReportView(state, false), _ => new AnalyticsView(state) };
    }
    private void UpdateStatus() { SidebarCount.Text = $"{state.Employees.Count} nhân viên · {state.Activities.Count} hồ sơ" + (state.Supplements.Count > 0 ? $"\n{state.Supplements.Count} chứng chỉ bổ sung" : ""); StatusText.Text = "●  Đã kết nối cơ sở dữ liệu"; StatusText.ToolTip = state.Repository.DatabasePath; }
    private void Sample_Click(object sender, RoutedEventArgs e)
    {
        var occupied = state.Employees.Count > 0 || state.Activities.Count > 0;
        var message = occupied ? "Dữ liệu hiện tại sẽ được sao lưu tự động, sau đó thay bằng 7 nhân viên và 14 hồ sơ mẫu. Tiếp tục?" : "Nạp 7 nhân viên và 14 hồ sơ đào tạo mẫu từ bản draft?";
        if (MessageBox.Show(this, message, "Nạp dữ liệu mẫu", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Ui.Try(this, () => { string? backup = null; if (occupied) { backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(state.Repository.DatabasePath)!, $"backup-before-sample-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db"); state.Repository.Backup(backup); } state.Repository.SeedSampleData(occupied); state.Reload(); MessageBox.Show(this, "Đã nạp dữ liệu mẫu." + (backup == null ? "" : $"\n\nBản sao dữ liệu cũ:\n{backup}"), "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information); });
    }
    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite database (*.db)|*.db", FileName = $"MediTrain-backup-{DateTime.Now:yyyyMMdd-HHmm}.db", Title = "Lưu bản sao cơ sở dữ liệu" };
        if (dialog.ShowDialog(this) != true) return;
        Ui.Try(this, () => { state.Repository.Backup(dialog.FileName); MessageBox.Show(this, $"Đã sao lưu đầy đủ nhân viên và hồ sơ đào tạo.\n\n{dialog.FileName}", "Sao lưu thành công", MessageBoxButton.OK, MessageBoxImage.Information); });
    }
}
