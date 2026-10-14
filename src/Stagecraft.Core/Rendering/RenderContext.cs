using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>What a drawing pass needs besides the slide: the deck (theme, size), its pictures and the mode.</summary>
public sealed class RenderContext
{
    public RenderContext(Deck deck, MediaStore media, bool editing = false)
    {
        Deck = deck;
        Media = media;
        Editing = editing;
    }

    public Deck Deck { get; }
    public MediaStore Media { get; }

    /// <summary>Editor drawing: empty placeholders show their hint ("Add a title"), empty pictures a drop target.</summary>
    public bool Editing { get; }

    /// <summary>"Today" for automatic footer dates (tests fix it).</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    public SlideStyle StyleFor(Slide slide) => SlideStyle.For(Deck, slide);

    /// <summary>The 1-based number shown by themes that print slide numbers (hidden slides don't count).</summary>
    public int NumberOf(Slide slide)
    {
        var n = 0;
        foreach (var s in Deck.Slides)
        {
            if (!s.Hidden || s.Id == slide.Id) n++;
            if (s.Id == slide.Id) return n;
        }
        return 0;
    }
}

/// <summary>The hint text an empty placeholder shows in the editor.</summary>
public static class PlaceholderHints
{
    public static string For(string? placeholder, TextRole role)
    {
        var p = placeholder ?? "";
        if (p.StartsWith("title", StringComparison.Ordinal)) return "Add a title";
        if (p.StartsWith("subtitle", StringComparison.Ordinal)) return "Add a subtitle";
        if (p.StartsWith("heading", StringComparison.Ordinal)) return "Add a heading";
        if (p.StartsWith("label", StringComparison.Ordinal)) return "Label";
        if (p.StartsWith("number", StringComparison.Ordinal)) return "42%";
        if (p.StartsWith("quote", StringComparison.Ordinal)) return "Add a quote that people will remember";
        if (p.StartsWith("author", StringComparison.Ordinal)) return "Name, role";
        if (p.StartsWith("name", StringComparison.Ordinal)) return "Name";
        if (p.StartsWith("role", StringComparison.Ordinal)) return "Role";
        if (p.StartsWith("date", StringComparison.Ordinal)) return "Date";
        if (p.StartsWith("item", StringComparison.Ordinal) || p.StartsWith("step", StringComparison.Ordinal)) return "Add a point";
        if (p.StartsWith("body", StringComparison.Ordinal)) return "Add text";
        return role switch
        {
            TextRole.Title or TextRole.Heading => "Add a title",
            TextRole.Subtitle => "Add a subtitle",
            _ => "Add text",
        };
    }
}
