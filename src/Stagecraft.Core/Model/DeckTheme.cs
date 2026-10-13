namespace Stagecraft.Core.Model;

/// <summary>
/// The look of a deck: colours (a main scheme and an "emphasis" scheme for title, section and closing slides),
/// five accent colours, a font pair with the size of every text role, corner radius, margins and the theme's
/// decorations. Saved in full inside every deck.
/// </summary>
public sealed class DeckTheme
{
    public string Id { get; set; } = "aurora";
    public string Name { get; set; } = "Aurora";

    /// <summary>The palette chosen from the theme's variants.</summary>
    public string Variant { get; set; } = "violet";

    /// <summary>Which decoration painter draws behind the content (see Rendering.DecorPainter).</summary>
    public string Decor { get; set; } = "aurora";

    public ColorScheme Main { get; set; } = new();
    public ColorScheme Emphasis { get; set; } = new();

    /// <summary>accent1 .. accent5 as #RRGGBB.</summary>
    public List<string> Accents { get; set; } = new() { "#6C5CE7", "#00B8D9", "#FF6B9A", "#FFB547", "#22C55E" };

    public ThemeFonts Fonts { get; set; } = new();

    /// <summary>Corner radius for cards, images and charts, in units.</summary>
    public double Radius { get; set; } = 16;

    /// <summary>Safe margin at a 1080-unit slide height.</summary>
    public double Margin { get; set; } = 120;

    /// <summary>Title and closing slides: centred or left-aligned.</summary>
    public TextAlign TitleAlign { get; set; } = TextAlign.Center;

    /// <summary>Layouts that use the emphasis colours.</summary>
    public List<string> EmphasisLayouts { get; set; } = new() { "title", "section", "thank-you" };

    public DeckTheme Clone() => new()
    {
        Id = Id,
        Name = Name,
        Variant = Variant,
        Decor = Decor,
        Main = Main.Clone(),
        Emphasis = Emphasis.Clone(),
        Accents = Accents.ToList(),
        Fonts = Fonts.Clone(),
        Radius = Radius,
        Margin = Margin,
        TitleAlign = TitleAlign,
        EmphasisLayouts = EmphasisLayouts.ToList(),
    };
}

public sealed class ColorScheme
{
    public Fill Background { get; set; } = Fill.Solid("#FFFFFF");
    public string Text { get; set; } = "#14142B";
    public string TextMuted { get; set; } = "#5D5F7A";
    /// <summary>Cards and panels.</summary>
    public string Surface { get; set; } = "#F1F1F8";
    /// <summary>Replaces accent1 on these slides (for example white on a coloured background).</summary>
    public string? Accent { get; set; }
    public bool IsDark { get; set; }

    public ColorScheme Clone() => new()
    {
        Background = Background.Clone(),
        Text = Text,
        TextMuted = TextMuted,
        Surface = Surface,
        Accent = Accent,
        IsDark = IsDark,
    };
}

public sealed class ThemeFonts
{
    public string Heading { get; set; } = "Plus Jakarta Sans";
    public string Body { get; set; } = "Inter";
    public string Mono { get; set; } = "JetBrains Mono";

    public int HeadingWeight { get; set; } = 700;
    public int BodyWeight { get; set; } = 400;
    public int LabelWeight { get; set; } = 600;

    // Sizes in units at a 1080-unit slide height (1 unit = half a point).
    public double TitleSize { get; set; } = 112;
    public double SubtitleSize { get; set; } = 46;
    public double HeadingSize { get; set; } = 72;
    public double BodySize { get; set; } = 44;
    public double CaptionSize { get; set; } = 28;
    public double QuoteSize { get; set; } = 64;
    public double StatSize { get; set; } = 260;
    public double LabelSize { get; set; } = 24;

    public double HeadingLineHeight { get; set; } = 1.08;
    public double BodyLineHeight { get; set; } = 1.38;

    public bool LabelUppercase { get; set; } = true;
    public bool HeadingUppercase { get; set; }
    public bool QuoteItalic { get; set; } = true;
    /// <summary>"heading" or "body".</summary>
    public string QuoteFont { get; set; } = "heading";

    public ThemeFonts Clone() => (ThemeFonts)MemberwiseClone();
}
