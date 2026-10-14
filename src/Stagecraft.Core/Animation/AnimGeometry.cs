using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Animation;

/// <summary>Turns an <see cref="AnimState"/> into WPF transforms and clip geometry (slide coordinates).</summary>
public static class AnimGeometry
{
    /// <summary>Scale and rotation around the state's centre, then the offset.</summary>
    public static Transform Transform(AnimState s)
    {
        if (!s.HasTransform) return System.Windows.Media.Transform.Identity;
        var group = new TransformGroup();
        if (Math.Abs(s.ScaleX - 1) > 1e-6 || Math.Abs(s.ScaleY - 1) > 1e-6) group.Children.Add(new ScaleTransform(s.ScaleX, s.ScaleY, s.Center.X, s.Center.Y));
        if (Math.Abs(s.Rotation) > 1e-6) group.Children.Add(new RotateTransform(s.Rotation, s.Center.X, s.Center.Y));
        if (Math.Abs(s.Dx) > 1e-6 || Math.Abs(s.Dy) > 1e-6) group.Children.Add(new TranslateTransform(s.Dx, s.Dy));
        return group;
    }

    /// <summary>The visible region while wiping, splitting or wheeling (null: no clip). <paramref name="bleed"/> covers shadows and strokes.</summary>
    public static Geometry? Clip(AnimState s, double bleed = 12)
    {
        if (s.Clip == ClipKind.None || s.ClipProgress >= 0.9999) return null;
        var b = s.ClipBounds;
        b.Inflate(bleed, bleed);
        var p = Easing.Clamp01(s.ClipProgress);
        if (p <= 0) return Geometry.Empty;
        switch (s.Clip)
        {
            case ClipKind.Wipe:
                return s.ClipDirection switch
                {
                    EffectDirection.Right => Box(b.Right - b.Width * p, b.Top, b.Width * p, b.Height),
                    EffectDirection.Top => Box(b.Left, b.Top, b.Width, b.Height * p),
                    EffectDirection.Bottom => Box(b.Left, b.Bottom - b.Height * p, b.Width, b.Height * p),
                    _ => Box(b.Left, b.Top, b.Width * p, b.Height),
                };
            case ClipKind.Split:
            {
                var cx = b.Left + b.Width / 2;
                var cy = b.Top + b.Height / 2;
                switch (s.ClipDirection)
                {
                    case EffectDirection.VerticalIn:
                    {
                        // Two bands closing in from the left and right edges.
                        var g = new GeometryGroup();
                        g.Children.Add(Box(b.Left, b.Top, b.Width / 2 * p, b.Height));
                        g.Children.Add(Box(b.Right - b.Width / 2 * p, b.Top, b.Width / 2 * p, b.Height));
                        g.Freeze();
                        return g;
                    }
                    case EffectDirection.HorizontalIn:
                    {
                        var g = new GeometryGroup();
                        g.Children.Add(Box(b.Left, b.Top, b.Width, b.Height / 2 * p));
                        g.Children.Add(Box(b.Left, b.Bottom - b.Height / 2 * p, b.Width, b.Height / 2 * p));
                        g.Freeze();
                        return g;
                    }
                    case EffectDirection.HorizontalOut:
                        return Box(b.Left, cy - b.Height / 2 * p, b.Width, b.Height * p);
                    default:
                        return Box(cx - b.Width / 2 * p, b.Top, b.Width * p, b.Height);
                }
            }
            case ClipKind.Wheel:
            {
                // A pie that sweeps clockwise from twelve o'clock.
                var c = new Point(b.Left + b.Width / 2, b.Top + b.Height / 2);
                var r = Math.Sqrt(b.Width * b.Width + b.Height * b.Height) / 2 + 2;
                var angle = 360 * p;
                var figure = new PathFigure { StartPoint = c, IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(new Point(c.X, c.Y - r), true));
                Point At(double deg)
                {
                    var a = (deg - 90) * Math.PI / 180;
                    return new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
                }
                // Arcs of at most 90° each keep WPF's arc maths stable.
                for (var done = 0.0; done < angle - 1e-6;)
                {
                    var step = Math.Min(90, angle - done);
                    done += step;
                    figure.Segments.Add(new ArcSegment(At(done), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                }
                var g = new PathGeometry();
                g.Figures.Add(figure);
                g.Freeze();
                return g;
            }
            default:
                return null;
        }
    }

    private static Geometry Box(double x, double y, double w, double h)
    {
        var g = new RectangleGeometry(new System.Windows.Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
        g.Freeze();
        return g;
    }
}
