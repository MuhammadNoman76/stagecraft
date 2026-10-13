using System.Runtime.ExceptionServices;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

/// <summary>Runs code on an STA thread (WPF drawing needs one; xUnit's threads are MTA).</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        }, 16 * 1024 * 1024);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Run(() => { result = func(); });
        return result;
    }
}

/// <summary>A folder under %TEMP% that is deleted afterwards.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stagecraft-tests-" + Guid.NewGuid().ToString("N").Substring(0, 10));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
            // best effort
        }
    }
}

internal static class Decks
{
    /// <summary>A deck with one slide of every layout in <paramref name="themeId"/>, with text filled in.</summary>
    public static Deck AllLayouts(string themeId = "aurora", string? variant = null)
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find(themeId), variant);
        deck.Slides.Clear();
        foreach (var layout in LayoutEngine.All)
        {
            var slide = LayoutEngine.CreateSlide(deck, layout.Id);
            foreach (var e in slide.Elements)
            {
                if (e is TextElement t && t.Placeholder != "decor")
                    t.Text.Content = RichText.FromPlain(layout.Name + " " + t.Placeholder, t.Text.Content.Paragraphs.FirstOrDefault()?.List ?? ListKind.None);
            }
            deck.Slides.Add(slide);
        }
        return deck;
    }

    /// <summary>A small PNG (solid colour) for media tests.</summary>
    public static byte[] Png(int width = 64, int height = 48, byte r = 200, byte g = 80, byte b = 40)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        return MediaStore.EncodePng(bitmap);
    }

    public static string Json(Deck deck) => DeckJson.Serialize(deck);
}
