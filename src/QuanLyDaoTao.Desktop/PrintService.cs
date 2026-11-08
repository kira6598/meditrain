using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuanLyDaoTao.Desktop;

public static class PrintService
{
    public static void PrintTable(Window? owner, string title, IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows, string subtitle = "", IReadOnlyList<PrintChartDefinition>? charts = null)
    {
        // Capture the selected rows and chart values before opening the modal print dialog.
        var headerSnapshot = headers.ToArray();
        var rowSnapshot = rows.Select(row => row.ToArray()).ToArray();
        var chartSnapshot = Snapshot(charts);
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        if (dialog.ShowDialog() != true) return;
        var document = CreateDocument(title, headerSnapshot, rowSnapshot, subtitle, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight, chartSnapshot);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    internal static FlowDocument CreateDocument(string title, IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows, string subtitle, double width, double height, IReadOnlyList<PrintChartDefinition>? charts = null)
    {
        var headerList = headers.ToArray();
        var rowList = rows.Select(row => row.ToArray()).ToArray();
        var chartList = Snapshot(charts);
        var document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 10, Foreground = Brushes.Black, Background = Brushes.White, PageWidth = width, PageHeight = height, PagePadding = new Thickness(24), ColumnWidth = double.PositiveInfinity };
        document.Blocks.Add(new Paragraph(new Run("MediTrain · QUẢN LÝ ĐÀO TẠO Y TẾ & CME")) { FontSize = 10, Foreground = Ui.Brush("#0D9488") });
        document.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 12) });
        document.Blocks.Add(new Paragraph(new Run(subtitle)) { FontSize = 10 });
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 12, 0, 12) };
        for (var i = 0; i < headerList.Length; i++) table.Columns.Add(new TableColumn { Width = new GridLength(i is 1 or 2 ? 2.5 : 1, GridUnitType.Star) });
        var group = new TableRowGroup();
        var head = new TableRow { Background = Ui.Brush("#EAF3F3"), FontWeight = FontWeights.Bold };
        foreach (var value in headerList) head.Cells.Add(Cell(value)); group.Rows.Add(head);
        foreach (var row in rowList) { var item = new TableRow(); foreach (var value in row) item.Cells.Add(Cell(value)); group.Rows.Add(item); }
        table.RowGroups.Add(group); document.Blocks.Add(table);
        var chartWidth = Math.Max(1, width - document.PagePadding.Left - document.PagePadding.Right);
        var chartHeight = Math.Max(1, height - document.PagePadding.Top - document.PagePadding.Bottom);
        foreach (var chart in chartList)
            foreach (var block in PrintChartVisual.Blocks(chart, chartWidth, chartHeight)) document.Blocks.Add(block);
        document.Blocks.Add(new Paragraph(new Run($"Ngày in: {DateTime.Now:dd/MM/yyyy HH:mm} · Số tiết tính theo năm bắt đầu khóa học.")) { FontSize = 9, Foreground = Brushes.Gray });
        return document;
    }
    private static PrintChartDefinition[] Snapshot(IReadOnlyList<PrintChartDefinition>? charts) => charts?.Select(chart => chart with { Items = chart.Items.Select(item => item with { }).ToArray() }).ToArray() ?? [];
    private static TableCell Cell(string value) => new(new Paragraph(new Run(value))) { Padding = new Thickness(6), BorderBrush = Ui.Brush("#D9E3E8"), BorderThickness = new Thickness(0, 0, 0, 0.5) };
}
