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

public enum DashStyleKind { Solid, Dash, Dot, LongDash, DashDot }

public sealed class Stroke
{
    public string Color { get; set; } = "text";
    /// <summary>Width in units.</summary>
    public double Width { get; set; } = 3;
    public DashStyleKind Dash { get; set; }

    public Stroke Clone() => new() { Color = Color, Width = Width, Dash = Dash };
}

/// <summary>
/// A soft shadow: a blurred, offset silhouette of the element (or of the letters, on text). The colour is a reference,
/// so shadows follow the theme ("#000000@35", "accent1@40").
/// </summary>
public sealed class Shadow
{
    public string Color { get; set; } = "#000000@30";

    /// <summary>Blur radius in units.</summary>
    public double Blur { get; set; } = 28;

    /// <summary>How far the shadow falls, in units.</summary>
    public double Distance { get; set; } = 10;

    /// <summary>The direction the shadow falls, in degrees (90 = straight down).</summary>
    public double Angle { get; set; } = 90;

    public Shadow Clone() => new() { Color = Color, Blur = Blur, Distance = Distance, Angle = Angle };

    public bool SameAs(Shadow? other) => other != null && other.Color == Color && Math.Abs(other.Blur - Blur) < 0.001 &&
                                         Math.Abs(other.Distance - Distance) < 0.001 && Math.Abs(other.Angle - Angle) < 0.001;

    /// <summary>The presets the panel offers.</summary>
    public static Shadow Soft => new() { Color = "#000000@30", Blur = 28, Distance = 10, Angle = 90 };
    public static Shadow Lifted => new() { Color = "#000000@40", Blur = 48, Distance = 24, Angle = 90 };
    public static Shadow Close => new() { Color = "#000000@45", Blur = 8, Distance = 5, Angle = 45 };
    public static Shadow Glow => new() { Color = "accent1@60", Blur = 40, Distance = 0, Angle = 90 };
}
