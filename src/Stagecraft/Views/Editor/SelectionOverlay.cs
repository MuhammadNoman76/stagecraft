using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

public enum HandleKind { NW, N, NE, E, SE, S, SW, W, Rotate, LineStart, LineEnd }

/// <summary>A selection handle in viewport (screen) coordinates.</summary>
public readonly struct Handle
{
    public Handle(HandleKind kind, Point view)
    {
        Kind = kind;
        View = view;
    }

    public HandleKind Kind { get; }
    public Point View { get; }

    public (int X, int Y) Direction => Kind switch
    {
        HandleKind.NW => (-1, -1),
        HandleKind.N => (0, -1),
        HandleKind.NE => (1, -1),
        HandleKind.E => (1, 0),
        HandleKind.SE => (1, 1),
        HandleKind.S => (0, 1),
        HandleKind.SW => (-1, 1),
        HandleKind.W => (-1, 0),
        _ => (0, 0),
    };

    public bool IsCorner => Kind is HandleKind.NW or HandleKind.NE or HandleKind.SE or HandleKind.SW;
}

/// <summary>
/// Draws selection frames, handles, the hover outline, smart guides, the marquee and the size / angle badge over
/// the canvas, in screen pixels so they stay crisp and the same size at any zoom.
/// </summary>
public sealed class SelectionOverlay : FrameworkElement
{
    private readonly SlideCanvas _canvas;

    public SelectionOverlay(SlideCanvas canvas)
    {
        _canvas = canvas;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<Guide> Guides { get; set; } = Array.Empty<Guide>();
    public Rect? Marquee { get; set; }
    public Element? Hover { get; set; }
    public string? Badge { get; set; }
    public Point BadgeAt { get; set; }
    public bool ShowHandles { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        var state = _canvas.State;
        var slide = state?.CurrentSlide;
        if (state == null || slide == null) return;

        var accent = (Brush)FindResource("Accent.Fill");
        var guideBrush = (Brush)FindResource("Guide");
        var white = Brushes.White;
        var thin = new Pen(accent, 1.5);
        thin.Freeze();

        // hover
        if (Hover != null && !state.IsSelected(Hover.Id) && slide.Elements.Contains(Hover))
        {
            var hoverPen = new Pen(accent, 1) { DashStyle = DashStyles.Solid };
            DrawFrame(dc, Hover, hoverPen);
        }

        var selected = state.SelectedElements;
        if (selected.Count > 0 && !_canvas.IsEditingText)
        {
            foreach (var e in selected) DrawFrame(dc, e, thin);
            if (selected.Count > 1 || selected[0].GroupId != null)
            {
                var union = _canvas.ToView(DeckOperations.BoundsOf(selected));
                var dashed = new Pen(accent, 1) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
                dc.DrawRectangle(null, dashed, Inflate(union, 4));
            }
            if (ShowHandles)
            {
                foreach (var h in _canvas.GetHandles())
                {
                    if (h.Kind == HandleKind.Rotate)
                    {
                        dc.DrawEllipse(white, new Pen(accent, 1.5), h.View, 6, 6);
                        dc.DrawEllipse(accent, null, h.View, 2.2, 2.2);
                    }
                    else if (h.Kind is HandleKind.LineStart or HandleKind.LineEnd)
                    {
                        dc.DrawEllipse(white, new Pen(accent, 2), h.View, 6, 6);
                    }
                    else
                    {
                        var size = h.IsCorner ? 9.0 : 7.0;
                        var r = new Rect(h.View.X - size / 2, h.View.Y - size / 2, size, size);
                        dc.DrawRoundedRectangle(white, new Pen(accent, 1.5), r, 2, 2);
                    }
                }
            }
        }
        else if (_canvas.IsEditingText && _canvas.EditingElement is { } editing)
        {
            var pen = new Pen(accent, 1.5);
            DrawFrame(dc, editing, pen, 3);
        }

        // smart guides
        var guidePen = new Pen(guideBrush, 1);
        guidePen.Freeze();
        foreach (var g in Guides)
        {
            if (g.Kind == GuideKind.Spacing)
            {
                foreach (var (start, end, cross) in g.Gaps)
                {
                    Point a, b;
                    if (!g.Vertical)
                    {
                        a = _canvas.ToView(new Point(start, cross));
                        b = _canvas.ToView(new Point(end, cross));
                    }
                    else
                    {
                        a = _canvas.ToView(new Point(cross, start));
                        b = _canvas.ToView(new Point(cross, end));
                    }
                    dc.DrawLine(guidePen, a, b);
                    var n = !g.Vertical ? new Vector(0, 5) : new Vector(5, 0);
                    dc.DrawLine(guidePen, a - n, a + n);
                    dc.DrawLine(guidePen, b - n, b + n);
                    var gap = Math.Round(end - start);
                    DrawPill(dc, gap.ToString(CultureInfo.CurrentCulture), new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), guideBrush, white);
                }
            }
            else
            {
                Point a, b;
                if (g.Vertical)
                {
                    a = _canvas.ToView(new Point(g.Position, g.From));
                    b = _canvas.ToView(new Point(g.Position, g.To));
                }
                else
                {
                    a = _canvas.ToView(new Point(g.From, g.Position));
                    b = _canvas.ToView(new Point(g.To, g.Position));
                }
                var pen = g.Kind == GuideKind.Slide ? new Pen(guideBrush, 1) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) } : guidePen;
                dc.DrawLine(pen, a, b);
            }
        }

        // marquee
        if (Marquee is { } m)
        {
            var r = _canvas.ToView(m);
            var fill = ((SolidColorBrush)accent).Color;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, fill.R, fill.G, fill.B)), thin, r);
        }

        if (Badge != null) DrawPill(dc, Badge, BadgeAt, (Brush)FindResource("Flyout.Background"), (Brush)FindResource("Text.Primary"), true);
    }

    private void DrawFrame(DrawingContext dc, Element e, Pen pen, double inflate = 0)
    {
        if (e is ShapeElement { IsLine: true } line)
        {
            var points = ElementRenderer.LinePath(line, line.Bounds);
            for (var i = 1; i < points.Count; i++) dc.DrawLine(pen, _canvas.ToView(points[i - 1]), _canvas.ToView(points[i]));
            return;
        }
        var b = e.Bounds;
        b = new Rect(b.X - inflate / _canvas.Zoom, b.Y - inflate / _canvas.Zoom, b.Width + 2 * inflate / _canvas.Zoom, b.Height + 2 * inflate / _canvas.Zoom);
        var c = e.Center;
        var corners = new[] { b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft }
            .Select(p => _canvas.ToView(SlideCanvas.Rotate(p, c, e.Rotation))).ToArray();
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(corners[0], false, true);
            ctx.PolyLineTo(corners.Skip(1).ToArray(), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static Rect Inflate(Rect r, double d) => new(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

    private void DrawPill(DrawingContext dc, string text, Point center, Brush background, Brush foreground, bool border = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11, foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var r = new Rect(center.X - ft.Width / 2 - 6, center.Y - ft.Height / 2 - 2, ft.Width + 12, ft.Height + 4);
        dc.DrawRoundedRectangle(background, border ? new Pen((Brush)FindResource("Flyout.Stroke"), 1) : null, r, 4, 4);
        dc.DrawText(ft, new Point(r.X + 6, r.Y + 2));
    }
}
