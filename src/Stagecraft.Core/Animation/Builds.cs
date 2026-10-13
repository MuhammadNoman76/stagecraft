using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>The look of an element part-way through its build (appear, fade, rise, scale, wipe).</summary>
public readonly struct BuildState
{
    public BuildState(double opacity, double offsetY, double scale, double wipe)
    {
        Opacity = opacity;
        OffsetY = offsetY;
        Scale = scale;
        Wipe = wipe;
    }

    public double Opacity { get; }
    /// <summary>Downward offset in slide units (Rise).</summary>
    public double OffsetY { get; }
    public double Scale { get; }
    /// <summary>Visible fraction from the left (Wipe), 1 = all.</summary>
    public double Wipe { get; }

    public static BuildState Hidden => new(0, 0, 1, 1);
    public static BuildState Shown => new(1, 0, 1, 1);
}

/// <summary>Which click shows which elements, and how a build looks over time.</summary>
public static class Builds
{
    public static bool HasBuild(Element e) => e.Build != null && e.Build.Effect != BuildEffect.None;

    /// <summary>The distinct build steps of a slide in order (their click numbers).</summary>
    public static IReadOnlyList<int> Steps(Slide slide) =>
        slide.Elements.Where(HasBuild).Select(e => e.Build!.Step).Distinct().OrderBy(s => s).ToList();

    /// <summary>How many clicks the slide's builds take.</summary>
    public static int StepCount(Slide slide) => Steps(slide).Count;

    /// <summary>The click (1-based, in order) that shows <paramref name="e"/>; 0 if it is there from the start.</summary>
    public static int ClickOf(Slide slide, Element e)
    {
        if (!HasBuild(e)) return 0;
        var steps = Steps(slide);
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] == e.Build!.Step) return i + 1;
        return 0;
    }

    /// <summary>Seconds from the click until every element of that click has finished building.</summary>
    public static double StepDuration(Slide slide, int click)
    {
        var elements = slide.Elements.Where(e => ClickOf(slide, e) == click).ToList();
        return elements.Count == 0 ? 0 : elements.Max(e => Math.Max(0, e.Build!.Delay) + (e.Build.Effect == BuildEffect.Appear ? 0 : Math.Max(0.05, e.Build.Duration)));
    }

    /// <summary>The state of a build <paramref name="seconds"/> after its click.</summary>
    public static BuildState StateAt(Build build, double seconds)
    {
        var p = build.Effect == BuildEffect.Appear
            ? (seconds >= build.Delay ? 1 : 0)
            : Easing.Clamp01((seconds - Math.Max(0, build.Delay)) / Math.Max(0.05, build.Duration));
        return State(build.Effect, p);
    }

    /// <summary>The look at progress <paramref name="p"/> (0 = hidden, 1 = fully built).</summary>
    public static BuildState State(BuildEffect effect, double p)
    {
        if (p <= 0) return BuildState.Hidden;
        if (p >= 1) return BuildState.Shown;
        var e = Easing.Decelerate(p);
        return effect switch
        {
            BuildEffect.Appear => BuildState.Shown,
            BuildEffect.Fade => new BuildState(Easing.Standard(p), 0, 1, 1),
            BuildEffect.Rise => new BuildState(Easing.Clamp01(p * 1.4), 48 * (1 - e), 1, 1),
            BuildEffect.Scale => new BuildState(Easing.Clamp01(p * 1.5), 0, 0.82 + 0.18 * e, 1),
            BuildEffect.Wipe => new BuildState(1, 0, 1, Easing.Standard(p)),
            _ => BuildState.Shown,
        };
    }
}
