using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Tests;

public class SnappingTests
{
    private static readonly Size Slide = new(1920, 1080);

    [Fact]
    public void SnapsToAnotherElementsEdge()
    {
        var targets = new SnapTargets(new[] { new Rect(400, 300, 200, 100) }, Slide, 0);
        var r = Snapper.SnapMove(new Rect(405, 600, 150, 80), targets, threshold: 8);
        Assert.True(r.SnappedX);
        Assert.Equal(-5, r.Dx, 6);
        Assert.Contains(r.Guides, g => g.Vertical && Math.Abs(g.Position - 400) < 0.01);
    }

    [Fact]
    public void SnapsCentreToSlideCentre()
    {
        var targets = new SnapTargets(Array.Empty<Rect>(), Slide, 0);
        var r = Snapper.SnapMove(new Rect(856, 487, 200, 100), targets, threshold: 8);
        Assert.Equal(960 - 956, r.Dx, 6);
        Assert.Equal(540 - 537, r.Dy, 6);
        Assert.Contains(r.Guides, g => g.Kind == GuideKind.Slide);
    }

    [Fact]
    public void SnapsToMargins()
    {
        var targets = new SnapTargets(Array.Empty<Rect>(), Slide, margin: 96);
        var r = Snapper.SnapMove(new Rect(100, 300, 50, 50), targets, threshold: 8);
        Assert.Equal(-4, r.Dx, 6);
    }

    [Fact]
    public void NothingNearby_NoSnap()
    {
        var targets = new SnapTargets(new[] { new Rect(0, 0, 100, 100) }, Slide, 0);
        var r = Snapper.SnapMove(new Rect(300, 300, 133, 77), targets, threshold: 6);
        Assert.False(r.SnappedX);
        Assert.False(r.SnappedY);
        Assert.Equal(0, r.Dx);
        Assert.Equal(0, r.Dy);
        Assert.Empty(r.Guides);
    }

    [Fact]
    public void EqualSpacing_MatchesExistingGap()
    {
        // A and B are 100 apart; moving C near the same gap after B snaps to exactly 100.
        var a = new Rect(500, 400, 100, 100);
        var b = new Rect(700, 400, 100, 100);
        var targets = new SnapTargets(new[] { a, b }, Slide, 0);
        var r = Snapper.SnapMove(new Rect(903, 400, 100, 100), targets, threshold: 6);
        Assert.True(r.SnappedX);
        Assert.Equal(-3, r.Dx, 6);
        var guide = Assert.Single(r.Guides, g => g.Kind == GuideKind.Spacing);
        Assert.Equal(2, guide.Gaps.Count);
        Assert.All(guide.Gaps, gap => Assert.Equal(100, gap.End - gap.Start, 6));
    }

    [Fact]
    public void EqualSpacing_CentresBetweenTwo()
    {
        var a = new Rect(100, 400, 100, 100);
        var b = new Rect(600, 400, 100, 100);
        var targets = new SnapTargets(new[] { a, b }, Slide, 0);
        // Room between them is 400; a 100 wide box centred leaves 150 on each side (x = 350).
        var r = Snapper.SnapMove(new Rect(354, 400, 100, 100), targets, threshold: 6);
        Assert.Equal(-4, r.Dx, 6);
    }

    [Fact]
    public void Resize_SnapsMovingEdgeOnly()
    {
        var targets = new SnapTargets(new[] { new Rect(800, 100, 100, 100) }, Slide, 0);
        var r = Snapper.SnapResize(new Rect(300, 500, 496, 100), horizontalEdge: 1, verticalEdge: 0, targets, threshold: 8);
        Assert.True(r.SnappedX);
        Assert.Equal(4, r.Dx, 6);
        Assert.False(r.SnappedY);
    }
}

public class TidyTests
{
    [Fact]
    public void MessyRow_BecomesEvenRow()
    {
        var input = new[]
        {
            new Rect(100, 200, 200, 150),
            new Rect(330, 214, 210, 145),
            new Rect(610, 190, 195, 152),
        };
        var output = Tidy.Arrange(input);
        Assert.All(output, r => Assert.Equal(output[0].Top, r.Top));
        Assert.All(output, r => Assert.Equal(output[0].Width, r.Width));
        var gap1 = output[1].Left - output[0].Right;
        var gap2 = output[2].Left - output[1].Right;
        Assert.Equal(gap1, gap2);
        Assert.True(gap1 > 0);
    }

    [Fact]
    public void Column_LinesUpLeftEdges()
    {
        var input = new[]
        {
            new Rect(300, 100, 400, 80),
            new Rect(312, 230, 400, 80),
            new Rect(290, 350, 400, 80),
        };
        var output = Tidy.Arrange(input);
        Assert.All(output, r => Assert.Equal(output[0].Left, r.Left));
        Assert.Equal(output[1].Top - output[0].Bottom, output[2].Top - output[1].Bottom);
    }

    [Fact]
    public void Grid_GetsEqualColumnsAndRows()
    {
        var input = new[]
        {
            new Rect(100, 100, 200, 120), new Rect(340, 108, 200, 120), new Rect(590, 95, 200, 120),
            new Rect(104, 300, 200, 120), new Rect(335, 290, 200, 120), new Rect(600, 305, 200, 120),
        };
        var output = Tidy.Arrange(input);
        Assert.Equal(output[0].Top, output[1].Top);
        Assert.Equal(output[1].Top, output[2].Top);
        Assert.Equal(output[3].Top, output[4].Top);
        Assert.Equal(output[0].Left, output[3].Left);
        Assert.Equal(output[1].Left, output[4].Left);
        Assert.Equal(output[2].Left, output[5].Left);
        Assert.Equal(output[1].Left - output[0].Right, output[2].Left - output[1].Right);
    }

    [Fact]
    public void Overlapping_GetsDefaultGap()
    {
        var input = new[] { new Rect(100, 100, 200, 100), new Rect(150, 105, 200, 100) };
        var output = Tidy.Arrange(input, defaultGap: 32);
        Assert.Equal(32, output[1].Left - output[0].Right);
    }
}

public class MorphTests
{
    private static Slide Slide(params Element[] elements)
    {
        var s = new Slide { Layout = "blank" };
        s.Elements.AddRange(elements);
        return s;
    }

    [Fact]
    public void DuplicatedSlide_MatchesByIdentity()
    {
        var a = new ShapeElement { Shape = ShapeKind.Ellipse, X = 100, Y = 100, Width = 200, Height = 200, Fill = Fill.Solid("accent1") };
        var b = new ShapeElement { Shape = ShapeKind.Rectangle, X = 500, Y = 100, Width = 200, Height = 200, Fill = Fill.Solid("accent2") };
        var from = Slide(a, b);
        var to = DeckOperations_Duplicate(from);
        to.Elements[0].X = 1200;
        to.Elements[1].Y = 700;
        var tracks = MorphPlanner.Plan(from, to);
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, t => Assert.Equal(MorphTrackKind.Morph, t.Kind));
        Assert.Same(a, tracks.Single(t => t.To == to.Elements[0]).From);
        Assert.Same(b, tracks.Single(t => t.To == to.Elements[1]).From);
    }

    private static Slide DeckOperations_Duplicate(Slide s) => DeckOperations.DuplicateSlide(s);

    [Fact]
    public void MatchesByName_ThenContent_ElseFades()
    {
        var named1 = new ShapeElement { Name = "Hero", X = 0, Y = 0, Width = 10, Height = 10 };
        var named2 = new ShapeElement { Name = "hero ", X = 50, Y = 50, Width = 10, Height = 10 };
        var text1 = new TextElement { Text = new TextBody { Content = RichText.FromPlain("Same words") } };
        var text2 = new TextElement { Text = new TextBody { Content = RichText.FromPlain("Same words") }, X = 300 };
        var gone = new IconElement { Icon = "rocket" };
        var added = new IconElement { Icon = "star" };
        var tracks = MorphPlanner.Plan(Slide(named1, text1, gone), Slide(named2, text2, added));
        Assert.Same(named1, tracks.Single(t => t.To == named2).From);
        var textTrack = tracks.Single(t => t.To == text2);
        Assert.Same(text1, textTrack.From);
        Assert.True(textTrack.SameText);
        Assert.Contains(tracks, t => t.Kind == MorphTrackKind.FadeOut && t.From == gone);
        Assert.Contains(tracks, t => t.Kind == MorphTrackKind.FadeIn && t.To == added);
    }

    [Fact]
    public void DifferentTypes_NeverMatch()
    {
        var shape = new ShapeElement { Name = "X" };
        var icon = new IconElement { Name = "X" };
        var tracks = MorphPlanner.Plan(Slide(shape), Slide(icon));
        Assert.DoesNotContain(tracks, t => t.Kind == MorphTrackKind.Morph);
    }

    [Fact]
    public void Interpolate_PositionSizeRotationColourRadius() => Sta.Run(() =>
    {
        var deck = DeckOperations_New();
        var style = SlideStyle.For(deck, deck.Slides[0]);
        var a = new ShapeElement { Shape = ShapeKind.RoundedRectangle, X = 0, Y = 100, Width = 100, Height = 100, Rotation = 350, Opacity = 0.2, CornerRadius = 10, Fill = Fill.Solid("#000000") };
        var b = new ShapeElement { Shape = ShapeKind.RoundedRectangle, X = 200, Y = 300, Width = 300, Height = 100, Rotation = 10, Opacity = 1, CornerRadius = 40, Fill = Fill.Solid("#FF8040") };
        var mid = (ShapeElement)MorphInterpolator.Interpolate(a, b, 0.5, style, style);
        Assert.Equal(100, mid.X, 6);
        Assert.Equal(200, mid.Y, 6);
        Assert.Equal(200, mid.Width, 6);
        Assert.Equal(0.6, mid.Opacity, 6);
        Assert.Equal(0, ((mid.Rotation % 360) + 360) % 360, 6); // the short way round, through 0
        Assert.Equal(25, mid.CornerRadius, 6);
        var c = SlideStyle.ParseHex(mid.Fill.Color)!.Value;
        Assert.InRange(c.R, 127, 128);
        Assert.InRange(c.G, 64, 65);
        Assert.InRange(c.B, 32, 33);

        var start = MorphInterpolator.Interpolate(a, b, 0, style, style);
        var end = MorphInterpolator.Interpolate(a, b, 1, style, style);
        Assert.Equal(a.Bounds, start.Bounds);
        Assert.Equal(b.Bounds, end.Bounds);
    });

    private static Deck DeckOperations_New() => DeckOperations.NewDeck();

    [Fact]
    public void LerpHelpers()
    {
        Assert.Equal(5, MorphInterpolator.Lerp(0, 10, 0.5));
        Assert.Equal(-10, MorphInterpolator.LerpAngle(10, 330, 0.5), 6);
        Assert.Equal(Color.FromArgb(255, 128, 128, 128), MorphInterpolator.LerpColor(Colors.Black, Colors.White, 0.5002));
        Assert.Equal(new Rect(5, 5, 10, 10), MorphInterpolator.LerpRect(new Rect(0, 0, 0, 0), new Rect(10, 10, 20, 20), 0.5));
    }

    [Fact]
    public void Easing_EndpointsAndMonotonic()
    {
        foreach (var f in new Func<double, double>[] { Easing.Standard, Easing.Decelerate, Easing.Accelerate, Easing.Morph })
        {
            Assert.Equal(0, f(0), 4);
            Assert.Equal(1, f(1), 4);
            var last = -1.0;
            for (var t = 0.0; t <= 1.0001; t += 0.02)
            {
                var v = f(Math.Min(1, t));
                Assert.True(v >= last - 1e-9, $"not monotonic at {t}");
                last = v;
            }
        }
        Assert.InRange(Easing.Morph(0.5), 0.49, 0.51); // symmetric
        Assert.True(Easing.Decelerate(0.2) > 0.2); // fast start
    }

    [Fact]
    public void Builds_StepsAndStates()
    {
        var slide = Slide(
            new ShapeElement { Build = new Build { Effect = BuildEffect.Fade, Step = 2 } },
            new ShapeElement { Build = new Build { Effect = BuildEffect.Rise, Step = 1 } },
            new ShapeElement { Build = new Build { Effect = BuildEffect.Rise, Step = 1, Delay = 0.3 } },
            new ShapeElement());
        Assert.Equal(2, Builds.StepCount(slide));
        Assert.Equal(1, Builds.ClickOf(slide, slide.Elements[1]));
        Assert.Equal(2, Builds.ClickOf(slide, slide.Elements[0]));
        Assert.Equal(0, Builds.State(BuildEffect.Fade, 0).Opacity, 6);
        Assert.Equal(1, Builds.State(BuildEffect.Fade, 1).Opacity, 6);
        Assert.True(Builds.StepDuration(slide, 1) >= 0.3);
    }
}
