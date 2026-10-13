using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The slide editing surface: draws the current slide at any zoom, and lets people select, move, resize, rotate,
/// snap, nudge, edit text in place and drop pictures. Pans and zooms like a design tool (Ctrl + wheel, Space + drag).
/// </summary>
public sealed partial class SlideCanvas : Grid
{
    private readonly Canvas _viewport = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Border _shadow = new() { Background = Brushes.White, IsHitTestVisible = false };
    private readonly Canvas _stage = new() { ClipToBounds = false };
    private readonly SlideBackgroundVisual _background = new();
    private readonly SelectionOverlay _overlay;
    private readonly Dictionary<string, ElementVisual> _visuals = new();
    private EditorState? _state;
    private Vector _offset;
    private bool _fit = true;

    public SlideCanvas()
    {
        _overlay = new SelectionOverlay(this);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        AllowDrop = true;
        Background = Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(this, "Slide editor");
        System.Windows.Automation.AutomationProperties.SetHelpText(this,
            "Arrow keys move the selection, Tab selects the next element, Enter edits text, Delete removes.");

        _shadow.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.18, RenderingBias = RenderingBias.Performance };
        _stage.Children.Add(_background);
        _viewport.Children.Add(_shadow);
        _viewport.Children.Add(_stage);
        _viewport.Children.Add(_overlay);
        Children.Add(_viewport);

        SizeChanged += (_, _) =>
        {
            _overlay.Width = ActualWidth;
            _overlay.Height = ActualHeight;
            if (_fit) FitToWindow();
            else ClampOffset();
            ApplyTransform();
        };
        InitInput();
    }

    public EditorState? State => _state;

    public double Zoom { get; private set; } = 0.5;

    public bool IsFit => _fit;

    /// <summary>Zoom or pan changed.</summary>
    public event Action? ViewChanged;

    /// <summary>Wheel over the slide at "fit" zoom: go to the next (+1) or previous (-1) slide.</summary>
    public event Action<int>? SlideWheel;

    /// <summary>Double-click on a chart, table, icon or empty picture: open its editor.</summary>
    public event Action<Element>? ElementActivated;

    /// <summary>Pictures dropped (bytes + extension) at a slide position, optionally onto an image element.</summary>
    public event Action<List<(byte[] Data, string Extension)>, Point, ImageElement?>? ImagesDropped;

    /// <summary>A right-click on the canvas: the editor builds the context menu.</summary>
    public event Action<Point>? ContextRequested;

    public void Attach(EditorState state)
    {
        _state = state;
        state.SelectionChanged += () => _overlay.InvalidateVisual();
        Refresh();
    }

    // ------------------------------------------------------------------ coordinates

    public Point ToView(Point slide) => new(slide.X * Zoom + _offset.X, slide.Y * Zoom + _offset.Y);

    public Rect ToView(Rect slide) => new(ToView(slide.TopLeft), ToView(slide.BottomRight));

    public Point ToSlide(Point view) => new((view.X - _offset.X) / Zoom, (view.Y - _offset.Y) / Zoom);

    public static Point Rotate(Point p, Point center, double degrees)
    {
        if (Math.Abs(degrees % 360) < 1e-9) return p;
        var a = degrees * Math.PI / 180;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    // ------------------------------------------------------------------ zoom

    private Size SlideSize => _state == null ? new Size(1920, 1080) : new Size(_state.Deck.Width, _state.Deck.Height);

    public double FitZoom()
    {
        var size = SlideSize;
        var w = Math.Max(50, ActualWidth - 96);
        var h = Math.Max(50, ActualHeight - 96);
        return Math.Max(0.02, Math.Min(w / size.Width, h / size.Height));
    }

    public void FitToWindow()
    {
        _fit = true;
        Zoom = FitZoom();
        var size = SlideSize;
        _offset = new Vector((ActualWidth - size.Width * Zoom) / 2, (ActualHeight - size.Height * Zoom) / 2);
        ApplyTransform();
    }

    /// <summary>Sets the zoom, keeping <paramref name="anchor"/> (viewport point) still; the centre if null.</summary>
    public void SetZoom(double zoom, Point? anchor = null)
    {
        zoom = MathEx.Clamp(zoom, 0.05, 8);
        var fit = FitZoom();
        if (Math.Abs(zoom - fit) < 0.005)
        {
            FitToWindow();
            return;
        }
        var a = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var slidePoint = ToSlide(a);
        Zoom = zoom;
        _fit = false;
        _offset = new Vector(a.X - slidePoint.X * zoom, a.Y - slidePoint.Y * zoom);
        ClampOffset();
        ApplyTransform();
    }

    public void ZoomStep(int direction, Point? anchor = null)
    {
        var steps = new[] { 0.1, 0.15, 0.2, 0.25, 0.33, 0.4, 0.5, 0.6, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8 };
        var current = Zoom;
        var next = direction > 0 ? steps.FirstOrDefault(s => s > current * 1.01) : steps.LastOrDefault(s => s < current * 0.99);
        if (next <= 0) next = direction > 0 ? 8 : 0.1;
        SetZoom(next, anchor);
    }

    private void ClampOffset()
    {
        var size = SlideSize;
        var w = size.Width * Zoom;
        var h = size.Height * Zoom;
        // Small slides stay centred; big ones can be panned but never off screen entirely.
        _offset.X = w <= ActualWidth ? (ActualWidth - w) / 2 : MathEx.Clamp(_offset.X, ActualWidth - w - 80, 80);
        _offset.Y = h <= ActualHeight ? (ActualHeight - h) / 2 : MathEx.Clamp(_offset.Y, ActualHeight - h - 80, 80);
    }

    private void ApplyTransform()
    {
        _stage.RenderTransform = new MatrixTransform(Zoom, 0, 0, Zoom, _offset.X, _offset.Y);
        var size = SlideSize;
        Canvas.SetLeft(_shadow, _offset.X);
        Canvas.SetTop(_shadow, _offset.Y);
        _shadow.Width = Math.Max(0, size.Width * Zoom);
        _shadow.Height = Math.Max(0, size.Height * Zoom);
        _overlay.InvalidateVisual();
        PositionTextEditor();
        ViewChanged?.Invoke();
    }

    public void Pan(Vector delta)
    {
        _offset += delta;
        _fit = false;
        ClampOffset();
        ApplyTransform();
    }

    // ------------------------------------------------------------------ visuals

    /// <summary>Rebuilds the visuals of the current slide (after slide changes, undo, theme changes).</summary>
    public void Refresh()
    {
        if (_state == null) return;
        if (IsEditingText && (EditingElement == null || _state.CurrentSlide?.Find(EditingElement.Id) == null)) CancelTextEdit();
        var slide = _state.CurrentSlide;
        foreach (var v in _visuals.Values) _stage.Children.Remove(v);
        _visuals.Clear();
        if (slide == null)
        {
            _background.Visibility = Visibility.Collapsed;
            _overlay.InvalidateVisual();
            return;
        }
        _background.Visibility = Visibility.Visible;
        var ctx = _state.RenderContext;
        var style = ctx.StyleFor(slide);
        _background.Update(slide, ctx);
        var index = 1;
        foreach (var e in slide.Elements)
        {
            var visual = new ElementVisual(e, style, ctx);
            if (IsEditingText && EditingElement?.Id == e.Id)
            {
                visual.HideText = true;
                EditingElement = e;
            }
            _visuals[e.Id] = visual;
            _stage.Children.Insert(index++, visual);
        }
        if (_fit) FitToWindow();
        else ApplyTransform();
        _overlay.Hover = null;
        _overlay.InvalidateVisual();
    }

    /// <summary>Moves / resizes visuals to match the model without redrawing them (fast, while dragging).</summary>
    public void PlaceVisuals(IEnumerable<Element> elements, bool redraw)
    {
        foreach (var e in elements)
        {
            if (!_visuals.TryGetValue(e.Id, out var v)) continue;
            v.Place();
            if (redraw) v.InvalidateVisual();
        }
        // Connectors follow what they join.
        if (_state?.CurrentSlide is { } slide)
        {
            foreach (var c in slide.Elements.OfType<ShapeElement>().Where(s => s.Shape == ShapeKind.Connector && (s.StartId != null || s.EndId != null)))
                if (_visuals.TryGetValue(c.Id, out var cv))
                {
                    cv.Place();
                    cv.InvalidateVisual();
                }
        }
        _overlay.InvalidateVisual();
        PositionTextEditor();
    }

    public void InvalidateOverlay() => _overlay.InvalidateVisual();

    public ElementVisual? VisualOf(string id) => _visuals.TryGetValue(id, out var v) ? v : null;

    // ------------------------------------------------------------------ hit testing

    /// <summary>The top-most unlocked element at a slide point.</summary>
    public Element? HitTest(Point p, bool includeLocked = false)
    {
        var slide = _state?.CurrentSlide;
        if (slide == null) return null;
        var tolerance = 6 / Zoom;
        for (var i = slide.Elements.Count - 1; i >= 0; i--)
        {
            var e = slide.Elements[i];
            if (e.Locked && !includeLocked) continue;
            if (Hits(e, p, tolerance)) return e;
        }
        return null;
    }

    private static bool Hits(Element e, Point p, double tolerance)
    {
        if (e is ShapeElement { IsLine: true } line)
        {
            var points = ElementRenderer.LinePath(line, line.Bounds);
            var width = ElementRenderer.LineStroke(line).Width / 2 + tolerance;
            for (var i = 1; i < points.Count; i++)
                if (DistanceToSegment(p, points[i - 1], points[i]) <= width) return true;
            return false;
        }
        var local = Rotate(p, e.Center, -e.Rotation);
        var b = e.Bounds;
        b.Inflate(tolerance * 0.3, tolerance * 0.3);
        if (!b.Contains(local)) return false;
        if (e is ShapeElement { Shape: ShapeKind.Ellipse } && (e as ShapeElement)!.Text == null)
        {
            var rx = b.Width / 2;
            var ry = b.Height / 2;
            if (rx <= 0 || ry <= 0) return false;
            var dx = (local.X - (b.X + rx)) / rx;
            var dy = (local.Y - (b.Y + ry)) / ry;
            return dx * dx + dy * dy <= 1;
        }
        return true;
    }

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len = ab.LengthSquared;
        if (len < 1e-9) return (p - a).Length;
        var t = MathEx.Clamp(Vector.Multiply(p - a, ab) / len, 0, 1);
        return (p - (a + t * ab)).Length;
    }

    // ------------------------------------------------------------------ handles

    public IEnumerable<Handle> GetHandles()
    {
        var state = _state;
        if (state == null || IsEditingText) yield break;
        var selected = state.SelectedElements;
        if (selected.Count == 0 || selected.Any(e => e.Locked)) yield break;

        if (selected.Count == 1 && selected[0] is ShapeElement { IsLine: true } line)
        {
            var (a, b) = ElementRenderer.LineEnds(line, line.Bounds);
            yield return new Handle(HandleKind.LineStart, ToView(a));
            yield return new Handle(HandleKind.LineEnd, ToView(b));
            yield break;
        }

        Rect box;
        Point center;
        double rotation;
        if (selected.Count == 1)
        {
            box = selected[0].Bounds;
            center = selected[0].Center;
            rotation = selected[0].Rotation;
        }
        else
        {
            box = DeckOperations.BoundsOf(selected);
            center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            rotation = 0;
        }

        Point P(double fx, double fy) => ToView(Rotate(new Point(box.X + box.Width * fx, box.Y + box.Height * fy), center, rotation));
        var small = box.Width * Zoom < 28 || box.Height * Zoom < 28;
        yield return new Handle(HandleKind.NW, P(0, 0));
        yield return new Handle(HandleKind.NE, P(1, 0));
        yield return new Handle(HandleKind.SE, P(1, 1));
        yield return new Handle(HandleKind.SW, P(0, 1));
        if (!small)
        {
            yield return new Handle(HandleKind.N, P(0.5, 0));
            yield return new Handle(HandleKind.E, P(1, 0.5));
            yield return new Handle(HandleKind.S, P(0.5, 1));
            yield return new Handle(HandleKind.W, P(0, 0.5));
        }
        // Rotation handle: 24 px above the top edge (in the element's own "up").
        var top = Rotate(new Point(box.X + box.Width / 2, box.Y), center, rotation);
        var up = Rotate(new Point(box.X + box.Width / 2, box.Y - 1), center, rotation) - top;
        up.Normalize();
        var topView = ToView(top);
        yield return new Handle(HandleKind.Rotate, topView + up * 26);
    }

    public Handle? HandleAt(Point view)
    {
        foreach (var h in GetHandles())
        {
            var r = h.Kind == HandleKind.Rotate ? 9 : 7;
            if ((h.View - view).Length <= r) return h;
        }
        return null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SlideCanvasPeer(this);

    private sealed class SlideCanvasPeer : FrameworkElementAutomationPeer
    {
        public SlideCanvasPeer(SlideCanvas owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

        protected override string GetClassNameCore() => "SlideCanvas";

        protected override List<AutomationPeer> GetChildrenCore()
        {
            var canvas = (SlideCanvas)Owner;
            var list = new List<AutomationPeer>();
            foreach (var v in canvas._visuals.Values)
            {
                var peer = CreatePeerForElement(v);
                if (peer != null) list.Add(peer);
            }
            return list;
        }
    }
}
