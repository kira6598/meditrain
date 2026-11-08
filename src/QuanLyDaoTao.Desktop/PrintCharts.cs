using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop;

public sealed record PrintChartItem
{
    public string Label { get; init; } = "";
    public decimal FirstValue { get; init; }
    public decimal? SecondValue { get; init; }
}

public sealed record PrintChartDefinition
{
    public string Title { get; init; } = "Tỷ lệ Đạt / Không đạt";
    public string Subtitle { get; init; } = "";
    public string FirstSeries { get; init; } = "";
    public string? SecondSeries { get; init; }
    public bool IsEmpty { get; init; }
    public IReadOnlyList<PrintChartItem> Items { get; init; } = [];
}

/// <summary>Pie proportions use total passed + failed employees in the printed table.</summary>
public static class PrintChartFactory
{
    public static IReadOnlyList<PrintChartDefinition> ForReport(IReadOnlyList<TrainingReportRow> rows, string periodLabel)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var passed = rows.Count(row => row.Passed);
        return [new PrintChartDefinition
        {
            FirstSeries = periodLabel, IsEmpty = rows.Count == 0,
            Items = rows.Count == 0 ? [] :
            [
                new() { Label = "Đạt", FirstValue = passed },
                new() { Label = "Không đạt", FirstValue = rows.Count - passed }
            ]
        }];
    }

    public static IReadOnlyList<PrintChartDefinition> ForDepartments(IReadOnlyList<DepartmentReportRow> rows, string biennialPeriod, string annualPeriod)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var total = rows.Sum(row => row.EmployeeCount);
        var biennialPassed = rows.Sum(row => row.BiennialPassed);
        var annualPassed = rows.Sum(row => row.AnnualPassed);
        return [new PrintChartDefinition
        {
            FirstSeries = $"48 tiết · {biennialPeriod}", SecondSeries = $"12 tiết · {annualPeriod}", IsEmpty = total == 0,
            Items = total == 0 ? [] :
            [
                new() { Label = "Đạt", FirstValue = biennialPassed, SecondValue = annualPassed },
                new() { Label = "Không đạt", FirstValue = total - biennialPassed, SecondValue = total - annualPassed }
            ]
        }];
    }
}

internal sealed record PrintPieDiagnostic(string Label, string Series, int TotalCount, int PassedCount,
    Rect Bounds, Geometry? PassedGeometry, Geometry? FailedGeometry);

/// <summary>Only summary pies, with percentages on their slices and in their legends.</summary>
internal sealed class PrintChartVisual : FrameworkElement
{
    private const double Padding = 14, Gap = 18;
    private static readonly Brush Ink = ColorBrush("#0F172A"), Muted = ColorBrush("#526674"), Line = ColorBrush("#D9E3E8");
    private static readonly Brush Passed = ColorBrush("#0D9488"), Failed = ColorBrush("#D97706");
    private readonly Layout layout;
    private readonly double drawingWidth, scale;
    public PrintChartDefinition Definition { get; }
    public IReadOnlyList<PrintPieDiagnostic> Pies { get; }

    private PrintChartVisual(PrintChartDefinition definition, double width, double maximumHeight)
    {
        Definition = definition; Tag = definition; drawingWidth = width;
        layout = Measure(definition, width);
        scale = Math.Min(1, Math.Max(1, maximumHeight) / layout.Height);
        Width = width; Height = layout.Height * scale;
        HorizontalAlignment = HorizontalAlignment.Left;
        Pies = layout.Pies.Select(pie => pie.Diagnostic).ToArray();
        AutomationProperties.SetName(this, definition.Title + " · " + string.Join(" · ", Pies.Select(pie =>
            $"{pie.Series}: Đạt {Percentage(pie.PassedCount, pie.TotalCount)}, Không đạt {Percentage(pie.TotalCount - pie.PassedCount, pie.TotalCount)}")));
    }

    internal static IEnumerable<BlockUIContainer> Blocks(PrintChartDefinition definition, double width, double pageContentHeight)
    {
        yield return new BlockUIContainer(new PrintChartVisual(definition, Math.Max(1, width), Math.Max(1, pageContentHeight - 20)))
        { Margin = new Thickness(0, 8, 0, 8) };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new TranslateTransform((Width - drawingWidth * scale) / 2, 0));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRoundedRectangle(Brushes.White, new Pen(Line, 0.8),
            new Rect(0.5, 0.5, Math.Max(0, drawingWidth - 1), layout.Height - 1), 6, 6);
        var title = Text(Definition.Title, 13, Math.Max(1, drawingWidth - 2 * Padding), Ink, true);
        dc.DrawText(title, new Point(Padding, Padding));
        if (!string.IsNullOrWhiteSpace(Definition.Subtitle))
            dc.DrawText(Text(Definition.Subtitle, 9, Math.Max(1, drawingWidth - 2 * Padding), Muted), new Point(Padding, Padding + title.Height + 4));
        foreach (var pie in layout.Pies)
        {
            var data = pie.Diagnostic;
            var caption = Text(data.Series, 10, pie.ColumnWidth, Ink, true); caption.TextAlignment = TextAlignment.Center;
            dc.DrawText(caption, pie.CaptionOrigin);
            if (data.TotalCount == 0)
                dc.DrawEllipse(Brushes.White, new Pen(Line, 1.2), Center(data.Bounds), data.Bounds.Width / 2, data.Bounds.Height / 2);
            else
            {
                if (data.PassedGeometry is not null) dc.DrawGeometry(Passed, new Pen(Brushes.White, 0.7), data.PassedGeometry);
                if (data.FailedGeometry is not null) dc.DrawGeometry(Failed, new Pen(Brushes.White, 0.7), data.FailedGeometry);
                DrawSliceLabel(dc, data.Bounds, data.PassedGeometry, data.PassedCount, data.TotalCount, -90);
                DrawSliceLabel(dc, data.Bounds, data.FailedGeometry, data.TotalCount - data.PassedCount, data.TotalCount,
                    -90 + 360d * data.PassedCount / data.TotalCount);
            }
            var y = pie.LegendOrigin.Y;
            y = Legend(dc, pie.LegendOrigin.X, y, pie.LegendWidth, Passed, $"Đạt: {Percentage(data.PassedCount, data.TotalCount)}");
            y = Legend(dc, pie.LegendOrigin.X, y, pie.LegendWidth, Failed, $"Không đạt: {Percentage(data.TotalCount - data.PassedCount, data.TotalCount)}");
            if (data.TotalCount == 0)
            {
                var empty = Text("Chưa có dữ liệu", 9, pie.LegendWidth, Muted); empty.TextAlignment = TextAlignment.Center;
                dc.DrawText(empty, new Point(pie.LegendOrigin.X, y + 2));
            }
        }
        dc.Pop(); dc.Pop();
    }

    private static Layout Measure(PrintChartDefinition definition, double width)
    {
        var innerWidth = Math.Max(1, width - 2 * Padding);
        var headingHeight = Padding + Text(definition.Title, 13, innerWidth, Ink, true).Height + 8;
        if (!string.IsNullOrWhiteSpace(definition.Subtitle)) headingHeight += Text(definition.Subtitle, 9, innerWidth, Muted).Height + 8;
        var seriesCount = string.IsNullOrWhiteSpace(definition.SecondSeries) ? 1 : 2;
        var columns = innerWidth >= 480 ? seriesCount : 1;
        var columnWidth = Math.Max(1, (innerWidth - (columns - 1) * Gap) / columns);
        var diameter = Math.Max(8, Math.Min(160, columnWidth - 20));
        var legendWidth = Math.Min(columnWidth, Math.Max(150, diameter));
        var captionHeight = Enumerable.Range(0, seriesCount).Max(index => Text(index == 0 ? definition.FirstSeries : definition.SecondSeries, 10, columnWidth, Ink, true).Height);
        var pies = new List<PieLayout>();
        var y = headingHeight;
        for (var start = 0; start < seriesCount; start += columns)
        {
            var rowBottom = y;
            for (var index = start; index < Math.Min(seriesCount, start + columns); index++)
            {
                var second = index == 1;
                var total = definition.IsEmpty ? 0 : Count(definition.Items.Sum(item => second ? item.SecondValue ?? 0 : item.FirstValue));
                var passedItem = definition.Items.FirstOrDefault(item => item.Label == "Đạt");
                var passed = definition.IsEmpty || passedItem is null ? 0 : Count(second ? passedItem.SecondValue ?? 0 : passedItem.FirstValue);
                var x = Padding + (index - start) * (columnWidth + Gap);
                var bounds = new Rect(x + (columnWidth - diameter) / 2, y + captionHeight + 8, diameter, diameter);
                var diagnostic = new PrintPieDiagnostic(definition.Title, second ? definition.SecondSeries! : definition.FirstSeries, total, passed, bounds,
                    Slice(bounds, passed, total, -90), Slice(bounds, total - passed, total, total == 0 ? -90 : -90 + 360d * passed / total));
                var legend = new Point(x + (columnWidth - legendWidth) / 2, bounds.Bottom + 9);
                var bottom = legend.Y + LegendHeight($"Đạt: {Percentage(passed, total)}", legendWidth)
                    + LegendHeight($"Không đạt: {Percentage(total - passed, total)}", legendWidth);
                if (total == 0) bottom += Text("Chưa có dữ liệu", 9, legendWidth, Muted).Height + 2;
                rowBottom = Math.Max(rowBottom, bottom);
                pies.Add(new PieLayout(diagnostic, new Point(x, y), legend, columnWidth, legendWidth));
            }
            y = rowBottom + Gap;
        }
        return new Layout(y - Gap + Padding, pies.ToArray());
    }

    private static void DrawSliceLabel(DrawingContext dc, Rect bounds, Geometry? sector, int value, int total, double start)
    {
        if (sector is null || value <= 0 || total <= 0 || bounds.Width < 72) return;
        var sweep = 360d * value / total;
        if (sweep < 65) return;
        var position = value == total ? Center(bounds) : PointAt(Center(bounds), bounds.Width * 0.29, start + sweep / 2);
        var text = Text(Percentage(value, total), 11, 60, Brushes.White, true); text.TextAlignment = TextAlignment.Center;
        var box = new Rect(position.X - text.WidthIncludingTrailingWhitespace / 2, position.Y - text.Height / 2, text.WidthIncludingTrailingWhitespace, text.Height);
        if (!new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight }.All(sector.FillContains)) return;
        dc.DrawText(text, new Point(position.X - 30, position.Y - text.Height / 2));
    }

    private static Geometry? Slice(Rect bounds, int value, int total, double start)
    {
        if (value <= 0 || total <= 0) return null;
        var center = Center(bounds); var radius = bounds.Width / 2;
        if (value >= total) { var ellipse = new EllipseGeometry(center, radius, radius); ellipse.Freeze(); return ellipse; }
        var sweep = 360d * value / total;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(center, true, true);
            context.LineTo(PointAt(center, radius, start), true, false);
            context.ArcTo(PointAt(center, radius, start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }

    private static double Legend(DrawingContext dc, double x, double y, double width, Brush brush, string label)
    {
        var text = Text(label, 10, Math.Max(1, width - 16), Ink);
        dc.DrawRectangle(brush, null, new Rect(x, y + 2, 9, 9)); dc.DrawText(text, new Point(x + 16, y));
        return y + Math.Max(12, text.Height) + 3;
    }
    private static double LegendHeight(string label, double width) => Math.Max(12, Text(label, 10, Math.Max(1, width - 16), Ink).Height) + 3;
    private static string Percentage(int value, int total) => total == 0 ? "—" : $"{100m * value / total:0.#}%";
    private static int Count(decimal value) => value >= 0 && value <= int.MaxValue && value == decimal.Truncate(value)
        ? (int)value : throw new ArgumentException("Số nhân viên thống kê không hợp lệ.");
    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static Point PointAt(Point center, double radius, double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180), center.Y + radius * Math.Sin(angle * Math.PI / 180));
    private static FormattedText Text(string? value, double size, double width, Brush brush, bool bold = false) => new(value ?? "", CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1)
        { MaxTextWidth = Math.Max(1, width), Trimming = TextTrimming.None };
    private static Brush ColorBrush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private sealed record PieLayout(PrintPieDiagnostic Diagnostic, Point CaptionOrigin, Point LegendOrigin, double ColumnWidth, double LegendWidth);
    private sealed record Layout(double Height, PieLayout[] Pies);
}
