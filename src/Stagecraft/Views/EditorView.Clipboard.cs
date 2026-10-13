using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

/// <summary>What Stagecraft puts on the clipboard: elements or slides, with the pictures they use.</summary>
public sealed class ClipboardPayload
{
    public const string Format = "Stagecraft.Clipboard.v1";

    public List<Element> Elements { get; set; } = new();
    public List<Slide> Slides { get; set; } = new();
    public Dictionary<string, string> Media { get; set; } = new();
    public double SourceWidth { get; set; } = 1920;
    public double SourceHeight { get; set; } = 1080;
    public string? SourceSlideId { get; set; }
}

public static class SlideClipboard
{
    public static bool HasSlides()
    {
        try
        {
            if (!Clipboard.ContainsData(ClipboardPayload.Format)) return false;
            return Read()?.Slides.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public static ClipboardPayload? Read()
    {
        try
        {
            if (Clipboard.GetData(ClipboardPayload.Format) is not string json) return null;
            return JsonSerializer.Deserialize<ClipboardPayload>(json, DeckJson.Options);
        }
        catch (Exception ex) when (ex is COMException or ExternalException or JsonException or NotSupportedException)
        {
            Log.Error(ex, "Read clipboard");
            return null;
        }
    }

    public static void Write(ClipboardPayload payload, string? text, System.Windows.Media.Imaging.BitmapSource? picture)
    {
        var data = new DataObject();
        data.SetData(ClipboardPayload.Format, JsonSerializer.Serialize(payload, DeckJson.Options));
        if (!string.IsNullOrWhiteSpace(text)) data.SetData(DataFormats.UnicodeText, text);
        if (picture != null)
        {
            data.SetData("PNG", new MemoryStream(MediaStore.EncodePng(picture)));
            data.SetImage(picture);
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(60); // another app has the clipboard open
            }
        }
    }
}

public partial class EditorView
{
    private void Copy()
    {
        if (RailFocused || _altViewIndex == 1 || (!HasSelection && HasSlide && !Canvas.IsKeyboardFocusWithin)) CopySlides();
        else if (HasSelection) CopyElements(cut: false);
    }

    private void Cut()
    {
        if (RailFocused || _altViewIndex == 1)
        {
            CopySlides();
            DeleteSlides();
        }
        else if (HasSelection) CopyElements(cut: true);
    }

    private void CopyElements(bool cut)
    {
        var slide = _state.CurrentSlide;
        var selected = _state.SelectedElements;
        if (slide == null || selected.Count == 0) return;
        var payload = new ClipboardPayload
        {
            Elements = selected.Select(e => DeckJson.Clone(e)).ToList(),
            SourceWidth = Session.Deck.Width,
            SourceHeight = Session.Deck.Height,
            SourceSlideId = slide.Id,
        };
        AddMedia(payload, selected);
        var text = string.Join(Environment.NewLine, selected.Select(e => e switch
        {
            TextElement t => t.Text.Content.PlainText,
            ShapeElement { Text: { } b } => b.Content.PlainText,
            _ => "",
        }).Where(s => s.Length > 0));
        SlideClipboard.Write(payload, text, RenderSelection(slide, selected));
        if (cut) DeleteSelection();
    }

    private void CopySlides()
    {
        var slides = TargetSlides();
        if (slides.Count == 0) return;
        var payload = new ClipboardPayload
        {
            Slides = slides.Select(s => DeckJson.Clone(s)).ToList(),
            SourceWidth = Session.Deck.Width,
            SourceHeight = Session.Deck.Height,
        };
        AddMedia(payload, slides.SelectMany(s => s.Elements));
        foreach (var s in slides)
            if (s.Background?.Media is { } bg && Session.Media.GetBytes(bg) is { } bytes) payload.Media[bg] = Convert.ToBase64String(bytes);
        var picture = SlideRenderer.Render(slides[0], Session.CreateRenderContext(), 1280);
        SlideClipboard.Write(payload, string.Join(Environment.NewLine, slides.Select(s => s.TitleText).Where(t => t.Length > 0)), picture);
        _window.ShowMessage(slides.Count == 1 ? "Slide copied." : $"{slides.Count} slides copied.", 2);
    }

    private void AddMedia(ClipboardPayload payload, IEnumerable<Element> elements)
    {
        foreach (var e in elements)
        {
            var media = e switch
            {
                ImageElement { Media: { } m } => m,
                ShapeElement { Fill.Media: { } f } => f,
                TextElement { Fill.Media: { } tf } => tf,
                _ => null,
            };
            if (media != null && !payload.Media.ContainsKey(media) && Session.Media.GetBytes(media) is { } bytes)
                payload.Media[media] = Convert.ToBase64String(bytes);
        }
    }

    /// <summary>A picture of the selection (other apps can paste it).</summary>
    private System.Windows.Media.Imaging.BitmapSource? RenderSelection(Slide slide, IReadOnlyList<Element> selected)
    {
        try
        {
            var bounds = DeckOperations.BoundsOf(selected);
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return null;
            var scale = Math.Min(2, 2000 / Math.Max(bounds.Width, bounds.Height));
            var ctx = Session.CreateRenderContext();
            var style = ctx.StyleFor(slide);
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                foreach (var e in slide.Elements.Where(selected.Contains)) ElementRenderer.Draw(dc, e, style, ctx);
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Render selection for the clipboard");
            return null;
        }
    }

    private void Paste()
    {
        if (Canvas.IsEditingText) return; // the text box pastes text itself
        IDataObject? data;
        try
        {
            data = Clipboard.GetDataObject();
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return;
        }
        if (data == null) return;

        if (data.GetDataPresent(ClipboardPayload.Format) && SlideClipboard.Read() is { } payload)
        {
            foreach (var (name, base64) in payload.Media)
            {
                try
                {
                    Session.Media.AddExisting(name, Convert.FromBase64String(base64));
                }
                catch (FormatException)
                {
                    // damaged entry: the element shows "missing picture"
                }
            }
            if (payload.Slides.Count > 0) PasteSlides(payload);
            else if (payload.Elements.Count > 0) PasteElements(payload);
            return;
        }
        var pictures = ClipboardImages.Read(data);
        if (pictures.Count > 0)
        {
            InsertPictures(pictures, null, null);
            return;
        }
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
        {
            PasteText(text);
        }
    }

    private void PasteSlides(ClipboardPayload payload)
    {
        var deck = Session.Deck;
        var slides = payload.Slides.Select(s =>
        {
            var copy = DeckJson.Clone(s);
            copy.Id = Ids.New();
            copy.Section = null;
            foreach (var e in copy.Elements)
            {
                e.MorphKey = null;
                e.Id = Ids.New();
            }
            ScaleElements(copy.Elements, payload.SourceWidth, payload.SourceHeight);
            return copy;
        }).ToList();
        var index = _state.CurrentSlide == null ? deck.Slides.Count : _state.CurrentIndex + 1;
        Session.EditStructure(slides.Count == 1 ? "Paste slide" : "Paste slides", d => d.Slides.InsertRange(index, slides));
        _state.GoTo(slides[0]);
    }

    private void ScaleElements(IEnumerable<Element> elements, double fromW, double fromH)
    {
        var deck = Session.Deck;
        if (Math.Abs(fromW - deck.Width) < 0.5 && Math.Abs(fromH - deck.Height) < 0.5) return;
        var k = Math.Min(deck.Width / fromW, deck.Height / fromH);
        var ox = (deck.Width - fromW * k) / 2;
        var oy = (deck.Height - fromH * k) / 2;
        foreach (var e in elements)
        {
            e.X = e.X * k + ox;
            e.Y = e.Y * k + oy;
            e.Width *= k;
            e.Height *= k;
        }
    }

    private void PasteElements(ClipboardPayload payload)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var elements = payload.Elements;
        ScaleElements(elements, payload.SourceWidth, payload.SourceHeight);
        // Pasting where the originals still sit: offset so the copy is visible.
        var collides = elements.All(e => slide.Elements.Any(x => Math.Abs(x.X - e.X) < 1 && Math.Abs(x.Y - e.Y) < 1 && x.GetType() == e.GetType()));
        var offset = collides ? 24 : 0;
        var copies = DeckOperations.CopyElements(elements, offset, offset);
        // Keep connectors attached to copied elements.
        var idMap = elements.Select((e, i) => (e.Id, copies[i].Id)).ToDictionary(x => x.Item1, x => x.Item2);
        foreach (var c in copies.OfType<ShapeElement>())
        {
            c.StartId = c.StartId != null && idMap.TryGetValue(c.StartId, out var s) ? s : null;
            c.EndId = c.EndId != null && idMap.TryGetValue(c.EndId, out var t) ? t : null;
        }
        Session.EditSlide(slide, "Paste", s => s.Elements.AddRange(copies));
        _state.Select(copies.Select(c => c.Id));
        Canvas.Focus();
    }

    private void PasteText(string text)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var style = SlideStyle.For(Session.Deck, slide);
        var body = new TextBody { Role = TextRole.Body, AutoFit = false, Content = RichText.FromOutline(text.Trim()) };
        var width = 1100.0;
        var height = Math.Ceiling(Core.Rendering.TextLayout.MeasureHeight(body, width, style));
        var element = new TextElement { Text = body };
        element.Bounds = PlaceAtCenter(width, Math.Min(height, Session.Deck.Height * 0.8));
        if (height > Session.Deck.Height * 0.8) body.AutoFit = true;
        AddElements("Paste text", element);
    }
}
