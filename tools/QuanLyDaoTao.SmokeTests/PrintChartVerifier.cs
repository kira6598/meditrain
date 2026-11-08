using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps.Packaging;
using System.Xml.Linq;
using QuanLyDaoTao.Core;
using QuanLyDaoTao.Desktop;

namespace QuanLyDaoTao.SmokeTests;

internal static class PrintChartVerifier
{
    public static void Verify(string output)
    {
        var report = ReportRows();
        var reportCharts = PrintChartFactory.ForReport(report, "Năm 2026 · mẫu đã lọc");
        AssertSummary(reportCharts, 3, 2);
        AssertNoDepartments(reportCharts, ["Khoa Minh Họa A", "Khoa Minh Họa B", "Khoa Riêng Tư Bị Loại"]);
        Require(!ChartText(reportCharts).Contains("NV-HIDDEN", StringComparison.Ordinal), "summary chart excludes private rows outside the print filter");

        var departments = new[]
        {
            new DepartmentReportRow { Department = "Khoa Minh Họa A", EmployeeCount = 3, BiennialPassed = 2, AnnualPassed = 1 },
            new DepartmentReportRow { Department = "Khoa Minh Họa B", EmployeeCount = 2, BiennialPassed = 2, AnnualPassed = 2 },
            new DepartmentReportRow { Department = "Khoa Minh Họa C", EmployeeCount = 4, BiennialPassed = 0, AnnualPassed = 0 }
        };
        var departmentCharts = PrintChartFactory.ForDepartments(departments, "2025–2026", "2026");
        AssertSummary(departmentCharts, 4, 5, 3, 6);
        AssertNoDepartments(departmentCharts, departments.Select(row => row.Department));
        Require(!string.IsNullOrWhiteSpace(departmentCharts[0].FirstSeries) && !string.IsNullOrWhiteSpace(departmentCharts[0].SecondSeries), "independent total pies identify both reporting periods");

        var uneven = new[]
        {
            new DepartmentReportRow { Department = "Khoa Nhỏ", EmployeeCount = 1, BiennialPassed = 1, AnnualPassed = 0 },
            new DepartmentReportRow { Department = "Khoa Lớn", EmployeeCount = 3, BiennialPassed = 0, AnnualPassed = 2 }
        };
        var unevenCharts = PrintChartFactory.ForDepartments(uneven, "2025–2026", "2027");
        AssertSummary(unevenCharts, 1, 3, 2, 2);
        AssertNoDepartments(unevenCharts, uneven.Select(row => row.Department));
        Require(ChartText(unevenCharts).Contains("2025–2026", StringComparison.Ordinal) && ChartText(unevenCharts).Contains("2027", StringComparison.Ordinal),
            "total analytics pies retain independent annual and biennial reporting periods");

        var emptyCharts = PrintChartFactory.ForReport([], "Năm 2026");
        Require(emptyCharts.Count == 1 && emptyCharts[0].IsEmpty && emptyCharts[0].Items.Sum(item => item.FirstValue) == 0, "empty data creates one neutral summary without fabricated employees");
        var zeroCharts = PrintChartFactory.ForReport([ReportRow("NV-ZERO", "Khoa Chưa Đạt", 0)], "Năm 2026");
        AssertSummary(zeroCharts, 0, 1);
        var passedCharts = PrintChartFactory.ForReport([ReportRow("NV-PASS", "Khoa Đã Đạt", 12)], "Năm 2026");
        AssertSummary(passedCharts, 1, 0);
        var fractionRows = new[] { ReportRow("NV-F1", "Khoa Mẫu", 12), ReportRow("NV-F2", "Khoa Mẫu", 0), ReportRow("NV-F3", "Khoa Mẫu", 6) };
        var fractionCharts = PrintChartFactory.ForReport(fractionRows, "Năm 2026");
        AssertSummary(fractionCharts, 1, 2);

        var data = report.Select(row => new[] { row.Code, row.Name, row.Department, row.ShkhktHours.ToString("0.##"), row.CmeHours.ToString("0.##"), row.TotalHours.ToString("0.##"), row.MissingHours.ToString("0.##"), row.Status }).ToArray();
        ExerciseDocument("report-landscape", ["Mã NV", "Họ và tên", "Khoa / Phòng", "SHKHKT", "CME", "Tổng tiết", "Còn thiếu", "Kết quả"], data, reportCharts, output, 1122, 793);
        ExerciseDocument("departments-portrait", ["Khoa / Phòng", "Nhân viên", "Đạt 48 tiết", "Đạt 12 tiết"],
            departments.Select(row => new[] { row.Department, row.EmployeeCount.ToString(), row.BiennialPassed.ToString(), row.AnnualPassed.ToString() }), departmentCharts, output, 793, 1122);
        ExerciseDocument("uneven-total-pies-landscape", ["Khoa / Phòng", "Nhân viên", "Đạt 48 tiết", "Đạt 12 tiết"],
            uneven.Select(row => new[] { row.Department, row.EmployeeCount.ToString(), row.BiennialPassed.ToString(), row.AnnualPassed.ToString() }), unevenCharts, output, 1122, 793);
        ExerciseDocument("empty-report-portrait", ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"], [], emptyCharts, output, 793, 1122);
        ExerciseDocument("zero-report-landscape", ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"], [["NV-ZERO", "Nhân Viên Minh Họa", "Khoa Chưa Đạt", "0"]], zeroCharts, output, 1122, 793);
        ExerciseDocument("passed-report-portrait", ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"], [["NV-PASS", "Nhân Viên Minh Họa", "Khoa Đã Đạt", "12"]], passedCharts, output, 793, 1122);
        ExerciseDocument("fractional-total-pie-portrait", ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"],
            fractionRows.Select(row => new[] { row.Code, row.Name, row.Department, row.TotalHours.ToString("0.##") }), fractionCharts, output, 793, 1122);

        var many = Enumerable.Range(1, 28).SelectMany(index => new[]
        {
            ReportRow($"NV-{index:00}-A", LongDepartment(index), index % 3 == 0 ? 0 : 12),
            ReportRow($"NV-{index:00}-B", LongDepartment(index), index % 3 == 2 ? 12 : 6)
        }).ToArray();
        var manyCharts = PrintChartFactory.ForReport(many, "Năm 2026");
        AssertSummary(manyCharts, many.Count(row => row.Passed), many.Count(row => !row.Passed));
        AssertNoDepartments(manyCharts, many.Select(row => row.Department));
        var manyDepartments = Enumerable.Range(1, 24).Select(index => new DepartmentReportRow
        {
            Department = LongDepartment(index), EmployeeCount = 3, BiennialPassed = index % 4, AnnualPassed = (index + 1) % 4
        }).ToArray();
        var manyDepartmentCharts = PrintChartFactory.ForDepartments(manyDepartments, "2025–2026", "2027");
        AssertSummary(manyDepartmentCharts, manyDepartments.Sum(row => row.BiennialPassed), manyDepartments.Sum(row => row.EmployeeCount - row.BiennialPassed),
            manyDepartments.Sum(row => row.AnnualPassed), manyDepartments.Sum(row => row.EmployeeCount - row.AnnualPassed));
        AssertNoDepartments(manyDepartmentCharts, manyDepartments.Select(row => row.Department));
        foreach (var layout in new[] { (Name: "landscape", Width: 1122d, Height: 793d), (Name: "portrait", Width: 793d, Height: 1122d) })
        {
            ExerciseDocument($"many-departments-{layout.Name}", ["Mã NV", "Họ và tên", "Khoa / Phòng", "Tổng tiết"],
                many.Select(row => new[] { row.Code, row.Name, row.Department, row.TotalHours.ToString("0.##") }), manyCharts, output, layout.Width, layout.Height);
            ExerciseDocument($"many-department-total-pies-{layout.Name}", ["Khoa / Phòng", "Nhân viên", "Đạt 48 tiết", "Đạt 12 tiết"],
                manyDepartments.Select(row => new[] { row.Department, row.EmployeeCount.ToString(), row.BiennialPassed.ToString(), row.AnnualPassed.ToString() }),
                manyDepartmentCharts, output, layout.Width, layout.Height);
        }
        Console.WriteLine("PASS: one total Đạt/Không đạt pie per report period, percentage-only legends, weighted filtered totals, neutral empty/0%/100%/fractional sectors; large department tables add no department charts; vector PNG/XPS pagination verified");
    }

    private static void AssertSummary(IReadOnlyList<PrintChartDefinition> charts, decimal passed, decimal failed, decimal? secondPassed = null, decimal? secondFailed = null)
    {
        Require(charts.Count == 1 && !charts[0].IsEmpty && charts[0].Items.Count == 2, "printing produces only one total-outcome definition without department details");
        var items = charts[0].Items;
        Require(items.Single(item => item.Label == "Đạt").FirstValue == passed && items.Single(item => item.Label == "Không đạt").FirstValue == failed,
            "total pie retains the exact passed/failed employee counts selected for printing");
        Require(items.Single(item => item.Label == "Đạt").SecondValue == secondPassed && items.Single(item => item.Label == "Không đạt").SecondValue == secondFailed,
            "analytics retains independent weighted total counts for its second reporting period");
    }

    private static void AssertNoDepartments(IEnumerable<PrintChartDefinition> charts, IEnumerable<string> departments)
    {
        var text = ChartText(charts);
        Require(departments.Distinct(StringComparer.Ordinal).All(department => !text.Contains(department, StringComparison.Ordinal)),
            "total summary chart definitions contain no department labels or detail sections");
    }

    private static void ExerciseDocument(string name, string[] headers, IEnumerable<IEnumerable<string>> rows,
        IReadOnlyList<PrintChartDefinition> definitions, string output, double width, double height)
    {
        var factory = typeof(PrintService).GetMethod("CreateDocument", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Print document factory unavailable.");
        var dataRows = rows.Select(row => row.ToArray()).ToArray();
        var departmentColumn = Array.FindIndex(headers, header => header == "Khoa / Phòng");
        string[] departments = departmentColumn < 0 ? [] : dataRows.Select(row => row[departmentColumn]).Distinct(StringComparer.Ordinal).ToArray();
        var document = (FlowDocument)factory.Invoke(null, ["Báo cáo đào tạo minh họa", headers, dataRows, "Dữ liệu mẫu tổng hợp để kiểm tra bản in", width, height, definitions])!;
        var blocks = document.Blocks.Cast<Block>().ToArray();
        var tableIndex = Array.FindIndex(blocks, block => block is Table);
        var chartBlocks = blocks.Select((block, index) => (Block: block as BlockUIContainer, Index: index)).Where(pair => pair.Block is not null).ToArray();
        Require(tableIndex >= 0 && chartBlocks.Length == 1 && chartBlocks[0].Index > tableIndex, "one total-summary chart block follows the complete printed data table");
        Require(((Table)blocks[tableIndex]).RowGroups.Sum(group => group.Rows.Count) == dataRows.Length + 1, "adding the summary preserves every table row and its header");
        Require(blocks[^1] is Paragraph footer && new TextRange(footer.ContentStart, footer.ContentEnd).Text.Contains("Ngày in:", StringComparison.Ordinal), "print footer follows the summary chart");
        var controls = chartBlocks.Select(pair => pair.Block!.Child as FrameworkElement ?? throw new InvalidOperationException("Chart block requires a FrameworkElement.")).ToArray();
        Require(Definition(controls[0]).Items.SequenceEqual(definitions.Single().Items), "summary chart preserves both total-outcome categories without splitting or duplication");
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();
        Require(paginator.PageCount > 0 && paginator.IsPageCountValid, "summary document has complete pagination");
        var contentWidth = width - document.PagePadding.Left - document.PagePadding.Right;
        var contentHeight = height - document.PagePadding.Top - document.PagePadding.Bottom;
        var seen = new HashSet<FrameworkElement>();
        for (var index = 0; index < paginator.PageCount; index++)
        {
            var page = paginator.GetPage(index);
            Require(page.Visual is not null && page.Size.Width <= width + 1 && page.Size.Height <= height + 1, "rendered page fits the requested printable area");
            Program.SaveVisual(page.Visual!, Path.Combine(output, $"print-{name}-page-{index + 1:00}.png"), page.Size.Width, page.Size.Height);
            foreach (var control in Descendants(page.Visual!).OfType<FrameworkElement>().Where(controls.Contains))
            {
                seen.Add(control);
                Require(control.ActualWidth > 0 && control.ActualHeight > 0 && control.ActualWidth <= contentWidth + 1 && control.ActualHeight <= contentHeight + 1, "summary dimensions fit portrait or landscape page content");
                var bounds = control.TransformToAncestor(page.Visual!).TransformBounds(new Rect(control.RenderSize));
                Require(bounds.Left >= document.PagePadding.Left - 1 && bounds.Top >= document.PagePadding.Top - 1
                    && bounds.Right <= width - document.PagePadding.Right + 1 && bounds.Bottom <= height - document.PagePadding.Bottom + 1,
                    "total pies remain inside the printable margins");
                AssertDrawingFits(control, Definition(control), departments);
            }
        }
        Require(seen.Count == 1, "exactly one summary block is rendered across all print pages");
        if (name is "report-landscape" or "departments-portrait") VerifyXps(paginator, definitions, Path.Combine(output, $"print-{name}.xps"));
        Console.WriteLine($"PASS: print {name}: {paginator.PageCount} pages, one total-summary block");
    }

    private static void VerifyXps(DocumentPaginator paginator, IReadOnlyList<PrintChartDefinition> definitions, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using (var package = new XpsDocument(path, FileAccess.ReadWrite)) XpsDocument.CreateXpsDocumentWriter(package).Write(paginator);
        using var archive = ZipFile.OpenRead(path);
        var pages = archive.Entries.Where(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.FullName, StringComparer.Ordinal).Select(entry =>
        {
            using var stream = entry.Open(); return XDocument.Load(stream);
        }).ToArray();
        Require(pages.Length == paginator.PageCount, "serialized XPS contains every paginated print page");
        var text = new StringBuilder();
        foreach (var page in pages)
        {
            Require(!page.Descendants().Any(element => element.Name.LocalName == "ImageBrush"), "serialized summary pies retain vectors rather than bitmap images");
            foreach (var glyph in page.Descendants().Where(element => element.Name.LocalName == "Glyphs"))
            {
                var value = (string?)glyph.Attribute("UnicodeString") ?? "";
                text.Append(value.StartsWith("{}", StringComparison.Ordinal) ? value[2..] : value);
            }
        }
        var serialized = WithoutWhitespace(text.ToString());
        foreach (var definition in definitions)
        {
            Require(serialized.Contains(WithoutWhitespace(definition.Title), StringComparison.Ordinal), "total chart title is retained in actual XPS vector pages");
            foreach (var label in definition.Items.Select(item => item.Label))
                Require(serialized.Contains(WithoutWhitespace(label), StringComparison.Ordinal), "Đạt/Không đạt labels are retained as Unicode print glyphs");
        }
        Require(pages.Any(page => page.Descendants().Any(element => element.Name.LocalName == "Path")), "serialized print document contains vector drawing paths");
        Console.WriteLine($"PASS: offline vector XPS {Path.GetFileName(path)}: {pages.Length} pages");
    }

    private static PrintChartDefinition Definition(FrameworkElement element)
        => element.Tag as PrintChartDefinition
            ?? element.GetType().GetProperty("Definition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(element) as PrintChartDefinition
            ?? throw new InvalidOperationException("Print chart control must expose its rendered Definition.");

    private static void AssertDrawingFits(FrameworkElement element, PrintChartDefinition definition, IReadOnlyList<string> departments)
    {
        var drawing = VisualTreeHelper.GetDrawing(element);
        Require(drawing is not null && !drawing.Bounds.IsEmpty, "total pie chart renders vector content");
        var drawings = Drawings(drawing!).ToArray();
        var glyphs = new StringBuilder();
        foreach (var item in drawings)
        {
            var bounds = item.Bounds;
            Require(bounds.IsEmpty || bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= element.ActualWidth + 1 && bounds.Bottom <= element.ActualHeight + 1, "pie circles and percentage legends fit without clipping");
            if (item is GlyphRunDrawing glyph && glyph.GlyphRun.Characters is { } characters) glyphs.Append(new string(characters.ToArray()));
        }
        var rendered = WithoutWhitespace(glyphs.ToString());
        Require(glyphs.Length > 0, "summary includes readable vector text");
        Require(departments.All(department => !rendered.Contains(WithoutWhitespace(department), StringComparison.Ordinal)), "printed summary glyphs contain no department names or detail charts");
        Require(!Regex.IsMatch(glyphs.ToString(), @"\d+\s*(?:nhân viên|người)|\d+\s*/\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "pie text excludes headcounts and numerator/denominator strings");
        var pies = ReadPies(element);
        Require(pies.Length == (string.IsNullOrWhiteSpace(definition.SecondSeries) ? 1 : 2), "summary renders exactly one overall pie for each independent reporting period");
        Require(!drawings.OfType<GeometryDrawing>().Any(item => item.Geometry is LineGeometry line && line.Bounds.Height > 30 && line.Bounds.Width < 1), "summary contains no bar-chart grid axes");
        foreach (var pie in pies)
        {
            var second = pie.Series == definition.SecondSeries;
            Require(pie.Series == definition.FirstSeries || second, "pie is associated with the correct reporting period");
            var total = definition.Items.Sum(item => second ? item.SecondValue ?? 0 : item.FirstValue);
            var passedItem = definition.Items.SingleOrDefault(item => item.Label == "Đạt");
            var passed = passedItem is null ? 0 : second ? passedItem.SecondValue ?? 0 : passedItem.FirstValue;
            AssertPie(pie, total, passed, drawings.OfType<GeometryDrawing>().ToArray());
            if (total > 0)
            {
                Require(rendered.Contains(WithoutWhitespace($"Đạt:{100m * passed / total:0.#}%"), StringComparison.Ordinal)
                    && rendered.Contains(WithoutWhitespace($"Không đạt:{100m * (total - passed) / total:0.#}%"), StringComparison.Ordinal),
                    "legend displays only the correct passed/failed percentages for the total population");
            }
            else Require(rendered.Contains(WithoutWhitespace("Chưa có dữ liệu"), StringComparison.Ordinal) && !Regex.IsMatch(rendered, @"\d+(?:[.,]\d+)?%"), "empty total pie displays no-data text without fabricated percentages");
        }
    }

    private static PieSnapshot[] ReadPies(FrameworkElement element)
    {
        var pies = Property(element, "Pies") as System.Collections.IEnumerable ?? throw new InvalidOperationException("Print control must expose its actual pie geometries.");
        return pies.Cast<object>().Select(item => new PieSnapshot(
            (string?)Property(item, "Label") ?? "", (string?)Property(item, "Series") ?? "",
            Convert.ToDecimal(Property(item, "TotalCount"), CultureInfo.InvariantCulture), Convert.ToDecimal(Property(item, "PassedCount"), CultureInfo.InvariantCulture),
            (Rect)(Property(item, "Bounds") ?? Rect.Empty), Property(item, "PassedGeometry") as Geometry, Property(item, "FailedGeometry") as Geometry)).ToArray();
    }

    private static void AssertPie(PieSnapshot pie, decimal total, decimal passed, IReadOnlyList<GeometryDrawing> drawings)
    {
        Require(pie.Total == total && pie.Passed == passed && passed >= 0 && passed <= total, "actual pie uses the true total passed/failed counts belonging to its reporting period");
        Require(!pie.Bounds.IsEmpty && pie.Bounds.Width > 20 && Math.Abs(pie.Bounds.Width - pie.Bounds.Height) <= 0.01, "pie has a positive circular plotting area");
        var fullArea = Math.PI * pie.Bounds.Width * pie.Bounds.Height / 4;
        var passedArea = pie.PassedGeometry?.GetArea(0.01, ToleranceType.Absolute) ?? 0;
        var failedArea = pie.FailedGeometry?.GetArea(0.01, ToleranceType.Absolute) ?? 0;
        if (total == 0)
        {
            Require(pie.PassedGeometry is null && pie.FailedGeometry is null, "empty total pie contains no fabricated passed/failed slices");
            Require(drawings.Any(item => HasCurves(item.Geometry) && SameBounds(item.Geometry.Bounds, pie.Bounds)), "empty summary retains a neutral circular outline");
            return;
        }
        Require(Math.Abs(passedArea / fullArea - (double)(passed / total)) <= 0.002
            && Math.Abs(failedArea / fullArea - (double)((total - passed) / total)) <= 0.002, "actual curved slice angle and area match the true total proportions");
        Require(Math.Abs((passedArea + failedArea) / fullArea - 1) <= 0.002, "passed and failed slices cover exactly one complete circle");
        if (passed == 0) Require(pie.PassedGeometry is null && pie.FailedGeometry is EllipseGeometry, "0% success renders one full failed circle");
        if (passed == total) Require(pie.FailedGeometry is null && pie.PassedGeometry is EllipseGeometry, "100% success renders one full passed circle");
        AssertRenderedSector(pie.PassedGeometry, "#0D9488", drawings);
        AssertRenderedSector(pie.FailedGeometry, "#D97706", drawings);
    }

    private static void AssertRenderedSector(Geometry? geometry, string color, IReadOnlyList<GeometryDrawing> drawings)
    {
        if (geometry is null) return;
        Require(HasCurves(geometry), "pie sectors use curved vector geometry");
        var expected = (Color)ColorConverter.ConvertFromString(color);
        var area = geometry.GetArea(0.01, ToleranceType.Absolute);
        Require(drawings.Any(item => item.Brush is SolidColorBrush brush && brush.Color == expected && HasCurves(item.Geometry)
            && SameBounds(item.Geometry.Bounds, geometry.Bounds) && Math.Abs(item.Geometry.GetArea(0.01, ToleranceType.Absolute) - area) <= 0.05), "actual vector slices retain teal for passed and amber for failed");
    }

    private static bool HasCurves(Geometry geometry) => geometry is EllipseGeometry
        || PathGeometry.CreateFromGeometry(geometry).Figures.SelectMany(figure => figure.Segments).Any(segment => segment is ArcSegment or BezierSegment or PolyBezierSegment or QuadraticBezierSegment or PolyQuadraticBezierSegment);
    private static bool SameBounds(Rect first, Rect second) => !first.IsEmpty && !second.IsEmpty
        && Math.Abs(first.Left - second.Left) <= 0.02 && Math.Abs(first.Top - second.Top) <= 0.02 && Math.Abs(first.Width - second.Width) <= 0.02 && Math.Abs(first.Height - second.Height) <= 0.02;
    private static object? Property(object item, string name) => (item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException($"Print pie property unavailable: {name}.")).GetValue(item);
    private sealed record PieSnapshot(string Label, string Series, decimal Total, decimal Passed, Rect Bounds, Geometry? PassedGeometry, Geometry? FailedGeometry);

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var descendant in Drawings(child)) yield return descendant;
        if (drawing is not DrawingGroup) yield return drawing;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, index))) yield return item;
    }

    private static IReadOnlyList<TrainingReportRow> ReportRows() => new[]
    {
        ReportRow("NV-A1", "Khoa Minh Họa A", 12), ReportRow("NV-A2", "Khoa Minh Họa A", 0), ReportRow("NV-A3", "Khoa Minh Họa A", 11.5m),
        ReportRow("NV-B1", "Khoa Minh Họa B", 12), ReportRow("NV-B2", "Khoa Minh Họa B", 16), ReportRow("NV-HIDDEN", "Khoa Riêng Tư Bị Loại", 48)
    }.Where(row => row.Code != "NV-HIDDEN").ToArray();
    private static TrainingReportRow ReportRow(string code, string department, decimal hours) => new()
    {
        Employee = new Employee { Code = code, Name = "Nhân Viên Minh Họa", Department = department }, CmeHours = hours, TotalHours = hours, TargetHours = 12
    };
    private static string LongDepartment(int index) => $"Khoa {index:00} · Điều trị chuyên sâu các bệnh lý mắt, nghiên cứu khoa học và phục hồi chức năng dành cho người bệnh tại cơ sở y tế Minh Họa";
    private static string ChartText(IEnumerable<PrintChartDefinition> charts) => string.Join("\n", charts.Select(chart => string.Join(" ", chart.Title, chart.Subtitle, chart.FirstSeries, chart.SecondSeries, string.Join(" ", chart.Items.Select(item => item.Label)))));
    private static string WithoutWhitespace(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character))).Normalize(NormalizationForm.FormC);
    private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException("Print chart smoke assertion failed: " + description); }
}

