using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>Which part of an element to draw: the slide show draws text pieces and effect overlays separately.</summary>
public sealed class ElementPart
{
    /// <summary>Fill, border, pictures: everything but the text.</summary>
    public bool BodyOnly { get; set; }

    /// <summary>Only the text.</summary>
    public bool TextOnly { get; set; }

    /// <summary>Only this piece of the text (the rest keeps its place, invisible).</summary>
    public TextMask? Mask { get; set; }

    /// <summary>All text bold (Bold flash).</summary>
    public bool Bold { get; set; }

    public bool NoShadow { get; set; }

    public static ElementPart Body => new() { BodyOnly = true };

    public static ElementPart Text(TextMask? mask) => new() { TextOnly = true, Mask = mask };

    public string Key => $"{(BodyOnly ? "b" : "")}{(TextOnly ? "t" : "")}{Mask?.Paragraph}:{Mask?.Start}:{Mask?.Length}{(Bold ? "B" : "")}";
}

/// <summary>Draws one element with a WPF DrawingContext (editor, thumbnails, slide show and PDF all use this).</summary>
public static class ElementRenderer
{
    /// <summary>
    /// Draws <paramref name="element"/>. With <paramref name="local"/> it draws at (0, 0) without its rotation
    /// (the editor rotates the element's visual instead); otherwise at its place on the slide, rotated.
    /// </summary>
    public static void Draw(DrawingContext dc, Element element, SlideStyle style, RenderContext ctx, bool local = false, ElementPart? part = null)
    {
        var box = local ? new Rect(0, 0, Math.Max(0, element.Width), Math.Max(0, element.Height)) : element.Bounds;
        if (!ctx.Editing && IsHiddenInShow(element)) return;
        var pushes = 0;
        var rotation = local ? 0 : element.Rotation;
        if (!local && Math.Abs(element.Rotation % 360) > 0.001)
        {
            dc.PushTransform(new RotateTransform(element.Rotation, box.X + box.Width / 2, box.Y + box.Height / 2));
            pushes++;
        }
        var isLine = element is ShapeElement { IsLine: true };
        var flipped = !isLine && element is ImageElement or IconElement or VideoElement && (element.FlipH || element.FlipV);
        if (flipped)
        {
            dc.PushTransform(new ScaleTransform(element.FlipH ? -1 : 1, element.FlipV ? -1 : 1, box.X + box.Width / 2, box.Y + box.Height / 2));
            pushes++;
        }
        if (element.Opacity < 0.999)
        {
            dc.PushOpacity(MathEx.Clamp(element.Opacity, 0, 1));
            pushes++;
        }

        if (element.Shadow is { } shadow && part?.NoShadow != true)
        {
            var key = DeckJson.ToJson(element) + "|" + local + "|" + style.Signature + "|" + part?.Key + "|" + ctx.Editing;
            ShadowRenderer.Draw(dc, shadow, style, c => DrawContent(c, element, box, style, ctx, part, rotation), key,
                rotation, flipped && element.FlipH, flipped && element.FlipV);
        }
        DrawContent(dc, element, box, style, ctx, part, rotation);

        for (var i = 0; i < pushes; i++) dc.Pop();
    }

    /// <summary>Things the slide show doesn't draw (a sound whose icon is hidden).</summary>
    public static bool IsHiddenInShow(Element e) => e is AudioElement { HideIcon: true };

    private static void DrawContent(DrawingContext dc, Element element, Rect box, SlideStyle style, RenderContext ctx, ElementPart? part, double rotation)
    {
        var textOnly = part?.TextOnly == true;
        var bodyOnly = part?.BodyOnly == true;
        switch (element)
        {
            case TextElement t:
                if (!textOnly) DrawPanel(dc, t.Fill, t.Stroke, t.CornerRadius, box, style, ctx);
                if (!bodyOnly) DrawTextBody(dc, t.Text, box, style, ctx, t.Placeholder, part, element, rotation);
                break;
            case ShapeElement s:
                DrawShape(dc, s, box, style, ctx, part, rotation);
                break;
            case ImageElement i when !textOnly:
                DrawImage(dc, i, box, style, ctx);
                break;
            case ChartElement c when !textOnly:
                ChartRenderer.Draw(dc, c, box, style);
                break;
            case TableElement tb when !textOnly:
                TableRenderer.Draw(dc, tb, box, style);
                break;
            case IconElement ic when !textOnly:
                DrawIcon(dc, ic, box, style);
                break;
            case VideoElement v when !textOnly:
                DrawVideo(dc, v, box, style, ctx);
                break;
            case AudioElement a when !textOnly:
                DrawAudio(dc, a, box, style, ctx);
                break;
            case OnlineVideoElement o when !textOnly:
                DrawOnlineVideo(dc, o, box, style, ctx);
                break;
        }
    }

    // ------------------------------------------------------------------ text

    public static void DrawTextBody(DrawingContext dc, TextBody body, Rect box, SlideStyle style, RenderContext ctx, string? placeholder) =>
        DrawTextBody(dc, body, box, style, ctx, placeholder, null, null, 0);

    private static void DrawTextBody(DrawingContext dc, TextBody body, Rect box, SlideStyle style, RenderContext ctx, string? placeholder, ElementPart? part, Element? owner, double rotation)
    {
        if (body.Content.IsEmpty)
        {
            if (!ctx.Editing || placeholder == null || part != null) return;
            var hint = PlaceholderHints.For(placeholder, body.Role);
            var hintColor = style.Resolve(body.Color ?? style.RoleFont(body.Role).Color);
            hintColor.A = (byte)(hintColor.A * 0.42);
            var hintLayout = TextLayout.LayoutInBox(body, box, style, hint, hintColor);
            TextLayout.Draw(dc, hintLayout, TextLayout.Inner(body, box), body.VAlign);
            return;
        }
        var layout = part?.Mask != null || part?.Bold == true
            ? TextLayout.LayoutInBox(body, box, style, part.Mask, part.Bold)
            : TextLayout.LayoutInBox(body, box, style);
        var inner = TextLayout.Inner(body, box);
        if (body.Shadow is { } shadow && part?.NoShadow != true)
        {
            var key = "text|" + (owner != null ? DeckJson.ToJson(owner) : DeckJson.ToJson(body)) + "|" + box.Width.ToString("F1", CultureInfo.InvariantCulture) + "x" +
                      box.Height.ToString("F1", CultureInfo.InvariantCulture) + "|" + style.Signature + "|" + part?.Key;
            ShadowRenderer.Draw(dc, shadow, style, c => TextLayout.Draw(c, layout, inner, body.VAlign), key, rotation);
        }
        TextLayout.Draw(dc, layout, inner, body.VAlign);
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
            DashStyleKind.LongDash => new DashStyle(new[] { 6.0, 2.6 }, 0),
            DashStyleKind.DashDot => new DashStyle(new[] { 3.6, 2.2, 0.01, 2.2 }, 0),
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
            case ShapeKind.ActionButton:
            {
                var r = s.CornerRadius > 0 ? s.CornerRadius : Math.Min(box.Width, box.Height) * (s.Shape == ShapeKind.ActionButton ? 0.24 : 0.2);
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

    private static void DrawShape(DrawingContext dc, ShapeElement s, Rect box, SlideStyle style, RenderContext ctx, ElementPart? part, double rotation)
    {
        if (s.IsLine)
        {
            if (part?.TextOnly != true) DrawLine(dc, s, box, style);
            return;
        }
        if (part?.TextOnly != true)
        {
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
            if (s.Shape == ShapeKind.ActionButton) DrawActionSymbol(dc, s, box, style);
        }
        if (s.Text != null && part?.BodyOnly != true) DrawTextBody(dc, s.Text, TextBoxOf(s, box), style, ctx, s.Placeholder, part, s, rotation);
    }

    /// <summary>The slide icon an action button shows.</summary>
    public static string ActionIcon(ActionButtonKind kind) => kind switch
    {
        ActionButtonKind.Previous => "arrow_left",
        ActionButtonKind.Next => "arrow_right",
        ActionButtonKind.First => "previous",
        ActionButtonKind.Last => "next",
        ActionButtonKind.Return => "arrow_reply",
        _ => "link",
    };

    /// <summary>The symbol of an action button: an icon in a colour that reads on the button's fill.</summary>
    private static void DrawActionSymbol(DrawingContext dc, ShapeElement s, Rect box, SlideStyle style)
    {
        if (s.Text is { Content.IsEmpty: false }) return; // a labelled button shows its label instead
        var geometry = IconCatalog.Geometry(ActionIcon(s.Action));
        if (geometry == null) return;
        var fill = s.Fill.Kind == FillKind.None ? style.Resolve("bg") : style.Resolve(s.Fill.Color);
        var ink = s.Fill.Kind == FillKind.None ? style.Resolve(s.Stroke?.Color ?? "accent1") : ChartRenderer.ContrastOn(fill);
        var size = Math.Min(box.Width, box.Height) * 0.56;
        var k = size / IconCatalog.Size;
        dc.PushTransform(new TranslateTransform(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2));
        dc.PushTransform(new ScaleTransform(k, k));
        dc.DrawGeometry(new SolidColorBrush(ink), null, geometry);
        dc.Pop();
        dc.Pop();
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
    public static Rect ImageDestination(ImageElement img, Rect box, double pixelWidth, double pixelHeight) =>
        PictureDestination(img.Crop, img.Fit, img.FocusX, img.FocusY, box, pixelWidth, pixelHeight);

    /// <summary>Where a whole picture goes so its visible part fills <paramref name="box"/> (crop first, else the fit).</summary>
    public static Rect PictureDestination(Crop? crop, ImageFit fit, double focusX, double focusY, Rect box, double pixelWidth, double pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return box;
        if (crop != null)
        {
            var visW = Math.Max(0.01, 1 - crop.Left - crop.Right);
            var visH = Math.Max(0.01, 1 - crop.Top - crop.Bottom);
            var fullW = box.Width / visW;
            var fullH = box.Height / visH;
            return new Rect(box.X - crop.Left * fullW, box.Y - crop.Top * fullH, fullW, fullH);
        }
        switch (fit)
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
                var fx = MathEx.Clamp(focusX, 0, 1);
                var fy = MathEx.Clamp(focusY, 0, 1);
                return new Rect(box.X - (w - box.Width) * fx, box.Y - (h - box.Height) * fy, w, h);
            }
        }
    }

    /// <summary>The crop that shows exactly what the current fit shows (crop mode starts from it).</summary>
    public static Crop CropFromFit(ImageElement img, double pixelWidth, double pixelHeight)
    {
        if (img.Crop != null) return new Crop { Left = img.Crop.Left, Top = img.Crop.Top, Right = img.Crop.Right, Bottom = img.Crop.Bottom };
        var box = img.Bounds;
        var dest = PictureDestination(null, img.Fit, img.FocusX, img.FocusY, box, pixelWidth, pixelHeight);
        if (dest.Width <= 0 || dest.Height <= 0) return new Crop();
        // Contain leaves bands: the picture fits inside, nothing is cut (negative crops aren't used).
        return new Crop
        {
            Left = Math.Max(0, (box.X - dest.X) / dest.Width),
            Top = Math.Max(0, (box.Y - dest.Y) / dest.Height),
            Right = Math.Max(0, (dest.Right - box.Right) / dest.Width),
            Bottom = Math.Max(0, (dest.Bottom - box.Bottom) / dest.Height),
        };
    }

    public static Geometry ImageClip(ImageElement img, Rect box) => MaskClip(img.Mask, img.CornerRadius, box);

    public static Geometry MaskClip(ImageMask mask, double cornerRadius, Rect box)
    {
        if (mask == ImageMask.Circle) return new EllipseGeometry(box);
        var r = Math.Min(Math.Max(0, cornerRadius), Math.Min(box.Width, box.Height) / 2);
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
            DrawEmptyFrame(dc, clip, box, style, img.Media == null ? "image" : "image_off");
            return;
        }
        var dest = ImageDestination(img, box, bitmap.PixelWidth, bitmap.PixelHeight);
        dc.PushClip(clip);
        dc.DrawImage(bitmap, dest);
        dc.Pop();
        var pen = MakePen(img.Stroke, style);
        if (pen != null) dc.DrawGeometry(null, pen, clip);
    }

    private static void DrawEmptyFrame(DrawingContext dc, Geometry clip, Rect box, SlideStyle style, string icon)
    {
        var fill = style.Resolve("text@7");
        dc.DrawGeometry(new SolidColorBrush(fill), new Pen(new SolidColorBrush(style.Resolve("text@22")), 2) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) }, clip);
        DrawCenteredIcon(dc, icon, box, Math.Min(Math.Min(box.Width, box.Height) * 0.28, 120), style.Resolve("text@40"));
    }

    private static void DrawCenteredIcon(DrawingContext dc, string icon, Rect box, double size, Color color)
    {
        var geometry = IconCatalog.Geometry(icon);
        if (geometry == null) return;
        var k = size / IconCatalog.Size;
        dc.PushTransform(new TranslateTransform(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2));
        dc.PushTransform(new ScaleTransform(k, k));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
        dc.Pop();
    }

    // ------------------------------------------------------------------ video and sound

    /// <summary>A video before it plays: its poster frame (or a neutral card), clipped to its shape.</summary>
    private static void DrawVideo(DrawingContext dc, VideoElement v, Rect box, SlideStyle style, RenderContext ctx)
    {
        var clip = MaskClip(v.Mask, v.CornerRadius, box);
        var poster = ctx.Media.GetImage(v.Poster);
        var missing = v.Media != null ? !ctx.Media.Contains(v.Media) : v.IsLinked && ctx.Media.ResolveLink(v) == null;
        if (poster != null)
        {
            var dest = PictureDestination(null, ImageFit.Cover, 0.5, 0.5, box, poster.PixelWidth, poster.PixelHeight);
            dc.PushClip(clip);
            dc.DrawImage(poster, dest);
            dc.Pop();
        }
        else
        {
            // No poster yet: a dark card with a film symbol.
            var card = new LinearGradientBrush(Color.FromRgb(0x24, 0x24, 0x2E), Color.FromRgb(0x12, 0x12, 0x18), 90);
            card.Freeze();
            dc.DrawGeometry(card, null, clip);
            DrawCenteredIcon(dc, "video", box, Math.Min(Math.Min(box.Width, box.Height) * 0.3, 140), Color.FromArgb(150, 255, 255, 255));
        }
        if (ctx.Editing)
        {
            // In the editor a small badge tells videos from pictures (and says when the file is missing).
            DrawMediaBadge(dc, box, missing ? "Video not found" : DurationText(v), missing ? "warning" : "video", missing);
        }
        var pen = MakePen(v.Stroke, style);
        if (pen != null) dc.DrawGeometry(null, pen, clip);
    }

    private static string DurationText(MediaClipElement clip)
    {
        var seconds = clip.PlayLength;
        if (seconds == null) return clip.KindName;
        var t = TimeSpan.FromSeconds(Math.Round(seconds.Value));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static void DrawMediaBadge(DrawingContext dc, Rect box, string text, string icon, bool warning)
    {
        var height = MathEx.Clamp(Math.Min(box.Width, box.Height) * 0.12, 18, 44);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontCatalog.Typeface("Inter", 600, false), height * 0.5,
            Brushes.White, 1.0);
        var pad = height * 0.3;
        var iconSize = height * 0.56;
        var width = pad + iconSize + pad * 0.6 + ft.Width + pad;
        if (width > box.Width - height * 0.6) return;
        var r = new Rect(box.X + height * 0.3, box.Bottom - height * 1.3, width, height);
        var back = new SolidColorBrush(warning ? Color.FromArgb(220, 0xC4, 0x2B, 0x1C) : Color.FromArgb(180, 0, 0, 0));
        back.Freeze();
        dc.DrawRoundedRectangle(back, null, r, height / 2, height / 2);
        var geometry = IconCatalog.Geometry(icon);
        if (geometry != null)
        {
            var k = iconSize / IconCatalog.Size;
            dc.PushTransform(new TranslateTransform(r.X + pad, r.Y + (height - iconSize) / 2));
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawGeometry(Brushes.White, null, geometry);
            dc.Pop();
            dc.Pop();
        }
        dc.DrawText(ft, new Point(r.X + pad + iconSize + pad * 0.6, r.Y + (height - ft.Height) / 2));
    }

    /// <summary>A sound: a round speaker button in its colour.</summary>
    private static void DrawAudio(DrawingContext dc, AudioElement a, Rect box, SlideStyle style, RenderContext ctx)
    {
        var color = style.Resolve(a.Color);
        var size = Math.Min(box.Width, box.Height);
        var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, size / 2, size / 2);
        var ink = ChartRenderer.ContrastOn(color);
        DrawCenteredIcon(dc, "speaker_2", new Rect(center.X - size / 2, center.Y - size / 2, size, size), size * 0.52, ink);
        var missing = a.Media != null ? !ctx.Media.Contains(a.Media) : a.IsLinked && ctx.Media.ResolveLink(a) == null;
        if (ctx.Editing && missing)
        {
            var warn = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            warn.Freeze();
            dc.DrawEllipse(warn, new Pen(Brushes.White, size * 0.03), new Point(center.X + size * 0.36, center.Y - size * 0.36), size * 0.13, size * 0.13);
        }
        if (ctx.Editing && a.HideIcon)
        {
            // Hidden in the show: drawn dashed in the editor.
            var pen = new Pen(new SolidColorBrush(style.Resolve("text@45")), Math.Max(1.5, size * 0.02)) { DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0) };
            pen.Freeze();
            dc.DrawEllipse(null, pen, center, size / 2 + size * 0.06, size / 2 + size * 0.06);
        }
    }

    /// <summary>A video on a website: its picture (or a styled card) with a play button, the title and the site.</summary>
    private static void DrawOnlineVideo(DrawingContext dc, OnlineVideoElement o, Rect box, SlideStyle style, RenderContext ctx)
    {
        var r = Math.Min(Math.Max(0, o.CornerRadius), Math.Min(box.Width, box.Height) / 2);
        var clip = new RectangleGeometry(box, r, r);
        var thumb = ctx.Media.GetImage(o.Thumbnail);
        dc.PushClip(clip);
        if (thumb != null)
        {
            dc.DrawImage(thumb, PictureDestination(null, ImageFit.Cover, 0.5, 0.5, box, thumb.PixelWidth, thumb.PixelHeight));
        }
        else
        {
            var a1 = style.Resolve("accent1");
            var dark = Color.FromRgb((byte)(a1.R * 0.25), (byte)(a1.G * 0.25), (byte)(a1.B * 0.25));
            var card = new LinearGradientBrush(Color.FromRgb((byte)(a1.R * 0.55), (byte)(a1.G * 0.55), (byte)(a1.B * 0.55)), dark, 45);
            card.Freeze();
            dc.DrawRectangle(card, null, box);
        }
        // A scrim at the bottom keeps the title readable on any picture.
        var scrim = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0), 90);
        scrim.Freeze();
        var scrimHeight = box.Height * 0.42;
        dc.DrawRectangle(scrim, null, new Rect(box.X, box.Bottom - scrimHeight, box.Width, scrimHeight));
        dc.Pop();

        // Play button.
        var size = Math.Min(box.Width, box.Height) * 0.26;
        var c = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        var disc = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
        disc.Freeze();
        dc.DrawEllipse(disc, null, c, size / 2, size / 2);
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(c.X - size * 0.13, c.Y - size * 0.19), true, true);
            g.LineTo(new Point(c.X + size * 0.22, c.Y), true, true);
            g.LineTo(new Point(c.X - size * 0.13, c.Y + size * 0.19), true, true);
        }
        tri.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A)), null, tri);

        // Title and site.
        var pad = Math.Min(box.Width, box.Height) * 0.06;
        var titleSize = MathEx.Clamp(box.Height * 0.075, 10, 64);
        var title = string.IsNullOrWhiteSpace(o.Title) ? ShortUrl(o.Url) : o.Title!;
        var family = style.Theme.Fonts.Body;
        var ft = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FontCatalog.Typeface(family, 600, false), titleSize, Brushes.White, 1.0)
        {
            MaxTextWidth = Math.Max(1, box.Width - 2 * pad),
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var site = new FormattedText((o.Provider ?? "Online video").ToUpperInvariant(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            FontCatalog.Typeface(family, 700, false), titleSize * 0.62, new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)), 1.0)
        {
            MaxTextWidth = Math.Max(1, box.Width - 2 * pad),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var y = box.Bottom - pad - ft.Height;
        dc.DrawText(ft, new Point(box.X + pad, y));
        dc.DrawText(site, new Point(box.X + pad, y - site.Height - titleSize * 0.15));
    }

    private static string ShortUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host + uri.AbsolutePath.TrimEnd('/');
        return url;
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
