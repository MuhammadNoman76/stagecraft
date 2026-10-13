using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Export;

public sealed class PdfExportOptions
{
    /// <summary>Hidden slides are left out unless this is on.</summary>
    public bool IncludeHidden { get; set; }

    /// <summary>Vector pages (sharp at any zoom, small files). Off: every page is a high-resolution picture.</summary>
    public bool Vector { get; set; } = true;

    /// <summary>Vector pages: add an invisible text layer so the words can be selected, copied and searched.</summary>
    public bool SelectableText { get; set; } = true;

    /// <summary>Pixel width of pages in picture mode, and of the pieces vector mode has to draw as pictures.</summary>
    public int RasterWidth { get; set; } = 2560;

    /// <summary>Called after each page with (done, total).</summary>
    public Action<int, int>? Progress { get; set; }
}

/// <summary>
/// PDF export with PDFsharp. Each slide is drawn by the same renderer as the editor into a WPF drawing, which is
/// then translated into PDF vector graphics: shapes and gradients stay vectors, pictures are embedded, and text
/// becomes vector outlines (identical to the screen, sharp at any zoom) with an invisible text layer on top, so the
/// words can still be selected, copied and searched. The few things
/// PDF can't express directly (soft glows, see-through gradients) are embedded as high-resolution pictures.
/// </summary>
public static class PdfExporter
{
    /// <summary>One slide unit is half a point (a 1920 x 1080 slide is a 960 x 540 pt page).</summary>
    public const double PointsPerUnit = 0.5;

    public static void Export(Deck deck, MediaStore media, string path, PdfExportOptions options)
    {
        var temp = path + ".part";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                Export(deck, media, stream, options);
            Compat.MoveOverwrite(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public static void Export(Deck deck, MediaStore media, Stream output, PdfExportOptions options)
    {
        var slides = deck.Slides.Where(s => options.IncludeHidden || !s.Hidden).ToList();
        using var document = new PdfDocument();
        document.Info.Title = deck.Title;
        document.Info.Creator = "Stagecraft";
        var ctx = new RenderContext(deck, media);
        var images = new Dictionary<ImageSource, XImage>();

        for (var i = 0; i < slides.Count; i++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(deck.Width * PointsPerUnit);
            page.Height = XUnit.FromPoint(deck.Height * PointsPerUnit);
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.ScaleTransform(PointsPerUnit);
                if (options.Vector)
                {
                    var drawing = SlideRenderer.ToDrawing(slides[i], ctx);
                    var writer = new Writer(gfx, options.RasterWidth / deck.Width, images) { SelectableText = options.SelectableText };
                    writer.Draw(drawing, 1.0);
                }
                else
                {
                    var bitmap = SlideRenderer.Render(slides[i], ctx, options.RasterWidth);
                    using var ms = new MemoryStream(MediaStore.EncodeJpeg(bitmap, 92));
                    var image = XImage.FromStream(ms);
                    gfx.DrawImage(image, 0, 0, deck.Width, deck.Height);
                }
            }
            options.Progress?.Invoke(i + 1, slides.Count);
        }
        if (slides.Count == 0)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(deck.Width * PointsPerUnit);
            page.Height = XUnit.FromPoint(deck.Height * PointsPerUnit);
        }
        document.Save(output, false);
    }

    /// <summary>
    /// The font of the invisible text layer: the bundled Inter (SIL OFL, embedding allowed), subset into the PDF.
    /// Only its character codes matter; the visible text is drawn as outlines in the slide's real fonts.
    /// </summary>
    private static class TextLayer
    {
        private const string Face = "StagecraftTextLayer";
        private static readonly object Gate = new();
        private static bool? s_ready;

        public static readonly XBrush Invisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

        public static bool Ready
        {
            get
            {
                lock (Gate)
                {
                    if (s_ready.HasValue) return s_ready.Value;
                    try
                    {
                        var file = FontCatalog.FindFile("Inter", 400, false);
                        if (file == null || !File.Exists(file))
                        {
                            s_ready = false;
                        }
                        else
                        {
                            GlobalFontSettings.FontResolver ??= new Resolver(File.ReadAllBytes(file));
                            s_ready = GlobalFontSettings.FontResolver is Resolver;
                        }
                    }
                    catch (Exception)
                    {
                        s_ready = false;
                    }
                    return s_ready.Value;
                }
            }
        }

        public static XFont Font(double size) => new(Face, size, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));

        private sealed class Resolver : IFontResolver
        {
            private readonly byte[] _data;

            public Resolver(byte[] data)
            {
                _data = data;
            }

            public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(Face);

            public byte[] GetFont(string faceName) => _data;
        }
    }

    /// <summary>Walks a WPF drawing and issues the matching PDFsharp calls.</summary>
    private sealed class Writer
    {
        private readonly XGraphics _gfx;
        private readonly double _rasterScale;
        private readonly Dictionary<ImageSource, XImage> _images;

        public Writer(XGraphics gfx, double rasterScale, Dictionary<ImageSource, XImage> images)
        {
            _gfx = gfx;
            _rasterScale = Math.Max(1, rasterScale);
            _images = images;
        }

        public void Draw(Drawing drawing, double opacity)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    DrawGroup(group, opacity);
                    break;
                case GeometryDrawing geometry:
                    DrawGeometry(geometry, opacity);
                    break;
                case GlyphRunDrawing glyphs:
                    DrawGlyphs(glyphs, opacity);
                    break;
                case ImageDrawing image:
                    DrawImage(image, opacity);
                    break;
                default:
                    Rasterize(drawing, opacity);
                    break;
            }
        }

        private void DrawGroup(DrawingGroup group, double opacity)
        {
            if (group.OpacityMask != null || group.BitmapEffect != null)
            {
                Rasterize(group, opacity);
                return;
            }
            var state = _gfx.Save();
            try
            {
                if (group.Transform != null && !group.Transform.Value.IsIdentity)
                {
                    var m = group.Transform.Value;
                    _gfx.MultiplyTransform(new XMatrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY));
                }
                if (group.ClipGeometry != null)
                {
                    var clip = ToPath(group.ClipGeometry);
                    if (clip != null) _gfx.IntersectClip(clip);
                }
                var o = opacity * group.Opacity;
                foreach (var child in group.Children) Draw(child, o);
            }
            finally
            {
                _gfx.Restore(state);
            }
        }

        private void DrawGeometry(GeometryDrawing drawing, double opacity)
        {
            if (drawing.Geometry == null) return;
            XBrush? brush = null;
            if (drawing.Brush != null)
            {
                brush = ToBrush(drawing.Brush, drawing.Geometry.Bounds, opacity);
                if (brush == null)
                {
                    Rasterize(drawing, opacity);
                    return;
                }
            }
            XPen? pen = null;
            if (drawing.Pen is { Brush: SolidColorBrush penBrush } p && p.Thickness > 0)
            {
                pen = new XPen(ToColor(penBrush.Color, penBrush.Opacity * opacity), p.Thickness)
                {
                    LineCap = p.StartLineCap switch { PenLineCap.Round => XLineCap.Round, PenLineCap.Square => XLineCap.Square, _ => XLineCap.Flat },
                    LineJoin = p.LineJoin switch { PenLineJoin.Round => XLineJoin.Round, PenLineJoin.Bevel => XLineJoin.Bevel, _ => XLineJoin.Miter },
                };
                if (p.DashStyle != null && p.DashStyle.Dashes.Count > 0)
                {
                    pen.DashStyle = XDashStyle.Custom;
                    pen.DashPattern = p.DashStyle.Dashes.Select(d => Math.Max(0.01, d)).ToArray();
                }
            }
            else if (drawing.Pen?.Brush != null && drawing.Pen.Brush is not SolidColorBrush)
            {
                Rasterize(drawing, opacity);
                return;
            }
            if (brush == null && pen == null) return;
            var path = ToPath(drawing.Geometry);
            if (path == null) return;
            if (brush != null && pen != null) _gfx.DrawPath(pen, brush, path);
            else if (brush != null) _gfx.DrawPath(brush, path);
            else _gfx.DrawPath(pen!, path);
        }

        private void DrawGlyphs(GlyphRunDrawing drawing, double opacity)
        {
            if (drawing.GlyphRun == null || drawing.ForegroundBrush == null) return;
            var geometry = drawing.GlyphRun.BuildGeometry();
            var brush = ToBrush(drawing.ForegroundBrush, geometry.Bounds, opacity);
            if (brush == null)
            {
                Rasterize(drawing, opacity);
            }
            else
            {
                var path = ToPath(geometry);
                if (path != null) _gfx.DrawPath(brush, path);
            }
            if (_selectableText) DrawInvisibleText(drawing.GlyphRun);
        }

        private bool _selectableText;

        public bool SelectableText
        {
            get => _selectableText;
            set => _selectableText = value && TextLayer.Ready;
        }

        /// <summary>
        /// The words of a glyph run as invisible PDF text over the outlines, stretched to the same width, so the
        /// text can be selected, copied and searched.
        /// </summary>
        private void DrawInvisibleText(GlyphRun run)
        {
            try
            {
                var chars = run.Characters;
                if (chars == null || chars.Count == 0) return;
                var text = new string(chars.ToArray()).Replace('​', ' ');
                if (string.IsNullOrWhiteSpace(text)) return;
                var size = run.FontRenderingEmSize;
                if (size <= 0.5) return;
                var advance = run.AdvanceWidths.Sum();
                var font = TextLayer.Font(size);
                var measured = _gfx.MeasureString(text, font).Width;
                var state = _gfx.Save();
                try
                {
                    _gfx.TranslateTransform(run.BaselineOrigin.X, run.BaselineOrigin.Y);
                    if (measured > 0.01 && advance > 0.01) _gfx.ScaleTransform(advance / measured, 1);
                    _gfx.DrawString(text, font, TextLayer.Invisible, 0, 0, XStringFormats.BaseLineLeft);
                }
                finally
                {
                    _gfx.Restore(state);
                }
            }
            catch (Exception)
            {
                // The text layer is a bonus: the page itself is already drawn.
                _selectableText = false;
            }
        }

        private void DrawImage(ImageDrawing drawing, double opacity)
        {
            if (drawing.ImageSource is not BitmapSource bitmap) return;
            if (opacity < 0.999)
            {
                Rasterize(drawing, opacity);
                return;
            }
            if (!_images.TryGetValue(bitmap, out var image))
            {
                var bytes = HasTransparency(bitmap) ? MediaStore.EncodePng(bitmap) : MediaStore.EncodeJpeg(bitmap, 92);
                image = XImage.FromStream(new MemoryStream(bytes));
                _images[bitmap] = image;
            }
            var r = drawing.Rect;
            _gfx.DrawImage(image, r.X, r.Y, r.Width, r.Height);
        }

        private static bool HasTransparency(BitmapSource bitmap)
        {
            try
            {
                var src = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var stride = src.PixelWidth * 4;
                var pixels = new byte[stride * src.PixelHeight];
                src.CopyPixels(pixels, stride, 0);
                for (var i = 3; i < pixels.Length; i += 4)
                    if (pixels[i] < 250) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Draws something PDF can't express as a picture of itself (in its own coordinate space).</summary>
        private void Rasterize(Drawing drawing, double opacity)
        {
            var bounds = drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            var scale = _rasterScale;
            var w = (int)Math.Ceiling(bounds.Width * scale);
            var h = (int)Math.Ceiling(bounds.Height * scale);
            var max = 3000.0;
            if (w > max || h > max)
            {
                var k = Math.Min(max / w, max / h);
                w = Math.Max(1, (int)(w * k));
                h = Math.Max(1, (int)(h * k));
                scale = w / bounds.Width;
            }
            if (w < 1 || h < 1) return;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                dc.PushOpacity(opacity);
                dc.DrawDrawing(drawing);
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var image = XImage.FromStream(new MemoryStream(MediaStore.EncodePng(bitmap)));
            _gfx.DrawImage(image, bounds.X, bounds.Y, w / scale, h / scale);
        }

        private static XColor ToColor(Color c, double opacity)
        {
            var a = MathEx.Clamp(c.A / 255.0 * opacity, 0, 1);
            return XColor.FromArgb((int)Math.Round(a * 255), c.R, c.G, c.B);
        }

        /// <summary>Solid colours and opaque two-colour linear gradients; null for anything else (rasterized).</summary>
        private static XBrush? ToBrush(Brush brush, Rect bounds, double opacity)
        {
            switch (brush)
            {
                case SolidColorBrush solid:
                    return new XSolidBrush(ToColor(solid.Color, solid.Opacity * opacity));
                case LinearGradientBrush linear when linear.GradientStops.Count == 2 && linear.Transform.Value.IsIdentity && linear.RelativeTransform.Value.IsIdentity
                                                     && linear.GradientStops.All(s => s.Color.A == 255) && opacity * linear.Opacity > 0.999:
                {
                    var stops = linear.GradientStops.OrderBy(s => s.Offset).ToList();
                    var start = linear.StartPoint;
                    var end = linear.EndPoint;
                    if (linear.MappingMode == BrushMappingMode.RelativeToBoundingBox)
                    {
                        start = new Point(bounds.X + start.X * bounds.Width, bounds.Y + start.Y * bounds.Height);
                        end = new Point(bounds.X + end.X * bounds.Width, bounds.Y + end.Y * bounds.Height);
                    }
                    // Stops not at 0 and 1: move the end points so the colours change over the same stretch.
                    var v = end - start;
                    var s0 = stops[0].Offset;
                    var s1 = stops[1].Offset;
                    var p0 = start + v * s0;
                    var p1 = start + v * s1;
                    if ((p1 - p0).Length < 0.01) return new XSolidBrush(ToColor(stops[1].Color, 1));
                    return new XLinearGradientBrush(new XPoint(p0.X, p0.Y), new XPoint(p1.X, p1.Y), ToColor(stops[0].Color, 1), ToColor(stops[1].Color, 1));
                }
                default:
                    return null;
            }
        }

        private static XGraphicsPath? ToPath(Geometry geometry)
        {
            PathGeometry pg;
            try
            {
                pg = PathGeometry.CreateFromGeometry(geometry);
            }
            catch
            {
                return null;
            }
            var path = new XGraphicsPath { FillMode = pg.FillRule == FillRule.EvenOdd ? XFillMode.Alternate : XFillMode.Winding };
            var transform = pg.Transform != null && !pg.Transform.Value.IsIdentity ? pg.Transform.Value : (Matrix?)null;
            XPoint T(Point p)
            {
                if (transform is { } m) p = m.Transform(p);
                return new XPoint(p.X, p.Y);
            }

            foreach (var figure in pg.Figures)
            {
                path.StartFigure();
                var current = figure.StartPoint;
                var hasArc = figure.Segments.Any(s => s is ArcSegment);
                if (hasArc && transform != null)
                {
                    // Arcs under a transform: flatten this figure.
                    var single = new PathGeometry(new[] { figure }) { Transform = pg.Transform };
                    var flat = single.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
                    foreach (var ff in flat.Figures)
                    {
                        var pts = new List<XPoint> { new(ff.StartPoint.X, ff.StartPoint.Y) };
                        foreach (var seg in ff.Segments)
                        {
                            if (seg is PolyLineSegment pls) pts.AddRange(pls.Points.Select(p => new XPoint(p.X, p.Y)));
                            else if (seg is LineSegment ls) pts.Add(new XPoint(ls.Point.X, ls.Point.Y));
                        }
                        if (pts.Count > 1) path.AddLines(pts.ToArray());
                        if (ff.IsClosed) path.CloseFigure();
                    }
                    continue;
                }
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment line:
                            path.AddLine(T(current), T(line.Point));
                            current = line.Point;
                            break;
                        case PolyLineSegment poly:
                            foreach (var p in poly.Points)
                            {
                                path.AddLine(T(current), T(p));
                                current = p;
                            }
                            break;
                        case BezierSegment bezier:
                            path.AddBezier(T(current), T(bezier.Point1), T(bezier.Point2), T(bezier.Point3));
                            current = bezier.Point3;
                            break;
                        case PolyBezierSegment polyBezier:
                            for (var i = 0; i + 2 < polyBezier.Points.Count; i += 3)
                            {
                                path.AddBezier(T(current), T(polyBezier.Points[i]), T(polyBezier.Points[i + 1]), T(polyBezier.Points[i + 2]));
                                current = polyBezier.Points[i + 2];
                            }
                            break;
                        case QuadraticBezierSegment quad:
                            AddQuadratic(path, current, quad.Point1, quad.Point2, T);
                            current = quad.Point2;
                            break;
                        case PolyQuadraticBezierSegment polyQuad:
                            for (var i = 0; i + 1 < polyQuad.Points.Count; i += 2)
                            {
                                AddQuadratic(path, current, polyQuad.Points[i], polyQuad.Points[i + 1], T);
                                current = polyQuad.Points[i + 1];
                            }
                            break;
                        case ArcSegment arc:
                            path.AddArc(T(current), T(arc.Point), new XSize(arc.Size.Width, arc.Size.Height), arc.RotationAngle, arc.IsLargeArc,
                                arc.SweepDirection == SweepDirection.Clockwise ? XSweepDirection.Clockwise : XSweepDirection.Counterclockwise);
                            current = arc.Point;
                            break;
                    }
                }
                if (figure.IsClosed) path.CloseFigure();
            }
            return path;
        }

        private static void AddQuadratic(XGraphicsPath path, Point p0, Point q, Point p3, Func<Point, XPoint> t)
        {
            var c1 = new Point(p0.X + 2.0 / 3 * (q.X - p0.X), p0.Y + 2.0 / 3 * (q.Y - p0.Y));
            var c2 = new Point(p3.X + 2.0 / 3 * (q.X - p3.X), p3.Y + 2.0 / 3 * (q.Y - p3.Y));
            path.AddBezier(t(p0), t(c1), t(c2), t(p3));
        }
    }
}
