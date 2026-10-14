using System.Diagnostics;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Show;

public enum ShowTool { Pointer, Laser, Pen, Highlighter, Eraser }

public enum BlankMode { None, Black, White }

public sealed class ShowOptions
{
    /// <summary>Test mode: the audience and presenter views are ordinary small windows, never full screen.</summary>
    public bool Windowed { get; set; }

    /// <summary>Only the presenter view (on one screen), e.g. for rehearsing.</summary>
    public bool PresenterOnly { get; set; }

    /// <summary>Record how long each slide is shown.</summary>
    public bool Rehearse { get; set; }
}

/// <summary>How the surfaces should go to the new slide.</summary>
public enum Arrival { Transition, Instant }

/// <summary>
/// The state of a running slide show, shared by the audience and presenter views: which slide and build step,
/// black / white screen, the drawing tool, ink per slide, the laser pointer, the timer and rehearsal timings.
/// </summary>
public sealed class SlideShowController
{
    private readonly Dictionary<string, StrokeCollection> _ink = new();
    private readonly Stopwatch _elapsed = new();
    private readonly Stopwatch _onSlide = new();
    private string _digits = "";
    private readonly DispatcherTimer _digitTimer;

    public SlideShowController(DeckSession session, int startIndex, ShowOptions options)
    {
        Session = session;
        Options = options;
        Slides = session.Deck.Slides.Where(s => !s.Hidden).ToList();
        if (Slides.Count == 0) Slides = session.Deck.Slides.ToList();
        Context = new RenderContext(session.Deck, session.Media);
        var start = session.Deck.Slides.ElementAtOrDefault(startIndex);
        Index = start != null && Slides.Contains(start) ? Slides.IndexOf(start) : 0;
        _digitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _digitTimer.Tick += (_, _) =>
        {
            _digitTimer.Stop();
            _digits = "";
            DigitsChanged?.Invoke();
        };
        _elapsed.Start();
        _onSlide.Start();
    }

    public DeckSession Session { get; }
    public ShowOptions Options { get; }
    public RenderContext Context { get; }
    public List<Slide> Slides { get; }

    public int Index { get; private set; }
    public int Step { get; private set; }

    /// <summary>After the last slide: the black "end of show" screen.</summary>
    public bool AtEnd { get; private set; }

    public Slide? Current => Index >= 0 && Index < Slides.Count ? Slides[Index] : null;
    public Slide? Next => Index + 1 < Slides.Count ? Slides[Index + 1] : null;

    public BlankMode Blank { get; private set; }
    public ShowTool Tool { get; private set; } = ShowTool.Pointer;
    public Point? Laser { get; private set; }
    public bool Ended { get; private set; }

    public string Digits => _digits;

    public TimeSpan Elapsed => _elapsed.Elapsed;
    public bool TimerRunning => _elapsed.IsRunning;
    public Dictionary<string, double> Timings { get; } = new();

    /// <summary>Raised when the slide changes (with how to arrive: animate the transition or cut).</summary>
    public event Action<Slide?, Slide?, Arrival>? SlideChanged;
    /// <summary>Raised when the next build step plays (the step number).</summary>
    public event Action<int>? StepAdvanced;
    /// <summary>Raised when the build state jumps without animation (going back).</summary>
    public event Action? StepReset;
    public event Action? BlankChanged;
    public event Action? ToolChanged;
    public event Action? LaserMoved;
    public event Action? DigitsChanged;
    public event Action? InkCleared;
    public event Action? ShowEnded;

    /// <summary>A trigger element was clicked: (element or group id, which of its groups plays).</summary>
    public event Action<string, int>? TriggerFired;

    /// <summary>A video or sound was told what to do (both windows follow; only the audience's makes sound).</summary>
    internal event Action<string, MediaAction, double?>? MediaCommand;

    /// <summary>A web, e-mail or file link was clicked (the window opens it, asking first for files).</summary>
    public event Action<Hyperlink>? LinkRequested;

    private readonly Dictionary<string, Animator> _animators = new();
    private readonly Dictionary<string, int> _triggerClicks = new();
    private int _lastViewed = -1;

    /// <summary>The animations of a slide, resolved once per show.</summary>
    public Animator AnimatorFor(Slide slide)
    {
        if (!_animators.TryGetValue(slide.Id, out var animator))
            _animators[slide.Id] = animator = new Animator(SlideTimeline.Build(slide), new Size(Session.Deck.Width, Session.Deck.Height));
        return animator;
    }

    /// <summary>How many clicks a slide's animations take.</summary>
    public int StepCount(Slide? slide) => slide == null ? 0 : AnimatorFor(slide).Timeline.Clicks;

    /// <summary>Plays the next group of a trigger element's animations (after the last, they start again).</summary>
    public void FireTrigger(string elementId)
    {
        if (Ended || Current == null) return;
        if (!AnimatorFor(Current).Timeline.Triggers.TryGetValue(elementId, out var groups) || groups.Count == 0) return;
        _triggerClicks.TryGetValue(elementId, out var next);
        if (next >= groups.Count) next = 0;
        _triggerClicks[elementId] = next + 1;
        TriggerFired?.Invoke(elementId, next);
    }

    internal void SendMedia(string elementId, MediaAction action, double? position) => MediaCommand?.Invoke(elementId, action, position);

    /// <summary>Follows a link: slide links jump; web, e-mail and file links go to the window to open.</summary>
    public void FollowLink(Hyperlink link)
    {
        if (Ended) return;
        if (link.Kind != LinkKind.Slide)
        {
            LinkRequested?.Invoke(link);
            return;
        }
        var target = LinkSafety.JumpTarget(link, Slides, Index, _lastViewed);
        if (target >= 0 && target != Index) GoTo(target);
    }

    public StrokeCollection InkFor(Slide slide)
    {
        if (!_ink.TryGetValue(slide.Id, out var strokes)) _ink[slide.Id] = strokes = new StrokeCollection();
        return strokes;
    }

    // ------------------------------------------------------------------ navigation

    public void NextStep()
    {
        if (Ended) return;
        if (Blank != BlankMode.None)
        {
            SetBlank(BlankMode.None);
            return;
        }
        if (AtEnd)
        {
            End();
            return;
        }
        if (Step < StepCount(Current))
        {
            Step++;
            StepAdvanced?.Invoke(Step);
            return;
        }
        if (Index + 1 < Slides.Count)
        {
            Move(Index + 1, 0, Arrival.Transition);
            return;
        }
        // Past the last slide: a black screen, the next click ends the show.
        AtEnd = true;
        RecordTime();
        SlideChanged?.Invoke(Current, null, Arrival.Instant);
    }

    public void PreviousStep()
    {
        if (Ended) return;
        if (Blank != BlankMode.None)
        {
            SetBlank(BlankMode.None);
            return;
        }
        if (AtEnd)
        {
            AtEnd = false;
            SlideChanged?.Invoke(null, Current, Arrival.Instant);
            return;
        }
        if (Step > 0)
        {
            Step--;
            StepReset?.Invoke();
            return;
        }
        if (Index > 0) Move(Index - 1, StepCount(Slides[Index - 1]), Arrival.Instant);
    }

    /// <summary>Jumps to a slide (0-based among the shown slides) with all its builds hidden.</summary>
    public void GoTo(int index)
    {
        if (Ended || Slides.Count == 0) return;
        index = MathEx.Clamp(index, 0, Slides.Count - 1);
        if (Blank != BlankMode.None) SetBlank(BlankMode.None);
        Move(index, 0, index == Index + 1 ? Arrival.Transition : Arrival.Instant);
    }

    private void Move(int index, int step, Arrival arrival)
    {
        var from = AtEnd ? null : Current;
        RecordTime();
        AtEnd = false;
        if (index != Index) _lastViewed = Index;
        _triggerClicks.Clear();
        Index = index;
        Step = step;
        Laser = null;
        SlideChanged?.Invoke(from, Current, arrival);
    }

    private void RecordTime()
    {
        if (Current != null && Options.Rehearse)
        {
            Timings.TryGetValue(Current.Id, out var seconds);
            Timings[Current.Id] = seconds + _onSlide.Elapsed.TotalSeconds;
        }
        _onSlide.Restart();
    }

    // ------------------------------------------------------------------ screen, tools, laser

    public void SetBlank(BlankMode mode)
    {
        Blank = Blank == mode ? BlankMode.None : mode;
        BlankChanged?.Invoke();
    }

    public void SetTool(ShowTool tool)
    {
        Tool = Tool == tool && tool != ShowTool.Pointer ? ShowTool.Pointer : tool;
        if (Tool != ShowTool.Laser && Laser != null)
        {
            Laser = null;
            LaserMoved?.Invoke();
        }
        ToolChanged?.Invoke();
    }

    public void MoveLaser(Point? slidePoint)
    {
        Laser = slidePoint;
        LaserMoved?.Invoke();
    }

    public void ClearInk()
    {
        if (Current != null) InkFor(Current).Clear();
        InkCleared?.Invoke();
    }

    public void ToggleTimer()
    {
        if (_elapsed.IsRunning) _elapsed.Stop();
        else _elapsed.Start();
    }

    public void ResetTimer()
    {
        _elapsed.Reset();
        _elapsed.Start();
    }

    public void End()
    {
        if (Ended) return;
        RecordTime();
        Ended = true;
        _digitTimer.Stop();
        ShowEnded?.Invoke();
    }

    // ------------------------------------------------------------------ keys (presenter clickers send Page Up / Page Down / B)

    public bool HandleKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl)
        {
            switch (key)
            {
                case Key.P: SetTool(ShowTool.Pen); return true;
                case Key.I: SetTool(ShowTool.Highlighter); return true;
                case Key.L: SetTool(ShowTool.Laser); return true;
                case Key.E: SetTool(ShowTool.Eraser); return true;
                case Key.A: SetTool(ShowTool.Pointer); return true;
            }
            return false;
        }

        // Typing a number then Enter jumps to that slide.
        var digit = key switch
        {
            >= Key.D0 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => -1,
        };
        if (digit >= 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (_digits.Length < 4) _digits += digit.ToString();
            _digitTimer.Stop();
            _digitTimer.Start();
            DigitsChanged?.Invoke();
            return true;
        }
        if (key == Key.Enter && _digits.Length > 0)
        {
            var n = int.Parse(_digits);
            _digits = "";
            _digitTimer.Stop();
            DigitsChanged?.Invoke();
            if (n >= 1) GoTo(n - 1);
            return true;
        }
        if (key == Key.Back && _digits.Length > 0)
        {
            _digits = _digits.Substring(0, _digits.Length - 1);
            DigitsChanged?.Invoke();
            return true;
        }

        switch (key)
        {
            case Key.Right:
            case Key.Down:
            case Key.Next:
            case Key.Space:
            case Key.Enter:
            case Key.N:
                NextStep();
                return true;
            case Key.Left:
            case Key.Up:
            case Key.Prior:
            case Key.Back:
            case Key.P:
                PreviousStep();
                return true;
            case Key.Home:
                GoTo(0);
                return true;
            case Key.End:
                GoTo(Slides.Count - 1);
                return true;
            case Key.B:
            case Key.OemPeriod:
                SetBlank(BlankMode.Black);
                return true;
            case Key.W:
            case Key.OemComma:
                SetBlank(BlankMode.White);
                return true;
            case Key.E:
                ClearInk();
                return true;
            case Key.Escape:
                if (Tool != ShowTool.Pointer)
                {
                    SetTool(ShowTool.Pointer);
                    return true;
                }
                End();
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ drawing attributes

    public static DrawingAttributes PenAttributes => new()
    {
        Color = Color.FromRgb(0xF0, 0x3E, 0x5A),
        Width = 6,
        Height = 6,
        FitToCurve = true,
        StylusTip = StylusTip.Ellipse,
    };

    public static DrawingAttributes HighlighterAttributes => new()
    {
        Color = Color.FromRgb(0xFF, 0xE0, 0x33),
        Width = 14,
        Height = 34,
        IsHighlighter = true,
        FitToCurve = true,
        StylusTip = StylusTip.Rectangle,
    };
}
