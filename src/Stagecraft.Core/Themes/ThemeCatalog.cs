using Stagecraft.Core.Model;

namespace Stagecraft.Core.Themes;

/// <summary>A palette of a built-in theme.</summary>
public sealed class ThemeVariant
{
    public ThemeVariant(string id, string name, Action<DeckTheme> apply)
    {
        Id = id;
        Name = name;
        Apply = apply;
    }

    public string Id { get; }
    public string Name { get; }
    public Action<DeckTheme> Apply { get; }
}

/// <summary>A built-in designer theme: a base look plus a few palettes.</summary>
public sealed class ThemeDefinition
{
    private readonly Func<DeckTheme> _base;

    public ThemeDefinition(string id, string name, string description, Func<DeckTheme> baseTheme, params ThemeVariant[] variants)
    {
        Id = id;
        Name = name;
        Description = description;
        _base = baseTheme;
        Variants = variants;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<ThemeVariant> Variants { get; }

    /// <summary>A fresh copy of the theme with the given palette (the first one if unknown).</summary>
    public DeckTheme Create(string? variant = null)
    {
        var theme = _base();
        theme.Id = Id;
        theme.Name = Name;
        var v = Variants.FirstOrDefault(x => x.Id == variant) ?? Variants[0];
        theme.Variant = v.Id;
        v.Apply(theme);
        return theme;
    }
}

/// <summary>The ten built-in themes.</summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<ThemeDefinition> All { get; } = Build();

    public static ThemeDefinition Default => All[0];

    public static ThemeDefinition? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ColorScheme Scheme(string bg, string text, string muted, string surface, bool dark, string? accent = null, Fill? fill = null) => new()
    {
        Background = fill ?? Fill.Solid(bg),
        Text = text,
        TextMuted = muted,
        Surface = surface,
        IsDark = dark,
        Accent = accent,
    };

    private static List<string> Accents(params string[] colors) => colors.ToList();

    private static List<ThemeDefinition> Build() => new()
    {
        // 1. Modern, light, soft colour glows.
        new ThemeDefinition("aurora", "Aurora", "Modern and airy, with soft colour glows",
            () => new DeckTheme
            {
                Decor = "aurora",
                Main = Scheme("#FBFBFE", "#14142B", "#5D5F7A", "#F0F0F8", false),
                Emphasis = Scheme("#120E2E", "#FFFFFF", "#C9C6E8", "#221C4A", true, fill: Fill.Linear("#1A1442", "#0E0B24", 120)),
                Accents = Accents("#6C5CE7", "#00B8D9", "#FF6B9A", "#FFB547", "#22C55E"),
                Fonts = new ThemeFonts { Heading = "Plus Jakarta Sans", Body = "Inter", HeadingWeight = 800, LabelWeight = 700 },
                Radius = 20,
                TitleAlign = TextAlign.Center,
            },
            new ThemeVariant("violet", "Violet", _ => { }),
            new ThemeVariant("ocean", "Ocean", t =>
            {
                t.Accents = Accents("#2563EB", "#06B6D4", "#8B5CF6", "#F59E0B", "#10B981");
                t.Emphasis.Background = Fill.Linear("#0B1E46", "#06112B", 120);
                t.Emphasis.Surface = "#13295A";
                t.Emphasis.TextMuted = "#B9C7E6";
            }),
            new ThemeVariant("sunrise", "Sunrise", t =>
            {
                t.Accents = Accents("#F05A28", "#FF8A5B", "#E0457B", "#FFC145", "#2BB673");
                t.Main = Scheme("#FFFBF7", "#2A1A14", "#7A6258", "#FBF0E8", false);
                t.Emphasis.Background = Fill.Linear("#3A1220", "#1C0A12", 120);
                t.Emphasis.Surface = "#4A1A2A";
                t.Emphasis.TextMuted = "#EBC8C4";
            })),

        // 2. Magazine: serif headings on warm paper.
        new ThemeDefinition("editorial", "Editorial", "Elegant serif type on warm paper",
            () => new DeckTheme
            {
                Decor = "editorial",
                Main = Scheme("#F6F1E9", "#1E1B18", "#6B635A", "#ECE5D8", false),
                Emphasis = Scheme("#1E1B18", "#F6F1E9", "#BDB4A6", "#2C2823", true),
                Accents = Accents("#B5442C", "#2F5D50", "#C9A227", "#3B4A6B", "#8E6C8A"),
                Fonts = new ThemeFonts
                {
                    Heading = "Playfair Display", Body = "Source Serif 4", HeadingWeight = 700, BodyWeight = 400, LabelWeight = 600,
                    TitleSize = 120, HeadingSize = 76, BodySize = 42, QuoteSize = 68, HeadingLineHeight = 1.06, BodyLineHeight = 1.42,
                },
                Radius = 2,
                TitleAlign = TextAlign.Left,
                EmphasisLayouts = new List<string> { "section", "thank-you" },
            },
            new ThemeVariant("paper", "Paper", _ => { }),
            new ThemeVariant("ink", "Ink", t =>
            {
                t.Main = Scheme("#191714", "#F3EDE3", "#ABA294", "#26231F", true);
                t.Emphasis = Scheme("#F3EDE3", "#191714", "#6B635A", "#E6DED1", false);
                t.Accents = Accents("#E07A5F", "#81B29A", "#F2CC8F", "#8DA9C4", "#C9A0DC");
            }),
            new ThemeVariant("sage", "Sage", t =>
            {
                t.Main = Scheme("#EEF0E8", "#1D241F", "#5F675F", "#E1E5D9", false);
                t.Emphasis = Scheme("#24342B", "#EEF0E8", "#B7C2B5", "#2F4136", true);
                t.Accents = Accents("#3E6B4E", "#B5442C", "#C9A227", "#5B7083", "#8E6C8A");
            })),

        // 3. Bold dark with neon accents.
        new ThemeDefinition("midnight", "Midnight", "Bold and dark with a bright accent",
            () => new DeckTheme
            {
                Decor = "midnight",
                Main = Scheme("#0B0D12", "#F5F7FA", "#9AA3B2", "#161A22", true),
                Emphasis = Scheme("#0B0D12", "#FFFFFF", "#A9B1BF", "#161A22", true),
                Accents = Accents("#7CFFCB", "#5B8CFF", "#FF5C8A", "#FFC857", "#B388FF"),
                Fonts = new ThemeFonts
                {
                    Heading = "Space Grotesk", Body = "Inter", HeadingWeight = 700, LabelWeight = 500,
                    TitleSize = 124, HeadingSize = 76, HeadingLineHeight = 1.02,
                },
                Radius = 14,
                TitleAlign = TextAlign.Left,
            },
            new ThemeVariant("mint", "Mint", _ => { }),
            new ThemeVariant("electric", "Electric", t => t.Accents = Accents("#5B8CFF", "#7CFFCB", "#FF5C8A", "#FFC857", "#B388FF")),
            new ThemeVariant("ember", "Ember", t => t.Accents = Accents("#FF7A45", "#FFC857", "#FF5C8A", "#5B8CFF", "#7CFFCB"))),

        // 4. Soft pastels, rounded and friendly.
        new ThemeDefinition("pastel", "Pastel", "Soft colours and friendly rounded shapes",
            () => new DeckTheme
            {
                Decor = "pastel",
                Main = Scheme("#FFF8F3", "#2B2733", "#6E6878", "#FFFFFF", false),
                Emphasis = Scheme("#FFE6EC", "#2B2733", "#6E6878", "#FFFFFF", false),
                Accents = Accents("#F25F8B", "#2BA8B5", "#F2A900", "#8A6CF0", "#3FAE7A"),
                Fonts = new ThemeFonts { Heading = "Plus Jakarta Sans", Body = "Plus Jakarta Sans", HeadingWeight = 800, BodyWeight = 400, LabelWeight = 700 },
                Radius = 28,
                TitleAlign = TextAlign.Center,
            },
            new ThemeVariant("peach", "Peach", _ => { }),
            new ThemeVariant("mint", "Mint", t =>
            {
                t.Main = Scheme("#F2FBF7", "#1F2D29", "#5F716B", "#FFFFFF", false);
                t.Emphasis = Scheme("#D9F3E8", "#1F2D29", "#5F716B", "#FFFFFF", false);
                t.Accents = Accents("#1E9E77", "#F25F8B", "#2B8BD9", "#F2A900", "#8A6CF0");
            }),
            new ThemeVariant("lilac", "Lilac", t =>
            {
                t.Main = Scheme("#F8F5FF", "#27223A", "#6A6480", "#FFFFFF", false);
                t.Emphasis = Scheme("#E9E1FF", "#27223A", "#6A6480", "#FFFFFF", false);
                t.Accents = Accents("#7A5AF0", "#F25F8B", "#2BA8B5", "#F2A900", "#3FAE7A");
            })),

        // 5. Clean and structured for business.
        new ThemeDefinition("corporate", "Corporate", "Clean, structured and trustworthy",
            () => new DeckTheme
            {
                Decor = "corporate",
                Main = Scheme("#FFFFFF", "#0F1B2D", "#5A6B85", "#F3F6FA", false),
                Emphasis = Scheme("#0F1B2D", "#FFFFFF", "#AEBBD0", "#1B2A42", true),
                Accents = Accents("#0B5FFF", "#00A3A3", "#F5A524", "#E5484D", "#6E56CF"),
                Fonts = new ThemeFonts { Heading = "Inter", Body = "Inter", HeadingWeight = 700, LabelWeight = 600, TitleSize = 104, HeadingSize = 64 },
                Radius = 8,
                TitleAlign = TextAlign.Left,
            },
            new ThemeVariant("blue", "Blue", _ => { }),
            new ThemeVariant("teal", "Teal", t =>
            {
                t.Accents = Accents("#0F766E", "#2563EB", "#F59E0B", "#DC2626", "#7C3AED");
                t.Emphasis = Scheme("#0B2E2B", "#FFFFFF", "#A7C7C2", "#12403B", true);
            }),
            new ThemeVariant("graphite", "Graphite", t =>
            {
                t.Accents = Accents("#F26B1D", "#334155", "#0EA5E9", "#22C55E", "#A855F7");
                t.Main = Scheme("#FFFFFF", "#1E232B", "#5F6773", "#F2F3F5", false);
                t.Emphasis = Scheme("#1E232B", "#FFFFFF", "#B0B6C0", "#2B313B", true);
            })),

        // 6. Minimal black and white with one accent.
        new ThemeDefinition("mono", "Mono", "Minimal black and white, one sharp accent",
            () => new DeckTheme
            {
                Decor = "mono",
                Main = Scheme("#FFFFFF", "#0A0A0A", "#6B6B6B", "#F2F2F2", false),
                Emphasis = Scheme("#0A0A0A", "#FFFFFF", "#A3A3A3", "#1C1C1C", true),
                Accents = Accents("#FF3B30", "#0A0A0A", "#8E8E93", "#3A3A3C", "#C7C7CC"),
                Fonts = new ThemeFonts { Heading = "Inter", Body = "Inter", HeadingWeight = 600, LabelWeight = 500, TitleSize = 116, HeadingLineHeight = 1.04 },
                Radius = 0,
                TitleAlign = TextAlign.Left,
            },
            new ThemeVariant("signal", "Signal", _ => { }),
            new ThemeVariant("carbon", "Carbon", t =>
            {
                t.Main = Scheme("#0E0E0E", "#F5F5F5", "#9A9A9A", "#1B1B1B", true);
                t.Emphasis = Scheme("#F5F5F5", "#0E0E0E", "#6B6B6B", "#E6E6E6", false);
                t.Accents = Accents("#FFD60A", "#F5F5F5", "#8E8E93", "#C7C7CC", "#3A3A3C");
            }),
            new ThemeVariant("cobalt", "Cobalt", t => t.Accents = Accents("#1F4FFF", "#0A0A0A", "#8E8E93", "#3A3A3C", "#C7C7CC"))),

        // 7. Vivid gradients.
        new ThemeDefinition("gradient", "Gradient", "Vivid colour gradients that pop",
            () => new DeckTheme
            {
                Decor = "gradient",
                Main = Scheme("#FFFFFF", "#1A1530", "#635C7A", "#F4F1FF", false, fill: Fill.Linear("#FFFFFF", "#F6F2FF", 90)),
                Emphasis = Scheme("#7F5AF0", "#FFFFFF", "#F1E9FF", "#FFFFFF@16", true, accent: "#FFFFFF", fill: Fill.Linear("#7F5AF0", "#FF5E8E", 135)),
                Accents = Accents("#7F5AF0", "#FF5E8E", "#2CB1BC", "#FFB86B", "#3DDC97"),
                Fonts = new ThemeFonts { Heading = "DM Sans", Body = "DM Sans", HeadingWeight = 700, LabelWeight = 700, TitleSize = 116 },
                Radius = 22,
                TitleAlign = TextAlign.Center,
            },
            new ThemeVariant("berry", "Berry", _ => { }),
            new ThemeVariant("ocean", "Ocean", t =>
            {
                t.Accents = Accents("#0072FF", "#00C6FF", "#7F5AF0", "#FFB86B", "#3DDC97");
                t.Main.Background = Fill.Linear("#FFFFFF", "#F0F7FF", 90);
                t.Main.Surface = "#EEF5FF";
                t.Emphasis.Background = Fill.Linear("#0052D4", "#00B4DB", 135);
            }),
            new ThemeVariant("citrus", "Citrus", t =>
            {
                t.Accents = Accents("#F76B1C", "#F7B500", "#E2366B", "#2CB1BC", "#3DDC97");
                t.Main.Background = Fill.Linear("#FFFFFF", "#FFF7EC", 90);
                t.Main.Surface = "#FFF3E2";
                t.Emphasis.Background = Fill.Linear("#F76B1C", "#F7B500", 135);
            })),

        // 8. Swiss typographic style: big type, grid, strong colour.
        new ThemeDefinition("swiss", "Swiss", "Big type on a strict grid",
            () => new DeckTheme
            {
                Decor = "swiss",
                Main = Scheme("#F4F2EE", "#111111", "#5C5C5C", "#E8E5DF", false),
                Emphasis = Scheme("#E63322", "#FFFFFF", "#FFD9D3", "#F0503F", true, accent: "#111111"),
                Accents = Accents("#E63322", "#111111", "#6B6B6B", "#1D4ED8", "#F2B705"),
                Fonts = new ThemeFonts
                {
                    Heading = "Inter", Body = "Inter", HeadingWeight = 800, LabelWeight = 700,
                    TitleSize = 150, HeadingSize = 84, HeadingLineHeight = 0.98, StatSize = 300,
                },
                Radius = 0,
                TitleAlign = TextAlign.Left,
            },
            new ThemeVariant("red", "Red", _ => { }),
            new ThemeVariant("blue", "Blue", t =>
            {
                t.Accents = Accents("#1D4ED8", "#111111", "#6B6B6B", "#E63322", "#F2B705");
                t.Emphasis = Scheme("#1D4ED8", "#FFFFFF", "#D6E0FF", "#3461E6", true, accent: "#F2B705");
            }),
            new ThemeVariant("yellow", "Yellow", t =>
            {
                t.Accents = Accents("#111111", "#E63322", "#6B6B6B", "#1D4ED8", "#F2B705");
                t.Emphasis = Scheme("#FFC700", "#111111", "#4A3F00", "#FFD233", false, accent: "#111111");
            })),

        // 9. Natural and calm.
        new ThemeDefinition("botanical", "Botanical", "Calm, natural colours and organic shapes",
            () => new DeckTheme
            {
                Decor = "botanical",
                Main = Scheme("#F3F1EA", "#1F2A24", "#5E6B63", "#E6E8DE", false),
                Emphasis = Scheme("#1F3A2E", "#F3F1EA", "#B9C6BB", "#2A4A3B", true),
                Accents = Accents("#2F6B4F", "#C4793D", "#8DA77A", "#D9B44A", "#6B8FA3"),
                Fonts = new ThemeFonts
                {
                    Heading = "DM Serif Display", Body = "DM Sans", HeadingWeight = 400, LabelWeight = 700,
                    TitleSize = 124, HeadingSize = 78, HeadingLineHeight = 1.04, QuoteItalic = true,
                },
                Radius = 18,
                TitleAlign = TextAlign.Center,
            },
            new ThemeVariant("forest", "Forest", _ => { }),
            new ThemeVariant("desert", "Desert", t =>
            {
                t.Main = Scheme("#F7F0E6", "#2E2219", "#776557", "#EEE3D3", false);
                t.Emphasis = Scheme("#5A3422", "#F7F0E6", "#E1C9B5", "#6B4230", true);
                t.Accents = Accents("#B5562D", "#5E7D4E", "#D9A441", "#8A6B5A", "#4F7C8A");
            }),
            new ThemeVariant("lagoon", "Lagoon", t =>
            {
                t.Main = Scheme("#EEF4F2", "#16302B", "#58706A", "#DFEAE6", false);
                t.Emphasis = Scheme("#0F3D3A", "#EEF4F2", "#AFCBC5", "#1A4E4A", true);
                t.Accents = Accents("#167A6E", "#E07A5F", "#5FA8D3", "#E9C46A", "#7B9E87");
            })),

        // 10. Dark professional.
        new ThemeDefinition("slate", "Slate", "Dark, focused and professional",
            () => new DeckTheme
            {
                Decor = "slate",
                Main = Scheme("#1A1F27", "#E8EDF3", "#929CAA", "#242B35", true),
                Emphasis = Scheme("#11151B", "#FFFFFF", "#A4AEBB", "#1E242D", true, fill: Fill.Linear("#1D232C", "#0E1116", 120)),
                Accents = Accents("#3FB9A3", "#F78166", "#79C0FF", "#D2A8FF", "#E3B341"),
                Fonts = new ThemeFonts { Heading = "DM Sans", Body = "Inter", HeadingWeight = 700, LabelWeight = 600 },
                Radius = 12,
                TitleAlign = TextAlign.Left,
            },
            new ThemeVariant("teal", "Teal", _ => { }),
            new ThemeVariant("coral", "Coral", t => t.Accents = Accents("#F78166", "#3FB9A3", "#79C0FF", "#D2A8FF", "#E3B341")),
            new ThemeVariant("sky", "Sky", t => t.Accents = Accents("#79C0FF", "#3FB9A3", "#F78166", "#D2A8FF", "#E3B341"))),
    };
}
