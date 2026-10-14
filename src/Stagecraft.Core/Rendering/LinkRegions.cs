using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>A clickable area of a slide and where it leads.</summary>
public sealed class LinkRegion
{
    public LinkRegion(Element element, Hyperlink link, Geometry area, bool isText)
    {
        Element = element;
        Link = link;
        Area = area;
        IsText = isText;
    }

    public Element Element { get; }
    public Hyperlink Link { get; }

    /// <summary>The area in slide coordinates (rotated with its element).</summary>
    public Geometry Area { get; }

    /// <summary>A link on part of the text (rather than the whole element).</summary>
    public bool IsText { get; }

    public Rect Bounds => Area.Bounds;

    public bool Contains(Point p) => Area.FillContains(p);
}

/// <summary>
/// Where the links of a slide are: whole elements with a link (and online videos, which open their page), and linked
/// text, line by line. The slide show uses it for the hand cursor and clicks, the PDF export for link annotations and
/// the editor for Ctrl+click.
/// </summary>
public static class LinkRegions
{
    /// <summary>Every link area of the slide, top-most first (the order to test clicks in).</summary>
    public static List<LinkRegion> For(Slide slide, RenderContext ctx)
    {
        var style = ctx.StyleFor(slide);
        var result = new List<LinkRegion>();
        for (var i = slide.Elements.Count - 1; i >= 0; i--)
        {
            var e = slide.Elements[i];
            if (!ctx.Editing && ElementRenderer.IsHiddenInShow(e)) continue;
            var rotate = Math.Abs(e.Rotation % 360) > 0.001 ? new RotateTransform(e.Rotation, e.Center.X, e.Center.Y) : null;

            // Linked text first: it sits on top of the element's own link.
            var body = Animation.TextUnits.BodyOf(e);
            if (body != null && !body.Content.IsEmpty)
            {
                var box = e is ShapeElement s ? ElementRenderer.TextBoxOf(s, e.Bounds) : e.Bounds;
                foreach (var (area, link) in TextLayout.LinkAreas(body, box, style))
                {
                    var g = new RectangleGeometry(area);
                    if (rotate != null) g.Transform = rotate;
                    g.Freeze();
                    result.Add(new LinkRegion(e, link, g, isText: true));
                }
            }

            var elementLink = e.Link ?? (e is OnlineVideoElement online && !string.IsNullOrWhiteSpace(online.Url) ? Hyperlink.Web(online.Url, online.Title) : null);
            if (elementLink != null)
            {
                Geometry outline = e is ShapeElement shape && !shape.IsLine ? ElementRenderer.ShapeGeometry(shape, e.Bounds).Clone() : new RectangleGeometry(e.Bounds);
                if (e is ShapeElement { IsLine: true } line)
                {
                    var pts = ElementRenderer.LinePath(line, line.Bounds);
                    var figure = new PathFigure { StartPoint = pts[0] };
                    for (var k = 1; k < pts.Count; k++) figure.Segments.Add(new LineSegment(pts[k], true));
                    var path = new PathGeometry(new[] { figure });
                    outline = path.GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(16, ElementRenderer.LineStroke(line).Width + 12)));
                }
                if (rotate != null) outline.Transform = rotate;
                outline.Freeze();
                result.Add(new LinkRegion(e, elementLink, outline, isText: false));
            }
        }
        return result;
    }

    /// <summary>The link at a slide point (top-most), or null.</summary>
    public static LinkRegion? At(IEnumerable<LinkRegion> regions, Point p) => regions.FirstOrDefault(r => r.Contains(p));
}
