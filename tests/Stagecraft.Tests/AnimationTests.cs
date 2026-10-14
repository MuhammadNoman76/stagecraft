using System.Text.Json.Nodes;
using System.Windows;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;

namespace Stagecraft.Tests;

public class AnimationTests
{
    private static readonly Size Slide1080 = new(1920, 1080);

    private static (Slide Slide, TextElement A, ShapeElement B, ShapeElement C) ThreeElements()
    {
        var slide = new Slide { Layout = "blank" };
        var a = new TextElement { X = 100, Y = 100, Width = 600, Height = 200, Text = new TextBody { Content = RichText.FromPlain("Alpha beta gamma\nSecond line here") } };
        var b = new ShapeElement { X = 900, Y = 400, Width = 300, Height = 300 };
        var c = new ShapeElement { X = 1300, Y = 700, Width = 200, Height = 100 };
        slide.Elements.AddRange(new Element[] { a, b, c });
        return (slide, a, b, c);
    }

    private static AnimationEffect Effect(Element target, EffectClass cls, EffectKind kind, EffectStart start, double delay = 0, double duration = 1)
    {
        var e = EffectCatalog.Create(cls, kind, target.Id);
        e.Start = start;
        e.Delay = delay;
        e.Duration = duration;
        return e;
    }

    // ------------------------------------------------------------------ migration from 1.0 builds

    private const string V1Deck = """
    {
      "schemaVersion": 1,
      "title": "Old deck",
      "slides": [
        {
          "id": "s1", "layout": "blank",
          "elements": [
            { "type": "shape", "id": "e1", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "fade", "step": 1, "delay": 0, "duration": 0.5 } },
            { "type": "shape", "id": "e2", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "rise", "step": 1, "delay": 0.2, "duration": 0.6 } },
            { "type": "shape", "id": "e3", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "scale", "step": 3, "delay": 0, "duration": 0.4 } },
            { "type": "shape", "id": "e4", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "none", "step": 2 } },
            { "type": "shape", "id": "e5", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "wipe", "step": 2, "delay": 0, "duration": 0.7 } },
            { "type": "shape", "id": "e6", "x": 0, "y": 0, "width": 100, "height": 100, "build": { "effect": "appear", "step": 2 } }
          ]
        },
        { "id": "s2", "layout": "blank", "elements": [ { "type": "shape", "id": "x", "x": 0, "y": 0, "width": 10, "height": 10 } ] }
      ]
    }
    """;

    [Fact]
    public void Migration_V1Builds_BecomeAnimations_InClickOrder() => Sta.Run(() =>
    {
        var deck = DeckFile.ParseDeck(V1Deck, out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(Deck.CurrentSchemaVersion, deck.SchemaVersion);
        Assert.True(Deck.CurrentSchemaVersion >= 2);
        var a = deck.Slides[0].Animations;
        // e4 had "none": no effect. Order: step 1 (e1, e2), step 2 (e5, e6), step 3 (e3).
        Assert.Equal(new[] { "e1", "e2", "e5", "e6", "e3" }, a.Select(x => x.Target).ToArray());
        Assert.All(a, x => Assert.Equal(EffectClass.Entrance, x.Class));
        Assert.Equal(new[] { EffectStart.OnClick, EffectStart.WithPrevious, EffectStart.OnClick, EffectStart.WithPrevious, EffectStart.OnClick }, a.Select(x => x.Start).ToArray());
        Assert.Equal(EffectKind.Fade, a[0].Effect);
        Assert.Equal(EffectKind.Rise, a[1].Effect);
        Assert.Equal(0.2, a[1].Delay, 6);
        Assert.Equal(0.6, a[1].Duration, 6);
        Assert.Equal(EffectKind.Wipe, a[2].Effect);
        Assert.Equal(EffectDirection.Left, a[2].Direction);
        Assert.Equal(EffectKind.Appear, a[3].Effect);
        Assert.Equal(EffectKind.Zoom, a[4].Effect);
        Assert.Equal(82, a[4].Amount, 6);
        Assert.Empty(deck.Slides[1].Animations);

        // The same clicks as 1.0: three clicks, e1 and e2 together on the first.
        var timeline = SlideTimeline.Build(deck.Slides[0]);
        Assert.Equal(3, timeline.Clicks);
        Assert.False(timeline.HasAutomaticStart);
        Assert.Equal(new[] { "e1", "e2" }, timeline.Groups[0].Items.Select(i => i.Effect.Target).ToArray());
        Assert.Equal(0.2, timeline.Groups[0].Items[1].Begin, 6);
    });

    [Fact]
    public void Migration_RemovesBuildProperty_AndSurvivesSaveLoad() => Sta.Run(() =>
    {
        var root = (JsonObject)JsonNode.Parse(V1Deck)!;
        Migrations.Upgrade(root, 1, new List<string>());
        Assert.Equal(2, root["schemaVersion"]!.GetValue<int>());
        var elements = (JsonArray)root["slides"]![0]!["elements"]!;
        Assert.All(elements.OfType<JsonObject>(), e => Assert.False(e.ContainsKey("build")));

        using var dir = new TempDir();
        var deck = DeckFile.ParseDeck(V1Deck, out _);
        var path = dir.File("migrated" + DeckFile.Extension);
        DeckFile.Save(path, deck, new MediaStore());
        var loaded = DeckFile.Load(path).Deck;
        Assert.Equal(Decks.Json(deck), Decks.Json(loaded));
        Assert.Equal(5, loaded.Slides[0].Animations.Count);
    });

    [Fact]
    public void Migration_ElementWithoutId_GetsOne_AndKeepsItsBuild()
    {
        var json = """{ "schemaVersion": 1, "slides": [ { "id": "s", "elements": [ { "type": "shape", "x": 1, "y": 1, "width": 9, "height": 9, "build": { "effect": "fade", "step": 1 } } ] } ] }""";
        var deck = Sta.Run(() => DeckFile.ParseDeck(json, out _));
        var effect = Assert.Single(deck.Slides[0].Animations);
        Assert.Equal(deck.Slides[0].Elements[0].Id, effect.Target);
    }

    [Fact]
    public void Repair_DropsAnimationsWithoutTargets_AndFixesValues() => Sta.Run(() =>
    {
        var (slide, a, b, _) = ThreeElements();
        slide.Animations.Add(Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, delay: -3, duration: double.NaN));
        slide.Animations.Add(new AnimationEffect { Target = "gone", Class = EffectClass.Exit, Effect = EffectKind.Fade });
        slide.Animations.Add(new AnimationEffect { Target = b.Id, Class = EffectClass.Emphasis, Effect = EffectKind.Fly }); // fly isn't an emphasis
        slide.Animations.Add(new AnimationEffect { Target = b.Id, Class = EffectClass.Motion, Effect = EffectKind.Line, Path = null });
        slide.Animations.Add(new AnimationEffect { Target = b.Id, Class = EffectClass.Media, Effect = EffectKind.Play }); // not a video
        slide.Animations.Add(new AnimationEffect { Target = b.Id, Trigger = "missing" });
        AnimationRules.Repair(slide);
        Assert.Equal(3, slide.Animations.Count);
        Assert.Equal(0, slide.Animations[0].Delay);
        Assert.Equal(EffectCatalog.DefaultDuration(EffectClass.Entrance, EffectKind.Fade), slide.Animations[0].Duration);
        Assert.True(EffectCatalog.Belongs(EffectClass.Emphasis, slide.Animations[1].Effect));
        Assert.NotNull(slide.Animations[2].Path);
        Assert.True(slide.Animations[2].Path!.Points.Count >= 2);
    });

    // ------------------------------------------------------------------ timeline

    [Fact]
    public void Timeline_ClickGroups_WithAndAfterPrevious()
    {
        var (slide, a, b, c) = ThreeElements();
        slide.Animations.Add(Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, 0.5, 1));      // click 1, 0.5–1.5
        slide.Animations.Add(Effect(b, EffectClass.Entrance, EffectKind.Fly, EffectStart.WithPrevious, 0.2, 2));   // with it: 0.2–2.2
        slide.Animations.Add(Effect(c, EffectClass.Entrance, EffectKind.Zoom, EffectStart.AfterPrevious, 0.3, 1)); // after both: 2.5–3.5
        slide.Animations.Add(Effect(a, EffectClass.Emphasis, EffectKind.Spin, EffectStart.OnClick, 0, 1));        // click 2
        slide.Animations.Add(Effect(b, EffectClass.Exit, EffectKind.Fade, EffectStart.WithPrevious, 1, 0.5));      // 1–1.5
        var t = SlideTimeline.Build(slide);
        Assert.Equal(2, t.Clicks);
        Assert.False(t.HasAutomaticStart);
        var g1 = t.Groups[0];
        Assert.Equal(1, g1.Click);
        Assert.Equal(new[] { 0.5, 0.2, 2.5 }, g1.Items.Select(i => Math.Round(i.Begin, 6)).ToArray());
        Assert.Equal(3.5, g1.Length, 6);
        var g2 = t.Groups[1];
        Assert.Equal(2, g2.Click);
        Assert.Equal(new[] { 0.0, 1.0 }, g2.Items.Select(i => Math.Round(i.Begin, 6)).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, t.AllItems.Select(i => i.Order).ToArray());
    }

    [Fact]
    public void Timeline_AutomaticStart_AndTriggers()
    {
        var (slide, a, b, c) = ThreeElements();
        slide.Animations.Add(Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.AfterPrevious, 1)); // starts with the slide
        slide.Animations.Add(Effect(b, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick));
        var t1 = Effect(c, EffectClass.Emphasis, EffectKind.Pulse, EffectStart.OnClick);
        t1.Trigger = b.Id;
        var t2 = Effect(c, EffectClass.Exit, EffectKind.Fade, EffectStart.AfterPrevious);
        t2.Trigger = b.Id;
        var t3 = Effect(a, EffectClass.Emphasis, EffectKind.Spin, EffectStart.OnClick);
        t3.Trigger = b.Id;
        slide.Animations.AddRange(new[] { t1, t2, t3 });
        var t = SlideTimeline.Build(slide);
        Assert.True(t.HasAutomaticStart);
        Assert.Equal(1, t.Clicks);
        Assert.Equal(0, t.Groups[0].Click);
        Assert.Equal(1.0, t.Groups[0].Items[0].Begin, 6);
        var trig = Assert.Single(t.Triggers);
        Assert.Equal(b.Id, trig.Key);
        Assert.Equal(2, trig.Value.Count); // pulse + exit on the first click of b, spin on the second
        Assert.Equal(new[] { 1, 2 }, trig.Value.Select(g => g.Click).ToArray());
        Assert.Equal(t1.Duration, trig.Value[0].Items[1].Begin, 6);
    }

    [Fact]
    public void Timeline_ByParagraphOnClick_SplitsIntoClicks_AndTextStaggerAddsUp()
    {
        var (slide, a, _, _) = ThreeElements();
        var byParagraph = Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, 0, 0.5);
        byParagraph.TextUnit = TextUnit.Paragraph;
        byParagraph.ParagraphClicks = true;
        slide.Animations.Add(byParagraph);
        var t = SlideTimeline.Build(slide);
        Assert.Equal(2, t.Clicks);
        Assert.Equal(new[] { 0, 1 }, t.Groups.Select(g => g.Items.Single().Paragraph).ToArray());

        slide.Animations.Clear();
        var byWord = Effect(a, EffectClass.Entrance, EffectKind.Fly, EffectStart.OnClick, 0, 0.5);
        byWord.TextUnit = TextUnit.Word;
        byWord.TextStagger = 0.1;
        slide.Animations.Add(byWord);
        var item = SlideTimeline.Build(slide).Groups.Single().Items.Single();
        Assert.Equal(6, item.Pieces); // "Alpha beta gamma" + "Second line here"
        Assert.Equal(0.5 + 5 * 0.1, item.Length, 6);

        byWord.TextUnit = TextUnit.Letter;
        byWord.TextStagger = null;
        item = SlideTimeline.Build(slide).Groups.Single().Items.Single();
        Assert.Equal("AlphabetagammaSecondlinehere".Length, item.Pieces);
        Assert.Equal(SlideTimeline.DefaultStagger(TextUnit.Letter), item.Stagger, 6);
    }

    [Fact]
    public void Timeline_RepeatCountsForAfterPrevious_EndlessRepeatsCountOnce()
    {
        var (slide, a, b, c) = ThreeElements();
        var spin = Effect(a, EffectClass.Emphasis, EffectKind.Spin, EffectStart.OnClick, 0, 1);
        spin.Repeat = 3;
        var pulse = Effect(b, EffectClass.Emphasis, EffectKind.Pulse, EffectStart.AfterPrevious, 0, 0.5);
        pulse.RepeatUntil = RepeatUntil.NextClick;
        slide.Animations.AddRange(new[] { spin, pulse, Effect(c, EffectClass.Entrance, EffectKind.Fade, EffectStart.AfterPrevious) });
        var g = SlideTimeline.Build(slide).Groups.Single();
        Assert.Equal(3.0, g.Items[1].Begin, 6);
        Assert.Equal(3.5, g.Items[2].Begin, 6);
    }

    [Fact]
    public void Timeline_GroupTarget_AnimatesEveryMember()
    {
        var (slide, _, b, c) = ThreeElements();
        b.GroupId = c.GroupId = "grp";
        slide.Animations.Add(new AnimationEffect { Target = "grp", Class = EffectClass.Entrance, Effect = EffectKind.Fade, Duration = 1, Easing = EffectEasing.Linear });
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        Assert.True(animator.StartsHidden(b.Id));
        Assert.True(animator.StartsHidden(c.Id));
        var head = new Playhead();
        head.Main.Add(10);
        Assert.Equal(0.5, animator.Evaluate(c, head, 10.5).Opacity, 2);
    }

    // ------------------------------------------------------------------ animator

    [Fact]
    public void Animator_EntranceExitAndEmphasis_States()
    {
        var (slide, a, b, c) = ThreeElements();
        slide.Animations.Add(Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, 0, 1));
        slide.Animations.Add(Effect(b, EffectClass.Exit, EffectKind.Fade, EffectStart.OnClick, 0, 1));
        var spin = Effect(c, EffectClass.Emphasis, EffectKind.Spin, EffectStart.WithPrevious, 0, 2);
        spin.Amount = 90;
        slide.Animations.Add(spin);
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);

        var before = new Playhead();
        Assert.False(animator.Evaluate(a, before, 0).Visible);
        Assert.True(animator.Evaluate(b, before, 0).Visible);
        Assert.True(animator.Evaluate(c, before, 0).Visible);

        var head = new Playhead();
        head.Main.Add(100); // click 1 at t = 100
        var mid = animator.Evaluate(a, head, 100.5);
        Assert.True(mid.Visible);
        Assert.InRange(mid.Opacity, 0.5, 0.99); // decelerating fade: past half way at half time
        var done = animator.Evaluate(a, head, 101.5);
        Assert.True(done.IsIdentity);

        head.Main.Add(200); // click 2
        var exiting = animator.Evaluate(b, head, 200.5);
        Assert.True(exiting.Visible);
        Assert.InRange(exiting.Opacity, 0.01, 0.75);
        Assert.False(animator.Evaluate(b, head, 201.5).Visible);
        Assert.Equal(45, animator.Evaluate(c, head, 201).Rotation, 0); // half way with smooth easing
        Assert.Equal(90, animator.Evaluate(c, head, 260).Rotation, 6); // stays turned
        Assert.Equal(c.Center, animator.Evaluate(c, head, 201).Center);

        // Going back to after click 1: settled states, nothing in progress.
        var back = Playhead.Settled(1);
        Assert.True(animator.Evaluate(a, back, 0).IsIdentity);
        Assert.True(animator.Evaluate(b, back, 0).Visible);
        Assert.Equal(0, animator.Evaluate(c, back, 0).Rotation);
        Assert.False(animator.IsRunning(back, 0));
        Assert.True(animator.IsRunning(head, 200.2));
        Assert.False(animator.IsRunning(head, 205));
    }

    [Fact]
    public void Animator_Rewind_ReturnsToTheStart()
    {
        var (slide, a, b, _) = ThreeElements();
        var entrance = Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, 0, 1);
        entrance.Rewind = true;
        var grow = Effect(b, EffectClass.Emphasis, EffectKind.Grow, EffectStart.WithPrevious, 0, 1);
        grow.Rewind = true;
        slide.Animations.AddRange(new[] { entrance, grow });
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        var head = new Playhead();
        head.Main.Add(0);
        Assert.True(animator.Evaluate(a, head, 0.5).Visible);
        Assert.False(animator.Evaluate(a, head, 2).Visible);
        Assert.True(animator.Evaluate(b, head, 0.5).ScaleX > 1.1);
        Assert.Equal(1, animator.Evaluate(b, head, 2).ScaleX, 6);
    }

    [Fact]
    public void Animator_GrowTransparencyAndMotion_KeepTheirEndState()
    {
        var (slide, a, b, c) = ThreeElements();
        var grow = Effect(a, EffectClass.Emphasis, EffectKind.Grow, EffectStart.OnClick, 0, 1);
        grow.Amount = 200;
        var fade = Effect(b, EffectClass.Emphasis, EffectKind.Transparency, EffectStart.WithPrevious, 0, 1);
        fade.Amount = 75;
        var move = Effect(c, EffectClass.Motion, EffectKind.Line, EffectStart.WithPrevious, 0, 1);
        move.Path = new MotionPath { Points = { new PathPoint(0, 0), new PathPoint(300, -100) } };
        slide.Animations.AddRange(new[] { grow, fade, move });
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        var head = Playhead.Settled(1);
        Assert.Equal(2, animator.Evaluate(a, head, 0).ScaleX, 6);
        Assert.Equal(0.25, animator.Evaluate(b, head, 0).Opacity, 6);
        var end = animator.Evaluate(c, head, 0);
        Assert.Equal(300, end.Dx, 6);
        Assert.Equal(-100, end.Dy, 6);

        // Half way along the line (smooth easing is symmetric).
        var live = new Playhead();
        live.Main.Add(0);
        var half = animator.Evaluate(c, live, 0.5);
        Assert.Equal(150, half.Dx, 1);
        Assert.Equal(-50, half.Dy, 1);
    }

    [Fact]
    public void Animator_FlyIn_StartsOffTheSlide()
    {
        foreach (var direction in EffectCatalog.Directions(EffectKind.Fly))
        {
            var (slide, a, _, _) = ThreeElements();
            var fly = Effect(a, EffectClass.Entrance, EffectKind.Fly, EffectStart.OnClick, 0, 1);
            fly.Direction = direction;
            fly.Easing = EffectEasing.Linear;
            slide.Animations.Add(fly);
            var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
            var head = new Playhead();
            head.Main.Add(0);
            var s = animator.Evaluate(a, head, 1e-6);
            var moved = a.RotatedBounds;
            moved.Offset(s.Dx, s.Dy);
            Assert.False(moved.IntersectsWith(new Rect(0, 0, 1920, 1080)), direction + " should start outside the slide");
            Assert.True(animator.Evaluate(a, head, 1).IsIdentity);
        }
    }

    [Fact]
    public void Animator_Wipe_ClipsFromTheNamedSide_AndExitsMirror() => Sta.Run(() =>
    {
        var (slide, a, b, _) = ThreeElements();
        var wipe = Effect(a, EffectClass.Entrance, EffectKind.Wipe, EffectStart.OnClick, 0, 1);
        wipe.Direction = EffectDirection.Left;
        wipe.Easing = EffectEasing.Linear;
        var wipeOut = Effect(b, EffectClass.Exit, EffectKind.Wipe, EffectStart.WithPrevious, 0, 1);
        wipeOut.Direction = EffectDirection.Left;
        wipeOut.Easing = EffectEasing.Linear;
        slide.Animations.AddRange(new[] { wipe, wipeOut });
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        var head = new Playhead();
        head.Main.Add(0);
        var s = animator.Evaluate(a, head, 0.25);
        Assert.Equal(ClipKind.Wipe, s.Clip);
        var clip = AnimGeometry.Clip(s, bleed: 0)!.Bounds;
        Assert.Equal(a.X, clip.X, 3);
        Assert.Equal(a.Width * 0.25, clip.Width, 3);
        // Wiping out "from the left": the left part goes first.
        var o = AnimGeometry.Clip(animator.Evaluate(b, head, 0.25), bleed: 0)!.Bounds;
        Assert.Equal(b.X + b.Width * 0.25, o.X, 3);
        Assert.Equal(b.RotatedBounds.Right, o.Right, 3);
    });

    [Fact]
    public void Animator_TextPieces_StaggerAndParagraphClicks() => Sta.Run(() =>
    {
        var (slide, a, _, _) = ThreeElements();
        var byWord = Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick, 0, 0.5);
        byWord.TextUnit = TextUnit.Word;
        byWord.TextStagger = 0.25;
        slide.Animations.Add(byWord);
        var deck = new Deck();
        var style = SlideStyle.For(deck, slide);
        var pieces = TextLayout.Pieces(a.Text, style, TextUnit.Word);
        Assert.Equal(6, pieces.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, pieces.Select(p => p.Ref.Word).ToArray());
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        Assert.Equal(TextUnit.Word, animator.PieceUnit(a.Id));
        var head = new Playhead();
        head.Main.Add(0);
        // At 0.6 s: word 0 done, word 1 (from 0.25 s) running, word 3 (from 0.75 s) not yet.
        Assert.True(animator.Evaluate(a, head, 0.6, pieces[0].Ref).IsIdentity);
        Assert.InRange(animator.Evaluate(a, head, 0.6, pieces[1].Ref).Opacity, 0.3, 0.999);
        Assert.False(animator.Evaluate(a, head, 0.6, pieces[3].Ref).Visible);
        Assert.True(animator.Evaluate(a, head, 0.6, PieceRef.Body).Visible);

        slide.Animations.Clear();
        var byParagraph = Effect(a, EffectClass.Entrance, EffectKind.Appear, EffectStart.OnClick, 0, 0);
        byParagraph.TextUnit = TextUnit.Paragraph;
        byParagraph.ParagraphClicks = true;
        slide.Animations.Add(byParagraph);
        animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        var paragraphs = TextLayout.Pieces(a.Text, style, TextUnit.Paragraph);
        var one = Playhead.Settled(1);
        Assert.True(animator.Evaluate(a, one, 0, paragraphs[0].Ref).Visible);
        Assert.False(animator.Evaluate(a, one, 0, paragraphs[1].Ref).Visible);
        Assert.True(animator.Evaluate(a, Playhead.Settled(2), 0, paragraphs[1].Ref).Visible);
    });

    [Fact]
    public void Animator_RepeatUntilNextClick_LoopsThenStops()
    {
        var (slide, a, b, _) = ThreeElements();
        var pulse = Effect(a, EffectClass.Emphasis, EffectKind.Teeter, EffectStart.OnClick, 0, 1);
        pulse.RepeatUntil = RepeatUntil.NextClick;
        slide.Animations.Add(pulse);
        slide.Animations.Add(Effect(b, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick));
        var animator = new Animator(SlideTimeline.Build(slide), Slide1080);
        var head = new Playhead();
        head.Main.Add(0);
        Assert.NotEqual(0, animator.Evaluate(a, head, 7.2).Rotation, 3);
        Assert.True(animator.IsRunning(head, 50));
        head.Main.Add(60);
        Assert.Equal(0, animator.Evaluate(a, head, 60.1).Rotation, 6);
    }

    [Fact]
    public void Animator_MediaCues_AreDueWhenTheirGroupStarts()
    {
        var slide = new Slide();
        var video = new VideoElement { X = 0, Y = 0, Width = 640, Height = 360, Media = "x.mp4" };
        slide.Elements.Add(video);
        slide.Animations.Add(new AnimationEffect { Target = video.Id, Class = EffectClass.Media, Effect = EffectKind.Play, Start = EffectStart.AfterPrevious, Delay = 0.5 });
        var timeline = SlideTimeline.Build(slide);
        Assert.True(timeline.HasAutomaticStart);
        var animator = new Animator(timeline, Slide1080);
        var head = new Playhead();
        head.Main.Add(10);
        Assert.Empty(animator.DueMedia(head, 10.2));
        var cue = Assert.Single(animator.DueMedia(head, 10.6));
        Assert.Equal(10.5, cue.Due, 6);
        Assert.Equal(EffectKind.Play, cue.Item.Effect.Effect);
    }

    // ------------------------------------------------------------------ copying

    [Fact]
    public void DuplicateSlide_AndRenewIds_KeepAnimationsPointingAtTheCopies()
    {
        var (slide, a, b, c) = ThreeElements();
        b.GroupId = c.GroupId = "g1";
        var trigger = Effect(a, EffectClass.Emphasis, EffectKind.Pulse, EffectStart.OnClick);
        trigger.Trigger = b.Id;
        slide.Animations.Add(Effect(a, EffectClass.Entrance, EffectKind.Fade, EffectStart.OnClick));
        slide.Animations.Add(new AnimationEffect { Target = "g1", Class = EffectClass.Entrance, Effect = EffectKind.Fly });
        slide.Animations.Add(trigger);
        var copy = DeckOperations.DuplicateSlide(slide);
        Assert.Equal(3, copy.Animations.Count);
        Assert.All(copy.Animations, x => Assert.NotEmpty(AnimationRules.Resolve(copy, x.Target)));
        Assert.DoesNotContain(copy.Animations, x => slide.Elements.Any(e => e.Id == x.Target) || x.Target == "g1");
        Assert.Equal(copy.Elements[1].Id, copy.Animations[2].Trigger);
        Assert.Equal(2, AnimationRules.Resolve(copy, copy.Animations[1].Target).Count);
        Assert.All(copy.Animations, x => Assert.DoesNotContain(slide.Animations, o => o.Id == x.Id));
    }

    // ------------------------------------------------------------------ easing and paths

    [Fact]
    public void Easing_Curves_StartAndEndRight_AndOvershootOvershoots()
    {
        foreach (var easing in Enum.GetValues(typeof(EffectEasing)).Cast<EffectEasing>())
        {
            var e = new AnimationEffect { Easing = easing };
            Assert.Equal(0, Animator.Ease(e, 0, true), 6);
            Assert.Equal(1, Animator.Ease(e, 1, true), 6);
        }
        var over = new AnimationEffect { Easing = EffectEasing.Overshoot };
        Assert.Contains(Enumerable.Range(1, 99), i => Animator.Ease(over, i / 100.0, null) > 1.01);
        var smooth = new AnimationEffect { Easing = EffectEasing.EaseInOut };
        var previous = 0.0;
        for (var i = 1; i <= 100; i++)
        {
            var v = Animator.Ease(smooth, i / 100.0, null);
            Assert.True(v >= previous - 1e-9);
            previous = v;
        }
        Assert.Equal(0.5, Animator.Ease(smooth, 0.5, null), 2);
        Assert.Equal(0, Animator.BounceOut(0), 6);
        Assert.Equal(1, Animator.BounceOut(1), 6);
        Assert.True(Animator.Ease(new AnimationEffect(), 0.3, true) > 0.3);   // decelerating in
        Assert.True(Animator.Ease(new AnimationEffect(), 0.3, false) < 0.3);  // accelerating out
    }

    [Fact]
    public void PathMath_Line_Arc_Circle_AndSimplify()
    {
        var line = new MotionPath { Points = { new PathPoint(0, 0), new PathPoint(100, 0), new PathPoint(100, 100) } };
        Assert.Equal(200, PathMath.Length(PathMath.Flatten(line)), 6);
        var mid = PathMath.Offset(line, 0.5);
        Assert.Equal(100, mid.X, 6);
        Assert.Equal(0, mid.Y, 6);
        var quarter = PathMath.Offset(line, 0.75);
        Assert.Equal(100, quarter.X, 6);
        Assert.Equal(50, quarter.Y, 6);

        var arc = EffectCatalog.DefaultPath(EffectKind.Arc, 200, 200);
        Assert.Equal(0, PathMath.Offset(arc, 0).Length, 6);
        var arcEnd = PathMath.Offset(arc, 1);
        Assert.True(arcEnd.X > 100 && Math.Abs(arcEnd.Y) < 1e-6);
        Assert.True(PathMath.Offset(arc, 0.5).Y < -40); // over the top

        var circle = EffectCatalog.DefaultPath(EffectKind.Circle, 200, 200);
        Assert.True(circle.Closed);
        Assert.Equal(0, PathMath.Offset(circle, 1).Length, 3); // back where it started
        var flat = PathMath.Flatten(circle);
        var radius = circle.Points.Max(p => p.Y) / 2;
        var center = new Point(0, radius);
        Assert.All(flat, p => Assert.InRange((p - center).Length, radius * 0.97, radius * 1.03)); // smooth = round

        var noisy = Enumerable.Range(0, 200).Select(i => new Point(i, i % 2 == 0 ? 0.2 : -0.2)).ToList();
        var simple = PathMath.Simplify(noisy, 1);
        Assert.Equal(2, simple.Count);
        Assert.Equal(new Point(0, 0.2), simple[0]);
    }

    [Fact]
    public void AnimGeometry_SplitAndWheel_CoverTheRightAmount() => Sta.Run(() =>
    {
        var s = AnimState.Shown(new Point(50, 50));
        s.ClipBounds = new Rect(0, 0, 100, 100);
        s.Clip = ClipKind.Split;
        s.ClipDirection = EffectDirection.VerticalOut;
        s.ClipProgress = 0.5;
        var g = AnimGeometry.Clip(s, 0)!;
        Assert.Equal(new Rect(25, 0, 50, 100), g.Bounds);
        s.ClipDirection = EffectDirection.HorizontalIn;
        var bands = AnimGeometry.Clip(s, 0)!;
        Assert.True(bands.FillContains(new Point(50, 10)));
        Assert.False(bands.FillContains(new Point(50, 50)));
        s.Clip = ClipKind.Wheel;
        s.ClipProgress = 0.25;
        var wheel = AnimGeometry.Clip(s, 0)!;
        Assert.True(wheel.FillContains(new Point(70, 30)));   // top right quarter is shown
        Assert.False(wheel.FillContains(new Point(30, 70)));  // bottom left isn't
        s.ClipProgress = 1;
        Assert.Null(AnimGeometry.Clip(s, 0));
    });

    [Fact]
    public void SampleDecks_AnimationsResolve_AndRenderAtEveryStep() => Sta.Run(() =>
    {
        foreach (var sample in DemoDecks.All)
        {
            var doc = sample.Create();
            foreach (var slide in doc.Deck.Slides)
            {
                Assert.All(slide.Animations, a => Assert.True(AnimationRules.IsLive(slide, a), sample.Id + ": dangling animation"));
                var timeline = SlideTimeline.Build(slide);
                var animator = new Animator(timeline, new Size(doc.Deck.Width, doc.Deck.Height));
                for (var g = 0; g <= timeline.Groups.Count; g++)
                {
                    var head = Playhead.Settled(g);
                    foreach (var e in slide.Elements) animator.Evaluate(e, head, 0);
                }
            }
        }
    });
}
