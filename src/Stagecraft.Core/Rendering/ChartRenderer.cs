using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>Bar, line, pie and donut charts drawn in the theme's colours and fonts.</summary>
public static class ChartRenderer
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static Color SeriesColor(SlideStyle style, int index)
    {
        var baseColor = style.Accent(index % 5);
        if (index < 5) return baseColor;
        // Beyond five: lighter versions of the accents.
        return Mix(baseColor, Colors.White, 0.45);
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static string Format(double value, string? format)
    {
        try
        {
            return value.ToString(string.IsNullOrWhiteSpace(format) ? "#,0.##" : format, Culture);
        }
        catch (FormatException)
        {
            return value.ToString("#,0.##", Culture);
        }
    }

    public static void Draw(DrawingContext dc, ChartElement chart, Rect box, SlideStyle style)
    {
        if (box.Width < 20 || box.Height < 20) return;
        var font = style.Theme.Fonts.Body;
        var labelSize = MathEx.Clamp(Math.Min(box.Width, box.Height) / 18, 14 * style.Scale, style.Theme.Fonts.CaptionSize * style.Scale);
        var text2 = style.Resolve("text2");
        var text = style.Resolve("text");

        var series = chart.Series.Where(s => s.Values.Count > 0).ToList();
        var categories = chart.Categories;
        var count = Math.Max(categories.Count, series.Count == 0 ? 0 : series.Max(s => s.Values.Count));
        if (series.Count == 0 || count == 0)
        {
            var empty = Text("Add data to this chart", font, 400, labelSize, text2);
            dc.DrawText(empty, new Point(box.X + (box.Width - empty.Width) / 2, box.Y + (box.Height - empty.Height) / 2));
            return;
        }

        var plot = box;
        var pie = chart.Kind is ChartKind.Pie or ChartKind.Donut;
        if (chart.ShowLegend && (series.Count > 1 || pie))
        {
            var names = pie ? Enumerable.Range(0, count).Select(i => i < categories.Count ? categories[i] : $"Item {i + 1}").ToList() : series.Select(s => s.Name).ToList();
            var legendHeight = DrawLegend(dc, names, box, labelSize, font, text2, style, pie);
            plot = new Rect(box.X, box.Y + legendHeight, box.Width, Math.Max(1, box.Height - legendHeight));
        }

        if (pie) DrawPie(dc, chart, series[0], count, plot, style, labelSize, font, text);
        else DrawAxes(dc, chart, series, categories, count, plot, style, labelSize, font, text, text2);
    }

    private static double DrawLegend(DrawingContext dc, IReadOnlyList<string> names, Rect box, double size, string font, Color color, SlideStyle style, bool pie)
    {
        var x = box.X;
        var y = box.Y;
        var dot = size * 0.62;
        var rowHeight = size * 1.6;
        var gap = size * 1.4;
        for (var i = 0; i < names.Count; i++)
        {
            var label = Text(names[i], font, 500, size, color);
            var itemWidth = dot + size * 0.5 + label.Width;
            if (x > box.X && x + itemWidth > box.Right)
            {
                x = box.X;
                y += rowHeight;
            }
            var brush = new SolidColorBrush(SeriesColor(style, i));
            dc.DrawRoundedRectangle(brush, null, new Rect(x, y + (rowHeight - dot) / 2 - size * 0.1, dot, dot), dot * 0.3, dot * 0.3);
            dc.DrawText(label, new Point(x + dot + size * 0.5, y + (rowHeight - label.Height) / 2 - size * 0.1));
            x += itemWidth + gap;
        }
        return y - box.Y + rowHeight + size * 0.8;
    }

    private static (double Min, double Max, double Step) NiceRange(double min, double max, bool includeZero = true)
    {
        if (includeZero)
        {
            min = Math.Min(0, min);
            max = Math.Max(0, max);
        }
        else
        {
            var pad = Math.Max((max - min) * 0.15, Math.Abs(max) * 0.02);
            min = Math.Max(0, min - pad);
        }
        if (Math.Abs(max - min) < 1e-9) max = min + 1;
        var raw = (max - min) / 4;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / mag;
        var step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * mag;
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    private static void DrawAxes(DrawingContext dc, ChartElement chart, List<ChartSeries> series, List<string> categories, int count, Rect area,
        SlideStyle style, double size, string font, Color text, Color text2)
    {
        var values = series.SelectMany(s => s.Values).ToList();
        // Bars start at zero; lines whose values are all well above zero zoom in on them.
        var lowest = values.Min();
        var highest = values.Max();
        var (min, max, step) = NiceRange(lowest, highest, chart.Kind != ChartKind.Line || lowest <= 0 || lowest < highest * 0.35);

        // Axis labels decide the plot area.
        var ticks = new List<double>();
        for (var v = min; v <= max + step / 2; v += step) ticks.Add(v);
        var tickLabels = ticks.Select(t => Text(Format(t, chart.ValueFormat), font, 400, size * 0.92, text2)).ToList();
        var axisWidth = tickLabels.Max(t => t.Width) + size * 0.8;
        var categoryHeight = size * 2.0;
        var top = area.Y + (chart.ShowValues ? size * 1.3 : size * 0.5);
        var plot = new Rect(area.X + axisWidth, top, Math.Max(1, area.Width - axisWidth), Math.Max(1, area.Bottom - categoryHeight - top));

        double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(46, text2.R, text2.G, text2.B)), Math.Max(1, size * 0.06));
        for (var i = 0; i < ticks.Count; i++)
        {
            var y = Y(ticks[i]);
            if (chart.ShowGrid || Math.Abs(ticks[i]) < 1e-9)
                dc.DrawLine(Math.Abs(ticks[i]) < 1e-9 ? new Pen(new SolidColorBrush(Color.FromArgb(110, text2.R, text2.G, text2.B)), gridPen.Thickness * 1.4) : gridPen,
                    new Point(plot.X, y), new Point(plot.Right, y));
            dc.DrawText(tickLabels[i], new Point(plot.X - size * 0.6 - tickLabels[i].Width, y - tickLabels[i].Height / 2));
        }

        var band = plot.Width / count;
        for (var c = 0; c < count; c++)
        {
            var name = c < categories.Count ? categories[c] : "";
            if (name.Length == 0) continue;
            var label = Text(name, font, 500, size, text2);
            label.MaxTextWidth = Math.Max(1, band - size * 0.3);
            label.MaxLineCount = 2;
            label.Trimming = TextTrimming.CharacterEllipsis;
            label.TextAlignment = TextAlignment.Center;
            dc.DrawText(label, new Point(plot.X + band * c + size * 0.15, plot.Bottom + size * 0.45));
        }

        var zero = Y(MathEx.Clamp(0, min, max));
        if (chart.Kind == ChartKind.Bar)
        {
            var groupWidth = band * (series.Count == 1 ? 0.56 : 0.72);
            var barGap = series.Count > 1 ? groupWidth * 0.06 : 0;
            var barWidth = (groupWidth - barGap * (series.Count - 1)) / series.Count;
            var radius = Math.Min(barWidth * 0.22, Math.Max(2, style.Theme.Radius * 0.5 * style.Scale));
            for (var si = 0; si < series.Count; si++)
            {
                var brush = new SolidColorBrush(SeriesColor(style, si));
                for (var c = 0; c < count && c < series[si].Values.Count; c++)
                {
                    var v = series[si].Values[c];
                    var x = plot.X + band * c + (band - groupWidth) / 2 + si * (barWidth + barGap);
                    var y = Y(v);
                    var rect = new Rect(x, Math.Min(y, zero), barWidth, Math.Max(0.5, Math.Abs(zero - y)));
                    dc.DrawGeometry(brush, null, TopRounded(rect, radius, v >= 0));
                    if (chart.ShowValues)
                    {
                        var label = Text(Format(v, chart.ValueFormat), font, 600, size * 0.9, text);
                        var ly = v >= 0 ? rect.Top - label.Height - size * 0.2 : rect.Bottom + size * 0.2;
                        if (label.Width <= barWidth + band * 0.3)
                            dc.DrawText(label, new Point(x + (barWidth - label.Width) / 2, ly));
                    }
                }
            }
        }
        else
        {
            var thickness = Math.Max(3, size * 0.2);
            for (var si = 0; si < series.Count; si++)
            {
                var color = SeriesColor(style, si);
                var points = new List<Point>();
                for (var c = 0; c < count && c < series[si].Values.Count; c++)
                    points.Add(new Point(plot.X + band * c + band / 2, Y(series[si].Values[c])));
                if (points.Count == 0) continue;

                if (series.Count <= 2 && points.Count > 1)
                {
                    var area2 = new PathFigure { StartPoint = new Point(points[0].X, zero), IsClosed = true };
                    foreach (var p in points) area2.Segments.Add(new LineSegment(p, false));
                    area2.Segments.Add(new LineSegment(new Point(points[points.Count - 1].X, zero), false));
                    var geo = new PathGeometry();
                    geo.Figures.Add(area2);
                    var fill = new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
                    dc.DrawGeometry(fill, null, geo);
                }

                var pen = new Pen(new SolidColorBrush(color), thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var line = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
                foreach (var p in points.Skip(1)) line.Segments.Add(new LineSegment(p, true));
                var lineGeo = new PathGeometry();
                lineGeo.Figures.Add(line);
                dc.DrawGeometry(null, pen, lineGeo);

                var bg = new SolidColorBrush(style.Resolve("bg"));
                var markerPen = new Pen(new SolidColorBrush(color), thickness * 0.9);
                for (var i = 0; i < points.Count; i++)
                {
                    dc.DrawEllipse(bg, markerPen, points[i], thickness * 1.5, thickness * 1.5);
                    if (chart.ShowValues && series.Count <= 2)
                    {
                        var label = Text(Format(series[si].Values[i], chart.ValueFormat), font, 600, size * 0.9, text);
                        dc.DrawText(label, new Point(points[i].X - label.Width / 2, points[i].Y - label.Height - thickness * 2.2));
                    }
                }
            }
        }
    }

    private static Geometry TopRounded(Rect r, double radius, bool top)
    {
        radius = Math.Min(radius, Math.Min(r.Width / 2, r.Height));
        if (radius < 0.5) return new RectangleGeometry(r);
        var f = new PathFigure { IsClosed = true, IsFilled = true };
        if (top)
        {
            f.StartPoint = new Point(r.Left, r.Bottom);
            f.Segments.Add(new LineSegment(new Point(r.Left, r.Top + radius), true));
            f.Segments.Add(new ArcSegment(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(r.Right - radius, r.Top), true));
            f.Segments.Add(new ArcSegment(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom), true));
        }
        else
        {
            f.StartPoint = new Point(r.Left, r.Top);
            f.Segments.Add(new LineSegment(new Point(r.Right, r.Top), true));
            f.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom - radius), true));
            f.Segments.Add(new ArcSegment(new Point(r.Right - radius, r.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(r.Left + radius, r.Bottom), true));
            f.Segments.Add(new ArcSegment(new Point(r.Left, r.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }
        var g = new PathGeometry();
        g.Figures.Add(f);
        return g;
    }

    private static void DrawPie(DrawingContext dc, ChartElement chart, ChartSeries data, int count, Rect area, SlideStyle style, double size, string font, Color text)
    {
        var values = Enumerable.Range(0, count).Select(i => i < data.Values.Count ? Math.Max(0, data.Values[i]) : 0).ToList();
        var total = values.Sum();
        var radius = Math.Min(area.Width, area.Height) / 2 * 0.94;
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        if (total <= 0 || radius <= 2) return;
        var donut = chart.Kind == ChartKind.Donut;
        var inner = donut ? radius * 0.6 : 0;
        var separator = new Pen(new SolidColorBrush(style.Resolve("bg")), Math.Max(2, radius * 0.012)) { LineJoin = PenLineJoin.Round };

        var angle = -90.0;
        for (var i = 0; i < count; i++)
        {
            var sweep = values[i] / total * 360;
            if (sweep <= 0) continue;
            var geometry = Slice(center, radius, inner, angle, sweep);
            dc.DrawGeometry(new SolidColorBrush(SeriesColor(style, i)), separator, geometry);
            if (chart.ShowValues && sweep >= 16)
            {
                var mid = (angle + sweep / 2) * Math.PI / 180;
                var r = donut ? (radius + inner) / 2 : radius * 0.64;
                var pct = values[i] / total;
                var label = Text(pct.ToString("0%", Culture), font, 700, size * (donut ? 0.95 : 1.05), ContrastOn(SeriesColor(style, i)));
                dc.DrawText(label, new Point(center.X + Math.Cos(mid) * r - label.Width / 2, center.Y + Math.Sin(mid) * r - label.Height / 2));
            }
            angle += sweep;
        }

        if (donut)
        {
            var big = Text(Format(total, chart.ValueFormat), style.Theme.Fonts.Heading, Math.Max(600, style.Theme.Fonts.HeadingWeight), inner * 0.42, text);
            var caption = Text(data.Name, font, 500, size, style.Resolve("text2"));
            var h = big.Height + caption.Height * 0.9;
            dc.DrawText(big, new Point(center.X - big.Width / 2, center.Y - h / 2));
            if (caption.Width < inner * 1.7) dc.DrawText(caption, new Point(center.X - caption.Width / 2, center.Y - h / 2 + big.Height * 0.95));
        }
    }

    public static Color ContrastOn(Color c) => SlideStyle.Luminance(c) > 0.45 ? Color.FromRgb(0x14, 0x14, 0x1A) : Colors.White;

    private static Geometry Slice(Point c, double r, double inner, double startDeg, double sweepDeg)
    {
        if (sweepDeg >= 359.99)
        {
            var whole = new EllipseGeometry(c, r, r);
            if (inner <= 0) return whole;
            return new CombinedGeometry(GeometryCombineMode.Exclude, whole, new EllipseGeometry(c, inner, inner));
        }
        var a0 = startDeg * Math.PI / 180;
        var a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var large = sweepDeg > 180;
        Point P(double radius, double a) => new(c.X + Math.Cos(a) * radius, c.Y + Math.Sin(a) * radius);
        var f = new PathFigure { IsClosed = true, IsFilled = true };
        if (inner <= 0)
        {
            f.StartPoint = c;
            f.Segments.Add(new LineSegment(P(r, a0), true));
            f.Segments.Add(new ArcSegment(P(r, a1), new Size(r, r), 0, large, SweepDirection.Clockwise, true));
        }
        else
        {
            f.StartPoint = P(inner, a0);
            f.Segments.Add(new LineSegment(P(r, a0), true));
            f.Segments.Add(new ArcSegment(P(r, a1), new Size(r, r), 0, large, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(P(inner, a1), true));
            f.Segments.Add(new ArcSegment(P(inner, a0), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        }
        var g = new PathGeometry();
        g.Figures.Add(f);
        return g;
    }

    internal static FormattedText Text(string text, string font, int weight, double size, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new FormattedText(text.Length == 0 ? " " : text, Culture, FlowDirection.LeftToRight, FontCatalog.Typeface(font, weight, false), Math.Max(1, size), brush, 1.0);
    }
}
