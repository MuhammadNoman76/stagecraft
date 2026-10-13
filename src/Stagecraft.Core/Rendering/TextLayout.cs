using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>A paragraph laid out for drawing (positions relative to the text box's content area).</summary>
public sealed class LaidParagraph
{
    public LaidParagraph(FormattedText text, FormattedText? marker, double x, double markerX, double y, double dot = 0, Color dotColor = default)
    {
        Dot = dot;
        DotColor = dotColor;
        Text = text;
        Marker = marker;
        X = x;
        MarkerX = markerX;
        Y = y;
    }

    public FormattedText Text { get; }
    public FormattedText? Marker { get; }
    public double X { get; }
    public double MarkerX { get; }
    public double Y { get; }
    public double Height => Text.Height;

    /// <summary>Bullet drawn as a dot of this diameter (0: the marker text is used).</summary>
    public double Dot { get; }
    public Color DotColor { get; }
}

public sealed class TextLayoutResult
{
    public TextLayoutResult(IReadOnlyList<LaidParagraph> paragraphs, double width, double height, double scale)
    {
        Paragraphs = paragraphs;
        Width = width;
        Height = height;
        Scale = scale;
    }

    public IReadOnlyList<LaidParagraph> Paragraphs { get; }
    public double Width { get; }
    public double Height { get; }
    public double Scale { get; }
}

/// <summary>
/// Lays out rich text with WPF's FormattedText, one per paragraph (fonts, sizes, colours per run; alignment,
/// bullets, numbering, line and paragraph spacing per paragraph), and shrinks text that doesn't fit its box.
/// The same layout draws the editor, thumbnails, the slide show and the vector PDF, so they always match.
/// </summary>
public static class TextLayout
{
    /// <summary>Auto-fit never shrinks text below this fraction of its size.</summary>
    public const double MinFitScale = 0.3;

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    private sealed class FitEntry
    {
        public string Key = "";
        public double Scale = 1;
    }

    private static readonly ConditionalWeakTable<RichText, FitEntry> FitCache = new();

    /// <summary>The effective look of a run (role defaults, then the text box, then the run).</summary>
    public readonly struct RunFormat
    {
        public RunFormat(string family, int weight, bool italic, double size, Color color, bool underline, bool strike)
        {
            Family = family;
            Weight = weight;
            Italic = italic;
            Size = size;
            Color = color;
            Underline = underline;
            Strike = strike;
        }

        public string Family { get; }
        public int Weight { get; }
        public bool Italic { get; }
        public double Size { get; }
        public Color Color { get; }
        public bool Underline { get; }
        public bool Strike { get; }
    }

    public static ResolvedFont RoleOf(TextBody body, SlideStyle style) => style.RoleFont(body.Role);

    /// <summary>The base (unscaled) size of the text box: its own size or the role's.</summary>
    public static double BaseSize(TextBody body, SlideStyle style) => body.Size ?? style.RoleFont(body.Role).Size;

    public static int BaseWeight(TextBody body, ResolvedFont role) =>
        body.Bold == true ? Math.Max(700, role.Weight) : body.Bold == false ? Math.Min(role.Weight, 400) : role.Weight;

    public static RunFormat Resolve(TextRun run, TextBody body, SlideStyle style, double scale)
    {
        var role = style.RoleFont(body.Role);
        var family = style.FontName(run.Font ?? body.Font, role.Family);
        var baseWeight = BaseWeight(body, role);
        var weight = run.Bold == true ? Math.Max(700, baseWeight) : run.Bold == false ? Math.Min(baseWeight, 400) : baseWeight;
        var italic = run.Italic ?? body.Italic ?? role.Italic;
        var size = (run.Size ?? body.Size ?? role.Size) * scale;
        var color = style.Resolve(run.Color ?? body.Color ?? role.Color);
        return new RunFormat(family, weight, italic, Math.Max(1, size), color, run.Underline == true, run.Strike == true);
    }

    /// <summary>Lays out <paramref name="body"/> for a content width, at a size scale (1 = as designed).</summary>
    public static TextLayoutResult Layout(TextBody body, double width, SlideStyle style, double scale = 1, string? hint = null, Color? hintColor = null)
    {
        var role = style.RoleFont(body.Role);
        var uppercase = body.Uppercase ?? role.Uppercase;
        var lineSpacing = body.LineSpacing ?? role.LineHeight;
        var baseSize = (body.Size ?? role.Size) * scale;
        var spacing = role.ParagraphSpacing * baseSize;

        var content = body.Content;
        if (hint != null)
        {
            content = RichText.FromPlain(hint);
            foreach (var p in content.Paragraphs)
            foreach (var r in p.Runs)
                r.Color = hintColor.HasValue ? SlideStyle.ToHex(hintColor.Value) : "text2@55";
        }
        if (content.Paragraphs.Count == 0) content = RichText.FromPlain("");

        var laid = new List<LaidParagraph>(content.Paragraphs.Count);
        double y = 0, maxWidth = 0;
        var counters = new int[8];
        for (var i = 0; i < content.Paragraphs.Count; i++)
        {
            var paragraph = content.Paragraphs[i];
            var level = MathEx.Clamp(paragraph.Level, 0, 7);
            // Numbering restarts after any paragraph that isn't numbered at this level or deeper.
            if (paragraph.List == ListKind.Number) counters[level]++;
            for (var l = paragraph.List == ListKind.Number ? level + 1 : level; l < counters.Length; l++) counters[l] = 0;

            var formats = new List<(int Start, int Length, RunFormat Format)>();
            var text = new System.Text.StringBuilder();
            var maxSize = 0.0;
            RunFormat? first = null;
            foreach (var run in paragraph.Runs)
            {
                var f = Resolve(run, body, style, scale);
                first ??= f;
                var t = uppercase ? run.Text.ToUpper(Culture) : run.Text;
                if (t.Length == 0) continue;
                formats.Add((text.Length, t.Length, f));
                text.Append(t);
                maxSize = Math.Max(maxSize, f.Size);
            }
            var empty = new TextRun();
            first ??= Resolve(paragraph.Runs.FirstOrDefault() ?? empty, body, style, scale);
            if (maxSize <= 0) maxSize = first.Value.Size;
            if (text.Length == 0) text.Append('​'); // keeps the line height of an empty paragraph

            var indent = 0.0;
            FormattedText? marker = null;
            var markerX = 0.0;
            var dot = 0.0;
            var dotColor = default(Color);
            var unit = first.Value.Size;
            if (paragraph.List != ListKind.None)
            {
                indent = unit * 1.15 * (level + 1);
                markerX = unit * 1.15 * level;
                var markerText = paragraph.List == ListKind.Bullet ? (level % 2 == 0 ? "•" : "–") : counters[level].ToString(Culture) + ".";
                var markerColor = body.Role == TextRole.Body && body.Color == null && hint == null ? style.Resolve("accent1") : first.Value.Color;
                if (paragraph.List == ListKind.Bullet && level % 2 == 0)
                {
                    // A drawn dot: crisp and the same in every font.
                    dot = Math.Max(2, unit * (level == 0 ? 0.3 : 0.24));
                    dotColor = markerColor;
                    marker = Make(markerText, first.Value.Family, 700, false, unit * 1.05, markerColor);
                }
                else
                {
                    marker = Make(markerText, first.Value.Family, Math.Max(first.Value.Weight, 600), false, unit * 0.92, markerColor);
                }
            }
            else if (level > 0)
            {
                indent = unit * 1.15 * level;
            }

            var ft = Make(text.ToString(), first.Value.Family, first.Value.Weight, first.Value.Italic, first.Value.Size, first.Value.Color);
            foreach (var (start, length, f) in formats)
            {
                ft.SetFontFamily(FontCatalog.Get(f.Family), start, length);
                ft.SetFontWeight(FontWeight.FromOpenTypeWeight(MathEx.Clamp(f.Weight, 1, 999)), start, length);
                ft.SetFontStyle(f.Italic ? FontStyles.Italic : FontStyles.Normal, start, length);
                ft.SetFontSize(f.Size, start, length);
                ft.SetForegroundBrush(Frozen(f.Color), start, length);
                if (f.Underline || f.Strike)
                {
                    var decorations = new TextDecorationCollection();
                    if (f.Underline) decorations.Add(TextDecorations.Underline);
                    if (f.Strike) decorations.Add(TextDecorations.Strikethrough);
                    ft.SetTextDecorations(decorations, start, length);
                }
            }
            ft.MaxTextWidth = Math.Max(1, width - indent);
            ft.TextAlignment = (paragraph.Align ?? body.Align) switch
            {
                TextAlign.Center => TextAlignment.Center,
                TextAlign.Right => TextAlignment.Right,
                TextAlign.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };
            ft.LineHeight = Math.Max(1, maxSize * lineSpacing);
            ft.Trimming = TextTrimming.None;
            var x = indent + (paragraph.List == ListKind.None ? Balance(ft, body.Role) : 0);

            if (i > 0) y += spacing;
            laid.Add(new LaidParagraph(ft, marker, x, markerX, y, dot, dotColor));
            y += ft.Height;
            maxWidth = Math.Max(maxWidth, indent + ft.WidthIncludingTrailingWhitespace);
        }
        return new TextLayoutResult(laid, maxWidth, y, scale);
    }

    private static int LineCount(FormattedText ft) => Math.Max(1, (int)Math.Round(ft.Height / Math.Max(1, ft.LineHeight)));

    /// <summary>
    /// Balanced wrapping for display text (titles, subtitles, headings, quotes): when a paragraph wraps, it gets the
    /// narrowest width that keeps the same number of lines, so lines are similar in length and a lone last word
    /// ("2026") doesn't hang on a line of its own. Returns how far to shift the paragraph to keep its alignment.
    /// </summary>
    private static double Balance(FormattedText ft, TextRole role)
    {
        if (role is not (TextRole.Title or TextRole.Subtitle or TextRole.Heading or TextRole.Quote)) return 0;
        if (ft.TextAlignment == TextAlignment.Justify) return 0;
        var full = ft.MaxTextWidth;
        var lines = LineCount(ft);
        if (lines < 2 || lines > 6 || full < 40) return 0;
        double lo = full * 0.4, hi = full;
        for (var k = 0; k < 9; k++)
        {
            var mid = (lo + hi) / 2;
            ft.MaxTextWidth = mid;
            if (LineCount(ft) > lines) lo = mid;
            else hi = mid;
        }
        var narrow = Math.Min(full, Math.Ceiling(hi + 1));
        ft.MaxTextWidth = narrow;
        if (LineCount(ft) != lines)
        {
            ft.MaxTextWidth = full;
            return 0;
        }
        return ft.TextAlignment switch
        {
            TextAlignment.Center => (full - narrow) / 2,
            TextAlignment.Right => full - narrow,
            _ => 0,
        };
    }

    private static FormattedText Make(string text, string family, int weight, bool italic, double size, Color color)
    {
#pragma warning disable CS0618 // pixelsPerDip: slides are drawn at any scale with ideal (vector) text
        return new FormattedText(text, Culture, FlowDirection.LeftToRight, FontCatalog.Typeface(family, weight, italic), Math.Max(1, size), Frozen(color), 1.0);
#pragma warning restore CS0618
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The size scale (MinFitScale..1) at which the text fits the box: 1 when it fits or auto-fit is off.
    /// Shrinks gracefully: sizes and line spacing scale together, so the design keeps its proportions.
    /// </summary>
    public static double FitScale(TextBody body, double width, double height, SlideStyle style)
    {
        if (!body.AutoFit || width <= 1 || height <= 1 || body.Content.IsEmpty) return 1;
        var key = FitKey(body, width, height, style);
        if (FitCache.TryGetValue(body.Content, out var entry) && entry.Key == key) return entry.Scale;

        double scale;
        if (Fits(body, width, height, style, 1)) scale = 1;
        else
        {
            double lo = MinFitScale, hi = 1;
            if (!Fits(body, width, height, style, lo)) scale = lo;
            else
            {
                for (var i = 0; i < 9; i++)
                {
                    var mid = (lo + hi) / 2;
                    if (Fits(body, width, height, style, mid)) lo = mid;
                    else hi = mid;
                }
                scale = Math.Floor(lo * 100) / 100; // stable values: no jitter while typing
            }
        }

        if (FitCache.TryGetValue(body.Content, out var existing)) FitCache.Remove(body.Content);
        FitCache.Add(body.Content, new FitEntry { Key = key, Scale = scale });
        return scale;
    }

    private static bool Fits(TextBody body, double width, double height, SlideStyle style, double scale)
    {
        var layout = Layout(body, width, style, scale);
        return layout.Height <= height + 0.5 && layout.Width <= width + 0.5;
    }

    private static string FitKey(TextBody body, double width, double height, SlideStyle style)
    {
        var f = style.Theme.Fonts;
        var sb = new System.Text.StringBuilder(160);
        sb.Append(width.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
          .Append(height.ToString("F1", CultureInfo.InvariantCulture)).Append('|')
          .Append(body.Role).Append('|').Append(body.Size).Append('|').Append(body.Font).Append('|').Append(body.Bold).Append('|')
          .Append(body.Italic).Append('|').Append(body.LineSpacing).Append('|').Append(body.Uppercase).Append('|')
          .Append(style.Scale.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
          .Append(f.Heading).Append(f.Body).Append(f.HeadingWeight).Append(f.BodyWeight).Append(f.TitleSize).Append(f.HeadingSize)
          .Append(f.BodySize).Append(f.SubtitleSize).Append(f.QuoteSize).Append(f.StatSize).Append(f.HeadingLineHeight).Append(f.BodyLineHeight)
          .Append('|').Append(body.Content.PlainText.GetHashCode()).Append('|').Append(RunSignature(body.Content));
        return sb.ToString();
    }

    private static int RunSignature(RichText text)
    {
        unchecked
        {
            var h = 17;
            foreach (var p in text.Paragraphs)
            {
                h = h * 31 + (int)p.List * 7 + p.Level;
                foreach (var r in p.Runs)
                    h = h * 31 + (r.Size?.GetHashCode() ?? 0) + (r.Bold == true ? 3 : 0) + (r.Font?.GetHashCode() ?? 0);
            }
            return h;
        }
    }

    /// <summary>Lays out the text of an element for its box (inset and auto-fit applied).</summary>
    public static TextLayoutResult LayoutInBox(TextBody body, Rect box, SlideStyle style, string? hint = null, Color? hintColor = null)
    {
        var inner = Inner(body, box);
        if (hint != null) return Layout(body, inner.Width, style, 1, hint, hintColor);
        var scale = FitScale(body, inner.Width, inner.Height, style);
        return Layout(body, inner.Width, style, scale);
    }

    public static Rect Inner(TextBody body, Rect box)
    {
        var inset = Math.Max(0, Math.Min(body.Inset, Math.Min(box.Width, box.Height) / 2 - 1));
        return new Rect(box.X + inset, box.Y + inset, Math.Max(1, box.Width - 2 * inset), Math.Max(1, box.Height - 2 * inset));
    }

    /// <summary>Draws a layout in a box with the vertical alignment.</summary>
    public static void Draw(DrawingContext dc, TextLayoutResult layout, Rect inner, VerticalAlign valign)
    {
        var offset = valign switch
        {
            VerticalAlign.Middle => (inner.Height - layout.Height) / 2,
            VerticalAlign.Bottom => inner.Height - layout.Height,
            _ => 0,
        };
        foreach (var p in layout.Paragraphs)
        {
            var top = inner.Y + offset + p.Y;
            if (p.Dot > 0)
            {
                // Centred on the first line, a little above the middle (where lower-case letters sit).
                var cy = top + p.Text.LineHeight / 2 + p.Text.LineHeight * 0.02;
                var brush = new SolidColorBrush(p.DotColor);
                brush.Freeze();
                dc.DrawEllipse(brush, null, new Point(inner.X + p.MarkerX + p.Dot * 0.9, cy), p.Dot / 2, p.Dot / 2);
            }
            else if (p.Marker != null)
            {
                // Centre the marker on the first line.
                var firstLine = p.Text.LineHeight;
                var markerTop = top + (firstLine - p.Marker.Height) / 2;
                dc.DrawText(p.Marker, new Point(inner.X + p.MarkerX, markerTop));
            }
            dc.DrawText(p.Text, new Point(inner.X + p.X, top));
        }
    }

    /// <summary>The height the text needs at full size in a box of this width (for text boxes that grow with their text).</summary>
    public static double MeasureHeight(TextBody body, double boxWidth, SlideStyle style)
    {
        var inset = Math.Max(0, body.Inset);
        var layout = Layout(body, Math.Max(1, boxWidth - 2 * inset), style, 1);
        return layout.Height + 2 * inset;
    }
}
