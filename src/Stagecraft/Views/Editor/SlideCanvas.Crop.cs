using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Crop mode for pictures: the whole picture shows faintly around the kept part; drag the black handles to cut the
/// edges, drag the picture to move it under the frame. Enter or a click outside keeps the crop, Esc cancels.
/// </summary>
public sealed partial class SlideCanvas
{
    [Flags]
    private enum CropEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Pan = 16 }

    private ImageElement? _crop;
    private Slide? _cropBefore;
    private Rect _cropFull;
    private CropEdge _cropDrag;
    private Rect _cropStartBox;
    private Rect _cropStartFull;
    private Point _cropDown;
    private bool _cropTouched;

    /// <summary>The picture being cropped (null when not cropping).</summary>
    public ImageElement? CroppingImage => _crop;

    public bool IsCropping => _crop != null;

    /// <summary>Starts crop mode on a picture. Returns false (with the reason) when it can't be cropped.</summary>
    public bool BeginCrop(ImageElement image, out string? reason)
    {
        reason = null;
        if (_state?.CurrentSlide is not { } slide || slide.Find(image.Id) is not ImageElement img) return false;
        var bitmap = _state.Session.Media.GetImage(img.Media);
        if (bitmap == null)
        {
            reason = "Choose a picture first.";
            return false;
        }
        if (Math.Abs(img.Rotation % 360) > 0.01)
        {
            reason = "Turn the picture straight (rotation 0°) to crop it, then turn it back.";
            return false;
        }
        if (IsEditingText) CommitTextEdit();
        EndCrop(commit: true);
        _cropBefore = _state.Session.Snapshot(slide);
        // Crop mode works with explicit crop fractions: start from exactly what the fit shows now.
        img.Crop = ElementRenderer.CropFromFit(img, bitmap.PixelWidth, bitmap.PixelHeight);
        _crop = img;
        _cropFull = ElementRenderer.ImageDestination(img, img.Bounds, bitmap.PixelWidth, bitmap.PixelHeight);
        _state.Select(new[] { img.Id }, expandGroups: false);
        PlaceVisuals(new[] { img }, redraw: true);
        _overlay.InvalidateVisual();
        Focus();
        return true;
    }

    /// <summary>Leaves crop mode, recording the crop (or putting it back).</summary>
    public void EndCrop(bool commit)
    {
        if (_crop == null) return;
        var slide = _state?.CurrentSlide;
        var before = _cropBefore;
        var img = _crop;
        _crop = null;
        _cropBefore = null;
        _cropDrag = CropEdge.None;
        if (slide != null && before != null)
        {
            // Untouched: the original fit comes back (no explicit crop is added).
            if (commit && _cropTouched) _state!.Session.Commit(slide, before, "Crop");
            else _state!.Session.Revert(slide, before);
        }
        _cropTouched = false;
        _overlay.InvalidateVisual();
        CropEnded?.Invoke();
    }

    /// <summary>Crop mode ended (the panel updates).</summary>
    public event Action? CropEnded;

    private void PlaceCropTools() => _overlay.InvalidateVisual();

    /// <summary>The handle points of the kept frame (viewport coordinates).</summary>
    internal IEnumerable<(CropEdgeKind Kind, Point View)> CropHandles()
    {
        if (_crop == null) yield break;
        var b = _crop.Bounds;
        yield return (CropEdgeKind.TopLeft, ToView(b.TopLeft));
        yield return (CropEdgeKind.TopRight, ToView(b.TopRight));
        yield return (CropEdgeKind.BottomRight, ToView(b.BottomRight));
        yield return (CropEdgeKind.BottomLeft, ToView(b.BottomLeft));
        yield return (CropEdgeKind.Top, ToView(new Point(b.X + b.Width / 2, b.Top)));
        yield return (CropEdgeKind.Right, ToView(new Point(b.Right, b.Y + b.Height / 2)));
        yield return (CropEdgeKind.Bottom, ToView(new Point(b.X + b.Width / 2, b.Bottom)));
        yield return (CropEdgeKind.Left, ToView(new Point(b.Left, b.Y + b.Height / 2)));
    }

    /// <summary>The whole picture's place while cropping (slide coordinates).</summary>
    internal Rect CropFull => _cropFull;

    private static CropEdge EdgesOf(CropEdgeKind k) => k switch
    {
        CropEdgeKind.TopLeft => CropEdge.Top | CropEdge.Left,
        CropEdgeKind.TopRight => CropEdge.Top | CropEdge.Right,
        CropEdgeKind.BottomRight => CropEdge.Bottom | CropEdge.Right,
        CropEdgeKind.BottomLeft => CropEdge.Bottom | CropEdge.Left,
        CropEdgeKind.Top => CropEdge.Top,
        CropEdgeKind.Right => CropEdge.Right,
        CropEdgeKind.Bottom => CropEdge.Bottom,
        _ => CropEdge.Left,
    };

    /// <summary>Mouse down in crop mode. Returns true when it was for the crop (otherwise crop mode ended).</summary>
    private bool CropMouseDown(Point view, Point p)
    {
        if (_crop == null) return false;
        foreach (var (kind, at) in CropHandles())
        {
            if ((at - view).Length > 9) continue;
            StartCropDrag(EdgesOf(kind), p);
            return true;
        }
        if (_cropFull.Contains(p))
        {
            StartCropDrag(CropEdge.Pan, p);
            return true;
        }
        EndCrop(commit: true);
        return false;
    }

    private void StartCropDrag(CropEdge edge, Point p)
    {
        _cropDrag = edge;
        _cropDown = p;
        _cropStartBox = _crop!.Bounds;
        _cropStartFull = _cropFull;
        _viewport.CaptureMouse();
    }

    private bool CropMouseMove(Point p)
    {
        if (_crop == null || _cropDrag == CropEdge.None) return false;
        var dx = p.X - _cropDown.X;
        var dy = p.Y - _cropDown.Y;
        var box = _cropStartBox;
        var full = _cropStartFull;
        const double min = 12;
        if (_cropDrag == CropEdge.Pan)
        {
            // Move the picture under the frame; it always covers the frame.
            var x = MathEx.Clamp(full.X + dx, box.Right - full.Width, box.Left);
            var y = MathEx.Clamp(full.Y + dy, box.Bottom - full.Height, box.Top);
            full = new Rect(x, y, full.Width, full.Height);
        }
        else
        {
            double left = box.Left, top = box.Top, right = box.Right, bottom = box.Bottom;
            if (_cropDrag.HasFlag(CropEdge.Left)) left = MathEx.Clamp(box.Left + dx, full.Left, right - min);
            if (_cropDrag.HasFlag(CropEdge.Right)) right = MathEx.Clamp(box.Right + dx, left + min, full.Right);
            if (_cropDrag.HasFlag(CropEdge.Top)) top = MathEx.Clamp(box.Top + dy, full.Top, bottom - min);
            if (_cropDrag.HasFlag(CropEdge.Bottom)) bottom = MathEx.Clamp(box.Bottom + dy, top + min, full.Bottom);
            box = new Rect(left, top, right - left, bottom - top);
        }
        _cropFull = full;
        _crop.Bounds = new Rect(Math.Round(box.X, 2), Math.Round(box.Y, 2), Math.Round(box.Width, 2), Math.Round(box.Height, 2));
        _crop.Crop = new Crop
        {
            Left = Math.Round((box.Left - full.Left) / full.Width, 5),
            Top = Math.Round((box.Top - full.Top) / full.Height, 5),
            Right = Math.Round((full.Right - box.Right) / full.Width, 5),
            Bottom = Math.Round((full.Bottom - box.Bottom) / full.Height, 5),
        };
        if (_crop.Placeholder != null) _crop.Moved = true;
        _cropTouched = true;
        PlaceVisuals(new[] { _crop }, redraw: true);
        return true;
    }

    private bool CropMouseUp()
    {
        if (_cropDrag == CropEdge.None) return false;
        _cropDrag = CropEdge.None;
        if (_viewport.IsMouseCaptured) _viewport.ReleaseMouseCapture();
        return true;
    }

    private Cursor? CropCursor(Point view, Point p)
    {
        if (_crop == null) return null;
        foreach (var (kind, at) in CropHandles())
        {
            if ((at - view).Length > 9) continue;
            return kind switch
            {
                CropEdgeKind.TopLeft or CropEdgeKind.BottomRight => Cursors.SizeNWSE,
                CropEdgeKind.TopRight or CropEdgeKind.BottomLeft => Cursors.SizeNESW,
                CropEdgeKind.Left or CropEdgeKind.Right => Cursors.SizeWE,
                _ => Cursors.SizeNS,
            };
        }
        return _cropFull.Contains(p) ? Cursors.SizeAll : Cursors.Arrow;
    }
}

public enum CropEdgeKind { TopLeft, TopRight, BottomRight, BottomLeft, Top, Right, Bottom, Left }
