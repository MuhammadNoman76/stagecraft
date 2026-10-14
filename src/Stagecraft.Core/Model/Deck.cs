using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>Short random identifiers for decks, slides and elements (12 hex characters).</summary>
public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N").Substring(0, 12);
}

/// <summary>
/// A presentation: slide size, the theme (a complete copy, so a deck always looks the same even if a later
/// Stagecraft changes its built-in themes) and the slides. Coordinates are "units": a 16:9 slide is 1920 x 1080
/// units, a 4:3 slide 1440 x 1080. One unit is 1/144 inch (half a point), which maps cleanly to PowerPoint.
/// </summary>
public sealed class Deck
{
    /// <summary>The deck.json schema this build writes. Older files are migrated on load; newer ones are refused.</summary>
    public const int CurrentSchemaVersion = 2;

    public const double DefaultWidth = 1920;
    public const double DefaultHeight = 1080;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Ids.New();
    public string Title { get; set; } = "Untitled presentation";
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public DeckTheme Theme { get; set; } = new();
    public List<Slide> Slides { get; set; } = new();
    public DeckInfo Info { get; set; } = new();

    /// <summary>Slide numbers, date and footer text on every slide (off by default).</summary>
    public HeaderFooter Footer { get; set; } = new();

    /// <summary>Guides the user dragged out of the rulers (the editor snaps to them).</summary>
    public List<GuideLine> Guides { get; set; } = new();

    [JsonIgnore]
    public double AspectRatio => Height <= 0 ? 16.0 / 9 : Width / Height;

    public Slide? FindSlide(string id) => Slides.FirstOrDefault(s => s.Id == id);

    public int IndexOf(string slideId) => Slides.FindIndex(s => s.Id == slideId);
}

/// <summary>Bookkeeping written with every save.</summary>
public sealed class DeckInfo
{
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
    /// <summary>The Stagecraft version that last saved the file.</summary>
    public string? SavedWith { get; set; }
    /// <summary>Where the deck came from when it was imported (for example "PowerPoint").</summary>
    public string? ImportedFrom { get; set; }
}

/// <summary>Slide sizes offered in the UI.</summary>
public static class SlideSizes
{
    public static (double Width, double Height) Widescreen => (1920, 1080);
    public static (double Width, double Height) Standard => (1440, 1080);

    public static string Describe(double width, double height)
    {
        var ratio = width / height;
        if (Math.Abs(ratio - 16.0 / 9) < 0.01) return "16:9";
        if (Math.Abs(ratio - 4.0 / 3) < 0.01) return "4:3";
        if (Math.Abs(ratio - 16.0 / 10) < 0.01) return "16:10";
        if (Math.Abs(ratio - 1) < 0.01) return "1:1";
        return $"{width / 144:0.##} × {height / 144:0.##} in";
    }
}
