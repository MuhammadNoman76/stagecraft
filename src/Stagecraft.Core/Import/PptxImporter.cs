using System.Globalization;
using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Stagecraft.Core.Import;

/// <summary>
/// Imports PowerPoint (.pptx) files: slide size, text (with basic formatting, bullets and alignment), shapes,
/// lines, pictures (with cropping), tables, simple charts (bar, line, pie, doughnut from their cached data), groups,
/// backgrounds, transitions, speaker notes, hyperlinks and action buttons, videos and sounds (embedded or linked, with
/// trim and fades) and animations (effects Stagecraft lacks become the closest one). SmartArt, other chart types,
/// embedded objects and the finer points of master / layout inheritance are not imported faithfully; the import
/// reports what it left out or changed.
/// </summary>
public static partial class PptxImporter
{
    private const double EmuPerUnit = 6350;

    public static DeckDocument Import(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Import(stream);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or System.Xml.XmlException or FileFormatException)
        {
            throw new DeckFileException(DeckFileProblem.NotADeck, "This file isn't a PowerPoint presentation Stagecraft can read (.pptx).", ex);
        }
    }

    public static DeckDocument Import(Stream stream)
    {
        using var doc = PresentationDocument.Open(stream, false);
        var importer = new Importer(doc);
        return importer.Run();
    }

    private sealed partial class Importer
    {
        private readonly PresentationDocument _doc;
        private readonly MediaStore _media = new();
        private readonly List<string> _warnings = new();
        private readonly Dictionary<string, int> _skipped = new();
        private readonly Dictionary<string, string> _scheme = new(StringComparer.OrdinalIgnoreCase);
        private double _k = 1; // EMU -> units, scaled so the slide is 1080 units high
        private string _majorFont = "Calibri Light";
        private string _minorFont = "Calibri";
        private Deck _deck = new();
        private int _unreadablePictures;
        private Dictionary<string, string> _clrMap = DefaultClrMap();

        public Importer(PresentationDocument doc)
        {
            _doc = doc;
        }

        private void Skip(string what)
        {
            _skipped.TryGetValue(what, out var n);
            _skipped[what] = n + 1;
        }

        public DeckDocument Run()
        {
            var presentationPart = _doc.PresentationPart ?? throw new DeckFileException(DeckFileProblem.NotADeck, "The file has no presentation in it.");
            var presentation = presentationPart.Presentation;
            var size = presentation?.SlideSize;
            double cx = size?.Cx?.Value ?? 12192000;
            double cy = size?.Cy?.Value ?? 6858000;
            if (cx <= 0 || cy <= 0)
            {
                cx = 12192000;
                cy = 6858000;
            }
            var heightUnits = cy / EmuPerUnit;
            _k = 1080.0 / heightUnits / EmuPerUnit;
            var title = _doc.PackageProperties.Title;
            _deck = new Deck { Width = Math.Round(cx * _k), Height = 1080, Title = !string.IsNullOrWhiteSpace(title) ? title! : "Imported presentation" };
            _deck.Info.ImportedFrom = "PowerPoint";

            var master = presentationPart.SlideMasterParts.FirstOrDefault();
            ReadTheme(master?.ThemePart ?? presentationPart.ThemePart);
            _deck.Theme = BuildTheme();

            var ids = presentation?.SlideIdList?.Elements<P.SlideId>().ToList() ?? new List<P.SlideId>();
            foreach (var id in ids)
                if (id.RelationshipId?.Value is { } r0 && presentationPart.GetPartById(r0) is SlidePart sp0 && !_slideIds.ContainsKey(sp0)) _slideIds[sp0] = Ids.New();
            foreach (var id in ids)
            {
                if (id.RelationshipId?.Value is not { } rid) continue;
                if (presentationPart.GetPartById(rid) is not SlidePart slidePart) continue;
                try
                {
                    _deck.Slides.Add(ReadSlide(slidePart));
                }
                catch (Exception)
                {
                    Skip("slide that couldn't be read");
                }
            }

            AddMediaAndAnimationWarnings(_warnings);
            var result = new DeckDocument(_deck, _media);
            foreach (var pair in _skipped)
                result.Warnings.Add(pair.Value == 1 ? $"1 {pair.Key} was left out." : $"{pair.Value} {Plural(pair.Key)} were left out.");
            if (_unreadablePictures > 0)
                result.Warnings.Add(_unreadablePictures == 1 ? "1 picture in a format Stagecraft can't show (such as EMF) was left out." : $"{_unreadablePictures} pictures in formats Stagecraft can't show (such as EMF) were left out.");
            result.Warnings.AddRange(_warnings.Distinct());
            return result;
        }

        private static string Plural(string what) => what switch
        {
            "SmartArt graphic" => "SmartArt graphics",
            "chart" => "charts",
            "video or sound" => "videos and sounds",
            "embedded object" => "embedded objects",
            "slide that couldn't be read" => "slides that couldn't be read",
            "ink drawing" => "ink drawings",
            _ => what + "s",
        };

        // ------------------------------------------------------------------ theme

        private void ReadTheme(ThemePart? part)
        {
            var theme = part?.Theme;
            var colors = theme?.ThemeElements?.ColorScheme;
            if (colors != null)
            {
                foreach (var child in colors.ChildElements)
                {
                    var hex = child.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value ?? child.GetFirstChild<A.SystemColor>()?.LastColor?.Value;
                    if (hex != null) _scheme[child.LocalName] = "#" + hex;
                }
            }
            var fonts = theme?.ThemeElements?.FontScheme;
            if (fonts?.MajorFont?.LatinFont?.Typeface?.Value is { Length: > 0 } major) _majorFont = major;
            if (fonts?.MinorFont?.LatinFont?.Typeface?.Value is { Length: > 0 } minor) _minorFont = minor;
        }

        private string SchemeHex(string name, string fallback) => _scheme.TryGetValue(name, out var v) ? v : fallback;

        private DeckTheme BuildTheme()
        {
            var bg = SchemeHex("lt1", "#FFFFFF");
            var text = SchemeHex("dk1", "#000000");
            var dark = SlideStyle.Luminance(SlideStyle.ParseHex(bg) ?? Colors.White) < 0.4;
            var main = new ColorScheme
            {
                Background = Fill.Solid(bg),
                Text = text,
                TextMuted = SchemeHex("dk2", text),
                Surface = SchemeHex("lt2", "#F2F2F2"),
                IsDark = dark,
            };
            return new DeckTheme
            {
                Id = "imported",
                Name = "Imported",
                Variant = "imported",
                Decor = "none",
                Main = main,
                Emphasis = main.Clone(),
                EmphasisLayouts = new List<string>(),
                Accents = Enumerable.Range(1, 5).Select(i => SchemeHex("accent" + i, "#4472C4")).ToList(),
                Fonts = new ThemeFonts
                {
                    Heading = _majorFont,
                    Body = _minorFont,
                    HeadingWeight = 400,
                    BodyWeight = 400,
                    TitleSize = 88,
                    HeadingSize = 80,
                    BodySize = 56,
                    SubtitleSize = 48,
                    BodyLineHeight = 1.2,
                    HeadingLineHeight = 1.0,
                    LabelUppercase = false,
                },
                Radius = 0,
                Margin = 80,
                TitleAlign = TextAlign.Left,
            };
        }

        // ------------------------------------------------------------------ colours

        private static Dictionary<string, string> DefaultClrMap() => new()
        {
            ["bg1"] = "lt1",
            ["tx1"] = "dk1",
            ["bg2"] = "lt2",
            ["tx2"] = "dk2",
        };

        private Color? ReadColor(OpenXmlElement? parent)
        {
            if (parent == null) return null;
            foreach (var child in parent.ChildElements)
            {
                Color? baseColor = child switch
                {
                    A.RgbColorModelHex rgb => SlideStyle.ParseHex("#" + rgb.Val?.Value),
                    A.SchemeColor scheme => SchemeColor(scheme.Val?.InnerText),
                    A.SystemColor sys => SlideStyle.ParseHex("#" + (sys.LastColor?.Value ?? "000000")),
                    A.PresetColor preset => Preset(preset.Val?.InnerText),
                    _ => null,
                };
                if (baseColor == null) continue;
                return ApplyModifiers(baseColor.Value, child);
            }
            return null;
        }

        private Color? SchemeColor(string? name)
        {
            if (name == null || name == "phClr") return null;
            if (_clrMap.TryGetValue(name, out var mapped)) name = mapped;
            return _scheme.TryGetValue(name, out var hex) ? SlideStyle.ParseHex(hex) : null;
        }

        private static Color? Preset(string? name) => name switch
        {
            "white" => Colors.White,
            "black" => Colors.Black,
            "red" => Colors.Red,
            "blue" => Colors.Blue,
            "green" => Colors.Green,
            "yellow" => Colors.Yellow,
            "gray" or "grey" => Colors.Gray,
            _ => null,
        };

        private static Color ApplyModifiers(Color c, OpenXmlElement colorElement)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, a = 1;
            foreach (var m in colorElement.ChildElements)
            {
                var val = m.GetAttributes().FirstOrDefault(x => x.LocalName == "val").Value;
                if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                v /= 100000.0;
                switch (m.LocalName)
                {
                    case "alpha":
                        a = v;
                        break;
                    case "lumMod":
                    case "lumOff":
                    {
                        RgbToHsl(r, g, b, out var h, out var s, out var l);
                        l = m.LocalName == "lumMod" ? l * v : l + v;
                        HslToRgb(h, s, MathEx.Clamp(l, 0, 1), out r, out g, out b);
                        break;
                    }
                    case "tint":
                        r += (1 - r) * (1 - v);
                        g += (1 - g) * (1 - v);
                        b += (1 - b) * (1 - v);
                        break;
                    case "shade":
                        r *= v;
                        g *= v;
                        b *= v;
                        break;
                }
            }
            return Color.FromArgb((byte)Math.Round(MathEx.Clamp(a, 0, 1) * 255), (byte)Math.Round(MathEx.Clamp(r, 0, 1) * 255),
                (byte)Math.Round(MathEx.Clamp(g, 0, 1) * 255), (byte)Math.Round(MathEx.Clamp(b, 0, 1) * 255));
        }

        private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (Math.Abs(max - min) < 1e-9)
            {
                h = s = 0;
                return;
            }
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s <= 0)
            {
                r = g = b = l;
                return;
            }
            static double Hue(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p2 = 2 * l - q2;
            r = Hue(p2, q2, h + 1.0 / 3);
            g = Hue(p2, q2, h);
            b = Hue(p2, q2, h - 1.0 / 3);
        }

        private static string Hex(Color c) => SlideStyle.ToHex(c);

        // ------------------------------------------------------------------ slides

        private sealed class Context
        {
            public SlidePart Slide = null!;
            public SlideLayoutPart? Layout;
            public SlideMasterPart? Master;
        }

        private Slide ReadSlide(SlidePart part)
        {
            var slide = new Slide { Layout = "blank", Id = _slideIds.TryGetValue(part, out var known) ? known : Ids.New() };
            _spids.Clear();
            var ctx = new Context { Slide = part, Layout = part.SlideLayoutPart, Master = part.SlideLayoutPart?.SlideMasterPart };
            _clrMap = ReadClrMap(ctx.Master?.SlideMaster?.ColorMap);
            var data = part.Slide;
            if (data?.Show?.Value == false) slide.Hidden = true;

            slide.Background = ReadBackground(data?.CommonSlideData?.Background, part)
                               ?? ReadBackground(ctx.Layout?.SlideLayout?.CommonSlideData?.Background, ctx.Layout)
                               ?? ReadBackground(ctx.Master?.SlideMaster?.CommonSlideData?.Background, ctx.Master);
            slide.ShowDecorations = false;

            // Pictures and shapes the master and layout show on every slide (logos, bars).
            var showMaster = data?.ShowMasterShapes?.Value ?? true;
            if (showMaster)
            {
                AddInherited(slide, ctx.Master?.SlideMaster?.CommonSlideData?.ShapeTree, ctx.Master, ctx);
                AddInherited(slide, ctx.Layout?.SlideLayout?.CommonSlideData?.ShapeTree, ctx.Layout, ctx);
            }

            var tree = data?.CommonSlideData?.ShapeTree;
            if (tree != null) ReadTree(tree, slide, ctx, part, Matrix.Identity, null);
            ReadTiming(data, slide, part);

            slide.Notes = ReadNotes(part);
            slide.Transition = ReadTransition(data);
            FinishTexts(slide);
            return slide;
        }

        private static Dictionary<string, string> ReadClrMap(P.ColorMap? map)
        {
            var result = DefaultClrMap();
            if (map == null) return result;
            if (map.Background1?.InnerText is { Length: > 0 } bg1) result["bg1"] = bg1;
            if (map.Text1?.InnerText is { Length: > 0 } tx1) result["tx1"] = tx1;
            if (map.Background2?.InnerText is { Length: > 0 } bg2) result["bg2"] = bg2;
            if (map.Text2?.InnerText is { Length: > 0 } tx2) result["tx2"] = tx2;
            return result;
        }

        private void AddInherited(Slide slide, P.ShapeTree? tree, OpenXmlPart? owner, Context ctx)
        {
            if (tree == null || owner == null) return;
            // Only non-placeholder content is drawn from layouts and masters.
            foreach (var child in tree.ChildElements)
            {
                if (child is P.Shape sp && sp.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape != null) continue;
                if (child is not (P.Picture or P.Shape or P.ConnectionShape)) continue;
                var holder = new P.ShapeTree();
                holder.AppendChild(child.CloneNode(true));
                ReadTree(holder, slide, ctx, owner, Matrix.Identity, null);
            }
        }

        private Fill? ReadBackground(P.Background? bg, OpenXmlPart? owner)
        {
            if (bg == null || owner == null) return null;
            var props = bg.BackgroundProperties;
            if (props != null)
            {
                if (props.GetFirstChild<A.SolidFill>() is { } solid && ReadColor(solid) is { } c) return Fill.Solid(Hex(c));
                if (props.GetFirstChild<A.GradientFill>() is { } grad) return ReadGradient(grad);
                if (props.GetFirstChild<A.BlipFill>() is { } blip && ReadBlip(blip.Blip, owner) is { } media) return Fill.Image(media);
                return null;
            }
            if (bg.BackgroundStyleReference is { } reference && ReadColor(reference) is { } refColor) return Fill.Solid(Hex(refColor));
            return null;
        }

        private Fill? ReadGradient(A.GradientFill grad)
        {
            var stops = grad.GradientStopList?.Elements<A.GradientStop>().OrderBy(s => s.Position?.Value ?? 0).ToList();
            if (stops == null || stops.Count == 0) return null;
            var first = ReadColor(stops[0]) ?? Colors.White;
            var last = ReadColor(stops[stops.Count - 1]) ?? first;
            var angle = (grad.GetFirstChild<A.LinearGradientFill>()?.Angle?.Value ?? 5400000) / 60000.0;
            return grad.GetFirstChild<A.PathGradientFill>() != null ? Fill.Radial(Hex(first), Hex(last)) : Fill.Linear(Hex(first), Hex(last), angle);
        }

        private string? ReadBlip(A.Blip? blip, OpenXmlPart owner)
        {
            var embed = blip?.Embed?.Value;
            if (embed == null) return null;
            try
            {
                if (owner.GetPartById(embed) is not ImagePart image) return null;
                using var s = image.GetStream(FileMode.Open, FileAccess.Read);
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                if (MediaStore.Decode(bytes, 64) == null)
                {
                    _unreadablePictures++;
                    return null;
                }
                return _media.Add(bytes, Path.GetExtension(image.Uri.OriginalString));
            }
            catch (Exception)
            {
                _unreadablePictures++;
                return null;
            }
        }

        // ------------------------------------------------------------------ shape tree

        private readonly struct Matrix
        {
            public Matrix(double sx, double sy, double ox, double oy)
            {
                Sx = sx;
                Sy = sy;
                Ox = ox;
                Oy = oy;
            }

            public double Sx { get; }
            public double Sy { get; }
            public double Ox { get; }
            public double Oy { get; }

            public static Matrix Identity => new(1, 1, 0, 0);

            public Rect Apply(Rect emu) => new(emu.X * Sx + Ox, emu.Y * Sy + Oy, Math.Max(0, emu.Width * Sx), Math.Max(0, emu.Height * Sy));

            /// <summary>Applies <paramref name="inner"/> first, then this.</summary>
            public Matrix Then(Matrix inner) => new(inner.Sx * Sx, inner.Sy * Sy, inner.Ox * Sx + Ox, inner.Oy * Sy + Oy);
        }

        private void ReadTree(OpenXmlCompositeElement tree, Slide slide, Context ctx, OpenXmlPart owner, Matrix transform, string? groupId)
        {
            foreach (var child in tree.ChildElements)
            {
                _owner = owner;
                switch (child)
                {
                    case P.Shape sp:
                        ReadShape(sp, slide, ctx, owner, transform, groupId);
                        break;
                    case P.Picture pic:
                        ReadPicture(pic, slide, owner, transform, groupId);
                        break;
                    case P.ConnectionShape cxn:
                        ReadConnector(cxn, slide, transform, groupId);
                        break;
                    case P.GroupShape group:
                        ReadGroup(group, slide, ctx, owner, transform, groupId);
                        break;
                    case P.GraphicFrame frame:
                        ReadGraphicFrame(frame, slide, owner, transform, groupId);
                        break;
                    case AlternateContent alt:
                        // The fallback uses only the basic schema.
                        var fallback = alt.Elements<AlternateContentFallback>().FirstOrDefault();
                        if (fallback != null) ReadTree(fallback, slide, ctx, owner, transform, groupId);
                        break;
                    case P.ContentPart:
                        Skip("ink drawing");
                        break;
                }
            }
        }

        /// <summary>A transform's rectangle in slide units (or null when there's none).</summary>
        private Rect? Box(A.Transform2D? xfrm, Matrix m, out double rotation, out bool flipH, out bool flipV)
        {
            rotation = 0;
            flipH = flipV = false;
            if (xfrm?.Offset == null || xfrm.Extents == null) return null;
            rotation = (xfrm.Rotation?.Value ?? 0) / 60000.0;
            flipH = xfrm.HorizontalFlip?.Value == true;
            flipV = xfrm.VerticalFlip?.Value == true;
            var emu = new Rect(xfrm.Offset.X?.Value ?? 0, xfrm.Offset.Y?.Value ?? 0, xfrm.Extents.Cx?.Value ?? 0, xfrm.Extents.Cy?.Value ?? 0);
            var r = m.Apply(emu);
            return new Rect(r.X * _k * EmuPerUnit / EmuPerUnit, r.Y * _k, r.Width * _k, r.Height * _k);
        }

        private static void Place(Element e, Rect box, double rotation, bool flipH, bool flipV, string? groupId)
        {
            e.X = Math.Round(box.X, 2);
            e.Y = Math.Round(box.Y, 2);
            e.Width = Math.Round(Math.Max(0, box.Width), 2);
            e.Height = Math.Round(Math.Max(0, box.Height), 2);
            e.Rotation = Math.Round(rotation, 2);
            e.FlipH = flipH;
            e.FlipV = flipV;
            e.GroupId = groupId;
        }

        /// <summary>The placeholders on the layout and master that a slide placeholder takes its position and text style from.</summary>
        private static (P.Shape? Layout, P.Shape? Master) Inherited(P.PlaceholderShape? ph, Context ctx)
        {
            if (ph == null) return (null, null);
            P.Shape? Find(P.ShapeTree? tree)
            {
                if (tree == null) return null;
                var shapes = tree.Descendants<P.Shape>().Where(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape != null).ToList();
                P.PlaceholderShape Ph(P.Shape s) => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape!;
                var type = ph.Type?.Value;
                var idx = ph.Index?.Value;
                P.Shape? match = null;
                if (idx != null) match = shapes.FirstOrDefault(s => Ph(s).Index?.Value == idx);
                if (match == null && type != null)
                {
                    match = shapes.FirstOrDefault(s => Ph(s).Type?.Value == type)
                            ?? (type == P.PlaceholderValues.CenteredTitle ? shapes.FirstOrDefault(s => Ph(s).Type?.Value == P.PlaceholderValues.Title) : null)
                            ?? (type == P.PlaceholderValues.SubTitle ? shapes.FirstOrDefault(s => Ph(s).Type?.Value == P.PlaceholderValues.Body) : null);
                }
                if (match == null && type == null) match = shapes.FirstOrDefault(s => Ph(s).Type?.Value == P.PlaceholderValues.Body || Ph(s).Type == null);
                return match;
            }
            return (Find(ctx.Layout?.SlideLayout?.CommonSlideData?.ShapeTree), Find(ctx.Master?.SlideMaster?.CommonSlideData?.ShapeTree));
        }

        private static readonly HashSet<string> KnownShapes = new()
        {
            "rect", "roundRect", "ellipse", "triangle", "rtTriangle", "diamond", "wedgeRectCallout", "wedgeRoundRectCallout", "wedgeEllipseCallout",
            "snip1Rect", "round1Rect", "round2SameRect", "line", "straightConnector1",
        };

        private void ReadShape(P.Shape sp, Slide slide, Context ctx, OpenXmlPart owner, Matrix m, string? groupId)
        {
            var ph = sp.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
            var (layoutPh, masterPh) = owner is SlidePart ? Inherited(ph, ctx) : (null, null);
            var spPr = sp.ShapeProperties;
            var box = Box(spPr?.Transform2D, m, out var rot, out var fh, out var fv)
                      ?? Box(layoutPh?.ShapeProperties?.Transform2D, Matrix.Identity, out rot, out fh, out fv)
                      ?? Box(masterPh?.ShapeProperties?.Transform2D, Matrix.Identity, out rot, out fh, out fv);
            if (box == null) return;

            var name = sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value;
            var preset = spPr?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText ?? (spPr?.GetFirstChild<A.CustomGeometry>() != null ? "custom" : "rect");
            var fill = ReadFill(spPr, sp.ShapeStyle, owner);
            var stroke = ReadStroke(spPr?.GetFirstChild<A.Outline>(), sp.ShapeStyle);
            var body = ReadText(sp.TextBody, ph, layoutPh, masterPh, ctx);

            if (preset is "line" or "straightConnector1")
            {
                var line = new ShapeElement { Shape = ShapeKind.Line, Fill = Fill.None, Stroke = stroke ?? new Stroke { Color = "text", Width = 2 }, Name = name };
                ReadHeads(spPr?.GetFirstChild<A.Outline>(), line);
                Place(line, box.Value, 0, fh, fv, groupId);
                slide.Elements.Add(line);
                Added(line, sp.NonVisualShapeProperties?.NonVisualDrawingProperties, owner);
                return;
            }

            var hasFill = fill != null && fill.Kind != FillKind.None;
            var hasStroke = stroke != null;
            var isTextBox = sp.NonVisualShapeProperties?.NonVisualShapeDrawingProperties?.TextBox?.Value == true || ph != null;
            if (!hasFill && !hasStroke && (preset == "rect" || isTextBox))
            {
                if (body == null || body.Content.IsEmpty) return; // empty placeholder: nothing to show
                var text = new TextElement { Text = body, Name = name };
                Place(text, box.Value, rot, false, false, groupId);
                slide.Elements.Add(text);
                Added(text, sp.NonVisualShapeProperties?.NonVisualDrawingProperties, owner);
                return;
            }

            var kind = preset switch
            {
                "roundRect" or "snip1Rect" or "round1Rect" or "round2SameRect" => ShapeKind.RoundedRectangle,
                "ellipse" => ShapeKind.Ellipse,
                "triangle" or "rtTriangle" => ShapeKind.Triangle,
                "diamond" => ShapeKind.Diamond,
                "wedgeRectCallout" or "wedgeRoundRectCallout" or "wedgeEllipseCallout" => ShapeKind.Callout,
                _ => ShapeKind.Rectangle,
            };
            if (!KnownShapes.Contains(preset) && !preset.StartsWith("actionButton", StringComparison.Ordinal)) _warnings.Add("Shapes Stagecraft doesn't have were imported as rectangles.");
            var shape = new ShapeElement { Shape = kind, Fill = fill ?? Fill.None, Stroke = stroke, Text = body is { Content.IsEmpty: false } ? body : null, Name = name };
            if (kind == ShapeKind.RoundedRectangle)
            {
                var adj = spPr?.GetFirstChild<A.PresetGeometry>()?.AdjustValueList?.Elements<A.ShapeGuide>().FirstOrDefault()?.Formula?.Value;
                var fraction = adj != null && adj.StartsWith("val ", StringComparison.Ordinal) && double.TryParse(adj.Substring(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 100000 : 0.1667;
                shape.CornerRadius = Math.Round(Math.Min(box.Value.Width, box.Value.Height) * fraction, 1);
            }
            if (preset.StartsWith("actionButton", StringComparison.Ordinal))
            {
                shape.Shape = ShapeKind.ActionButton;
                shape.Action = preset switch
                {
                    "actionButtonBackPrevious" => ActionButtonKind.Previous,
                    "actionButtonForwardNext" => ActionButtonKind.Next,
                    "actionButtonBeginning" or "actionButtonHome" => ActionButtonKind.First,
                    "actionButtonEnd" => ActionButtonKind.Last,
                    "actionButtonReturn" => ActionButtonKind.Return,
                    _ => ActionButtonKind.Custom,
                };
                shape.CornerRadius = Math.Round(Math.Min(box.Value.Width, box.Value.Height) * 0.24, 1);
            }
            Place(shape, box.Value, rot, fh, fv, groupId);
            slide.Elements.Add(shape);
            Added(shape, sp.NonVisualShapeProperties?.NonVisualDrawingProperties, owner);
        }

        private Fill? ReadFill(OpenXmlCompositeElement? spPr, P.ShapeStyle? style, OpenXmlPart owner)
        {
            if (spPr != null)
            {
                if (spPr.GetFirstChild<A.NoFill>() != null) return Fill.None;
                if (spPr.GetFirstChild<A.SolidFill>() is { } solid && ReadColor(solid) is { } c) return Fill.Solid(Hex(c));
                if (spPr.GetFirstChild<A.GradientFill>() is { } grad) return ReadGradient(grad);
                if (spPr.GetFirstChild<A.BlipFill>() is { } blip && ReadBlip(blip.Blip, owner) is { } media) return Fill.Image(media);
            }
            if (style?.FillReference is { } fillRef && (fillRef.Index?.Value ?? 0) > 0 && ReadColor(fillRef) is { } rc) return Fill.Solid(Hex(rc));
            return null;
        }

        private Stroke? ReadStroke(A.Outline? ln, P.ShapeStyle? style)
        {
            if (ln?.GetFirstChild<A.NoFill>() != null) return null;
            var color = ln?.GetFirstChild<A.SolidFill>() is { } solid ? ReadColor(solid) : null;
            if (color == null && style?.LineReference is { } lineRef && (lineRef.Index?.Value ?? 0) > 0) color = ReadColor(lineRef);
            if (color == null) return null;
            var width = (ln?.Width?.Value ?? 12700) * _k;
            var dash = ln?.GetFirstChild<A.PresetDash>()?.Val?.InnerText switch
            {
                "dash" or "lgDash" or "dashDot" or "lgDashDot" or "sysDash" => DashStyleKind.Dash,
                "dot" or "sysDot" => DashStyleKind.Dot,
                _ => DashStyleKind.Solid,
            };
            return new Stroke { Color = Hex(color.Value), Width = Math.Max(0.5, Math.Round(width, 2)), Dash = dash };
        }

        private static void ReadHeads(A.Outline? ln, ShapeElement line)
        {
            static ArrowHead Head(string? type) => type switch
            {
                null or "none" => ArrowHead.None,
                "oval" => ArrowHead.Dot,
                _ => ArrowHead.Arrow,
            };
            line.StartHead = Head(ln?.GetFirstChild<A.HeadEnd>()?.Type?.InnerText);
            line.EndHead = Head(ln?.GetFirstChild<A.TailEnd>()?.Type?.InnerText);
            if (line.EndHead == ArrowHead.Arrow && line.StartHead == ArrowHead.None && line.Shape == ShapeKind.Line) line.Shape = ShapeKind.Arrow;
        }

        private void ReadConnector(P.ConnectionShape cxn, Slide slide, Matrix m, string? groupId)
        {
            var spPr = cxn.ShapeProperties;
            var box = Box(spPr?.Transform2D, m, out _, out var fh, out var fv);
            if (box == null) return;
            var preset = spPr?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText ?? "line";
            var bent = preset.StartsWith("bent", StringComparison.Ordinal);
            var line = new ShapeElement
            {
                Shape = bent ? ShapeKind.Connector : ShapeKind.Line,
                Elbow = bent,
                Fill = Fill.None,
                Stroke = ReadStroke(spPr?.GetFirstChild<A.Outline>(), cxn.ShapeStyle) ?? new Stroke { Color = "text", Width = 2 },
                Name = cxn.NonVisualConnectionShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            };
            ReadHeads(spPr?.GetFirstChild<A.Outline>(), line);
            Place(line, box.Value, 0, fh, fv, groupId);
            slide.Elements.Add(line);
            Added(line, cxn.NonVisualConnectionShapeProperties?.NonVisualDrawingProperties, _owner);
        }

        private void ReadPicture(P.Picture pic, Slide slide, OpenXmlPart owner, Matrix m, string? groupId)
        {
            var box = Box(pic.ShapeProperties?.Transform2D, m, out var rot, out var fh, out var fv);
            if (box == null) return;
            var app = pic.NonVisualPictureProperties?.ApplicationNonVisualDrawingProperties;
            if (app != null && app.ChildElements.Any(c => c.LocalName is "videoFile" or "audioFile" or "quickTimeFile"))
            {
                if (ReadMedia(pic, app, slide, owner, box.Value, rot, fh, fv, groupId)) return;
                Skip("video or sound");
            }
            var blipFill = pic.BlipFill;
            if (blipFill == null) return;
            var media = ReadBlip(blipFill.Blip, owner);
            if (media == null) return;
            var image = new ImageElement
            {
                Media = media,
                Fit = ImageFit.Stretch,
                AltText = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value,
                Name = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value,
            };
            if (blipFill.SourceRectangle is { } src)
            {
                var crop = new Crop
                {
                    Left = (src.Left?.Value ?? 0) / 100000.0,
                    Top = (src.Top?.Value ?? 0) / 100000.0,
                    Right = (src.Right?.Value ?? 0) / 100000.0,
                    Bottom = (src.Bottom?.Value ?? 0) / 100000.0,
                };
                if (Math.Abs(crop.Left) + Math.Abs(crop.Top) + Math.Abs(crop.Right) + Math.Abs(crop.Bottom) > 0.0001) image.Crop = crop;
            }
            var geometry = pic.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText;
            if (geometry == "ellipse") image.Mask = ImageMask.Circle;
            else if (geometry == "roundRect") image.CornerRadius = Math.Round(Math.Min(box.Value.Width, box.Value.Height) * 0.1667, 1);
            image.Stroke = ReadStroke(pic.ShapeProperties?.GetFirstChild<A.Outline>(), pic.ShapeStyle);
            Place(image, box.Value, rot, fh, fv, groupId);
            slide.Elements.Add(image);
            Added(image, pic.NonVisualPictureProperties?.NonVisualDrawingProperties, owner);
        }

        private void ReadGroup(P.GroupShape group, Slide slide, Context ctx, OpenXmlPart owner, Matrix m, string? groupId)
        {
            var xfrm = group.GroupShapeProperties?.TransformGroup;
            var inner = m;
            if (xfrm?.Offset != null && xfrm.Extents != null && xfrm.ChildOffset != null && xfrm.ChildExtents != null)
            {
                double cw = xfrm.ChildExtents.Cx?.Value ?? 1, ch = xfrm.ChildExtents.Cy?.Value ?? 1;
                var sx = cw == 0 ? 1 : (xfrm.Extents.Cx?.Value ?? 0) / cw;
                var sy = ch == 0 ? 1 : (xfrm.Extents.Cy?.Value ?? 0) / ch;
                var local = new Matrix(sx, sy, (xfrm.Offset.X?.Value ?? 0) - (xfrm.ChildOffset.X?.Value ?? 0) * sx, (xfrm.Offset.Y?.Value ?? 0) - (xfrm.ChildOffset.Y?.Value ?? 0) * sy);
                inner = m.Then(local);
            }
            var id = groupId ?? Ids.New();
            if (owner is SlidePart && group.NonVisualGroupShapeProperties?.NonVisualDrawingProperties?.Id?.Value is { } gid) _spids[gid] = id;
            ReadTree(group, slide, ctx, owner, inner, id);
            _owner = owner;
        }

        private void ReadGraphicFrame(P.GraphicFrame frame, Slide slide, OpenXmlPart owner, Matrix m, string? groupId)
        {
            var xfrm = frame.Transform;
            if (xfrm?.Offset == null || xfrm.Extents == null) return;
            var r = m.Apply(new Rect(xfrm.Offset.X?.Value ?? 0, xfrm.Offset.Y?.Value ?? 0, xfrm.Extents.Cx?.Value ?? 0, xfrm.Extents.Cy?.Value ?? 0));
            var box = new Rect(r.X * _k, r.Y * _k, r.Width * _k, r.Height * _k);
            var data = frame.Graphic?.GraphicData;
            if (data == null) return;
            var uri = data.Uri?.Value ?? "";
            var name = frame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Name?.Value;
            if (data.GetFirstChild<A.Table>() is { } table)
            {
                var element = new TableElement { Name = name };
                foreach (var row in table.Elements<A.TableRow>())
                {
                    var cells = row.Elements<A.TableCell>()
                        .Select(c => string.Join("\n", c.TextBody?.Elements<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text))) ?? Array.Empty<string>()))
                        .ToList();
                    element.Cells.Add(cells);
                }
                element.HeaderRow = table.TableProperties?.FirstRow?.Value ?? true;
                element.Banded = table.TableProperties?.BandRow?.Value ?? true;
                var widths = table.TableGrid?.Elements<A.GridColumn>().Select(g => (double)(g.Width?.Value ?? 1)).ToList();
                if (widths != null && widths.Count > 0) element.ColumnWidths = widths;
                var firstSize = table.Descendants<A.RunProperties>().FirstOrDefault(rp => rp.FontSize != null)?.FontSize?.Value;
                if (firstSize != null) element.FontSize = Size(firstSize.Value);
                Place(element, box, 0, false, false, groupId);
                slide.Elements.Add(element);
                Added(element, frame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties, owner);
                return;
            }
            if (uri.EndsWith("/chart", StringComparison.Ordinal))
            {
                var chartRef = data.GetFirstChild<C.ChartReference>();
                if (chartRef?.Id?.Value is { } rid && owner.GetPartById(rid) is ChartPart chartPart && ReadChart(chartPart) is { } chart)
                {
                    chart.Name = name;
                    Place(chart, box, 0, false, false, groupId);
                    slide.Elements.Add(chart);
                    Added(chart, frame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties, owner);
                }
                else Skip("chart");
                return;
            }
            if (uri.Contains("diagram")) Skip("SmartArt graphic");
            else Skip("embedded object");
        }

        private static ChartElement? ReadChart(ChartPart part)
        {
            var plot = part.ChartSpace?.GetFirstChild<C.Chart>()?.PlotArea;
            if (plot == null) return null;
            OpenXmlCompositeElement? group = plot.GetFirstChild<C.BarChart>();
            group ??= plot.GetFirstChild<C.LineChart>();
            group ??= plot.GetFirstChild<C.PieChart>();
            group ??= plot.GetFirstChild<C.DoughnutChart>();
            group ??= plot.GetFirstChild<C.AreaChart>();
            if (group == null) return null;
            var chart = new ChartElement
            {
                Kind = group switch
                {
                    C.LineChart or C.AreaChart => ChartKind.Line,
                    C.PieChart => ChartKind.Pie,
                    C.DoughnutChart => ChartKind.Donut,
                    _ => ChartKind.Bar,
                },
            };
            foreach (var ser in group.ChildElements.Where(e => e.LocalName == "ser"))
            {
                var name = string.Concat(ser.Elements<C.SeriesText>().SelectMany(t => t.Descendants<C.NumericValue>()).Select(v => v.Text));
                var categories = ser.Elements<C.CategoryAxisData>().SelectMany(c => c.Descendants<C.NumericValue>()).Select(v => v.Text ?? "").ToList();
                var values = ser.Elements<C.Values>().SelectMany(c => c.Descendants<C.NumericValue>())
                    .Select(v => double.TryParse(v.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToList();
                if (chart.Categories.Count == 0) chart.Categories = categories;
                chart.Series.Add(new ChartSeries { Name = name.Length > 0 ? name : $"Series {chart.Series.Count + 1}", Values = values });
            }
            return chart.Series.Count == 0 ? null : chart;
        }

        // ------------------------------------------------------------------ text

        private sealed class LevelStyle
        {
            public int? Size;
            public bool? Bold;
            public bool? Italic;
            public string? Color;
            public string? Font;
            public TextAlign? Align;
            public ListKind? List;
        }

        private TextBody? ReadText(P.TextBody? textBody, P.PlaceholderShape? ph, P.Shape? layoutPh, P.Shape? masterPh, Context ctx)
        {
            if (textBody == null) return null;
            var body = new TextBody { AutoFit = false };
            var type = ph?.Type?.Value;
            var isTitle = type == P.PlaceholderValues.Title || type == P.PlaceholderValues.CenteredTitle;
            body.Role = isTitle ? TextRole.Title : type == P.PlaceholderValues.SubTitle ? TextRole.Subtitle : TextRole.Body;

            var bodyPr = textBody.BodyProperties;
            var anchor = bodyPr?.Anchor?.InnerText ?? layoutPh?.TextBody?.BodyProperties?.Anchor?.InnerText ?? masterPh?.TextBody?.BodyProperties?.Anchor?.InnerText;
            body.VAlign = anchor switch { "ctr" => VerticalAlign.Middle, "b" => VerticalAlign.Bottom, _ => VerticalAlign.Top };
            var lIns = (bodyPr?.LeftInset?.Value ?? 91440) * _k;
            var tIns = (bodyPr?.TopInset?.Value ?? 45720) * _k;
            body.Inset = Math.Round(Math.Min(lIns, tIns * 2), 1);
            body.AutoFit = bodyPr?.GetFirstChild<A.NormalAutoFit>() != null || layoutPh?.TextBody?.BodyProperties?.GetFirstChild<A.NormalAutoFit>() != null
                           || masterPh?.TextBody?.BodyProperties?.GetFirstChild<A.NormalAutoFit>() != null;

            // The master's text styles for this kind of placeholder.
            var txStyles = ctx.Master?.SlideMaster?.TextStyles;
            OpenXmlCompositeElement? masterLevels = isTitle ? txStyles?.TitleStyle : ph != null ? txStyles?.BodyStyle : txStyles?.OtherStyle;

            foreach (var p in textBody.Elements<A.Paragraph>())
            {
                var level = p.ParagraphProperties?.Level?.Value ?? 0;
                var chain = new[]
                {
                    LevelOf(textBody.ListStyle, level), LevelOf(layoutPh?.TextBody?.ListStyle, level), LevelOf(masterPh?.TextBody?.ListStyle, level), LevelOf(masterLevels, level),
                };
                var inherited = new LevelStyle();
                foreach (var l in chain)
                {
                    if (l == null) continue;
                    inherited.Size ??= l.Size;
                    inherited.Bold ??= l.Bold;
                    inherited.Italic ??= l.Italic;
                    inherited.Color ??= l.Color;
                    inherited.Font ??= l.Font;
                    inherited.Align ??= l.Align;
                    inherited.List ??= l.List;
                }
                var own = ReadLevel(p.ParagraphProperties);
                var paragraph = new Paragraph
                {
                    Level = Math.Min(level, 4),
                    Align = own.Align ?? inherited.Align,
                    List = own.List ?? inherited.List ?? ListKind.None,
                };
                foreach (var child in p.ChildElements)
                {
                    switch (child)
                    {
                        case A.Run run:
                            paragraph.Runs.Add(ReadRun(run.RunProperties, run.Text?.Text ?? "", own, inherited));
                            break;
                        case A.Break br:
                            paragraph.Runs.Add(ReadRun(br.RunProperties, "\n", own, inherited));
                            break;
                        case A.Field field:
                            paragraph.Runs.Add(ReadRun(field.RunProperties, field.Text?.Text ?? "", own, inherited));
                            break;
                    }
                }
                if (paragraph.Runs.Count == 0)
                {
                    var end = p.GetFirstChild<A.EndParagraphRunProperties>();
                    paragraph.Runs.Add(new TextRun { Size = Size(end?.FontSize?.Value ?? own.Size ?? inherited.Size) });
                }
                if (paragraph.Runs.All(r => r.Text.Length == 0)) paragraph.List = ListKind.None;
                body.Content.Paragraphs.Add(paragraph);
            }
            var aligns = body.Content.Paragraphs.Select(p => p.Align).Distinct().ToList();
            if (aligns.Count == 1 && aligns[0] != null)
            {
                body.Align = aligns[0]!.Value;
                foreach (var p in body.Content.Paragraphs) p.Align = null;
            }
            // PowerPoint often keeps empty paragraphs at the end.
            while (body.Content.Paragraphs.Count > 1 && body.Content.Paragraphs[body.Content.Paragraphs.Count - 1].Text.Length == 0)
                body.Content.Paragraphs.RemoveAt(body.Content.Paragraphs.Count - 1);
            return body;
        }

        /// <summary>Hundredths of a point to slide units.</summary>
        private double? Size(int? hundredthsOfPoint) => hundredthsOfPoint == null ? null : Math.Round(hundredthsOfPoint.Value / 100.0 * 12700 * _k, 1);

        private LevelStyle? LevelOf(OpenXmlCompositeElement? styles, int level)
        {
            var element = styles?.ChildElements.OfType<OpenXmlCompositeElement>().FirstOrDefault(e => e.LocalName == $"lvl{level + 1}pPr");
            return element == null ? null : ReadLevel(element);
        }

        private LevelStyle ReadLevel(OpenXmlCompositeElement? pPr)
        {
            var s = new LevelStyle();
            if (pPr == null) return s;
            var algn = pPr.GetAttributes().FirstOrDefault(a => a.LocalName == "algn").Value;
            s.Align = algn switch { "ctr" => TextAlign.Center, "r" => TextAlign.Right, "just" or "dist" => TextAlign.Justify, "l" => TextAlign.Left, _ => null };
            if (pPr.GetFirstChild<A.NoBullet>() != null) s.List = ListKind.None;
            else if (pPr.GetFirstChild<A.AutoNumberedBullet>() != null) s.List = ListKind.Number;
            else if (pPr.GetFirstChild<A.CharacterBullet>() != null || pPr.GetFirstChild<A.PictureBullet>() != null) s.List = ListKind.Bullet;
            var def = pPr.GetFirstChild<A.DefaultRunProperties>();
            if (def != null)
            {
                s.Size = def.FontSize?.Value;
                s.Bold = def.Bold?.Value;
                s.Italic = def.Italic?.Value;
                if (ReadColor(def.GetFirstChild<A.SolidFill>()) is { } c) s.Color = Hex(c);
                s.Font = Typeface(def.GetFirstChild<A.LatinFont>()?.Typeface?.Value);
            }
            return s;
        }

        private string? Typeface(string? face) => face switch
        {
            null or "" => null,
            "+mj-lt" => _majorFont,
            "+mn-lt" => _minorFont,
            _ when face.StartsWith("+", StringComparison.Ordinal) => null,
            _ => face,
        };

        private TextRun ReadRun(A.TextCharacterPropertiesType? rPr, string text, LevelStyle own, LevelStyle inherited)
        {
            var run = new TextRun { Text = text.Replace('\v', '\n') };
            run.Size = Size(rPr?.FontSize?.Value ?? own.Size ?? inherited.Size);
            run.Bold = rPr?.Bold?.Value ?? own.Bold ?? inherited.Bold;
            run.Italic = rPr?.Italic?.Value ?? own.Italic ?? inherited.Italic;
            var underline = rPr?.Underline?.InnerText;
            if (underline != null && underline != "none") run.Underline = true;
            var strike = rPr?.Strike?.InnerText;
            if (strike != null && strike != "noStrike") run.Strike = true;
            run.Color = ReadColor(rPr?.GetFirstChild<A.SolidFill>()) is { } c ? Hex(c) : own.Color ?? inherited.Color;
            run.Font = Typeface(rPr?.GetFirstChild<A.LatinFont>()?.Typeface?.Value) ?? own.Font ?? inherited.Font;
            if (run.Bold == false) run.Bold = null;
            run.Link = ReadLink(rPr?.GetFirstChild<A.HyperlinkOnClick>(), _owner);
            if (run.Italic == false) run.Italic = null;
            return run;
        }

        /// <summary>Text without explicit sizes gets PowerPoint's 18 pt default and the theme's text font.</summary>
        private void FinishTexts(Slide slide)
        {
            foreach (var e in slide.Elements)
            {
                var body = e switch { TextElement t => t.Text, ShapeElement s => s.Text, _ => null };
                if (body == null) continue;
                foreach (var p in body.Content.Paragraphs)
                foreach (var r in p.Runs)
                {
                    r.Size ??= Size(1800);
                    if (r.Link == null) r.Color ??= "text";
                    r.Font ??= body.Role == TextRole.Title ? _majorFont : _minorFont;
                }
                body.LineSpacing ??= 1.2;
            }
        }

        // ------------------------------------------------------------------ notes, transitions

        private static string ReadNotes(SlidePart part)
        {
            var notes = part.NotesSlidePart?.NotesSlide;
            if (notes == null) return "";
            var body = notes.Descendants<P.Shape>().FirstOrDefault(s =>
            {
                var ph = s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
                return ph != null && ph.Type?.Value == P.PlaceholderValues.Body;
            });
            if (body?.TextBody == null) return "";
            return string.Join("\n", body.TextBody.Elements<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text)))).Trim();
        }

        private static Transition ReadTransition(P.Slide? slide)
        {
            var result = new Transition();
            if (slide == null) return result;
            OpenXmlElement? transition = slide.GetFirstChild<P.Transition>();
            if (transition == null)
            {
                // PowerPoint 2010+ writes transitions inside mc:AlternateContent; the choice may hold Morph.
                foreach (var alt in slide.Elements<AlternateContent>())
                {
                    var choice = alt.Elements<AlternateContentChoice>().FirstOrDefault();
                    var t = choice?.ChildElements.FirstOrDefault(c => c.LocalName == "transition");
                    if (t == null) continue;
                    transition = t;
                    break;
                }
            }
            if (transition == null) return result;
            var effect = transition.ChildElements.FirstOrDefault(c => c.LocalName != "sndAc" && c.LocalName != "extLst");
            result.Kind = effect?.LocalName switch
            {
                null => TransitionKind.Fade,
                "fade" or "dissolve" => TransitionKind.Fade,
                "push" => TransitionKind.Push,
                "cover" or "pull" or "wipe" or "split" or "reveal" => TransitionKind.Slide,
                "zoom" or "warp" => TransitionKind.Zoom,
                "morph" => TransitionKind.Morph,
                _ => TransitionKind.Fade,
            };
            var dir = effect?.GetAttributes().FirstOrDefault(a => a.LocalName == "dir").Value;
            result.Direction = dir switch { "r" => TransitionDirection.Right, "u" => TransitionDirection.Up, "d" => TransitionDirection.Down, _ => TransitionDirection.Left };
            var speed = transition.GetAttributes().FirstOrDefault(a => a.LocalName == "spd").Value;
            result.Duration = speed switch { "fast" => 0.4, "slow" => 1.0, _ => Transition.DefaultDuration(result.Kind) };
            var dur = transition.GetAttributes().FirstOrDefault(a => a.LocalName == "dur").Value;
            if (dur != null && double.TryParse(dur, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) result.Duration = Math.Round(ms / 1000.0, 2);
            return result;
        }
    }
}
