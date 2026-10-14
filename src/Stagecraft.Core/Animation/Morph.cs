using System.Windows;
using System.Windows.Media;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Animation;

public enum MorphTrackKind { Morph, FadeOut, FadeIn }

/// <summary>What happens to one element during a Morph transition.</summary>
public sealed class MorphTrack
{
    public MorphTrack(MorphTrackKind kind, Element? from, Element? to)
    {
        Kind = kind;
        From = from;
        To = to;
    }

    public MorphTrackKind Kind { get; }
    public Element? From { get; }
    public Element? To { get; }

    /// <summary>The text is the same on both slides: it moves and resizes instead of cross-fading.</summary>
    public bool SameText { get; internal set; }
}

/// <summary>
/// Morph: decides which elements of two consecutive slides are "the same thing" (same identity after a slide
/// was duplicated, same name, same placeholder, or same content), so they can glide between their two states.
/// </summary>
public static class MorphPlanner
{
    public static string Identity(Element e) => e.MorphKey ?? e.Id;

    public static List<MorphTrack> Plan(Slide from, Slide to)
    {
        var a = from.Elements.ToList();
        var b = to.Elements.ToList();
        var pairs = new Dictionary<Element, Element>(); // to -> from
        var usedFrom = new HashSet<Element>();

        void Match(Func<Element, Element, bool> same)
        {
            foreach (var target in b)
            {
                if (pairs.ContainsKey(target)) continue;
                var source = a.FirstOrDefault(s => !usedFrom.Contains(s) && s.GetType() == target.GetType() && same(s, target));
                if (source == null) continue;
                pairs[target] = source;
                usedFrom.Add(source);
            }
        }

        // 1. The same object (a duplicated slide keeps each element's identity).
        Match((s, t) => Identity(s) == Identity(t));
        // 2. The same name.
        Match((s, t) => !string.IsNullOrWhiteSpace(s.Name) && string.Equals(s.Name!.Trim(), t.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
        // 3. The same content (same picture, same words, same icon).
        Match((s, t) => s.ContentKey == t.ContentKey && !s.ContentKey.EndsWith(":", StringComparison.Ordinal));
        // 4. The same layout slot (title to title, ...), but never decorations of different layouts.
        Match((s, t) => s.Placeholder != null && s.Placeholder != "decor" && s.Placeholder == t.Placeholder);

        var tracks = new List<MorphTrack>();
        foreach (var s in a.Where(x => !usedFrom.Contains(x))) tracks.Add(new MorphTrack(MorphTrackKind.FadeOut, s, null));
        foreach (var t in b)
        {
            if (pairs.TryGetValue(t, out var s))
            {
                var track = new MorphTrack(MorphTrackKind.Morph, s, t) { SameText = TextOf(s)?.Content.PlainText == TextOf(t)?.Content.PlainText };
                tracks.Add(track);
            }
            else tracks.Add(new MorphTrack(MorphTrackKind.FadeIn, null, t));
        }
        return tracks;
    }

    internal static TextBody? TextOf(Element? e) => e switch
    {
        TextElement t => t.Text,
        ShapeElement s => s.Text,
        _ => null,
    };
}

/// <summary>In-between states for Morph: numbers, angles and colours.</summary>
public static class MorphInterpolator
{
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Angles turn the short way round.</summary>
    public static double LerpAngle(double a, double b, double t)
    {
        var delta = ((b - a) % 360 + 540) % 360 - 180;
        return a + delta * t;
    }

    public static Color LerpColor(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(Lerp(a.A, b.A, t)), (byte)Math.Round(Lerp(a.R, b.R, t)),
        (byte)Math.Round(Lerp(a.G, b.G, t)), (byte)Math.Round(Lerp(a.B, b.B, t)));

    public static Rect LerpRect(Rect a, Rect b, double t) =>
        new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Math.Max(0, Lerp(a.Width, b.Width, t)), Math.Max(0, Lerp(a.Height, b.Height, t)));

    /// <summary>
    /// The element halfway (t) between <paramref name="a"/> and <paramref name="b"/>: a copy of b with position,
    /// size, rotation, opacity, corner radius and colours (resolved in each slide's style) in between.
    /// </summary>
    public static Element Interpolate(Element a, Element b, double t, SlideStyle styleA, SlideStyle styleB)
    {
        var e = DeckJson.Clone(b);
        e.Opacity = Lerp(a.Opacity, b.Opacity, t);

        if (a is ShapeElement { IsLine: true } la && b is ShapeElement { IsLine: true } lb)
        {
            var (a0, a1) = ElementRenderer.LineEnds(la, la.Bounds);
            var (b0, b1) = ElementRenderer.LineEnds(lb, lb.Bounds);
            DeckOperations.SetLine((ShapeElement)e, new Point(Lerp(a0.X, b0.X, t), Lerp(a0.Y, b0.Y, t)), new Point(Lerp(a1.X, b1.X, t), Lerp(a1.Y, b1.Y, t)));
        }
        else
        {
            e.Bounds = LerpRect(a.Bounds, b.Bounds, t);
            e.Rotation = LerpAngle(a.Rotation, b.Rotation, t);
        }

        switch (e)
        {
            case ShapeElement s when a is ShapeElement sa && b is ShapeElement sb:
                s.Fill = LerpFill(sa.Fill, sb.Fill, t, styleA, styleB) ?? Fill.None;
                s.CornerRadius = Lerp(Radius(sa), Radius(sb), t);
                s.Stroke = LerpStroke(sa.Stroke ?? (sa.IsLine ? ElementRenderer.LineStroke(sa) : null), sb.Stroke ?? (sb.IsLine ? ElementRenderer.LineStroke(sb) : null), t, styleA, styleB);
                break;
            case TextElement x when a is TextElement ta && b is TextElement tb:
                x.Fill = LerpFill(ta.Fill, tb.Fill, t, styleA, styleB);
                x.Stroke = LerpStroke(ta.Stroke, tb.Stroke, t, styleA, styleB);
                x.CornerRadius = Lerp(ta.CornerRadius, tb.CornerRadius, t);
                break;
            case ImageElement i when a is ImageElement ia && b is ImageElement ib:
                i.CornerRadius = Lerp(ia.CornerRadius, ib.CornerRadius, t);
                i.FocusX = Lerp(ia.FocusX, ib.FocusX, t);
                i.FocusY = Lerp(ia.FocusY, ib.FocusY, t);
                break;
            case IconElement c when a is IconElement ca && b is IconElement cb:
                c.Color = SlideStyle.ToHex(LerpColor(styleA.Resolve(ca.Color), styleB.Resolve(cb.Color), t));
                break;
        }
        return e;
    }

    private static double Radius(ShapeElement s) => s.Shape switch
    {
        ShapeKind.RoundedRectangle => s.CornerRadius > 0 ? s.CornerRadius : Math.Min(s.Width, s.Height) * 0.2,
        ShapeKind.Ellipse => Math.Min(s.Width, s.Height) / 2,
        _ => s.CornerRadius,
    };

    private static Fill? LerpFill(Fill? a, Fill? b, double t, SlideStyle sa, SlideStyle sb)
    {
        if (a == null && b == null) return null;
        var fa = a ?? new Fill { Kind = FillKind.Solid, Color = "transparent" };
        var fb = b ?? new Fill { Kind = FillKind.Solid, Color = "transparent" };
        if (fa.Kind == FillKind.None) fa = new Fill { Kind = FillKind.Solid, Color = SlideStyle.ToHex(Transparent(sb.Resolve(fb.Color))) };
        if (fb.Kind == FillKind.None) fb = new Fill { Kind = FillKind.Solid, Color = SlideStyle.ToHex(Transparent(sa.Resolve(fa.Color))) };
        if (fa.Kind is FillKind.Image || fb.Kind is FillKind.Image) return t < 0.5 ? a : b;

        var a1 = sa.Resolve(fa.Color);
        var a2 = sa.Resolve(fa.Color2 ?? fa.Color);
        var b1 = sb.Resolve(fb.Color);
        var b2 = sb.Resolve(fb.Color2 ?? fb.Color);
        var kind = fa.Kind == fb.Kind ? fb.Kind : (fb.Kind == FillKind.Solid ? fa.Kind : fb.Kind);
        return new Fill
        {
            Kind = kind == FillKind.None ? FillKind.Solid : kind,
            Color = SlideStyle.ToHex(LerpColor(a1, b1, t)),
            Color2 = SlideStyle.ToHex(LerpColor(a2, b2, t)),
            Angle = LerpAngle(fa.Angle, fb.Angle, t),
        };
    }

    private static Color Transparent(Color c) => Color.FromArgb(0, c.R, c.G, c.B);

    private static Stroke? LerpStroke(Stroke? a, Stroke? b, double t, SlideStyle sa, SlideStyle sb)
    {
        if (a == null && b == null) return null;
        var ca = a != null ? sa.Resolve(a.Color) : Transparent(sb.Resolve(b!.Color));
        var cb = b != null ? sb.Resolve(b.Color) : Transparent(ca);
        return new Stroke
        {
            Color = SlideStyle.ToHex(LerpColor(ca, cb, t)),
            Width = Lerp(a?.Width ?? b!.Width, b?.Width ?? a!.Width, t),
            Dash = (t < 0.5 ? a : b)?.Dash ?? DashStyleKind.Solid,
        };
    }
}

/// <summary>Draws one frame of a Morph transition (vector, so it's sharp at any size).</summary>
public sealed class MorphFrame
{
    private readonly Slide _from;
    private readonly Slide _to;
    private readonly RenderContext _ctxFrom;
    private readonly RenderContext _ctxTo;
    private readonly SlideStyle _styleFrom;
    private readonly SlideStyle _styleTo;
    private readonly List<MorphTrack> _tracks;
    private readonly Dictionary<Element, double> _fitScale = new();

    /// <summary>
    /// <paramref name="showFrom"/> / <paramref name="showTo"/> say which elements are on screen at the end of the first
    /// slide's animations and at the start of the second's (elements waiting for an entrance don't morph in early).
    /// </summary>
    public MorphFrame(Slide from, Slide to, RenderContext ctxFrom, RenderContext ctxTo, Func<Element, bool>? showFrom = null, Func<Element, bool>? showTo = null)
    {
        _from = Visible(from, showFrom);
        _to = Visible(to, showTo);
        _ctxFrom = ctxFrom;
        _ctxTo = ctxTo;
        _styleFrom = ctxFrom.StyleFor(from);
        _styleTo = ctxTo.StyleFor(to);
        _tracks = MorphPlanner.Plan(_from, _to);
        // Auto-fit scales of both ends, measured once (re-measuring every frame would be slow).
        foreach (var track in _tracks)
        {
            if (track.From != null) Fit(track.From, _styleFrom);
            if (track.To != null) Fit(track.To, _styleTo);
        }
    }

    public IReadOnlyList<MorphTrack> Tracks => _tracks;

    private static Slide Visible(Slide slide, Func<Element, bool>? show)
    {
        if (show == null) return slide;
        return new Slide
        {
            Id = slide.Id,
            Layout = slide.Layout,
            Background = slide.Background,
            ShowDecorations = slide.ShowDecorations,
            Hidden = slide.Hidden,
            Elements = slide.Elements.Where(show).ToList(),
        };
    }

    private void Fit(Element e, SlideStyle style)
    {
        var body = MorphPlanner.TextOf(e);
        if (body == null) return;
        var box = e is ShapeElement s ? ElementRenderer.TextBoxOf(s, e.Bounds) : e.Bounds;
        var inner = TextLayout.Inner(body, box);
        _fitScale[e] = TextLayout.FitScale(body, inner.Width, inner.Height, style);
    }

    /// <summary>Draws the frame at time <paramref name="t"/> (0..1, already eased).</summary>
    public void Draw(DrawingContext dc, double t)
    {
        var deck = _ctxTo.Deck;
        var size = new Rect(0, 0, deck.Width, deck.Height);
        dc.PushClip(new RectangleGeometry(size));
        DecorPainter.Paint(dc, _from, _styleFrom, _ctxFrom, deck.Width, deck.Height);
        if (t > 0)
        {
            dc.PushOpacity(t);
            DecorPainter.Paint(dc, _to, _styleTo, _ctxTo, deck.Width, deck.Height);
            dc.Pop();
        }

        foreach (var track in _tracks)
        {
            switch (track.Kind)
            {
                case MorphTrackKind.FadeOut:
                {
                    var o = 1 - Easing.Clamp01(t * 1.7);
                    if (o <= 0) break;
                    dc.PushOpacity(o);
                    ElementRenderer.Draw(dc, track.From!, _styleFrom, _ctxFrom);
                    dc.Pop();
                    break;
                }
                case MorphTrackKind.FadeIn:
                {
                    var o = Easing.Clamp01((t - 0.4) / 0.6);
                    if (o <= 0) break;
                    dc.PushOpacity(o);
                    ElementRenderer.Draw(dc, track.To!, _styleTo, _ctxTo);
                    dc.Pop();
                    break;
                }
                default:
                    DrawMorph(dc, track, t);
                    break;
            }
        }
        dc.Pop();
    }

    private void DrawMorph(DrawingContext dc, MorphTrack track, double t)
    {
        var a = track.From!;
        var b = track.To!;
        var mid = MorphInterpolator.Interpolate(a, b, t, _styleFrom, _styleTo);
        var bodyA = MorphPlanner.TextOf(a);
        var bodyB = MorphPlanner.TextOf(b);
        if (bodyA == null || bodyB == null || (a is ShapeElement && b is ShapeElement && ((ShapeElement)b).Text == null))
        {
            if (a is ImageElement ia && b is ImageElement ib && ia.Media != ib.Media)
            {
                // Different pictures: cross-fade inside the moving frame.
                var from = DeckJson.Clone(ia);
                from.Bounds = mid.Bounds;
                from.Rotation = mid.Rotation;
                dc.PushOpacity(1 - t);
                ElementRenderer.Draw(dc, from, _styleFrom, _ctxFrom);
                dc.Pop();
                dc.PushOpacity(t);
                ElementRenderer.Draw(dc, mid, _styleTo, _ctxTo);
                dc.Pop();
                return;
            }
            if (a is ChartElement or TableElement || (a is IconElement ca && b is IconElement cb && ca.Icon != cb.Icon))
            {
                var from = DeckJson.Clone(a);
                from.Bounds = mid.Bounds;
                from.Rotation = mid.Rotation;
                dc.PushOpacity(1 - t);
                ElementRenderer.Draw(dc, from, _styleFrom, _ctxFrom);
                dc.Pop();
                dc.PushOpacity(t);
                ElementRenderer.Draw(dc, mid, _styleTo, _ctxTo);
                dc.Pop();
                return;
            }
            ElementRenderer.Draw(dc, mid, t < 0.5 ? _styleFrom : _styleTo, t < 0.5 ? _ctxFrom : _ctxTo);
            return;
        }

        // Text: draw the frame (fill / border) morphing, then the text.
        var box = mid.Bounds;
        var pushes = 0;
        if (Math.Abs(mid.Rotation) > 0.001)
        {
            dc.PushTransform(new RotateTransform(mid.Rotation, box.X + box.Width / 2, box.Y + box.Height / 2));
            pushes++;
        }
        if (mid.Opacity < 0.999)
        {
            dc.PushOpacity(mid.Opacity);
            pushes++;
        }
        var frameOnly = DeckJson.Clone(mid);
        frameOnly.Rotation = 0;
        frameOnly.Opacity = 1;
        if (frameOnly is TextElement te) te.Text = new TextBody();
        else if (frameOnly is ShapeElement se) se.Text = null;
        ElementRenderer.Draw(dc, frameOnly, _styleTo, _ctxTo);

        var textBoxMid = mid is ShapeElement sm ? ElementRenderer.TextBoxOf(sm, box) : box;
        var scaleA = _fitScale.TryGetValue(a, out var fa) ? fa : 1;
        var scaleB = _fitScale.TryGetValue(b, out var fb) ? fb : 1;
        if (track.SameText)
        {
            // Same words: one text that changes size, place and colour.
            var sizeA = TextLayout.BaseSize(bodyA, _styleFrom) * scaleA;
            var sizeB = TextLayout.BaseSize(bodyB, _styleTo) * scaleB;
            var scale = MorphInterpolator.Lerp(sizeA, sizeB, t) / Math.Max(0.01, TextLayout.BaseSize(bodyB, _styleTo));
            var colored = DeckJson.Clone(bodyB);
            var ca = _styleFrom.Resolve(bodyA.Color ?? _styleFrom.RoleFont(bodyA.Role).Color);
            var cb = _styleTo.Resolve(bodyB.Color ?? _styleTo.RoleFont(bodyB.Role).Color);
            colored.Color = SlideStyle.ToHex(MorphInterpolator.LerpColor(ca, cb, t));
            var inner = TextLayout.Inner(colored, textBoxMid);
            var layout = TextLayout.Layout(colored, inner.Width, _styleTo, scale);
            TextLayout.Draw(dc, layout, inner, colored.VAlign);
        }
        else
        {
            var inner = TextLayout.Inner(bodyA, textBoxMid);
            if (t < 1)
            {
                dc.PushOpacity(1 - Easing.Clamp01(t * 1.6));
                TextLayout.Draw(dc, TextLayout.Layout(bodyA, inner.Width, _styleFrom, scaleA), inner, bodyA.VAlign);
                dc.Pop();
            }
            if (t > 0.3)
            {
                var innerB = TextLayout.Inner(bodyB, textBoxMid);
                dc.PushOpacity(Easing.Clamp01((t - 0.3) / 0.7));
                TextLayout.Draw(dc, TextLayout.Layout(bodyB, innerB.Width, _styleTo, scaleB), innerB, bodyB.VAlign);
                dc.Pop();
            }
        }
        for (var i = 0; i < pushes; i++) dc.Pop();
    }
}
