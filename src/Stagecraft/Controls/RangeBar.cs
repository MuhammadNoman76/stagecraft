using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Stagecraft.Controls;

/// <summary>
/// A time bar for trimming: the kept part between two handles, and a play head. In single mode (poster frame) only the
/// play head is shown. Keyboard: Left / Right move the play head (Shift = 1 s), Home / End jump.
/// </summary>
public sealed class RangeBar : FrameworkElement
{
    private enum Part { None, Start, End, Head }

    private Part _drag;
    private double _length = 1;
    private double _start;
    private double _end = 1;
    private double _position;

    public RangeBar()
    {
        Height = 44;
        Focusable = true;
        Cursor = Cursors.Hand;
        SetResourceReference(FocusVisualStyleProperty, "FocusVisual"); // shown for keyboard focus only
        AutomationProperties.SetName(this, "Time bar");
    }

    /// <summary>Only a play head (choosing a frame), no range.</summary>
    public bool SingleMode { get; set; }

    public double Length
    {
        get => _length;
        set
        {
            _length = Math.Max(0.01, value);
            InvalidateVisual();
        }
    }

    public double Start
    {
        get => _start;
        set
        {
            _start = MathEx.Clamp(value, 0, _length);
            InvalidateVisual();
        }
    }

    public double End
    {
        get => _end;
        set
        {
            _end = MathEx.Clamp(value, 0, _length);
            InvalidateVisual();
        }
    }

    public double Position
    {
        get => _position;
        set
        {
            _position = MathEx.Clamp(value, 0, _length);
            InvalidateVisual();
        }
    }

    /// <summary>The user moved the start or end handle.</summary>
    public event Action? RangeChanged;

    /// <summary>The user moved the play head.</summary>
    public event Action? PositionChanged;

    private const double Pad = 10;

    private double X(double seconds) => Pad + seconds / _length * Math.Max(1, ActualWidth - 2 * Pad);

    private double Seconds(double x) => MathEx.Clamp((x - Pad) / Math.Max(1, ActualWidth - 2 * Pad) * _length, 0, _length);

    protected override void OnRender(DrawingContext dc)
    {
        var accent = TryFindResource("Accent.Fill") as Brush ?? Brushes.DodgerBlue;
        var track = TryFindResource("Control.StrongFill") as Brush ?? Brushes.Gray;
        var text = TryFindResource("Text.Primary") as Brush ?? Brushes.Black;
        var mid = ActualHeight / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushOpacity(0.35);
        dc.DrawRoundedRectangle(track, null, new Rect(Pad, mid - 4, Math.Max(0, ActualWidth - 2 * Pad), 8), 4, 4);
        dc.Pop();
        if (!SingleMode)
        {
            var a = X(_start);
            var b = X(_end);
            dc.DrawRoundedRectangle(accent, null, new Rect(a, mid - 4, Math.Max(0, b - a), 8), 4, 4);
            foreach (var x in new[] { a, b })
            {
                var grip = new Rect(x - 5, mid - 15, 10, 30);
                dc.DrawRoundedRectangle(Brushes.White, new Pen(accent, 2), grip, 4, 4);
                dc.DrawLine(new Pen(accent, 1.2), new Point(x, mid - 7), new Point(x, mid + 7));
            }
        }
        var h = X(_position);
        dc.DrawLine(new Pen(text, 2), new Point(h, 4), new Point(h, ActualHeight - 4));
        dc.DrawEllipse(text, null, new Point(h, 5), 4.5, 4.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var x = e.GetPosition(this).X;
        _drag = Part.Head;
        if (!SingleMode)
        {
            if (Math.Abs(x - X(_start)) <= 9) _drag = Part.Start;
            else if (Math.Abs(x - X(_end)) <= 9) _drag = Part.End;
        }
        CaptureMouse();
        Move(x);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (_drag != Part.None && IsMouseCaptured) Move(x);
        else if (!SingleMode) Cursor = Math.Abs(x - X(_start)) <= 9 || Math.Abs(x - X(_end)) <= 9 ? Cursors.SizeWE : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = Part.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void Move(double x)
    {
        var s = Seconds(x);
        switch (_drag)
        {
            case Part.Start:
                _start = Math.Min(s, _end - Math.Min(0.1, _length / 10));
                _position = _start;
                RangeChanged?.Invoke();
                PositionChanged?.Invoke();
                break;
            case Part.End:
                _end = Math.Max(s, _start + Math.Min(0.1, _length / 10));
                _position = _end;
                RangeChanged?.Invoke();
                PositionChanged?.Invoke();
                break;
            case Part.Head:
                _position = s;
                PositionChanged?.Invoke();
                break;
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.0 : 1.0 / 30;
        switch (e.Key)
        {
            case Key.Left: _position = Math.Max(0, _position - step); break;
            case Key.Right: _position = Math.Min(_length, _position + step); break;
            case Key.Home: _position = SingleMode ? 0 : _start; break;
            case Key.End: _position = SingleMode ? _length : _end; break;
            default: return;
        }
        PositionChanged?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();
}
