using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>Draws whole slides: to a DrawingContext, a Drawing (vector export) or a bitmap (thumbnails, PNG / JPG).</summary>
public static class SlideRenderer
{
    /// <summary>Background, decorations and every element (optionally only those <paramref name="include"/> accepts).</summary>
    public static void Draw(DrawingContext dc, Slide slide, RenderContext ctx, Func<Element, bool>? include = null, bool background = true)
    {
        var deck = ctx.Deck;
        var style = ctx.StyleFor(slide);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, deck.Width, deck.Height)));
        if (background) DecorPainter.Paint(dc, slide, style, ctx, deck.Width, deck.Height);
        foreach (var element in slide.Elements)
        {
            if (include != null && !include(element)) continue;
            ElementRenderer.Draw(dc, element, style, ctx);
        }
        dc.Pop();
    }

    /// <summary>The slide as a vector drawing in slide units.</summary>
    public static DrawingGroup ToDrawing(Slide slide, RenderContext ctx)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open()) Draw(dc, slide, ctx);
        group.Freeze();
        return group;
    }

    /// <summary>Renders the slide to a bitmap <paramref name="pixelWidth"/> pixels wide.</summary>
    public static BitmapSource Render(Slide slide, RenderContext ctx, int pixelWidth)
    {
        var deck = ctx.Deck;
        var pixelHeight = Math.Max(1, (int)Math.Round(pixelWidth * deck.Height / deck.Width));
        var scale = pixelWidth / deck.Width;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        RenderOptions.SetClearTypeHint(visual, ClearTypeHint.Auto);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            Draw(dc, slide, ctx);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, pixelWidth), pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
