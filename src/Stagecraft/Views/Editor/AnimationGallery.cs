using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The "Add animation" gallery: every effect as a tile, grouped into entrance, emphasis, exit and motion paths.
/// Resting the pointer on a tile plays it on the selection (a live preview on the canvas); clicking adds it.
/// </summary>
public static class AnimationGallery
{
    public enum Purpose { Add, Change }

    /// <summary>Screenshots: receives the gallery instead of it opening as a popup (popups go on screen).</summary>
    internal static Action<FrameworkElement>? Intercept { get; set; }

    public static void Show(FrameworkElement target, EditorView editor, Purpose purpose)
    {
        Popup? popup = null;
        var hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        (EffectClass Class, EffectKind Kind)? pending = null;
        (EffectClass Class, EffectKind Kind)? playing = null;
        hoverTimer.Tick += (_, _) =>
        {
            hoverTimer.Stop();
            if (pending == null || pending == playing) return;
            playing = pending;
            editor.PreviewEffect(pending.Value.Class, pending.Value.Kind);
        };

        var root = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };
        var title = new TextBlock
        {
            Text = purpose == Purpose.Add ? "Add animation" : "Change effect",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(2, 0, 0, 2),
        };
        root.Children.Add(title);
        var hint = new TextBlock { Text = "Point at an effect to see it on the slide.", FontSize = 12, Margin = new Thickness(2, 0, 0, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        root.Children.Add(hint);

        void Section(string header, EffectClass cls, IEnumerable<EffectKind> kinds)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 6) };
            row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(PropertiesPanel.ClassColor(cls)), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            var h = new TextBlock { Text = header, FontSize = 12, FontWeight = FontWeights.SemiBold };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            row.Children.Add(h);
            root.Children.Add(row);
            var tiles = new WrapPanel { Margin = new Thickness(-3, 0, -3, 0) };
            foreach (var kind in kinds)
            {
                var k = kind;
                var name = EffectCatalog.Name(cls, k);
                var stack = new StackPanel { Width = 74 };
                stack.Children.Add(EffectGlyph.Create(cls, k, 28));
                stack.Children.Add(new TextBlock { Text = name, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 5, 2, 0), MaxHeight = 32 });
                var tile = new Button { Content = stack, Padding = new Thickness(2, 9, 2, 7), Margin = new Thickness(3), ToolTip = name };
                tile.SetResourceReference(FrameworkElement.StyleProperty, "CardButton");
                AutomationProperties.SetName(tile, header + ": " + name);
                tile.MouseEnter += (_, _) =>
                {
                    pending = (cls, k);
                    hoverTimer.Stop();
                    hoverTimer.Start();
                };
                tile.MouseLeave += (_, _) =>
                {
                    if (pending == (cls, k)) pending = null;
                };
                tile.Click += (_, _) =>
                {
                    hoverTimer.Stop();
                    if (popup != null) popup.IsOpen = false;
                    if (purpose == Purpose.Add) editor.AddAnimation(cls, k);
                    else editor.ChangeEffect(cls, k);
                };
                tiles.Children.Add(tile);
            }
            root.Children.Add(tiles);
        }

        Section("Entrance", EffectClass.Entrance, EffectCatalog.EntranceKinds);
        Section("Emphasis", EffectClass.Emphasis, EffectCatalog.EmphasisKinds);
        Section("Exit", EffectClass.Exit, EffectCatalog.EntranceKinds);
        Section("Motion paths", EffectClass.Motion, EffectCatalog.MotionKinds);

        var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 620 };
        var surface = new Border { Child = scroll, Margin = new Thickness(8, 4, 8, 16), Width = 360 + 24 + 16 };
        surface.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutSurface");
        if (Intercept is { } intercept)
        {
            intercept(surface);
            return;
        }
        popup = new Popup
        {
            Child = surface,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            HorizontalOffset = target.ActualWidth - (360 + 24 + 16 + 16),
        };
        popup.Closed += (_, _) =>
        {
            hoverTimer.Stop();
            editor.StopEffectPreview();
        };
        surface.MouseLeave += (_, _) =>
        {
            pending = null;
            playing = null;
            hoverTimer.Stop();
            editor.StopEffectPreview();
        };
        popup.IsOpen = true;
    }
}

/// <summary>
/// The small pictures of the effect gallery: a star (entrance, emphasis, exit) with a hint of what the effect does,
/// or the shape of a motion path, in the colour of its kind.
/// </summary>
public static class EffectGlyph
{
    public static FrameworkElement Create(EffectClass cls, EffectKind kind, double size)
    {
        var color = PropertiesPanel.ClassColor(cls);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var faint = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B));
        faint.Freeze();
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // 24 × 24 design space.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 24, 24));
            var pen = new Pen(brush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var thin = new Pen(faint, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (cls == EffectClass.Motion)
            {
                var path = kind switch
                {
                    EffectKind.Arc => Geometry.Parse("M 3,18 C 5,4 19,4 21,18"),
                    EffectKind.Circle => new EllipseGeometry(new Point(12, 12), 8, 8),
                    EffectKind.Custom => Geometry.Parse("M 3,16 C 6,4 9,20 13,10 S 19,5 21,9"),
                    _ => Geometry.Parse("M 3,12 L 21,12"),
                };
                dc.DrawGeometry(null, new Pen(brush, 1.8) { DashStyle = new DashStyle(new[] { 2.0, 1.6 }, 0), DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
                var start = kind switch { EffectKind.Circle => new Point(12, 4), EffectKind.Arc => new Point(3, 18), EffectKind.Custom => new Point(3, 16), _ => new Point(3, 12) };
                var end = kind switch { EffectKind.Circle => new Point(12, 4), EffectKind.Arc => new Point(21, 18), EffectKind.Custom => new Point(21, 9), _ => new Point(21, 12) };
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0x5A)), null, start, 2.4, 2.4);
                if (kind != EffectKind.Circle) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), null, end, 2.4, 2.4);
            }
            else
            {
                var exit = cls == EffectClass.Exit;
                switch (kind)
                {
                    case EffectKind.Fade:
                    case EffectKind.Transparency:
                        dc.PushOpacity(exit ? 0.35 : 0.55);
                        Star(dc, brush, 12, 12, 9);
                        dc.Pop();
                        Star(dc, null, 12, 12, 9, pen);
                        break;
                    case EffectKind.Fly:
                        Star(dc, brush, exit ? 10 : 14, 12, 7.5);
                        dc.DrawLine(thin, new Point(exit ? 18 : 2, 9), new Point(exit ? 23 : 6, 9));
                        dc.DrawLine(thin, new Point(exit ? 19 : 1, 13), new Point(exit ? 23 : 5, 13));
                        dc.DrawLine(thin, new Point(exit ? 18 : 2, 17), new Point(exit ? 22 : 5, 17));
                        break;
                    case EffectKind.Float:
                    case EffectKind.Rise:
                        Star(dc, brush, 12, kind == EffectKind.Rise ? 10 : 9, 7.5);
                        dc.DrawLine(thin, new Point(8, 19), new Point(8, 23));
                        dc.DrawLine(thin, new Point(12, 18), new Point(12, 23));
                        dc.DrawLine(thin, new Point(16, 19), new Point(16, 23));
                        break;
                    case EffectKind.Wipe:
                        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 13, 24)));
                        Star(dc, brush, 12, 12, 9);
                        dc.Pop();
                        Star(dc, null, 12, 12, 9, thin);
                        dc.DrawLine(pen, new Point(13, 2), new Point(13, 22));
                        break;
                    case EffectKind.Split:
                        dc.PushClip(new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 10, 24)), new RectangleGeometry(new Rect(14, 0, 10, 24)) } });
                        Star(dc, brush, 12, 12, 9);
                        dc.Pop();
                        dc.DrawLine(thin, new Point(12, 3), new Point(12, 21));
                        break;
                    case EffectKind.Zoom:
                        Star(dc, null, 12, 12, 10, thin);
                        Star(dc, brush, 12, 12, 5);
                        break;
                    case EffectKind.GrowTurn:
                        dc.PushTransform(new RotateTransform(-25, 12, 12));
                        Star(dc, brush, 12, 12, 7);
                        dc.Pop();
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 4,6 A 10,10 0 0 1 10,2"));
                        break;
                    case EffectKind.Bounce:
                        Star(dc, brush, 16, 15, 6);
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 2,3 Q 5,21 8,14 Q 10,9 12,16"));
                        break;
                    case EffectKind.Wheel:
                        Star(dc, null, 12, 12, 9, thin);
                        dc.PushClip(Geometry.Parse("M 12,12 L 12,0 A 12,12 0 0 1 24,12 A 12,12 0 0 1 12,24 Z"));
                        Star(dc, brush, 12, 12, 9);
                        dc.Pop();
                        break;
                    case EffectKind.Swivel:
                        dc.PushTransform(new ScaleTransform(0.45, 1, 12, 12));
                        Star(dc, brush, 12, 12, 9);
                        dc.Pop();
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 3,7 Q 12,3 21,7"));
                        break;
                    case EffectKind.Pulse:
                        Star(dc, brush, 12, 12, 7);
                        Star(dc, null, 12, 12, 10.5, thin);
                        break;
                    case EffectKind.Spin:
                        Star(dc, brush, 12, 12, 7);
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 21,12 A 9,9 0 1 1 18,5.5"));
                        dc.DrawGeometry(faint, null, Geometry.Parse("M 19.5,2.5 L 19.5,7 L 15,7 Z"));
                        break;
                    case EffectKind.Grow:
                        Star(dc, brush, 12, 12, 6.5);
                        dc.DrawLine(thin, new Point(3, 3), new Point(7, 7));
                        dc.DrawLine(thin, new Point(21, 21), new Point(17, 17));
                        dc.DrawLine(thin, new Point(21, 3), new Point(17, 7));
                        dc.DrawLine(thin, new Point(3, 21), new Point(7, 17));
                        break;
                    case EffectKind.Teeter:
                        dc.PushTransform(new RotateTransform(-14, 12, 14));
                        Star(dc, brush, 12, 12, 8);
                        dc.Pop();
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 4,21 Q 12,17 20,21"));
                        break;
                    case EffectKind.ColorPulse:
                        Star(dc, new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x9A)), 12, 12, 8);
                        Star(dc, null, 12, 12, 10.5, thin);
                        break;
                    case EffectKind.BoldFlash:
                    {
                        var ft = new FormattedText("B", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 18, brush, 1.0);
                        dc.DrawText(ft, new Point(12 - ft.Width / 2, 12 - ft.Height / 2));
                        break;
                    }
                    case EffectKind.Wave:
                        Star(dc, brush, 12, 9, 6.5);
                        dc.DrawGeometry(null, thin, Geometry.Parse("M 2,19 Q 5,15 8,19 T 14,19 T 20,19 T 23,18"));
                        break;
                    default: // Appear
                        if (exit)
                        {
                            Star(dc, null, 12, 12, 9, pen);
                        }
                        else Star(dc, brush, 12, 12, 9);
                        break;
                }
                if (exit && kind is not (EffectKind.Fade or EffectKind.Appear))
                {
                    // A small "out" mark tells exits from entrances.
                    dc.DrawEllipse(Brushes.White, new Pen(brush, 1.2), new Point(20, 20), 3.4, 3.4);
                    dc.DrawLine(new Pen(brush, 1.2), new Point(18.4, 20), new Point(21.6, 20));
                }
            }
        }
        group.Freeze();
        return new Image { Source = new DrawingImage(group), Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
    }

    private static void Star(DrawingContext dc, Brush? fill, double cx, double cy, double r, Pen? pen = null)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = fill != null };
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? r : r * 0.45;
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var p = new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius);
            if (i == 0) figure.StartPoint = p;
            else figure.Segments.Add(new LineSegment(p, true) { IsSmoothJoin = true });
        }
        var g = new PathGeometry(new[] { figure });
        dc.DrawGeometry(fill, pen, g);
    }
}
