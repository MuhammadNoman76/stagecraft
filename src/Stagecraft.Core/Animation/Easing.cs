namespace Stagecraft.Core.Animation;

/// <summary>Easing curves for transitions, Morph and builds (input and output 0..1).</summary>
public static class Easing
{
    /// <summary>Smooth start and end (CSS cubic-bezier(0.4, 0, 0.2, 1)), for most motion.</summary>
    public static double Standard(double t) => CubicBezier(0.4, 0.0, 0.2, 1.0, t);

    /// <summary>Fast start, gentle stop (cubic-bezier(0, 0, 0.2, 1)), for things arriving.</summary>
    public static double Decelerate(double t) => CubicBezier(0.0, 0.0, 0.2, 1.0, t);

    /// <summary>Slow start (cubic-bezier(0.4, 0, 1, 1)), for things leaving.</summary>
    public static double Accelerate(double t) => CubicBezier(0.4, 0.0, 1.0, 1.0, t);

    /// <summary>Symmetric ease in and out (CSS ease-in-out, cubic-bezier(0.42, 0, 0.58, 1)): emphasis and motion paths.</summary>
    public static double InOut(double t) => CubicBezier(0.42, 0.0, 0.58, 1.0, t);

    /// <summary>Morph: a little more emphasis in the middle (cubic-bezier(0.65, 0, 0.35, 1)).</summary>
    public static double Morph(double t) => CubicBezier(0.65, 0.0, 0.35, 1.0, t);

    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    /// <summary>The y of a CSS-style cubic Bézier timing curve at x = <paramref name="t"/>.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        t = Clamp01(t);
        if (t == 0 || t == 1) return t;

        // Solve x(s) = t for s (Newton, then bisection as a safety net), return y(s).
        double X(double s) => ((1 - 3 * x2 + 3 * x1) * s + (3 * x2 - 6 * x1)) * s * s + 3 * x1 * s;
        double Y(double s) => ((1 - 3 * y2 + 3 * y1) * s + (3 * y2 - 6 * y1)) * s * s + 3 * y1 * s;
        double DX(double s) => 3 * (1 - 3 * x2 + 3 * x1) * s * s + 2 * (3 * x2 - 6 * x1) * s + 3 * x1;

        var u = t;
        for (var i = 0; i < 8; i++)
        {
            var x = X(u) - t;
            if (Math.Abs(x) < 1e-6) return Y(u);
            var d = DX(u);
            if (Math.Abs(d) < 1e-6) break;
            u -= x / d;
        }
        double lo = 0, hi = 1;
        u = t;
        for (var i = 0; i < 40; i++)
        {
            var x = X(u);
            if (Math.Abs(x - t) < 1e-7) break;
            if (x < t) lo = u;
            else hi = u;
            u = (lo + hi) / 2;
        }
        return Y(u);
    }
}
