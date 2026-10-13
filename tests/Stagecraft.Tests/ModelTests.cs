using System.Windows;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

public class ModelTests
{
    private static DeckSession NewSession()
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Default);
        return new DeckSession(deck, new MediaStore());
    }

    [Fact]
    public void NewDeck_HasTitleSlideAndTheme()
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("editorial"));
        Assert.Single(deck.Slides);
        Assert.Equal("editorial", deck.Theme.Id);
        Assert.Equal(1920, deck.Width);
        Assert.Equal(1080, deck.Height);
        Assert.NotNull(deck.Slides[0].FindPlaceholder<TextElement>("title"));
    }

    [Fact]
    public void EditSlide_UndoRedo_RestoresExactState()
    {
        var session = NewSession();
        var slide = session.Deck.Slides[0];
        var title = slide.FindPlaceholder<TextElement>("title")!;
        var original = DeckJson.ToJson(slide);

        session.EditSlide(slide, "Move", s => s.Find(title.Id)!.X += 120);
        session.EditSlide(slide, "Text", s => ((TextElement)s.Find(title.Id)!).Text.Content = RichText.FromPlain("Hello"));
        var edited = DeckJson.ToJson(slide);
        Assert.True(session.IsDirty);
        Assert.Equal(2, session.History.Count);

        session.History.Undo();
        session.History.Undo();
        Assert.Equal(original, DeckJson.ToJson(session.Deck.Slides[0]));
        Assert.False(session.IsDirty);

        session.History.Redo();
        session.History.Redo();
        Assert.Equal(edited, DeckJson.ToJson(session.Deck.Slides[0]));
        Assert.True(session.IsDirty);
        Assert.False(session.History.CanRedo);
    }

    [Fact]
    public void EditsWithSameMergeKey_BecomeOneStep()
    {
        var session = NewSession();
        var slide = session.Deck.Slides[0];
        var id = slide.Elements[0].Id;
        for (var i = 0; i < 5; i++) session.EditSlide(slide, "Nudge", s => s.Find(id)!.X += 1, mergeKey: "nudge:" + id);
        Assert.Equal(1, session.History.Count);
        session.History.Seal();
        session.EditSlide(slide, "Nudge", s => s.Find(id)!.X += 1, mergeKey: "nudge:" + id);
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public void Structure_AddDeleteReorder_CanBeUndone()
    {
        var session = NewSession();
        var first = session.Deck.Slides[0].Id;
        session.EditStructure("Add", d => d.Slides.Add(LayoutEngine.CreateSlide(d, LayoutIds.TitleContent)));
        session.EditStructure("Add", d => d.Slides.Add(LayoutEngine.CreateSlide(d, LayoutIds.Quote)));
        Assert.Equal(3, session.Deck.Slides.Count);
        session.EditStructure("Reorder", d =>
        {
            var s = d.Slides[2];
            d.Slides.RemoveAt(2);
            d.Slides.Insert(0, s);
        });
        Assert.NotEqual(first, session.Deck.Slides[0].Id);
        session.EditStructure("Delete", d => d.Slides.RemoveAt(1));
        Assert.Equal(2, session.Deck.Slides.Count);

        session.History.Undo();
        session.History.Undo();
        Assert.Equal(3, session.Deck.Slides.Count);
        Assert.Equal(first, session.Deck.Slides[0].Id);
        session.History.Undo();
        session.History.Undo();
        Assert.Single(session.Deck.Slides);
        session.History.Redo();
        Assert.Equal(2, session.Deck.Slides.Count);
    }

    [Fact]
    public void ThemeChange_CanBeUndone_AndKeepsText()
    {
        var session = NewSession();
        var slide = session.Deck.Slides[0];
        var title = slide.FindPlaceholder<TextElement>("title")!;
        session.EditSlide(slide, "Text", s => ((TextElement)s.Find(title.Id)!).Text.Content = RichText.FromPlain("Kept"));
        var before = DeckJson.ToJson(session.Deck);

        session.EditDeck("Theme", d => DeckOperations.ApplyTheme(d, ThemeCatalog.Find("midnight")!, null));
        Assert.Equal("midnight", session.Deck.Theme.Id);
        Assert.Equal("Kept", session.Deck.Slides[0].FindPlaceholder<TextElement>("title")!.Text.Content.PlainText);

        session.History.Undo();
        Assert.Equal(before, DeckJson.ToJson(session.Deck));
    }

    [Fact]
    public void MarkSaved_ClearsDirty_UntilNextEdit()
    {
        var session = NewSession();
        var slide = session.Deck.Slides[0];
        session.EditSlide(slide, "Move", s => s.Elements[0].Y += 10);
        session.MarkSaved(@"C:\nowhere\deck.stagecraft");
        Assert.False(session.IsDirty);
        session.History.Undo();
        Assert.True(session.IsDirty);
        session.History.Redo();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void DuplicateSlide_KeepsMorphIdentity_NewIds()
    {
        var deck = DeckOperations.NewDeck();
        var slide = deck.Slides[0];
        var copy = DeckOperations.DuplicateSlide(slide);
        Assert.NotEqual(slide.Id, copy.Id);
        Assert.Equal(slide.Elements.Count, copy.Elements.Count);
        for (var i = 0; i < slide.Elements.Count; i++)
        {
            Assert.NotEqual(slide.Elements[i].Id, copy.Elements[i].Id);
            Assert.Equal(Core.Animation.MorphPlanner.Identity(slide.Elements[i]), Core.Animation.MorphPlanner.Identity(copy.Elements[i]));
        }
    }

    [Fact]
    public void AlignAndDistribute()
    {
        var a = new ShapeElement { X = 100, Y = 100, Width = 100, Height = 50 };
        var b = new ShapeElement { X = 400, Y = 180, Width = 60, Height = 50 };
        var c = new ShapeElement { X = 900, Y = 140, Width = 100, Height = 50 };
        var all = new List<Element> { a, b, c };
        DeckOperations.Align(all, AlignMode.Top, new Size(1920, 1080));
        Assert.All(all, e => Assert.Equal(100, e.Y));
        DeckOperations.Distribute(all, horizontal: true);
        var gap1 = b.X - (a.X + a.Width);
        var gap2 = c.X - (b.X + b.Width);
        Assert.Equal(gap1, gap2, 3);
    }

    [Fact]
    public void GroupAndUngroup()
    {
        var a = new ShapeElement { X = 0, Y = 0, Width = 10, Height = 10 };
        var b = new ShapeElement { X = 20, Y = 0, Width = 10, Height = 10 };
        var group = DeckOperations.Group(new Element[] { a, b });
        Assert.Equal(group, a.GroupId);
        Assert.Equal(group, b.GroupId);
        Assert.Single(DeckOperations.Units(new Element[] { a, b }));
        DeckOperations.Ungroup(new Element[] { a, b });
        Assert.Null(a.GroupId);
        Assert.Equal(2, DeckOperations.Units(new Element[] { a, b }).Count);
    }

    [Fact]
    public void SlideSize_ScalesContent()
    {
        var deck = Decks.AllLayouts();
        DeckOperations.SetSlideSize(deck, 1440, 1080);
        Assert.Equal(1440, deck.Width);
        foreach (var slide in deck.Slides)
        foreach (var e in slide.Elements)
        {
            Assert.True(e.X >= -1 && e.X + e.Width <= 1441, $"{slide.Layout}/{e.Placeholder} x {e.X}..{e.X + e.Width}");
        }
    }

    [Fact]
    public void EveryLayout_InEveryTheme_StaysOnTheSlide()
    {
        foreach (var theme in ThemeCatalog.All)
        foreach (var variant in theme.Variants)
        {
            var deck = Decks.AllLayouts(theme.Id, variant.Id);
            foreach (var slide in deck.Slides)
            {
                if (slide.Layout != LayoutIds.Blank) Assert.NotEmpty(slide.Elements);
                foreach (var e in slide.Elements)
                {
                    Assert.True(e.Width >= 0 && e.Height >= 0);
                    Assert.True(e.X >= -0.5 && e.Y >= -0.5 && e.X + e.Width <= deck.Width + 0.5 && e.Y + e.Height <= deck.Height + 0.5,
                        $"{theme.Id}/{variant.Id}/{slide.Layout}/{e.Placeholder}: {e.Bounds}");
                }
            }
        }
    }

    [Fact]
    public void ChangingLayout_KeepsTitleAndBody()
    {
        var deck = DeckOperations.NewDeck();
        var slide = LayoutEngine.CreateSlide(deck, LayoutIds.TitleContent);
        slide.FindPlaceholder<TextElement>("title")!.Text.Content = RichText.FromPlain("Plan");
        slide.FindPlaceholder<TextElement>("body")!.Text.Content = RichText.FromPlain("One\nTwo\nThree", ListKind.Bullet);
        deck.Slides.Add(slide);
        LayoutEngine.Apply(deck, slide, LayoutIds.TwoColumns);
        Assert.Equal("Plan", slide.FindPlaceholder<TextElement>("title")!.Text.Content.PlainText);
        Assert.Contains("Two", string.Join("\n", slide.Elements.OfType<TextElement>().Select(t => t.Text.Content.PlainText)));
        LayoutEngine.Apply(deck, slide, LayoutIds.Agenda);
        Assert.Equal("Plan", slide.FindPlaceholder<TextElement>("title")!.Text.Content.PlainText);
        var text = string.Join("\n", slide.Elements.OfType<TextElement>().Select(t => t.Text.Content.PlainText));
        Assert.Contains("One", text);
        Assert.Contains("Three", text);
    }

    [Fact]
    public void JsonClone_IsDeepAndPolymorphic()
    {
        var deck = Decks.AllLayouts();
        deck.Slides[0].Elements.Add(new ChartElement { Kind = ChartKind.Donut, Categories = { "A", "B" }, Series = { new ChartSeries { Name = "S", Values = { 1, 2 } } } });
        deck.Slides[0].Elements.Add(new IconElement { Icon = "rocket" });
        deck.Slides[0].Elements.Add(new TableElement { Cells = { new List<string> { "a", "b" } } });
        var clone = DeckJson.Clone(deck);
        Assert.Equal(DeckJson.ToJson(deck), DeckJson.ToJson(clone));
        clone.Slides[0].Elements[0].X += 5;
        Assert.NotEqual(deck.Slides[0].Elements[0].X, clone.Slides[0].Elements[0].X);
        Assert.IsType<ChartElement>(clone.Slides[0].Elements[clone.Slides[0].Elements.Count - 3]);
        Assert.IsType<IconElement>(clone.Slides[0].Elements[clone.Slides[0].Elements.Count - 2]);
    }

    [Fact]
    public void ColorRefs_Resolve()
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("corporate"));
        var style = SlideStyle.For(deck, deck.Slides[0]);
        var accent = style.Resolve("accent1");
        Assert.Equal(SlideStyle.ParseHex(deck.Theme.Accents[0])!.Value, accent);
        var half = style.Resolve("#FF0000@50");
        Assert.Equal(255, half.R);
        Assert.InRange(half.A, 126, 129);
    }
}
