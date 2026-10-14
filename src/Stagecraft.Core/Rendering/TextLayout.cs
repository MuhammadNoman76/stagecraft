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

    /// <summary>Where each run's text is in <see cref="Text"/> (as displayed: capitals applied).</summary>
    public List<(int Start, int Length, TextRun Run)> Runs { get; } = new();

    /// <summary>The paragraph's displayed text.</summary>
    public string DisplayText { get; internal set; } = "";

    /// <summary>Not drawn (a text piece of another paragraph is being drawn).</summary>
    public bool Hidden { get; internal set; }

    /// <summary>The bullet or number isn't drawn (the piece doesn't start the paragraph).</summary>
    public bool HideMarker { get; internal set; }
}

/// <summary>Draw only part of a text body: one paragraph, or a range of characters of one paragraph (animated pieces).</summary>
public readonly struct TextMask
{
    public TextMask(int paragraph, int start, int length)
    {
        Paragraph = paragraph;
        Start = start;
        Length = length;
    }

    public int Paragraph { get; }

    /// <summary>First character (in the displayed text) and how many; Length &lt; 0 = the whole paragraph.</summary>
    public int Start { get; }
    public int Length { get; }

    public static TextMask WholeParagraph(int paragraph) => new(paragraph, 0, -1);
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
        // Links: the theme's link colour (unless the text has its own colour), always underlined.
        var color = run.Color == null && run.Link != null ? LinkColorOn(style) : style.Resolve(run.Color ?? body.Color ?? role.Color);
        return new RunFormat(family, weight, italic, Math.Max(1, size), color, run.Underline == true || run.Link != null, run.Strike == true);
    }

    /// <summary>The colour reference of links (the scheme's accent, so links stay readable on emphasis slides).</summary>
    public const string LinkColor = "accent1";

    /// <summary>
    /// The colour links are drawn in on a slide: the scheme's accent, lightened on dark slides and darkened on light
    /// ones when it would be hard to read.
    /// </summary>
    public static Color LinkColorOn(SlideStyle style)
    {
        var c = style.Resolve(LinkColor);
        static double Luminance(Color x) => (0.2126 * x.R + 0.7152 * x.G + 0.0722 * x.B) / 255;
        static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        var l = Luminance(c);
        if (style.IsDark && l < 0.55) return Mix(c, Colors.White, MathEx.Clamp((0.55 - l) * 1.6, 0.25, 0.6));
        if (!style.IsDark && l > 0.62) return Mix(c, Colors.Black, 0.3);
        return c;
    }

    /// <summary>Lays out <paramref name="body"/> for a content width, at a size scale (1 = as designed).</summary>
    public static TextLayoutResult Layout(TextBody body, double width, SlideStyle style, double scale = 1, string? hint = null, Color? hintColor = null) =>
        Layout(body, width, style, scale, hint, hintColor, null, false);

    /// <summary>
    /// Lays out <paramref name="body"/>; with a <paramref name="mask"/> only that piece is visible (everything keeps its
    /// place), and <paramref name="bold"/> draws every run bold (Bold flash).
    /// </summary>
    public static TextLayoutResult Layout(TextBody body, double width, SlideStyle style, double scale, string? hint, Color? hintColor, TextMask? mask, bool bold)
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

            var formats = new List<(int Start, int Length, RunFormat Format, TextRun Run)>();
            var text = new System.Text.StringBuilder();
            var maxSize = 0.0;
            RunFormat? first = null;
            foreach (var run in paragraph.Runs)
            {
                var f = Resolve(run, body, style, scale);
                if (bold && f.Weight < 700) f = new RunFormat(f.Family, 700, f.Italic, f.Size, f.Color, f.Underline, f.Strike);
                first ??= f;
                var t = uppercase ? run.Text.ToUpper(Culture) : run.Text;
                if (t.Length == 0) continue;
                formats.Add((text.Length, t.Length, f, run));
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
            foreach (var (start, length, f, _) in formats)
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

            var displayText = text.ToString();
            var laidParagraph = new LaidParagraph(ft, marker, x, markerX, y + (i > 0 ? spacing : 0), dot, dotColor) { DisplayText = displayText };
            foreach (var (start, length, _, run) in formats) laidParagraph.Runs.Add((start, length, run));
            if (mask is { } m)
            {
                if (m.Paragraph != i) laidParagraph.Hidden = true;
                else if (m.Length >= 0)
                {
                    // Only the piece shows; the rest keeps its place but is see-through.
                    var s0 = MathEx.Clamp(m.Start, 0, displayText.Length);
                    var e0 = MathEx.Clamp(m.Start + m.Length, s0, displayText.Length);
                    if (s0 > 0) ft.SetForegroundBrush(Brushes.Transparent, 0, s0);
                    if (e0 < displayText.Length) ft.SetForegroundBrush(Brushes.Transparent, e0, displayText.Length - e0);
                    var firstVisible = 0;
                    while (firstVisible < displayText.Length && char.IsWhiteSpace(displayText[firstVisible])) firstVisible++;
                    laidParagraph.HideMarker = s0 > firstVisible;
                }
            }
            if (i > 0) y += spacing;
            laid.Add(laidParagraph);
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

    /// <summary>Like <see cref="LayoutInBox(TextBody, Rect, SlideStyle, string?, Color?)"/>, showing only a piece and / or all bold.</summary>
    public static TextLayoutResult LayoutInBox(TextBody body, Rect box, SlideStyle style, TextMask? mask, bool bold)
    {
        var inner = Inner(body, box);
        var scale = FitScale(body, inner.Width, inner.Height, style);
        return Layout(body, inner.Width, style, scale, null, null, mask, bold);
    }

    /// <summary>Where the top of each laid paragraph is drawn (the same arithmetic as <see cref="Draw"/>).</summary>
    public static double VerticalOffset(TextLayoutResult layout, Rect inner, VerticalAlign valign) => valign switch
    {
        VerticalAlign.Middle => (inner.Height - layout.Height) / 2,
        VerticalAlign.Bottom => inner.Height - layout.Height,
        _ => 0,
    };

    /// <summary>
    /// The pieces text animates in: whole paragraphs (with text), words or letters, as character ranges of the
    /// displayed paragraphs, numbered the way the animation timeline counts them.
    /// </summary>
    public static List<(Animation.PieceRef Ref, TextMask Mask)> Pieces(TextBody body, SlideStyle style, TextUnit unit)
    {
        var result = new List<(Animation.PieceRef, TextMask)>();
        var uppercase = body.Uppercase ?? style.RoleFont(body.Role).Uppercase;
        int ordinal = 0, word = 0, letter = 0;
        for (var i = 0; i < body.Content.Paragraphs.Count; i++)
        {
            var raw = body.Content.Paragraphs[i].Text;
            var text = uppercase ? raw.ToUpper(Culture) : raw;
            if (text.Trim().Length == 0) continue;
            var words = Animation.TextUnits.Ranges(text, TextUnit.Word).ToList();
            var letters = Animation.TextUnits.Ranges(text, TextUnit.Letter).ToList();
            switch (unit)
            {
                case TextUnit.Word:
                    foreach (var (s, l) in words)
                    {
                        var firstLetter = letter + letters.Count(x => x.Start < s);
                        result.Add((new Animation.PieceRef(i, ordinal, word++, firstLetter), new TextMask(i, s, l)));
                    }
                    break;
                case TextUnit.Letter:
                    foreach (var (s, l) in letters)
                    {
                        var wordIndex = word + Math.Max(0, words.FindLastIndex(w => w.Start <= s));
                        result.Add((new Animation.PieceRef(i, ordinal, wordIndex, letter + letters.IndexOf((s, l))), new TextMask(i, s, l)));
                    }
                    word += words.Count;
                    break;
                default:
                    result.Add((new Animation.PieceRef(i, ordinal, word, letter), TextMask.WholeParagraph(i)));
                    word += words.Count;
                    break;
            }
            letter += letters.Count;
            ordinal++;
        }
        return result;
    }

    /// <summary>
    /// Areas (in the box's coordinates, before rotation) covered by characters <paramref name="start"/>..+<paramref name="length"/>
    /// of paragraph <paramref name="paragraph"/>: one rectangle per line.
    /// </summary>
    public static List<Rect> RangeAreas(TextLayoutResult layout, Rect inner, VerticalAlign valign, int paragraph, int start, int length)
    {
        var result = new List<Rect>();
        if (paragraph < 0 || paragraph >= layout.Paragraphs.Count || length <= 0) return result;
        var p = layout.Paragraphs[paragraph];
        var top = inner.Y + VerticalOffset(layout, inner, valign) + p.Y;
        var s = MathEx.Clamp(start, 0, p.DisplayText.Length);
        var l = MathEx.Clamp(length, 0, p.DisplayText.Length - s);
        if (l == 0) return result;
        var geometry = p.Text.BuildHighlightGeometry(new Point(inner.X + p.X, top), s, l);
        if (geometry == null) return result;
        foreach (var figure in PathGeometry.CreateFromGeometry(geometry).Figures)
        {
            var g = new PathGeometry(new[] { figure });
            var b = g.Bounds;
            if (!b.IsEmpty && b.Width > 0 && b.Height > 0) result.Add(b);
        }
        return result;
    }


    /// <summary>
    /// Areas covered by characters of a paragraph counted in the model's text (the runs as stored, before capitals or
    /// other display changes): used to highlight find results. Box coordinates, before rotation.
    /// </summary>
    public static List<Rect> ModelRangeAreas(TextBody body, Rect box, SlideStyle style, int paragraph, int start, int length)
    {
        var layout = LayoutInBox(body, box, style);
        var inner = Inner(body, box);
        if (paragraph < 0 || paragraph >= layout.Paragraphs.Count || paragraph >= body.Content.Paragraphs.Count) return new List<Rect>();
        var laid = layout.Paragraphs[paragraph];
        int Display(int model)
        {
            var position = 0;
            foreach (var run in body.Content.Paragraphs[paragraph].Runs)
            {
                var len = run.Text?.Length ?? 0;
                if (model <= position + len)
                {
                    foreach (var (s, l, r) in laid.Runs)
                        if (ReferenceEquals(r, run)) return s + Math.Min(model - position, l);
                    return model;
                }
                position += len;
            }
            return model;
        }
        var from = Display(start);
        var to = Display(start + length);
        return RangeAreas(layout, inner, body.VAlign, paragraph, from, Math.Max(0, to - from));
    }
    /// <summary>The links in a text body and the areas they cover (box coordinates, before rotation).</summary>
    public static List<(Rect Area, Hyperlink Link)> LinkAreas(TextBody body, Rect box, SlideStyle style)
    {
        var result = new List<(Rect, Hyperlink)>();
        if (!body.Content.Paragraphs.Any(p => p.Runs.Any(r => r.Link != null))) return result;
        var layout = LayoutInBox(body, box, style);
        var inner = Inner(body, box);
        for (var i = 0; i < layout.Paragraphs.Count; i++)
        {
            foreach (var (start, length, run) in layout.Paragraphs[i].Runs)
            {
                if (run.Link == null) continue;
                foreach (var area in RangeAreas(layout, inner, body.VAlign, i, start, length)) result.Add((area, run.Link));
            }
        }
        return result;
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
            if (p.Hidden) continue;
            var top = inner.Y + offset + p.Y;
            if (p.HideMarker)
            {
                // A piece in the middle of the paragraph: no bullet.
            }
            else if (p.Dot > 0)
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
