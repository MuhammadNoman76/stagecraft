using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>One effect placed in time inside its click group.</summary>
public sealed class TimelineItem
{
    public TimelineItem(AnimationEffect effect, int paragraph, int order)
    {
        Effect = effect;
        Paragraph = paragraph;
        Order = order;
    }

    public AnimationEffect Effect { get; }

    /// <summary>-1: the whole element. 0 or more: only this paragraph (each paragraph on its own click).</summary>
    public int Paragraph { get; }

    /// <summary>Position in the slide's animation order (1-based, as the animation pane numbers it).</summary>
    public int Order { get; }

    /// <summary>Seconds after its click group starts (the effect's delay included).</summary>
    public double Begin { get; internal set; }

    /// <summary>One run: the duration plus the stagger of its text pieces.</summary>
    public double Length { get; internal set; }

    /// <summary>Text pieces it runs over one after another (1 when it isn't by paragraph, word or letter).</summary>
    public int Pieces { get; internal set; } = 1;

    /// <summary>Seconds between pieces.</summary>
    public double Stagger { get; internal set; }

    /// <summary>How long it takes before the next "After previous" effect can start (repeats included, endless ones counted once).</summary>
    public double Active => Effect.RepeatUntil != RepeatUntil.None ? Length : Length * Math.Max(1, Effect.Repeat);

    public double End => Begin + Active;

    /// <summary>The group this item belongs to.</summary>
    public ClickGroup Group { get; internal set; } = null!;
}

/// <summary>Effects that start together on one click (or automatically, or on one click of a trigger).</summary>
public sealed class ClickGroup
{
    public ClickGroup(int index, int click, string? trigger)
    {
        Index = index;
        Click = click;
        Trigger = trigger;
    }

    /// <summary>Position in its sequence (0-based).</summary>
    public int Index { get; }

    /// <summary>The click that starts it: 0 = automatically when the slide appears, 1 = the first click…</summary>
    public int Click { get; }

    /// <summary>The element whose click starts it (null: the slide's click order).</summary>
    public string? Trigger { get; }

    public bool Automatic => Click == 0 && Trigger == null;

    public List<TimelineItem> Items { get; } = new();

    /// <summary>Seconds until everything in the group has finished (endless repeats counted once).</summary>
    public double Length => Items.Count == 0 ? 0 : Items.Max(i => i.End);
}

/// <summary>
/// A slide's animations resolved into time: the main sequence as click groups (an optional automatic group 0, then
/// one group per click) and, separately, the groups each trigger element starts. Effects "With previous" start with
/// the effect before them; "After previous" when everything started together before them has finished.
/// </summary>
public sealed class SlideTimeline
{
    private SlideTimeline(Slide slide)
    {
        Slide = slide;
    }

    public Slide Slide { get; }

    /// <summary>The main sequence, in order.</summary>
    public List<ClickGroup> Groups { get; } = new();

    /// <summary>Trigger element id → its groups (each click on the element plays the next).</summary>
    public Dictionary<string, List<ClickGroup>> Triggers { get; } = new();

    /// <summary>How many clicks the main sequence needs.</summary>
    public int Clicks => Groups.Count(g => !g.Automatic);

    public bool HasAutomaticStart => Groups.Count > 0 && Groups[0].Automatic;

    public IEnumerable<TimelineItem> AllItems => Groups.SelectMany(g => g.Items).Concat(Triggers.Values.SelectMany(t => t).SelectMany(g => g.Items));

    /// <summary>The main group that a click number starts (1-based), or null.</summary>
    public ClickGroup? GroupForClick(int click) => Groups.FirstOrDefault(g => g.Click == click);

    /// <summary>Default seconds between paragraphs, words and letters.</summary>
    public static double DefaultStagger(TextUnit unit) => unit switch
    {
        TextUnit.Paragraph => 0.3,
        TextUnit.Word => 0.1,
        TextUnit.Letter => 0.05,
        _ => 0,
    };

    public static SlideTimeline Build(Slide slide)
    {
        var timeline = new SlideTimeline(slide);
        var sequences = new Dictionary<string, Sequence>();
        var main = new Sequence(timeline.Groups, null);
        var order = 0;
        foreach (var effect in slide.Animations)
        {
            if (effect == null || !AnimationRules.IsLive(slide, effect)) continue;
            order++;
            var targets = AnimationRules.Resolve(slide, effect.Target);
            var body = targets.Count == 1 ? TextUnits.BodyOf(targets[0]) : null;
            Sequence seq;
            if (effect.Trigger == null) seq = main;
            else if (!sequences.TryGetValue(effect.Trigger, out seq!))
            {
                var list = new List<ClickGroup>();
                timeline.Triggers[effect.Trigger] = list;
                sequences[effect.Trigger] = seq = new Sequence(list, effect.Trigger);
            }

            // By paragraph with a click each: one item per paragraph (the first keeps the effect's start).
            if (body != null && effect.TextUnit == TextUnit.Paragraph && effect.ParagraphClicks && effect.Class != EffectClass.Media)
            {
                var paragraphs = TextUnits.NonEmptyParagraphs(body);
                if (paragraphs.Count > 0)
                {
                    for (var i = 0; i < paragraphs.Count; i++)
                    {
                        var item = new TimelineItem(effect, paragraphs[i], order) { Length = Math.Max(0, effect.Duration) };
                        seq.Add(item, i == 0 ? effect.Start : EffectStart.OnClick, effect.Delay);
                    }
                    continue;
                }
            }

            var pieces = 1;
            var stagger = 0.0;
            if (body != null && effect.TextUnit != TextUnit.All && effect.Class != EffectClass.Media)
            {
                pieces = Math.Max(1, TextUnits.Count(body, effect.TextUnit));
                stagger = Math.Max(0, effect.TextStagger ?? DefaultStagger(effect.TextUnit));
            }
            var whole = new TimelineItem(effect, -1, order)
            {
                Pieces = pieces,
                Stagger = stagger,
                Length = Math.Max(0, effect.Duration) + (pieces - 1) * stagger,
            };
            seq.Add(whole, effect.Start, effect.Delay);
        }
        return timeline;
    }

    /// <summary>Builds groups for one sequence (the main one or a trigger's).</summary>
    private sealed class Sequence
    {
        private readonly List<ClickGroup> _groups;
        private readonly string? _trigger;
        private ClickGroup? _current;
        private double _parStart;
        private double _parEnd;

        public Sequence(List<ClickGroup> groups, string? trigger)
        {
            _groups = groups;
            _trigger = trigger;
        }

        public void Add(TimelineItem item, EffectStart start, double delay)
        {
            delay = Math.Max(0, delay);
            if (_current == null || start == EffectStart.OnClick)
            {
                // A trigger's first effect always waits for its click; the main sequence's starts by itself unless "On click".
                var click = _trigger != null || start == EffectStart.OnClick
                    ? (_groups.Count == 0 ? 1 : _groups[_groups.Count - 1].Click + 1)
                    : 0;
                _current = new ClickGroup(_groups.Count, click, _trigger);
                _groups.Add(_current);
                _parStart = 0;
                _parEnd = 0;
            }
            else if (start == EffectStart.AfterPrevious)
            {
                _parStart = _parEnd;
            }
            item.Begin = _parStart + delay;
            item.Group = _current;
            _current.Items.Add(item);
            _parEnd = Math.Max(_parEnd, item.End);
        }
    }
}

/// <summary>Counting and finding the paragraphs, words and letters of text (they animate one after another).</summary>
public static class TextUnits
{
    public static TextBody? BodyOf(Element e) => e switch
    {
        TextElement t => t.Text,
        ShapeElement { Text: { } b } => b,
        _ => null,
    };

    /// <summary>The indexes of paragraphs with visible text.</summary>
    public static List<int> NonEmptyParagraphs(TextBody body)
    {
        var list = new List<int>();
        for (var i = 0; i < body.Content.Paragraphs.Count; i++)
            if (body.Content.Paragraphs[i].Text.Trim().Length > 0) list.Add(i);
        return list;
    }

    public static int Count(TextBody body, TextUnit unit)
    {
        switch (unit)
        {
            case TextUnit.Paragraph:
                return NonEmptyParagraphs(body).Count;
            case TextUnit.Word:
                return body.Content.Paragraphs.Sum(p => Ranges(p.Text, TextUnit.Word).Count());
            case TextUnit.Letter:
                return body.Content.Paragraphs.Sum(p => Ranges(p.Text, TextUnit.Letter).Count());
            default:
                return 1;
        }
    }

    /// <summary>Words (runs of non-space characters) or letters (non-space characters) of one paragraph's text.</summary>
    public static IEnumerable<(int Start, int Length)> Ranges(string text, TextUnit unit)
    {
        if (unit == TextUnit.Letter)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]) || text[i] == '​') continue;
                // Keep surrogate pairs (emoji) together.
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return (i, 2);
                    i++;
                    continue;
                }
                yield return (i, 1);
            }
            yield break;
        }
        if (unit == TextUnit.Word)
        {
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var space = i == text.Length || char.IsWhiteSpace(text[i]) || text[i] == '​';
                if (!space && start < 0) start = i;
                else if (space && start >= 0)
                {
                    yield return (start, i - start);
                    start = -1;
                }
            }
            yield break;
        }
        if (text.Trim().Length > 0) yield return (0, text.Length);
    }
}
