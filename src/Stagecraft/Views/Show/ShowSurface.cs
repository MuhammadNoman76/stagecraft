using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Stagecraft.Controls;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Show;

/// <summary>
/// Shows slides full size (letterboxed): transitions (fade, push, slide over, zoom, Morph), animations (click groups,
/// triggers, text pieces, motion paths), links, video and sound with a control bar, ink, the laser pointer and black /
/// white screens. Everything moves per frame on the compositor clock (CompositionTarget.Rendering).
/// </summary>
public sealed class ShowSurface : Grid
{
    private sealed class Running
    {
        public double Start;
        public double Duration;
        public Action<double> Apply = _ => { };
        public Action? Done;
    }

    private readonly Canvas _scene = new() { ClipToBounds = true };
    private readonly Canvas _overlay = new() { IsHitTestVisible = true, Background = null };
    private readonly Border _blank = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border _end = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    private readonly InkCanvas _ink = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Ellipse _laser = new() { Width = 30, Height = 30, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _tip = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 7), MaxWidth = 420 };
    private readonly TextBlock _tipText = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly MediaControlBar _bar;
    private readonly List<Running> _running = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<TimelineItem> _fired = new();
    private SlideLayer? _current;
    private Playhead _playhead = new();
    private List<LinkRegion>? _links;
    private SlideShowController? _controller;
    private RenderContext? _ctx;
    private bool _ticking;
    private bool _animating;
    private double _scale = 1;
    private bool _acceptsInk;

    public ShowSurface()
    {
        Background = Brushes.Black;
        ClipToBounds = true;
        Focusable = false;
        Children.Add(_scene);
        _laser.Fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(255, 255, 70, 70), 0),
                new GradientStop(Color.FromArgb(230, 255, 30, 40), 0.35),
                new GradientStop(Color.FromArgb(90, 255, 0, 0), 0.6),
                new GradientStop(Color.FromArgb(0, 255, 0, 0), 1),
            },
        };
        _end.Child = new TextBlock
        {
            Text = "End of slide show. Click or press a key to exit.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 36, 0, 0),
        };
        Children.Add(_end);
        Children.Add(_blank);
        _tip.Background = new SolidColorBrush(Color.FromArgb(235, 0x20, 0x20, 0x24));
        _tip.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        _tip.BorderThickness = new Thickness(1);
        _tip.Child = _tipText;
        _bar = new MediaControlBar();
        _bar.Visibility = Visibility.Collapsed;
        _bar.Command += OnBarCommand;
        _overlay.Children.Add(_bar);
        _overlay.Children.Add(_tip);
        Children.Add(_overlay);
        Playback = new MediaPlayback(sound: true);
        Playback.StateChanged += host =>
        {
            if (host == _bar.Host) _bar.Refresh();
        };
        SizeChanged += (_, _) => Layout();
        MouseMove += (_, e) => UpdateHover(e.GetPosition(this));
        MouseLeave += (_, _) =>
        {
            HideTip();
            if (!_bar.IsMouseOver) HideBar();
        };
        Unloaded += (_, _) => Shutdown();
    }

    /// <summary>This surface's players and whether they make sound.</summary>
    internal MediaPlayback Playback { get; }

    /// <summary>The scale from slide units to this surface.</summary>
    public double Scale => _scale;

    /// <summary>The players make sound (the audience window); the presenter's copy plays silently.</summary>
    public bool PlaysSound
    {
        get => Playback.Sound;
        set => Playback.Sound = value;
    }

    public Point ToSlide(Point p)
    {
        var t = _scene.RenderTransform.Value;
        t.Invert();
        return t.Transform(p);
    }

    public Point FromSlide(Point p) => _scene.RenderTransform.Value.Transform(p);

    private Size DeckSize => _ctx == null ? new Size(1920, 1080) : new Size(_ctx.Deck.Width, _ctx.Deck.Height);

    private double CacheScale => Math.Max(1, _scale * VisualTreeHelper.GetDpi(this).DpiScaleX);

    private void Layout()
    {
        var size = DeckSize;
        _scene.Width = size.Width;
        _scene.Height = size.Height;
        _ink.Width = size.Width;
        _ink.Height = size.Height;
        _scale = Math.Max(0.01, Math.Min(ActualWidth / size.Width, ActualHeight / size.Height));
        var ox = (ActualWidth - size.Width * _scale) / 2;
        var oy = (ActualHeight - size.Height * _scale) / 2;
        _scene.HorizontalAlignment = HorizontalAlignment.Left;
        _scene.VerticalAlignment = VerticalAlignment.Top;
        _scene.RenderTransform = new MatrixTransform(_scale, 0, 0, _scale, ox, oy);
        if (_bar.Host != null) PlaceBar(_bar.Host);
    }

    // ------------------------------------------------------------------ attach to a show

    public void Attach(SlideShowController controller, bool acceptsInk, bool sound)
    {
        _controller = controller;
        _ctx = controller.Context;
        Playback.Sound = sound;
        Layout();
        controller.SlideChanged += OnSlideChanged;
        controller.StepAdvanced += OnStepAdvanced;
        controller.StepReset += () => Settle(controller.Step);
        controller.TriggerFired += OnTriggerFired;
        controller.MediaCommand += OnMediaCommand;
        controller.BlankChanged += UpdateBlank;
        controller.ToolChanged += UpdateTool;
        controller.LaserMoved += UpdateLaser;
        controller.ShowEnded += Shutdown;
        _scene.Children.Add(_ink);
        _scene.Children.Add(_laser);
        _ink.DefaultDrawingAttributes = SlideShowController.PenAttributes;
        _acceptsInk = acceptsInk;
        MouseMove += (_, e) =>
        {
            if (_controller?.Tool == ShowTool.Laser) _controller.MoveLaser(ToSlide(e.GetPosition(this)));
        };
        MouseLeave += (_, _) =>
        {
            if (_controller?.Tool == ShowTool.Laser) _controller.MoveLaser(null);
        };
        ShowSlide(controller.Current, controller.Step, live: controller.Step == 0);
        UpdateTool();
    }

    /// <summary>Stops every player (the show ended or the window closed).</summary>
    public void Shutdown()
    {
        FinishAll();
        StopTicking();
        if (_current != null)
        {
            _current.StopMedia();
            _current.ReleaseMedia();
        }
        foreach (var carried in Playback.Background.Values.ToList())
        {
            carried.Timer.Stop();
            try
            {
                carried.Player.Stop();
                carried.Player.Close();
            }
            catch
            {
                // closing anyway
            }
        }
        Playback.Background.Clear();
        HideBar();
    }

    private Animator MakeAnimator(Slide slide) => new(SlideTimeline.Build(slide), DeckSize);

    private SlideLayer MakeLayer(Slide slide) => new(slide, _ctx!, _controller?.AnimatorFor(slide) ?? MakeAnimator(slide), Playback);

    /// <summary>Puts a slide on screen. <paramref name="live"/>: its automatic animations play; otherwise it shows <paramref name="step"/> clicks done.</summary>
    private void ShowSlide(Slide? slide, int step, bool live)
    {
        FinishAll();
        LeaveCurrent();
        if (slide != null && _ctx != null)
        {
            _current = MakeLayer(slide);
            _scene.Children.Insert(0, _current);
            if (_controller != null) _ink.Strokes = _controller.InkFor(slide);
            Begin(step, live);
        }
        _end.Visibility = _controller?.AtEnd == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Starts the current slide's animations: live (automatic group now) or settled after <paramref name="step"/> clicks.</summary>
    private void Begin(int step, bool live)
    {
        if (_current == null) return;
        _links = null;
        _fired.Clear();
        var timeline = _current.Animator.Timeline;
        var auto = timeline.HasAutomaticStart ? 1 : 0;
        if (live)
        {
            _playhead = new Playhead();
            if (auto == 1) _playhead.Main.Add(Now);
        }
        else
        {
            _playhead = Playhead.Settled(auto + step);
            // Effects that already happened don't run again (a video doesn't restart when going back).
            foreach (var cue in _current.Animator.DueMedia(_playhead, Now)) _fired.Add(cue.Item);
            foreach (var host in _current.Layers.Select(l => l.Media).OfType<AudioHost>()) host.AdoptBackground();
        }
        Refresh();
    }

    /// <summary>Leaves the current slide: players stop, except background music that plays across slides.</summary>
    private void LeaveCurrent()
    {
        if (_current == null) return;
        foreach (var carried in Playback.Background.Values.ToList())
        {
            if (carried.SlidesLeft == null) continue;
            carried.SlidesLeft--;
            if (carried.SlidesLeft > 0) continue;
            carried.Timer.Stop();
            carried.Player.Stop();
            carried.Player.Close();
            Playback.Background.Remove(carried.Clip.Id);
        }
        _current.StopMedia(m => m is AudioHost a && a.HandOff());
        _current.ReleaseMedia();
        _scene.Children.Remove(_current);
        _current = null;
        HideBar();
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    private void OnSlideChanged(Slide? from, Slide? to, Arrival arrival)
    {
        if (_controller == null || _ctx == null) return;
        if (_controller.AtEnd)
        {
            FinishAll();
            LeaveCurrent();
            _end.Visibility = Visibility.Visible;
            return;
        }
        _end.Visibility = Visibility.Collapsed;
        if (to == null) return;
        if (arrival == Arrival.Instant || from == null || to.Transition.Kind == TransitionKind.None || _current == null)
        {
            ShowSlide(to, _controller.Step, live: _controller.Step == 0);
            return;
        }
        PlayTransition(to, () => Begin(0, live: true));
    }

    private void OnStepAdvanced(int step)
    {
        if (_current == null) return;
        FinishAll();
        var timeline = _current.Animator.Timeline;
        var auto = timeline.HasAutomaticStart ? 1 : 0;
        // The automatic group can't be skipped: if a click comes first, it starts now too.
        while (_playhead.Main.Count < auto + step - 1) _playhead.Main.Add(Now);
        if (_playhead.Main.Count < auto + step) _playhead.Main.Add(Now);
        Refresh();
    }

    private void OnTriggerFired(string elementId, int group)
    {
        if (_current == null) return;
        if (!_playhead.Triggers.TryGetValue(elementId, out var starts)) _playhead.Triggers[elementId] = starts = new List<double>();
        if (group == 0 && starts.Count > 0)
        {
            // Played through: the sequence starts again.
            starts.Clear();
            foreach (var item in _current.Animator.Timeline.Triggers.TryGetValue(elementId, out var groups) ? groups.SelectMany(g => g.Items) : Enumerable.Empty<TimelineItem>())
                _fired.Remove(item);
        }
        while (starts.Count < group) starts.Add(-1e6);
        starts.Add(Now);
        Refresh();
    }

    /// <summary>Back: the state after <paramref name="step"/> clicks, without animating.</summary>
    private void Settle(int step)
    {
        if (_current == null) return;
        FinishAll();
        var auto = _current.Animator.Timeline.HasAutomaticStart ? 1 : 0;
        var keep = auto + step;
        var settled = Playhead.Settled(keep);
        // Media started by clicks that were taken back stop.
        foreach (var cue in _current.Animator.DueMedia(_playhead, Now).Where(c => c.Item.Group.Trigger == null && c.Item.Group.Index >= keep))
            if (_current.LayerOf(cue.Item.Effect.Target)?.Media is { } m) m.Stop();
        _playhead = settled;
        Refresh();
    }

    /// <summary>Applies the animation state now and keeps drawing frames while anything moves.</summary>
    private void Refresh()
    {
        _animating = true;
        ApplyFrame();
        EnsureTicking();
    }

    private void ApplyFrame()
    {
        if (_current == null)
        {
            _animating = false;
            return;
        }
        var now = Now;
        _current.Apply(_playhead, now, CacheScale);
        foreach (var cue in _current.Animator.DueMedia(_playhead, now))
        {
            if (!_fired.Add(cue.Item)) continue;
            foreach (var target in AnimationRules.Resolve(_current.Slide, cue.Item.Effect.Target))
            {
                var host = _current.LayerOf(target.Id)?.Media;
                if (host == null) continue;
                // Through the controller so the presenter's copy follows (silently).
                host.Run(MediaHost.ActionFor(cue.Item.Effect.Effect));
            }
        }
        _animating = _current.Animator.IsRunning(_playhead, now);
    }

    private void OnMediaCommand(string elementId, MediaAction action, double? position)
    {
        var host = _current?.LayerOf(elementId)?.Media;
        host?.Run(action, position);
    }

    // ------------------------------------------------------------------ clicks, links, hover

    /// <summary>
    /// A click on the slide: follows a link, fires a trigger or plays / pauses a video or sound under the pointer.
    /// Returns false when the click should advance the show.
    /// </summary>
    public bool HandleClick(Point surfacePoint)
    {
        if (_current == null || _controller == null || _controller.AtEnd || _controller.Blank != BlankMode.None) return false;
        var p = ToSlide(surfacePoint);
        var link = LinkAt(p);
        if (link != null)
        {
            _controller.FollowLink(link.Link);
            return true;
        }
        var trigger = TriggerAt(p);
        if (trigger != null)
        {
            _controller.FireTrigger(trigger);
            return true;
        }
        var media = MediaAt(p);
        if (media != null)
        {
            _controller.SendMedia(media.Element.Id, MediaAction.Toggle, null);
            return true;
        }
        return false;
    }

    private LinkRegion? LinkAt(Point p)
    {
        if (_current == null || _ctx == null) return null;
        _links ??= LinkRegions.For(_current.Slide, _ctx);
        foreach (var region in _links)
        {
            if (!region.Contains(p)) continue;
            if (_current.LayerOf(region.Element.Id)?.IsShown != true) continue;
            return region;
        }
        return null;
    }

    private string? TriggerAt(Point p)
    {
        if (_current == null) return null;
        var triggers = _current.Animator.Timeline.Triggers;
        if (triggers.Count == 0) return null;
        for (var i = _current.Slide.Elements.Count - 1; i >= 0; i--)
        {
            var e = _current.Slide.Elements[i];
            if (_current.LayerOf(e.Id)?.IsShown != true || !Contains(e, p)) continue;
            if (triggers.ContainsKey(e.Id)) return e.Id;
            if (e.GroupId != null && triggers.ContainsKey(e.GroupId)) return e.GroupId;
        }
        return null;
    }

    private MediaHost? MediaAt(Point p)
    {
        if (_current == null) return null;
        for (var i = _current.Layers.Count - 1; i >= 0; i--)
        {
            var layer = _current.Layers[i];
            if (layer.Media == null || !layer.Media.IsShown || !layer.Media.IsVisible) continue;
            if (Contains(layer.Element, p)) return layer.Media;
        }
        return null;
    }

    private static bool Contains(Element e, Point p)
    {
        var local = Rotate(p, e.Center, -e.Rotation);
        var b = e.Bounds;
        b.Inflate(4, 4);
        return b.Contains(local);
    }

    private static Point Rotate(Point p, Point c, double degrees)
    {
        if (Math.Abs(degrees % 360) < 1e-9) return p;
        var a = degrees * Math.PI / 180;
        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        return new Point(c.X + dx * Math.Cos(a) - dy * Math.Sin(a), c.Y + dx * Math.Sin(a) + dy * Math.Cos(a));
    }

    /// <summary>Screenshots: the pointer over a slide point (link tips, the media bar), without a mouse.</summary>
    internal void HoverForCapture(Point slidePoint) => UpdateHover(FromSlide(slidePoint));

    private void UpdateHover(Point surfacePoint)
    {
        if (_current == null || _controller == null || _controller.Tool != ShowTool.Pointer || _controller.AtEnd)
        {
            HideTip();
            return;
        }
        var p = ToSlide(surfacePoint);
        var link = LinkAt(p);
        var media = link == null ? MediaAt(p) : null;
        var trigger = link == null && media == null ? TriggerAt(p) : null;
        Cursor = link != null || media != null || trigger != null ? Cursors.Hand : null;
        if (link != null)
        {
            var tip = string.IsNullOrWhiteSpace(link.Link.ScreenTip) ? link.Link.Summary : link.Link.ScreenTip!;
            ShowTip(tip, surfacePoint);
        }
        else HideTip();
        if (media != null) ShowBar(media);
        else if (_bar.Host != null && !_bar.IsMouseOver && !Within(_bar.Host, p)) HideBar();
    }

    private bool Within(MediaHost host, Point p)
    {
        var b = host.Element.RotatedBounds;
        b.Inflate(0, 60 / Math.Max(0.1, _scale));
        return b.Contains(p);
    }

    private void ShowTip(string text, Point at)
    {
        _tipText.Text = text;
        _tip.Visibility = Visibility.Visible;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _tip.DesiredSize;
        var x = Math.Min(at.X + 14, Math.Max(0, ActualWidth - size.Width - 6));
        var y = at.Y + 22 + size.Height > ActualHeight ? at.Y - size.Height - 10 : at.Y + 22;
        Canvas.SetLeft(_tip, x);
        Canvas.SetTop(_tip, Math.Max(0, y));
    }

    private void HideTip() => _tip.Visibility = Visibility.Collapsed;

    private void ShowBar(MediaHost host)
    {
        if (_bar.Host != host)
        {
            _bar.Host = host;
            _bar.Refresh();
        }
        PlaceBar(host);
        _bar.Visibility = Visibility.Visible;
    }

    private void PlaceBar(MediaHost host)
    {
        var b = host.Element.RotatedBounds;
        var bottomLeft = FromSlide(new Point(b.Left, b.Bottom));
        var bottomRight = FromSlide(new Point(b.Right, b.Bottom));
        var width = Math.Max(220, Math.Min(520, bottomRight.X - bottomLeft.X - 16));
        _bar.Width = width;
        var x = (bottomLeft.X + bottomRight.X) / 2 - width / 2;
        var y = bottomLeft.Y - 52;
        if (host is AudioHost) y = bottomLeft.Y + 8;
        Canvas.SetLeft(_bar, MathEx.Clamp(x, 4, Math.Max(4, ActualWidth - width - 4)));
        Canvas.SetTop(_bar, MathEx.Clamp(y, 4, Math.Max(4, ActualHeight - 48)));
    }

    private void HideBar()
    {
        _bar.Visibility = Visibility.Collapsed;
        _bar.Host = null;
    }

    private void OnBarCommand(MediaHost host, MediaAction action, double? position)
    {
        if (_controller != null) _controller.SendMedia(host.Element.Id, action, position);
        else host.Run(action, position);
    }

    // ------------------------------------------------------------------ blank, tools

    private void UpdateBlank()
    {
        var mode = _controller?.Blank ?? BlankMode.None;
        if (mode == BlankMode.None)
        {
            _blank.Visibility = Visibility.Collapsed;
            return;
        }
        _blank.Background = mode == BlankMode.Black ? Brushes.Black : Brushes.White;
        _blank.Visibility = Visibility.Visible;
        _blank.Opacity = 0;
        _blank.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
    }

    private void UpdateTool()
    {
        var tool = _controller?.Tool ?? ShowTool.Pointer;
        var drawing = _acceptsInk && tool is ShowTool.Pen or ShowTool.Highlighter or ShowTool.Eraser;
        _ink.IsHitTestVisible = drawing && _acceptsInk;
        _ink.EditingMode = tool switch
        {
            ShowTool.Pen or ShowTool.Highlighter => InkCanvasEditingMode.Ink,
            ShowTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        _ink.DefaultDrawingAttributes = tool == ShowTool.Highlighter ? SlideShowController.HighlighterAttributes : SlideShowController.PenAttributes;
        Cursor = tool == ShowTool.Laser ? Cursors.None : tool is ShowTool.Pen or ShowTool.Highlighter ? Cursors.Pen : null;
        if (tool != ShowTool.Pointer)
        {
            HideTip();
            HideBar();
        }
        UpdateLaser();
    }

    private void UpdateLaser()
    {
        var p = _controller?.Laser;
        if (p == null || _controller?.Tool != ShowTool.Laser)
        {
            _laser.Visibility = Visibility.Collapsed;
            return;
        }
        var size = 30 / Math.Max(0.3, _scale * 1.6) * 1.6;
        _laser.Width = size;
        _laser.Height = size;
        Canvas.SetLeft(_laser, p.Value.X - size / 2);
        Canvas.SetTop(_laser, p.Value.Y - size / 2);
        _laser.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ previews in the editor (no show)

    /// <summary>
    /// Plays a slide's transition (from the previous slide) and / or its animations one click after another, starting
    /// at click <paramref name="fromClick"/> (0 = the beginning). Media stays silent unless <paramref name="sound"/>.
    /// </summary>
    public void Preview(Slide? from, Slide to, RenderContext ctx, bool transition, bool animations, Action done, int fromClick = 0, bool sound = false)
    {
        _ctx = ctx;
        Playback.Sound = sound;
        Layout();
        void PlayAnimations()
        {
            if (!animations || _current == null)
            {
                After(0.4, done);
                return;
            }
            var timeline = _current.Animator.Timeline;
            var auto = timeline.HasAutomaticStart ? 1 : 0;
            var clicks = timeline.Clicks;
            var click = Math.Max(0, Math.Min(fromClick, clicks));
            if (click > 0)
            {
                _playhead = Playhead.Settled(auto + click - 1);
                foreach (var cue in _current.Animator.DueMedia(_playhead, Now)) _fired.Add(cue.Item);
            }
            else if (auto == 1)
            {
                _playhead.Main.Add(Now);
            }
            Refresh();
            void NextClick()
            {
                if (_current == null) return;
                if (_playhead.Main.Count >= auto + clicks)
                {
                    After(Math.Max(0.6, RemainingInCurrentGroup() + 0.4), done);
                    return;
                }
                _playhead.Main.Add(Now);
                Refresh();
                After(RemainingInCurrentGroup() + 0.45, NextClick);
            }
            After(click > 0 ? 0.2 : RemainingInCurrentGroup() + 0.35, NextClick);
        }
        if (transition && from != null && to.Transition.Kind != TransitionKind.None)
        {
            ShowSlide(from, int.MaxValue / 4, live: false);
            PlayTransition(to, () =>
            {
                _playhead = new Playhead();
                _fired.Clear();
                PlayAnimations();
            });
        }
        else
        {
            ShowSlide(to, 0, live: false);
            _playhead = new Playhead();
            _fired.Clear();
            Refresh();
            PlayAnimations();
        }
    }

    /// <summary>Seconds until the latest started group has finished (endless repeats counted once, capped).</summary>
    private double RemainingInCurrentGroup()
    {
        if (_current == null || _playhead.Main.Count == 0) return 0;
        var index = _playhead.Main.Count - 1;
        var group = _current.Animator.Timeline.Groups.ElementAtOrDefault(index);
        if (group == null) return 0;
        var elapsed = Now - _playhead.Main[index];
        return Math.Min(30, Math.Max(0, group.Length - elapsed));
    }

    private void After(double seconds, Action action) => Add(seconds, _ => { }, action);

    // ------------------------------------------------------------------ frames

    private void Add(double duration, Action<double> apply, Action? done)
    {
        _running.Add(new Running { Start = Now, Duration = Math.Max(0.01, duration), Apply = apply, Done = done });
        EnsureTicking();
    }

    private void EnsureTicking()
    {
        if (_ticking) return;
        _ticking = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = Now;
        foreach (var r in _running.ToList())
        {
            var t = Easing.Clamp01((now - r.Start) / r.Duration);
            r.Apply(t);
            if (t >= 1)
            {
                _running.Remove(r);
                r.Done?.Invoke();
            }
        }
        if (_animating) ApplyFrame();
        if (_running.Count == 0 && !_animating) StopTicking();
    }

    /// <summary>Jumps every running transition to its end.</summary>
    public void FinishAll()
    {
        while (_running.Count > 0)
        {
            var list = _running.ToList();
            _running.Clear();
            foreach (var r in list)
            {
                r.Apply(1);
                r.Done?.Invoke();
            }
        }
    }

    // ------------------------------------------------------------------ transitions

    private void PlayTransition(Slide to, Action? done = null)
    {
        if (_ctx == null) return;
        FinishAll();
        var transition = to.Transition;
        var duration = Math.Max(0.1, transition.Duration);
        var oldLayer = _current;
        var oldPlayhead = _playhead;
        var newLayer = MakeLayer(to);
        // The new slide arrives as it starts: elements waiting for an entrance aren't there yet.
        newLayer.Apply(new Playhead(), Now, CacheScale);
        _current = newLayer;
        _links = null;
        _fired.Clear();
        _playhead = new Playhead();
        if (_controller != null) _ink.Strokes = _controller.InkFor(to);
        HideBar();
        var size = DeckSize;
        var w = size.Width;
        var h = size.Height;
        var cache = new BitmapCache(CacheScale);

        void Cleanup()
        {
            if (oldLayer != null)
            {
                oldLayer.StopMedia(m => m is AudioHost a && a.HandOff());
                oldLayer.ReleaseMedia();
                _scene.Children.Remove(oldLayer);
            }
            newLayer.Opacity = 1;
            newLayer.RenderTransform = Transform.Identity;
            newLayer.CacheMode = null;
            newLayer.Visibility = Visibility.Visible;
            done?.Invoke();
        }

        // Background music: count the slide now (not when the transition ends).
        foreach (var carried in Playback.Background.Values.ToList())
        {
            if (carried.SlidesLeft == null) continue;
            carried.SlidesLeft--;
            if (carried.SlidesLeft > 0) continue;
            carried.Timer.Stop();
            carried.Player.Stop();
            carried.Player.Close();
            Playback.Background.Remove(carried.Clip.Id);
        }
        oldLayer?.StopMedia(m => m is AudioHost a && a.Element is AudioElement { AcrossSlides: true } && m.IsPlaying && Playback.Sound);

        if (transition.Kind == TransitionKind.Morph && oldLayer != null)
        {
            var fromAnimator = oldLayer.Animator;
            var toAnimator = newLayer.Animator;
            var now = Now;
            var morph = new MorphFrame(oldLayer.Slide, to, _ctx, _ctx,
                e => fromAnimator.Evaluate(e, oldPlayhead, now).Visible,
                e => !toAnimator.StartsHidden(e.Id));
            var host = new MorphHost(morph, w, h);
            _scene.Children.Insert(_scene.Children.IndexOf(_ink) is var i && i >= 0 ? i : _scene.Children.Count, host);
            newLayer.Visibility = Visibility.Hidden;
            _scene.Children.Insert(0, newLayer);
            oldLayer.Visibility = Visibility.Hidden;
            Add(duration, t => host.Set(Easing.Morph(t)), () =>
            {
                _scene.Children.Remove(host);
                Cleanup();
            });
            return;
        }

        var index = oldLayer != null ? _scene.Children.IndexOf(oldLayer) + 1 : 0;
        _scene.Children.Insert(index, newLayer);
        newLayer.CacheMode = cache;
        if (oldLayer != null) oldLayer.CacheMode = cache;
        var dir = transition.Direction;
        double dx = dir switch { TransitionDirection.Left => 1, TransitionDirection.Right => -1, _ => 0 };
        double dy = dir switch { TransitionDirection.Up => 1, TransitionDirection.Down => -1, _ => 0 };

        Action<double> apply = transition.Kind switch
        {
            TransitionKind.Fade => t => newLayer.Opacity = Easing.Standard(t),
            TransitionKind.Push => t =>
            {
                var e = Easing.Standard(t);
                newLayer.RenderTransform = new TranslateTransform(dx * w * (1 - e), dy * h * (1 - e));
                if (oldLayer != null) oldLayer.RenderTransform = new TranslateTransform(-dx * w * e, -dy * h * e);
            },
            TransitionKind.Slide => t =>
            {
                var e = Easing.Decelerate(t);
                newLayer.RenderTransform = new TranslateTransform(dx * w * (1 - e), dy * h * (1 - e));
                if (oldLayer != null) oldLayer.Opacity = 1 - 0.35 * e;
            },
            TransitionKind.Zoom => t =>
            {
                var e = Easing.Standard(t);
                newLayer.RenderTransform = new ScaleTransform(0.86 + 0.14 * e, 0.86 + 0.14 * e, w / 2, h / 2);
                newLayer.Opacity = Easing.Clamp01(t * 1.4);
                if (oldLayer != null)
                {
                    oldLayer.RenderTransform = new ScaleTransform(1 + 0.08 * e, 1 + 0.08 * e, w / 2, h / 2);
                    oldLayer.Opacity = 1 - e;
                }
            },
            _ => t => newLayer.Opacity = t,
        };
        apply(0);
        Add(duration, apply, Cleanup);
    }

    /// <summary>Draws a Morph frame (re-recorded each frame; vector, so it's sharp at any size).</summary>
    private sealed class MorphHost : FrameworkElement
    {
        private readonly MorphFrame _frame;
        private double _t;

        public MorphHost(MorphFrame frame, double w, double h)
        {
            _frame = frame;
            Width = w;
            Height = h;
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        public void Set(double t)
        {
            _t = t;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            try
            {
                _frame.Draw(dc, _t);
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Morph frame");
            }
        }
    }
}
