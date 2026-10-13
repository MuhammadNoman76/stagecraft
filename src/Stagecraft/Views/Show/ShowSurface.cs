using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Show;

/// <summary>Draws a prepared WPF drawing (a slide background or one element).</summary>
internal sealed class DrawingHost : FrameworkElement
{
    private readonly Drawing _drawing;

    public DrawingHost(Drawing drawing, double width, double height)
    {
        _drawing = drawing;
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);
}

/// <summary>One slide as separate layers (background + an element each) so builds can animate elements.</summary>
internal sealed class SlideLayer : Canvas
{
    private readonly List<(Element Element, DrawingHost Host, int Click)> _elements = new();

    public SlideLayer(Slide slide, RenderContext ctx)
    {
        Slide = slide;
        var deck = ctx.Deck;
        Width = deck.Width;
        Height = deck.Height;
        IsHitTestVisible = false;
        ClipToBounds = true;
        var style = ctx.StyleFor(slide);

        var bg = new DrawingGroup();
        using (var dc = bg.Open()) DecorPainter.Paint(dc, slide, style, ctx, deck.Width, deck.Height);
        bg.Freeze();
        Children.Add(new DrawingHost(bg, deck.Width, deck.Height));

        foreach (var e in slide.Elements)
        {
            var g = new DrawingGroup();
            using (var dc = g.Open()) ElementRenderer.Draw(dc, e, style, ctx);
            g.Freeze();
            var host = new DrawingHost(g, deck.Width, deck.Height);
            Children.Add(host);
            _elements.Add((e, host, Builds.ClickOf(slide, e)));
        }
    }

    public Slide Slide { get; }

    /// <summary>Shows everything built up to click <paramref name="step"/>, hides the rest (no animation).</summary>
    public void SetStep(int step)
    {
        foreach (var (e, host, click) in _elements) Apply(e, host, click <= step ? BuildState.Shown : BuildState.Hidden);
    }

    /// <summary>The elements that appear on click <paramref name="step"/> with their builds.</summary>
    public IEnumerable<(Element Element, DrawingHost Host)> ElementsOf(int step) =>
        _elements.Where(x => x.Click == step).Select(x => (x.Element, x.Host));

    public static void Apply(Element e, UIElement host, BuildState state)
    {
        host.Opacity = state.Opacity;
        var c = e.Center;
        var group = new TransformGroup();
        if (Math.Abs(state.Scale - 1) > 0.0001) group.Children.Add(new ScaleTransform(state.Scale, state.Scale, c.X, c.Y));
        if (Math.Abs(state.OffsetY) > 0.0001) group.Children.Add(new TranslateTransform(0, state.OffsetY));
        host.RenderTransform = group.Children.Count == 0 ? Transform.Identity : group;
        if (state.Wipe < 0.9999)
        {
            var r = e.RotatedBounds;
            host.Clip = new RectangleGeometry(new Rect(r.X - 4, r.Y - 4000, Math.Max(0, (r.Width + 8) * state.Wipe), 9000));
        }
        else host.Clip = null;
    }
}

/// <summary>
/// Shows slides full size (letterboxed): transitions (fade, push, slide over, zoom, Morph), build animations, ink,
/// the laser pointer and black / white screens. Animations run per frame on the compositor clock.
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
    private readonly Border _blank = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border _end = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    private readonly InkCanvas _ink = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Ellipse _laser = new() { Width = 30, Height = 30, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<Running> _running = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SlideLayer? _current;
    private SlideShowController? _controller;
    private RenderContext? _ctx;
    private bool _ticking;
    private double _scale = 1;

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
        SizeChanged += (_, _) => Layout();
        _ink.StrokeCollected += (_, _) => { };
    }

    /// <summary>The scale from slide units to this surface.</summary>
    public double Scale => _scale;

    public Point ToSlide(Point p)
    {
        var t = _scene.RenderTransform.Value;
        t.Invert();
        return t.Transform(p);
    }

    private Size DeckSize => _ctx == null ? new Size(1920, 1080) : new Size(_ctx.Deck.Width, _ctx.Deck.Height);

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
    }

    // ------------------------------------------------------------------ attach to a show

    public void Attach(SlideShowController controller, bool acceptsInk)
    {
        _controller = controller;
        _ctx = controller.Context;
        Layout();
        controller.SlideChanged += OnSlideChanged;
        controller.StepAdvanced += OnStepAdvanced;
        controller.StepReset += () => _current?.SetStep(controller.Step);
        controller.BlankChanged += UpdateBlank;
        controller.ToolChanged += UpdateTool;
        controller.LaserMoved += UpdateLaser;
        controller.InkCleared += () => { };
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
        ShowSlide(controller.Current, controller.Step);
        UpdateTool();
    }

    private bool _acceptsInk;

    private void ShowSlide(Slide? slide, int step)
    {
        FinishAll();
        if (_current != null) _scene.Children.Remove(_current);
        _current = null;
        if (slide != null && _ctx != null)
        {
            _current = new SlideLayer(slide, _ctx);
            _current.SetStep(step);
            _scene.Children.Insert(0, _current);
            if (_controller != null) _ink.Strokes = _controller.InkFor(slide);
        }
        _end.Visibility = _controller?.AtEnd == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSlideChanged(Slide? from, Slide? to, Arrival arrival)
    {
        if (_controller == null || _ctx == null) return;
        if (_controller.AtEnd)
        {
            FinishAll();
            _end.Visibility = Visibility.Visible;
            return;
        }
        _end.Visibility = Visibility.Collapsed;
        if (to == null) return;
        if (arrival == Arrival.Instant || from == null || to.Transition.Kind == TransitionKind.None)
        {
            ShowSlide(to, _controller.Step);
            return;
        }
        PlayTransition(from, to, _controller.Step);
    }

    private void OnStepAdvanced(int step)
    {
        if (_current == null) return;
        PlayStep(_current, step, null);
    }

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

    /// <summary>Plays a slide's transition (from the previous slide) and then its builds one after another.</summary>
    public void Preview(Slide? from, Slide to, RenderContext ctx, bool transition, bool builds, Action done)
    {
        _ctx = ctx;
        Layout();
        void PlayBuilds()
        {
            if (!builds || _current == null)
            {
                done();
                return;
            }
            var steps = Builds.StepCount(to);
            var step = 0;
            void NextBuild()
            {
                step++;
                if (step > steps || _current == null)
                {
                    After(0.6, done);
                    return;
                }
                PlayStep(_current, step, () => After(0.45, NextBuild));
            }
            After(0.35, NextBuild);
        }
        if (transition && from != null && to.Transition.Kind != TransitionKind.None)
        {
            ShowSlide(from, Builds.StepCount(from));
            PlayTransition(from, to, 0, PlayBuilds);
        }
        else
        {
            ShowSlide(to, 0);
            PlayBuilds();
        }
    }

    private void After(double seconds, Action action) => Add(seconds, _ => { }, action);

    // ------------------------------------------------------------------ animation

    private void Add(double duration, Action<double> apply, Action? done)
    {
        _running.Add(new Running { Start = _clock.Elapsed.TotalSeconds, Duration = Math.Max(0.01, duration), Apply = apply, Done = done });
        if (!_ticking)
        {
            _ticking = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
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
        if (_running.Count == 0 && _ticking)
        {
            _ticking = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    /// <summary>Jumps every running animation to its end.</summary>
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

    private void PlayStep(SlideLayer layer, int step, Action? done)
    {
        var items = layer.ElementsOf(step).ToList();
        if (items.Count == 0)
        {
            done?.Invoke();
            return;
        }
        var total = items.Max(i => Math.Max(0, i.Element.Build!.Delay) + (i.Element.Build.Effect == BuildEffect.Appear ? 0.01 : Math.Max(0.05, i.Element.Build.Duration)));
        foreach (var (element, host) in items) host.CacheMode = new BitmapCache(Math.Max(1, _scale));
        Add(total, t =>
        {
            var seconds = t * total;
            foreach (var (element, host) in items) SlideLayer.Apply(element, host, Builds.StateAt(element.Build!, seconds));
        }, () =>
        {
            foreach (var (element, host) in items)
            {
                SlideLayer.Apply(element, host, BuildState.Shown);
                host.CacheMode = null;
            }
            done?.Invoke();
        });
    }

    private void PlayTransition(Slide from, Slide to, int step, Action? done = null)
    {
        if (_ctx == null) return;
        FinishAll();
        var transition = to.Transition;
        var duration = Math.Max(0.1, transition.Duration);
        var oldLayer = _current;
        var newLayer = new SlideLayer(to, _ctx);
        newLayer.SetStep(step);
        _current = newLayer;
        if (_controller != null) _ink.Strokes = _controller.InkFor(to);
        var size = DeckSize;
        var w = size.Width;
        var h = size.Height;
        var cache = new BitmapCache(Math.Max(1, _scale * VisualTreeHelper.GetDpi(this).DpiScaleX));

        void Cleanup()
        {
            if (oldLayer != null) _scene.Children.Remove(oldLayer);
            newLayer.Opacity = 1;
            newLayer.RenderTransform = Transform.Identity;
            newLayer.CacheMode = null;
            newLayer.Visibility = Visibility.Visible;
            done?.Invoke();
        }

        if (transition.Kind == TransitionKind.Morph)
        {
            var morph = new MorphFrame(from, to, _ctx, _ctx);
            var host = new MorphHost(morph, w, h);
            _scene.Children.Insert(_scene.Children.IndexOf(_ink) is var i && i >= 0 ? i : _scene.Children.Count, host);
            newLayer.Visibility = Visibility.Hidden;
            _scene.Children.Insert(0, newLayer);
            if (oldLayer != null) oldLayer.Visibility = Visibility.Hidden;
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
