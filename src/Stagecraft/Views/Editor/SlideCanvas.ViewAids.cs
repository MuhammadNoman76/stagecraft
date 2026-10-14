using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stagecraft.Core.Model;
using Stagecraft.Services;

namespace Stagecraft.Views.Editor;

/// <summary>
/// View aids: rulers along the top and left, gridlines, and the presentation's own guides (drag one out of a ruler,
/// drag it to move it, drag it off the slide to remove it). Snapping to them lives in <see cref="Core.Editing.Snapper"/>.
/// </summary>
public sealed partial class SlideCanvas
{
    public const double RulerSize = 20;

    private Ruler? _topRuler;
    private Ruler? _leftRuler;

    // A guide being dragged: its index in Deck.Guides (-1 = a new one from a ruler) and where it is now.
    private int _guideIndex = -2;
    private GuideLine? _guidePreview;

    /// <summary>The guide being dragged (drawn by the overlay instead of the stored one).</summary>
    internal (int Index, GuideLine Line)? DraggedGuide => _guidePreview != null && _guideIndex >= -1 ? (_guideIndex, _guidePreview) : null;

    /// <summary>Shows or hides rulers and gridlines as Settings say.</summary>
    public void ApplyViewAids()
    {
        var show = AppSettings.Current.ShowRulers;
        if (show && _topRuler == null)
        {
            _topRuler = new Ruler(this, horizontal: true) { Height = RulerSize, VerticalAlignment = VerticalAlignment.Top };
            _leftRuler = new Ruler(this, horizontal: false) { Width = RulerSize, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, RulerSize, 0, 0) };
            Children.Add(_topRuler);
            Children.Add(_leftRuler);
        }
        if (_topRuler != null && _leftRuler != null)
        {
            _topRuler.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _leftRuler.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _topRuler.InvalidateVisual();
            _leftRuler.InvalidateVisual();
        }
        _overlay.InvalidateVisual();
    }

    private void InvalidateRulers()
    {
        _topRuler?.InvalidateVisual();
        _leftRuler?.InvalidateVisual();
    }

    /// <summary>The stored guide under a view point (within a few pixels), or -1.</summary>
    private int GuideAt(Point view)
    {
        var deck = _state?.Deck;
        if (deck == null) return -1;
        for (var i = deck.Guides.Count - 1; i >= 0; i--)
        {
            var g = deck.Guides[i];
            var at = ToView(g.Vertical ? new Point(g.Position, 0) : new Point(0, g.Position));
            var top = ToView(new Point(0, 0));
            var bottom = ToView(new Point(deck.Width, deck.Height));
            if (g.Vertical && Math.Abs(view.X - at.X) <= 4 && view.Y >= top.Y && view.Y <= bottom.Y) return i;
            if (!g.Vertical && Math.Abs(view.Y - at.Y) <= 4 && view.X >= top.X && view.X <= bottom.X) return i;
        }
        return -1;
    }

    /// <summary>Mouse down on a guide: starts moving it. Returns true when it took the click.</summary>
    private bool GuideMouseDown(Point view)
    {
        if (_state?.Deck is not { } deck || IsEditingText || IsCropping) return false;
        var index = GuideAt(view);
        if (index < 0) return false;
        StartGuideDrag(index, deck.Guides[index].Clone());
        return true;
    }

    /// <summary>A new guide pulled out of a ruler (the top ruler makes a horizontal guide, the left one a vertical guide).</summary>
    internal void BeginNewGuide(bool vertical, Point view)
    {
        if (_state?.CurrentSlide == null) return;
        if (IsEditingText) CommitTextEdit();
        var p = ToSlide(view);
        StartGuideDrag(-1, new GuideLine { Vertical = vertical, Position = vertical ? p.X : p.Y });
    }

    private void StartGuideDrag(int index, GuideLine line)
    {
        _guideIndex = index;
        _guidePreview = line;
        _mode = DragMode.Guide;
        _viewport.CaptureMouse();
        Cursor = line.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
        _overlay.InvalidateVisual();
        InvalidateRulers();
    }

    private void DoGuideDrag(Point slidePoint)
    {
        if (_guidePreview == null || _state == null) return;
        var deck = _state.Deck;
        var size = _guidePreview.Vertical ? deck.Width : deck.Height;
        var value = _guidePreview.Vertical ? slidePoint.X : slidePoint.Y;
        // Snap to the slide's centre and to the grid when it's on (Alt skips).
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            var threshold = 6 / Zoom;
            if (Math.Abs(value - size / 2) < threshold) value = size / 2;
            else if (AppSettings.Current.SnapToGrid) value += Core.Editing.Snapper.GridDelta(value, AppSettings.Current.GridSize);
        }
        _guidePreview.Position = Math.Round(value, 1);
        _overlay.InvalidateVisual();
        InvalidateRulers();
    }

    private void EndGuideDrag(bool cancel)
    {
        var index = _guideIndex;
        var line = _guidePreview;
        _guideIndex = -2;
        _guidePreview = null;
        Cursor = null;
        _overlay.InvalidateVisual();
        InvalidateRulers();
        if (cancel || line == null || _state == null) return;
        var deck = _state.Deck;
        var size = line.Vertical ? deck.Width : deck.Height;
        var off = line.Position < 0 || line.Position > size;
        var session = _state.Session;
        if (index < 0)
        {
            if (!off) session.EditStructure("Add guide", d => d.Guides.Add(line), redrawSlides: false);
        }
        else if (index < deck.Guides.Count)
        {
            if (off) session.EditStructure("Remove guide", d => d.Guides.RemoveAt(index), redrawSlides: false);
            else if (Math.Abs(deck.Guides[index].Position - line.Position) > 0.01) session.EditStructure("Move guide", d => d.Guides[index].Position = line.Position, redrawSlides: false);
        }
        _overlay.InvalidateVisual();
    }

    /// <summary>A guide through the middle of the slide (View › Add guide).</summary>
    public void AddGuide(bool vertical)
    {
        if (_state == null) return;
        var deck = _state.Deck;
        var at = vertical ? deck.Width / 2 : deck.Height / 2;
        // Not on top of an existing one.
        while (deck.Guides.Any(g => g.Vertical == vertical && Math.Abs(g.Position - at) < 1)) at += vertical ? deck.Width / 8 : deck.Height / 8;
        var size = vertical ? deck.Width : deck.Height;
        if (at > size) at = size / 2;
        _state.Session.EditStructure("Add guide", d => d.Guides.Add(new GuideLine { Vertical = vertical, Position = Math.Round(at, 1) }), redrawSlides: false);
        _overlay.InvalidateVisual();
    }

    /// <summary>A length in slide units in the user's units (centimetres where metric is used, else inches).</summary>
    internal static string DescribeLength(double units)
    {
        var inches = units / 144.0;
        return Metric ? (inches * 2.54).ToString("0.00", CultureInfo.CurrentCulture) + " cm" : inches.ToString("0.00", CultureInfo.CurrentCulture) + " in";
    }

    internal static bool Metric
    {
        get
        {
            try
            {
                return RegionInfo.CurrentRegion.IsMetric;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }
}

/// <summary>A ruler along the top or left edge of the slide editor (centimetres or inches, zero at the slide's corner).</summary>
internal sealed class Ruler : FrameworkElement
{
    private readonly SlideCanvas _canvas;
    private readonly bool _horizontal;

    public Ruler(SlideCanvas canvas, bool horizontal)
    {
        _canvas = canvas;
        _horizontal = horizontal;
        Cursor = horizontal ? Cursors.SizeNS : Cursors.SizeWE;
        ToolTip = horizontal ? "Drag down to add a horizontal guide" : "Drag right to add a vertical guide";
        System.Windows.Automation.AutomationProperties.SetName(this, horizontal ? "Horizontal ruler" : "Vertical ruler");
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _canvas.BeginNewGuide(vertical: !_horizontal, e.GetPosition(_canvas));
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var deck = _canvas.State?.Deck;
        var w = ActualWidth;
        var h = ActualHeight;
        var background = TryFindResource("Panel.Background") as Brush ?? Brushes.White;
        var text = TryFindResource("Text.Secondary") as Brush ?? Brushes.Gray;
        var line = TryFindResource("Divider") as Brush ?? Brushes.LightGray;
        var accent = TryFindResource("Accent.Selection") as Brush ?? Brushes.LightBlue;
        dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
        if (deck == null) return;

        // the slide's extent and the selection's, along this ruler
        var origin = _canvas.ToView(new Point(0, 0));
        var end = _canvas.ToView(new Point(deck.Width, deck.Height));
        var state = _canvas.State!;
        if (state.SelectedElements.Count > 0)
        {
            var b = Core.Editing.DeckOperations.BoundsOf(state.SelectedElements);
            var a = _canvas.ToView(b.TopLeft);
            var z = _canvas.ToView(b.BottomRight);
            if (_horizontal) dc.DrawRectangle(accent, null, new Rect(new Point(a.X, 0), new Point(z.X, h)));
            else dc.DrawRectangle(accent, null, new Rect(new Point(0, a.Y), new Point(w, z.Y)));
        }

        var unit = SlideCanvas.Metric ? 144 / 2.54 : 144.0; // slide units per centimetre or inch
        var pxPerUnit = unit * _canvas.Zoom;
        // label every 1, 2, 5 or 10 units so labels don't crowd
        var labelEvery = pxPerUnit >= 40 ? 1 : pxPerUnit >= 20 ? 2 : pxPerUnit >= 8 ? 5 : 10;
        var subdivisions = SlideCanvas.Metric ? 10 : 8;
        while (subdivisions > 1 && pxPerUnit / subdivisions < 5) subdivisions /= 2;
        var pen = new Pen(line, 1);
        pen.Freeze();
        var majorPen = new Pen(text, 1);
        majorPen.Freeze();
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var length = _horizontal ? deck.Width : deck.Height;
        var count = (int)Math.Ceiling(length / unit);
        for (var i = 0; i <= count; i++)
        {
            for (var s = 0; s < subdivisions; s++)
            {
                var value = (i + s / (double)subdivisions) * unit;
                if (value > length + 0.01) break;
                var pos = _horizontal ? _canvas.ToView(new Point(value, 0)).X : _canvas.ToView(new Point(0, value)).Y;
                if (_horizontal ? pos < 0 || pos > w : pos < 0 || pos > h) continue;
                var major = s == 0;
                var half = !major && subdivisions % 2 == 0 && s == subdivisions / 2;
                var tick = major ? (_horizontal ? h : w) * 0.55 : half ? (_horizontal ? h : w) * 0.35 : (_horizontal ? h : w) * 0.2;
                pos = Math.Round(pos) + 0.5;
                if (_horizontal) dc.DrawLine(major ? majorPen : pen, new Point(pos, h - tick), new Point(pos, h));
                else dc.DrawLine(major ? majorPen : pen, new Point(w - tick, pos), new Point(w, pos));
                if (major && i % labelEvery == 0)
                {
                    var ft = new FormattedText(i.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.5, text, dpi);
                    if (_horizontal) dc.DrawText(ft, new Point(pos + 3, 1));
                    else
                    {
                        // reads upwards, just after the tick
                        dc.PushTransform(new TranslateTransform(1, pos + 3 + ft.Width));
                        dc.PushTransform(new RotateTransform(-90));
                        dc.DrawText(ft, new Point(0, 0));
                        dc.Pop();
                        dc.Pop();
                    }
                }
            }
        }
        // the slide's edges and the guide being dragged
        var edgePen = new Pen(text, 1);
        if (_horizontal)
        {
            dc.DrawLine(edgePen, new Point(Math.Round(origin.X) + 0.5, 0), new Point(Math.Round(origin.X) + 0.5, h));
            dc.DrawLine(edgePen, new Point(Math.Round(end.X) + 0.5, 0), new Point(Math.Round(end.X) + 0.5, h));
        }
        else
        {
            dc.DrawLine(edgePen, new Point(0, Math.Round(origin.Y) + 0.5), new Point(w, Math.Round(origin.Y) + 0.5));
            dc.DrawLine(edgePen, new Point(0, Math.Round(end.Y) + 0.5), new Point(w, Math.Round(end.Y) + 0.5));
        }
        if (_canvas.DraggedGuide is { } g && g.Line.Vertical == _horizontal)
        {
            var p = _canvas.ToView(g.Line.Vertical ? new Point(g.Line.Position, 0) : new Point(0, g.Line.Position));
            var guidePen = new Pen(SelectionOverlay.UserGuideBrush, 2);
            if (_horizontal) dc.DrawLine(guidePen, new Point(p.X, 0), new Point(p.X, h));
            else dc.DrawLine(guidePen, new Point(0, p.Y), new Point(w, p.Y));
        }
        // a hairline between the ruler and the slide area
        if (_horizontal) dc.DrawLine(pen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        else dc.DrawLine(pen, new Point(w - 0.5, 0), new Point(w - 0.5, h));
    }
}
