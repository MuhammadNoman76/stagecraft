using System.Windows;

namespace Stagecraft.Core.Editing;

public enum GuideKind { Edge, Center, Slide, Spacing, User }

/// <summary>A guide line drawn while something snaps (slide units).</summary>
public sealed class Guide
{
    /// <summary>True for a vertical line at X = <see cref="Position"/>, from Y = <see cref="From"/> to <see cref="To"/>.</summary>
    public bool Vertical { get; init; }
    public double Position { get; init; }
    public double From { get; init; }
    public double To { get; init; }
    public GuideKind Kind { get; init; }

    /// <summary>Spacing guides: the equal gaps (as segments along the axis) that were matched.</summary>
    public IReadOnlyList<(double Start, double End, double Cross)> Gaps { get; init; } = Array.Empty<(double, double, double)>();
}

public sealed class SnapResult
{
    public double Dx { get; init; }
    public double Dy { get; init; }
    public IReadOnlyList<Guide> Guides { get; init; } = Array.Empty<Guide>();
    public bool SnappedX { get; init; }
    public bool SnappedY { get; init; }
}

/// <summary>What things can snap to: the other elements, the slide's edges, centre and margins.</summary>
public sealed class SnapTargets
{
    public SnapTargets(IEnumerable<Rect> others, Size slide, double margin)
    {
        Others = others.Where(r => !r.IsEmpty && r.Width >= 0 && r.Height >= 0).ToList();
        Slide = slide;
        Margin = margin;
    }

    public IReadOnlyList<Rect> Others { get; }
    public Size Slide { get; }
    public double Margin { get; }

    /// <summary>Smart guides: other elements, the slide's edges, centre and margins, equal spacing.</summary>
    public bool Smart { get; init; } = true;

    /// <summary>The presentation's own guides (View › guides): always snapped to.</summary>
    public IReadOnlyList<Model.GuideLine> UserGuides { get; init; } = Array.Empty<Model.GuideLine>();

    /// <summary>Grid spacing to snap to (0 = no grid). Smart guides and user guides win when they're close.</summary>
    public double Grid { get; init; }
}

/// <summary>
/// Smart guides: snaps a moving or resized box to the edges and centres of other elements, the slide's edges,
/// centre and margins, and to equal spacing between neighbours. Pure maths, so it's unit tested.
/// </summary>
public static class Snapper
{
    private enum Edge { Start, Center, End }

    private readonly struct Candidate
    {
        public Candidate(double value, GuideKind kind, Rect? source)
        {
            Value = value;
            Kind = kind;
            Source = source;
        }

        public double Value { get; }
        public GuideKind Kind { get; }
        public Rect? Source { get; }
    }

    /// <summary>Snaps a box being moved. <paramref name="threshold"/> is in slide units (a few screen pixels / zoom).</summary>
    public static SnapResult SnapMove(Rect moving, SnapTargets targets, double threshold, bool snapX = true, bool snapY = true)
    {
        var guides = new List<Guide>();
        double dx = 0, dy = 0;
        bool sx = false, sy = false;

        if (snapX)
        {
            var edge = BestEdge(moving, targets, threshold, horizontal: true, edges: new[] { Edge.Start, Edge.Center, Edge.End });
            var spacing = targets.Smart ? BestSpacing(moving, targets, threshold, horizontal: true) : null;
            if (spacing != null && (edge == null || Math.Abs(spacing.Value.Delta) < Math.Abs(edge.Value.Delta) - 0.01))
            {
                dx = spacing.Value.Delta;
                guides.Add(spacing.Value.Guide);
                sx = true;
            }
            else if (edge != null)
            {
                dx = edge.Value.Delta;
                sx = true;
            }
        }
        if (snapY)
        {
            var edge = BestEdge(moving, targets, threshold, horizontal: false, edges: new[] { Edge.Start, Edge.Center, Edge.End });
            var spacing = targets.Smart ? BestSpacing(moving, targets, threshold, horizontal: false) : null;
            if (spacing != null && (edge == null || Math.Abs(spacing.Value.Delta) < Math.Abs(edge.Value.Delta) - 0.01))
            {
                dy = spacing.Value.Delta;
                guides.Add(spacing.Value.Guide);
                sy = true;
            }
            else if (edge != null)
            {
                dy = edge.Value.Delta;
                sy = true;
            }
        }

        // Nothing close: the grid (the box's left and top edges go to the nearest grid line).
        if (snapX && !sx && targets.Grid > 0)
        {
            dx = GridDelta(moving.Left, targets.Grid);
            sx = true;
        }
        if (snapY && !sy && targets.Grid > 0)
        {
            dy = GridDelta(moving.Top, targets.Grid);
            sy = true;
        }
        var snapped = new Rect(moving.X + dx, moving.Y + dy, moving.Width, moving.Height);
        if (sx && !guides.Any(g => g.Kind == GuideKind.Spacing && !g.Vertical)) guides.AddRange(LinesAt(snapped, targets, vertical: true));
        if (sy && !guides.Any(g => g.Kind == GuideKind.Spacing && g.Vertical)) guides.AddRange(LinesAt(snapped, targets, vertical: false));
        return new SnapResult { Dx = dx, Dy = dy, Guides = guides, SnappedX = sx, SnappedY = sy };
    }

    /// <summary>
    /// Snaps the edges being dragged while resizing (-1 = left/top edge moves, 1 = right/bottom edge moves, 0 = not on that axis).
    /// Returns how far each moving edge should shift.
    /// </summary>
    public static SnapResult SnapResize(Rect box, int horizontalEdge, int verticalEdge, SnapTargets targets, double threshold)
    {
        double dx = 0, dy = 0;
        bool sx = false, sy = false;
        if (horizontalEdge != 0)
        {
            var e = BestEdge(box, targets, threshold, true, new[] { horizontalEdge < 0 ? Edge.Start : Edge.End });
            if (e != null)
            {
                dx = e.Value.Delta;
                sx = true;
            }
        }
        if (verticalEdge != 0)
        {
            var e = BestEdge(box, targets, threshold, false, new[] { verticalEdge < 0 ? Edge.Start : Edge.End });
            if (e != null)
            {
                dy = e.Value.Delta;
                sy = true;
            }
        }
        if (horizontalEdge != 0 && !sx && targets.Grid > 0)
        {
            dx = GridDelta(horizontalEdge < 0 ? box.Left : box.Right, targets.Grid);
            sx = true;
        }
        if (verticalEdge != 0 && !sy && targets.Grid > 0)
        {
            dy = GridDelta(verticalEdge < 0 ? box.Top : box.Bottom, targets.Grid);
            sy = true;
        }
        var snapped = box;
        if (sx)
        {
            if (horizontalEdge < 0) snapped = new Rect(box.X + dx, box.Y, Math.Max(0, box.Width - dx), box.Height);
            else snapped = new Rect(box.X, box.Y, Math.Max(0, box.Width + dx), box.Height);
        }
        if (sy)
        {
            if (verticalEdge < 0) snapped = new Rect(snapped.X, box.Y + dy, snapped.Width, Math.Max(0, box.Height - dy));
            else snapped = new Rect(snapped.X, snapped.Y, snapped.Width, Math.Max(0, box.Height + dy));
        }
        var guides = new List<Guide>();
        if (sx) guides.AddRange(LinesAt(snapped, targets, true).Where(g => Math.Abs(g.Position - (horizontalEdge < 0 ? snapped.Left : snapped.Right)) < 0.5));
        if (sy) guides.AddRange(LinesAt(snapped, targets, false).Where(g => Math.Abs(g.Position - (verticalEdge < 0 ? snapped.Top : snapped.Bottom)) < 0.5));
        return new SnapResult { Dx = dx, Dy = dy, Guides = guides, SnappedX = sx, SnappedY = sy };
    }

    private static IEnumerable<Candidate> Candidates(SnapTargets t, bool horizontal)
    {
        foreach (var g in t.UserGuides)
            if (g.Vertical == horizontal) yield return new Candidate(g.Position, GuideKind.User, null);
        if (!t.Smart) yield break;
        var size = horizontal ? t.Slide.Width : t.Slide.Height;
        yield return new Candidate(0, GuideKind.Slide, null);
        yield return new Candidate(size / 2, GuideKind.Slide, null);
        yield return new Candidate(size, GuideKind.Slide, null);
        if (t.Margin > 0)
        {
            yield return new Candidate(t.Margin, GuideKind.Slide, null);
            yield return new Candidate(size - t.Margin, GuideKind.Slide, null);
        }
        foreach (var r in t.Others)
        {
            yield return new Candidate(horizontal ? r.Left : r.Top, GuideKind.Edge, r);
            yield return new Candidate(horizontal ? r.Left + r.Width / 2 : r.Top + r.Height / 2, GuideKind.Center, r);
            yield return new Candidate(horizontal ? r.Right : r.Bottom, GuideKind.Edge, r);
        }
    }

    /// <summary>How far <paramref name="value"/> is from the nearest grid line.</summary>
    public static double GridDelta(double value, double grid) => grid > 0 ? Math.Round(value / grid) * grid - value : 0;

    private static double EdgeValue(Rect r, Edge e, bool horizontal) => (horizontal, e) switch
    {
        (true, Edge.Start) => r.Left,
        (true, Edge.Center) => r.Left + r.Width / 2,
        (true, Edge.End) => r.Right,
        (false, Edge.Start) => r.Top,
        (false, Edge.Center) => r.Top + r.Height / 2,
        _ => r.Bottom,
    };

    private static (double Delta, double Value)? BestEdge(Rect moving, SnapTargets t, double threshold, bool horizontal, Edge[] edges)
    {
        (double Delta, double Value)? best = null;
        foreach (var c in Candidates(t, horizontal))
        foreach (var e in edges)
        {
            var d = c.Value - EdgeValue(moving, e, horizontal);
            if (Math.Abs(d) > threshold) continue;
            if (best == null || Math.Abs(d) < Math.Abs(best.Value.Delta) - 1e-9) best = (d, c.Value);
        }
        return best;
    }

    /// <summary>Guide lines through every candidate the snapped box now lines up with.</summary>
    private static IEnumerable<Guide> LinesAt(Rect box, SnapTargets t, bool vertical)
    {
        var mine = new[] { Edge.Start, Edge.Center, Edge.End }.Select(e => EdgeValue(box, e, vertical)).ToArray();
        var found = new Dictionary<long, (double Pos, double From, double To, GuideKind Kind)>();
        foreach (var c in Candidates(t, vertical))
        {
            foreach (var m in mine)
            {
                if (Math.Abs(c.Value - m) > 0.5) continue;
                var key = (long)Math.Round(c.Value * 2);
                double from, to;
                if (c.Source is { } src)
                {
                    from = Math.Min(vertical ? src.Top : src.Left, vertical ? box.Top : box.Left);
                    to = Math.Max(vertical ? src.Bottom : src.Right, vertical ? box.Bottom : box.Right);
                }
                else
                {
                    from = 0;
                    to = vertical ? t.Slide.Height : t.Slide.Width;
                }
                if (found.TryGetValue(key, out var existing))
                {
                    from = Math.Min(from, existing.From);
                    to = Math.Max(to, existing.To);
                    var kind = existing.Kind == GuideKind.Slide ? existing.Kind : c.Kind;
                    found[key] = (c.Value, from, to, kind);
                }
                else
                {
                    found[key] = (c.Value, from, to, c.Kind);
                }
            }
        }
        return found.Values.Select(f => new Guide { Vertical = vertical, Position = f.Pos, From = f.From, To = f.To, Kind = f.Kind });
    }

    /// <summary>Equal spacing: the moving box keeps the same gap to its neighbour as other neighbours have, or sits halfway between two.</summary>
    private static (double Delta, Guide Guide)? BestSpacing(Rect moving, SnapTargets t, double threshold, bool horizontal)
    {
        double Start(Rect r) => horizontal ? r.Left : r.Top;
        double End(Rect r) => horizontal ? r.Right : r.Bottom;
        double CrossStart(Rect r) => horizontal ? r.Top : r.Left;
        double CrossEnd(Rect r) => horizontal ? r.Bottom : r.Right;
        bool Overlaps(Rect a, Rect b) => CrossStart(a) < CrossEnd(b) && CrossStart(b) < CrossEnd(a);

        // Elements in the same row (or column) as the moving box.
        var band = t.Others.Where(r => Overlaps(r, moving)).OrderBy(Start).ToList();
        if (band.Count == 0) return null;

        var before = band.Where(r => End(r) <= Start(moving) + threshold).OrderByDescending(End).FirstOrDefault();
        var after = band.Where(r => Start(r) >= End(moving) - threshold).OrderBy(Start).FirstOrDefault();
        var hasBefore = band.Any(r => End(r) <= Start(moving) + threshold);
        var hasAfter = band.Any(r => Start(r) >= End(moving) - threshold);

        // Gaps that already exist between neighbours in the row.
        var gaps = new List<(double Gap, Rect A, Rect B)>();
        for (var i = 0; i < band.Count; i++)
        for (var j = 0; j < band.Count; j++)
        {
            if (i == j) continue;
            var a = band[i];
            var b = band[j];
            var gap = Start(b) - End(a);
            if (gap <= 0.5) continue;
            // Only direct neighbours (nothing between them).
            if (band.Any(c => c != a && c != b && Start(c) >= End(a) - 0.5 && End(c) <= Start(b) + 0.5)) continue;
            gaps.Add((gap, a, b));
        }

        (double Delta, Guide Guide)? best = null;
        void Consider(double delta, Func<Rect, Guide> guide)
        {
            if (Math.Abs(delta) > threshold) return;
            if (best == null || Math.Abs(delta) < Math.Abs(best.Value.Delta) - 1e-9)
            {
                var snapped = horizontal ? new Rect(moving.X + delta, moving.Y, moving.Width, moving.Height) : new Rect(moving.X, moving.Y + delta, moving.Width, moving.Height);
                best = (delta, guide(snapped));
            }
        }

        Guide Make(Rect snapped, IEnumerable<(Rect A, Rect B)> pairs)
        {
            var segments = pairs.Select(p =>
            {
                var cross = (Math.Max(CrossStart(p.A), CrossStart(p.B)) + Math.Min(CrossEnd(p.A), CrossEnd(p.B))) / 2;
                return (End(p.A), Start(p.B), cross);
            }).ToList();
            return new Guide
            {
                Vertical = !horizontal,
                Kind = GuideKind.Spacing,
                Position = horizontal ? snapped.Top + snapped.Height / 2 : snapped.Left + snapped.Width / 2,
                From = segments.Min(s => s.Item1),
                To = segments.Max(s => s.Item2),
                Gaps = segments,
            };
        }

        if (hasBefore && before != Rect.Empty)
        {
            foreach (var (gap, a, b) in gaps)
            {
                var target = End(before) + gap;
                Consider(target - Start(moving), s => Make(s, new[] { (a, b), (before, s) }));
            }
        }
        if (hasAfter && after != Rect.Empty)
        {
            foreach (var (gap, a, b) in gaps)
            {
                var target = Start(after) - gap - (End(moving) - Start(moving));
                Consider(target - Start(moving), s => Make(s, new[] { (a, b), (s, after) }));
            }
        }
        if (hasBefore && hasAfter && before != Rect.Empty && after != Rect.Empty)
        {
            var room = Start(after) - End(before) - (End(moving) - Start(moving));
            if (room > 0)
            {
                var target = End(before) + room / 2;
                Consider(target - Start(moving), s => Make(s, new[] { (before, s), (s, after) }));
            }
        }
        return best;
    }
}
