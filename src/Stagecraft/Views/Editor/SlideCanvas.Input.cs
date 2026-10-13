using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;

namespace Stagecraft.Views.Editor;

public sealed partial class SlideCanvas
{
    private enum DragMode { None, Pending, Move, Resize, Rotate, Marquee, Pan, LineEnd }

    private DragMode _mode;
    private Point _downView;
    private Point _downSlide;
    private Slide? _before;
    private Dictionary<string, (Rect Box, double Rotation)> _start = new();
    private Rect _startUnion;
    private Handle _handle;
    private double _startAngle;
    private bool _duplicated;
    private bool _changed;
    private bool _spaceDown;
    private Vector _lastPanPoint;
    private HashSet<string> _marqueeBase = new();
    private double _wheelAccumulator;
    private DateTime _lastWheelSlide;

    private void InitInput()
    {
        _viewport.MouseDown += OnMouseDown;
        _viewport.MouseMove += OnMouseMove;
        _viewport.MouseUp += OnMouseUp;
        _viewport.LostMouseCapture += (_, _) =>
        {
            if (_mode is not DragMode.None) EndDrag(cancel: false);
        };
        _viewport.MouseLeave += (_, _) =>
        {
            if (_overlay.Hover != null)
            {
                _overlay.Hover = null;
                _overlay.InvalidateVisual();
            }
        };
        PreviewMouseWheel += OnWheel;
        KeyDown += OnKeyDown;
        KeyUp += (_, e) =>
        {
            if (e.Key == Key.Space)
            {
                _spaceDown = false;
                if (_mode != DragMode.Pan) Cursor = null;
            }
        };
        TextInput += OnTextInput;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private bool SnappingOn => AppSettings.Current.SnapToGuides && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;

    private bool FromTextEditor(object source) =>
        _textBox != null && source is DependencyObject d && (d == _textBox || IsDescendant(_textBox, d));

    private static bool IsDescendant(DependencyObject parent, DependencyObject child)
    {
        var node = child;
        while (node != null)
        {
            if (node == parent) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    // ------------------------------------------------------------------ mouse down

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_state?.CurrentSlide is not { } slide) return;
        if (FromTextEditor(e.OriginalSource)) return;
        var view = e.GetPosition(_viewport);
        var p = ToSlide(view);

        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            StartPan(view);
            e.Handled = true;
            return;
        }

        if (IsEditingText)
        {
            // Clicking inside the edited element is handled by the text box; anywhere else ends editing.
            CommitTextEdit();
        }
        Focus();

        if (e.ChangedButton == MouseButton.Right)
        {
            var target = HitTest(p);
            if (target != null && !_state.IsSelected(target.Id)) _state.Select(new[] { target.Id });
            else if (target == null && _state.Selection.Count > 0 && !DeckOperations.BoundsOf(_state.SelectedElements).Contains(p)) _state.ClearSelection();
            ContextRequested?.Invoke(p);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _downView = view;
        _downSlide = p;
        _changed = false;
        _duplicated = false;

        if (e.ClickCount == 2)
        {
            var hit = HitTest(p);
            if (hit != null) Activate(hit, p);
            e.Handled = true;
            return;
        }

        var handle = HandleAt(view);
        if (handle != null)
        {
            BeginDrag(handle.Value.Kind == HandleKind.Rotate ? DragMode.Rotate
                : handle.Value.Kind is HandleKind.LineStart or HandleKind.LineEnd ? DragMode.LineEnd : DragMode.Resize);
            _handle = handle.Value;
            if (_mode == DragMode.Rotate)
            {
                var c = RotationCenter();
                _startAngle = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI;
            }
            e.Handled = true;
            return;
        }

        var element = HitTest(p);
        _textClickCandidate = element != null && _state.IsSelected(element.Id) && _state.Selection.Count == 1 ? element.Id : null;
        if (element != null)
        {
            if (shift || ctrl)
            {
                if (_state.IsSelected(element.Id) && !ctrl) _state.RemoveFromSelection(new[] { element.Id });
                else if (_state.IsSelected(element.Id) && ctrl)
                {
                    // Ctrl on a selected element: keep it (Ctrl+drag duplicates).
                }
                else _state.AddToSelection(new[] { element.Id });
            }
            else if (!_state.IsSelected(element.Id))
            {
                _state.Select(new[] { element.Id });
            }
            BeginDrag(DragMode.Pending);
            e.Handled = true;
            return;
        }

        // Empty space: marquee selection.
        _marqueeBase = shift || ctrl ? new HashSet<string>(_state.Selection) : new HashSet<string>();
        if (!shift && !ctrl) _state.ClearSelection();
        BeginDrag(DragMode.Marquee);
        e.Handled = true;
    }

    private void BeginDrag(DragMode mode)
    {
        var slide = _state!.CurrentSlide!;
        _mode = mode;
        _before = mode is DragMode.Marquee or DragMode.Pan ? null : _state.Session.Snapshot(slide);
        _start = _state.SelectedElements.ToDictionary(e => e.Id, e => (e.Bounds, e.Rotation));
        _startUnion = _state.SelectedElements.Count > 0 ? DeckOperations.BoundsOf(_state.SelectedElements) : Rect.Empty;
        _viewport.CaptureMouse();
    }

    private void StartPan(Point view)
    {
        _mode = DragMode.Pan;
        _lastPanPoint = (Vector)view;
        Cursor = Cursors.ScrollAll;
        _viewport.CaptureMouse();
    }

    /// <summary>Double-click: edit text, or open the element's own editor.</summary>
    private void Activate(Element hit, Point p)
    {
        var state = _state!;
        // Inside a group: double-click selects the single member.
        if (hit.GroupId != null && state.EnteredGroup != hit.GroupId)
        {
            state.EnteredGroup = hit.GroupId;
            state.Select(new[] { hit.Id }, expandGroups: false);
            return;
        }
        switch (hit)
        {
            case TextElement:
            case ShapeElement { IsLine: false }:
                state.Select(new[] { hit.Id }, expandGroups: false);
                BeginTextEdit(hit, null, p);
                break;
            default:
                state.Select(new[] { hit.Id }, expandGroups: false);
                ElementActivated?.Invoke(hit);
                break;
        }
    }

    private Point RotationCenter()
    {
        var selected = _state!.SelectedElements;
        if (selected.Count == 1) return selected[0].Center;
        var u = _startUnion;
        return new Point(u.X + u.Width / 2, u.Y + u.Height / 2);
    }

    // ------------------------------------------------------------------ mouse move

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_state?.CurrentSlide is not { } slide) return;
        var view = e.GetPosition(_viewport);
        var p = ToSlide(view);

        switch (_mode)
        {
            case DragMode.None:
                UpdateHover(view, p, e);
                return;
            case DragMode.Pan:
                Pan((Vector)view - _lastPanPoint);
                _lastPanPoint = (Vector)view;
                return;
            case DragMode.Pending:
                if ((view - _downView).Length < 3) return;
                if (_state.SelectedElements.Any(x => x.Locked) || _state.Selection.Count == 0)
                {
                    _mode = DragMode.None;
                    return;
                }
                _mode = DragMode.Move;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) DuplicateForDrag(slide);
                goto case DragMode.Move;
            case DragMode.Move:
                DoMove(slide, p);
                return;
            case DragMode.Resize:
                DoResize(slide, p);
                return;
            case DragMode.Rotate:
                DoRotate(slide, p);
                return;
            case DragMode.LineEnd:
                DoLineEnd(slide, p);
                return;
            case DragMode.Marquee:
                DoMarquee(slide, p);
                return;
        }
    }

    private void UpdateHover(Point view, Point p, MouseEventArgs e)
    {
        if (_spaceDown)
        {
            Cursor = Cursors.Hand;
            return;
        }
        var handle = HandleAt(view);
        if (handle != null)
        {
            Cursor = CursorFor(handle.Value);
            if (_overlay.Hover != null)
            {
                _overlay.Hover = null;
                _overlay.InvalidateVisual();
            }
            return;
        }
        var hit = HitTest(p);
        Cursor = hit != null && !hit.Locked ? Cursors.SizeAll : null;
        if (hit is TextElement && _state!.IsSelected(hit.Id) && _state.Selection.Count == 1) Cursor = Cursors.IBeam;
        if (_overlay.Hover != hit)
        {
            _overlay.Hover = hit;
            _overlay.InvalidateVisual();
        }
    }

    private Cursor CursorFor(Handle h)
    {
        if (h.Kind == HandleKind.Rotate) return Cursors.Hand;
        if (h.Kind is HandleKind.LineStart or HandleKind.LineEnd) return Cursors.Cross;
        var rotation = _state!.SelectedElements.Count == 1 ? _state.SelectedElements[0].Rotation : 0;
        var (dx, dy) = h.Direction;
        var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + rotation + 360) % 180;
        if (angle < 22.5 || angle >= 157.5) return Cursors.SizeWE;
        if (angle < 67.5) return Cursors.SizeNWSE;
        if (angle < 112.5) return Cursors.SizeNS;
        return Cursors.SizeNESW;
    }

    private void DuplicateForDrag(Slide slide)
    {
        var selected = _state!.SelectedElements;
        var copies = DeckOperations.CopyElements(selected, 0, 0);
        var insertAt = selected.Max(x => slide.Elements.IndexOf(x)) + 1;
        slide.Elements.InsertRange(insertAt, copies);
        _duplicated = true;
        _state.Select(copies.Select(c => c.Id));
        _start = copies.ToDictionary(c => c.Id, c => (c.Bounds, c.Rotation));
        Refresh();
    }

    private SnapTargets Targets(Slide slide)
    {
        var others = slide.Elements.Where(x => !_state!.IsSelected(x.Id) && x.Placeholder != "decor").Select(x => x.RotatedBounds);
        var style = SlideStyle.For(_state!.Deck, slide);
        return new SnapTargets(others, new Size(_state.Deck.Width, _state.Deck.Height), style.Margin);
    }

    private void DoMove(Slide slide, Point p)
    {
        var dx = p.X - _downSlide.X;
        var dy = p.Y - _downSlide.Y;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (Math.Abs(dx) > Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        var union = _startUnion;
        if (_duplicated) union = DeckOperations.BoundsOf(_state!.SelectedElements.Select(x =>
        {
            var clone = DeckJson.Clone(x);
            clone.Bounds = _start[x.Id].Box;
            return clone;
        }));
        var moved = new Rect(union.X + dx, union.Y + dy, union.Width, union.Height);
        IReadOnlyList<Guide> guides = Array.Empty<Guide>();
        if (SnappingOn)
        {
            var snap = Snapper.SnapMove(moved, Targets(slide), 7 / Zoom);
            dx += snap.Dx;
            dy += snap.Dy;
            guides = snap.Guides;
        }
        foreach (var e in _state!.SelectedElements)
        {
            var start = _start[e.Id].Box;
            e.X = Math.Round(start.X + dx, 2);
            e.Y = Math.Round(start.Y + dy, 2);
        }
        DeckOperations.UpdateConnectors(slide);
        _changed = true;
        _overlay.Guides = guides;
        _overlay.Badge = null;
        PlaceVisuals(_state.SelectedElements, redraw: false);
        _state.RaiseLiveChange();
    }

    private void DoResize(Slide slide, Point p)
    {
        var selected = _state!.SelectedElements;
        if (selected.Count == 0) return;
        var (hx, hy) = _handle.Direction;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (selected.Count == 1)
        {
            var e = selected[0];
            var (box, rotation) = _start[e.Id];
            var aspectByDefault = e is ImageElement or IconElement;
            var keepAspect = _handle.IsCorner && (aspectByDefault ^ shift);
            var rect = ResizeRect(box, rotation, p, hx, hy, keepAspect, alt);
            if (Math.Abs(rotation % 360) < 0.01 && SnappingOn && !keepAspect && !alt)
            {
                var snap = Snapper.SnapResize(rect, hx, hy, Targets(slide), 7 / Zoom);
                if (snap.SnappedX)
                {
                    if (hx < 0) rect = new Rect(rect.X + snap.Dx, rect.Y, Math.Max(4, rect.Width - snap.Dx), rect.Height);
                    else rect = new Rect(rect.X, rect.Y, Math.Max(4, rect.Width + snap.Dx), rect.Height);
                }
                if (snap.SnappedY)
                {
                    if (hy < 0) rect = new Rect(rect.X, rect.Y + snap.Dy, rect.Width, Math.Max(4, rect.Height - snap.Dy));
                    else rect = new Rect(rect.X, rect.Y, rect.Width, Math.Max(4, rect.Height + snap.Dy));
                }
                _overlay.Guides = snap.Guides;
            }
            else _overlay.Guides = Array.Empty<Guide>();
            e.Bounds = Round(rect);
            if (e.Placeholder != null) e.Moved = true;
            _overlay.Badge = $"{Math.Round(e.Width)} × {Math.Round(e.Height)}";
            PlaceVisuals(selected, redraw: true);
        }
        else
        {
            var keepAspect = _handle.IsCorner && !shift;
            var rect = ResizeRect(_startUnion, 0, p, hx, hy, keepAspect, alt);
            foreach (var e in selected)
            {
                var clone = DeckJson.Clone(e);
                clone.Bounds = _start[e.Id].Box;
                DeckOperations.ScaleInto(new[] { clone }, _startUnion, rect);
                e.Bounds = Round(clone.Bounds);
                if (e.Placeholder != null) e.Moved = true;
            }
            _overlay.Badge = $"{Math.Round(rect.Width)} × {Math.Round(rect.Height)}";
            _overlay.Guides = Array.Empty<Guide>();
            PlaceVisuals(selected, redraw: true);
        }
        DeckOperations.UpdateConnectors(slide);
        _overlay.BadgeAt = Mouse.GetPosition(_viewport) + new Vector(0, 28);
        _changed = true;
        _state.RaiseLiveChange();
    }

    private static Rect Round(Rect r) => new(Math.Round(r.X, 1), Math.Round(r.Y, 1), Math.Max(1, Math.Round(r.Width, 1)), Math.Max(0, Math.Round(r.Height, 1)));

    /// <summary>The new box when a handle of a (possibly rotated) box is dragged to <paramref name="mouse"/>; the opposite side stays put.</summary>
    public static Rect ResizeRect(Rect box, double rotation, Point mouse, int hx, int hy, bool keepAspect, bool fromCenter)
    {
        var c0 = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        var m = Rotate(mouse, c0, -rotation) - c0; // mouse in the box's own frame, relative to its centre
        var w = box.Width;
        var h = box.Height;
        var ax = -hx * w / 2;
        var ay = -hy * h / 2;
        double newW = w, newH = h;
        if (fromCenter)
        {
            if (hx != 0) newW = Math.Max(4, 2 * hx * m.X);
            if (hy != 0) newH = Math.Max(4, 2 * hy * m.Y);
        }
        else
        {
            if (hx != 0) newW = Math.Max(4, hx * (m.X - ax));
            if (hy != 0) newH = Math.Max(4, hy * (m.Y - ay));
        }
        if (keepAspect && w > 0 && h > 0)
        {
            var ratio = w / h;
            if (hx != 0 && hy != 0)
            {
                if (newW / w > newH / h) newH = newW / ratio;
                else newW = newH * ratio;
            }
            else if (hx != 0) newH = newW / ratio;
            else newW = newH * ratio;
        }
        double cx, cy;
        if (fromCenter)
        {
            cx = 0;
            cy = 0;
        }
        else
        {
            cx = hx != 0 ? ax + hx * newW / 2 : 0;
            cy = hy != 0 ? ay + hy * newH / 2 : 0;
        }
        var center = Rotate(new Point(c0.X + cx, c0.Y + cy), c0, rotation);
        return new Rect(center.X - newW / 2, center.Y - newH / 2, newW, newH);
    }

    private void DoRotate(Slide slide, Point p)
    {
        var c = RotationCenter();
        var angle = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI;
        var delta = angle - _startAngle;
        var selected = _state!.SelectedElements;
        double Snap(double a)
        {
            a = ((a % 360) + 540) % 360 - 180;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return Math.Round(a / 15) * 15;
            var nearest = Math.Round(a / 90) * 90;
            return Math.Abs(a - nearest) < 4 ? nearest : Math.Round(a, 1);
        }
        if (selected.Count == 1)
        {
            var e = selected[0];
            e.Rotation = Snap(_start[e.Id].Rotation + delta);
            if (Math.Abs(e.Rotation) < 0.001 || Math.Abs(Math.Abs(e.Rotation) - 360) < 0.001) e.Rotation = 0;
            _overlay.Badge = $"{e.Rotation:0.#}°";
        }
        else
        {
            var snapped = Snap(delta);
            foreach (var e in selected)
            {
                var (box, rot) = _start[e.Id];
                var center = Rotate(new Point(box.X + box.Width / 2, box.Y + box.Height / 2), c, snapped);
                e.X = Math.Round(center.X - box.Width / 2, 2);
                e.Y = Math.Round(center.Y - box.Height / 2, 2);
                e.Rotation = Snap(rot + snapped);
            }
            _overlay.Badge = $"{snapped:0.#}°";
        }
        _overlay.BadgeAt = Mouse.GetPosition(_viewport) + new Vector(0, 28);
        _changed = true;
        PlaceVisuals(selected, redraw: false);
        _state.RaiseLiveChange();
    }

    private void DoLineEnd(Slide slide, Point p)
    {
        if (_state!.SingleSelection is not ShapeElement line) return;
        var (box, _) = _start[line.Id];
        var original = DeckJson.Clone(line);
        original.Bounds = box;
        var (a, b) = ElementRenderer.LineEnds(original, box);
        var moving = _handle.Kind == HandleKind.LineStart;
        var fixedPoint = moving ? b : a;
        var target = p;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var v = target - fixedPoint;
            var angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
            var len = v.Length;
            target = fixedPoint + new Vector(Math.Cos(angle) * len, Math.Sin(angle) * len);
        }

        // Connectors attach to the edge midpoint of the element under the pointer.
        if (line.Shape == ShapeKind.Connector)
        {
            var under = slide.Elements.LastOrDefault(x => x.Id != line.Id && !(x is ShapeElement { IsLine: true }) && x.RotatedBounds.Contains(p));
            if (under != null)
            {
                target = DeckOperations.Anchor(under, ConnectorSide.Auto, fixedPoint);
                if (moving)
                {
                    line.StartId = under.Id;
                    line.StartSide = ConnectorSide.Auto;
                }
                else
                {
                    line.EndId = under.Id;
                    line.EndSide = ConnectorSide.Auto;
                }
            }
            else if (moving) line.StartId = null;
            else line.EndId = null;
        }
        else if (SnappingOn)
        {
            var snap = Snapper.SnapMove(new Rect(target, new Size(0, 0)), Targets(slide), 7 / Zoom);
            target = new Point(target.X + snap.Dx, target.Y + snap.Dy);
            _overlay.Guides = snap.Guides;
        }

        DeckOperations.SetLine(line, moving ? target : fixedPoint, moving ? fixedPoint : target);
        _changed = true;
        PlaceVisuals(new[] { line }, redraw: true);
        _state.RaiseLiveChange();
    }

    private void DoMarquee(Slide slide, Point p)
    {
        var rect = new Rect(_downSlide, p);
        _overlay.Marquee = rect;
        var inside = slide.Elements.Where(x => !x.Locked && rect.Contains(x.RotatedBounds)).Select(x => x.Id);
        var ids = new HashSet<string>(_marqueeBase);
        foreach (var id in inside) ids.Add(id);
        _state!.Select(ids);
        _overlay.InvalidateVisual();
    }

    // ------------------------------------------------------------------ mouse up

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;
        if (_mode == DragMode.Pending && _state?.SingleSelection is TextElement t && (e.GetPosition(_viewport) - _downView).Length < 3
            && e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0 && _textClickCandidate == t.Id)
        {
            // A second single click on a selected text box starts editing at the click.
            EndDrag(cancel: false);
            BeginTextEdit(t, null, _downSlide);
            return;
        }
        EndDrag(cancel: false);
        e.Handled = true;
    }

    private string? _textClickCandidate;

    private void EndDrag(bool cancel)
    {
        var mode = _mode;
        _mode = DragMode.None;
        if (_viewport.IsMouseCaptured) _viewport.ReleaseMouseCapture();
        Cursor = _spaceDown ? Cursors.Hand : null;
        _overlay.Guides = Array.Empty<Guide>();
        _overlay.Marquee = null;
        _overlay.Badge = null;
        _overlay.InvalidateVisual();

        var slide = _state?.CurrentSlide;
        if (slide != null && _before != null && (_changed || _duplicated))
        {
            if (cancel)
            {
                _state!.Session.Revert(slide, _before);
            }
            else
            {
                if (mode == DragMode.Move)
                    foreach (var el in _state!.SelectedElements.Where(x => x.Placeholder != null)) el.Moved = true;
                var label = _duplicated ? "Duplicate" : mode switch
                {
                    DragMode.Move => "Move",
                    DragMode.Resize => "Resize",
                    DragMode.Rotate => "Rotate",
                    DragMode.LineEnd => "Change line",
                    _ => "Edit",
                };
                _state!.Session.Commit(slide, _before, label);
            }
        }
        _before = null;
        _changed = false;
        _duplicated = false;
    }

    // ------------------------------------------------------------------ wheel

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_state == null) return;
        if (IsEditingText && FromTextEditor(e.OriginalSource) && (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var view = e.GetPosition(_viewport);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var factor = Math.Pow(1.0015, e.Delta);
            SetZoom(Zoom * factor, view);
            e.Handled = true;
            return;
        }
        if (Zoom > FitZoom() * 1.02)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Pan(new Vector(e.Delta * 0.6, 0));
            else Pan(new Vector(0, e.Delta * 0.6));
            e.Handled = true;
            return;
        }
        // At "fit" the wheel turns pages, one per notch (touchpads send many small steps).
        _wheelAccumulator += e.Delta;
        if (Math.Abs(_wheelAccumulator) >= 120 && (DateTime.UtcNow - _lastWheelSlide).TotalMilliseconds > 140)
        {
            var dir = _wheelAccumulator < 0 ? 1 : -1;
            _wheelAccumulator = 0;
            _lastWheelSlide = DateTime.UtcNow;
            SlideWheel?.Invoke(dir);
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ keyboard

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_state?.CurrentSlide is not { } slide || IsEditingText) return;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Space when !e.IsRepeat:
                _spaceDown = true;
                Cursor = Cursors.Hand;
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                if (_state.Selection.Count == 0 || _state.SelectedElements.Any(x => x.Locked)) return;
                var step = shift ? 20 : ctrl ? 1 : 2; // 1 pt (2 units), Shift 10 pt, Ctrl ½ pt
                var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                var ids = _state.Selection.ToList();
                _state.Session.EditSlide(slide, "Nudge", s =>
                {
                    foreach (var el in s.Elements.Where(x => ids.Contains(x.Id)))
                    {
                        el.X = Math.Round(el.X + dx, 2);
                        el.Y = Math.Round(el.Y + dy, 2);
                        if (el.Placeholder != null) el.Moved = true;
                    }
                    DeckOperations.UpdateConnectors(s);
                }, "nudge");
                e.Handled = true;
                break;
            case Key.Tab when !ctrl:
                CycleSelection(shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_state.EnteredGroup != null)
                {
                    var group = _state.EnteredGroup;
                    _state.EnteredGroup = null;
                    _state.Select(slide.Elements.Where(x => x.GroupId == group).Select(x => x.Id));
                }
                else _state.ClearSelection();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.F2:
                if (_state.SingleSelection is { } single and (TextElement or ShapeElement { IsLine: false }))
                {
                    BeginTextEdit(single, null, null);
                    e.Handled = true;
                }
                else if (_state.SingleSelection is { } other)
                {
                    ElementActivated?.Invoke(other);
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>Typing with one text element selected starts editing it with that character.</summary>
    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsEditingText || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        if (_state?.SingleSelection is not { } single || single is not (TextElement or ShapeElement { IsLine: false })) return;
        if (e.Text == " ") return;
        BeginTextEdit(single, e.Text, null, replaceAll: true);
        e.Handled = true;
    }

    public void CycleSelection(int direction)
    {
        var slide = _state?.CurrentSlide;
        if (slide == null || slide.Elements.Count == 0) return;
        var list = slide.Elements;
        var current = _state!.SelectedElements.LastOrDefault();
        var index = current == null ? (direction > 0 ? -1 : list.Count) : list.IndexOf(current);
        index = ((index + direction) % list.Count + list.Count) % list.Count;
        _state.EnteredGroup = list[index].GroupId;
        _state.Select(new[] { list[index].Id }, expandGroups: false);
    }

    // ------------------------------------------------------------------ drag and drop of pictures

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ClipboardImages.HasImages(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var images = ClipboardImages.Read(e.Data);
        if (images.Count == 0) return;
        var p = ToSlide(e.GetPosition(_viewport));
        var target = HitTest(p) as ImageElement;
        ImagesDropped?.Invoke(images, p, target);
        e.Handled = true;
    }

    public string ZoomText => Math.Round(Zoom * 100).ToString(CultureInfo.CurrentCulture) + "%";
}
