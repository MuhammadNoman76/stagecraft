using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>A motion path as the canvas shows it: the route on the slide (absolute points) for one effect.</summary>
public sealed class PathView
{
    public PathView(AnimationEffect effect, Point origin, List<Point> route, Rect targetBounds)
    {
        Effect = effect;
        Origin = origin;
        Route = route;
        TargetBounds = targetBounds;
    }

    public AnimationEffect Effect { get; }

    /// <summary>The centre of the element where the motion starts (earlier motion paths of the same element included).</summary>
    public Point Origin { get; }

    /// <summary>The drawn route on the slide (curves flattened).</summary>
    public List<Point> Route { get; }

    /// <summary>The element's box where the motion starts.</summary>
    public Rect TargetBounds { get; }
}

public sealed partial class SlideCanvas
{
    private string? _drawPathEffect;
    private List<Point>? _drawn;
    private (string EffectId, int Index)? _pathNode;
    private PathPoint? _pathNodeStart;

    /// <summary>The next drag on the slide draws a motion path for this effect.</summary>
    public void BeginDrawPath(string effectId)
    {
        _drawPathEffect = effectId;
        Cursor = Cursors.Pen;
        Focus();
        if (Window.GetWindow(this) is MainWindow window) window.ShowMessage("Draw the path on the slide: press, drag where the element should go, release. Esc cancels.", 6);
    }

    public bool IsDrawingPath => _drawPathEffect != null;

    private void CancelDrawPath()
    {
        _drawPathEffect = null;
        _drawn = null;
        Cursor = null;
        _overlay.InvalidateVisual();
    }

    /// <summary>The motion paths of the current slide, each starting where the element is when it begins.</summary>
    public List<PathView> MotionPaths()
    {
        var result = new List<PathView>();
        var slide = _state?.CurrentSlide;
        if (slide == null) return result;
        var offsets = new Dictionary<string, Vector>();
        foreach (var a in slide.Animations)
        {
            if (a.Class != EffectClass.Motion || a.Path == null) continue;
            var targets = AnimationRules.Resolve(slide, a.Target);
            if (targets.Count == 0) continue;
            var bounds = DeckOperations.BoundsOf(targets);
            offsets.TryGetValue(a.Target, out var before);
            var origin = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) + before;
            var flat = PathMath.Flatten(a.Path);
            var start = flat[0];
            var route = flat.Select(p => origin + (p - start)).ToList();
            bounds.Offset(before);
            result.Add(new PathView(a, origin, route, bounds));
            if (!a.Rewind) offsets[a.Target] = before + (flat[flat.Count - 1] - start);
        }
        return result;
    }

    /// <summary>The editable points of a path on the slide (absolute).</summary>
    private static List<Point> Nodes(PathView view)
    {
        var path = view.Effect.Path!;
        var first = path.Points[0];
        return path.Points.Select(p => new Point(view.Origin.X + p.X - first.X, view.Origin.Y + p.Y - first.Y)).ToList();
    }

    /// <summary>The path node under a viewport point (only on selected motion effects in the animation pane).</summary>
    private (string EffectId, int Index)? PathNodeAt(Point view)
    {
        if (_state == null || !_state.AnimationMode) return null;
        foreach (var pv in MotionPaths().Where(p => _state.SelectedEffects.Contains(p.Effect.Id)))
        {
            var nodes = Nodes(pv);
            // The first point is the element itself; the others can be dragged.
            for (var i = nodes.Count - 1; i >= 1; i--)
                if ((ToView(nodes[i]) - view).Length <= 7) return (pv.Effect.Id, i);
        }
        return null;
    }

    /// <summary>Mouse down while drawing a path or on a path node. Returns true when handled.</summary>
    private bool AnimationMouseDown(Point view, Point p)
    {
        if (_state?.CurrentSlide is not { } slide) return false;
        if (_drawPathEffect != null)
        {
            _drawn = new List<Point> { p };
            _mode = DragMode.DrawPath;
            _viewport.CaptureMouse();
            return true;
        }
        var node = PathNodeAt(view);
        if (node == null) return false;
        var effect = slide.Animations.FirstOrDefault(a => a.Id == node.Value.EffectId);
        if (effect?.Path == null) return false;
        _pathNode = node;
        _pathNodeStart = effect.Path.Points[node.Value.Index].Clone();
        _before = _state.Session.Snapshot(slide);
        _downSlide = p;
        _mode = DragMode.PathNode;
        _changed = false;
        _viewport.CaptureMouse();
        return true;
    }

    private void DoDrawPath(Point p)
    {
        if (_drawn == null) return;
        if (_drawn.Count == 0 || (p - _drawn[_drawn.Count - 1]).Length * Zoom >= 3) _drawn.Add(p);
        _overlay.InvalidateVisual();
    }

    private void DoPathNode(Point p)
    {
        if (_state?.CurrentSlide is not { } slide || _pathNode == null || _pathNodeStart == null) return;
        var effect = slide.Animations.FirstOrDefault(a => a.Id == _pathNode.Value.EffectId);
        if (effect?.Path == null) return;
        var dx = p.X - _downSlide.X;
        var dy = p.Y - _downSlide.Y;
        var index = _pathNode.Value.Index;
        effect.Path.Points[index].X = Math.Round(_pathNodeStart.X + dx, 2);
        effect.Path.Points[index].Y = Math.Round(_pathNodeStart.Y + dy, 2);
        // A reshaped arc or circle is a path of its own now.
        if (effect.Effect is EffectKind.Line or EffectKind.Arc or EffectKind.Circle && effect.Path.Points.Count > 2) effect.Effect = EffectKind.Custom;
        _changed = true;
        _overlay.InvalidateVisual();
    }

    /// <summary>Mouse up after drawing a path or dragging a node: records the change.</summary>
    private void EndAnimationDrag(DragMode mode)
    {
        var slide = _state?.CurrentSlide;
        if (mode == DragMode.DrawPath)
        {
            var id = _drawPathEffect;
            var drawn = _drawn;
            CancelDrawPath();
            if (slide == null || id == null || drawn == null || drawn.Count < 2) return;
            if (PathMath.Length(drawn) < 12) return;
            var simple = PathMath.Simplify(drawn, 3);
            var start = simple[0];
            var path = new MotionPath { Smooth = simple.Count > 2, Points = simple.Select(q => new PathPoint(Math.Round(q.X - start.X, 1), Math.Round(q.Y - start.Y, 1))).ToList() };
            PathDrawn?.Invoke(id, path);
            return;
        }
        if (mode == DragMode.PathNode)
        {
            if (slide != null && _before != null && _changed) _state!.Session.Commit(slide, _before, "Motion path");
            _pathNode = null;
            _pathNodeStart = null;
        }
    }

    /// <summary>A path was drawn for an effect (the editor records it).</summary>
    public event Action<string, MotionPath>? PathDrawn;

    /// <summary>Drawn while drawing a path (slide coordinates).</summary>
    internal IReadOnlyList<Point>? DrawnPath => _drawn;

    /// <summary>Ctrl+click on a link in the editor: the link under the point, if any.</summary>
    public Hyperlink? LinkAt(Point slidePoint)
    {
        var slide = _state?.CurrentSlide;
        if (slide == null) return null;
        var regions = Core.Rendering.LinkRegions.For(slide, _state!.RenderContext);
        return Core.Rendering.LinkRegions.At(regions, slidePoint)?.Link;
    }

    /// <summary>A link was Ctrl+clicked in the editor.</summary>
    public event Action<Hyperlink>? LinkFollowed;
}
