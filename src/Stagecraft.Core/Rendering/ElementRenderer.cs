using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>Draws one element with a WPF DrawingContext (editor, thumbnails, slide show and PDF all use this).</summary>
public static class ElementRenderer
{
    /// <summary>
    /// Draws <paramref name="element"/>. With <paramref name="local"/> it draws at (0, 0) without its rotation
    /// (the editor rotates the element's visual instead); otherwise at its place on the slide, rotated.
    /// </summary>
    public static void Draw(DrawingContext dc, Element element, SlideStyle style, RenderContext ctx, bool local = false)
    {
        var box = local ? new Rect(0, 0, Math.Max(0, element.Width), Math.Max(0, element.Height)) : element.Bounds;
        var pushes = 0;
        if (!local && Math.Abs(element.Rotation % 360) > 0.001)
        {
            dc.PushTransform(new RotateTransform(element.Rotation, box.X + box.Width / 2, box.Y + box.Height / 2));
            pushes++;
        }
        var isLine = element is ShapeElement { IsLine: true };
        if (!isLine && element is ImageElement or IconElement && (element.FlipH || element.FlipV))
        {
            dc.PushTransform(new ScaleTransform(element.FlipH ? -1 : 1, element.FlipV ? -1 : 1, box.X + box.Width / 2, box.Y + box.Height / 2));
            pushes++;
        }
        if (element.Opacity < 0.999)
        {
            dc.PushOpacity(MathEx.Clamp(element.Opacity, 0, 1));
            pushes++;
        }

        switch (element)
        {
            case TextElement t:
                DrawText(dc, t, box, style, ctx);
                break;
            case ShapeElement s:
                DrawShape(dc, s, box, style, ctx);
                break;
            case ImageElement i:
                DrawImage(dc, i, box, style, ctx);
                break;
            case ChartElement c:
                ChartRenderer.Draw(dc, c, box, style);
                break;
            case TableElement tb:
                TableRenderer.Draw(dc, tb, box, style);
                break;
            case IconElement ic:
                DrawIcon(dc, ic, box, style);
                break;
        }

        for (var i = 0; i < pushes; i++) dc.Pop();
    }

    // ------------------------------------------------------------------ text

    private static void DrawText(DrawingContext dc, TextElement t, Rect box, SlideStyle style, RenderContext ctx)
    {
        DrawPanel(dc, t.Fill, t.Stroke, t.CornerRadius, box, style, ctx);
        DrawTextBody(dc, t.Text, box, style, ctx, t.Placeholder);
    }

    public static void DrawTextBody(DrawingContext dc, TextBody body, Rect box, SlideStyle style, RenderContext ctx, string? placeholder)
    {
        if (body.Content.IsEmpty)
        {
            if (!ctx.Editing || placeholder == null) return;
            var hint = PlaceholderHints.For(placeholder, body.Role);
            var hintColor = style.Resolve(body.Color ?? style.RoleFont(body.Role).Color);
            hintColor.A = (byte)(hintColor.A * 0.42);
            var hintLayout = TextLayout.LayoutInBox(body, box, style, hint, hintColor);
            TextLayout.Draw(dc, hintLayout, TextLayout.Inner(body, box), body.VAlign);
            return;
        }
        var layout = TextLayout.LayoutInBox(body, box, style);
        TextLayout.Draw(dc, layout, TextLayout.Inner(body, box), body.VAlign);
    }

    /// <summary>Fill and border of a text box or picture frame.</summary>
    private static void DrawPanel(DrawingContext dc, Fill? fill, Stroke? stroke, double radius, Rect box, SlideStyle style, RenderContext ctx)
    {
        var brush = style.Brush(fill, box, ctx.Media);
        var pen = MakePen(stroke, style);
        if (brush == null && pen == null) return;
        var r = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
        if (brush is ImageBrush && fill?.Dim > 0)
        {
            dc.DrawRoundedRectangle(brush, null, box, r, r);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(fill.Dim * 255), 0, 0, 0)), null, box, r, r);
            if (pen != null) dc.DrawRoundedRectangle(null, pen, box, r, r);
            return;
        }
        dc.DrawRoundedRectangle(brush, pen, box, r, r);
    }

    public static Pen? MakePen(Stroke? stroke, SlideStyle style)
    {
        if (stroke == null || stroke.Width <= 0) return null;
        var pen = new Pen(style.SolidBrush(stroke.Color), stroke.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashCap = PenLineCap.Round,
        };
        pen.DashStyle = stroke.Dash switch
        {
            DashStyleKind.Dash => new DashStyle(new[] { 3.0, 2.2 }, 0),
            DashStyleKind.Dot => new DashStyle(new[] { 0.01, 2.2 }, 0),
            _ => DashStyles.Solid,
        };
        pen.Freeze();
        return pen;
    }

    // ------------------------------------------------------------------ shapes

    /// <summary>Default line style for lines, arrows and connectors without one.</summary>
    public static Stroke LineStroke(ShapeElement s) => s.Stroke ?? new Stroke { Color = "text", Width = 4 };

    /// <summary>The two ends of a line in its box (FlipH / FlipV choose the diagonal).</summary>
    public static (Point Start, Point End) LineEnds(ShapeElement s, Rect box)
    {
        var x1 = s.FlipH ? box.Right : box.Left;
        var x2 = s.FlipH ? box.Left : box.Right;
        var y1 = s.FlipV ? box.Bottom : box.Top;
        var y2 = s.FlipV ? box.Top : box.Bottom;
        return (new Point(x1, y1), new Point(x2, y2));
    }

    /// <summary>The outline of a filled shape (not lines) in <paramref name="box"/>.</summary>
    public static Geometry ShapeGeometry(ShapeElement s, Rect box)
    {
        switch (s.Shape)
        {
            case ShapeKind.Ellipse:
                return new EllipseGeometry(box);
            case ShapeKind.RoundedRectangle:
            {
                var r = s.CornerRadius > 0 ? s.CornerRadius : Math.Min(box.Width, box.Height) * 0.2;
                r = Math.Min(r, Math.Min(box.Width, box.Height) / 2);
                return new RectangleGeometry(box, r, r);
            }
            case ShapeKind.Triangle:
                return Polygon(new Point(box.X + box.Width / 2, box.Y), new Point(box.Right, box.Bottom), new Point(box.X, box.Bottom));
            case ShapeKind.Diamond:
                return Polygon(new Point(box.X + box.Width / 2, box.Y), new Point(box.Right, box.Y + box.Height / 2),
                    new Point(box.X + box.Width / 2, box.Bottom), new Point(box.X, box.Y + box.Height / 2));
            case ShapeKind.Callout:
                return CalloutGeometry(s, box);
            default:
            {
                var r = Math.Min(Math.Max(0, s.CornerRadius), Math.Min(box.Width, box.Height) / 2);
                return new RectangleGeometry(box, r, r);
            }
        }
    }

    private static Geometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        for (var i = 1; i < points.Length; i++) figure.Segments.Add(new LineSegment(points[i], true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    private static Geometry CalloutGeometry(ShapeElement s, Rect box)
    {
        var r = s.CornerRadius > 0 ? s.CornerRadius : Math.Min(box.Width, box.Height) * 0.18;
        r = Math.Min(r, Math.Min(box.Width, box.Height) / 2);
        var body = new RectangleGeometry(box, r, r);
        var tip = new Point(box.X + box.Width * s.TailX, box.Y + box.Height * s.TailY);
        var cx = MathEx.Clamp(tip.X, box.X + r + box.Width * 0.08, box.Right - r - box.Width * 0.08);
        var baseHalf = Math.Max(6, Math.Min(box.Width, box.Height) * 0.12);
        Geometry tail;
        if (tip.Y > box.Bottom) tail = Polygon(new Point(cx - baseHalf, box.Bottom - 1), new Point(cx + baseHalf, box.Bottom - 1), tip);
        else if (tip.Y < box.Top) tail = Polygon(new Point(cx - baseHalf, box.Top + 1), new Point(cx + baseHalf, box.Top + 1), tip);
        else
        {
            var cy = MathEx.Clamp(tip.Y, box.Y + r + 4, box.Bottom - r - 4);
            var edge = tip.X < box.X ? box.X + 1 : box.Right - 1;
            tail = Polygon(new Point(edge, cy - baseHalf), new Point(edge, cy + baseHalf), tip);
        }
        return new CombinedGeometry(GeometryCombineMode.Union, body, tail);
    }

    private static void DrawShape(DrawingContext dc, ShapeElement s, Rect box, SlideStyle style, RenderContext ctx)
    {
        if (s.IsLine)
        {
            DrawLine(dc, s, box, style);
            return;
        }
        var geometry = ShapeGeometry(s, box);
        var brush = style.Brush(s.Fill, box, ctx.Media);
        var pen = MakePen(s.Stroke, style);
        if (brush is ImageBrush && s.Fill.Dim > 0)
        {
            dc.DrawGeometry(brush, null, geometry);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(s.Fill.Dim * 255), 0, 0, 0)), pen, geometry);
        }
        else
        {
            dc.DrawGeometry(brush, pen, geometry);
        }
        if (s.Text != null) DrawTextBody(dc, s.Text, TextBoxOf(s, box), style, ctx, s.Placeholder);
    }

    /// <summary>Where the text of a shape goes (inside ellipses and triangles it gets extra room around it).</summary>
    public static Rect TextBoxOf(ShapeElement s, Rect box) => s.Shape switch
    {
        ShapeKind.Ellipse => Deflate(box, box.Width * 0.146, box.Height * 0.146),
        ShapeKind.Triangle => new Rect(box.X + box.Width * 0.25, box.Y + box.Height * 0.45, box.Width * 0.5, box.Height * 0.5),
        ShapeKind.Diamond => Deflate(box, box.Width * 0.25, box.Height * 0.25),
        _ => box,
    };

    private static Rect Deflate(Rect r, double dx, double dy) =>
        new(r.X + dx, r.Y + dy, Math.Max(1, r.Width - 2 * dx), Math.Max(1, r.Height - 2 * dy));

    /// <summary>The path a line, arrow or connector follows (two points, or an elbow route).</summary>
    public static List<Point> LinePath(ShapeElement s, Rect box)
    {
        var (a, b) = LineEnds(s, box);
        if (s.Shape != ShapeKind.Connector || !s.Elbow) return new List<Point> { a, b };
        var horizontalFirst = s.StartSide is ConnectorSide.Left or ConnectorSide.Right ||
                              (s.StartSide == ConnectorSide.Auto && Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y));
        if (horizontalFirst)
        {
            var mx = (a.X + b.X) / 2;
            return new List<Point> { a, new(mx, a.Y), new(mx, b.Y), b };
        }
        var my = (a.Y + b.Y) / 2;
        return new List<Point> { a, new(a.X, my), new(b.X, my), b };
    }

    private static void DrawLine(DrawingContext dc, ShapeElement s, Rect box, SlideStyle style)
    {
        var stroke = LineStroke(s);
        var pen = MakePen(stroke, style);
        if (pen == null) return;
        var points = LinePath(s, box);
        var headSize = Math.Max(stroke.Width * 3.2, 14);
        var end = s.Shape == ShapeKind.Arrow && s.EndHead == ArrowHead.None ? ArrowHead.Arrow : s.EndHead;
        var start = s.StartHead;

        // Pull the line back under the arrow heads so the tip stays sharp.
        if (end == ArrowHead.Arrow) points[points.Count - 1] = Back(points[points.Count - 1], points[points.Count - 2], headSize * 0.8);
        if (start == ArrowHead.Arrow) points[0] = Back(points[0], points[1], headSize * 0.8);

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        for (var i = 1; i < points.Count; i++) figure.Segments.Add(new LineSegment(points[i], true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);

        var original = LinePath(s, box);
        var brush = style.SolidBrush(stroke.Color);
        DrawHead(dc, end, original[original.Count - 1], original[original.Count - 2], headSize, brush, stroke.Width);
        DrawHead(dc, start, original[0], original[1], headSize, brush, stroke.Width);
    }

    private static Point Back(Point tip, Point from, double distance)
    {
        var v = tip - from;
        if (v.Length < 0.001) return tip;
        v.Normalize();
        return tip - v * Math.Min(distance, (tip - from).Length);
    }

    private static void DrawHead(DrawingContext dc, ArrowHead head, Point tip, Point from, double size, Brush brush, double width)
    {
        if (head == ArrowHead.None) return;
        var v = tip - from;
        if (v.Length < 0.001) return;
        v.Normalize();
        if (head == ArrowHead.Dot)
        {
            var r = Math.Max(width * 1.8, 7);
            dc.DrawEllipse(brush, null, tip, r, r);
            return;
        }
        var n = new Vector(-v.Y, v.X);
        var basePoint = tip - v * size;
        var p1 = basePoint + n * size * 0.55;
        var p2 = basePoint - n * size * 0.55;
        var figure = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new LineSegment(tip - v * size * 0.78, true));
        figure.Segments.Add(new LineSegment(p2, true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        var pen = new Pen(brush, Math.Max(1, width * 0.5)) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(brush, pen, g);
    }

    // ------------------------------------------------------------------ images

    /// <summary>Where the whole picture is drawn so that its visible part fills the box as the fit / crop asks.</summary>
    public static Rect ImageDestination(ImageElement img, Rect box, double pixelWidth, double pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return box;
        if (img.Crop is { } crop)
        {
            var visW = Math.Max(0.01, 1 - crop.Left - crop.Right);
            var visH = Math.Max(0.01, 1 - crop.Top - crop.Bottom);
            var fullW = box.Width / visW;
            var fullH = box.Height / visH;
            return new Rect(box.X - crop.Left * fullW, box.Y - crop.Top * fullH, fullW, fullH);
        }
        switch (img.Fit)
        {
            case ImageFit.Stretch:
                return box;
            case ImageFit.Contain:
            {
                var k = Math.Min(box.Width / pixelWidth, box.Height / pixelHeight);
                var w = pixelWidth * k;
                var h = pixelHeight * k;
                return new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }
            default:
            {
                var k = Math.Max(box.Width / pixelWidth, box.Height / pixelHeight);
                var w = pixelWidth * k;
                var h = pixelHeight * k;
                var fx = MathEx.Clamp(img.FocusX, 0, 1);
                var fy = MathEx.Clamp(img.FocusY, 0, 1);
                return new Rect(box.X - (w - box.Width) * fx, box.Y - (h - box.Height) * fy, w, h);
            }
        }
    }

    public static Geometry ImageClip(ImageElement img, Rect box)
    {
        if (img.Mask == ImageMask.Circle) return new EllipseGeometry(box);
        var r = Math.Min(Math.Max(0, img.CornerRadius), Math.Min(box.Width, box.Height) / 2);
        return new RectangleGeometry(box, r, r);
    }

    private static void DrawImage(DrawingContext dc, ImageElement img, Rect box, SlideStyle style, RenderContext ctx)
    {
        var clip = ImageClip(img, box);
        var bitmap = ctx.Media.GetImage(img.Media);
        if (bitmap == null)
        {
            if (img.Media == null && !ctx.Editing) return;
            // Empty placeholder (editor) or a picture that is missing from the file.
            var fill = style.Resolve("text@7");
            dc.DrawGeometry(new SolidColorBrush(fill), new Pen(new SolidColorBrush(style.Resolve("text@22")), 2) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) }, clip);
            var icon = IconCatalog.Geometry(img.Media == null ? "image" : "image_off");
            if (icon != null)
            {
                var size = Math.Min(Math.Min(box.Width, box.Height) * 0.28, 120);
                var k = size / IconCatalog.Size;
                dc.PushTransform(new TranslateTransform(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2));
                dc.PushTransform(new ScaleTransform(k, k));
                dc.DrawGeometry(new SolidColorBrush(style.Resolve("text@40")), null, icon);
                dc.Pop();
                dc.Pop();
            }
            return;
        }
        var dest = ImageDestination(img, box, bitmap.PixelWidth, bitmap.PixelHeight);
        dc.PushClip(clip);
        dc.DrawImage(bitmap, dest);
        dc.Pop();
        var pen = MakePen(img.Stroke, style);
        if (pen != null) dc.DrawGeometry(null, pen, clip);
    }

    // ------------------------------------------------------------------ icons

    public static void DrawIcon(DrawingContext dc, IconElement icon, Rect box, SlideStyle style)
    {
        var color = style.Resolve(icon.Color);
        var size = Math.Min(box.Width, box.Height);
        var square = new Rect(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2, size, size);
        if (icon.Container != IconContainer.None)
        {
            var c = icon.ContainerColor != null ? style.Resolve(icon.ContainerColor) : Color.FromArgb((byte)(color.A * 0.14), color.R, color.G, color.B);
            var brush = new SolidColorBrush(c);
            if (icon.Container == IconContainer.Circle) dc.DrawEllipse(brush, null, new Point(square.X + size / 2, square.Y + size / 2), size / 2, size / 2);
            else dc.DrawRoundedRectangle(brush, null, square, size * 0.24, size * 0.24);
            square = Deflate(square, size * 0.22, size * 0.22);
            size = square.Width;
        }
        var geometry = IconCatalog.Geometry(icon.Icon);
        if (geometry == null) return;
        var k = size / IconCatalog.Size;
        dc.PushTransform(new TranslateTransform(square.X, square.Y));
        dc.PushTransform(new ScaleTransform(k, k));
        dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
        dc.Pop();
        dc.Pop();
    }
}
