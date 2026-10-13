using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Export;
using Stagecraft.Core.Import;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;
using Stagecraft.Core.Themes;
using P = DocumentFormat.OpenXml.Presentation;

namespace Stagecraft.Tests;

public class PptxTests
{
    private readonly ITestOutputHelper _output;

    public PptxTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static MemoryStream Export(Deck deck, MediaStore media, bool includeHidden = false)
    {
        var ms = new MemoryStream();
        PptxExporter.Export(deck, media, ms, new PptxExportOptions { IncludeHidden = includeHidden });
        ms.Position = 0;
        return ms;
    }

    private void AssertValid(Stream stream, string name)
    {
        stream.Position = 0;
        using var doc = PresentationDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2016);
        var errors = validator.Validate(doc).ToList();
        foreach (var e in errors.Take(20)) _output.WriteLine($"{name}: {e.Part?.Uri} {e.Path?.XPath}: {e.Description}");
        Assert.Empty(errors);
        stream.Position = 0;
    }

    [Fact]
    public void Export_DemoDecks_AreValidOpenXml() => Sta.Run(() =>
    {
        foreach (var sample in DemoDecks.All)
        {
            var doc = sample.Create();
            using var ms = Export(doc.Deck, doc.Media);
            AssertValid(ms, sample.Id);
            using var pptx = PresentationDocument.Open(ms, false);
            var ids = pptx.PresentationPart!.Presentation!.SlideIdList!.Elements<P.SlideId>().Count();
            Assert.Equal(doc.Deck.Slides.Count(s => !s.Hidden), ids);
            var size = pptx.PresentationPart!.Presentation!.SlideSize!;
            Assert.Equal(1920 * 6350, size.Cx!.Value);
            Assert.Equal(1080 * 6350, size.Cy!.Value);
            _output.WriteLine($"{sample.Id}: {ms.Length / 1024} KB");
        }
    });

    [Fact]
    public void Export_EveryLayoutAndTheme_IsValid() => Sta.Run(() =>
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var deck = Decks.AllLayouts(theme.Id);
            var slide = deck.Slides[deck.Slides.Count - 1];
            slide.Elements.Add(new TableElement { X = 100, Y = 100, Width = 800, Height = 300, Cells = { new List<string> { "A", "B" }, new List<string> { "1", "2" } } });
            slide.Elements.Add(new ChartElement { X = 1000, Y = 100, Width = 600, Height = 400, Categories = { "Q1", "Q2" }, Series = { new ChartSeries { Name = "S", Values = { 3, 5 } } } });
            slide.Elements.Add(new IconElement { Icon = "rocket", X = 100, Y = 600, Width = 120, Height = 120, Container = IconContainer.Circle });
            slide.Elements.Add(new ShapeElement { Shape = ShapeKind.Arrow, X = 300, Y = 700, Width = 400, Height = 0, EndHead = ArrowHead.Arrow });
            slide.Elements.Add(new ShapeElement { Shape = ShapeKind.Callout, X = 800, Y = 600, Width = 400, Height = 200, Fill = Fill.Solid("accent2"), Text = new TextBody { Content = RichText.FromPlain("Hi") } });
            slide.Elements.Add(new ShapeElement { Shape = ShapeKind.Triangle, X = 1300, Y = 600, Width = 200, Height = 200, Fill = Fill.Linear("accent1", "accent3", 45), Rotation = 30 });
            slide.Notes = "Notes with <markup> & “quotes”";
            using var ms = Export(deck, new MediaStore(), includeHidden: true);
            AssertValid(ms, theme.Id);
        }
    });

    [Fact]
    public void Import_OwnExport_KeepsSlidesTextNotesAndPictures() => Sta.Run(() =>
    {
        var doc = DemoDecks.AuroraKickoff();
        using var ms = Export(doc.Deck, doc.Media);
        var imported = PptxImporter.Import(ms);
        foreach (var w in imported.Warnings) _output.WriteLine("warning: " + w);

        var source = doc.Deck.Slides.Where(s => !s.Hidden).ToList();
        Assert.Equal(source.Count, imported.Deck.Slides.Count);
        Assert.Equal(1920, imported.Deck.Width, 0);
        Assert.Equal(1080, imported.Deck.Height, 0);

        for (var i = 0; i < source.Count; i++)
        {
            var original = source[i];
            var copy = imported.Deck.Slides[i];
            Assert.Equal(original.Notes.Trim(), copy.Notes.Trim());
            var title = original.TitleText;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var allText = string.Join("\n", copy.Elements.Select(TextOf));
                Assert.Contains(title.Split('\n')[0].Trim(), allText);
            }
            Assert.Equal(original.Transition.Kind == TransitionKind.None, copy.Transition.Kind == TransitionKind.None);
        }
        // Pictures came across.
        var pictures = imported.Deck.Slides.SelectMany(s => s.Elements).OfType<ImageElement>().ToList();
        Assert.NotEmpty(pictures);
        Assert.All(pictures, p => Assert.NotNull(imported.Media.GetImage(p.Media)));

        // And it renders.
        var ctx = new RenderContext(imported.Deck, imported.Media);
        foreach (var s in imported.Deck.Slides) SlideRenderer.Render(s, ctx, 320);
    });

    [Fact]
    public void Import_PositionsAndTables_RoundTrip() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("corporate"));
        var slide = deck.Slides[0];
        slide.Elements.Clear();
        slide.Elements.Add(new ShapeElement { Shape = ShapeKind.Ellipse, X = 100, Y = 200, Width = 300, Height = 150, Fill = Fill.Solid("#FF8800"), Name = "Ball" });
        slide.Elements.Add(new ShapeElement { Shape = ShapeKind.RoundedRectangle, X = 600, Y = 200, Width = 400, Height = 200, Fill = Fill.Solid("#2255AA"), CornerRadius = 40, Rotation = 15 });
        slide.Elements.Add(new TextElement { X = 100, Y = 600, Width = 900, Height = 160, Text = new TextBody { Content = RichText.FromPlain("Plain words here"), AutoFit = false } });
        slide.Elements.Add(new TableElement { X = 1100, Y = 500, Width = 700, Height = 300, Cells = { new List<string> { "Name", "Score" }, new List<string> { "Ann", "12" }, new List<string> { "Bo", "9" } } });
        using var ms = Export(deck, new MediaStore());
        var imported = PptxImporter.Import(ms).Deck;
        var s = imported.Slides[0];

        var ball = Assert.Single(s.Elements.OfType<ShapeElement>(), e => e.Shape == ShapeKind.Ellipse);
        Assert.Equal(100, ball.X, 0);
        Assert.Equal(200, ball.Y, 0);
        Assert.Equal(300, ball.Width, 0);
        Assert.Equal(150, ball.Height, 0);
        Assert.Equal("#FF8800", ball.Fill.Color, ignoreCase: true);

        var rounded = Assert.Single(s.Elements.OfType<ShapeElement>(), e => e.Shape == ShapeKind.RoundedRectangle);
        Assert.Equal(15, rounded.Rotation, 1);
        Assert.InRange(rounded.CornerRadius, 35, 45);

        var text = Assert.Single(s.Elements.OfType<TextElement>());
        Assert.Equal("Plain words here", text.Text.Content.PlainText);

        var table = Assert.Single(s.Elements.OfType<TableElement>());
        Assert.Equal(3, table.Cells.Count);
        Assert.Equal("Score", table.Cells[0][1]);
        Assert.Equal("9", table.Cells[2][1]);
    });

    [Fact]
    public void Import_FourByThree_ScalesToHeight1080() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck();
        DeckOperations.SetSlideSize(deck, 1440, 1080);
        using var ms = Export(deck, new MediaStore());
        var imported = PptxImporter.Import(ms).Deck;
        Assert.Equal(1440, imported.Width, 0);
        Assert.Equal(1080, imported.Height, 0);
    });

    [Fact]
    public void Import_NotAPresentation_FailsCleanly()
    {
        using var dir = new TempDir();
        var path = dir.File("fake.pptx");
        File.WriteAllText(path, "this is not a zip");
        var ex = Assert.Throws<DeckFileException>(() => PptxImporter.Import(path));
        Assert.Equal(DeckFileProblem.NotADeck, ex.Problem);
    }

    private static string TextOf(Element e) => e switch
    {
        TextElement t => t.Text.Content.PlainText,
        ShapeElement { Text: { } b } => b.Content.PlainText,
        _ => "",
    };
}
