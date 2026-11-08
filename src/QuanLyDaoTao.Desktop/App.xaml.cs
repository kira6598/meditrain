using System.Globalization;
using System.IO;
using System.Windows;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("vi-VN")));
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyDaoTao");
            var dbIndex = Array.IndexOf(e.Args, "--database");
            var packagedDatabase = Path.Combine(AppContext.BaseDirectory, "Data", "training.db");
            var path = dbIndex >= 0 && dbIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[dbIndex + 1])
                : File.Exists(packagedDatabase) ? packagedDatabase : Path.Combine(dataDirectory, "training.db");
            var repository = new TrainingRepository(path);
            repository.Initialize();
            MainWindow = new MainWindow(new AppState(repository));
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể khởi động ứng dụng.\n\n{ex.Message}", "Quản lý đào tạo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
