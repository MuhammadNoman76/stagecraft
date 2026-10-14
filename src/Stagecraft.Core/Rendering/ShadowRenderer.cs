using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// Soft shadows: the silhouette of what an element draws, filled with the shadow colour, blurred (WPF's blur effect,
/// rendered once into a bitmap and cached) and drawn offset underneath. Being a picture, it looks the same in the
/// editor, the slide show, thumbnails and PDF (where it's embedded as a transparent image).
/// </summary>
public static class ShadowRenderer
{
    private sealed class Entry
    {
        public BitmapSource Bitmap = null!;
        /// <summary>Where the bitmap goes relative to the content's bounds origin (slide units).</summary>
        public Rect Placement;
        public long Used;
    }

    private static readonly Dictionary<string, Entry> Cache = new();
    private static readonly object Gate = new();
    private static long s_tick;
    private const int MaxEntries = 160;

    /// <summary>
    /// Draws the shadow of <paramref name="content"/>. <paramref name="key"/> identifies the content's look (element,
    /// part and theme); <paramref name="frameRotation"/> and the flips are those of the coordinate frame being drawn in,
    /// so the shadow always falls the same way on the slide.
    /// </summary>
    public static void Draw(DrawingContext dc, Shadow shadow, SlideStyle style, Action<DrawingContext> content, string key,
        double frameRotation = 0, bool flipH = false, bool flipV = false)
    {
        var color = style.Resolve(shadow.Color);
        if (color.A == 0) return;
        var drawing = new DrawingGroup();
        using (var c = drawing.Open()) content(c);
        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width < 0.5 || bounds.Height < 0.5) return;

        var fullKey = key + "|" + SlideStyle.ToHex(color) + "|" + shadow.Blur.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                      bounds.Width.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "x" + bounds.Height.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        Entry? entry;
        lock (Gate)
        {
            if (Cache.TryGetValue(fullKey, out entry)) entry.Used = ++s_tick;
        }
        if (entry == null)
        {
            entry = Render(drawing, bounds, color, Math.Max(0, shadow.Blur));
            if (entry == null) return;
            lock (Gate)
            {
                if (Cache.Count >= MaxEntries)
                {
                    foreach (var old in Cache.OrderBy(p => p.Value.Used).Take(MaxEntries / 3).Select(p => p.Key).ToList()) Cache.Remove(old);
                }
                entry.Used = ++s_tick;
                Cache[fullKey] = entry;
            }
        }

        // The offset is on the slide; the frame we draw in may be rotated or flipped.
        var a = shadow.Angle * Math.PI / 180;
        var offset = new Vector(Math.Cos(a) * shadow.Distance, Math.Sin(a) * shadow.Distance);
        if (Math.Abs(frameRotation) > 0.001)
        {
            var r = -frameRotation * Math.PI / 180;
            offset = new Vector(offset.X * Math.Cos(r) - offset.Y * Math.Sin(r), offset.X * Math.Sin(r) + offset.Y * Math.Cos(r));
        }
        if (flipH) offset.X = -offset.X;
        if (flipV) offset.Y = -offset.Y;
        var place = entry.Placement;
        dc.DrawImage(entry.Bitmap, new Rect(bounds.X + place.X + offset.X, bounds.Y + place.Y + offset.Y, place.Width, place.Height));
    }

    private static Entry? Render(DrawingGroup drawing, Rect bounds, Color color, double blur)
    {
        try
        {
            var pad = Math.Ceiling(blur * 1.1 + 2);
            var w = bounds.Width + 2 * pad;
            var h = bounds.Height + 2 * pad;
            // Blurry pictures don't need many pixels: half resolution for wide blurs, never more than 1600 px.
            var k = blur >= 16 ? 0.5 : 1.0;
            k = Math.Min(k, 1600 / Math.Max(w, h));
            k = Math.Max(k, 0.05);
            var pw = Math.Max(1, (int)Math.Ceiling(w * k));
            var ph = Math.Max(1, (int)Math.Ceiling(h * k));

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var mask = new DrawingBrush(drawing) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds };
            mask.Freeze();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(k, k));
                dc.PushTransform(new TranslateTransform(pad, pad));
                dc.PushOpacityMask(mask);
                dc.DrawRectangle(brush, null, new Rect(0, 0, bounds.Width, bounds.Height));
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }
            if (blur > 0.25) visual.Effect = new BlurEffect { Radius = blur * k, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
            var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return new Entry { Bitmap = bitmap, Placement = new Rect(-pad, -pad, pw / k, ph / k) };
        }
        catch (Exception)
        {
            return null; // a shadow is decoration: never let it break drawing
        }
    }

    /// <summary>Forgets every cached shadow (tests, theme changes are covered by the key).</summary>
    public static void Clear()
    {
        lock (Gate) Cache.Clear();
    }
}
