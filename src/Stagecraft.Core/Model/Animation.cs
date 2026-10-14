using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>What an animation does to its element: makes it appear, draws attention, makes it leave, moves it or plays media.</summary>
public enum EffectClass { Entrance, Emphasis, Exit, Motion, Media }

/// <summary>
/// The effect. Entrance and exit share their names (an exit is the mirror of the entrance); emphasis, motion and media
/// effects have their own. <see cref="EffectCatalog"/> says which kinds belong to which class.
/// </summary>
public enum EffectKind
{
    // Entrance and exit
    Appear,
    Fade,
    Fly,
    Float,
    Wipe,
    Split,
    Zoom,
    GrowTurn,
    Bounce,
    Wheel,
    Swivel,
    Rise,

    // Emphasis
    Pulse,
    Spin,
    Grow,
    Teeter,
    ColorPulse,
    Transparency,
    BoldFlash,
    Wave,

    // Motion paths
    Line,
    Arc,
    Circle,
    Custom,

    // Video and sound
    Play,
    Pause,
    Stop,
}

/// <summary>Where an effect comes from or goes to (Fly, Float, Wipe), or how it opens (Split).</summary>
public enum EffectDirection
{
    Bottom,
    Left,
    Right,
    Top,
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    HorizontalIn,
    HorizontalOut,
    VerticalIn,
    VerticalOut,
}

/// <summary>When an effect starts: on the next click, together with the effect before it, or after it.</summary>
public enum EffectStart { OnClick, WithPrevious, AfterPrevious }

/// <summary>How the effect speeds up and slows down (Auto: the curve that suits the effect).</summary>
public enum EffectEasing { Auto, Linear, EaseIn, EaseOut, EaseInOut, Overshoot }

/// <summary>How the text of an element animates: as one, or paragraph by paragraph, word by word, letter by letter.</summary>
public enum TextUnit { All, Paragraph, Word, Letter }

/// <summary>Repeating until something happens, instead of a number of times.</summary>
public enum RepeatUntil { None, NextClick, EndOfSlide }

/// <summary>A point of a motion path, relative to the centre of the element where the motion starts (slide units).</summary>
public sealed class PathPoint
{
    public PathPoint()
    {
    }

    public PathPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }
    public double Y { get; set; }

    public PathPoint Clone() => new(X, Y);
}

/// <summary>The route of a motion path: points relative to the element's centre; curves pass smoothly through them.</summary>
public sealed class MotionPath
{
    public List<PathPoint> Points { get; set; } = new();

    /// <summary>Returns to the first point (circles).</summary>
    public bool Closed { get; set; }

    /// <summary>A smooth curve through the points (arcs, circles, drawn paths) instead of straight segments.</summary>
    public bool Smooth { get; set; }

    public MotionPath Clone() => new() { Points = Points.Select(p => p.Clone()).ToList(), Closed = Closed, Smooth = Smooth };
}

/// <summary>
/// One animation of a slide. A slide's animations are an ordered list (its animation order): effects that start
/// "On click" begin a new click; "With previous" and "After previous" join the click before them. Effects with a
/// <see cref="Trigger"/> run when that element is clicked in the slide show instead of in the click order.
/// </summary>
public sealed class AnimationEffect
{
    public string Id { get; set; } = Ids.New();

    /// <summary>The element it animates, or a group id (the group animates as one).</summary>
    public string Target { get; set; } = "";

    public EffectClass Class { get; set; } = EffectClass.Entrance;
    public EffectKind Effect { get; set; } = EffectKind.Fade;
    public EffectDirection Direction { get; set; } = EffectDirection.Bottom;
    public EffectStart Start { get; set; } = EffectStart.OnClick;

    /// <summary>Seconds after the effect's start point.</summary>
    public double Delay { get; set; }

    /// <summary>Seconds for one run of the effect.</summary>
    public double Duration { get; set; } = 0.5;

    /// <summary>How many times it runs (1 = once).</summary>
    public int Repeat { get; set; } = 1;

    public RepeatUntil RepeatUntil { get; set; }

    /// <summary>Goes back to how it was before when it's done (an entrance hides again, a spin turns back).</summary>
    public bool Rewind { get; set; }

    public EffectEasing Easing { get; set; }

    /// <summary>Text: all at once, by paragraph, word or letter.</summary>
    public TextUnit TextUnit { get; set; }

    /// <summary>Seconds between paragraphs, words or letters (null: a pleasant default for the unit).</summary>
    public double? TextStagger { get; set; }

    /// <summary>By paragraph: each paragraph waits for its own click (bullets one by one).</summary>
    public bool ParagraphClicks { get; set; }

    /// <summary>Plays when this element is clicked in the slide show (null: in the click order).</summary>
    public string? Trigger { get; set; }

    /// <summary>
    /// Effect strength: Spin degrees, Grow / Shrink and Zoom percent, Transparency percent (0 = the effect's default).
    /// </summary>
    public double Amount { get; set; }

    /// <summary>Colour pulse: the colour (reference); null = the theme's second accent.</summary>
    public string? Color { get; set; }

    /// <summary>Motion paths: the route.</summary>
    public MotionPath? Path { get; set; }

    [JsonIgnore]
    public bool IsMedia => Class == EffectClass.Media;

    public AnimationEffect Clone() => new()
    {
        Id = Id,
        Target = Target,
        Class = Class,
        Effect = Effect,
        Direction = Direction,
        Start = Start,
        Delay = Delay,
        Duration = Duration,
        Repeat = Repeat,
        RepeatUntil = RepeatUntil,
        Rewind = Rewind,
        Easing = Easing,
        TextUnit = TextUnit,
        TextStagger = TextStagger,
        ParagraphClicks = ParagraphClicks,
        Trigger = Trigger,
        Amount = Amount,
        Color = Color,
        Path = Path?.Clone(),
    };

    /// <summary>"Fly in", "Fade out", "Spin", "Motion: line", "Play"…</summary>
    [JsonIgnore]
    public string DisplayName => EffectCatalog.Name(Class, Effect);

    /// <summary>The effect's strength with its default filled in.</summary>
    [JsonIgnore]
    public double EffectiveAmount => Amount != 0 ? Amount : EffectCatalog.DefaultAmount(Effect);
}

/// <summary>One effect of the "Add animation" gallery.</summary>
public sealed record EffectInfo(EffectClass Class, EffectKind Kind, string Name, double Duration, bool HasDirection, bool TextFriendly);

/// <summary>The effects Stagecraft has, their names and defaults.</summary>
public static class EffectCatalog
{
    public static IReadOnlyList<EffectKind> EntranceKinds { get; } = new[]
    {
        EffectKind.Appear, EffectKind.Fade, EffectKind.Fly, EffectKind.Float, EffectKind.Wipe, EffectKind.Split, EffectKind.Zoom,
        EffectKind.GrowTurn, EffectKind.Bounce, EffectKind.Wheel, EffectKind.Swivel, EffectKind.Rise,
    };

    public static IReadOnlyList<EffectKind> EmphasisKinds { get; } = new[]
    {
        EffectKind.Pulse, EffectKind.Spin, EffectKind.Grow, EffectKind.Teeter, EffectKind.ColorPulse, EffectKind.Transparency,
        EffectKind.BoldFlash, EffectKind.Wave,
    };

    public static IReadOnlyList<EffectKind> MotionKinds { get; } = new[] { EffectKind.Line, EffectKind.Arc, EffectKind.Circle, EffectKind.Custom };

    public static IReadOnlyList<EffectKind> MediaKinds { get; } = new[] { EffectKind.Play, EffectKind.Pause, EffectKind.Stop };

    /// <summary>Every effect of a class, in gallery order.</summary>
    public static IReadOnlyList<EffectKind> KindsOf(EffectClass c) => c switch
    {
        EffectClass.Entrance or EffectClass.Exit => EntranceKinds,
        EffectClass.Emphasis => EmphasisKinds,
        EffectClass.Motion => MotionKinds,
        _ => MediaKinds,
    };

    public static bool Belongs(EffectClass c, EffectKind k) => KindsOf(c).Contains(k);

    public static IEnumerable<EffectInfo> All()
    {
        foreach (var c in new[] { EffectClass.Entrance, EffectClass.Emphasis, EffectClass.Exit, EffectClass.Motion })
        foreach (var k in KindsOf(c))
            yield return Info(c, k);
    }

    public static EffectInfo Info(EffectClass c, EffectKind k) => new(c, k, Name(c, k), DefaultDuration(c, k), HasDirection(k), TextFriendly(k));

    public static string Name(EffectClass c, EffectKind k)
    {
        if (c == EffectClass.Media)
            return k switch { EffectKind.Pause => "Pause", EffectKind.Stop => "Stop", _ => "Play" };
        if (c == EffectClass.Motion)
            return k switch { EffectKind.Arc => "Arc", EffectKind.Circle => "Circle", EffectKind.Custom => "Custom path", _ => "Line" };
        if (c == EffectClass.Emphasis)
        {
            return k switch
            {
                EffectKind.Pulse => "Pulse",
                EffectKind.Spin => "Spin",
                EffectKind.Grow => "Grow / shrink",
                EffectKind.Teeter => "Teeter",
                EffectKind.ColorPulse => "Colour pulse",
                EffectKind.Transparency => "Transparency",
                EffectKind.BoldFlash => "Bold flash",
                EffectKind.Wave => "Wave",
                _ => k.ToString(),
            };
        }
        var exit = c == EffectClass.Exit;
        return k switch
        {
            EffectKind.Appear => exit ? "Disappear" : "Appear",
            EffectKind.Fade => exit ? "Fade out" : "Fade",
            EffectKind.Fly => exit ? "Fly out" : "Fly in",
            EffectKind.Float => exit ? "Float out" : "Float in",
            EffectKind.Wipe => "Wipe",
            EffectKind.Split => "Split",
            EffectKind.Zoom => "Zoom",
            EffectKind.GrowTurn => exit ? "Shrink & turn" : "Grow & turn",
            EffectKind.Bounce => "Bounce",
            EffectKind.Wheel => "Wheel",
            EffectKind.Swivel => "Swivel",
            EffectKind.Rise => exit ? "Sink down" : "Rise up",
            _ => k.ToString(),
        };
    }

    public static double DefaultDuration(EffectClass c, EffectKind k) => k switch
    {
        EffectKind.Appear => 0,
        EffectKind.Play or EffectKind.Pause or EffectKind.Stop => 0,
        EffectKind.Fade => 0.5,
        EffectKind.Rise => 0.6,
        EffectKind.Fly or EffectKind.Float or EffectKind.Wipe or EffectKind.Split or EffectKind.Zoom => 0.5,
        EffectKind.Bounce or EffectKind.Wheel or EffectKind.Swivel or EffectKind.GrowTurn => 1.0,
        EffectKind.Spin or EffectKind.Teeter or EffectKind.Wave => 1.0,
        EffectKind.Pulse or EffectKind.ColorPulse or EffectKind.BoldFlash => 0.6,
        EffectKind.Grow or EffectKind.Transparency => 0.8,
        EffectKind.Line or EffectKind.Arc or EffectKind.Circle or EffectKind.Custom => 2.0,
        _ => 0.5,
    };

    /// <summary>The strength an effect uses when <see cref="AnimationEffect.Amount"/> is 0.</summary>
    public static double DefaultAmount(EffectKind k) => k switch
    {
        EffectKind.Spin => 360,
        EffectKind.Grow => 150,
        EffectKind.Transparency => 50,
        EffectKind.Zoom => 30,
        _ => 0,
    };

    /// <summary>Effects with a direction to choose.</summary>
    public static bool HasDirection(EffectKind k) => k is EffectKind.Fly or EffectKind.Float or EffectKind.Wipe or EffectKind.Split;

    /// <summary>The directions an effect offers, in menu order.</summary>
    public static IReadOnlyList<EffectDirection> Directions(EffectKind k) => k switch
    {
        EffectKind.Fly => new[]
        {
            EffectDirection.Bottom, EffectDirection.BottomLeft, EffectDirection.Left, EffectDirection.TopLeft,
            EffectDirection.Top, EffectDirection.TopRight, EffectDirection.Right, EffectDirection.BottomRight,
        },
        EffectKind.Float => new[] { EffectDirection.Bottom, EffectDirection.Top },
        EffectKind.Wipe => new[] { EffectDirection.Bottom, EffectDirection.Left, EffectDirection.Right, EffectDirection.Top },
        EffectKind.Split => new[] { EffectDirection.HorizontalIn, EffectDirection.HorizontalOut, EffectDirection.VerticalIn, EffectDirection.VerticalOut },
        _ => Array.Empty<EffectDirection>(),
    };

    public static EffectDirection DefaultDirection(EffectKind k) => k == EffectKind.Split ? EffectDirection.VerticalOut : EffectKind.Wipe == k ? EffectDirection.Left : EffectDirection.Bottom;

    /// <summary>"From bottom", "Vertical out"…</summary>
    public static string DirectionName(EffectDirection d, EffectClass c = EffectClass.Entrance)
    {
        var prefix = c == EffectClass.Exit ? "To " : "From ";
        return d switch
        {
            EffectDirection.Bottom => prefix + "bottom",
            EffectDirection.Left => prefix + "left",
            EffectDirection.Right => prefix + "right",
            EffectDirection.Top => prefix + "top",
            EffectDirection.BottomLeft => prefix + "bottom left",
            EffectDirection.BottomRight => prefix + "bottom right",
            EffectDirection.TopLeft => prefix + "top left",
            EffectDirection.TopRight => prefix + "top right",
            EffectDirection.HorizontalIn => "Horizontal in",
            EffectDirection.HorizontalOut => "Horizontal out",
            EffectDirection.VerticalIn => "Vertical in",
            _ => "Vertical out",
        };
    }

    /// <summary>Effects that read well applied by word or letter.</summary>
    public static bool TextFriendly(EffectKind k) => k is EffectKind.Appear or EffectKind.Fade or EffectKind.Fly or EffectKind.Float or EffectKind.Zoom
        or EffectKind.Rise or EffectKind.Bounce or EffectKind.Swivel or EffectKind.Wave or EffectKind.Pulse or EffectKind.ColorPulse or EffectKind.Wipe
        or EffectKind.Teeter or EffectKind.BoldFlash or EffectKind.GrowTurn;

    /// <summary>A new effect with the defaults of its kind.</summary>
    public static AnimationEffect Create(EffectClass c, EffectKind k, string target)
    {
        var e = new AnimationEffect
        {
            Target = target,
            Class = c,
            Effect = k,
            Duration = DefaultDuration(c, k),
            Direction = DefaultDirection(k),
            Start = EffectStart.OnClick,
        };
        if (k == EffectKind.Wave) e.TextUnit = TextUnit.Letter;
        return e;
    }

    /// <summary>The default route of a motion path for an element of this size (Custom paths are drawn by hand).</summary>
    public static MotionPath DefaultPath(EffectKind k, double width, double height)
    {
        var span = Math.Max(120, Math.Min(480, Math.Max(width, height) * 0.9));
        switch (k)
        {
            case EffectKind.Arc:
            {
                // Half an ellipse over the top, from left to right.
                var path = new MotionPath { Smooth = true };
                for (var i = 0; i <= 8; i++)
                {
                    var a = Math.PI - Math.PI * i / 8;
                    path.Points.Add(new PathPoint(Math.Round(span / 2 + Math.Cos(a) * span / 2, 2), Math.Round(-Math.Sin(a) * span * 0.45, 2)));
                }
                return path;
            }
            case EffectKind.Circle:
            {
                // Clockwise round a circle that starts and ends where the element is.
                var path = new MotionPath { Smooth = true, Closed = true };
                var r = span / 2;
                for (var i = 0; i < 12; i++)
                {
                    var a = -Math.PI / 2 + 2 * Math.PI * i / 12;
                    path.Points.Add(new PathPoint(Math.Round(Math.Cos(a) * r, 2), Math.Round(r + Math.Sin(a) * r, 2)));
                }
                return path;
            }
            default:
                return new MotionPath { Points = { new PathPoint(0, 0), new PathPoint(Math.Round(span), 0) } };
        }
    }
}
