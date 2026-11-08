using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuanLyDaoTao.Desktop.Views;

internal sealed class ComplianceRing : FrameworkElement
{
    private readonly int _passed;
    private readonly int _total;
    private readonly Brush _accent;

    public ComplianceRing(int passed, int total, string color)
    {
        _passed = passed; _total = total; _accent = Ui.Brush(color);
        Width = 152; Height = 152;
        System.Windows.Automation.AutomationProperties.SetName(this, total == 0 ? "Chưa có dữ liệu" : $"{passed} / {total} nhân viên đạt, {100m * passed / total:0.#}%");
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var center = new Point(76, 76);
        var background = new Pen(Ui.Brush("#ECF1F5"), 14);
        drawing.DrawEllipse(null, background, center, 61, 61);
        var ratio = _total == 0 ? 0 : Math.Clamp((double)_passed / _total, 0, 1);
        var pen = new Pen(_accent, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (ratio >= 1) drawing.DrawEllipse(null, pen, center, 61, 61);
        else if (ratio > 0)
        {
            var angle = ratio * Math.PI * 2 - Math.PI / 2;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(76, 15), false, false);
                context.ArcTo(new Point(76 + 61 * Math.Cos(angle), 76 + 61 * Math.Sin(angle)), new Size(61, 61), 0, ratio > .5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze(); drawing.DrawGeometry(null, pen, geometry);
        }
        DrawCentered(drawing, _total == 0 ? "—" : $"{ratio * 100:0.#}%", 31, 48, Ui.Brush("#172B3A"), true);
        DrawCentered(drawing, _total == 0 ? "Chưa có dữ liệu" : "nhân viên đạt", 11, 90, Ui.Brush("#6C808C"), false);
    }

    private void DrawCentered(DrawingContext drawing, string value, double size, double top, Brush brush, bool bold)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text, new Point(76 - text.Width / 2, top));
    }
}

internal static class ChartUi
{
    public static Border ComplianceCard(string title, string subtitle, int passed, int total, string color)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Text(title, 16, bold: true));
        var sub = Ui.Text(subtitle, 12, "#6C808C"); sub.Margin = new Thickness(0, 5, 0, 12); panel.Children.Add(sub);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(164) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(new ComplianceRing(passed, total, color));
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        legend.Children.Add(LegendRow("Đạt ngưỡng", passed.ToString(), color));
        legend.Children.Add(LegendRow("Chưa đạt", (total - passed).ToString(), "#B7C6D2"));
        var count = Ui.Text($"Tổng {total} nhân viên", 12, "#6C808C"); count.Margin = new Thickness(0, 12, 0, 0); legend.Children.Add(count);
        Grid.SetColumn(legend, 1); body.Children.Add(legend); panel.Children.Add(body); return Ui.Card(panel);
    }

    public static StackPanel LegendRow(string title, string value, string color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Ui.Brush(color), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Ui.Text($"{title}  {value}", 13)); return row;
    }

    public static ProgressBar Bar(decimal value, string color) => new()
    {
        Minimum = 0, Maximum = 100, Value = (double)value, Height = 9,
        Foreground = Ui.Brush(color), Background = Ui.Brush("#EDF2F5"), BorderThickness = new Thickness(0),
        ToolTip = $"{value:0.#}%"
    };
}
