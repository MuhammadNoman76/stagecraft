using System.Windows;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>How part of an element is clipped while it wipes, splits or wheels in or out.</summary>
public enum ClipKind { None, Wipe, Split, Wheel }

/// <summary>
/// The look of an element (or one of its text pieces) at a moment of the show: visible or not, opacity, offset, scale
/// and rotation around a centre, a reveal clip, a colour tint and a "bold" amount. Every renderer applies the same state.
/// </summary>
public struct AnimState
{
    public bool Visible;
    public double Opacity;
    public double Dx;
    public double Dy;
    public double ScaleX;
    public double ScaleY;
    public double Rotation;

    /// <summary>Where scale and rotation turn around (slide units).</summary>
    public Point Center;

    public ClipKind Clip;
    public EffectDirection ClipDirection;

    /// <summary>0 = nothing shown, 1 = all shown.</summary>
    public double ClipProgress;

    /// <summary>The box the clip works on (slide units).</summary>
    public Rect ClipBounds;

    /// <summary>0..1 towards <see cref="TintColor"/>.</summary>
    public double Tint;

    public string? TintColor;

    /// <summary>0..1 towards bold text.</summary>
    public double Bold;

    public static AnimState Shown(Point center) => new()
    {
        Visible = true,
        Opacity = 1,
        ScaleX = 1,
        ScaleY = 1,
        Center = center,
    };

    public static AnimState Hidden(Point center)
    {
        var s = Shown(center);
        s.Visible = false;
        return s;
    }

    /// <summary>Nothing changes the element: draw it as on the slide.</summary>
    public bool IsIdentity => Visible && Math.Abs(Opacity - 1) < 1e-6 && Math.Abs(Dx) < 1e-6 && Math.Abs(Dy) < 1e-6 &&
                              Math.Abs(ScaleX - 1) < 1e-6 && Math.Abs(ScaleY - 1) < 1e-6 && Math.Abs(Rotation) < 1e-6 &&
                              Clip == ClipKind.None && Tint < 1e-6 && Bold < 1e-6;

    public bool HasTransform => Math.Abs(Dx) > 1e-6 || Math.Abs(Dy) > 1e-6 || Math.Abs(ScaleX - 1) > 1e-6 || Math.Abs(ScaleY - 1) > 1e-6 || Math.Abs(Rotation) > 1e-6;
}

/// <summary>Which text piece of an element is being drawn (pieces animate one after another).</summary>
public readonly struct PieceRef
{
    public PieceRef(int paragraph, int paragraphOrdinal, int word, int letter, bool isBody = false)
    {
        Paragraph = paragraph;
        ParagraphOrdinal = paragraphOrdinal;
        Word = word;
        Letter = letter;
        IsBody = isBody;
    }

    /// <summary>Index among all paragraphs.</summary>
    public int Paragraph { get; }

    /// <summary>Index among paragraphs with text.</summary>
    public int ParagraphOrdinal { get; }

    /// <summary>Index of the word within the element.</summary>
    public int Word { get; }

    /// <summary>Index of the letter within the element.</summary>
    public int Letter { get; }

    /// <summary>The element's fill and border (not text): animates with the first piece.</summary>
    public bool IsBody { get; }

    public static PieceRef Body => new(0, 0, 0, 0, true);

    public int IndexFor(TextUnit unit) => IsBody ? 0 : unit switch
    {
        TextUnit.Paragraph => ParagraphOrdinal,
        TextUnit.Word => Word,
        TextUnit.Letter => Letter,
        _ => 0,
    };
}

/// <summary>When each group of a slide's animations started (seconds on the show's clock).</summary>
public sealed class Playhead
{
    /// <summary>Start times of the main sequence's groups that have started, in order.</summary>
    public List<double> Main { get; } = new();

    /// <summary>Trigger element id → start times of its groups that have started.</summary>
    public Dictionary<string, List<double>> Triggers { get; } = new();

    /// <summary>How many main groups have started.</summary>
    public int Started => Main.Count;

    public Playhead Clone()
    {
        var copy = new Playhead();
        copy.Main.AddRange(Main);
        foreach (var (k, v) in Triggers) copy.Triggers[k] = v.ToList();
        return copy;
    }

    /// <summary>The state after <paramref name="groups"/> main groups have played to the end (going back, jumping).</summary>
    public static Playhead Settled(int groups)
    {
        var p = new Playhead();
        for (var i = 0; i < groups; i++) p.Main.Add(-1e6);
        return p;
    }
}

/// <summary>
/// Evaluates a slide's animations at a moment: for every element (and text piece) the state its effects give it.
/// Effects apply in the order they started; entrances start hidden, exits end hidden, emphasis and motion keep their
/// end state (spin angle, size, transparency, position) unless they rewind.
/// </summary>
public sealed class Animator
{
    private readonly SlideTimeline _timeline;
    private readonly Size _slide;
    private readonly Dictionary<string, List<TimelineItem>> _byElement = new();
    private readonly Dictionary<string, bool> _startsHidden = new();
    private readonly Dictionary<AnimationEffect, Rect> _targetBounds = new();

    public Animator(SlideTimeline timeline, Size slideSize)
    {
        _timeline = timeline;
        _slide = slideSize;
        var slide = timeline.Slide;
        foreach (var item in timeline.AllItems)
        {
            var targets = AnimationRules.Resolve(slide, item.Effect.Target);
            if (!_targetBounds.ContainsKey(item.Effect)) _targetBounds[item.Effect] = Union(targets);
            foreach (var e in targets)
            {
                if (!_byElement.TryGetValue(e.Id, out var list)) _byElement[e.Id] = list = new List<TimelineItem>();
                list.Add(item);
            }
        }
        // An element whose first entrance / exit (in the animation order) is an entrance starts hidden.
        foreach (var (id, items) in _byElement)
        {
            var first = items.OrderBy(i => i.Order).FirstOrDefault(i => i.Effect.Class is EffectClass.Entrance or EffectClass.Exit);
            _startsHidden[id] = first?.Effect.Class == EffectClass.Entrance;
        }
    }

    public SlideTimeline Timeline => _timeline;

    /// <summary>Elements with at least one effect.</summary>
    public IEnumerable<string> AnimatedElements => _byElement.Keys;

    public bool IsAnimated(string elementId) => _byElement.ContainsKey(elementId);

    public bool StartsHidden(string elementId) => _startsHidden.TryGetValue(elementId, out var h) && h;

    /// <summary>The finest text unit any effect on the element uses (pieces are made of it), or All.</summary>
    public TextUnit PieceUnit(string elementId)
    {
        if (!_byElement.TryGetValue(elementId, out var items)) return TextUnit.All;
        var unit = TextUnit.All;
        foreach (var i in items)
        {
            var u = i.Paragraph >= 0 ? TextUnit.Paragraph : i.Pieces > 1 ? i.Effect.TextUnit : TextUnit.All;
            if (u > unit) unit = u;
        }
        return unit;
    }

    private static Rect Union(IEnumerable<Element> elements)
    {
        var r = Rect.Empty;
        foreach (var e in elements) r.Union(e.RotatedBounds);
        return r.IsEmpty ? new Rect(0, 0, 1, 1) : r;
    }

    // ------------------------------------------------------------------ evaluation

    /// <summary>
    /// The state of <paramref name="element"/> (or one of its pieces) at <paramref name="now"/> seconds on the clock
    /// the <paramref name="playhead"/>'s start times use.
    /// </summary>
    public AnimState Evaluate(Element element, Playhead playhead, double now, PieceRef? piece = null)
    {
        var center = element.Center;
        if (!_byElement.TryGetValue(element.Id, out var items)) return AnimState.Shown(center);
        var state = StartsHidden(element.Id) ? AnimState.Hidden(center) : AnimState.Shown(center);

        // Every item that has started, with its start on the clock, in the order they started.
        var started = new List<(TimelineItem Item, double Start, bool Past)>();
        foreach (var item in items)
        {
            var group = item.Group;
            List<double>? starts = group.Trigger == null ? playhead.Main : playhead.Triggers.TryGetValue(group.Trigger, out var t) ? t : null;
            if (starts == null || group.Index >= starts.Count) continue;
            // Past: a later group of the same sequence has started (its effects were finished off by the click).
            var past = group.Index < starts.Count - 1;
            started.Add((item, starts[group.Index] + item.Begin, past));
        }
        foreach (var (item, start, past) in started.OrderBy(s => s.Start).ThenBy(s => s.Item.Order))
        {
            if (item.Effect.Class == EffectClass.Media) continue;
            if (item.Paragraph >= 0)
            {
                // Paragraph by paragraph: each item moves only its paragraph (the fill and border go with the first).
                var applies = piece is { } pr ? (pr.IsBody ? IsFirstParagraphItem(item, element) : pr.Paragraph == item.Paragraph) : IsFirstParagraphItem(item, element);
                if (!applies) continue;
            }
            var offset = 0.0;
            if (piece is { } p && item.Pieces > 1) offset = p.IndexFor(item.Effect.TextUnit) * item.Stagger;
            var local = now - start - offset;
            state = Apply(state, item, local, past, element);
        }
        return state;
    }

    /// <summary>The body (fill, border) of an element animated paragraph by paragraph follows the first paragraph.</summary>
    private static bool IsFirstParagraphItem(TimelineItem item, Element element)
    {
        var body = TextUnits.BodyOf(element);
        if (body == null) return true;
        var first = TextUnits.NonEmptyParagraphs(body).FirstOrDefault();
        return item.Paragraph == first;
    }

    /// <summary>Whether something is still moving at <paramref name="now"/> (the show keeps drawing frames until not).</summary>
    public bool IsRunning(Playhead playhead, double now)
    {
        foreach (var item in _timeline.AllItems)
        {
            var group = item.Group;
            var starts = group.Trigger == null ? playhead.Main : playhead.Triggers.TryGetValue(group.Trigger, out var t) ? t : null;
            if (starts == null || group.Index >= starts.Count) continue;
            var past = group.Index < starts.Count - 1;
            var local = now - starts[group.Index] - item.Begin;
            if (item.Effect.Class == EffectClass.Media)
            {
                if (!past && local < 0) return true; // waiting to fire
                continue;
            }
            if (item.Effect.RepeatUntil == RepeatUntil.EndOfSlide) return true;
            if (past) continue;
            if (item.Effect.RepeatUntil == RepeatUntil.NextClick) return true;
            if (local < item.Active + 0.001) return true;
        }
        return false;
    }

    private AnimState Apply(AnimState state, TimelineItem item, double local, bool past, Element element)
    {
        var effect = item.Effect;
        var duration = Math.Max(0, effect.Duration);
        var endless = effect.RepeatUntil == RepeatUntil.EndOfSlide || (effect.RepeatUntil == RepeatUntil.NextClick && !past);
        if (local < 0 && !past) return state; // not started yet
        if (past && !endless) return End(state, item, element);
        if (local < 0) local = 0;
        if (duration <= 1e-6) return End(state, item, element);

        var cycles = effect.RepeatUntil != RepeatUntil.None ? int.MaxValue : Math.Max(1, effect.Repeat);
        var cycle = (int)Math.Floor(local / duration);
        if (!endless && cycle >= cycles) return End(state, item, element);
        var p = (local - cycle * duration) / duration;
        return Look(state, item, Easing.Clamp01(p), element);
    }

    /// <summary>The state once the effect has finished.</summary>
    private AnimState End(AnimState state, TimelineItem item, Element element)
    {
        var effect = item.Effect;
        switch (effect.Class)
        {
            case EffectClass.Entrance:
                state.Visible = !effect.Rewind;
                ResetLook(ref state);
                return state;
            case EffectClass.Exit:
                state.Visible = effect.Rewind;
                ResetLook(ref state);
                return state;
            case EffectClass.Motion:
                if (!effect.Rewind && effect.Path != null)
                {
                    var v = PathMath.Offset(effect.Path, 1);
                    state.Dx += v.X;
                    state.Dy += v.Y;
                }
                return state;
            case EffectClass.Emphasis:
                if (effect.Rewind) return state;
                switch (effect.Effect)
                {
                    case EffectKind.Spin:
                        state.Rotation += effect.EffectiveAmount;
                        break;
                    case EffectKind.Grow:
                        var k = Math.Max(0.01, effect.EffectiveAmount / 100);
                        state.ScaleX *= k;
                        state.ScaleY *= k;
                        break;
                    case EffectKind.Transparency:
                        state.Opacity *= 1 - MathEx.Clamp(effect.EffectiveAmount, 0, 100) / 100;
                        break;
                }
                return state;
            default:
                return state;
        }
    }

    private static void ResetLook(ref AnimState state)
    {
        state.Clip = ClipKind.None;
        state.ClipProgress = 1;
    }

    /// <summary>The state part-way (<paramref name="p"/> 0..1, not eased yet) through one run of the effect.</summary>
    private AnimState Look(AnimState state, TimelineItem item, double p, Element element)
    {
        var effect = item.Effect;
        var bounds = _targetBounds.TryGetValue(effect, out var b) ? b : element.RotatedBounds;
        var groupCenter = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        switch (effect.Class)
        {
            case EffectClass.Entrance:
            {
                state.Visible = true;
                var e = Ease(effect, p, entering: true);
                EntranceLook(ref state, effect, e, p, bounds, groupCenter, exit: false);
                return state;
            }
            case EffectClass.Exit:
            {
                state.Visible = true;
                // An exit is its entrance played backwards (accelerating out instead of decelerating in).
                var e = 1 - Ease(effect, p, entering: false);
                EntranceLook(ref state, effect, e, 1 - p, bounds, groupCenter, exit: true);
                return state;
            }
            case EffectClass.Emphasis:
                EmphasisLook(ref state, effect, p, groupCenter);
                return state;
            case EffectClass.Motion:
                if (effect.Path != null)
                {
                    var v = PathMath.Offset(effect.Path, Ease(effect, p, entering: null));
                    state.Dx += v.X;
                    state.Dy += v.Y;
                }
                return state;
            default:
                return state;
        }
    }

    /// <summary>The effect's easing: its own choice, or the natural curve (decelerate in, accelerate out, smooth otherwise).</summary>
    public static double Ease(AnimationEffect effect, double p, bool? entering)
    {
        return effect.Easing switch
        {
            EffectEasing.Linear => p,
            EffectEasing.EaseIn => Easing.Accelerate(p),
            EffectEasing.EaseOut => Easing.Decelerate(p),
            EffectEasing.EaseInOut => Easing.InOut(p),
            EffectEasing.Overshoot => Easing.CubicBezier(0.34, 1.56, 0.64, 1.0, p),
            _ => entering switch
            {
                true => Easing.Decelerate(p),
                false => Easing.Accelerate(p),
                _ => Easing.InOut(p),
            },
        };
    }

    /// <summary>
    /// How an entrance looks at eased progress <paramref name="e"/> (0 = not in yet, 1 = in place); <paramref name="raw"/>
    /// is the uneased progress for effects with their own rhythm (bounce, swivel).
    /// </summary>
    private void EntranceLook(ref AnimState s, AnimationEffect effect, double e, double raw, Rect bounds, Point center, bool exit)
    {
        var w = _slide.Width;
        var h = _slide.Height;
        switch (effect.Effect)
        {
            case EffectKind.Appear:
                if (raw <= 0) s.Visible = false;
                break;
            case EffectKind.Fade:
                s.Opacity *= Easing.Clamp01(e);
                break;
            case EffectKind.Fly:
            {
                var (dx, dy) = OffSlide(effect.Direction, bounds, w, h);
                s.Dx += dx * (1 - e);
                s.Dy += dy * (1 - e);
                break;
            }
            case EffectKind.Float:
            {
                var d = h * 0.1 * (effect.Direction == EffectDirection.Top ? -1 : 1);
                s.Dy += d * (1 - e);
                s.Opacity *= Easing.Clamp01(e);
                break;
            }
            case EffectKind.Wipe:
                // Out: the named side disappears first (the wipe runs on in the same direction).
                s.Clip = ClipKind.Wipe;
                s.ClipDirection = exit ? Opposite(effect.Direction) : effect.Direction;
                s.ClipProgress = Easing.Clamp01(e);
                s.ClipBounds = bounds;
                break;
            case EffectKind.Split:
                s.Clip = ClipKind.Split;
                s.ClipDirection = effect.Direction;
                s.ClipProgress = Easing.Clamp01(e);
                s.ClipBounds = bounds;
                break;
            case EffectKind.Zoom:
            {
                var from = MathEx.Clamp(effect.EffectiveAmount / 100, 0.01, 0.99);
                Scale(ref s, from + (1 - from) * e, center);
                s.Opacity *= Easing.Clamp01(e * 1.6);
                break;
            }
            case EffectKind.GrowTurn:
                Scale(ref s, 0.08 + 0.92 * e, center);
                Rotate(ref s, -90 * (1 - e), center);
                s.Opacity *= Easing.Clamp01(e * 2);
                break;
            case EffectKind.Bounce:
            {
                // Drops in from above and bounces to rest (its own rhythm; the easing choice doesn't apply).
                var drop = Math.Min(bounds.Bottom + 20, h * 0.35);
                s.Dy += -drop * (1 - BounceOut(raw));
                s.Opacity *= Easing.Clamp01(raw * 5);
                break;
            }
            case EffectKind.Wheel:
                s.Clip = ClipKind.Wheel;
                s.ClipProgress = Easing.Clamp01(e);
                s.ClipBounds = bounds;
                break;
            case EffectKind.Swivel:
            {
                var k = Math.Abs(Math.Cos((1 - e) * Math.PI * 2.5));
                ScaleXY(ref s, Math.Max(0.02, k), 1, center);
                s.Opacity *= Easing.Clamp01(raw * 2);
                break;
            }
            case EffectKind.Rise:
            {
                s.Dy += h * 0.045 * (1 - e);
                s.Opacity *= Easing.Clamp01(raw * 1.4);
                break;
            }
        }
    }

    public static EffectDirection Opposite(EffectDirection d) => d switch
    {
        EffectDirection.Left => EffectDirection.Right,
        EffectDirection.Right => EffectDirection.Left,
        EffectDirection.Top => EffectDirection.Bottom,
        EffectDirection.Bottom => EffectDirection.Top,
        _ => d,
    };

    /// <summary>The offset that puts <paramref name="bounds"/> just outside the slide on the given side(s).</summary>
    public static (double Dx, double Dy) OffSlide(EffectDirection d, Rect bounds, double w, double h)
    {
        double dx = 0, dy = 0;
        if (d is EffectDirection.Bottom or EffectDirection.BottomLeft or EffectDirection.BottomRight) dy = h - bounds.Top + 8;
        if (d is EffectDirection.Top or EffectDirection.TopLeft or EffectDirection.TopRight) dy = -bounds.Bottom - 8;
        if (d is EffectDirection.Left or EffectDirection.BottomLeft or EffectDirection.TopLeft) dx = -bounds.Right - 8;
        if (d is EffectDirection.Right or EffectDirection.BottomRight or EffectDirection.TopRight) dx = w - bounds.Left + 8;
        return (dx, dy);
    }

    private void EmphasisLook(ref AnimState s, AnimationEffect effect, double p, Point center)
    {
        var e = Ease(effect, p, entering: null);
        switch (effect.Effect)
        {
            case EffectKind.Pulse:
                Scale(ref s, 1 + 0.08 * Math.Sin(Math.PI * e), center);
                break;
            case EffectKind.Spin:
                Rotate(ref s, effect.EffectiveAmount * e, center);
                break;
            case EffectKind.Grow:
                Scale(ref s, 1 + (Math.Max(0.01, effect.EffectiveAmount / 100) - 1) * e, center);
                break;
            case EffectKind.Teeter:
                Rotate(ref s, 8 * Math.Sin(4 * Math.PI * p) * (1 - 0.5 * p), center);
                break;
            case EffectKind.ColorPulse:
                s.Tint = Math.Max(s.Tint, 0.85 * Math.Sin(Math.PI * e));
                s.TintColor = effect.Color ?? "accent2";
                break;
            case EffectKind.Transparency:
                s.Opacity *= 1 - MathEx.Clamp(effect.EffectiveAmount, 0, 100) / 100 * e;
                break;
            case EffectKind.BoldFlash:
                s.Bold = Math.Max(s.Bold, Math.Sin(Math.PI * p));
                break;
            case EffectKind.Wave:
                s.Dy += -_slide.Height * 0.03 * Math.Sin(Math.PI * p);
                break;
        }
    }

    private static void Scale(ref AnimState s, double k, Point center) => ScaleXY(ref s, k, k, center);

    private static void ScaleXY(ref AnimState s, double kx, double ky, Point center)
    {
        s.ScaleX *= kx;
        s.ScaleY *= ky;
        s.Center = center;
    }

    private static void Rotate(ref AnimState s, double degrees, Point center)
    {
        s.Rotation += degrees;
        s.Center = center;
    }

    /// <summary>Standard "bounce out" curve: falls and bounces three times, ending at 1.</summary>
    public static double BounceOut(double t)
    {
        t = Easing.Clamp01(t);
        const double n = 7.5625, d = 2.75;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d)
        {
            t -= 1.5 / d;
            return n * t * t + 0.75;
        }
        if (t < 2.5 / d)
        {
            t -= 2.25 / d;
            return n * t * t + 0.9375;
        }
        t -= 2.625 / d;
        return n * t * t + 0.984375;
    }

    // ------------------------------------------------------------------ media cues

    /// <summary>A Play / Pause / Stop effect that is due.</summary>
    public readonly struct MediaCue
    {
        public MediaCue(TimelineItem item, double due)
        {
            Item = item;
            Due = due;
        }

        public TimelineItem Item { get; }
        public double Due { get; }
    }

    /// <summary>Media effects whose time has come at <paramref name="now"/> (the caller remembers which it already ran).</summary>
    public IEnumerable<MediaCue> DueMedia(Playhead playhead, double now)
    {
        foreach (var item in _timeline.AllItems)
        {
            if (item.Effect.Class != EffectClass.Media) continue;
            var group = item.Group;
            var starts = group.Trigger == null ? playhead.Main : playhead.Triggers.TryGetValue(group.Trigger, out var t) ? t : null;
            if (starts == null || group.Index >= starts.Count) continue;
            var due = starts[group.Index] + item.Begin;
            var past = group.Index < starts.Count - 1;
            if (past || now >= due) yield return new MediaCue(item, due);
        }
    }
}
