using System.Globalization;
using System.Windows;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

/// <summary>1.1's editing helpers: find and replace, header and footer, grid and guides, automatic save, groups, window placement, crop.</summary>
public class EditingV11Tests
{
    private static Slide SlideWith(params string[] texts)
    {
        var slide = new Slide { Layout = "blank" };
        foreach (var t in texts)
            slide.Elements.Add(new TextElement { Text = new TextBody { Content = RichText.FromPlain(t) }, Width = 600, Height = 200 });
        return slide;
    }

    // ------------------------------------------------------------------ find and replace

    [Fact]
    public void Find_TextTablesAndNotes_WithOptions()
    {
        var deck = new Deck();
        var s1 = SlideWith("The cat sat", "Concatenate CAT");
        s1.Notes = "Remember the cat";
        s1.Elements.Add(new TableElement { Cells = { new List<string> { "cat", "dog" } } });
        deck.Slides.Add(s1);
        deck.Slides.Add(SlideWith("No match here"));

        var all = FindReplace.FindAll(deck, "cat", new FindOptions());
        Assert.Equal(5, all.Count); // cat, Concatenate, CAT, the table cell, the notes
        Assert.Equal(1, all.Count(m => m.Area == FindArea.Notes));
        Assert.Equal(1, all.Count(m => m.Area == FindArea.TableCell));

        Assert.Equal(4, FindReplace.FindAll(deck, "cat", new FindOptions { MatchCase = true }).Count); // not "CAT"
        Assert.Equal(3, FindReplace.FindAll(deck, "cat", new FindOptions { WholeWord = true, MatchCase = true }).Count);
        Assert.Equal(4, FindReplace.FindAll(deck, "cat", new FindOptions { WholeWord = true }).Count); // not "Concatenate"
        Assert.Equal(4, FindReplace.FindAll(deck, "cat", new FindOptions { IncludeNotes = false }).Count);
        Assert.Empty(FindReplace.FindAll(deck, "", new FindOptions()));
    }

    [Fact]
    public void Replace_AcrossRuns_KeepsTheFirstRunsFormat()
    {
        var p = new Paragraph();
        p.Runs.Add(new TextRun { Text = "Hello wo", Bold = true });
        p.Runs.Add(new TextRun { Text = "rld!", Italic = true });
        FindReplace.ReplaceInParagraph(p, 6, 5, "there");
        Assert.Equal("Hello there!", FindReplace.TextOf(p));
        Assert.Equal("Hello there", p.Runs[0].Text);
        Assert.True(p.Runs[0].Bold);
        Assert.Equal("!", p.Runs[1].Text);

        // A match exactly covering a whole run, and one at the very start.
        var q = new Paragraph();
        q.Runs.Add(new TextRun { Text = "A" });
        q.Runs.Add(new TextRun { Text = "BC" });
        q.Runs.Add(new TextRun { Text = "D" });
        FindReplace.ReplaceInParagraph(q, 1, 2, "x");
        Assert.Equal("AxD", FindReplace.TextOf(q));
        FindReplace.ReplaceInParagraph(q, 0, 1, "");
        Assert.Equal("xD", FindReplace.TextOf(q));
    }

    [Fact]
    public void ReplaceAll_EverywhereAndOnlyWhereItStillMatches()
    {
        var deck = new Deck();
        var slide = SlideWith("red, Red and RED", "redder");
        slide.Notes = "red notes";
        deck.Slides.Add(slide);
        var options = new FindOptions { WholeWord = true };
        Assert.Equal(4, FindReplace.ReplaceAll(deck, "red", "blue", options));
        Assert.Equal("blue, blue and blue", FindReplace.TextOf(((TextElement)slide.Elements[0]).Text.Content.Paragraphs[0]));
        Assert.Equal("redder", ((TextElement)slide.Elements[1]).Text.Content.PlainText);
        Assert.Equal("blue notes", slide.Notes);

        // A match whose text changed since the search isn't replaced.
        var m = FindReplace.FindIn(slide, "blue", new FindOptions())[0];
        ((TextElement)slide.Elements[0]).Text.Content = RichText.FromPlain("green");
        Assert.False(FindReplace.Replace(slide, m, "blue", "x", new FindOptions()));
        Assert.Equal("green", ((TextElement)slide.Elements[0]).Text.Content.PlainText);
    }

    [Fact]
    public void ReplaceAll_IsOneUndoStep()
    {
        var deck = new Deck();
        deck.Slides.Add(SlideWith("alpha beta"));
        deck.Slides.Add(SlideWith("beta gamma"));
        var session = new DeckSession(deck, new MediaStore());
        session.EditDeck("Replace all", d => FindReplace.ReplaceAll(d, "beta", "BETA", new FindOptions { MatchCase = true }));
        Assert.Contains("BETA", ((TextElement)deck.Slides[1].Elements[0]).Text.Content.PlainText);
        session.History.Undo();
        Assert.Equal("alpha beta", ((TextElement)deck.Slides[0].Elements[0]).Text.Content.PlainText);
        Assert.Equal("beta gamma", ((TextElement)deck.Slides[1].Elements[0]).Text.Content.PlainText);
    }

    // ------------------------------------------------------------------ header and footer

    [Fact]
    public void Footer_ShowsWhereAsked_AndFormatsDates()
    {
        var f = new HeaderFooter { SlideNumber = true, HideOnTitle = true };
        Assert.True(f.Any);
        Assert.False(f.ShowsOn(new Slide { Layout = Core.Layouts.LayoutIds.Title }));
        Assert.True(f.ShowsOn(new Slide { Layout = "title-content" }));
        Assert.False(new HeaderFooter().Any);

        var day = new DateTime(2026, 10, 14);
        var gb = CultureInfo.GetCultureInfo("en-GB");
        f.Date = true;
        Assert.Equal("14 October 2026", f.DateFor(day, gb));
        Assert.Equal("October 14, 2026", f.DateFor(day, CultureInfo.GetCultureInfo("en-US")));
        f.DateFormat = "iso";
        Assert.Equal("2026-10-14", f.DateFor(day, gb));
        f.DateFormat = "short";
        Assert.Equal("14/10/2026", f.DateFor(day, gb));
        f.FixedDate = true;
        f.DateText = "Spring meeting";
        Assert.Equal("Spring meeting", f.DateFor(day, gb));
    }

    [Fact]
    public void Footer_Edit_UndoesAndSurvivesSaving() => Sta.Run(() =>
    {
        var deck = Core.Editing.DeckOperations.NewDeck(ThemeCatalog.Find("aurora"));
        var session = new DeckSession(deck, new MediaStore());
        session.EditStructure("Header and footer", d => d.Footer = new HeaderFooter { SlideNumber = true, Text = "Footer", HideOnTitle = false });
        Assert.True(deck.Footer.SlideNumber);
        session.History.Undo();
        Assert.False(deck.Footer.SlideNumber);
        session.History.Redo();
        Assert.Equal("Footer", deck.Footer.Text);

        using var dir = new TempDir();
        var path = dir.File("footer.stagecraft");
        Core.IO.DeckFile.Save(path, deck, session.Media);
        var loaded = Core.IO.DeckFile.Load(path);
        Assert.True(loaded.Deck.Footer.SlideNumber);
        Assert.Equal("Footer", loaded.Deck.Footer.Text);

        // The slide number is drawn (the rendering differs from the same slide without a footer).
        var ctx = new RenderContext(deck, session.Media);
        var slide = deck.Slides.First(s => deck.Footer.ShowsOn(s));
        var with = SlideRenderer.Render(slide, ctx, 480);
        deck.Footer = new HeaderFooter();
        var without = SlideRenderer.Render(slide, new RenderContext(deck, session.Media), 480);
        Assert.NotEqual(Pixels(with), Pixels(without));
    });

    private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource b)
    {
        var stride = b.PixelWidth * 4;
        var data = new byte[stride * b.PixelHeight];
        b.CopyPixels(data, stride, 0);
        return data;
    }

    // ------------------------------------------------------------------ grid and guides

    [Fact]
    public void Snap_ToGridWhenNothingIsNear_AndToUserGuides()
    {
        var targets = new SnapTargets(Array.Empty<Rect>(), new Size(1920, 1080), 0) { Smart = false, Grid = 40 };
        var r = Snapper.SnapMove(new Rect(317, 205, 100, 100), targets, threshold: 6);
        Assert.Equal(320, 317 + r.Dx, 3);
        Assert.Equal(200, 205 + r.Dy, 3);

        // A user guide close by wins over the grid.
        var guided = new SnapTargets(Array.Empty<Rect>(), new Size(1920, 1080), 0)
        {
            Smart = false,
            Grid = 40,
            UserGuides = new[] { new GuideLine { Vertical = true, Position = 333 } },
        };
        var g = Snapper.SnapMove(new Rect(330, 205, 100, 100), guided, threshold: 6);
        Assert.Equal(333, 330 + g.Dx, 3);
        Assert.Contains(g.Guides, x => x.Kind == GuideKind.User && x.Vertical);

        // Smart guides off: other elements are ignored.
        var others = new SnapTargets(new[] { new Rect(500, 500, 100, 100) }, new Size(1920, 1080), 0) { Smart = false };
        Assert.False(Snapper.SnapMove(new Rect(503, 300, 100, 100), others, threshold: 6).SnappedX);

        // Resizing to the grid.
        var resize = Snapper.SnapResize(new Rect(100, 100, 213, 100), 1, 0, targets, threshold: 6);
        Assert.Equal(320, 100 + 213 + resize.Dx, 3);
        Assert.Equal(5, Snapper.GridDelta(35, 40), 6);
        Assert.Equal(0, Snapper.GridDelta(35, 0), 6);
    }

    [Fact]
    public void Guides_AreUndoableAndSaved() => Sta.Run(() =>
    {
        var deck = Core.Editing.DeckOperations.NewDeck(ThemeCatalog.Find("aurora"));
        var session = new DeckSession(deck, new MediaStore());
        var revision = deck.Slides[0].Revision;
        session.EditStructure("Add guide", d => d.Guides.Add(new GuideLine { Vertical = true, Position = 960 }), redrawSlides: false);
        Assert.Equal(revision, deck.Slides[0].Revision); // guides don't redraw thumbnails
        session.History.Undo();
        Assert.Empty(deck.Guides);
        session.History.Redo();
        Assert.Single(deck.Guides);
        var json = Core.IO.DeckJson.Serialize(deck);
        Assert.Contains("\"guides\"", json);
        Assert.Equal(960, Core.IO.DeckJson.Deserialize(json)!.Guides[0].Position);
    });

    // ------------------------------------------------------------------ automatic save

    [Fact]
    public void AutoSave_MarksTheSnapshotSaved_NotLaterEdits()
    {
        var deck = new Deck();
        deck.Slides.Add(SlideWith("one"));
        var session = new DeckSession(deck, new MediaStore(), @"C:\x\deck.stagecraft");
        var slide = deck.Slides[0];
        session.EditSlide(slide, "Edit", s => ((TextElement)s.Elements[0]).Text.Content = RichText.FromPlain("two"));
        Assert.True(session.IsDirty);

        // The automatic save takes its snapshot here …
        session.History.Seal();
        var snapshot = session.History.Top;
        // … the user keeps typing while it writes …
        session.EditSlide(slide, "Edit", s => ((TextElement)s.Elements[0]).Text.Content = RichText.FromPlain("three"), "typing");
        // … and when it finishes, only the snapshot counts as saved.
        session.MarkSaved(@"C:\x\deck.stagecraft", snapshot);
        Assert.True(session.IsDirty);
        session.History.Undo();
        Assert.False(session.IsDirty); // back at the saved state
        session.History.Redo();
        Assert.True(session.IsDirty);
        session.MarkSaved(@"C:\x\deck.stagecraft", session.History.Top);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void AutoSave_SealPreventsMergingIntoTheSnapshot()
    {
        var deck = new Deck();
        deck.Slides.Add(SlideWith("a"));
        var session = new DeckSession(deck, new MediaStore(), @"C:\x\deck.stagecraft");
        var slide = deck.Slides[0];
        session.EditSlide(slide, "Opacity", s => s.Elements[0].Opacity = 0.5, "opacity");
        session.History.Seal();
        var snapshot = session.History.Top;
        session.EditSlide(slide, "Opacity", s => s.Elements[0].Opacity = 0.4, "opacity"); // would merge without the seal
        Assert.NotSame(snapshot, session.History.Top);
        session.MarkSaved(@"C:\x\deck.stagecraft", snapshot);
        Assert.True(session.IsDirty);
    }

    // ------------------------------------------------------------------ groups keep their animations

    [Fact]
    public void Ungroup_MovesTheGroupsEffectsToItsMembers()
    {
        var slide = SlideWith("a", "b", "c");
        var members = slide.Elements.Take(2).ToList();
        var group = DeckOperations.Group(members);
        var effect = EffectCatalog.Create(EffectClass.Entrance, EffectKind.Fly, group);
        effect.Delay = 0.3;
        slide.Animations.Add(effect);
        var other = EffectCatalog.Create(EffectClass.Entrance, EffectKind.Fade, slide.Elements[2].Id);
        other.Trigger = group;
        slide.Animations.Add(other);

        AnimationOps.Ungroup(slide, members);
        Assert.All(members, m => Assert.Null(m.GroupId));
        var fly = slide.Animations.Where(a => a.Effect == EffectKind.Fly).ToList();
        Assert.Equal(2, fly.Count);
        Assert.Equal(members.Select(m => m.Id), fly.Select(a => a.Target));
        Assert.Equal(EffectStart.OnClick, fly[0].Start);
        Assert.Equal(EffectStart.WithPrevious, fly[1].Start);
        Assert.All(fly, a => Assert.Equal(0.3, a.Delay));
        Assert.Equal(members[0].Id, slide.Animations.Single(a => a.Effect == EffectKind.Fade).Trigger);
        Assert.Equal(0, AnimationRules.Prune(slide));
    }

    [Fact]
    public void Group_MergingGroups_KeepsTheirEffects()
    {
        var slide = SlideWith("a", "b", "c");
        var first = DeckOperations.Group(slide.Elements.Take(2));
        slide.Animations.Add(EffectCatalog.Create(EffectClass.Entrance, EffectKind.Fade, first));
        var merged = AnimationOps.Group(slide, slide.Elements.ToList());
        Assert.All(slide.Elements, e => Assert.Equal(merged, e.GroupId));
        Assert.Equal(2, slide.Animations.Count); // one per former member, not lost
        Assert.Equal(0, AnimationRules.Prune(slide));
    }

    // ------------------------------------------------------------------ window placement

    [Fact]
    public void ScreenFit_FirstStart_FitsSmallScreens()
    {
        var big = ScreenFit.FirstStart(new Size(1440, 900), new Size(960, 600), new Rect(0, 0, 2560, 1400));
        Assert.False(big.Maximize);
        Assert.Equal(new Rect(560, 250, 1440, 900), big.Bounds);

        // 1366 × 728 work area: shrunk, centred, title bar on screen.
        var small = ScreenFit.FirstStart(new Size(1440, 900), new Size(960, 600), new Rect(0, 0, 1366, 728));
        Assert.False(small.Maximize);
        Assert.Equal(1318, small.Bounds.Width);
        Assert.Equal(680, small.Bounds.Height);
        Assert.True(small.Bounds.Top >= 0);

        // Too small even for the minimum: maximized.
        Assert.True(ScreenFit.FirstStart(new Size(1440, 900), new Size(960, 600), new Rect(0, 0, 1000, 560)).Maximize);

        // A second display to the left (negative coordinates).
        var left = ScreenFit.FirstStart(new Size(1440, 900), new Size(960, 600), new Rect(-1920, 0, 1920, 1040));
        Assert.Equal(-1920 + 240, left.Bounds.Left);
    }

    [Fact]
    public void ScreenFit_KeepInside_MovesAndShrinks()
    {
        var work = new Rect(0, 0, 1920, 1040);
        Assert.Null(ScreenFit.KeepInside(new Rect(100, 100, 800, 600), work, new Size(400, 300), true, out var m0));
        Assert.False(m0);
        // Title bar above the top of the screen: moved down.
        Assert.Equal(new Rect(100, 0, 800, 600), ScreenFit.KeepInside(new Rect(100, -40, 800, 600), work, new Size(400, 300), true, out _));
        // Bigger than the screen: shrunk to it.
        Assert.Equal(new Rect(0, 0, 1920, 1040), ScreenFit.KeepInside(new Rect(-50, -50, 2400, 1300), work, new Size(400, 300), true, out _));
        // Minimum doesn't fit and it can be resized: maximize.
        Assert.Null(ScreenFit.KeepInside(new Rect(0, 0, 800, 600), new Rect(0, 0, 700, 500), new Size(800, 600), true, out var m1));
        Assert.True(m1);
        // A dialog that can't be resized is just moved in.
        Assert.NotNull(ScreenFit.KeepInside(new Rect(1500, 900, 800, 600), work, new Size(800, 600), false, out var m2));
        Assert.False(m2);
    }

    // ------------------------------------------------------------------ crop

    [Fact]
    public void CropFromFit_KeepsWhatTheFitShowed()
    {
        // A 400 × 200 picture in a 200 × 200 box, filled: the middle half shows.
        var img = new ImageElement { X = 0, Y = 0, Width = 200, Height = 200, Fit = ImageFit.Cover };
        var crop = ElementRenderer.CropFromFit(img, 400, 200);
        Assert.Equal(0.25, crop.Left, 3);
        Assert.Equal(0.25, crop.Right, 3);
        Assert.Equal(0, crop.Top, 3);
        Assert.Equal(0, crop.Bottom, 3);
        // Stretch shows everything.
        img.Fit = ImageFit.Stretch;
        var all = ElementRenderer.CropFromFit(img, 400, 200);
        Assert.Equal(0, all.Left + all.Right + all.Top + all.Bottom, 3);
    }
}
