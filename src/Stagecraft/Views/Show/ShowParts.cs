using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Show;

/// <summary>Draws a prepared WPF drawing (a slide background or one element) in slide coordinates.</summary>
internal sealed class DrawingHost : FrameworkElement
{
    private readonly Drawing _drawing;

    public DrawingHost(Drawing drawing, double width, double height)
    {
        _drawing = drawing;
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public Drawing Drawing => _drawing;

    protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);
}

/// <summary>
/// One drawable part of an element in the slide show: the whole element, its body (fill, border) or one text piece.
/// Takes an <see cref="AnimState"/>: visibility, opacity, transform, reveal clip, and the colour-pulse and
/// bold-flash overlays (made only when an effect needs them).
/// </summary>
internal sealed class PartHost : Canvas
{
    private readonly DrawingHost _main;
    private readonly Func<Drawing>? _boldDrawing;
    private DrawingHost? _bold;
    private Rectangle? _tint;
    private string? _tintColor;
    private AnimState _last;
    private bool _applied;

    public PartHost(Drawing drawing, Size slide, PieceRef? piece, Func<Drawing>? boldDrawing)
    {
        Width = slide.Width;
        Height = slide.Height;
        IsHitTestVisible = false;
        Piece = piece;
        _boldDrawing = boldDrawing;
        _main = new DrawingHost(drawing, slide.Width, slide.Height);
        Children.Add(_main);
    }

    /// <summary>The text piece (null: the whole element; <see cref="PieceRef.IsBody"/>: fill and border).</summary>
    public PieceRef? Piece { get; }

    public Drawing Drawing => _main.Drawing;

    public bool IsShown => !_applied || _last.Visible;

    public void Apply(AnimState s, SlideStyle style, double cacheScale)
    {
        if (_applied && Same(_last, s)) return;
        _applied = true;
        _last = s;
        Visibility = s.Visible ? Visibility.Visible : Visibility.Hidden;
        if (!s.Visible) return;
        Opacity = MathEx.Clamp(s.Opacity, 0, 1);
        RenderTransform = AnimGeometry.Transform(s);
        Clip = AnimGeometry.Clip(s, 60);

        if (s.Tint > 0.001)
        {
            if (_tint == null)
            {
                _tint = new Rectangle { Width = Width, Height = Height, IsHitTestVisible = false, OpacityMask = new DrawingBrush(_main.Drawing) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, Width, Height), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, Width, Height) } };
                Children.Add(_tint);
            }
            if (_tintColor != s.TintColor)
            {
                _tintColor = s.TintColor;
                var brush = new SolidColorBrush(style.Resolve(s.TintColor ?? "accent2"));
                brush.Freeze();
                _tint.Fill = brush;
            }
            _tint.Visibility = Visibility.Visible;
            _tint.Opacity = Math.Min(1, s.Tint);
        }
        else if (_tint != null) _tint.Visibility = Visibility.Collapsed;

        if (s.Bold > 0.001 && _boldDrawing != null)
        {
            if (_bold == null)
            {
                _bold = new DrawingHost(_boldDrawing(), Width, Height);
                Children.Insert(1, _bold);
            }
            _bold.Visibility = Visibility.Visible;
            _bold.Opacity = Math.Min(1, s.Bold);
            _main.Opacity = 1 - Math.Min(1, s.Bold);
        }
        else
        {
            if (_bold != null) _bold.Visibility = Visibility.Collapsed;
            _main.Opacity = 1;
        }

        // Moving parts draw from a cached picture; at rest they're vectors again (sharp text).
        var moving = !s.IsIdentity;
        if (moving && CacheMode == null) CacheMode = new BitmapCache(Math.Max(1, cacheScale));
        else if (!moving && CacheMode != null) CacheMode = null;
    }

    private static bool Same(AnimState a, AnimState b) =>
        a.Visible == b.Visible && Math.Abs(a.Opacity - b.Opacity) < 1e-4 && Math.Abs(a.Dx - b.Dx) < 1e-3 && Math.Abs(a.Dy - b.Dy) < 1e-3 &&
        Math.Abs(a.ScaleX - b.ScaleX) < 1e-5 && Math.Abs(a.ScaleY - b.ScaleY) < 1e-5 && Math.Abs(a.Rotation - b.Rotation) < 1e-4 &&
        a.Clip == b.Clip && Math.Abs(a.ClipProgress - b.ClipProgress) < 1e-5 && Math.Abs(a.Tint - b.Tint) < 1e-4 && a.TintColor == b.TintColor &&
        Math.Abs(a.Bold - b.Bold) < 1e-4;
}

/// <summary>One element of a slide in the show: its parts (and, for videos and sounds, the player).</summary>
internal sealed class ElementLayer
{
    public ElementLayer(Element element)
    {
        Element = element;
    }

    public Element Element { get; }
    public List<PartHost> Parts { get; } = new();
    public MediaHost? Media { get; set; }

    /// <summary>Something of it can be seen (clicks only count on visible elements).</summary>
    public bool IsShown => Parts.Count == 0 ? Media?.IsShown ?? true : Parts.Any(p => p.IsShown);
}

/// <summary>
/// One slide in the show: its background and every element as separate layers, so animations can move, fade and
/// clip each element (or each word or letter of its text) independently on the GPU.
/// </summary>
internal sealed class SlideLayer : Canvas
{
    private readonly List<ElementLayer> _layers = new();
    private readonly SlideStyle _style;

    public SlideLayer(Slide slide, RenderContext ctx, Animator animator, MediaPlayback playback)
    {
        Slide = slide;
        Animator = animator;
        var deck = ctx.Deck;
        Width = deck.Width;
        Height = deck.Height;
        IsHitTestVisible = false;
        ClipToBounds = true;
        _style = ctx.StyleFor(slide);
        var size = new Size(deck.Width, deck.Height);

        var bg = new DrawingGroup();
        using (var dc = bg.Open()) DecorPainter.Paint(dc, slide, _style, ctx, deck.Width, deck.Height);
        bg.Freeze();
        Children.Add(new DrawingHost(bg, deck.Width, deck.Height));

        foreach (var e in slide.Elements)
        {
            var layer = new ElementLayer(e);
            if (e is MediaClipElement clip)
            {
                layer.Media = clip is VideoElement video ? new VideoHost(video, ctx, _style, playback) : new AudioHost((AudioElement)clip, ctx, _style, playback);
                Children.Add(layer.Media);
                _layers.Add(layer);
                continue;
            }
            var unit = animator.IsAnimated(e.Id) ? animator.PieceUnit(e.Id) : TextUnit.All;
            var body = TextUnits.BodyOf(e);
            if (unit != TextUnit.All && body != null && !body.Content.IsEmpty)
            {
                // Text animated in pieces: the body, then one part per paragraph, word or letter.
                layer.Parts.Add(Part(e, ctx, size, ElementPart.Body, PieceRef.Body));
                foreach (var (piece, mask) in TextLayout.Pieces(body, _style, unit))
                    layer.Parts.Add(Part(e, ctx, size, ElementPart.Text(mask), piece));
            }
            else
            {
                layer.Parts.Add(Part(e, ctx, size, null, null));
            }
            foreach (var p in layer.Parts) Children.Add(p);
            _layers.Add(layer);
        }
    }

    private PartHost Part(Element e, RenderContext ctx, Size size, ElementPart? part, PieceRef? piece)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open()) ElementRenderer.Draw(dc, e, _style, ctx, part: part);
        g.Freeze();
        Func<Drawing>? bold = null;
        if (TextUnits.BodyOf(e) != null && part?.BodyOnly != true)
        {
            bold = () =>
            {
                var b = new DrawingGroup();
                var boldPart = new ElementPart { Bold = true, Mask = part?.Mask, TextOnly = part?.TextOnly == true, NoShadow = true };
                using (var dc = b.Open()) ElementRenderer.Draw(dc, e, _style, ctx, part: boldPart);
                b.Freeze();
                return b;
            };
        }
        return new PartHost(g, size, piece, bold);
    }

    public Slide Slide { get; }
    public Animator Animator { get; }
    public SlideStyle SlideStyle => _style;
    public IReadOnlyList<ElementLayer> Layers => _layers;

    public ElementLayer? LayerOf(string elementId) => _layers.FirstOrDefault(l => l.Element.Id == elementId);

    /// <summary>Shows every element as the animations say at <paramref name="now"/>.</summary>
    public void Apply(Playhead playhead, double now, double cacheScale)
    {
        foreach (var layer in _layers)
        {
            var e = layer.Element;
            if (layer.Media != null)
            {
                layer.Media.ApplyAnimation(Animator.Evaluate(e, playhead, now), cacheScale);
                continue;
            }
            foreach (var part in layer.Parts) part.Apply(Animator.Evaluate(e, playhead, now, part.Piece), _style, cacheScale);
        }
    }

    /// <summary>Stops every player of the slide (leaving it).</summary>
    public void StopMedia(Func<MediaHost, bool>? keep = null)
    {
        foreach (var m in _layers.Select(l => l.Media).OfType<MediaHost>())
            if (keep == null || !keep(m)) m.Stop();
    }

    public void ReleaseMedia(Func<MediaHost, bool>? keep = null)
    {
        foreach (var m in _layers.Select(l => l.Media).OfType<MediaHost>())
            if (keep == null || !keep(m)) m.Release();
    }
}
