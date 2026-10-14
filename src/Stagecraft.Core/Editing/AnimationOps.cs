using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Editing;

/// <summary>
/// Editing a slide's animation list (no UI, no undo: callers wrap them in session edits): adding effects, removing,
/// reordering, and the animation painter.
/// </summary>
public static class AnimationOps
{
    /// <summary>
    /// The animation target for a selection: each ungrouped element by itself, each whole group by its group id (the
    /// group animates as one), unless <paramref name="enteredGroup"/> says the user is working inside that group.
    /// </summary>
    public static List<string> TargetsFor(IEnumerable<Element> selection, string? enteredGroup = null)
    {
        var result = new List<string>();
        foreach (var e in selection)
        {
            var target = e.GroupId != null && e.GroupId != enteredGroup ? e.GroupId : e.Id;
            if (!result.Contains(target)) result.Add(target);
        }
        return result;
    }

    /// <summary>
    /// Adds an effect for each target at the end of the animation order (or after <paramref name="after"/>): the first
    /// starts on a click, the others with it. Motion paths get a default route sized to the element. Returns them.
    /// </summary>
    public static List<AnimationEffect> Add(Slide slide, EffectClass cls, EffectKind kind, IReadOnlyList<string> targets, string? after = null)
    {
        var added = new List<AnimationEffect>();
        var at = after != null ? slide.Animations.FindIndex(a => a.Id == after) + 1 : slide.Animations.Count;
        if (at <= 0) at = slide.Animations.Count;
        foreach (var target in targets)
        {
            var elements = AnimationRules.Resolve(slide, target);
            if (elements.Count == 0) continue;
            if (cls == EffectClass.Media && !elements.Any(e => e is MediaClipElement)) continue;
            var effect = EffectCatalog.Create(cls, kind, target);
            effect.Start = added.Count == 0 ? EffectStart.OnClick : EffectStart.WithPrevious;
            if (cls == EffectClass.Motion)
            {
                var bounds = DeckOperations.BoundsOf(elements);
                effect.Path = EffectCatalog.DefaultPath(kind == EffectKind.Custom ? EffectKind.Line : kind, bounds.Width, bounds.Height);
            }
            if (elements.Count == 1 && TextUnits.BodyOf(elements[0]) is { } body && !body.Content.IsEmpty && kind == EffectKind.Wave) effect.TextUnit = TextUnit.Letter;
            slide.Animations.Insert(at + added.Count, effect);
            added.Add(effect);
        }
        return added;
    }

    /// <summary>Changes the effect of existing animations, keeping their timing (and picking a fitting direction).</summary>
    public static void ChangeEffect(Slide slide, IEnumerable<string> ids, EffectClass cls, EffectKind kind)
    {
        foreach (var a in slide.Animations.Where(x => ids.Contains(x.Id)))
        {
            var sameClass = a.Class == cls;
            a.Class = cls;
            a.Effect = kind;
            if (!EffectCatalog.Directions(kind).Contains(a.Direction)) a.Direction = EffectCatalog.DefaultDirection(kind);
            if (!sameClass || a.Duration <= 0 || EffectCatalog.DefaultDuration(cls, kind) == 0) a.Duration = EffectCatalog.DefaultDuration(cls, kind);
            a.Amount = 0;
            if (cls == EffectClass.Motion)
            {
                if (a.Path == null || kind != EffectKind.Custom)
                {
                    var bounds = DeckOperations.BoundsOf(AnimationRules.Resolve(slide, a.Target));
                    a.Path = EffectCatalog.DefaultPath(kind == EffectKind.Custom ? EffectKind.Line : kind, bounds.Width, bounds.Height);
                }
            }
            else a.Path = null;
        }
    }

    public static int Remove(Slide slide, IEnumerable<string> ids)
    {
        var set = new HashSet<string>(ids);
        return slide.Animations.RemoveAll(a => set.Contains(a.Id));
    }

    /// <summary>
    /// Moves effects (keeping their relative order) so they sit before the effect at <paramref name="insertIndex"/> of
    /// the current list (Count = to the end).
    /// </summary>
    public static void Move(Slide slide, IEnumerable<string> ids, int insertIndex)
    {
        var set = new HashSet<string>(ids);
        var moving = slide.Animations.Where(a => set.Contains(a.Id)).ToList();
        if (moving.Count == 0) return;
        var before = slide.Animations.Take(Math.Max(0, Math.Min(insertIndex, slide.Animations.Count))).Count(a => set.Contains(a.Id));
        var rest = slide.Animations.Where(a => !set.Contains(a.Id)).ToList();
        var at = Math.Max(0, Math.Min(rest.Count, insertIndex - before));
        rest.InsertRange(at, moving);
        slide.Animations = rest;
    }

    /// <summary>Moves effects one place earlier (-1) or later (+1) in the order.</summary>
    public static void Nudge(Slide slide, IEnumerable<string> ids, int direction)
    {
        var set = new HashSet<string>(ids);
        var indexes = slide.Animations.Select((a, i) => (a, i)).Where(x => set.Contains(x.a.Id)).Select(x => x.i).ToList();
        if (indexes.Count == 0) return;
        if (direction < 0 && indexes[0] > 0) Move(slide, set, indexes[0] - 1);
        else if (direction > 0 && indexes[indexes.Count - 1] < slide.Animations.Count - 1) Move(slide, set, indexes[indexes.Count - 1] + 2);
    }

    /// <summary>The animation painter's copy: the effects of one element (or its group), in order.</summary>
    public static List<AnimationEffect> CopyOf(Slide slide, Element element)
    {
        var ids = new HashSet<string> { element.Id };
        if (element.GroupId != null) ids.Add(element.GroupId);
        return slide.Animations.Where(a => ids.Contains(a.Target)).Select(a => a.Clone()).ToList();
    }

    /// <summary>
    /// Pastes copied effects onto targets: their own effects are replaced, the copies go to the end of the order with
    /// the same timing (triggers are dropped: they belong to the original slide). Returns the new effects.
    /// </summary>
    public static List<AnimationEffect> Paint(Slide slide, IReadOnlyList<AnimationEffect> copied, IReadOnlyList<string> targets)
    {
        var added = new List<AnimationEffect>();
        foreach (var target in targets)
        {
            slide.Animations.RemoveAll(a => a.Target == target);
            foreach (var template in copied)
            {
                var copy = template.Clone();
                copy.Id = Ids.New();
                copy.Target = target;
                copy.Trigger = template.Trigger != null && AnimationRules.Resolve(slide, template.Trigger).Count > 0 ? template.Trigger : null;
                slide.Animations.Add(copy);
                added.Add(copy);
            }
        }
        AnimationRules.Prune(slide);
        return added;
    }

    /// <summary>
    /// Before a group goes away (ungroup, or merged into a new group), its effects move to its members: one effect per
    /// member, the first keeping the timing and the rest playing with it. Triggers on the group move to its first member.
    /// </summary>
    public static void SplitGroupEffects(Slide slide, string groupId)
    {
        var members = slide.Elements.Where(e => e.GroupId == groupId).Select(e => e.Id).ToList();
        if (members.Count == 0) return;
        for (var i = 0; i < slide.Animations.Count; i++)
        {
            var a = slide.Animations[i];
            if (a.Trigger == groupId) a.Trigger = members[0];
            if (a.Target != groupId) continue;
            a.Target = members[0];
            for (var m = 1; m < members.Count; m++)
            {
                var copy = a.Clone();
                copy.Id = Ids.New();
                copy.Target = members[m];
                copy.Start = EffectStart.WithPrevious;
                copy.Trigger = a.Trigger;
                slide.Animations.Insert(++i, copy);
            }
        }
    }

    /// <summary>Groups elements and keeps the animations of any groups merged into the new one.</summary>
    public static string Group(Slide slide, IReadOnlyList<Element> elements)
    {
        foreach (var g in elements.Select(e => e.GroupId).Where(g => g != null).Distinct().ToList()) SplitGroupEffects(slide, g!);
        return DeckOperations.Group(elements);
    }

    /// <summary>Ungroups elements; the groups' animations move to the members.</summary>
    public static void Ungroup(Slide slide, IReadOnlyList<Element> elements)
    {
        foreach (var g in elements.Select(e => e.GroupId).Where(g => g != null).Distinct().ToList()) SplitGroupEffects(slide, g!);
        DeckOperations.Ungroup(elements);
    }

    /// <summary>A video or sound's start: Play in the click order (on click or automatically), or only when clicked on.</summary>
    public static MediaStart StartOf(Slide slide, MediaClipElement clip)
    {
        var play = slide.Animations.FirstOrDefault(a => a.Target == clip.Id && a.Class == EffectClass.Media && a.Effect == EffectKind.Play && a.Trigger == null);
        if (play == null) return MediaStart.WhenClicked;
        return play.Start == EffectStart.OnClick ? MediaStart.OnClick : MediaStart.Automatically;
    }

    /// <summary>Sets how a video or sound starts by adding, changing or removing its Play effect.</summary>
    public static void SetStart(Slide slide, MediaClipElement clip, MediaStart start)
    {
        var play = slide.Animations.FirstOrDefault(a => a.Target == clip.Id && a.Class == EffectClass.Media && a.Effect == EffectKind.Play && a.Trigger == null);
        if (start == MediaStart.WhenClicked)
        {
            if (play != null) slide.Animations.Remove(play);
            return;
        }
        if (play == null)
        {
            play = EffectCatalog.Create(EffectClass.Media, EffectKind.Play, clip.Id);
            if (start == MediaStart.Automatically)
            {
                // Plays when the slide appears: first in the order, after anything else that starts with the slide.
                var autoCount = 0;
                while (autoCount < slide.Animations.Count && slide.Animations[autoCount].Start != EffectStart.OnClick && slide.Animations[autoCount].Trigger == null) autoCount++;
                slide.Animations.Insert(autoCount, play);
            }
            else slide.Animations.Add(play);
        }
        play.Start = start == MediaStart.OnClick ? EffectStart.OnClick : EffectStart.AfterPrevious;
        if (start == MediaStart.Automatically)
        {
            // Must not depend on a click: move it before the first "On click".
            var index = slide.Animations.IndexOf(play);
            var firstClick = slide.Animations.FindIndex(a => a.Start == EffectStart.OnClick && a.Trigger == null);
            if (firstClick >= 0 && firstClick < index)
            {
                slide.Animations.RemoveAt(index);
                slide.Animations.Insert(firstClick, play);
            }
        }
    }
}

/// <summary>How a video or sound starts in the slide show.</summary>
public enum MediaStart { WhenClicked, OnClick, Automatically }
