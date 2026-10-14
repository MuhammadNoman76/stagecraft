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

    /// <summary>Videos and sounds: name → file on disk (large media don't go through the clipboard itself).</summary>
    public Dictionary<string, string> MediaFiles { get; set; } = new();

    /// <summary>The animations of the copied elements.</summary>
    public List<AnimationEffect> Animations { get; set; } = new();
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
            Elements = selected.Select(e => PortableCopy(e)).ToList(),
            Animations = Core.Animation.AnimationRules.EffectsOf(slide, selected).Select(a => a.Clone()).ToList(),
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
            Slides = slides.Select(s =>
            {
                var copy = DeckJson.Clone(s);
                copy.Elements = s.Elements.Select(e => PortableCopy(e)).ToList();
                return copy;
            }).ToList(),
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
            foreach (var media in DeckFile.MediaOf(e))
            {
                if (payload.Media.ContainsKey(media) || payload.MediaFiles.ContainsKey(media)) continue;
                var item = Session.Media.Get(media);
                if (item == null) continue;
                // Videos and sounds can be huge: the clipboard carries the path of the cached file instead.
                if (item.FilePath != null) payload.MediaFiles[media] = item.FilePath;
                else payload.Media[media] = Convert.ToBase64String(item.Data);
            }
        }
    }

    /// <summary>
    /// A copy for the clipboard: links to files and linked videos keep working in another presentation (their paths
    /// become full paths; saving makes them relative to the new presentation again).
    /// </summary>
    private Element PortableCopy(Element e)
    {
        var copy = DeckJson.Clone(e);
        var folder = DeckFolder;
        if (copy is MediaClipElement { IsLinked: true } clip && e is MediaClipElement original && Session.Media.ResolveLink(original) is { } full)
            clip.LinkPath = full;
        void Fix(Hyperlink? link)
        {
            if (link is { Kind: LinkKind.File } && LinkSafety.ResolveFile(link.Address, folder) is { } path) link.Address = path;
        }
        Fix(copy.Link);
        foreach (var body in DeckFile.TextBodies(copy))
        foreach (var p in body.Content.Paragraphs)
        foreach (var r in p.Runs)
            Fix(r.Link);
        return copy;
    }

    /// <summary>Takes the videos and sounds named on the clipboard into this presentation's media.</summary>
    private void AdoptMediaFiles(ClipboardPayload payload)
    {
        foreach (var (name, path) in payload.MediaFiles)
        {
            if (Session.Media.Get(name) != null || !DeckFile.IsSafeMediaName(name)) continue;
            try
            {
                if (!File.Exists(path)) continue; // the other window closed: the element shows "not found"
                var target = MediaCache.PathFor(name);
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
                    MediaCache.WriteAtomically(target, output =>
                    {
                        using var input = File.OpenRead(path);
                        input.CopyTo(output, 1 << 16);
                    });
                Session.Media.AddExistingFile(name, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Log.Error(ex, "Paste media");
            }
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
            AdoptMediaFiles(payload);
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
            DeckOperations.RenewIds(copy, keepMorphIdentity: false);
            Core.Animation.AnimationRules.Prune(copy);
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
        var copies = DeckOperations.CopyElements(elements, offset, offset, out var idMap);
        var effects = Core.Animation.AnimationRules.CopyFor(payload.Animations ?? new List<AnimationEffect>(), idMap);
        // Keep connectors attached to copied elements.
        foreach (var c in copies.OfType<ShapeElement>())
        {
            c.StartId = c.StartId != null && idMap.TryGetValue(c.StartId, out var s) ? s : null;
            c.EndId = c.EndId != null && idMap.TryGetValue(c.EndId, out var t) ? t : null;
        }
        Session.EditSlide(slide, "Paste", s =>
        {
            s.Elements.AddRange(copies);
            s.Animations.AddRange(effects);
            Core.Animation.AnimationRules.Prune(s);
        });
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
