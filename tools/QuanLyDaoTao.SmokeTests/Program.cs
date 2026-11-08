using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuanLyDaoTao.Core;
using QuanLyDaoTao.Desktop;
using QuanLyDaoTao.Desktop.Views;

namespace QuanLyDaoTao.SmokeTests;

internal static class Program
{
    private static readonly string[] Pages = ["dashboard", "employees", "activities", "biennial", "annual", "analytics"];
    private static readonly List<Exception> DispatcherErrors = [];

    [STAThread]
    private static int Main(string[] args)
    {
        var portableExe = Argument(args, "--portable-exe");
        if (portableExe is not null) return PortablePackageVerifier.Verify(portableExe, Argument(args, "--portable-database"));
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "QuanLyDaoTao.UiSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        Application? application = null;
        try
        {
            var culture = CultureInfo.GetCultureInfo("vi-VN");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("vi-VN")));
            var output = Path.GetFullPath(Argument(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "qa"));
            Directory.CreateDirectory(output);
            var database = Argument(args, "--database");
            if (database is not null && File.Exists(database))
                throw new InvalidOperationException("Smoke test requires a new QA database path; an existing database will not be changed.");

            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources = LoadAppResources();
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                DispatcherErrors.Add(eventArgs.Exception);
                eventArgs.Handled = true;
            };
            var diagnostics = new ErrorListener();
            PresentationTraceSources.DataBindingSource.Listeners.Add(diagnostics);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            if (args.Contains("--print-only", StringComparer.Ordinal))
            {
                ExercisePrint(output);
                Require(DispatcherErrors.Count == 0 && diagnostics.Messages.Count == 0, "print tests have no dispatcher or binding errors");
                application.Shutdown(0);
                return 0;
            }

            var repository = new TrainingRepository(database ?? Path.Combine(temporaryDirectory, "smoke.db"));
            repository.Initialize();
            var state = new AppState(repository);
            Require(state.Employees.Count == 0 && state.Activities.Count == 0, "new database is empty");
            var main = new MainWindow(state);
            PrepareWindow(main);
            main.Width = 1440;
            main.Height = 900;
            main.Show();
            Pump(main);
            ExercisePages(main, output, "empty");

            repository.SeedSampleData();
            state.Reload();
            Require(state.Employees.Count == 7 && state.Activities.Count == 14, "sample dataset has 7 employees and 14 activities");
            ExercisePages(main, output, "seeded");
            main.Width = 1120;
            main.Height = 700;
            ExercisePages(main, output, "minimum");
            main.Width = 1440;
            main.Height = 900;
            Pump(main);
            ExerciseWindow(new EmployeeDialog(state), main, output, "dialog-employee-new");
            ExerciseWindow(new EmployeeDialog(state, state.Employees[0]), main, output, "dialog-employee-edit");
            ExerciseWindow(new ActivityDialog(state), main, output, "dialog-activity-new");
            ExerciseWindow(new ActivityDialog(state, state.Activities[0]), main, output, "dialog-activity-edit");
            ExerciseWindow(new EmployeeDetailWindow(state, state.Employees[0]), main, output, "dialog-employee-detail");
            ExerciseImportWindow(state, main, output, temporaryDirectory);
            EmployeeDetailVerifier.Verify(main, temporaryDirectory, output);
            PracticeLicenseVerifier.Verify(main, temporaryDirectory, output);
            var importFile = Argument(args, "--import-file");
            if (importFile is not null) ExerciseImportFile(importFile, temporaryDirectory);
            ExercisePrint(output);
            Require(state.Employees.Count == 7 && state.Activities.Count == 14, "layout tests did not mutate samples");
            Require(DispatcherErrors.Count == 0, $"no dispatcher errors: {string.Join("; ", DispatcherErrors.Select(error => error.ToString()))}");
            Require(diagnostics.Messages.Count == 0, $"no WPF binding errors: {string.Join("; ", diagnostics.Messages)}");
            main.Close();
            application.Shutdown(0);
            Console.WriteLine($"PASS: all views, import/profile/detail scenarios, print pagination. PNGs: {output}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            application?.Shutdown(1);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    private static void ExercisePages(MainWindow main, string output, string phase)
    {
        for (var index = 0; index < Pages.Length; index++)
        {
            main.Navigate(index);
            Pump(main);
            Require(main.ActualWidth > 1000 && main.ActualHeight > 600, $"{phase} {Pages[index]} has visible layout");
            SaveVisual(main, Path.Combine(output, $"{phase}-{index:00}-{Pages[index]}.png"));
            Console.WriteLine($"PASS: {phase} {Pages[index]}");
        }
    }

    private static void ExerciseWindow(Window window, Window owner, string output, string filename)
    {
        window.Owner = owner;
        PrepareWindow(window);
        window.Show();
        Pump(window);
        Require(window.ActualWidth > 200 && window.ActualHeight > 200, $"{filename} has visible layout");
        SaveVisual(window, Path.Combine(output, filename + ".png"));
        window.Close();
        Pump(owner);
        Console.WriteLine($"PASS: {filename}");
    }

    private static void ExerciseImportWindow(AppState state, Window owner, string output, string temporaryDirectory)
    {
        ExerciseWindow(new EmployeeImportWindow(state), owner, output, "dialog-import-empty");
        var synthetic = new Employee
        {
            Code = "NV-MAU", Name = "Nhân viên Minh Họa", Department = "Khoa Minh Họa", Title = "Điều dưỡng mẫu", Degree = "Cử nhân mẫu",
            SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1990, 2, 28), PracticeLicenseNumber = "CCHN-MAU", PracticeLicenseDate = new DateTime(2020, 3, 15),
            ProfessionalCertificates = "Chứng chỉ minh họa năm 2025\nChứng chỉ minh họa năm 2026", ConferenceHistory = "Hội nghị minh họa\nTập huấn minh họa", ScientificActivityHistory = "Sinh hoạt khoa học minh họa\nChưa quy đổi số tiết"
        };
        var path = Path.Combine(temporaryDirectory, "synthetic-employees.xlsx");
        ExportService.ExportEmployees(path, [synthetic]);
        AddSyntheticSupplementSheet(path, synthetic.Name);
        var preview = new EmployeeImportWindow(state);
        preview.LoadFile(path);
        preview.Owner = owner; PrepareWindow(preview); preview.Show(); Pump(preview);
        try
        {
            var previewTabs = Control<TabControl>(preview, "EmployeeImportPreviewTabs");
            Require(previewTabs.Items.Count == 2 && previewTabs.SelectedIndex == 1, "import preview separates employee and detail sheets and opens the supplement tab");
            previewTabs.SelectedIndex = 0; Pump(preview);
            var employeeTable = Control<DataGrid>(preview, "EmployeeImportEmployeeTable");
            previewTabs.SelectedIndex = 1; Pump(preview);
            var supplementTable = Control<DataGrid>(preview, "EmployeeImportSupplementTable");
            Require(employeeTable.Items.Count == 1 && supplementTable.Items.Count == 2, "import preview shows one employee and two supplemental certificates without duplicate employees");
            Require(Control<Button>(preview, "EmployeeImportApply").IsEnabled, "valid supplemental certificate preview can be imported");
            var columns = supplementTable.Columns.OfType<DataGridTextColumn>().Select(column => (column.Binding as System.Windows.Data.Binding)?.Path?.Path).ToArray();
            Require(new[] { "Row.IssuedDate", "Row.StudyPeriod", "Row.StartDate", "Row.EndDate", "Row.Hours", "Row.Provider" }.All(columns.Contains), "supplement preview has distinct issue/study-date, hours and issuer columns");
            SaveVisual(preview, Path.Combine(output, "dialog-import-preview-supplemental.png"));
        }
        finally { preview.Close(); Pump(owner); }
        var profileRepository = new TrainingRepository(Path.Combine(temporaryDirectory, "synthetic-profile.db"));
        profileRepository.Initialize();
        profileRepository.SaveEmployee(synthetic);
        var profileState = new AppState(profileRepository);
        ExerciseWindow(new EmployeeDialog(profileState, synthetic), owner, output, "dialog-employee-profile-edit");
        var profile = new EmployeeDetailWindow(profileState, synthetic) { Owner = owner };
        PrepareWindow(profile);
        profile.Show();
        Pump(profile);
        var tabs = Descendants<TabControl>(profile).FirstOrDefault() ?? throw new InvalidOperationException("Employee professional profile tabs unavailable.");
        Require(tabs.Items.Count >= 2, "employee profile offers training and professional tabs");
        tabs.SelectedIndex = 1;
        Pump(profile);
        SaveVisual(profile, Path.Combine(output, "dialog-employee-professional-profile.png"));
        profile.Close();
        Console.WriteLine("PASS: synthetic import preview and professional profile dialogs");
    }

    private static void AddSyntheticSupplementSheet(string path, string employeeName)
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace documentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        XDocument Read(string name) { using var stream = archive.GetEntry(name)!.Open(); return XDocument.Load(stream); }
        void Write(string name, XDocument document)
        {
            archive.GetEntry(name)?.Delete();
            using var stream = archive.CreateEntry(name).Open(); document.Save(stream);
        }
        var workbook = Read("xl/workbook.xml");
        workbook.Root!.Element(spreadsheet + "sheets")!.Add(new XElement(spreadsheet + "sheet", new XAttribute("name", "Chứng chỉ chi tiết"), new XAttribute("sheetId", 2), new XAttribute(documentRelationships + "id", "rIdSupplement")));
        Write("xl/workbook.xml", workbook);
        var relations = Read("xl/_rels/workbook.xml.rels");
        relations.Root!.Add(new XElement(relationships + "Relationship", new XAttribute("Id", "rIdSupplement"), new XAttribute("Type", documentRelationships.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/sheet2.xml")));
        Write("xl/_rels/workbook.xml.rels", relations);
        var types = Read("[Content_Types].xml");
        types.Root!.Add(new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet2.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Write("[Content_Types].xml", types);
        XElement Cell(string reference, string text) => new(spreadsheet + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), new XElement(spreadsheet + "is", new XElement(spreadsheet + "t", text)));
        XElement Row(int number, params XElement[] cells) => new(spreadsheet + "row", new XAttribute("r", number), cells);
        var sheet = new XDocument(new XElement(spreadsheet + "worksheet", new XElement(spreadsheet + "sheetData",
            Row(1, Cell("B1", "Họ và tên"), Cell("C1", "Tên chứng chỉ"), Cell("D1", "Thời gian học"), Cell("E1", "Số tiết"), Cell("F1", "Ngày cấp"), Cell("G1", "Nơi cấp")),
            Row(2, Cell("B2", employeeName), Cell("C2", "Chứng chỉ minh họa năm 2025"), Cell("D2", "01/05/2025 - 10/05/2025"), Cell("E2", "8,5"), Cell("F2", "15/05/2025"), Cell("G2", "Bệnh viện Minh Họa")),
            Row(3, Cell("C3", "Chứng chỉ minh họa năm 2026"), Cell("D3", "2020-2021"), Cell("F3", "01/06/2026"))),
            new XElement(spreadsheet + "mergeCells", new XAttribute("count", 1), new XElement(spreadsheet + "mergeCell", new XAttribute("ref", "B2:B3")))));
        Write("xl/worksheets/sheet2.xml", sheet);
    }

    private static void ExerciseImportFile(string sourcePath, string temporaryDirectory)
    {
        var before = SHA256.HashData(File.ReadAllBytes(sourcePath));
        var preview = EmployeeImportService.Read(sourcePath);
        Require(preview.Rows.Count == 50, "user workbook yields 50 source employee rows");
        Require(preview.Rows.All(row => row.IsValid), "user workbook preview has no row errors");
        Require(preview.SupplementRows.All(row => row.IsValid), "user workbook supplemental certificate preview has no row errors");
        var raw = ReadRawFirstSheet(sourcePath);
        foreach (var row in preview.Rows)
        {
            Require(row.Employee.ProfessionalCertificates == raw.GetValueOrDefault($"H{row.RowNumber}", ""), "source certificate text retained");
            Require(row.Employee.ConferenceHistory == raw.GetValueOrDefault($"I{row.RowNumber}", ""), "source conference text retained");
            Require(row.Employee.ScientificActivityHistory == raw.GetValueOrDefault($"J{row.RowNumber}", ""), "source scientific activity text retained");
        }
        var repository = new TrainingRepository(Path.Combine(temporaryDirectory, "actual-import-qa.db"));
        repository.Initialize();
        var result = EmployeeImportService.Apply(repository, preview);
        Require(result.Added == 50 && result.Updated == 0, "first user workbook import adds 50 employees to isolated temporary QA database");
        var saved = repository.GetEmployees();
        var supplements = repository.GetSupplements();
        Require(saved.Count == 50 && repository.GetActivities().Count == 0, "import creates professional profiles without invented training hours");
        Require(result.SupplementsAdded == preview.SupplementRows.Count && supplements.Count == preview.SupplementRows.Count, "all actual workbook supplemental detail rows are persisted separately");
        foreach (var row in preview.Rows)
        {
            var matches = saved.Where(employee => employee.Name == row.Employee.Name && employee.SocialInsuranceNumber == row.Employee.SocialInsuranceNumber).ToArray();
            Require(matches.Length == 1, "imported source row maps uniquely to saved employee");
            var employee = matches[0];
            Require(employee.DateOfBirth == row.Employee.DateOfBirth && employee.PracticeLicenseDate == row.Employee.PracticeLicenseDate, "source dates round-trip through SQLite");
            Require(employee.ProfessionalCertificates == row.Employee.ProfessionalCertificates && employee.ConferenceHistory == row.Employee.ConferenceHistory && employee.ScientificActivityHistory == row.Employee.ScientificActivityHistory, "all raw professional notes round-trip through SQLite");
        }
        var projectedRowCount = 0;
        var projectedEmployeeCount = 0;
        foreach (var employee in saved)
        {
            var history = EmployeeTrainingHistory.Build(employee, repository.GetActivities(), supplements);
            Require(history.All(row => row.IsImported && row.Activity is null), "actual workbook history is projected as imported rows without fabricated training records");
            Require(history.All(row => !string.IsNullOrWhiteSpace(row.Title) && !string.IsNullOrWhiteSpace(row.SourceReference) && !string.IsNullOrWhiteSpace(row.SourceText)), "actual workbook grid rows retain a title, source text and stable source reference");
            Require(history.Select(row => row.SourceReference).Distinct(StringComparer.Ordinal).Count() == history.Count, "actual workbook grid contains no duplicate source row references");
            var meaningfulSource = new[] { employee.ProfessionalCertificates, employee.ConferenceHistory, employee.ScientificActivityHistory }
                .Any(text => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(line => !string.IsNullOrWhiteSpace(line.Trim().Trim('-', '•', '–', '—', '*'))));
            Require(!meaningfulSource || history.Count > 0, "actual workbook professional notes appear in the unified employee history projection");
            if (history.Count > 0) projectedEmployeeCount++;
            projectedRowCount += history.Count;
        }
        Require(repository.GetActivities().Count == 0, "projecting all actual workbook employee grids does not save any training activities");
        foreach (var detail in preview.SupplementRows)
        {
            var stored = supplements.Single(supplement => supplement.SourceSheet == detail.SheetName && supplement.SourceRow == detail.RowNumber);
            Require(stored.Title == detail.Title && stored.IssuedDate == detail.IssuedDate && stored.StartDate == detail.StartDate && stored.EndDate == detail.EndDate
                && stored.Hours == detail.Hours && stored.Provider == detail.Provider && stored.StudyPeriod == detail.StudyPeriod,
                "actual supplemental issue/study dates, hours, issuer and original study period round-trip through SQLite without inference");
        }
        var repeated = EmployeeImportService.Apply(repository, EmployeeImportService.Read(sourcePath));
        Require(repeated.Added == 0 && repeated.Updated == 0 && repeated.Skipped == 50, "re-import skips all 50 identities without duplicate employees");
        Require(repeated.SupplementsAdded == 0 && repeated.SupplementsUpdated == 0 && repeated.SupplementsSkipped == supplements.Count, "re-import skips unchanged supplemental certificates without duplicate detail rows");
        Require(repository.GetEmployees().Count == 50, "re-import keeps 50 employee records");
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourcePath))), "source user workbook remains byte-for-byte unchanged");
        Console.WriteLine($"PASS: user workbook 50 employees, {supplements.Count} supplemental certificates, {projectedRowCount} unified history rows for {projectedEmployeeCount} profiles; notes/dates retained, no invented activities, repeated import adds zero; source unchanged (temporary QA database only)");
    }

    private static Dictionary<string, string> ReadRawFirstSheet(string path)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var archive = ZipFile.OpenRead(path);
        static XDocument Read(ZipArchiveEntry entry) { using var stream = entry.Open(); return XDocument.Load(stream); }
        var strings = archive.GetEntry("xl/sharedStrings.xml") is { } shared ? Read(shared).Descendants(ns + "si").Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToArray() : [];
        var sheet = Read(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        return sheet.Descendants(ns + "c").ToDictionary(cell => (string)cell.Attribute("r")!, cell =>
        {
            var type = (string?)cell.Attribute("t");
            if (type == "s") return strings[int.Parse(cell.Element(ns + "v")!.Value, CultureInfo.InvariantCulture)];
            if (type == "inlineStr") return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
            return cell.Element(ns + "v")?.Value ?? "";
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static T Control<T>(DependencyObject root, string id) where T : DependencyObject => Descendants<T>(root)
        .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id)
        ?? throw new InvalidOperationException($"Smoke control unavailable: {id}.");

    private static void ExercisePrint(string output)
    {
        var factory = typeof(PrintService).GetMethod("CreateDocument", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Print document factory unavailable.");
        string[] headers = ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"];
        var rows = Enumerable.Range(1, 60).Select(index => (IEnumerable<string>)new[] { $"NV{index:000}", "Nguyễn Thị Ánh", "Khoa Nội Tổng hợp", "12,5" }).ToArray();
        var document = (FlowDocument)factory.Invoke(null, ["Báo cáo đào tạo 2026", headers, rows, "Kiểm tra phân trang tự động", 1122d, 793d, null])!;
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();
        Require(paginator.PageCount >= 2, "60 print rows span multiple pages");
        var page = paginator.GetPage(0);
        var visual = page.Visual ?? throw new InvalidOperationException("Print paginator did not return a first-page visual.");
        SaveVisual(visual, Path.Combine(output, "print-first-page.png"), page.Size.Width, page.Size.Height);
        Console.WriteLine($"PASS: print document paginated into {paginator.PageCount} pages");
        PrintChartVerifier.Verify(output);
    }

    internal static void PrepareWindow(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    internal static void Pump(FrameworkElement element)
    {
        element.UpdateLayout();
        for (var index = 0; index < 3; index++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            element.UpdateLayout();
        }
    }

    internal static void SaveVisual(Visual visual, string path, double? width = null, double? height = null)
    {
        var bounds = visual is FrameworkElement element ? new Size(element.ActualWidth, element.ActualHeight) : VisualTreeHelper.GetDescendantBounds(visual).Size;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width ?? bounds.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height ?? bounds.Height));
        var raw = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        raw.Render(visual);
        var composite = new DrawingVisual();
        using (var drawing = composite.RenderOpen())
        {
            var rectangle = new Rect(0, 0, pixelWidth, pixelHeight);
            drawing.DrawRectangle(Brushes.White, null, rectangle);
            drawing.DrawImage(raw, rectangle);
        }
        var image = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(composite);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index == args.Length - 1) throw new ArgumentException($"Missing value for {name}.");
        return args[index + 1];
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Smoke assertion failed: " + description);
    }

    private sealed class ErrorListener : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private static ResourceDictionary LoadAppResources()
    {
        // App's queued OnStartup opens the user's normal database, so load its resource subtree
        // into a plain Application while the actual controls still use their compiled BAML.
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "QuanLyDaoTao.Desktop", "App.xaml");
            if (File.Exists(path))
            {
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var source = XDocument.Load(path);
                var resources = source.Root!.Element(presentation + "Application.Resources")!;
                var dictionary = new XElement(presentation + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName), resources.Nodes());
                return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Run smoke tests from the repository root so App.xaml resources can be loaded.");
    }
}
