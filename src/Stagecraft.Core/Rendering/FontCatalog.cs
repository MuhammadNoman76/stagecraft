using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// The fonts Stagecraft bundles (SIL Open Font License) live in a "Fonts" folder next to the program and are
/// loaded privately through a file URI, so nothing is installed on the PC. Other names resolve to installed fonts.
/// </summary>
public static class FontCatalog
{
    /// <summary>The bundled families, in the order the font menu lists them.</summary>
    public static IReadOnlyList<string> Bundled { get; } = new[]
    {
        "Inter", "Plus Jakarta Sans", "DM Sans", "Space Grotesk", "Playfair Display", "DM Serif Display", "Source Serif 4", "JetBrains Mono",
    };

    /// <summary>Installed Windows fonts offered next to the bundled ones (only those present are shown).</summary>
    public static IReadOnlyList<string> SystemChoices { get; } = new[]
    {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Georgia", "Times New Roman", "Verdana", "Consolas", "Aptos",
    };

    private static readonly ConcurrentDictionary<string, FontFamily> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static string? s_directory;

    /// <summary>Where the bundled font files are (…\Fonts next to Stagecraft.Core.dll).</summary>
    public static string Directory
    {
        get
        {
            if (s_directory != null) return s_directory;
            // The app's folder first (test runners may shadow-copy the assembly somewhere else), then the assembly's.
            var candidates = new[] { AppDomain.CurrentDomain.BaseDirectory, Path.GetDirectoryName(typeof(FontCatalog).Assembly.Location) };
            var found = candidates.Where(d => !string.IsNullOrEmpty(d)).Select(d => Path.Combine(d!, "Fonts"))
                .FirstOrDefault(d => System.IO.Directory.Exists(d) && System.IO.Directory.GetFiles(d, "*.ttf").Length > 0);
            s_directory = found ?? Path.Combine(candidates.FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? ".", "Fonts");
            return s_directory;
        }
    }

    public static bool IsBundled(string name) => Bundled.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fallback for characters a font doesn't have (emoji, CJK, …) and for missing fonts.</summary>
    private const string Fallback = "Segoe UI, Segoe UI Emoji, Segoe UI Symbol";

    /// <summary>The WPF font family for a name (bundled or installed), with sensible fallbacks.</summary>
    public static FontFamily Get(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? "Inter" : name!.Trim();
        return Cache.GetOrAdd(key, Create);
    }

    private static FontFamily Create(string name)
    {
        FontFamily family;
        if (IsBundled(name) && System.IO.Directory.Exists(Directory))
        {
            var baseUri = new Uri(Directory.TrimEnd('\\') + "\\", UriKind.Absolute);
            family = new FontFamily(baseUri, "./#" + WpfName(name, baseUri) + ", " + Fallback);
        }
        else
        {
            family = new FontFamily(name + ", " + Fallback);
        }
        return family;
    }

    private static Dictionary<string, string>? s_wpfNames;
    private static readonly object NamesGate = new();

    /// <summary>
    /// The name WPF groups a bundled family under. Fonts with optical-size names (DM Sans, Source Serif 4: their
    /// static instances are "14pt") are listed by their Windows family name ("DM Sans 14pt"), not the typographic one.
    /// </summary>
    private static string WpfName(string name, Uri folder)
    {
        lock (NamesGate)
        {
            if (s_wpfNames == null)
            {
                s_wpfNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                    foreach (var family in Fonts.GetFontFamilies(folder))
                    {
                        var source = family.Source;
                        var hash = source.IndexOf('#');
                        var wpf = hash >= 0 ? source.Substring(hash + 1) : source;
                        foreach (var typeface in family.GetTypefaces())
                        {
                            if (!typeface.TryGetGlyphTypeface(out var glyphs)) continue;
                            var typographic = glyphs.FamilyNames.TryGetValue(english, out var n) ? n : glyphs.FamilyNames.Values.FirstOrDefault();
                            if (string.IsNullOrEmpty(typographic)) continue;
                            // Prefer the shortest grouping (the regular / bold family over "… SemiBold").
                            if (!s_wpfNames.TryGetValue(typographic!, out var existing) || wpf.Length < existing.Length) s_wpfNames[typographic!] = wpf;
                            break;
                        }
                    }
                }
                catch
                {
                    // Fall back to the plain names.
                }
            }
            return s_wpfNames.TryGetValue(name, out var mapped) ? mapped : name;
        }
    }

    /// <summary>The family name to store for a WPF font family ("./#DM Sans 14pt, Segoe UI" → "DM Sans").</summary>
    public static string NameOf(FontFamily family)
    {
        var first = family.Source.Split(',')[0].Trim();
        var hash = first.IndexOf('#');
        var name = hash >= 0 ? first.Substring(hash + 1) : first;
        lock (NamesGate)
        {
            if (s_wpfNames != null)
                foreach (var pair in s_wpfNames)
                    if (string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }
        return name;
    }

    /// <summary>Installed fonts from <see cref="SystemChoices"/> that this PC has.</summary>
    public static IReadOnlyList<string> AvailableSystemChoices()
    {
        var installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
        return SystemChoices.Where(installed.Contains).ToList();
    }

    public static Typeface Typeface(string? name, int weight, bool italic) =>
        new(Get(name), italic ? FontStyles.Italic : FontStyles.Normal, FontWeight.FromOpenTypeWeight(MathEx.Clamp(weight, 1, 999)), FontStretches.Normal);

    /// <summary>The font file for a bundled family and style (for exporters that embed fonts), or null.</summary>
    public static string? FindFile(string family, int weight, bool italic)
    {
        if (!IsBundled(family)) return null;
        var prefix = family.Replace(" ", "");
        var candidates = System.IO.Directory.Exists(Directory) ? System.IO.Directory.GetFiles(Directory, prefix + "-*.ttf") : Array.Empty<string>();
        string Name(string f) => Path.GetFileNameWithoutExtension(f).Substring(prefix.Length + 1);
        int WeightOf(string style) => style.Replace("Italic", "") switch
        {
            "Medium" => 500,
            "SemiBold" => 600,
            "Bold" => 700,
            "ExtraBold" => 800,
            _ => 400,
        };
        return candidates
            .OrderBy(f => (Name(f).Contains("Italic") == italic ? 0 : 1000) + Math.Abs(WeightOf(Name(f)) - weight))
            .FirstOrDefault();
    }
}
