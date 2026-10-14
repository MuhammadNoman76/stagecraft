using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// Paints a slide's background and the theme's decorations (glows, rules, organic shapes, slide numbers).
/// They are part of the theme, not elements, so they follow theme changes and can't be moved by accident.
/// </summary>
public static class DecorPainter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static void PaintBackground(DrawingContext dc, Slide slide, SlideStyle style, RenderContext ctx, double width, double height)
    {
        var rect = new Rect(0, 0, width, height);
        var fill = style.Background;
        var brush = style.Brush(fill, rect, ctx.Media) ?? new SolidColorBrush(style.Resolve("bg", Colors.White));
        dc.DrawRectangle(brush, null, rect);
        if (fill.Kind == FillKind.Image && fill.Dim > 0)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(MathEx.Clamp(fill.Dim, 0, 1) * 255), 0, 0, 0)), null, rect);
    }

    public static void Paint(DrawingContext dc, Slide slide, SlideStyle style, RenderContext ctx, double width, double height, bool footer = true)
    {
        PaintBackground(dc, slide, style, ctx, width, height);
        // Pictures as backgrounds, image-led layouts and custom backgrounds get no decorations.
        var decorate = slide.ShowDecorations && slide.Background == null && slide.Layout is not ("image-full" or "blank");
        if (decorate) PaintDecorations(dc, slide, style, ctx, width, height);
        if (footer) PaintFooter(dc, slide, style, ctx, width, height);
    }

    /// <summary>
    /// The header and footer: date on the left, footer text in the middle, slide number on the right, in the theme's
    /// text font and muted colour, inside the bottom margin.
    /// </summary>
    public static void PaintFooter(DrawingContext dc, Slide slide, SlideStyle style, RenderContext ctx, double width, double height)
    {
        var footer = ctx.Deck.Footer;
        if (footer == null || !footer.ShowsOn(slide)) return;
        var m = style.Margin;
        var size = 22 * style.Scale;
        var color = style.Resolve("text2");
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var face = FontCatalog.Typeface(style.Theme.Fonts.Body, 500, false);
        var middle = height - m * 0.42;
        FormattedText Make(string s, double maxWidth) => new(s, Culture, FlowDirection.LeftToRight, face, size, brush, 1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var third = (width - 2 * m) / 3;
        if (footer.Date)
        {
            var date = footer.DateFor(ctx.Now);
            if (date.Length > 0)
            {
                var ft = Make(date, third);
                dc.DrawText(ft, new Point(m, middle - ft.Height / 2));
            }
        }
        if (!string.IsNullOrWhiteSpace(footer.Text))
        {
            var ft = Make(footer.Text!.Trim(), third * 1.4);
            ft.TextAlignment = TextAlignment.Center;
            dc.DrawText(ft, new Point(width / 2 - ft.MaxTextWidth / 2, middle - ft.Height / 2));
        }
        if (footer.SlideNumber)
        {
            var number = ctx.NumberOf(slide);
            if (number > 0)
            {
                var ft = Make(number.ToString(Culture), third);
                dc.DrawText(ft, new Point(width - m - ft.Width, middle - ft.Height / 2));
            }
        }
    }

    private static void PaintDecorations(DrawingContext dc, Slide slide, SlideStyle style, RenderContext ctx, double width, double height)
    {
        var w = width;
        var h = height;
        var m = style.Margin;
        var emphasis = style.IsEmphasis;
        // With the footer's slide numbers on, the theme doesn't draw its own.
        var number = ctx.Deck.Footer is { SlideNumber: true } ? 0 : ctx.NumberOf(slide);
        var a1 = style.Accent(0);
        var a2 = style.Accent(1);
        var a3 = style.Accent(2);
        var a4 = style.Accent(3);
        var text = style.Resolve("text");
        var text2 = style.Resolve("text2");
        var imageSide = slide.Layout is "image-left" or "image-right";

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        switch (style.Theme.Decor)
        {
            case "aurora":
                if (emphasis)
                {
                    Glow(dc, new Point(w * 0.12, h * 0.18), h * 0.75, h * 0.6, a1, 0.62);
                    Glow(dc, new Point(w * 0.9, h * 0.92), h * 0.8, h * 0.62, a3, 0.5);
                    Glow(dc, new Point(w * 0.78, h * 0.12), h * 0.5, h * 0.42, a2, 0.42);
                }
                else if (!imageSide)
                {
                    Glow(dc, new Point(w * 1.0, h * 0.0), h * 0.62, h * 0.55, a1, 0.16);
                    Glow(dc, new Point(w * 0.0, h * 1.05), h * 0.55, h * 0.45, a2, 0.11);
                }
                break;

            case "editorial":
            {
                var rule = new SolidColorBrush(Color.FromArgb((byte)(emphasis ? 90 : 110), text2.R, text2.G, text2.B));
                var ruleY = m * 0.55;
                dc.DrawRectangle(rule, null, new Rect(m, ruleY, w - 2 * m, Math.Max(1.5, 1.5 * style.Scale)));
                dc.DrawRectangle(new SolidColorBrush(a1), null, new Rect(m, ruleY - 5 * style.Scale, 34 * style.Scale, 11 * style.Scale));
                if (!emphasis && number > 0) Number(dc, number.ToString("00", Culture), style, new Point(w - m, h - m * 0.62), true, text2, style.Theme.Fonts.Heading, 400, true);
                break;
            }

            case "midnight":
                if (emphasis)
                {
                    Glow(dc, new Point(w * 0.92, h * 1.0), h * 0.95, h * 0.7, a1, 0.36);
                    Glow(dc, new Point(w * 0.6, h * 1.1), h * 0.6, h * 0.4, a2, 0.22);
                    Dots(dc, new Rect(w - m - 300 * style.Scale, m * 0.8, 300 * style.Scale, 200 * style.Scale), 28 * style.Scale, Color.FromArgb(46, text.R, text.G, text.B), 2.6 * style.Scale);
                }
                else
                {
                    if (!imageSide) Glow(dc, new Point(w * 0.02, h * 1.0), h * 0.55, h * 0.4, a1, 0.12);
                    if (!imageSide) Dots(dc, new Rect(w - m * 0.5 - 168 * style.Scale, m * 0.45, 168 * style.Scale, 112 * style.Scale), 28 * style.Scale, Color.FromArgb(34, text.R, text.G, text.B), 2.4 * style.Scale);
                    if (number > 0) Number(dc, number.ToString("00", Culture), style, new Point(w - m * 0.5, h - m * 0.5), true, text2, style.Theme.Fonts.Mono, 500, false);
                }
                break;

            case "pastel":
                if (emphasis)
                {
                    Circle(dc, new Point(w * 0.93, h * 0.12), h * 0.3, a1, 0.28);
                    Circle(dc, new Point(w * 0.06, h * 0.86), h * 0.26, a2, 0.3);
                    Circle(dc, new Point(w * 0.16, h * 0.1), h * 0.08, a4, 0.55);
                    Circle(dc, new Point(w * 0.86, h * 0.84), h * 0.06, a3, 0.6);
                }
                else if (!imageSide)
                {
                    Circle(dc, new Point(w * 1.0, h * 1.02), h * 0.28, a1, 0.16);
                    Circle(dc, new Point(w * 0.965, h * 0.66), h * 0.05, a4, 0.5);
                }
                break;

            case "corporate":
                if (emphasis)
                {
                    dc.DrawRectangle(new SolidColorBrush(a1), null, new Rect(0, 0, 20 * style.Scale, h));
                    var band = new StreamGeometry();
                    using (var g = band.Open())
                    {
                        g.BeginFigure(new Point(w * 0.68, 0), true, true);
                        g.LineTo(new Point(w, 0), false, false);
                        g.LineTo(new Point(w, h), false, false);
                        g.LineTo(new Point(w * 0.86, h), false, false);
                    }
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), null, band);
                }
                else
                {
                    dc.DrawRectangle(new SolidColorBrush(a1), null, new Rect(m, m * 0.42, 64 * style.Scale, 8 * style.Scale));
                    var footY = h - m * 0.55;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, text2.R, text2.G, text2.B)), null, new Rect(m, footY, w - 2 * m, Math.Max(1, 1.5 * style.Scale)));
                    if (number > 0) Number(dc, number.ToString(Culture), style, new Point(w - m, footY + 14 * style.Scale), true, text2, style.Theme.Fonts.Body, 500, false, top: true);
                }
                break;

            case "mono":
            {
                var square = 14 * style.Scale;
                dc.DrawRectangle(new SolidColorBrush(a1), null, new Rect(m, m * 0.5, square, square));
                if (number > 0) Number(dc, number.ToString("00", Culture), style, new Point(w - m, m * 0.5 - 6 * style.Scale), true, text2, style.Theme.Fonts.Mono, 500, false, top: true);
                break;
            }

            case "gradient":
                if (emphasis)
                {
                    Circle(dc, new Point(w * 0.95, h * 0.05), h * 0.42, Colors.White, 0.1);
                    Circle(dc, new Point(w * 0.03, h * 0.98), h * 0.32, Colors.White, 0.08);
                    Circle(dc, new Point(w * 0.12, h * 0.16), h * 0.05, Colors.White, 0.18);
                }
                else
                {
                    var bar = new LinearGradientBrush(a1, a2, 0);
                    dc.DrawRectangle(bar, null, new Rect(0, h - 12 * style.Scale, w, 12 * style.Scale));
                    if (!imageSide) Glow(dc, new Point(w * 0.98, h * 0.02), h * 0.5, h * 0.42, a2, 0.12);
                }
                break;

            case "swiss":
            {
                var bar = new SolidColorBrush(emphasis ? style.Resolve("text") : text);
                dc.DrawRectangle(bar, null, new Rect(m, m * 0.5, w - 2 * m, 10 * style.Scale));
                if (number > 0) Number(dc, number.ToString("00", Culture), style, new Point(w - m, m * 0.5 + 26 * style.Scale), true, emphasis ? style.Resolve("text") : text, style.Theme.Fonts.Heading, 800, false, top: true);
                if (emphasis) Ring(dc, new Point(w * 0.86, h * 0.64), h * 0.26, style.Resolve("text"), 0.9, 22 * style.Scale);
                break;
            }

            case "botanical":
                if (emphasis)
                {
                    Leaf(dc, new Point(w * 0.94, h * 0.84), h * 0.42, h * 0.16, -38, a3, 0.34);
                    Leaf(dc, new Point(w * 0.86, h * 0.96), h * 0.34, h * 0.13, -68, a2, 0.3);
                    Leaf(dc, new Point(w * 0.06, h * 0.1), h * 0.32, h * 0.12, 30, a3, 0.26);
                }
                else if (!imageSide)
                {
                    Leaf(dc, new Point(w * 0.985, h * 0.9), h * 0.3, h * 0.11, -40, a1, 0.14);
                    Leaf(dc, new Point(w * 0.93, h * 1.0), h * 0.24, h * 0.09, -72, a3, 0.22);
                }
                break;

            case "slate":
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, text2.R, text2.G, text2.B)), 3 * style.Scale) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
                var len = 44 * style.Scale;
                var inset = m * 0.45;
                dc.DrawLine(pen, new Point(inset, inset), new Point(inset + len, inset));
                dc.DrawLine(pen, new Point(inset, inset), new Point(inset, inset + len));
                dc.DrawLine(pen, new Point(w - inset, h - inset), new Point(w - inset - len, h - inset));
                dc.DrawLine(pen, new Point(w - inset, h - inset), new Point(w - inset, h - inset - len));
                if (emphasis) Glow(dc, new Point(w * 0.85, h * 0.2), h * 0.7, h * 0.55, a1, 0.2);
                else if (!imageSide) Glow(dc, new Point(w * 1.0, h * 0.0), h * 0.5, h * 0.4, a1, 0.1);
                break;
            }
        }
        dc.Pop();
    }

    private static void Glow(DrawingContext dc, Point center, double rx, double ry, Color color, double alpha)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)(alpha * 0.45 * 255), color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, rx, ry);
    }

    private static void Circle(DrawingContext dc, Point center, double r, Color color, double alpha) =>
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B)), null, center, r, r);

    private static void Ring(DrawingContext dc, Point center, double r, Color color, double alpha, double thickness) =>
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B)), thickness), center, r, r);

    private static void Dots(DrawingContext dc, Rect area, double spacing, Color color, double radius)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        for (var y = area.Top; y <= area.Bottom + 0.1; y += spacing)
        for (var x = area.Left; x <= area.Right + 0.1; x += spacing)
            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
    }

    /// <summary>A leaf: two arcs meeting in points, rotated.</summary>
    private static void Leaf(DrawingContext dc, Point center, double length, double width, double angle, Color color, double alpha)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var a = new Point(center.X - length / 2, center.Y);
            var b = new Point(center.X + length / 2, center.Y);
            ctx.BeginFigure(a, true, true);
            ctx.BezierTo(new Point(center.X - length * 0.2, center.Y - width), new Point(center.X + length * 0.2, center.Y - width), b, false, false);
            ctx.BezierTo(new Point(center.X + length * 0.2, center.Y + width), new Point(center.X - length * 0.2, center.Y + width), a, false, false);
        }
        g.Transform = new RotateTransform(angle, center.X, center.Y);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B)), null, g);
    }

    private static void Number(DrawingContext dc, string value, SlideStyle style, Point anchor, bool alignRight, Color color, string font, int weight, bool italic, bool top = false)
    {
        var size = 22 * style.Scale;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var ft = new FormattedText(value, Culture, FlowDirection.LeftToRight, FontCatalog.Typeface(font, weight, italic), size, brush, 1.0);
        var x = alignRight ? anchor.X - ft.Width : anchor.X;
        var y = top ? anchor.Y : anchor.Y - ft.Height;
        dc.DrawText(ft, new Point(x, y));
    }
}
