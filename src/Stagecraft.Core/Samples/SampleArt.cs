using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Samples;

/// <summary>
/// Original artwork for the sample decks, drawn in code (skies, landscapes, a coffee cup, a skyline, portraits
/// as initials). No photos are bundled, so the samples carry no third-party images.
/// </summary>
public static class SampleArt
{
    private static Color C(string hex) => SlideStyle.ParseHex(hex) ?? Colors.Gray;

    private static byte[] Render(int width, int height, Action<DrawingContext> draw, bool jpeg = true)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return jpeg ? MediaStore.EncodeJpeg(bitmap, 90) : MediaStore.EncodePng(bitmap);
    }

    private static Brush Vertical(params (string Color, double Offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        foreach (var (color, offset) in stops) b.GradientStops.Add(new GradientStop(C(color), offset));
        b.Freeze();
        return b;
    }

    private static Geometry Ridge(double width, double height, double baseY, double amplitude, int seed, double detail)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, height), true, true);
            var y = baseY;
            ctx.LineTo(new Point(0, y), false, false);
            var steps = (int)(detail);
            for (var i = 1; i <= steps; i++)
            {
                var x = width * i / steps;
                var target = baseY - amplitude * (0.35 + 0.65 * rnd.NextDouble());
                if (i % 2 == 0) target = baseY - amplitude * 0.25 * rnd.NextDouble();
                var cx = width * (i - 0.5) / steps;
                ctx.QuadraticBezierTo(new Point(cx, (y + target) / 2 - amplitude * 0.1), new Point(x, target), true, true);
                y = target;
            }
            ctx.LineTo(new Point(width, height), false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Northern lights over mountains at night.</summary>
    public static byte[] Aurora(int w = 1600, int h = 1200) => Render(w, h, dc =>
    {
        dc.DrawRectangle(Vertical(("#05060F", 0), ("#121A3A", 0.55), ("#1F2B57", 1)), null, new Rect(0, 0, w, h));
        var rnd = new Random(7);
        for (var i = 0; i < 260; i++)
        {
            var r = rnd.NextDouble() * 1.6 + 0.4;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(60, 220), 255, 255, 255)), null, new Point(rnd.NextDouble() * w, rnd.NextDouble() * h * 0.6), r, r);
        }
        void Ribbon(double y0, double y1, string color, double alpha, double thickness, int seed)
        {
            var c = C(color);
            var g = new StreamGeometry();
            var rr = new Random(seed);
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(-100, y0), true, true);
                ctx.BezierTo(new Point(w * 0.25, y0 - 160 - rr.Next(80)), new Point(w * 0.55, y1 + 140), new Point(w + 100, y1 - 60), true, true);
                ctx.LineTo(new Point(w + 100, y1 - 60 + thickness), true, true);
                ctx.BezierTo(new Point(w * 0.55, y1 + 140 + thickness * 1.6), new Point(w * 0.25, y0 - 160 + thickness), new Point(-100, y0 + thickness), true, true);
            }
            var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            dc.DrawGeometry(brush, null, g);
        }
        Ribbon(h * 0.42, h * 0.3, "#3CF2B4", 0.55, 260, 1);
        Ribbon(h * 0.36, h * 0.22, "#7C5CFF", 0.45, 220, 2);
        Ribbon(h * 0.5, h * 0.4, "#22D3EE", 0.35, 180, 3);
        dc.DrawGeometry(new SolidColorBrush(C("#0B1028")), null, Ridge(w, h, h * 0.74, h * 0.2, 11, 9));
        dc.DrawGeometry(new SolidColorBrush(C("#070A1B")), null, Ridge(w, h, h * 0.86, h * 0.12, 5, 7));
    }, jpeg: true);

    /// <summary>Layered hills at sunrise.</summary>
    /// <remarks>Without the sun (<paramref name="sun"/> false) for a slide that animates its own.</remarks>
    public static byte[] Dawn(int w = 1600, int h = 1200, bool sun = true) => Render(w, h, dc =>
    {
        dc.DrawRectangle(Vertical(("#FCD9B8", 0), ("#F8A98B", 0.45), ("#E0787F", 0.8), ("#B65E86", 1)), null, new Rect(0, 0, w, h));
        var glow = new RadialGradientBrush(C("#FFF6D8"), Color.FromArgb(0, 255, 236, 190));
        dc.DrawEllipse(glow, null, new Point(w * 0.62, h * 0.48), h * 0.34, h * 0.34);
        if (sun) dc.DrawEllipse(new SolidColorBrush(C("#FFF3D4")), null, new Point(w * 0.62, h * 0.48), h * 0.09, h * 0.09);
        var layers = new[] { ("#C97C93", 0.58, 0.18), ("#9E5F86", 0.68, 0.16), ("#6E4A78", 0.78, 0.13), ("#3E2E57", 0.9, 0.1) };
        var seed = 3;
        foreach (var (color, y, amp) in layers) dc.DrawGeometry(new SolidColorBrush(C(color)), null, Ridge(w, h, h * y, h * amp, seed++, 6));
    }, jpeg: true);

    /// <summary>A latte seen from above, on a warm wooden table.</summary>
    public static byte[] Coffee(int w = 1920, int h = 1080) => Render(w, h, dc =>
    {
        dc.DrawRectangle(Vertical(("#5A3A28", 0), ("#3C2619", 1)), null, new Rect(0, 0, w, h));
        var rnd = new Random(4);
        for (var i = 0; i < 26; i++)
        {
            var y = i * h / 26.0 + rnd.NextDouble() * 6;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(10, 28), 255, 230, 200)), null, new Rect(0, y, w, 2 + rnd.NextDouble() * 3));
        }
        // Up and to the right, so a title in the lower left stays on the dark table.
        var c = new Point(w * 0.76, h * 0.37);
        var k = h * 0.84;
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Point(c.X + 26, c.Y + 34), k * 0.4, k * 0.4);
        dc.DrawEllipse(new SolidColorBrush(C("#F3EEE7")), null, c, k * 0.4, k * 0.4);
        dc.DrawEllipse(new SolidColorBrush(C("#E4DCD1")), null, c, k * 0.33, k * 0.33);
        dc.DrawEllipse(new SolidColorBrush(C("#FBF8F3")), null, c, k * 0.27, k * 0.27);
        dc.DrawEllipse(new SolidColorBrush(C("#7A4A2A")), null, c, k * 0.23, k * 0.23);
        var crema = new RadialGradientBrush(C("#B57A4B"), C("#6E3F22"));
        dc.DrawEllipse(crema, null, c, k * 0.215, k * 0.215);
        // latte-art heart
        var heart = new StreamGeometry();
        using (var ctx = heart.Open())
        {
            var s = k * 0.12;
            ctx.BeginFigure(new Point(c.X, c.Y + s * 0.9), true, true);
            ctx.BezierTo(new Point(c.X - s * 1.4, c.Y + s * 0.1), new Point(c.X - s * 0.9, c.Y - s * 0.95), new Point(c.X, c.Y - s * 0.35), true, true);
            ctx.BezierTo(new Point(c.X + s * 0.9, c.Y - s * 0.95), new Point(c.X + s * 1.4, c.Y + s * 0.1), new Point(c.X, c.Y + s * 0.9), true, true);
        }
        dc.DrawGeometry(new SolidColorBrush(C("#F4E6D3")), null, heart);
        // handle
        dc.DrawRoundedRectangle(new SolidColorBrush(C("#FBF8F3")), null, new Rect(c.X + k * 0.26, c.Y - k * 0.04, k * 0.14, k * 0.08), k * 0.04, k * 0.04);
        // beans
        for (var i = 0; i < 9; i++)
        {
            var p = new Point(w * (0.12 + 0.07 * i % 0.35) + rnd.Next(0, 160), h * (0.15 + 0.08 * i) + rnd.Next(-30, 30));
            var g = new EllipseGeometry(p, 26, 17) { Transform = new RotateTransform(rnd.Next(0, 180), p.X, p.Y) };
            dc.DrawGeometry(new SolidColorBrush(C("#2A170D")), null, g);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(C("#4B2C1A")), 3), new LineGeometry(new Point(p.X - 18, p.Y), new Point(p.X + 18, p.Y)) { Transform = new RotateTransform(rnd.Next(0, 180), p.X, p.Y) });
        }
    }, jpeg: true);

    /// <summary>A city skyline at dusk.</summary>
    public static byte[] Skyline(int w = 1600, int h = 1200) => Render(w, h, dc =>
    {
        dc.DrawRectangle(Vertical(("#0F2A4A", 0), ("#2D5D8A", 0.55), ("#F3A26B", 1)), null, new Rect(0, 0, w, h));
        var rnd = new Random(21);
        void Row(double baseline, string color, double maxHeight, double alpha, bool windows)
        {
            var x = -20.0;
            while (x < w)
            {
                var bw = rnd.Next(70, 170);
                var bh = maxHeight * (0.35 + rnd.NextDouble() * 0.65);
                var rect = new Rect(x, baseline - bh, bw, bh + 4);
                var c = C(color);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B)), null, rect);
                if (windows)
                {
                    for (var wy = rect.Top + 18; wy < baseline - 20; wy += 26)
                    for (var wx = rect.Left + 14; wx < rect.Right - 14; wx += 22)
                        if (rnd.NextDouble() < 0.32) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 214, 140)), null, new Rect(wx, wy, 9, 12));
                }
                x += bw + rnd.Next(4, 18);
            }
        }
        Row(h * 0.82, "#3B5F86", h * 0.42, 0.75, false);
        Row(h * 0.92, "#132842", h * 0.5, 1, true);
        dc.DrawRectangle(new SolidColorBrush(C("#0B1A2C")), null, new Rect(0, h * 0.92, w, h * 0.08));
    }, jpeg: true);

    /// <summary>A portrait placeholder: initials on a soft gradient circle background.</summary>
    public static byte[] Avatar(string initials, string from, string to, int size = 600) => Render(size, size, dc =>
    {
        var bg = new LinearGradientBrush(C(from), C(to), 45);
        dc.DrawRectangle(bg, null, new Rect(0, 0, size, size));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, new Point(size * 0.18, size * 0.12), size * 0.5, size * 0.5);
        var text = new FormattedText(initials, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            FontCatalog.Typeface("Plus Jakarta Sans", 700, false), size * 0.3, Brushes.White, 1.0);
        dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
    }, jpeg: true);
}
