using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop.Views;

public sealed class EmployeeDetailWindow : Window
{
    private readonly AppState _state;
    private readonly Employee _employee;
    private readonly ComboBox _annualYear = new() { Width = 110, MinHeight = 32 };
    private readonly ComboBox _startYear = new() { Width = 110, MinHeight = 32 };
    private readonly TextBox _search = new() { MinWidth = 200, Padding = new Thickness(10, 8, 10, 8) };
    private readonly ComboBox _type = new() { Width = 145 };
    private readonly ComboBox _scope = new() { Width = 190 };
    private readonly DataGrid _table = FormUi.Table();
    private readonly TabControl _tabs = new() { Background = Brushes.Transparent, BorderBrush = FormUi.Line, BorderThickness = new Thickness(0) };
    private readonly Grid _journal = new() { Margin = new Thickness(0, 16, 0, 0) };
    private readonly StackPanel _profile = new();
    private readonly TextBlock _metadata = FormUi.Text("", 15, bold: true);
    private readonly TextBlock _count = FormUi.Text("", 14, FormUi.Teal, true);
    private readonly TextBlock _empty = FormUi.Text("", 13, FormUi.Muted);
    private readonly TextBlock _selectionDetails = FormUi.Text("", 12, FormUi.Muted);
    private readonly SummaryPanel _annualSummary;
    private readonly SummaryPanel _biennialSummary;
    private readonly Button _edit, _delete;
    private bool _refreshing;

    public EmployeeDetailWindow(AppState state, Employee employee)
    {
        _state = state; _employee = employee;
        FormUi.ConfigureDialog(this, $"Hồ sơ đào tạo · {employee.Code}", 850);
        Width = 1180; MinWidth = 900; MinHeight = 590;

        Identify(_annualYear, "EmployeeDetailAnnualYear", "Năm theo dõi mục tiêu 12 tiết");
        Identify(_startYear, "EmployeeDetailBiennialStartYear", "Năm bắt đầu giai đoạn mục tiêu 48 tiết");
        Identify(_search, "EmployeeDetailSearch", "Tìm tên khóa học, đơn vị hoặc số chứng chỉ");
        Identify(_type, "EmployeeDetailTypeFilter", "Lọc loại hình đào tạo");
        Identify(_scope, "EmployeeDetailPeriodScope", "Phạm vi thời gian của danh sách đào tạo");
        Identify(_table, "EmployeeDetailTrainingTable", "Danh sách khóa đào tạo theo ngày hoàn thành");
        Identify(_count, "EmployeeDetailDisplayedTotal", "Tổng cộng tích lũy hiển thị");
        Identify(_metadata, "EmployeeDetailMetadata", "Thông tin nhân viên");
        Identify(_tabs, "EmployeeDetailTabs", "Hồ sơ đào tạo và hồ sơ chuyên môn");

        var outer = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var windowHeading = FormUi.Text("Hồ sơ đào tạo chi tiết nhân viên", 25, bold: true);
        header.Children.Add(windowHeading);
        _metadata.Margin = new Thickness(0, 9, 0, 0); header.Children.Add(_metadata);
        DockPanel.SetDock(header, Dock.Top); outer.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _edit = FormUi.Button("Sửa hồ sơ", (_, _) => EditSelected());
        _delete = FormUi.Button("Xóa chứng chỉ", (_, _) => Delete(), danger: true);
        Identify(_edit, "EmployeeDetailEditActivity", "Sửa hoặc bổ sung thông tin đào tạo");
        Identify(_delete, "EmployeeDetailDeleteActivity", "Xóa chứng chỉ hoặc hồ sơ đào tạo đang chọn");
        var close = FormUi.Button("Đóng", (_, _) => Close()); close.IsCancel = true;
        Identify(close, "EmployeeDetailClose", "Đóng hồ sơ nhân viên");
        actions.Children.Add(_edit); actions.Children.Add(_delete); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        _count.VerticalAlignment = VerticalAlignment.Center; _count.Margin = new Thickness(0, 0, 14, 0); footer.Children.Add(_count);
        var footerBorder = new Border { BorderBrush = FormUi.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerBorder, Dock.Bottom); outer.Children.Add(footerBorder);

        for (var index = 0; index < 6; index++)
            _journal.RowDefinitions.Add(new RowDefinition { Height = index == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var summaries = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        summaries.ColumnDefinitions.Add(new ColumnDefinition()); summaries.ColumnDefinitions.Add(new ColumnDefinition());
        _annualSummary = CreateSummary("12 TIẾT / NĂM", _annualYear, "EmployeeDetailAnnualSummary");
        _biennialSummary = CreateSummary("48 TIẾT / 2 NĂM LIÊN TIẾP", _startYear, "EmployeeDetailBiennialSummary");
        _annualSummary.Card.Margin = new Thickness(0, 0, 8, 0); _biennialSummary.Card.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_biennialSummary.Card, 1); summaries.Children.Add(_annualSummary.Card); summaries.Children.Add(_biennialSummary.Card);
        _journal.Children.Add(summaries);

        var historyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var add = FormUi.Button("+  Thêm hồ sơ đào tạo", (_, _) => new ActivityDialog(_state, employeeId: employee.Id) { Owner = this }.ShowDialog(), true);
        Identify(add, "EmployeeDetailAddActivity", "Thêm hồ sơ đào tạo cho nhân viên");
        DockPanel.SetDock(add, Dock.Right); historyHeader.Children.Add(add);
        var heading = FormUi.Text("Danh sách chứng chỉ & lịch sử đào tạo", 18, bold: true); heading.VerticalAlignment = VerticalAlignment.Center; historyHeader.Children.Add(heading);
        Grid.SetRow(historyHeader, 1); _journal.Children.Add(historyHeader);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchField = FilterField("Tìm khóa học / chứng chỉ", _search); searchField.Margin = new Thickness(0, 0, 12, 0); filters.Children.Add(searchField);
        var typeField = FilterField("Loại hình", _type); typeField.Margin = new Thickness(0, 0, 12, 0); Grid.SetColumn(typeField, 1); filters.Children.Add(typeField);
        var periodField = FilterField("Phạm vi danh sách", _scope); Grid.SetColumn(periodField, 2); filters.Children.Add(periodField);
        _type.ItemsSource = new[] { "Tất cả loại hình", "CME", "SHKHKT", "Chứng chỉ", "Hội thảo" }; _type.SelectedIndex = 0;
        _scope.ItemsSource = new[] { "Tất cả các năm", "Năm đang theo dõi", "Giai đoạn 2 năm" }; _scope.SelectedIndex = 0;
        Grid.SetRow(filters, 2); _journal.Children.Add(filters);

        _table.MinHeight = 80; _table.RowHeight = double.NaN;
        FormUi.Column(_table, "Ngày hoàn thành", "EndDate", 130, "dd/MM/yyyy");
        ((Binding)((DataGridTextColumn)_table.Columns[0]).Binding).TargetNullValue = "Chưa rõ";
        FormUi.Column(_table, "Tên khóa học / Buổi SHKHKT", "Title", -1);
        FormUi.Column(_table, "Phân loại", "Type", 90); FormUi.Column(_table, "Số tiết", "Hours", 110, "0.##");
        ((Binding)((DataGridTextColumn)_table.Columns[3]).Binding).TargetNullValue = "Chưa ghi nhận";
        FormUi.Column(_table, "Ngày cấp", "IssuedDate", 110, "dd/MM/yyyy");
        ((Binding)((DataGridTextColumn)_table.Columns[4]).Binding).TargetNullValue = "Chưa rõ";
        FormUi.Column(_table, "Nơi cấp / Đơn vị tổ chức", "Provider", 180);
        var courseStyle = new Style(typeof(TextBlock)); courseStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        ((DataGridTextColumn)_table.Columns[1]).ElementStyle = courseStyle;
        ((DataGridTextColumn)_table.Columns[5]).ElementStyle = courseStyle;
        var typeStyle = new Style(typeof(TextBlock)); typeStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, FormUi.Teal)); typeStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        ((DataGridTextColumn)_table.Columns[2]).ElementStyle = typeStyle;
        var hoursStyle = new Style(typeof(TextBlock)); hoursStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        ((DataGridTextColumn)_table.Columns[3]).ElementStyle = hoursStyle;
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(EmployeeTrainingHistoryRow.SourceText))));
        _table.RowStyle = rowStyle;
        var tablePanel = new Grid(); tablePanel.Children.Add(_table);
        _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center; _empty.TextAlignment = TextAlignment.Center; _empty.IsHitTestVisible = false; _empty.Margin = new Thickness(24, 42, 24, 0); tablePanel.Children.Add(_empty);
        var tableCard = FormUi.Card(tablePanel); tableCard.Padding = new Thickness(0); Grid.SetRow(tableCard, 3); _journal.Children.Add(tableCard);
        _selectionDetails.Margin = new Thickness(0, 8, 0, 7); _selectionDetails.MaxHeight = 36;
        _selectionDetails.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(_selectionDetails, 4); _journal.Children.Add(_selectionDetails);
        var note = FormUi.Text("Số tiết trong mục tiêu chỉ tính hồ sơ đã xác nhận, theo năm bắt đầu. Chọn dòng nhập từ Excel và “Bổ sung thông tin” để ghi nhận số tiết.", 11, FormUi.Muted);
        note.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(note, 5); _journal.Children.Add(note);

        _tabs.Items.Add(new TabItem { Header = "Nhật ký đào tạo", Content = _journal, Padding = new Thickness(18, 9, 18, 9) });
        _tabs.Items.Add(new TabItem { Header = "Hồ sơ chuyên môn", Content = new ScrollViewer { Content = _profile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 16, 0, 0) }, Padding = new Thickness(18, 9, 18, 9) });
        outer.Children.Add(_tabs); Content = outer;

        void AdjustLayout()
        {
            var compact = (ActualHeight > 0 ? ActualHeight : Height) < 780;
            outer.Margin = new Thickness(compact ? 16 : 24);
            header.Margin = new Thickness(0, 0, 0, compact ? 10 : 18);
            windowHeading.FontSize = compact ? 22 : 25;
            _metadata.FontSize = compact ? 14 : 15;
            _journal.Margin = new Thickness(0, compact ? 10 : 16, 0, 0);
            summaries.Margin = new Thickness(0, 0, 0, compact ? 10 : 14);
            foreach (var summary in new[] { _annualSummary, _biennialSummary })
            {
                summary.Card.Padding = new Thickness(compact ? 14 : 18, compact ? 10 : 14, compact ? 14 : 18, compact ? 10 : 14);
                summary.Hours.FontSize = compact ? 24 : 29;
            }
            historyHeader.Margin = new Thickness(0, 0, 0, compact ? 8 : 12);
            heading.FontSize = compact ? 16 : 18;
            filters.Margin = new Thickness(0, 0, 0, compact ? 8 : 12);
            _selectionDetails.Margin = compact ? new Thickness(0, 5, 0, 5) : new Thickness(0, 8, 0, 7);
            note.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            footer.Margin = new Thickness(0, compact ? 10 : 14, 0, 0);
            _table.ColumnHeaderHeight = compact ? 38 : 42;
            _table.MinRowHeight = compact ? 40 : 44;
        }
        SizeChanged += (_, _) => AdjustLayout();
        AdjustLayout();

        _annualYear.SelectionChanged += (_, _) => PeriodChanged(); _startYear.SelectionChanged += (_, _) => PeriodChanged();
        _search.TextChanged += (_, _) => { if (!_refreshing) RefreshRows(); };
        _type.SelectionChanged += (_, _) => { if (!_refreshing) RefreshRows(); }; _scope.SelectionChanged += (_, _) => { if (!_refreshing) RefreshRows(); };
        _tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, _tabs)) UpdateActions(); };
        _table.SelectionChanged += (_, _) => { UpdateSelectionDetails(); UpdateActions(); };
        _table.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_table, source) is DataGridRow row && row.Item is EmployeeTrainingHistoryRow)
                EditSelected();
        };
        Loaded += (_, _) => { _state.Changed += StoreChanged; Refresh(); };
        Unloaded += (_, _) => _state.Changed -= StoreChanged;
        UpdateActions();
    }

    private Employee CurrentEmployee => _state.Employees.FirstOrDefault(e => e.Id == _employee.Id) ?? _employee;
    private TrainingActivity[] AllActivities => _state.Activities.Where(a => a.EmployeeId == _employee.Id).ToArray();
    private void StoreChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        var annual = _annualYear.SelectedItem as int? ?? DateTime.Today.Year;
        var start = _startYear.SelectedItem as int? ?? DateTime.Today.Year - 1;
        var years = EmployeeTrainingHistory.Build(CurrentEmployee, _state.Activities, _state.Supplements)
            .SelectMany(a => new[] { a.StartDate, a.EndDate }).Where(date => date.HasValue)
            .SelectMany(date => new[] { date!.Value.Year - 1, date.Value.Year, date.Value.Year + 1 })
            .Concat(Enumerable.Range(DateTime.Today.Year - 5, 8)).Append(annual).Append(start).Distinct().OrderDescending().ToArray();
        _annualYear.ItemsSource = years.Where(y => y >= 1 && y <= 9999).ToArray(); _annualYear.SelectedItem = Math.Clamp(annual, 1, 9999);
        _startYear.ItemsSource = years.Where(y => y >= 1 && y <= 9998).ToArray(); _startYear.SelectedItem = Math.Clamp(start, 1, 9998);
        var employee = CurrentEmployee;
        _metadata.Text = $"{employee.Code}   ·   {employee.Name}   ·   {employee.Department}";
        AutomationProperties.SetName(_metadata, _metadata.Text);
        Title = $"Hồ sơ đào tạo · {employee.Code} · {employee.Name}";
        _refreshing = false;
        RefreshSummary(); RefreshRows(); RefreshProfile(); UpdateActions();
    }

    private void PeriodChanged()
    {
        if (_refreshing) return;
        RefreshSummary(); RefreshRows();
    }

    private void RefreshSummary()
    {
        if (_annualYear.SelectedItem is not int year || _startYear.SelectedItem is not int startYear) return;
        var employees = new[] { CurrentEmployee }; var activities = AllActivities;
        var annual = ReportService.BuildAnnual(employees, activities, year).Single();
        var biennial = ReportService.BuildBiennial(employees, activities, startYear).Single();
        UpdateSummary(_annualSummary, annual, $"Năm {year}");
        UpdateSummary(_biennialSummary, biennial, $"Giai đoạn {startYear} – {startYear + 1}");
    }

    private void RefreshRows()
    {
        var selectedId = (_table.SelectedItem as EmployeeTrainingHistoryRow)?.Id;
        var annual = _annualYear.SelectedItem as int?;
        var start = _startYear.SelectedItem as int?;
        var type = _type.SelectedIndex > 0 ? _type.SelectedItem as string : null;
        var all = EmployeeTrainingHistory.Build(CurrentEmployee, _state.Activities, _state.Supplements);
        var rows = all.Where(a => (type is null || a.Type == type)
                && (_scope.SelectedIndex != 1 || a.StartDate?.Year == annual)
                && (_scope.SelectedIndex != 2 || a.StartDate?.Year == start || a.StartDate?.Year == start + 1)
                && FormUi.Matches(_search.Text, a.Title, a.Provider, a.CertificateNumber, a.Notes, a.SourceText, a.StudyPeriod))
            .ToArray();
        _table.ItemsSource = rows; _table.SelectedItem = rows.FirstOrDefault(a => a.Id == selectedId);
        _empty.Text = all.Count == 0 ? "Chưa có chứng chỉ hoặc khóa đào tạo.\nChọn “Thêm hồ sơ đào tạo” để bắt đầu." : "Không có khóa học phù hợp với bộ lọc.";
        _empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pending = rows.Count(row => row.IsImported);
        var hours = rows.Where(row => !row.IsImported).Sum(row => row.Hours ?? 0);
        _count.Text = $"Hiển thị: {rows.Length} mục · {hours:0.##} tiết đã xác nhận" + (pending > 0 ? $" · {pending} chờ xác nhận" : "");
        AutomationProperties.SetName(_count, _count.Text);
        UpdateSelectionDetails(); UpdateActions();
    }

    private static SummaryPanel CreateSummary(string title, ComboBox year, string automationId)
    {
        var content = new StackPanel();
        var header = new DockPanel(); DockPanel.SetDock(year, Dock.Right); header.Children.Add(year);
        var caption = FormUi.Text(title, 11, FormUi.Muted, true); caption.VerticalAlignment = VerticalAlignment.Center; caption.Margin = new Thickness(0, 0, 12, 0); header.Children.Add(caption); content.Children.Add(header);
        var metrics = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        var status = FormUi.Text("", 11, bold: true);
        var surface = new Border { Child = status, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(surface, Dock.Right); metrics.Children.Add(surface);
        var hours = FormUi.Text("", 29, FormUi.Teal, true); metrics.Children.Add(hours); content.Children.Add(metrics);
        var detail = FormUi.Text("", 12, FormUi.Muted); content.Children.Add(detail);
        var card = FormUi.Card(content); card.Padding = new Thickness(18, 14, 18, 14); Identify(card, automationId, title);
        return new SummaryPanel(card, hours, status, surface, detail);
    }

    private static void UpdateSummary(SummaryPanel summary, TrainingReportRow row, string period)
    {
        var color = row.Passed ? FormUi.Teal : new SolidColorBrush(Color.FromRgb(180, 83, 9));
        summary.Hours.Text = $"{row.TotalHours:0.##} tiết"; summary.Hours.Foreground = color;
        summary.Status.Text = row.Passed ? "✓  ĐÃ ĐẠT" : "CHƯA ĐẠT"; summary.Status.Foreground = color;
        summary.StatusSurface.Background = row.Passed ? new SolidColorBrush(Color.FromRgb(220, 252, 231)) : new SolidColorBrush(Color.FromRgb(254, 243, 199));
        summary.Detail.Text = row.Passed ? $"{period} · Mục tiêu {row.TargetHours:0.##} tiết" : $"{period} · Còn thiếu {row.MissingHours:0.##} / {row.TargetHours:0.##} tiết";
        AutomationProperties.SetName(summary.Card, $"{period}: {row.TotalHours:0.##} tiết / {row.TargetHours:0.##} tiết · {(row.Passed ? "ĐÃ ĐẠT" : "CHƯA ĐẠT")}");
    }

    private void UpdateSelectionDetails()
    {
        if (_table.SelectedItem is not EmployeeTrainingHistoryRow activity)
        { _selectionDetails.Text = "Chọn một dòng để xem chi tiết, sửa, bổ sung thông tin hoặc xóa chứng chỉ."; _selectionDetails.ToolTip = null; return; }
        var issued = activity.IssuedDate?.ToString("dd/MM/yyyy") ?? "Chưa rõ";
        var provider = string.IsNullOrWhiteSpace(activity.Provider) ? "Chưa rõ" : activity.Provider;
        var period = !string.IsNullOrWhiteSpace(activity.StudyPeriod) ? activity.StudyPeriod
            : activity.StartDate.HasValue && activity.EndDate.HasValue ? $"{activity.StartDate:dd/MM/yyyy} – {activity.EndDate:dd/MM/yyyy}" : "Chưa rõ";
        _selectionDetails.Text = $"Ngày học: {period}" + (activity.IsImported ? "   ·   Chờ xác nhận loại hình CME / SHKHKT." : $"   ·   Chứng chỉ: {(string.IsNullOrWhiteSpace(activity.CertificateNumber) ? "Chưa có" : activity.CertificateNumber)}")
            + $"\nNgày cấp: {issued}   ·   Nơi cấp: {provider}";
        _selectionDetails.ToolTip = _selectionDetails.Text + (string.IsNullOrWhiteSpace(activity.SourceText) ? "" : $"\nNguồn Excel: {activity.SourceText}") + (string.IsNullOrWhiteSpace(activity.Notes) ? "" : $"\n{activity.Notes}");
    }

    private void UpdateActions()
    {
        var trainingTab = _tabs.SelectedIndex <= 0;
        _count.Visibility = trainingTab ? Visibility.Visible : Visibility.Collapsed;
        _edit.Visibility = trainingTab ? Visibility.Visible : Visibility.Collapsed; _delete.Visibility = _edit.Visibility;
        var row = _table.SelectedItem as EmployeeTrainingHistoryRow;
        _edit.Content = row?.IsImported == true ? "Bổ sung thông tin" : "Sửa hồ sơ";
        _edit.IsEnabled = trainingTab && row is not null; _delete.IsEnabled = trainingTab && row is not null;
    }

    private void EditSelected()
    {
        if (_table.SelectedItem is not EmployeeTrainingHistoryRow row) return;
        if (row.Activity is not null) new ActivityDialog(_state, row.Activity with
        {
            IssuedDate = row.IssuedDate,
            StudyPeriod = row.StudyPeriod,
            Provider = row.Provider
        }) { Owner = this }.ShowDialog();
        else new ActivityDialog(_state, employeeId: _employee.Id, importedRow: row) { Owner = this }.ShowDialog();
    }

    private void RefreshProfile()
    {
        var employee = CurrentEmployee;
        _profile.Children.Clear();
        var information = new StackPanel(); information.Children.Add(FormUi.Text("Thông tin nhân viên và hành nghề", 17, bold: true));
        var fields = new (string Label, string Value)[]
        {
            ("Mã nhân viên", employee.Code), ("Họ và tên", employee.Name),
            ("Khoa / Phòng", employee.Department), ("Chức danh / Chức vụ", employee.Title),
            ("Trình độ chuyên môn", employee.Degree), ("Ngày vào làm", employee.StartDate?.ToString("dd/MM/yyyy") ?? ""),
            ("Mã số BHXH", employee.SocialInsuranceNumber), ("Ngày sinh", employee.DateOfBirth?.ToString("dd/MM/yyyy") ?? ""),
            ("Số chứng chỉ / Giấy phép hành nghề", employee.PracticeLicenseNumber), ("Ngày cấp CCHN / GPHN", employee.PracticeLicenseDate?.ToString("dd/MM/yyyy") ?? "")
        };
        var details = new Grid { Margin = new Thickness(0, 18, 0, 0) }; details.ColumnDefinitions.Add(new ColumnDefinition()); details.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < (fields.Length + 1) / 2; row++) details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < fields.Length; index++)
        {
            var field = new StackPanel { Margin = new Thickness(index % 2 == 0 ? 0 : 14, 0, index % 2 == 0 ? 14 : 0, 16) };
            field.Children.Add(FormUi.Text(fields[index].Label, 11, FormUi.Muted, true));
            var value = ReadableText(fields[index].Value); value.Margin = new Thickness(0, 6, 0, 0); field.Children.Add(value);
            Grid.SetColumn(field, index % 2); Grid.SetRow(field, index / 2); details.Children.Add(field);
        }
        information.Children.Add(details); var infoCard = FormUi.Card(information); infoCard.Margin = new Thickness(0, 0, 0, 18); _profile.Children.Add(infoCard);
        var training = new StackPanel();
        training.Children.Add(FormUi.Text("Chứng chỉ và lịch sử đào tạo", 17, bold: true));
        var note = FormUi.Text("Các chứng chỉ, hội thảo và sinh hoạt khoa học được trình bày theo từng dòng trong bảng Nhật ký đào tạo. Để xóa, mở bảng hồ sơ, chọn dòng cần xóa rồi bấm “Xóa chứng chỉ”.", 13, FormUi.Muted);
        note.Margin = new Thickness(0, 9, 0, 14); training.Children.Add(note);
        var showHistory = FormUi.Button("Xem bảng hồ sơ đào tạo", (_, _) => _tabs.SelectedIndex = 0, true);
        showHistory.HorizontalAlignment = HorizontalAlignment.Left; training.Children.Add(showHistory);
        _profile.Children.Add(FormUi.Card(training));
    }

    private static TextBox ReadableText(string value) => new()
    {
        Text = string.IsNullOrWhiteSpace(value) ? "Chưa có thông tin" : value,
        IsReadOnly = true, IsReadOnlyCaretVisible = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0,
        Foreground = string.IsNullOrWhiteSpace(value) ? FormUi.Muted : FormUi.Ink
    };

    private static StackPanel FilterField(string label, Control control)
    {
        var panel = new StackPanel(); panel.Children.Add(FormUi.Text(label, 11, FormUi.Muted)); control.Margin = new Thickness(0, 5, 0, 0); panel.Children.Add(control); return panel;
    }

    private static void Identify(DependencyObject element, string id, string name)
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); }

    private void Delete()
    {
        if (_tabs.SelectedIndex > 0 || _table.SelectedItem is not EmployeeTrainingHistoryRow row) return;
        var message = $"Xóa chứng chỉ “{row.Title}” của {CurrentEmployee.Name}?\n\nChứng chỉ và thông tin đào tạo liên quan sẽ được xóa khỏi hồ sơ nhân viên."
            + (row.Activity is null ? "" : "\nTổng số tiết và kết quả tuân thủ sẽ được cập nhật sau khi xóa.")
            + "\n\nThao tác này không thể hoàn tác.";
        if (MessageBox.Show(this, message, "Xác nhận xóa chứng chỉ", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _state.Repository.DeleteTrainingHistoryRow(_employee.Id, row.Id); _state.Reload(); }
        catch (Exception error) { FormUi.Error(this, error); }
    }

    private sealed record SummaryPanel(Border Card, TextBlock Hours, TextBlock Status, Border StatusSurface, TextBlock Detail);
}
