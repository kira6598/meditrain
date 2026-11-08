using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using QuanLyDaoTao.Core;
using QuanLyDaoTao.Desktop;
using QuanLyDaoTao.Desktop.Views;

namespace QuanLyDaoTao.SmokeTests;

internal static class EmployeeDetailVerifier
{
    public static void Verify(Window owner, string temporaryDirectory, string output)
    {
        var repository = new TrainingRepository(Path.Combine(temporaryDirectory, "employee-detail-qa.db"));
        repository.Initialize();
        var employee = new Employee
        {
            Code = "NV-MAU-DETAIL", Name = "BS. Nhân Viên Minh Họa", Department = "Khoa Nội Minh Họa", Title = "Bác sĩ điều trị", Degree = "Bác sĩ CKI",
            PracticeLicenseNumber = "CCHN-MAU", PracticeLicenseDate = new DateTime(2020, 3, 15), SocialInsuranceNumber = "0012345678", DateOfBirth = new DateTime(1990, 2, 28)
        };
        var other = new Employee { Code = "NV-KHAC", Name = "Nhân Viên Khác Minh Họa", Department = "Khoa Khác" };
        repository.SaveEmployee(employee); repository.SaveEmployee(other);
        var cme2026 = Course(employee.Id, "Cập nhật cấp cứu mẫu 16 tiết", "CME", 16, new DateTime(2026, 5, 20));
        var shkhkt2026 = Course(employee.Id, "SHKHKT mẫu sáu tiết", "SHKHKT", 6, new DateTime(2026, 3, 15));
        var cme2025 = Course(employee.Id, "Hội nghị chuyên môn mẫu 30 tiết", "CME", 30, new DateTime(2025, 8, 10));
        repository.SaveActivity(cme2026); repository.SaveActivity(shkhkt2026); repository.SaveActivity(cme2025);
        repository.SaveActivity(Course(other.Id, "Khóa học của nhân viên khác", "CME", 999, new DateTime(2026, 6, 1)));
        var state = new AppState(repository);

        var annual = ReportService.BuildAnnual([employee], state.Activities, 2026).Single();
        var biennial = ReportService.BuildBiennial([employee], state.Activities, 2025).Single();
        Require(annual.TotalHours == 22 && annual.TargetHours == 12 && annual.Passed, "synthetic annual report is 22/12 passed");
        Require(biennial.TotalHours == 52 && biennial.TargetHours == 48 && biennial.Passed, "synthetic biennial report is 52/48 passed");

        var window = Open(state, employee, owner);
        try
        {
            var annualYear = Control<ComboBox>(window, "EmployeeDetailAnnualYear");
            var pairYear = Control<ComboBox>(window, "EmployeeDetailBiennialStartYear");
            var search = Control<TextBox>(window, "EmployeeDetailSearch");
            var type = Control<ComboBox>(window, "EmployeeDetailTypeFilter");
            var scope = Control<ComboBox>(window, "EmployeeDetailPeriodScope");
            var table = Control<DataGrid>(window, "EmployeeDetailTrainingTable");
            SelectYear(annualYear, 2026, window); SelectYear(pairYear, 2025, window);
            var windowText = Text(window);
            Require(windowText.Contains(employee.Name) && windowText.Contains(employee.Code) && windowText.Contains(employee.Department), "employee identity information is displayed");
            foreach (var identity in new[] { employee.Name, employee.Code, employee.Department })
            {
                var label = All(window).OfType<TextBlock>().FirstOrDefault(label => label.Text.Contains(identity));
                Require(label is not null, "employee identity label appears in active training tab");
                AssertWithinViewport(window, label!);
            }
            Require(table.Columns.Count == 6, "training history includes the four requested columns plus issue date and issuer");
            Require(table.Columns.FirstOrDefault() is DataGridTextColumn { Binding: Binding binding } && binding.Path?.Path == nameof(EmployeeTrainingHistoryRow.EndDate), "history first date column binds completion date");
            AssertRows(table, [cme2026, shkhkt2026, cme2025]);
            AssertDeleteState(window, enabled: false);
            table.SelectedItem = table.Items.OfType<EmployeeTrainingHistoryRow>().First(); Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            var tabs = Control<TabControl>(window, "EmployeeDetailTabs");
            tabs.SelectedIndex = 1; Program.Pump(window);
            AssertDeleteState(window, enabled: false, visible: false);
            tabs.SelectedIndex = 0; Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            table.SelectedItem = null; Program.Pump(window);
            AssertDeleteState(window, enabled: false);
            AssertFooter(window, 3, 52);
            AssertSummary(window, annual: true, 2026, 22, 12, passed: true);
            AssertSummary(window, annual: false, 2025, 52, 48, passed: true);
            AssertWithinViewport(window, Control<FrameworkElement>(window, "EmployeeDetailAnnualSummary"));
            AssertWithinViewport(window, Control<FrameworkElement>(window, "EmployeeDetailBiennialSummary"));
            AssertWithinViewport(window, Control<FrameworkElement>(window, "EmployeeDetailDisplayedTotal"));
            foreach (var activity in new[] { cme2026, shkhkt2026, cme2025 })
            {
                var item = table.Items.OfType<EmployeeTrainingHistoryRow>().Single(row => row.Activity?.Id == activity.Id);
                var renderedRow = table.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                Require(renderedRow is not null, "all three default history rows are rendered in first viewport");
                AssertWithinViewport(window, renderedRow!);
            }
            Capture(window, output, "detail-three-courses-default");
            Console.WriteLine($"PASS: default employee detail {window.ActualWidth:0}x{window.ActualHeight:0} shows identity, both cards, three rows and footer in first viewport");

            type.SelectedItem = type.Items.Cast<object>().First(item => string.Equals(ItemText(item), "CME", StringComparison.OrdinalIgnoreCase));
            Program.Pump(window);
            AssertRows(table, [cme2026, cme2025]);
            AssertFooter(window, 2, 46);
            AssertSummary(window, annual: true, 2026, 22, 12, passed: true);
            AssertSummary(window, annual: false, 2025, 52, 48, passed: true);
            Capture(window, output, "detail-filter-cme");

            type.SelectedIndex = 0;
            search.Text = "mẫu sáu";
            Program.Pump(window);
            AssertRows(table, [shkhkt2026]);
            AssertFooter(window, 1, 6);
            AssertSummary(window, annual: true, 2026, 22, 12, passed: true);
            AssertSummary(window, annual: false, 2025, 52, 48, passed: true);
            Capture(window, output, "detail-filter-search");

            search.Text = "không có kết quả phù hợp";
            Program.Pump(window);
            AssertRows(table, []); AssertFooter(window, 0, 0);
            AssertSummary(window, annual: true, 2026, 22, 12, passed: true);
            Capture(window, output, "detail-filter-no-results");
            search.Text = "";
            SelectYear(annualYear, 2025, window);
            AssertSummary(window, annual: true, 2025, 30, 12, passed: true);
            AssertSummary(window, annual: false, 2025, 52, 48, passed: true);
            SelectYear(pairYear, 2024, window);
            AssertSummary(window, annual: true, 2025, 30, 12, passed: true);
            AssertSummary(window, annual: false, 2024, 30, 48, passed: false);
            SelectYear(annualYear, 2024, window);
            AssertSummary(window, annual: true, 2024, 0, 12, passed: false);
            Capture(window, output, "detail-failed-periods");

            SelectYear(annualYear, 2026, window); SelectYear(pairYear, 2025, window);
            scope.SelectedIndex = 1; Program.Pump(window);
            AssertRows(table, [cme2026, shkhkt2026]); AssertFooter(window, 2, 22);
            scope.SelectedIndex = 2; Program.Pump(window);
            AssertRows(table, [cme2026, shkhkt2026, cme2025]); AssertFooter(window, 3, 52);
            scope.SelectedIndex = 0; Program.Pump(window);
            window.Width = 900; window.Height = 700; Program.Pump(window);
            Require(window.ActualWidth <= 901 && window.ActualHeight <= 701, "detail supports minimum 900x700 window");
            AssertWithinViewport(window, Control<FrameworkElement>(window, "EmployeeDetailDisplayedTotal"));
            Require(table.ActualHeight >= table.ColumnHeaderHeight + 3 * table.MinRowHeight, "minimum window reserves enough grid height for at least three rows and the column headers");
            foreach (var item in table.Items.OfType<EmployeeTrainingHistoryRow>())
            {
                var renderedRow = table.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                Require(renderedRow is not null, "minimum window renders each of the three structured history rows");
                AssertWithinViewport(window, renderedRow!);
                var rowBounds = renderedRow!.TransformToAncestor(table).TransformBounds(new Rect(renderedRow.RenderSize));
                Require(rowBounds.Top >= -1 && rowBounds.Bottom <= table.ActualHeight + 1, "all three history rows are fully inside the minimum-size grid viewport");
            }
            Capture(window, output, "detail-minimum-900x700");
            window.Width = 1280; window.Height = 900;

            var crossing = Course(employee.Id, "Khóa học mẫu qua năm", "CME", 5, new DateTime(2025, 12, 28)) with { EndDate = new DateTime(2026, 1, 3) };
            repository.SaveActivity(crossing); state.Reload(); Program.Pump(window);
            AssertRows(table, [cme2026, shkhkt2026, crossing, cme2025]); AssertFooter(window, 4, 57);
            AssertSummary(window, annual: true, 2026, 22, 12, passed: true);
            AssertSummary(window, annual: false, 2025, 57, 48, passed: true);
            SelectYear(annualYear, 2025, window);
            AssertSummary(window, annual: true, 2025, 35, 12, passed: true);
            Capture(window, output, "detail-completion-order-start-year-totals");
        }
        finally { window.Close(); }

        var emptyEmployee = new Employee { Code = "NV-EMPTY", Name = "Nhân Viên Chưa Đào Tạo", Department = "Khoa Minh Họa" };
        repository.SaveEmployee(emptyEmployee); state.Reload();
        ExerciseEmpty(state, emptyEmployee, owner, output);
        var rawEmployee = new Employee
        {
            Code = "NV-RAW", Name = "BS. Nhân Viên Minh Họa", Department = "Khoa Mắt Tổng Hợp",
            ProfessionalCertificates = "-\n- Phẫu thuật cắt dịch kính cơ bản (23/07/2025)\n- Chẩn đoán hình ảnh các bệnh lý bán phần sau (21/10/2016)\n- Ứng dụng công nghệ mới để cải thiện thị giác cho bệnh nhân(01/03/2019)\n- Phẫu thuật Phaco căn bản(11/12/2015)\n- Laser điều trị bệnh lý dịch kính và võng mạc",
            ConferenceHistory = "QLCL- Triển khai hiệu quả công tác QLCL BV tại các cơ sở y tế (28/10/2022)\nHành trình hồi phục sau Phaco : góc nhìn lấy bệnh nhân làm trung tâm (22/06/2026)",
            ScientificActivityHistory = "T1: 4\nT2: 2"
        };
        repository.SaveEmployee(rawEmployee); state.Reload();
        ExerciseImportedGrid(state, rawEmployee, owner, output);
        ExerciseSupplementedGrid(state, owner, output);
        Require(repository.GetActivities().Count == 5, "detail supplement and deletion scenarios preserve the five original training records");
        Console.WriteLine("PASS: employee detail totals, independent year selectors, filters/footer, completion sorting/start-year allocation, minimum900x700, empty/imported-only/mixed grids");
    }

    private static void ExerciseEmpty(AppState state, Employee employee, Window owner, string output)
    {
        var window = Open(state, employee, owner);
        try
        {
            SelectYear(Control<ComboBox>(window, "EmployeeDetailAnnualYear"), 2026, window);
            SelectYear(Control<ComboBox>(window, "EmployeeDetailBiennialStartYear"), 2025, window);
            AssertRows(Control<DataGrid>(window, "EmployeeDetailTrainingTable"), []);
            AssertDeleteState(window, enabled: false);
            AssertFooter(window, 0, 0);
            AssertSummary(window, annual: true, 2026, 0, 12, passed: false);
            AssertSummary(window, annual: false, 2025, 0, 48, passed: false);
            Require(!All(window).OfType<Expander>().Any(expander => expander.IsVisible), "training history does not use a separate raw-text expander");
            Capture(window, output, "detail-no-activities");
        }
        finally { window.Close(); }
    }

    private static void ExerciseImportedGrid(AppState state, Employee employee, Window owner, string output)
    {
        var initialActivityCount = state.Activities.Count;
        var window = Open(state, employee, owner);
        TrainingActivity? completed = null;
        try
        {
            var table = Control<DataGrid>(window, "EmployeeDetailTrainingTable");
            var search = Control<TextBox>(window, "EmployeeDetailSearch");
            var type = Control<ComboBox>(window, "EmployeeDetailTypeFilter");
            var scope = Control<ComboBox>(window, "EmployeeDetailPeriodScope");
            SelectYear(Control<ComboBox>(window, "EmployeeDetailAnnualYear"), 2026, window);
            SelectYear(Control<ComboBox>(window, "EmployeeDetailBiennialStartYear"), 2025, window);
            var rows = table.Items.OfType<EmployeeTrainingHistoryRow>().ToArray();
            Require(table.Columns.Count == 6 && rows.Length == 9 && rows.All(row => row.IsImported), "five certificates, two conferences and two scientific notes are individual rows in the same grid with additional issue-date and issuer columns");
            Require(rows.Count(row => row.Type == "Chứng chỉ") == 5 && rows.Count(row => row.Type == "Hội thảo") == 2 && rows.Count(row => row.Type == "SHKHKT") == 2, "source categories remain distinguishable without assigning CME credits");
            Require(rows.All(row => row.Hours is null), "the workbook notes do not invent any confirmed training hours");
            Require(rows[0].EndDate == new DateTime(2026, 6, 22) && rows[1].EndDate == new DateTime(2025, 7, 23), "known completion dates are extracted and sorted descending");
            var undated = rows.Single(row => row.Title.StartsWith("Laser", StringComparison.Ordinal));
            Require(undated.StartDate is null && undated.EndDate is null && undated.Hours is null, "undated certificate remains explicitly incomplete");
            AssertDeleteState(window, enabled: false);
            table.SelectedItem = undated; Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            table.SelectedItem = rows[0]; table.ScrollIntoView(rows[0]); Program.Pump(window);
            Require(rows.Where(row => row.Title.StartsWith("T", StringComparison.Ordinal)).All(row => row.EndDate is null && row.Hours is null), "month-only scientific notes do not imply a year or hours");
            Require(rows.Any(row => row.SourceText.Contains("23/07/2025")), "source text is retained while titles have structured display dates");
            Require(!All(window).OfType<Expander>().Any(expander => expander.IsVisible), "imported records use the main grid without raw-text cards");
            AssertFooter(window, 9, 0);
            AssertSummary(window, annual: true, 2026, 0, 12, passed: false);
            AssertSummary(window, annual: false, 2025, 0, 48, passed: false);
            AssertWithinViewport(window, Control<FrameworkElement>(window, "EmployeeDetailDisplayedTotal"));
            var firstRow = table.ItemContainerGenerator.ContainerFromItem(rows[0]) as DataGridRow;
            var thirdRow = table.ItemContainerGenerator.ContainerFromItem(rows[2]) as DataGridRow;
            Require(firstRow is not null && thirdRow is not null, "imported grid renders several rows immediately on opening");
            AssertWithinViewport(window, firstRow!); AssertWithinViewport(window, thirdRow!);
            Capture(window, output, "detail-imported-certificates-grid");

            type.SelectedItem = type.Items.Cast<object>().First(item => ItemText(item) == "Chứng chỉ"); Program.Pump(window);
            Require(table.Items.OfType<EmployeeTrainingHistoryRow>().Count() == 5, "certificate filter includes imported certificate rows");
            AssertFooter(window, 5, 0);
            type.SelectedIndex = 0;
            search.Text = "Phaco"; Program.Pump(window);
            Require(table.Items.OfType<EmployeeTrainingHistoryRow>().Count() == 2, "search includes imported certificate and conference titles");
            AssertSummary(window, annual: true, 2026, 0, 12, passed: false);
            search.Text = ""; scope.SelectedIndex = 1; Program.Pump(window);
            Require(table.Items.OfType<EmployeeTrainingHistoryRow>().Single().EndDate == new DateTime(2026, 6, 22), "annual grid filter uses known imported dates and excludes unknown dates");
            scope.SelectedIndex = 0; Program.Pump(window);

            VerifyImportedDraft(state, employee, undated, owner, output);
            Require(state.Repository.GetActivities().Count == initialActivityCount, "opening imported grid and supplement dialog does not save invented training records");

            var original = rows.Single(row => row.EndDate == new DateTime(2025, 7, 23));
            completed = CompleteImportedViaDialog(state, employee, original, owner, output);
            Program.Pump(window);
            var mixed = table.Items.OfType<EmployeeTrainingHistoryRow>().ToArray();
            Require(mixed.Length == 9 && mixed.Count(row => row.IsImported) == 8 && mixed.Count(row => row.Activity?.Id == completed.Id) == 1, "completing an imported row replaces it in the mixed grid without duplication");
            AssertFooter(window, 9, 16);
            AssertSummary(window, annual: true, 2026, 0, 12, passed: false);
            AssertSummary(window, annual: false, 2025, 16, 48, passed: false);
            var persisted = state.Repository.GetEmployees().Single(row => row.Id == employee.Id);
            Require(persisted.ProfessionalCertificates == employee.ProfessionalCertificates && persisted.ConferenceHistory == employee.ConferenceHistory && persisted.ScientificActivityHistory == employee.ScientificActivityHistory, "completing a row preserves all original imported employee notes");
            type.SelectedItem = type.Items.Cast<object>().First(item => ItemText(item) == "CME"); Program.Pump(window);
            AssertRows(table, [completed]); AssertFooter(window, 1, 16);
            type.SelectedIndex = 0; Program.Pump(window);
            Capture(window, output, "detail-mixed-imported-confirmed-grid");
        }
        finally
        {
            window.Close();
            if (completed is not null) { state.Repository.DeleteActivity(completed.Id); state.Reload(); }
        }
    }

    private static void VerifyImportedDraft(AppState state, Employee employee, EmployeeTrainingHistoryRow row, Window owner, string output)
    {
        var dialog = new ActivityDialog(state, employeeId: employee.Id, importedRow: row) { Owner = owner };
        Program.PrepareWindow(dialog); dialog.Show(); Program.Pump(dialog);
        try
        {
            Require(Control<TextBox>(dialog, "ActivityDialogTitle").Text == row.Title, "supplement dialog is prefilled from the selected imported row");
            Require(Control<DatePicker>(dialog, "ActivityDialogStartDate").SelectedDate is null && Control<DatePicker>(dialog, "ActivityDialogEndDate").SelectedDate is null, "supplement dialog leaves unknown dates empty");
            Require(string.IsNullOrEmpty(Control<TextBox>(dialog, "ActivityDialogHours").Text), "supplement dialog leaves unreported hours empty");
            Require(Control<ComboBox>(dialog, "ActivityDialogType").SelectedItem is null, "an imported professional certificate does not silently become CME or SHKHKT");
            Capture(dialog, output, "detail-imported-supplement-dialog");
        }
        finally { dialog.Close(); }
    }

    private static void ExerciseSupplementedGrid(AppState state, Window owner, string output)
    {
        var employee = new Employee
        {
            Code = "NV-SUPPLEMENT", Name = "Nhân Viên Có Chứng Chỉ Bổ Sung", Department = "Khoa Minh Họa",
            ProfessionalCertificates = "Khóa học có thông tin bổ sung (23/07/2025)\nChứng chỉ chỉ có ngày cấp"
        };
        state.Repository.SaveEmployee(employee);
        var raw = EmployeeTrainingHistory.Build(employee, []);
        var known = new TrainingSupplement
        {
            EmployeeId = employee.Id, Title = "Khóa học có thông tin bổ sung", IssuedDate = new DateTime(2025, 7, 23),
            StartDate = new DateTime(2025, 7, 15), EndDate = new DateTime(2025, 7, 20), Hours = 8.5m,
            Provider = "Bệnh viện Minh Họa", StudyPeriod = "15/07/2025 - 20/07/2025", SourceReference = "supplement:known",
            LinkedSourceReference = raw.Single(row => row.Title == "Khóa học có thông tin bổ sung").SourceReference,
            SourceSheet = "Chứng chỉ chi tiết", SourceRow = 3
        };
        var incomplete = new TrainingSupplement
        {
            EmployeeId = employee.Id, Title = "Chứng chỉ chỉ có ngày cấp", IssuedDate = new DateTime(2022, 1, 12), Hours = 4,
            StudyPeriod = "2020-2021", SourceReference = "supplement:incomplete",
            LinkedSourceReference = raw.Single(row => row.Title == "Chứng chỉ chỉ có ngày cấp").SourceReference,
            SourceSheet = "Chứng chỉ chi tiết", SourceRow = 4
        };
        state.Repository.SaveSupplement(known); state.Repository.SaveSupplement(incomplete); state.Reload();
        var window = Open(state, employee, owner);
        TrainingActivity? confirmed = null;
        try
        {
            SelectYear(Control<ComboBox>(window, "EmployeeDetailAnnualYear"), 2025, window);
            SelectYear(Control<ComboBox>(window, "EmployeeDetailBiennialStartYear"), 2025, window);
            var table = Control<DataGrid>(window, "EmployeeDetailTrainingTable");
            var rows = table.Items.OfType<EmployeeTrainingHistoryRow>().ToArray();
            Require(rows.Length == 2 && rows.All(row => row.IsImported && row.Supplement is not null), "supplement details replace matching raw lines in the unified grid");
            Require(table.Columns[4] is DataGridTextColumn { Binding: Binding issuedBinding } && issuedBinding.Path?.Path == nameof(EmployeeTrainingHistoryRow.IssuedDate), "issue date has a separate grid column");
            Require(table.Columns[5] is DataGridTextColumn { Binding: Binding providerBinding } && providerBinding.Path?.Path == nameof(EmployeeTrainingHistoryRow.Provider), "issuing organization has a separate grid column");
            var row = rows.Single(row => row.SourceReference == known.SourceReference);
            Require(row.EndDate == known.EndDate && row.IssuedDate == known.IssuedDate && row.Hours == 8.5m && row.Provider == known.Provider && row.StudyPeriod == known.StudyPeriod,
                "known study date, issue date, hours, issuer and original study period appear separately");
            var unknown = rows.Single(row => row.SourceReference == incomplete.SourceReference);
            Require(unknown.StartDate is null && unknown.EndDate is null && unknown.IssuedDate == incomplete.IssuedDate,
                "issue date is not substituted for an unknown study date");
            AssertFooter(window, 2, 0); AssertSummary(window, annual: true, 2025, 0, 12, passed: false);
            table.SelectedItem = row; Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            Require(Text(window).Contains(known.Provider) && Text(window).Contains("23/07/2025"), "supplement grid displays issuer and issue date");
            table.SelectedItem = unknown; Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            Capture(window, output, "detail-supplemented-certificates-grid");
            Capture(window, output, "detail-pending-certificate-delete-enabled");
            VerifyIncompleteSupplementDraft(state, employee, unknown, owner, output);
            confirmed = CompleteImportedViaDialog(state, employee, row, owner, output); Program.Pump(window);
            var mixed = table.Items.OfType<EmployeeTrainingHistoryRow>().ToArray();
            Require(mixed.Length == 2 && mixed.Count(row => row.IsImported) == 1 && mixed.Single(row => row.Activity?.Id == confirmed.Id).IssuedDate == known.IssuedDate,
                "supplement promotion keeps issue metadata and does not duplicate the original row");
            AssertFooter(window, 2, 16); AssertSummary(window, annual: true, 2025, 16, 12, passed: true);
            table.SelectedItem = mixed.Single(item => item.Activity?.Id == confirmed.Id); Program.Pump(window);
            AssertDeleteState(window, enabled: true);
            Capture(window, output, "detail-confirmed-supplement-grid");
            VerifyPendingCertificateDeletion(state, employee, window, table, confirmed, unknown, owner, output);
        }
        finally
        {
            window.Close();
            if (confirmed is not null) { state.Repository.DeleteActivity(confirmed.Id); state.Reload(); }
        }
    }

    private static void VerifyPendingCertificateDeletion(AppState state, Employee employee, EmployeeDetailWindow window, DataGrid table,
        TrainingActivity confirmed, EmployeeTrainingHistoryRow pending, Window owner, string output)
    {
        var otherEmployees = state.Employees.Where(item => item.Id != employee.Id).OrderBy(item => item.Id).ToArray();
        var otherActivities = state.Activities.Where(item => item.EmployeeId != employee.Id).OrderBy(item => item.Id).ToArray();
        var otherSupplements = state.Supplements.Where(item => item.EmployeeId != employee.Id).OrderBy(item => item.Id).ToArray();
        table.SelectedItem = table.Items.OfType<EmployeeTrainingHistoryRow>().Single(item => item.Id == pending.Id); Program.Pump(window);
        AssertDeleteState(window, enabled: true);
        Require(state.Repository.DeleteTrainingHistoryRow(employee.Id, pending.Id), "pending certificate deletion succeeds for the selected employee and row");
        state.Reload(); Program.Pump(window);
        AssertRows(table, [confirmed]); AssertFooter(window, 1, 16);
        AssertSummary(window, annual: true, 2025, 16, 12, passed: true);
        AssertSummary(window, annual: false, 2025, 16, 48, passed: false);
        AssertDeleteState(window, enabled: false);
        Require(!state.Supplements.Any(item => item.Id == pending.Supplement?.Id), "pending certificate deletion removes its supplemental source record");
        var savedEmployee = state.Employees.Single(item => item.Id == employee.Id);
        Require(!savedEmployee.ProfessionalCertificates.Contains(pending.Title, StringComparison.Ordinal), "pending certificate deletion removes the linked raw note rather than allowing a fallback row to reappear");
        Require(state.Activities.Single(item => item.Id == confirmed.Id) == confirmed, "deleting the pending certificate leaves the other confirmed course unchanged");
        Require(otherEmployees.SequenceEqual(state.Employees.Where(item => item.Id != employee.Id).OrderBy(item => item.Id))
            && otherActivities.SequenceEqual(state.Activities.Where(item => item.EmployeeId != employee.Id).OrderBy(item => item.Id))
            && otherSupplements.SequenceEqual(state.Supplements.Where(item => item.EmployeeId != employee.Id).OrderBy(item => item.Id)),
            "deleting a certificate leaves every other employee and their history unchanged");
        Capture(window, output, "detail-pending-certificate-deleted");
        var reopened = Open(state, employee, owner);
        try
        {
            SelectYear(Control<ComboBox>(reopened, "EmployeeDetailAnnualYear"), 2025, reopened);
            SelectYear(Control<ComboBox>(reopened, "EmployeeDetailBiennialStartYear"), 2025, reopened);
            AssertRows(Control<DataGrid>(reopened, "EmployeeDetailTrainingTable"), [confirmed]);
            AssertFooter(reopened, 1, 16); AssertSummary(reopened, annual: true, 2025, 16, 12, passed: true);
            AssertDeleteState(reopened, enabled: false);
        }
        finally { reopened.Close(); }
        Console.WriteLine("PASS: raw, pending and confirmed rows enable certificate deletion; deleting a pending supplement removes its linked note across reload/reopen and preserves all other history and credits");
    }

    private static void VerifyIncompleteSupplementDraft(AppState state, Employee employee, EmployeeTrainingHistoryRow row, Window owner, string output)
    {
        var dialog = new ActivityDialog(state, employeeId: employee.Id, importedRow: row) { Owner = owner };
        Program.PrepareWindow(dialog); dialog.Show(); Program.Pump(dialog);
        try
        {
            Require(Control<DatePicker>(dialog, "ActivityDialogStartDate").SelectedDate is null && Control<DatePicker>(dialog, "ActivityDialogEndDate").SelectedDate is null,
                "supplement form leaves unknown study dates empty despite a known issue date");
            Require(Control<DatePicker>(dialog, "ActivityDialogIssuedDate").SelectedDate == row.IssuedDate, "supplement form preserves the known issue date");
            Require(Control<TextBox>(dialog, "ActivityDialogStudyPeriod").Text == row.StudyPeriod, "supplement form displays original study period text");
            Require(Control<TextBox>(dialog, "ActivityDialogHours").Text == "4", "supplement form prefills known hours");
            Require(Control<ComboBox>(dialog, "ActivityDialogType").SelectedItem is null, "supplement certificate requires explicit CME or SHKHKT confirmation");
            Capture(dialog, output, "detail-supplement-unknown-study-date");
        }
        finally { dialog.Close(); }
    }

    private static TrainingActivity CompleteImportedViaDialog(AppState state, Employee employee, EmployeeTrainingHistoryRow row, Window owner, string output)
    {
        var initialCount = state.Repository.GetActivities().Count;
        var dialog = new ActivityDialog(state, employeeId: employee.Id, importedRow: row) { Owner = owner };
        Program.PrepareWindow(dialog);
        Exception? callbackError = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            try
            {
                var type = Control<ComboBox>(dialog, "ActivityDialogType");
                var provider = Control<TextBox>(dialog, "ActivityDialogProvider");
                var hours = Control<TextBox>(dialog, "ActivityDialogHours");
                var start = Control<DatePicker>(dialog, "ActivityDialogStartDate");
                var end = Control<DatePicker>(dialog, "ActivityDialogEndDate");
                var save = Control<Button>(dialog, "ActivityDialogSave");
                Require(start.SelectedDate == row.StartDate && end.SelectedDate == row.EndDate, "supplement form prefills known source dates");
                Require(provider.Text == row.Provider, "supplement form prefills the known issuing organization");
                Require(Control<DatePicker>(dialog, "ActivityDialogIssuedDate").SelectedDate == row.IssuedDate
                    && (string.IsNullOrWhiteSpace(row.StudyPeriod) || Control<TextBox>(dialog, "ActivityDialogStudyPeriod").Text == row.StudyPeriod),
                    "supplement form prefills issue date and original study period separately");
                provider.Text = "Đơn vị Minh Họa"; hours.Text = "16";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));
                Require(dialog.IsVisible && dialog.DialogResult is null && state.Repository.GetActivities().Count == initialCount,
                    "save rejects missing training classification and leaves the imported row uncredited");
                Require(Text(dialog).Contains("xác nhận loại hình", StringComparison.OrdinalIgnoreCase), "missing type validation is shown in the supplement form");
                type.SelectedItem = "CME";
                start.SelectedDate = null;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));
                Require(dialog.IsVisible && dialog.DialogResult is null && state.Repository.GetActivities().Count == initialCount,
                    "save rejects a missing study date without creating a training record");
                Require(Text(dialog).Contains("chọn đầy đủ ngày", StringComparison.OrdinalIgnoreCase), "missing date validation is shown in the supplement form");
                start.SelectedDate = row.StartDate; end.SelectedDate = row.EndDate;
                Capture(dialog, output, "detail-imported-supplement-valid");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));
                Require(!dialog.IsVisible && dialog.DialogResult == true, "valid supplement is saved through the form and closes its modal window");
            }
            catch (Exception error)
            {
                callbackError = error;
                dialog.Close();
            }
        }));
        var result = dialog.ShowDialog();
        if (callbackError is not null) throw new InvalidOperationException("Imported supplement form smoke test failed.", callbackError);
        Require(result == true, "supplement modal reports successful save");
        var saved = state.Repository.GetActivities();
        Require(saved.Count == initialCount + 1, "valid supplement persists exactly one training activity");
        var completed = saved.Single(activity => activity.EmployeeId == employee.Id && activity.SourceReference == row.SourceReference);
        Require(completed.Title == row.Title && completed.Type == "CME" && completed.Hours == 16 && completed.StartDate == row.StartDate && completed.EndDate == row.EndDate,
            "supplement save persists the source-linked title, classification, dates and confirmed hours");
        Require(completed.IssuedDate == row.IssuedDate && completed.StudyPeriod == row.StudyPeriod, "supplement save retains separate issue date and original study period");
        Require(state.Activities.Any(activity => activity.Id == completed.Id), "successful supplement save reloads application state");
        Console.WriteLine("PASS: imported supplement form rejects missing type/date, then saves one linked CME record with 16 hours without source duplication");
        return completed;
    }

    private static EmployeeDetailWindow Open(AppState state, Employee employee, Window owner)
    {
        var window = new EmployeeDetailWindow(state, employee) { Owner = owner };
        Program.PrepareWindow(window);
        window.Show(); Program.Pump(window);
        return window;
    }

    private static void SelectYear(ComboBox selector, int year, Window window)
    {
        var item = selector.Items.Cast<object>().FirstOrDefault(item => int.TryParse(ItemText(item), out var value) && value == year)
            ?? throw new InvalidOperationException($"Employee detail selector does not offer year {year}.");
        selector.SelectedItem = item; Program.Pump(window);
    }

    private static void AssertSummary(Window window, bool annual, int year, decimal hours, decimal target, bool passed)
    {
        var summary = Control<FrameworkElement>(window, annual ? "EmployeeDetailAnnualSummary" : "EmployeeDetailBiennialSummary");
        var description = AutomationProperties.GetName(summary) + " " + Text(summary);
        Require(Number(description, year) && Number(description, hours) && Number(description, target), $"{(annual ? "annual" : "biennial")} card displays selected year {year}, total {hours}, target {target}");
        var failing = description.Contains("Chưa đạt", StringComparison.OrdinalIgnoreCase) || description.Contains("Còn thiếu", StringComparison.OrdinalIgnoreCase);
        Require(passed ? !failing && description.Contains("Đạt", StringComparison.OrdinalIgnoreCase) : failing, "compliance card shows correct pass/fail status");
    }

    private static void AssertRows(DataGrid table, IReadOnlyList<TrainingActivity> expected)
    {
        var actual = table.Items.OfType<EmployeeTrainingHistoryRow>().ToArray();
        Require(actual.Length == expected.Count && actual.Select(row => row.Activity?.Id).SequenceEqual(expected.Select(activity => activity.Id)), "history has correct filtered employee rows sorted by completion date descending");
    }

    private static void AssertFooter(Window window, int count, decimal hours)
    {
        var footer = Control<FrameworkElement>(window, "EmployeeDetailDisplayedTotal");
        var description = AutomationProperties.GetName(footer) + " " + Text(footer);
        Require(Number(description, count) && Number(description, hours), $"displayed footer contains {count} rows and {hours} hours");
    }

    private static void AssertDeleteState(Window window, bool enabled, bool visible = true)
    {
        var button = Control<Button>(window, "EmployeeDetailDeleteActivity");
        Require(button.Content?.ToString() == "Xóa chứng chỉ", "delete action has a clear certificate label for all row sources");
        Require(button.IsEnabled == enabled, enabled ? "selected certificate can be deleted regardless of source or confirmation state" : "delete action is disabled without an active selected training row");
        Require(button.IsVisible == visible, "delete action is displayed only in the training history tab");
    }

    private static void AssertWithinViewport(Window window, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        Require(element.IsVisible && bounds.Top >= -1 && bounds.Left >= -1 && bounds.Bottom <= window.ActualHeight + 1 && bounds.Right <= window.ActualWidth + 1,
            "default metadata, summary cards, history rows and footer fit inside first window viewport");
    }

    private static bool Number(string text, decimal value) => Regex.IsMatch(text, $@"(?<!\d){Regex.Escape(value.ToString("0.##", CultureInfo.CurrentCulture))}(?!\d)");
    private static string ItemText(object item) => item is ComboBoxItem { Content: { } content } ? content.ToString() ?? "" : item.ToString() ?? "";
    private static void Capture(Window window, string output, string name) { Program.Pump(window); Program.SaveVisual(window, Path.Combine(output, name + ".png")); }
    private static TrainingActivity Course(string employeeId, string title, string type, decimal hours, DateTime date) => new() { EmployeeId = employeeId, Title = title, Type = type, Hours = hours, Provider = "Đơn vị Minh Họa", StartDate = date, EndDate = date, CertificateNumber = "CME-MAU" };

    private static T Control<T>(DependencyObject root, string id) where T : DependencyObject => All(root).OfType<T>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id)
        ?? throw new InvalidOperationException($"Employee detail control not found: {id}.");

    private static string Text(DependencyObject root) => string.Join(" ", All(root).Select(node => node switch { TextBlock text => text.Text, TextBox text => text.Text, _ => "" }).Where(text => !string.IsNullOrEmpty(text)));
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var descendant in All(VisualTreeHelper.GetChild(root, index))) yield return descendant;
    }
    private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException("Employee detail smoke assertion failed: " + description); }
}
