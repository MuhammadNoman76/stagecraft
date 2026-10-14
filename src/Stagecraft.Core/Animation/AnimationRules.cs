using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>Housekeeping for a slide's animation list: targets, repairs, copying with new ids.</summary>
public static class AnimationRules
{
    /// <summary>The elements an effect animates: the element with the target id, or every member of a group with that id.</summary>
    public static List<Element> Resolve(Slide slide, string? target)
    {
        if (string.IsNullOrEmpty(target)) return new List<Element>();
        var single = slide.Find(target!);
        if (single != null) return new List<Element> { single };
        return slide.Elements.Where(e => e.GroupId == target).ToList();
    }

    /// <summary>Whether an effect's target (and trigger) still exist on the slide.</summary>
    public static bool IsLive(Slide slide, AnimationEffect effect)
    {
        if (Resolve(slide, effect.Target).Count == 0) return false;
        if (effect.Trigger != null && Resolve(slide, effect.Trigger).Count == 0) return false;
        if (effect.Class == EffectClass.Media && !Resolve(slide, effect.Target).Any(e => e is MediaClipElement)) return false;
        return true;
    }

    /// <summary>The effects that animate any of <paramref name="elements"/> (or their groups), in slide order.</summary>
    public static List<AnimationEffect> EffectsOf(Slide slide, IEnumerable<Element> elements)
    {
        var ids = new HashSet<string>();
        foreach (var e in elements)
        {
            ids.Add(e.Id);
            if (e.GroupId != null) ids.Add(e.GroupId);
        }
        return slide.Animations.Where(a => ids.Contains(a.Target)).ToList();
    }

    /// <summary>Removes effects whose element (or trigger) is gone, e.g. after deleting elements.</summary>
    public static int Prune(Slide slide) => slide.Animations.RemoveAll(a => a == null || !IsLive(slide, a));

    /// <summary>Fixes impossible values of a loaded slide's animations and drops the ones that can't play.</summary>
    public static void Repair(Slide slide)
    {
        slide.Animations.RemoveAll(a => a == null);
        var ids = new HashSet<string>();
        foreach (var a in slide.Animations)
        {
            if (string.IsNullOrEmpty(a.Id) || !ids.Add(a.Id))
            {
                a.Id = Ids.New();
                ids.Add(a.Id);
            }
            a.Target ??= "";
            if (!EffectCatalog.Belongs(a.Class, a.Effect)) a.Effect = EffectCatalog.KindsOf(a.Class)[0];
            if (!Sane(a.Delay) || a.Delay < 0) a.Delay = 0;
            a.Delay = Math.Min(a.Delay, 600);
            if (!Sane(a.Duration) || a.Duration < 0) a.Duration = EffectCatalog.DefaultDuration(a.Class, a.Effect);
            a.Duration = Math.Min(a.Duration, 600);
            if (a.Repeat < 1) a.Repeat = 1;
            a.Repeat = Math.Min(a.Repeat, 999);
            if (a.TextStagger is { } s && (!Sane(s) || s < 0)) a.TextStagger = null;
            if (!Sane(a.Amount)) a.Amount = 0;
            if (a.Class == EffectClass.Motion)
            {
                a.Path ??= EffectCatalog.DefaultPath(a.Effect, 200, 200);
                a.Path.Points ??= new List<PathPoint>();
                a.Path.Points.RemoveAll(p => p == null || !Sane(p.X) || !Sane(p.Y));
                if (a.Path.Points.Count < 2) a.Path = EffectCatalog.DefaultPath(a.Effect == EffectKind.Custom ? EffectKind.Line : a.Effect, 200, 200);
            }
            else a.Path = null;
            if (a.Class == EffectClass.Media) a.Duration = 0;
        }
        Prune(slide);
    }

    /// <summary>
    /// Copies of <paramref name="effects"/> for copied elements: targets (and triggers) go through
    /// <paramref name="idMap"/> (old element or group id → new id); effects whose target wasn't copied are left out.
    /// </summary>
    public static List<AnimationEffect> CopyFor(IEnumerable<AnimationEffect> effects, IReadOnlyDictionary<string, string> idMap)
    {
        var result = new List<AnimationEffect>();
        foreach (var a in effects)
        {
            if (!idMap.TryGetValue(a.Target, out var target)) continue;
            var copy = a.Clone();
            copy.Id = Ids.New();
            copy.Target = target;
            copy.Trigger = a.Trigger != null && idMap.TryGetValue(a.Trigger, out var trigger) ? trigger : null;
            result.Add(copy);
        }
        return result;
    }

    /// <summary>
    /// The number shown on an element's badge in the editor: the click that first animates it (0 = starts with the
    /// slide; null = not animated, or only by a trigger).
    /// </summary>
    public static int? BadgeOf(Slide slide, Element element)
    {
        var timeline = SlideTimeline.Build(slide);
        foreach (var group in timeline.Groups)
            foreach (var item in group.Items)
                if (Resolve(slide, item.Effect.Target).Contains(element))
                    return group.Click;
        return null;
    }

    private static bool Sane(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
