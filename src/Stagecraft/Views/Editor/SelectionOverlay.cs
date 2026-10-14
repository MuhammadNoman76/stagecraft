using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Animation;
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


    /// <summary>The colour of the presentation's own guides (smart guides use the theme's "Guide" colour).</summary>
    public static readonly Brush UserGuideBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
    private static readonly Brush FindFill = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD4, 0x3B)));
    private static readonly Brush FindCurrentFill = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xB0, 0x20)));
    private static readonly Brush FindCurrentStroke = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>Gridlines (Settings › Show gridlines) and the presentation's guides, over the slide.</summary>
    private void DrawViewAids(DrawingContext dc, Deck deck)
    {
        var settings = Services.AppSettings.Current;
        var origin = _canvas.ToView(new Point(0, 0));
        var end = _canvas.ToView(new Point(deck.Width, deck.Height));
        if (settings.ShowGrid && settings.GridSize > 0)
        {
            // Every grid line while they're at least 6 px apart, else every second (fourth…) one.
            var step = settings.GridSize;
            while (step * _canvas.Zoom < 6) step *= 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (var x = step; x < deck.Width - 0.01; x += step)
                {
                    var vx = Math.Round(_canvas.ToView(new Point(x, 0)).X) + 0.5;
                    ctx.BeginFigure(new Point(vx, origin.Y), false, false);
                    ctx.LineTo(new Point(vx, end.Y), true, false);
                }
                for (var y = step; y < deck.Height - 0.01; y += step)
                {
                    var vy = Math.Round(_canvas.ToView(new Point(0, y)).Y) + 0.5;
                    ctx.BeginFigure(new Point(origin.X, vy), false, false);
                    ctx.LineTo(new Point(end.X, vy), true, false);
                }
            }
            g.Freeze();
            // Dark dots on light slides, light dots on dark ones would need the slide colour: a neutral grey with a
            // white halo reads on both.
            var halo = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80)), 1) { DashStyle = new DashStyle(new double[] { 1, 3 }, 0) };
            dc.DrawGeometry(null, halo, g);
            dc.DrawGeometry(null, pen, g);
        }
        var dragged = _canvas.DraggedGuide;
        var guidePen = new Pen(UserGuideBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        guidePen.Freeze();
        for (var i = 0; i < deck.Guides.Count; i++)
        {
            if (dragged is { } d && d.Index == i) continue;
            DrawGuideLine(dc, deck.Guides[i], guidePen, origin, end, null);
        }
        if (dragged is { } drag)
        {
            var solid = new Pen(UserGuideBrush, 1.5);
            var size = drag.Line.Vertical ? deck.Width : deck.Height;
            var off = drag.Line.Position < 0 || drag.Line.Position > size;
            DrawGuideLine(dc, drag.Line, solid, origin, end, off ? "Release to remove" : SlideCanvas.DescribeLength(drag.Line.Position));
        }
    }

    private void DrawGuideLine(DrawingContext dc, GuideLine line, Pen pen, Point origin, Point end, string? label)
    {
        var at = _canvas.ToView(line.Vertical ? new Point(line.Position, 0) : new Point(0, line.Position));
        if (line.Vertical)
        {
            var x = Math.Round(at.X) + 0.5;
            dc.DrawLine(pen, new Point(x, origin.Y), new Point(x, end.Y));
            if (label != null) DrawPill(dc, label, new Point(x + 8, Math.Max(origin.Y, 0) + 8));
        }
        else
        {
            var y = Math.Round(at.Y) + 0.5;
            dc.DrawLine(pen, new Point(origin.X, y), new Point(end.X, y));
            if (label != null) DrawPill(dc, label, new Point(Math.Max(origin.X, 0) + 8, y + 8));
        }
    }

    private void DrawPill(DrawingContext dc, string text, Point at)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11.5, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var box = new Rect(at.X, at.Y, ft.Width + 14, ft.Height + 6);
        dc.DrawRoundedRectangle(UserGuideBrush, null, box, 4, 4);
        dc.DrawText(ft, new Point(box.X + 7, box.Y + 3));
    }
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

        DrawViewAids(dc, state.Deck);

        // find results
        foreach (var (area, current) in _canvas.FindAreas())
        {
            dc.DrawGeometry(current ? FindCurrentFill : FindFill, current ? new Pen(FindCurrentStroke, 1.5) : null, area);
        }

        // cropping a picture: the whole picture faintly, the kept part, the crop handles
        if (_canvas.CroppingImage is { } cropping)
        {
            DrawCrop(dc, state, cropping);
            return;
        }

        // animation pane open: motion paths and click badges
        if (state.AnimationMode) DrawAnimationMarks(dc, state, slide, accent);
        if (_canvas.DrawnPath is { Count: > 1 } drawn)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(_canvas.ToView(drawn[0]), false, false);
                ctx.PolyLineTo(drawn.Skip(1).Select(_canvas.ToView).ToList(), true, true);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Stagecraft.Views.Editor.PropertiesPanel.ClassColor(EffectClass.Motion)), 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }

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

    /// <summary>
    /// Click numbers next to animated elements (like PowerPoint's badges), and motion paths: a dashed route from the
    /// element's centre with a green start and a red end, an outline where the element ends up, and draggable points
    /// on the selected paths.
    /// </summary>
    private void DrawAnimationMarks(DrawingContext dc, EditorState state, Slide slide, Brush accent)
    {
        var timeline = SlideTimeline.Build(slide);
        var motion = new SolidColorBrush(PropertiesPanel.ClassColor(EffectClass.Motion));
        motion.Freeze();
        var green = new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0x5A));
        var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        foreach (var path in _canvas.MotionPaths())
        {
            var selected = state.SelectedEffects.Contains(path.Effect.Id);
            var points = path.Route.Select(_canvas.ToView).ToList();
            if (points.Count < 2) continue;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            g.Freeze();
            var pen = new Pen(motion, selected ? 2 : 1.4) { DashStyle = new DashStyle(new[] { 3.0, 2.5 }, 0), LineJoin = PenLineJoin.Round, Thickness = selected ? 2 : 1.4 };
            dc.DrawGeometry(null, new Pen(Brushes.White, selected ? 4 : 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
            dc.DrawGeometry(null, pen, g);
            // Where the element ends up.
            var end = path.Route[path.Route.Count - 1];
            var shift = end - path.Origin;
            var ghost = path.TargetBounds;
            ghost.Offset(shift);
            var ghostView = _canvas.ToView(ghost);
            dc.DrawRectangle(null, new Pen(motion, 1) { DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0) }, ghostView);
            // Start (green triangle pointing along the path) and end (red bar).
            DrawArrowHead(dc, points[0], points[Math.Min(points.Count - 1, 3)], green, 6.5);
            var last = points[points.Count - 1];
            var before = points[Math.Max(0, points.Count - 4)];
            var dir = last - before;
            if (dir.Length > 0.01)
            {
                dir.Normalize();
                var n = new Vector(-dir.Y, dir.X) * 6;
                dc.DrawLine(new Pen(red, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, last - n, last + n);
            }
            if (selected && path.Effect.Path != null)
            {
                var first = path.Effect.Path.Points[0];
                for (var i = 1; i < path.Effect.Path.Points.Count; i++)
                {
                    var q = path.Effect.Path.Points[i];
                    var v = _canvas.ToView(new Point(path.Origin.X + q.X - first.X, path.Origin.Y + q.Y - first.Y));
                    dc.DrawRectangle(Brushes.White, new Pen(motion, 1.5), new Rect(v.X - 4, v.Y - 4, 8, 8));
                }
            }
        }

        // Badges: the click(s) that animate each element.
        var badges = new Dictionary<string, List<string>>();
        void Note(TimelineItem item, string label)
        {
            foreach (var e in AnimationRules.Resolve(slide, item.Effect.Target))
            {
                if (!badges.TryGetValue(e.Id, out var list)) badges[e.Id] = list = new List<string>();
                if (!list.Contains(label)) list.Add(label);
            }
        }
        foreach (var g in timeline.Groups)
            foreach (var item in g.Items) Note(item, g.Click.ToString(CultureInfo.CurrentCulture));
        foreach (var t in timeline.Triggers.Values)
            foreach (var g in t)
                foreach (var item in g.Items) Note(item, "⚡");
        var selectedTargets = new HashSet<string>(state.SelectedEffectList.SelectMany(a => AnimationRules.Resolve(slide, a.Target)).Select(e => e.Id));
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var e in slide.Elements)
        {
            if (!badges.TryGetValue(e.Id, out var labels)) continue;
            var corner = _canvas.ToView(e.RotatedBounds.TopLeft);
            var x = corner.X - 6;
            var y = corner.Y - 8;
            foreach (var label in labels.Take(4))
            {
                var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10.5, Brushes.White, dpi);
                var w = Math.Max(16, ft.Width + 8);
                var r = new Rect(x - w, y, w, 16);
                var hot = selectedTargets.Contains(e.Id);
                dc.DrawRoundedRectangle(hot ? accent : new SolidColorBrush(Color.FromRgb(0x5B, 0x5F, 0x6E)), new Pen(Brushes.White, 1), r, 4, 4);
                dc.DrawText(ft, new Point(r.X + (w - ft.Width) / 2, r.Y + (16 - ft.Height) / 2));
                y += 18;
            }
        }
    }

    private void DrawCrop(DrawingContext dc, EditorState state, ImageElement image)
    {
        var bitmap = state.Session.Media.GetImage(image.Media);
        var full = _canvas.ToView(_canvas.CropFull);
        var box = _canvas.ToView(image.Bounds);
        if (bitmap != null)
        {
            // The cut-away parts: the picture, dimmed.
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(box)));
            dc.PushOpacity(0.38);
            dc.DrawImage(bitmap, full);
            dc.Pop();
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, full);
            dc.Pop();
        }
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) }, full);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1), box);
        // Thick black L-shaped corners and bars, outlined in white so they read on any picture.
        var black = new Pen(Brushes.Black, 4) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
        var halo = new Pen(Brushes.White, 6.5) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
        var arm = Math.Min(16, Math.Min(box.Width, box.Height) / 3);
        void Draw(Pen pen)
        {
            dc.DrawLine(pen, box.TopLeft, new Point(box.Left + arm, box.Top));
            dc.DrawLine(pen, box.TopLeft, new Point(box.Left, box.Top + arm));
            dc.DrawLine(pen, box.TopRight, new Point(box.Right - arm, box.Top));
            dc.DrawLine(pen, box.TopRight, new Point(box.Right, box.Top + arm));
            dc.DrawLine(pen, box.BottomRight, new Point(box.Right - arm, box.Bottom));
            dc.DrawLine(pen, box.BottomRight, new Point(box.Right, box.Bottom - arm));
            dc.DrawLine(pen, box.BottomLeft, new Point(box.Left + arm, box.Bottom));
            dc.DrawLine(pen, box.BottomLeft, new Point(box.Left, box.Bottom - arm));
            var cx = box.Left + box.Width / 2;
            var cy = box.Top + box.Height / 2;
            dc.DrawLine(pen, new Point(cx - arm / 2, box.Top), new Point(cx + arm / 2, box.Top));
            dc.DrawLine(pen, new Point(cx - arm / 2, box.Bottom), new Point(cx + arm / 2, box.Bottom));
            dc.DrawLine(pen, new Point(box.Left, cy - arm / 2), new Point(box.Left, cy + arm / 2));
            dc.DrawLine(pen, new Point(box.Right, cy - arm / 2), new Point(box.Right, cy + arm / 2));
        }
        Draw(halo);
        Draw(black);
        var label = "Drag the edges to crop · drag the picture to move it · Enter to finish";
        DrawPill(dc, label, new Point(box.X + box.Width / 2, Math.Max(14, box.Top - 18)), (Brush)FindResource("Flyout.Background"), (Brush)FindResource("Text.Primary"), true);
    }

    private static void DrawArrowHead(DrawingContext dc, Point at, Point toward, Brush brush, double size)
    {
        var v = toward - at;
        if (v.Length < 0.01) v = new Vector(1, 0);
        v.Normalize();
        var n = new Vector(-v.Y, v.X);
        var tip = at + v * size;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(at - v * size * 0.4 + n * size * 0.8, true, true);
            ctx.LineTo(at - v * size * 0.4 - n * size * 0.8, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(brush, new Pen(Brushes.White, 1), g);
    }

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
