using System.Windows;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Tests;

public class LinkTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("  https://example.com/path?q=1  ", "https://example.com/path?q=1")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("www.example.org/a b", null)]
    [InlineData("localhost:8080/x", "https://localhost:8080/x")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("JavaScript:alert(1)", null)]
    [InlineData("vbscript:msgbox", null)]
    [InlineData("data:text/html,<b>x</b>", null)]
    [InlineData("file:///C:/Windows/notepad.exe", null)]
    [InlineData("ftp://example.com", null)]
    [InlineData("mailto:a@b.com", null)]
    [InlineData("", null)]
    [InlineData("https://", null)]
    public void NormalizeWeb_AllowsOnlyHttp(string input, string? expected) => Assert.Equal(expected, LinkSafety.NormalizeWeb(input));

    [Fact]
    public void Email_ValidatesAndEncodesTheSubject()
    {
        Assert.True(LinkSafety.IsEmail("someone@example.com"));
        Assert.True(LinkSafety.IsEmail("mailto:someone@example.com"));
        Assert.False(LinkSafety.IsEmail("someone@example"));
        Assert.False(LinkSafety.IsEmail("some one@example.com"));
        Assert.False(LinkSafety.IsEmail("a@b@c.com"));
        Assert.False(LinkSafety.IsEmail("someone@example.com>"));
        Assert.Equal("mailto:someone@example.com?subject=Q3%20plan%20%26%20budget", LinkSafety.MailtoUri("someone@example.com", "Q3 plan & budget"));
        Assert.Equal("mailto:someone@example.com", LinkSafety.MailtoUri("someone@example.com", null));
        Assert.Null(LinkSafety.MailtoUri("not an address", "x"));
    }

    [Fact]
    public void Files_NeverStartPrograms()
    {
        foreach (var bad in new[] { "setup.exe", @"C:\x\run.BAT", "script.ps1", "a.vbs", "b.js", "c.lnk", "d.msi", "e.scr", "f.hta", "noextension" })
            Assert.True(LinkSafety.IsBlockedFile(bad), bad);
        foreach (var ok in new[] { "report.pdf", "data.xlsx", "photo.jpg", "notes.txt", "deck.pptx" })
            Assert.False(LinkSafety.IsBlockedFile(ok), ok);
        Assert.Null(LinkSafety.SafeUri(Hyperlink.ToFile("report.pdf"))); // files are never URLs
        Assert.Null(LinkSafety.ResolveFile("evil.exe", @"C:\Decks"));
        Assert.Equal(@"C:\Decks\files\report.pdf", LinkSafety.ResolveFile(@"files\report.pdf", @"C:\Decks"));
        Assert.Equal(@"C:\Other\report.pdf", LinkSafety.ResolveFile(@"C:\Other\report.pdf", @"C:\Decks"));
        Assert.Equal(@"C:\Other\report.pdf", LinkSafety.ResolveFile("file:///C:/Other/report.pdf", null));
        Assert.Null(LinkSafety.ResolveFile(@"files\report.pdf", null));
    }

    [Fact]
    public void SafeUri_And_IsValid()
    {
        Assert.Equal("https://stagecraft.example/", LinkSafety.SafeUri(Hyperlink.Web("stagecraft.example")));
        Assert.Null(LinkSafety.SafeUri(Hyperlink.Web("javascript:void(0)")));
        Assert.Equal("mailto:x@y.org?subject=Hi", LinkSafety.SafeUri(Hyperlink.Email("x@y.org", "Hi")));
        var deck = DeckOperations.NewDeck();
        Assert.True(LinkSafety.IsValid(Hyperlink.ToSlide(SlideJump.Next), deck));
        Assert.False(LinkSafety.IsValid(Hyperlink.ToSlide(SlideJump.Specific, "nope"), deck));
        Assert.True(LinkSafety.IsValid(Hyperlink.ToSlide(SlideJump.Specific, deck.Slides[0].Id), deck));
        Assert.False(LinkSafety.IsValid(Hyperlink.ToFile("x.exe")));
    }

    [Fact]
    public void JumpTargets()
    {
        var slides = Enumerable.Range(0, 5).Select(_ => new Slide()).ToList();
        int Jump(SlideJump j, int current, int last = -1, string? id = null) => LinkSafety.JumpTarget(Hyperlink.ToSlide(j, id), slides, current, last);
        Assert.Equal(3, Jump(SlideJump.Next, 2));
        Assert.Equal(-1, Jump(SlideJump.Next, 4));
        Assert.Equal(1, Jump(SlideJump.Previous, 2));
        Assert.Equal(-1, Jump(SlideJump.Previous, 0));
        Assert.Equal(0, Jump(SlideJump.First, 3));
        Assert.Equal(4, Jump(SlideJump.Last, 0));
        Assert.Equal(1, Jump(SlideJump.LastViewed, 3, 1));
        Assert.Equal(-1, Jump(SlideJump.LastViewed, 3));
        Assert.Equal(2, Jump(SlideJump.Specific, 0, -1, slides[2].Id));
        Assert.Equal(-1, LinkSafety.JumpTarget(Hyperlink.Web("example.com"), slides, 0));
    }


    private static double Luma(System.Windows.Media.Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    [Fact]
    public void LinkedText_IsUnderlinedInTheLinkColour_AndHasAreas() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck();
        var slide = deck.Slides[0];
        var body = new TextBody { Content = new RichText() };
        var p = new Paragraph();
        p.Runs.Add(new TextRun { Text = "Read the " });
        p.Runs.Add(new TextRun { Text = "full report", Link = Hyperlink.Web("example.com/report", "Opens the report") });
        p.Runs.Add(new TextRun { Text = " online." });
        body.Content.Paragraphs.Add(p);
        var text = new TextElement { X = 200, Y = 300, Width = 1200, Height = 200, Text = body };
        var shape = new ShapeElement { X = 100, Y = 700, Width = 300, Height = 150, Rotation = 30, Link = Hyperlink.ToSlide(SlideJump.Next) };
        slide.Elements.Add(text);
        slide.Elements.Add(shape);
        var style = SlideStyle.For(deck, slide);
        var f = TextLayout.Resolve(p.Runs[1], body, style, 1);
        Assert.True(f.Underline);
        Assert.Equal(TextLayout.LinkColorOn(style), f.Color);
        // Links stay readable: lighter than a dark slide, darker than a light one, in every theme and variant.
        foreach (var theme in Core.Themes.ThemeCatalog.All)
        foreach (var variant in theme.Variants)
        {
            var d = DeckOperations.NewDeck(theme, variant.Id);
            foreach (var s in d.Slides)
            {
                var st = SlideStyle.For(d, s);
                var luma = Luma(TextLayout.LinkColorOn(st));
                if (st.IsDark) Assert.True(luma >= 0.3, $"{theme.Id}/{variant.Id}: {luma}");
                else Assert.True(luma <= 0.75, $"{theme.Id}/{variant.Id}: {luma}");
            }
        }
        Assert.False(TextLayout.Resolve(p.Runs[0], body, style, 1).Underline);

        var regions = LinkRegions.For(slide, new RenderContext(deck, new MediaStore()));
        var textRegion = Assert.Single(regions, r => r.IsText);
        Assert.Equal("Opens the report", textRegion.Link.ScreenTip);
        Assert.InRange(textRegion.Bounds.Left, 200, 1000);
        Assert.True(textRegion.Bounds.Width > 50 && textRegion.Bounds.Width < 1000);
        var shapeRegion = Assert.Single(regions, r => !r.IsText);
        Assert.True(shapeRegion.Contains(shape.Center));
        Assert.Equal(shapeRegion, LinkRegions.At(regions, shape.Center));
        Assert.Null(LinkRegions.At(regions, new Point(1800, 50)));
    });

    [Fact]
    public void Links_SurviveSaveLoad_AndRunsWithDifferentLinksDontMerge() => Sta.Run(() =>
    {
        var a = new TextRun { Text = "a", Link = Hyperlink.Web("example.com") };
        var b = new TextRun { Text = "b", Link = Hyperlink.Web("example.org") };
        Assert.False(a.SameFormat(b));
        Assert.True(a.SameFormat(new TextRun { Text = "c", Link = Hyperlink.Web("example.com") }));

        using var dir = new TempDir();
        var deck = DeckOperations.NewDeck();
        deck.Slides[0].Elements.Add(new ShapeElement { Shape = ShapeKind.ActionButton, Action = ActionButtonKind.Next, Link = Hyperlink.ToSlide(SlideJump.Next), Width = 120, Height = 120 });
        var t = deck.Slides[0].FindPlaceholder<TextElement>("title")!;
        t.Text.Content = RichText.FromPlain("Say hello");
        t.Text.Content.Paragraphs[0].Runs[0].Link = Hyperlink.Email("hello@example.com", "Hi");
        var path = dir.File("links" + DeckFile.Extension);
        DeckFile.Save(path, deck, new MediaStore());
        var loaded = DeckFile.Load(path).Deck;
        Assert.Equal(Decks.Json(deck), Decks.Json(loaded));
        var button = loaded.Slides[0].Elements.OfType<ShapeElement>().Single(s => s.Shape == ShapeKind.ActionButton);
        Assert.Equal(SlideJump.Next, button.Link!.Jump);
    });

    [Fact]
    public void FileLinks_AreRebasedWhenSavedElsewhere() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var docs = Directory.CreateDirectory(Path.Combine(dir.Path, "Docs")).FullName;
        var target = Path.Combine(docs, "report.pdf");
        File.WriteAllText(target, "%PDF-1.4");
        var deck = DeckOperations.NewDeck();
        deck.Slides[0].Elements.Add(new ShapeElement { Link = Hyperlink.ToFile(target), Width = 100, Height = 100 });
        var media = new MediaStore();
        var first = Path.Combine(Directory.CreateDirectory(Path.Combine(dir.Path, "A")).FullName, "d" + DeckFile.Extension);
        DeckFile.Save(first, deck, media);
        var link = deck.Slides[0].Elements.OfType<ShapeElement>().Last().Link!;
        Assert.Equal(Path.Combine("..", "Docs", "report.pdf"), link.Address);
        Assert.Equal(target, LinkSafety.ResolveFile(link.Address, media.BaseFolder));
        var second = Path.Combine(Directory.CreateDirectory(Path.Combine(dir.Path, "B", "C")).FullName, "d" + DeckFile.Extension);
        DeckFile.Save(second, deck, media);
        Assert.Equal(Path.Combine("..", "..", "Docs", "report.pdf"), link.Address);
        Assert.Equal(target, LinkSafety.ResolveFile(link.Address, media.BaseFolder));
    });
}
