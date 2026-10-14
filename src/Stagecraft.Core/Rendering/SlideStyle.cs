using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// The theme as it applies to one slide: which colour scheme (main or emphasis, or one derived from a custom
/// background), colour references resolved to colours, and the font settings of every text role.
/// </summary>
public sealed class SlideStyle
{
    private SlideStyle(DeckTheme theme, ColorScheme scheme, Fill background, bool emphasis, double slideHeight)
    {
        Theme = theme;
        Scheme = scheme;
        Background = background;
        IsEmphasis = emphasis;
        Scale = slideHeight <= 0 ? 1 : slideHeight / 1080.0;
    }

    public DeckTheme Theme { get; }
    public ColorScheme Scheme { get; }
    public Fill Background { get; }
    public bool IsEmphasis { get; }

    /// <summary>Theme sizes are for a 1080-unit high slide; other heights scale them.</summary>
    public double Scale { get; }

    public double Margin => Theme.Margin * Scale;

    public bool IsDark => Scheme.IsDark;

    private string? _signature;

    /// <summary>Identifies everything that decides colours and fonts here (cache keys of drawn effects use it).</summary>
    public string Signature => _signature ??= string.Join("|", Theme.Id, Theme.Variant, string.Join(",", Theme.Accents), Scheme.Text, Scheme.TextMuted,
        Scheme.Surface, Scheme.Accent ?? "", Scheme.Background.Color, Theme.Fonts.Heading, Theme.Fonts.Body, Theme.Fonts.HeadingWeight, Theme.Fonts.BodyWeight,
        Scale.ToString("F4", CultureInfo.InvariantCulture), IsEmphasis);

    public static SlideStyle For(Deck deck, Slide slide) => For(deck.Theme, slide, deck.Height);

    public static SlideStyle For(DeckTheme theme, Slide slide, double slideHeight)
    {
        var emphasis = theme.EmphasisLayouts.Contains(slide.Layout);
        var scheme = emphasis ? theme.Emphasis : theme.Main;
        if (slide.Background is { } custom && custom.Kind != FillKind.None)
        {
            // A background the user picked: keep the text readable on it.
            var probe = new SlideStyle(theme, scheme, custom, emphasis, slideHeight);
            // Photos: assume white text (the image fill's Dim overlay keeps it readable).
            var dark = custom.Kind == FillKind.Image || IsDarkFill(probe, custom);
            if (dark != scheme.IsDark)
            {
                var other = emphasis ? theme.Main : theme.Emphasis;
                scheme = other.IsDark == dark ? other : Derived(dark);
            }
            return new SlideStyle(theme, scheme, custom, emphasis, slideHeight);
        }
        return new SlideStyle(theme, scheme, scheme.Background, emphasis, slideHeight);
    }

    private static ColorScheme Derived(bool dark) => dark
        ? new ColorScheme { Text = "#FFFFFF", TextMuted = "#FFFFFF@75", Surface = "#FFFFFF@14", IsDark = true }
        : new ColorScheme { Text = "#16161A", TextMuted = "#16161A@68", Surface = "#000000@6", IsDark = false };

    private static bool IsDarkFill(SlideStyle style, Fill fill)
    {
        var a = style.Resolve(fill.Color);
        var lum = Luminance(a);
        if (fill.Kind is FillKind.Linear or FillKind.Radial && fill.Color2 != null)
            lum = (lum + Luminance(style.Resolve(fill.Color2))) / 2;
        return lum < 0.45;
    }

    /// <summary>Relative luminance (0 = black, 1 = white), WCAG formula.</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    // ------------------------------------------------------------------ colours

    /// <summary>Resolves a colour reference ("accent2", "text@60", "#FF8800", ...). Unknown references give <paramref name="fallback"/>.</summary>
    public Color Resolve(string? reference, Color? fallback = null)
    {
        var fb = fallback ?? ParseHex(Scheme.Text) ?? Colors.Black;
        if (string.IsNullOrWhiteSpace(reference)) return fb;
        var text = reference!.Trim();
        double alpha = 1;
        var at = text.IndexOf('@');
        if (at > 0)
        {
            if (double.TryParse(text.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                alpha = MathEx.Clamp(pct / 100.0, 0, 1);
            text = text.Substring(0, at);
        }

        Color? color = text.ToLowerInvariant() switch
        {
            "text" => Resolve(Scheme.Text, Colors.Black),
            "text2" => Resolve(Scheme.TextMuted, Colors.Gray),
            "bg" => Resolve(Scheme.Background.Color, Colors.White),
            "bg2" or "surface" => Resolve(Scheme.Surface, Colors.LightGray),
            "accent1" or "accent" => Scheme.Accent != null ? Resolve(Scheme.Accent, null) : Accent(0),
            "accent2" => Accent(1),
            "accent3" => Accent(2),
            "accent4" => Accent(3),
            "accent5" => Accent(4),
            "white" => Colors.White,
            "black" => Colors.Black,
            "transparent" or "none" => Colors.Transparent,
            _ => ParseHex(text),
        };
        var c = color ?? fb;
        if (alpha < 1) c = Color.FromArgb((byte)Math.Round(c.A * alpha), c.R, c.G, c.B);
        return c;
    }

    /// <summary>The accent colour that is never overridden by the scheme (for charts and the palette swatches).</summary>
    public Color Accent(int index)
    {
        var list = Theme.Accents;
        if (list.Count == 0) return Color.FromRgb(0x6C, 0x5C, 0xE7);
        return ParseHex(list[((index % list.Count) + list.Count) % list.Count]) ?? Colors.SteelBlue;
    }

    /// <summary>"#RGB", "#RRGGBB", "#AARRGGBB" (and "@pct" opacity).</summary>
    public static Color? ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text!.Trim();
        double alpha = 1;
        var at = s.IndexOf('@');
        if (at > 0)
        {
            if (double.TryParse(s.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) alpha = MathEx.Clamp(pct / 100.0, 0, 1);
            s = s.Substring(0, at);
        }
        if (!s.StartsWith("#", StringComparison.Ordinal)) return null;
        s = s.Substring(1);
        if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
        if (s.Length is not (6 or 8)) return null;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        var c = s.Length == 6
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        if (alpha < 1) c.A = (byte)Math.Round(c.A * alpha);
        return c;
    }

    public static string ToHex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public SolidColorBrush SolidBrush(string? reference, Color? fallback = null)
    {
        var b = new SolidColorBrush(Resolve(reference, fallback));
        b.Freeze();
        return b;
    }

    /// <summary>A WPF brush for a fill over <paramref name="bounds"/> (image fills need the media store).</summary>
    public Brush? Brush(Fill? fill, Rect bounds, MediaStore? media = null)
    {
        if (fill == null) return null;
        Brush? brush;
        switch (fill.Kind)
        {
            case FillKind.None:
                return null;
            case FillKind.Solid:
                brush = new SolidColorBrush(Resolve(fill.Color));
                break;
            case FillKind.Linear:
            {
                var (start, end) = GradientPoints(fill.Angle);
                brush = new LinearGradientBrush(Resolve(fill.Color), Resolve(fill.Color2 ?? fill.Color), 0)
                {
                    StartPoint = start,
                    EndPoint = end,
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                };
                break;
            }
            case FillKind.Radial:
                brush = new RadialGradientBrush(Resolve(fill.Color), Resolve(fill.Color2 ?? fill.Color))
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.75,
                    RadiusY = 0.75,
                };
                break;
            case FillKind.Image:
            {
                var image = fill.Media != null ? media?.GetImage(fill.Media) : null;
                if (image == null) return new SolidColorBrush(Resolve("bg2"));
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                break;
            }
            default:
                return null;
        }
        brush.Freeze();
        return brush;
    }

    /// <summary>Start and end of a linear gradient at <paramref name="angle"/> degrees, in bounding-box coordinates.</summary>
    public static (Point Start, Point End) GradientPoints(double angle)
    {
        var a = angle * Math.PI / 180;
        var dx = Math.Cos(a) / 2;
        var dy = Math.Sin(a) / 2;
        return (new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
    }

    // ------------------------------------------------------------------ text roles

    public ResolvedFont RoleFont(TextRole role)
    {
        var f = Theme.Fonts;
        var s = Scale;
        return role switch
        {
            TextRole.Title => new ResolvedFont(f.Heading, f.HeadingWeight, false, f.TitleSize * s, f.HeadingLineHeight, "text", f.HeadingUppercase, 0.15),
            TextRole.Subtitle => new ResolvedFont(f.Body, f.BodyWeight, false, f.SubtitleSize * s, 1.3, "text2", false, 0.3),
            TextRole.Heading => new ResolvedFont(f.Heading, f.HeadingWeight, false, f.HeadingSize * s, f.HeadingLineHeight, "text", f.HeadingUppercase, 0.15),
            TextRole.Body => new ResolvedFont(f.Body, f.BodyWeight, false, f.BodySize * s, f.BodyLineHeight, "text", false, 0.55),
            TextRole.Caption => new ResolvedFont(f.Body, f.BodyWeight, false, f.CaptionSize * s, 1.35, "text2", false, 0.4),
            TextRole.Quote => new ResolvedFont(f.QuoteFont == "body" ? f.Body : f.Heading, f.QuoteFont == "body" ? f.BodyWeight : Math.Min(f.HeadingWeight, 500),
                f.QuoteItalic, f.QuoteSize * s, 1.18, "text", false, 0.3),
            TextRole.Stat => new ResolvedFont(f.Heading, f.HeadingWeight, false, f.StatSize * s, 1.0, "accent1", false, 0),
            TextRole.Label => new ResolvedFont(f.Body, f.LabelWeight, false, f.LabelSize * s, 1.3, "accent1", f.LabelUppercase, 0.3),
            TextRole.Code => new ResolvedFont(f.Mono, 400, false, f.BodySize * 0.85 * s, 1.45, "text", false, 0.3),
            _ => new ResolvedFont(f.Body, f.BodyWeight, false, f.BodySize * s, f.BodyLineHeight, "text", false, 0.5),
        };
    }

    /// <summary>"heading" / "body" / "mono" to a family name; anything else is a family name already.</summary>
    public string FontName(string? font, string fallback)
    {
        if (string.IsNullOrWhiteSpace(font)) return fallback;
        return font!.ToLowerInvariant() switch
        {
            "heading" => Theme.Fonts.Heading,
            "body" => Theme.Fonts.Body,
            "mono" => Theme.Fonts.Mono,
            _ => font,
        };
    }
}

/// <summary>A text role's font settings for one slide.</summary>
public sealed record ResolvedFont(string Family, int Weight, bool Italic, double Size, double LineHeight, string Color, bool Uppercase, double ParagraphSpacing);
