using System.Windows.Media.Imaging;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Views.Editor;

/// <summary>Cached preview pictures of themes and layouts for galleries.</summary>
public static class Previews
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    /// <summary>A title slide in the theme (and palette).</summary>
    public static BitmapSource Theme(ThemeDefinition theme, string? variant, int width, string? title = null)
    {
        var key = $"theme|{theme.Id}|{variant}|{width}|{title}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var deck = DeckOperations.NewDeck(theme, variant);
        var slide = deck.Slides[0];
        var titleBox = slide.FindPlaceholder<TextElement>("title")!;
        titleBox.Text.Content = RichText.FromPlain(title ?? "Your big idea");
        if (title == "Aa") titleBox.Text.Size = 230;
        slide.FindPlaceholder<TextElement>("subtitle")!.Text.Content = RichText.FromPlain(theme.Description);
        var bitmap = SlideRenderer.Render(slide, new RenderContext(deck, new MediaStore()), width);
        Cache[key] = bitmap;
        return bitmap;
    }

    /// <summary>A layout in the deck's current theme, with its placeholder hints showing the structure.</summary>
    public static BitmapSource Layout(Deck deck, string layoutId, int width)
    {
        var t = deck.Theme;
        var key = $"layout|{layoutId}|{t.Id}|{t.Variant}|{t.Decor}|{string.Join(",", t.Accents)}|{t.Main.Background.Color}|{deck.Width}x{deck.Height}|{width}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var probe = new Deck { Width = deck.Width, Height = deck.Height, Theme = deck.Theme.Clone() };
        var slide = LayoutEngine.CreateSlide(probe, layoutId);
        probe.Slides.Add(slide);
        var bitmap = SlideRenderer.Render(slide, new RenderContext(probe, new MediaStore(), editing: true), width);
        Cache[key] = bitmap;
        if (Cache.Count > 400) Cache.Clear();
        return bitmap;
    }
}
