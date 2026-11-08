using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace QuanLyDaoTao.Desktop;

internal static class Ui
{
    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static TextBlock Text(string text, double size = 14, string color = "#172B3A", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    public static Border Card(UIElement content, int padding = 20) => new() { Child = content, Background = Brushes.White, BorderBrush = Brush("#E1E8ED"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(padding) };
    public static Button Button(string label, Action action, bool primary = false) { var button = new Button { Content = label }; if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); button.Click += (_, _) => action(); return button; }
    public static void Try(Window? owner, Action action) { try { action(); } catch (Exception ex) { if (owner != null) MessageBox.Show(owner, ex.Message, "Không thể thực hiện", MessageBoxButton.OK, MessageBoxImage.Warning); else MessageBox.Show(ex.Message, "Không thể thực hiện", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    public static DataGridTextColumn Column(string title, string property, double width = 120, string? format = null) { var binding = new Binding(property); if (format != null) binding.StringFormat = format; return new DataGridTextColumn { Header = title, Binding = binding, Width = new DataGridLength(width), MinWidth = width }; }
    public static IEnumerable<int> Years(AppState state) => Enumerable.Range(DateTime.Today.Year - 5, 8).Concat(state.Activities.SelectMany(a => new[] { a.StartDate.Year, a.StartDate.Year - 1 })).Distinct().OrderDescending();
    public static StackPanel Field(string label, UIElement input) { var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; var caption = Text(label, 11, "#6C808C", true); caption.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(caption); panel.Children.Add(input); return panel; }
}
