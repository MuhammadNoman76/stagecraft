using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>One slide: its layout, optional background override, the elements (back to front) and speaker notes.</summary>
public sealed class Slide
{
    public string Id { get; set; } = Ids.New();

    /// <summary>The smart layout it was made from (see <see cref="Layouts.LayoutIds"/>). Decides the default background.</summary>
    public string Layout { get; set; } = "title-content";

    /// <summary>A custom background; null uses the theme's background for this layout.</summary>
    public Fill? Background { get; set; }

    /// <summary>Theme decorations (shapes the theme draws behind the content) on this slide.</summary>
    public bool ShowDecorations { get; set; } = true;

    /// <summary>Elements in z-order: the first is at the back.</summary>
    public List<Element> Elements { get; set; } = new();

    public string Notes { get; set; } = "";

    /// <summary>Skipped in the slide show and in exports (unless asked).</summary>
    public bool Hidden { get; set; }

    /// <summary>When set, a new section with this name starts at this slide.</summary>
    public string? Section { get; set; }

    public Transition Transition { get; set; } = new();

    /// <summary>Seconds spent on this slide in the last rehearsal.</summary>
    public double? RehearsedSeconds { get; set; }

    /// <summary>Changes on every edit (thumbnail caches compare it). Not saved.</summary>
    [JsonIgnore]
    public long Revision { get; set; }

    public Element? Find(string id) => Elements.FirstOrDefault(e => e.Id == id);

    public T? FindPlaceholder<T>(string role) where T : Element =>
        Elements.OfType<T>().FirstOrDefault(e => e.Placeholder == role);

    /// <summary>The title text (first title / heading placeholder, else the first text), for lists and the outline.</summary>
    [JsonIgnore]
    public string TitleText
    {
        get
        {
            var title = Elements.OfType<TextElement>().FirstOrDefault(e => e.Placeholder == "title")
                        ?? Elements.OfType<TextElement>().FirstOrDefault(e => e.Text.Role is TextRole.Title or TextRole.Heading);
            return title?.Text.Content.PlainText.Trim() ?? "";
        }
    }
}

public enum TransitionKind { None, Fade, Push, Slide, Zoom, Morph }

public enum TransitionDirection { Left, Right, Up, Down }

/// <summary>How the slide arrives in the slide show.</summary>
public sealed class Transition
{
    public TransitionKind Kind { get; set; } = TransitionKind.None;
    public TransitionDirection Direction { get; set; } = TransitionDirection.Left;
    /// <summary>Seconds.</summary>
    public double Duration { get; set; } = 0.6;

    public Transition Clone() => new() { Kind = Kind, Direction = Direction, Duration = Duration };

    /// <summary>The duration this kind normally uses.</summary>
    public static double DefaultDuration(TransitionKind kind) => kind switch
    {
        TransitionKind.Morph => 1.0,
        TransitionKind.Fade => 0.5,
        TransitionKind.Zoom => 0.6,
        _ => 0.6,
    };
}
