using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>Find results on the current slide, highlighted over the text.</summary>
public sealed partial class SlideCanvas
{
    private IReadOnlyList<FindMatch> _findMatches = Array.Empty<FindMatch>();
    private FindMatch? _findCurrent;

    /// <summary>Highlights find results on this slide (the current one stronger).</summary>
    public void SetFindHighlights(IReadOnlyList<FindMatch> matches, FindMatch? current)
    {
        _findMatches = matches;
        _findCurrent = current;
        _overlay.InvalidateVisual();
    }

    /// <summary>Areas (view coordinates, with the element's rotation) of each find result on the current slide.</summary>
    internal IEnumerable<(Geometry Area, bool Current)> FindAreas()
    {
        if (_findMatches.Count == 0 || _state?.CurrentSlide is not { } slide) yield break;
        var style = SlideStyle.For(_state.Deck, slide);
        foreach (var m in _findMatches)
        {
            if (m.SlideId != slide.Id || m.ElementId == null || slide.Find(m.ElementId) is not { } e) continue;
            if (IsEditingText && EditingElement == e) continue;
            var rects = new List<Rect>();
            if (m.Area == FindArea.Text)
            {
                var bodies = FindReplace.Bodies(e);
                if (m.Body >= bodies.Count) continue;
                var box = e is ShapeElement s ? ElementRenderer.TextBoxOf(s, e.Bounds) : e.Bounds;
                try
                {
                    rects.AddRange(Core.Rendering.TextLayout.ModelRangeAreas(bodies[m.Body], box, style, m.Paragraph, m.Start, m.Length));
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "Find highlight");
                }
            }
            else
            {
                rects.Add(e.Bounds); // a table cell: the table
            }
            var group = new GeometryGroup();
            foreach (var r in rects)
            {
                var v = ToView(r);
                v.Inflate(1.5, 1);
                group.Children.Add(new RectangleGeometry(v, 2, 2));
            }
            if (Math.Abs(e.Rotation % 360) > 0.01)
            {
                var c = ToView(new Point(e.X + e.Width / 2, e.Y + e.Height / 2));
                group.Transform = new RotateTransform(e.Rotation, c.X, c.Y);
            }
            yield return (group, ReferenceEquals(m, _findCurrent) || Equals(m, _findCurrent));
        }
    }
}
