using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace QuanLyDaoTao.Desktop.Views;

internal static class FormUi
{
    internal static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(13, 148, 136));
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(15, 23, 42));
    internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    internal static readonly Brush Line = new SolidColorBrush(Color.FromRgb(226, 232, 240));

    internal static Button Button(string text, RoutedEventHandler click, bool primary = false, bool danger = false)
    {
        var result = new Button { Content = text, Padding = new Thickness(15, 9, 15, 9), Margin = new Thickness(4, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand };
        var key = danger ? "DangerButton" : primary ? "PrimaryButton" : "SecondaryButton";
        if (Application.Current.TryFindResource(key) is Style style) result.Style = style;
        else
        {
            result.Background = primary ? Teal : Brushes.White;
            result.Foreground = primary ? Brushes.White : danger ? Brushes.Firebrick : Ink;
            result.BorderBrush = Line;
        }
        result.Click += click;
        return result;
    }

    internal static TextBlock Text(string text, double size = 13, Brush? color = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ink,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    internal static Border Card(UIElement content) => new()
    {
        Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = content
    };

    internal static StackPanel Header(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        panel.Children.Add(Text(title, 25, bold: true));
        var sub = Text(subtitle, 13, Muted); sub.Margin = new Thickness(0, 7, 0, 0); panel.Children.Add(sub);
        return panel;
    }

    internal static DataGrid Table()
    {
        return new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0), Background = Brushes.White, RowHeaderWidth = 0,
            MinRowHeight = 44, ColumnHeaderHeight = 42, HorizontalGridLinesBrush = Line,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            EnableRowVirtualization = true
        };
    }

    internal static void Column(DataGrid table, string title, string path, double width = 120, string? format = null)
    {
        table.Columns.Add(new DataGridTextColumn
        {
            Header = title, Binding = new Binding(path) { StringFormat = format },
            Width = width < 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            MinWidth = width < 0 ? 180 : width
        });
    }

    internal static StackPanel Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(Text(label, 12, Muted, true));
        System.Windows.Automation.AutomationProperties.SetName(control, label.Replace(" *", ""));
        control.Margin = new Thickness(0, 6, 0, 0);
        control.MinHeight = 36;
        if (control is TextBox box) box.Padding = new Thickness(10, 7, 10, 7);
        panel.Children.Add(control);
        return panel;
    }

    internal static void Error(Window owner, Exception error) => MessageBox.Show(owner, error.Message, "Không thể hoàn tất", MessageBoxButton.OK, MessageBoxImage.Warning);

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                text.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        }
        return text.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static bool Matches(string term, params string[] values) => string.IsNullOrWhiteSpace(term) || values.Any(value => Normalize(value).Contains(Normalize(term), StringComparison.Ordinal));

    internal static void ConfigureDialog(Window window, string title, double height = 600)
    {
        window.Title = title; window.Width = 650; window.Height = height;
        window.MinWidth = 520; window.MinHeight = 400;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13;
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage("vi-VN");
        window.ResizeMode = ResizeMode.CanResize;
    }

    internal static StackPanel TwoColumns(UIElement left, UIElement right)
    {
        var panel = new StackPanel();
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (left is FrameworkElement lf) lf.Margin = new Thickness(0, 0, 8, 16);
        if (right is FrameworkElement rf) rf.Margin = new Thickness(8, 0, 0, 16);
        Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); panel.Children.Add(grid); return panel;
    }
}
