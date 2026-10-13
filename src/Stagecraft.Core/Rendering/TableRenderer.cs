using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>Tables in the theme's style: a coloured header row, banded rows, hairline separators.</summary>
public static class TableRenderer
{
    public static IReadOnlyList<double> ColumnWidths(TableElement table, double width)
    {
        var n = Math.Max(1, table.ColumnCount);
        var weights = Enumerable.Range(0, n).Select(i => table.ColumnWidths != null && i < table.ColumnWidths.Count && table.ColumnWidths[i] > 0 ? table.ColumnWidths[i] : 1.0).ToList();
        var total = weights.Sum();
        return weights.Select(w => w / total * width).ToList();
    }

    public static double FontSizeFor(TableElement table, Rect box, SlideStyle style)
    {
        if (table.FontSize is { } fixedSize && fixedSize > 0) return fixedSize;
        var rows = Math.Max(1, table.RowCount);
        var rowHeight = box.Height / rows;
        return Math.Max(10, Math.Min(style.Theme.Fonts.BodySize * style.Scale * 0.82, rowHeight * 0.4));
    }

    public static void Draw(DrawingContext dc, TableElement table, Rect box, SlideStyle style)
    {
        var rows = table.RowCount;
        var cols = table.ColumnCount;
        if (rows == 0 || cols == 0 || box.Width < 4 || box.Height < 4) return;

        var widths = ColumnWidths(table, box.Width);
        var rowHeight = box.Height / rows;
        var size = FontSizeFor(table, box, style);
        var pad = Math.Max(size * 0.6, 8);
        var radius = Math.Min(style.Theme.Radius * style.Scale, Math.Min(box.Width, box.Height) / 6);
        var header = style.Resolve(table.HeaderColor);
        var headerText = ChartRenderer.ContrastOn(header);
        var text = style.Resolve("text");
        var band = style.Resolve("bg2");
        var lineColor = style.Resolve("text2@22");
        var font = style.Theme.Fonts.Body;

        dc.PushClip(new RectangleGeometry(box, radius, radius));
        var y = box.Y;
        for (var r = 0; r < rows; r++)
        {
            var isHeader = table.HeaderRow && r == 0;
            var rowRect = new Rect(box.X, y, box.Width, rowHeight);
            if (isHeader) dc.DrawRectangle(new SolidColorBrush(header), null, rowRect);
            else if (table.Banded && (r - (table.HeaderRow ? 1 : 0)) % 2 == 1) dc.DrawRectangle(new SolidColorBrush(band), null, rowRect);
            else if (table.Banded) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(band.A * 0.35), band.R, band.G, band.B)), null, rowRect);

            var x = box.X;
            var row = table.Cells[r];
            for (var c = 0; c < cols; c++)
            {
                var value = c < row.Count ? row[c] ?? "" : "";
                if (value.Length > 0)
                {
                    var bold = isHeader || (table.FirstColumnBold && c == 0);
                    var ft = ChartRenderer.Text(value, font, bold ? 700 : 400, size, isHeader ? headerText : text);
                    ft.MaxTextWidth = Math.Max(1, widths[c] - 2 * pad);
                    ft.MaxTextHeight = Math.Max(size * 1.4, rowHeight - pad * 0.5);
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    ft.TextAlignment = LooksNumeric(value) && !isHeader ? TextAlignment.Right : TextAlignment.Left;
                    dc.DrawText(ft, new Point(x + pad, y + (rowHeight - ft.Height) / 2));
                }
                x += widths[c];
            }
            y += rowHeight;
            if (r < rows - 1 && !isHeader)
                dc.DrawRectangle(new SolidColorBrush(lineColor), null, new Rect(box.X, y - 0.75, box.Width, 1.5));
        }
        dc.Pop();
    }

    private static bool LooksNumeric(string value)
    {
        var v = value.Trim().TrimStart('$', '€', '£', '+', '-', '−').TrimEnd('%', 'k', 'K', 'M', 'm', 'x');
        return v.Length > 0 && v.All(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == ' ');
    }
}
