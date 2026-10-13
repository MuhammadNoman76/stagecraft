using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>One element on the editor canvas, drawn by the shared renderer in its own (unrotated) box.</summary>
public sealed class ElementVisual : FrameworkElement
{
    private Element _element;
    private SlideStyle _style;
    private RenderContext _ctx;

    public ElementVisual(Element element, SlideStyle style, RenderContext ctx)
    {
        _element = element;
        _style = style;
        _ctx = ctx;
        IsHitTestVisible = false;
        Focusable = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Place();
    }

    public Element Element => _element;

    /// <summary>The text is being edited in place: draw everything but the text.</summary>
    public bool HideText { get; set; }

    public void Update(Element element, SlideStyle style, RenderContext ctx)
    {
        _element = element;
        _style = style;
        _ctx = ctx;
        Place();
        InvalidateVisual();
    }

    /// <summary>Position, size and rotation from the model (cheap; used while dragging).</summary>
    public void Place()
    {
        Canvas.SetLeft(this, _element.X);
        Canvas.SetTop(this, _element.Y);
        Width = Math.Max(0, _element.Width);
        Height = Math.Max(0, _element.Height);
        RenderTransform = Math.Abs(_element.Rotation % 360) > 0.001 ? new RotateTransform(_element.Rotation) : Transform.Identity;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var element = _element;
        if (HideText)
        {
            element = DeckJson.Clone(_element);
            switch (element)
            {
                case TextElement t:
                    t.Text = new TextBody();
                    t.Placeholder = null;
                    break;
                case ShapeElement s:
                    s.Text = null;
                    break;
            }
        }
        try
        {
            ElementRenderer.Draw(dc, element, _style, _ctx, local: true);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Draw element");
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ElementVisualPeer(this);

    private sealed class ElementVisualPeer : FrameworkElementAutomationPeer
    {
        public ElementVisualPeer(ElementVisual owner) : base(owner) { }

        protected override string GetNameCore() => ((ElementVisual)Owner).Element.Describe;

        protected override AutomationControlType GetAutomationControlTypeCore() => ((ElementVisual)Owner).Element switch
        {
            TextElement => AutomationControlType.Text,
            ImageElement or IconElement => AutomationControlType.Image,
            TableElement => AutomationControlType.Table,
            _ => AutomationControlType.Custom,
        };

        protected override string GetClassNameCore() => "SlideElement";

        protected override bool IsContentElementCore() => true;
    }
}

/// <summary>The slide's background and theme decorations.</summary>
public sealed class SlideBackgroundVisual : FrameworkElement
{
    private Slide? _slide;
    private RenderContext? _ctx;

    public SlideBackgroundVisual()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public void Update(Slide slide, RenderContext ctx)
    {
        _slide = slide;
        _ctx = ctx;
        Width = ctx.Deck.Width;
        Height = ctx.Deck.Height;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_slide == null || _ctx == null) return;
        try
        {
            DecorPainter.Paint(dc, _slide, _ctx.StyleFor(_slide), _ctx, _ctx.Deck.Width, _ctx.Deck.Height);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Draw background");
        }
    }
}
