using System.Diagnostics;
using System.Windows;
using PdfSharp.Pdf.IO;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Export;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

public class RenderTests
{
    private readonly ITestOutputHelper _output;

    public RenderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BundledFonts_AreFound_ByTheirFamilyNames() => Sta.Run(() =>
    {
        foreach (var name in FontCatalog.Bundled)
        {
            var family = FontCatalog.Get(name);
            var typeface = new System.Windows.Media.Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), name + " has no glyph typeface");
            var file = glyphs.FontUri.LocalPath;
            Assert.StartsWith(FontCatalog.Directory, file, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(name, FontCatalog.NameOf(family));
        }
    });

    [Fact]
    public void AutoFit_ShrinksLongText_LeavesShortTextAlone() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("corporate"));
        var style = SlideStyle.For(deck, deck.Slides[0]);
        var shortBody = new TextBody { Role = TextRole.Body, Content = RichText.FromPlain("Short") };
        Assert.Equal(1, TextLayout.FitScale(shortBody, 800, 300, style), 6);

        var lines = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"Point number {i} with a few more words to wrap"));
        var longBody = new TextBody { Role = TextRole.Body, Content = RichText.FromPlain(lines, ListKind.Bullet) };
        var scale = TextLayout.FitScale(longBody, 800, 500, style);
        Assert.InRange(scale, 0.31, 0.99);
        // At that scale the text fits.
        var layout = TextLayout.Layout(longBody, 800, style, scale);
        Assert.True(layout.Height <= 500.5, $"height {layout.Height}");
        Assert.True(TextLayout.Layout(longBody, 800, style, 1).Height > 500.5);

        longBody.AutoFit = false;
        Assert.Equal(1, TextLayout.FitScale(longBody, 800, 500, style), 6);
    });

    [Fact]
    public void AllLayouts_AllThemes_Render() => Sta.Run(() =>
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var deck = Decks.AllLayouts(theme.Id);
            var ctx = new RenderContext(deck, new MediaStore());
            foreach (var slide in deck.Slides)
            {
                var bitmap = SlideRenderer.Render(slide, ctx, 320);
                Assert.Equal(320, bitmap.PixelWidth);
                Assert.Equal(180, bitmap.PixelHeight);
            }
        }
    });

    [Fact]
    public void PdfExport_PageCountAndSize() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var doc = DemoDecks.AuroraKickoff();
        doc.Deck.Slides[1].Hidden = true;
        var visible = doc.Deck.Slides.Count(s => !s.Hidden);
        foreach (var vector in new[] { true, false })
        {
            var path = dir.File(vector ? "vector.pdf" : "pictures.pdf");
            var progress = 0;
            PdfExporter.Export(doc.Deck, doc.Media, path, new PdfExportOptions { Vector = vector, RasterWidth = 1280, Progress = (done, _) => progress = done });
            Assert.Equal(visible, progress);
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(visible, pdf.PageCount);
            Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(), p =>
            {
                Assert.Equal(960, p.Width.Point, 1);
                Assert.Equal(540, p.Height.Point, 1);
            });
            _output.WriteLine($"{(vector ? "vector" : "pictures")}: {new FileInfo(path).Length / 1024} KB");
        }
        PdfExporter.Export(doc.Deck, doc.Media, dir.File("all.pdf"), new PdfExportOptions { IncludeHidden = true });
        using var all = PdfReader.Open(dir.File("all.pdf"), PdfDocumentOpenMode.Import);
        Assert.Equal(doc.Deck.Slides.Count, all.PageCount);
        Assert.False(File.Exists(dir.File("all.pdf.part")));
    });

    [Fact]
    public void PdfExport_HasSelectableTextLayer() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("editorial"));
        deck.Slides[0].FindPlaceholder<TextElement>("title")!.Text.Content = RichText.FromPlain("Searchable words");
        string Export(bool selectable)
        {
            using var ms = new MemoryStream();
            PdfExporter.Export(deck, new MediaStore(), ms, new PdfExportOptions { SelectableText = selectable });
            return System.Text.Encoding.ASCII.GetString(ms.ToArray());
        }
        var with = Export(true);
        Assert.Contains("/FontFile2", with);
        Assert.Contains("/ToUnicode", with);
        Assert.Matches(@"/ca\s+0[\s/>]", with); // the text layer is fully transparent
        var without = Export(false);
        Assert.DoesNotContain("/FontFile2", without);
    });

    [Fact]
    public void PdfExport_FourByThree() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var deck = Decks.AllLayouts("botanical");
        DeckOperations.SetSlideSize(deck, 1440, 1080);
        var path = dir.File("43.pdf");
        PdfExporter.Export(deck, new MediaStore(), path, new PdfExportOptions());
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.Equal(deck.Slides.Count, pdf.PageCount);
        Assert.Equal(720, pdf.Pages[0].Width.Point, 1);
        Assert.Equal(540, pdf.Pages[0].Height.Point, 1);
    });

    [Fact]
    public void ImageExport_AllSlides() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var doc = DemoDecks.CoffeePitch();
        var files = ImageExporter.ExportAll(doc.Deck, doc.Media, dir.Path, "Coffee pitch", ImageFormat.Png, 960);
        Assert.Equal(doc.Deck.Slides.Count(s => !s.Hidden), files.Count);
        var first = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(files[0]), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Assert.Equal(960, first.PixelWidth);
        Assert.Equal(540, first.PixelHeight);
        var jpeg = ImageExporter.Encode(doc.Deck, doc.Media, doc.Deck.Slides[0], ImageFormat.Jpeg, 1920);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
    });

    [Fact]
    public void Performance_200Slides_LoadAndThumbnails() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var source = DemoDecks.AuroraKickoff();
        var deck = DeckJson.Clone(source.Deck);
        deck.Slides.Clear();
        while (deck.Slides.Count < 200)
        {
            foreach (var s in source.Deck.Slides)
            {
                if (deck.Slides.Count == 200) break;
                deck.Slides.Add(DeckOperations.DuplicateSlide(s));
            }
        }
        var path = dir.File("big" + DeckFile.Extension);
        var watch = Stopwatch.StartNew();
        DeckFile.Save(path, deck, source.Media);
        var save = watch.Elapsed;

        watch.Restart();
        var loaded = DeckFile.Load(path);
        var load = watch.Elapsed;
        Assert.Equal(200, loaded.Deck.Slides.Count);

        watch.Restart();
        var ctx = new RenderContext(loaded.Deck, loaded.Media);
        foreach (var slide in loaded.Deck.Slides) SlideRenderer.Render(slide, ctx, 240);
        var thumbs = watch.Elapsed;

        _output.WriteLine($"200 slides: save {save.TotalMilliseconds:0} ms, load {load.TotalMilliseconds:0} ms, 200 thumbnails {thumbs.TotalMilliseconds:0} ms, file {new FileInfo(path).Length / 1024} KB");
        Assert.True(load < TimeSpan.FromSeconds(10), "load took " + load);
        Assert.True(thumbs < TimeSpan.FromSeconds(60), "thumbnails took " + thumbs);
    });

    [Fact]
    public void IconCatalog_LoadsAndSearches()
    {
        Assert.True(IconCatalog.All.Count > 500);
        Assert.Contains(IconCatalog.Search("rocket"), i => i.Key == "rocket");
        Assert.Equal("rocket", IconCatalog.Search("")[0].Key);
        Assert.NotNull(Sta.Run(() => IconCatalog.Geometry("lightbulb")));
    }
}
