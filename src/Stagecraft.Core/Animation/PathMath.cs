using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>
/// Motion path maths: the path as a polyline (curves through the points for smooth paths), its length, and the point
/// a given fraction of the way along it (by distance, so the speed along the path is even).
/// </summary>
public static class PathMath
{
    /// <summary>Points along the path (relative to the start centre), with curves subdivided.</summary>
    public static List<Point> Flatten(MotionPath path, int segmentsPerSpan = 16)
    {
        var pts = path.Points.Select(p => new Point(p.X, p.Y)).ToList();
        if (pts.Count == 0) return new List<Point> { new(0, 0) };
        if (path.Closed && pts.Count > 2 && (pts[0] - pts[pts.Count - 1]).Length > 0.001) pts.Add(pts[0]);
        if (!path.Smooth || pts.Count < 3) return pts;

        // Catmull-Rom (centripetal enough for hand-drawn input) through every point.
        var result = new List<Point> { pts[0] };
        for (var i = 0; i < pts.Count - 1; i++)
        {
            Point p0, p3;
            if (path.Closed)
            {
                // Closed: wrap around (the last point repeats the first).
                var n = pts.Count - 1;
                p0 = pts[(i - 1 + n) % n];
                p3 = pts[(i + 2) % n];
            }
            else
            {
                p0 = i > 0 ? pts[i - 1] : pts[i] + (pts[i] - pts[i + 1]);
                p3 = i + 2 < pts.Count ? pts[i + 2] : pts[i + 1] + (pts[i + 1] - pts[i]);
            }
            var p1 = pts[i];
            var p2 = pts[i + 1];
            for (var s = 1; s <= segmentsPerSpan; s++)
            {
                var t = s / (double)segmentsPerSpan;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        return result;
    }

    private static Point CatmullRom(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        double C(double a, double b, double c, double d) =>
            0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
        return new Point(C(p0.X, p1.X, p2.X, p3.X), C(p0.Y, p1.Y, p2.Y, p3.Y));
    }

    /// <summary>Total length of a polyline.</summary>
    public static double Length(IReadOnlyList<Point> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).Length;
        return total;
    }

    /// <summary>The point <paramref name="fraction"/> (0..1) of the way along the polyline, by distance.</summary>
    public static Point At(IReadOnlyList<Point> points, double fraction)
    {
        if (points.Count == 0) return new Point(0, 0);
        if (points.Count == 1) return points[0];
        var total = Length(points);
        if (total < 1e-9) return points[0];
        var target = Easing.Clamp01(fraction) * total;
        var walked = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var seg = (points[i] - points[i - 1]).Length;
            if (walked + seg >= target)
            {
                var k = seg < 1e-12 ? 0 : (target - walked) / seg;
                return points[i - 1] + (points[i] - points[i - 1]) * k;
            }
            walked += seg;
        }
        return points[points.Count - 1];
    }

    /// <summary>The offset of the element's centre after a fraction of the motion (the first point is the start).</summary>
    public static Vector Offset(MotionPath path, double fraction)
    {
        var flat = Flatten(path);
        var start = flat[0];
        return At(flat, fraction) - start;
    }

    /// <summary>The path as WPF geometry (for drawing it in the editor), offset to <paramref name="origin"/>.</summary>
    public static Geometry ToGeometry(MotionPath path, Point origin)
    {
        var flat = Flatten(path);
        var figure = new PathFigure { StartPoint = flat[0] + (Vector)origin, IsClosed = false, IsFilled = false };
        for (var i = 1; i < flat.Count; i++) figure.Segments.Add(new LineSegment(flat[i] + (Vector)origin, true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        g.Freeze();
        return g;
    }

    /// <summary>
    /// Simplifies a hand-drawn stroke (Ramer–Douglas–Peucker) so a drawn path keeps a handful of editable points.
    /// </summary>
    public static List<Point> Simplify(IReadOnlyList<Point> points, double tolerance)
    {
        if (points.Count < 3) return points.ToList();
        var keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double maxD = 0;
            var index = -1;
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i], points[a], points[b]);
                if (d > maxD)
                {
                    maxD = d;
                    index = i;
                }
            }
            if (index >= 0 && maxD > tolerance)
            {
                keep[index] = true;
                stack.Push((a, index));
                stack.Push((index, b));
            }
        }
        var result = new List<Point>();
        for (var i = 0; i < points.Count; i++)
            if (keep[i]) result.Add(points[i]);
        return result;
    }

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len = ab.LengthSquared;
        if (len < 1e-12) return (p - a).Length;
        var t = Easing.Clamp01(Vector.Multiply(p - a, ab) / len);
        return (p - (a + t * ab)).Length;
    }
}
