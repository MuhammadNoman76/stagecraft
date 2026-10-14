using System.Text.Json.Serialization;
using System.Windows;

namespace Stagecraft.Core.Model;

/// <summary>
/// Something on a slide. The box (X, Y, Width, Height in slide units) is before rotation; rotation (degrees,
/// clockwise) turns it around its centre. Lines use the box diagonal (top-left to bottom-right, flipped by FlipH / FlipV).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(ChartElement), "chart")]
[JsonDerivedType(typeof(TableElement), "table")]
[JsonDerivedType(typeof(IconElement), "icon")]
[JsonDerivedType(typeof(VideoElement), "video")]
[JsonDerivedType(typeof(AudioElement), "audio")]
[JsonDerivedType(typeof(OnlineVideoElement), "onlineVideo")]
public abstract class Element
{
    public string Id { get; set; } = Ids.New();

    /// <summary>A name the user gave it. Elements with the same name on consecutive slides morph into each other.</summary>
    public string? Name { get; set; }

    /// <summary>Identity kept when a slide is duplicated, so Morph knows which elements belong together.</summary>
    public string? MorphKey { get; set; }

    /// <summary>The layout slot it fills (title, subtitle, body, image, ...); null for things the user added.</summary>
    public string? Placeholder { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 200;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    public bool Locked { get; set; }

    /// <summary>A placeholder the user moved or resized: theme and size changes leave its position alone.</summary>
    public bool Moved { get; set; }

    /// <summary>Elements with the same group id move, resize and select together.</summary>
    public string? GroupId { get; set; }

    /// <summary>A link followed when the element is clicked in the slide show (Ctrl+click in the editor).</summary>
    public Hyperlink? Link { get; set; }

    /// <summary>A soft shadow behind the element (null: none).</summary>
    public Shadow? Shadow { get; set; }

    [JsonIgnore]
    public Rect Bounds
    {
        get => new(X, Y, Math.Max(0, Width), Math.Max(0, Height));
        set
        {
            X = value.X;
            Y = value.Y;
            Width = value.Width;
            Height = value.Height;
        }
    }

    [JsonIgnore]
    public Point Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>"Text", "Image", ... for the UI and screen readers.</summary>
    [JsonIgnore]
    public abstract string KindName { get; }

    /// <summary>The key used to recognise "the same thing" on two slides when names and ids don't match (Morph).</summary>
    [JsonIgnore]
    public virtual string ContentKey => KindName;

    /// <summary>A short description for screen readers and the layer list.</summary>
    [JsonIgnore]
    public virtual string Describe => string.IsNullOrWhiteSpace(Name) ? KindName : Name!;

    /// <summary>The axis-aligned box that contains the rotated element.</summary>
    [JsonIgnore]
    public Rect RotatedBounds
    {
        get
        {
            var r = Bounds;
            if (Math.Abs(Rotation % 360) < 0.001) return r;
            var c = Center;
            var a = Rotation * Math.PI / 180;
            var cos = Math.Abs(Math.Cos(a));
            var sin = Math.Abs(Math.Sin(a));
            var w = r.Width * cos + r.Height * sin;
            var h = r.Width * sin + r.Height * cos;
            return new Rect(c.X - w / 2, c.Y - h / 2, w, h);
        }
    }
}

public enum TextRole { Title, Subtitle, Heading, Body, Caption, Quote, Stat, Label, Code }

public enum TextAlign { Left, Center, Right, Justify }

public enum VerticalAlign { Top, Middle, Bottom }

/// <summary>Text with a style role; the theme decides font, size and colour for the role unless overridden here.</summary>
public sealed class TextBody
{
    public RichText Content { get; set; } = new();
    public TextRole Role { get; set; } = TextRole.Body;

    /// <summary>Base size in units (null: the theme's size for the role).</summary>
    public double? Size { get; set; }

    /// <summary>"heading", "body", "mono" or a font family name (null: the role's font).</summary>
    public string? Font { get; set; }

    /// <summary>Colour reference (null: the role's colour).</summary>
    public string? Color { get; set; }

    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Uppercase { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Left;
    public VerticalAlign VAlign { get; set; } = VerticalAlign.Top;

    /// <summary>Line height as a multiple of the font size (null: the role's).</summary>
    public double? LineSpacing { get; set; }

    /// <summary>Shrink the text when it doesn't fit the box.</summary>
    public bool AutoFit { get; set; } = true;

    /// <summary>Padding inside the box, in units.</summary>
    public double Inset { get; set; }

    /// <summary>A shadow behind the letters (null: none).</summary>
    public Shadow? Shadow { get; set; }
}

public sealed class TextElement : Element
{
    public TextBody Text { get; set; } = new();
    public Fill? Fill { get; set; }
    public Stroke? Stroke { get; set; }
    public double CornerRadius { get; set; }

    public override string KindName => "Text";

    public override string ContentKey => "text:" + Text.Content.PlainText.Trim();

    public override string Describe
    {
        get
        {
            var text = Text.Content.PlainText.Trim().Replace('\n', ' ');
            if (text.Length > 48) text = text.Substring(0, 47) + "…";
            return !string.IsNullOrWhiteSpace(Name) ? Name! : text.Length > 0 ? "Text: " + text : "Empty text";
        }
    }
}

public enum ShapeKind { Rectangle, RoundedRectangle, Ellipse, Triangle, Diamond, Line, Arrow, Callout, Connector, ActionButton }

/// <summary>The symbol of an action button (and the slide it goes to by default).</summary>
public enum ActionButtonKind { Previous, Next, First, Last, Return, Custom }

public enum ArrowHead { None, Arrow, Dot }

/// <summary>Which side of an element a connector attaches to.</summary>
public enum ConnectorSide { Auto, Left, Top, Right, Bottom }

public sealed class ShapeElement : Element
{
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;
    public Fill Fill { get; set; } = Fill.Solid("accent1");
    public Stroke? Stroke { get; set; }
    public double CornerRadius { get; set; }

    /// <summary>Optional text inside the shape.</summary>
    public TextBody? Text { get; set; }

    // Lines, arrows and connectors
    public ArrowHead StartHead { get; set; }
    public ArrowHead EndHead { get; set; }

    // Connectors: the elements they join (the line follows when those move).
    public string? StartId { get; set; }
    public string? EndId { get; set; }
    public ConnectorSide StartSide { get; set; }
    public ConnectorSide EndSide { get; set; }
    /// <summary>Right-angled route instead of a straight line.</summary>
    public bool Elbow { get; set; }

    // Callouts: where the tail points, as a fraction of the box (0..1, may be outside).
    public double TailX { get; set; } = 0.2;
    public double TailY { get; set; } = 1.35;

    /// <summary>Action buttons: the symbol drawn on the button.</summary>
    public ActionButtonKind Action { get; set; }

    [JsonIgnore]
    public bool IsLine => Shape is ShapeKind.Line or ShapeKind.Arrow or ShapeKind.Connector;

    public override string KindName => Shape switch
    {
        ShapeKind.Rectangle => "Rectangle",
        ShapeKind.RoundedRectangle => "Rounded rectangle",
        ShapeKind.Ellipse => "Ellipse",
        ShapeKind.Triangle => "Triangle",
        ShapeKind.Diamond => "Diamond",
        ShapeKind.Line => "Line",
        ShapeKind.Arrow => "Arrow",
        ShapeKind.Callout => "Callout",
        ShapeKind.Connector => "Connector",
        ShapeKind.ActionButton => "Action button",
        _ => "Shape",
    };

    public override string ContentKey => "shape:" + Shape + ":" + (Text?.Content.PlainText.Trim() ?? "");
}

public enum ImageFit { Cover, Contain, Stretch }

public enum ImageMask { Rectangle, Circle }

/// <summary>A part of the picture to keep (fractions cut from each edge), used by imported pictures.</summary>
public sealed class Crop
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }
}

public sealed class ImageElement : Element
{
    /// <summary>File name in the deck's media folder (null: an empty image placeholder).</summary>
    public string? Media { get; set; }

    public ImageFit Fit { get; set; } = ImageFit.Cover;

    /// <summary>For Cover: which part stays in view (0..1).</summary>
    public double FocusX { get; set; } = 0.5;
    public double FocusY { get; set; } = 0.5;

    public Crop? Crop { get; set; }
    public ImageMask Mask { get; set; }
    public double CornerRadius { get; set; }
    public Stroke? Stroke { get; set; }

    /// <summary>Alternative text for screen readers and exports.</summary>
    public string? AltText { get; set; }

    public override string KindName => "Image";

    public override string ContentKey => "image:" + (Media ?? "");

    public override string Describe => !string.IsNullOrWhiteSpace(Name) ? Name! :
        !string.IsNullOrWhiteSpace(AltText) ? "Image: " + AltText : Media == null ? "Image placeholder" : "Image";
}

public enum ChartKind { Bar, Line, Pie, Donut }

public sealed class ChartSeries
{
    public string Name { get; set; } = "Series 1";
    public List<double> Values { get; set; } = new();
}

public sealed class ChartElement : Element
{
    public ChartKind Kind { get; set; } = ChartKind.Bar;
    public List<string> Categories { get; set; } = new();
    public List<ChartSeries> Series { get; set; } = new();
    public bool ShowLegend { get; set; } = true;
    public bool ShowValues { get; set; } = true;
    public bool ShowGrid { get; set; } = true;

    /// <summary>.NET number format for the value labels and axis ("0", "0.0", "0%", "$#,0").</summary>
    public string ValueFormat { get; set; } = "#,0.##";

    public override string KindName => Kind switch
    {
        ChartKind.Bar => "Bar chart",
        ChartKind.Line => "Line chart",
        ChartKind.Pie => "Pie chart",
        _ => "Donut chart",
    };

    public override string ContentKey => "chart:" + Kind + ":" + string.Join(",", Categories);
}

public sealed class TableElement : Element
{
    /// <summary>Rows of cells (plain text). The first row is the header when <see cref="HeaderRow"/> is on.</summary>
    public List<List<string>> Cells { get; set; } = new();
    public bool HeaderRow { get; set; } = true;
    public bool Banded { get; set; } = true;
    public bool FirstColumnBold { get; set; }

    /// <summary>Relative column widths (null: equal).</summary>
    public List<double>? ColumnWidths { get; set; }

    /// <summary>Text size in units (null: the theme's caption-to-body size that fits).</summary>
    public double? FontSize { get; set; }

    /// <summary>Header colour reference.</summary>
    public string HeaderColor { get; set; } = "accent1";

    [JsonIgnore]
    public int RowCount => Cells.Count;

    [JsonIgnore]
    public int ColumnCount => Cells.Count == 0 ? 0 : Cells.Max(r => r.Count);

    public override string KindName => "Table";

    public override string ContentKey => "table:" + RowCount + "x" + ColumnCount;
}

public enum IconContainer { None, Circle, RoundedSquare }

public sealed class IconElement : Element
{
    /// <summary>Key in the icon catalogue (Fluent UI System Icons, for example "rocket").</summary>
    public string Icon { get; set; } = "star";
    public string Color { get; set; } = "accent1";
    public IconContainer Container { get; set; }
    /// <summary>Colour of the container (null: the icon colour at 14 % opacity).</summary>
    public string? ContainerColor { get; set; }

    public override string KindName => "Icon";

    public override string ContentKey => "icon:" + Icon;

    public override string Describe => !string.IsNullOrWhiteSpace(Name) ? Name! : "Icon: " + Icon.Replace('_', ' ');
}
