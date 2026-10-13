namespace Stagecraft.Core.Model;

public enum FillKind { None, Solid, Linear, Radial, Image }

/// <summary>
/// A fill. Colours are references: a theme colour ("accent1".."accent5", "text", "text2", "bg", "bg2", "white",
/// "black") or "#RRGGBB" / "#AARRGGBB", optionally with an opacity in percent after "@" ("accent1@20").
/// </summary>
public sealed class Fill
{
    public FillKind Kind { get; set; } = FillKind.Solid;
    public string Color { get; set; } = "accent1";
    /// <summary>The second gradient colour.</summary>
    public string? Color2 { get; set; }
    /// <summary>Linear gradient direction in degrees (0 = left to right, 90 = top to bottom).</summary>
    public double Angle { get; set; } = 90;
    /// <summary>Image fills: the media file.</summary>
    public string? Media { get; set; }
    /// <summary>Image fills: darkening overlay (0..1) so text on top stays readable.</summary>
    public double Dim { get; set; }

    public static Fill None => new() { Kind = FillKind.None };
    public static Fill Solid(string color) => new() { Kind = FillKind.Solid, Color = color };
    public static Fill Linear(string from, string to, double angle) => new() { Kind = FillKind.Linear, Color = from, Color2 = to, Angle = angle };
    public static Fill Radial(string inner, string outer) => new() { Kind = FillKind.Radial, Color = inner, Color2 = outer };
    public static Fill Image(string media, double dim = 0) => new() { Kind = FillKind.Image, Media = media, Dim = dim };

    public Fill Clone() => new() { Kind = Kind, Color = Color, Color2 = Color2, Angle = Angle, Media = Media, Dim = Dim };

    public bool IsVisible => Kind != FillKind.None;
}

public enum DashStyleKind { Solid, Dash, Dot }

public sealed class Stroke
{
    public string Color { get; set; } = "text";
    /// <summary>Width in units.</summary>
    public double Width { get; set; } = 3;
    public DashStyleKind Dash { get; set; }

    public Stroke Clone() => new() { Color = Color, Width = Width, Dash = Dash };
}

public enum BuildEffect { None, Appear, Fade, Rise, Scale, Wipe }

/// <summary>A build animation: the element appears on click number <see cref="Step"/> of its slide.</summary>
public sealed class Build
{
    public BuildEffect Effect { get; set; } = BuildEffect.Fade;
    /// <summary>1 = the first click on the slide; elements with the same step appear together.</summary>
    public int Step { get; set; } = 1;
    /// <summary>Seconds after the click.</summary>
    public double Delay { get; set; }
    /// <summary>Seconds.</summary>
    public double Duration { get; set; } = 0.5;

    public Build Clone() => new() { Effect = Effect, Step = Step, Delay = Delay, Duration = Duration };
}
