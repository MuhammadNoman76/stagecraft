using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Interop;

namespace Stagecraft.Views.Show;

/// <summary>
/// What the audience sees: the slide, full screen on the chosen display (or a normal small window in the
/// hidden test mode). Click / wheel / keys / presenter clickers move through the show; right-click for a menu.
/// </summary>
public sealed class AudienceWindow : Window
{
    private readonly SlideShowController _controller;
    private readonly ShowSurface _surface = new();
    private readonly Native.MonitorInfo? _monitor;
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _closingFromShow;

    internal AudienceWindow(SlideShowController controller, Native.MonitorInfo? monitor, bool windowed)
    {
        _controller = controller;
        _monitor = windowed ? null : monitor;
        Title = windowed ? "Slide show (test window)" : "Stagecraft slide show";
        Background = Brushes.Black;
        Content = _surface;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        System.Windows.Automation.AutomationProperties.SetName(this, "Slide show");
        if (Application.Current?.MainWindow?.Icon is { } icon) Icon = icon;

        if (windowed)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Width = 960;
            Height = 540 + SystemParameters.WindowCaptionHeight + 16;
            WindowStartupLocation = WindowStartupLocation.Manual;
            var work = SystemParameters.WorkArea;
            Left = SlideShow.WindowedOrigin?.X ?? work.Left + 40;
            Top = SlideShow.WindowedOrigin?.Y ?? work.Top + 40;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        _surface.Attach(controller, acceptsInk: true);
        PreviewKeyDown += (_, e) =>
        {
            if (_controller.HandleKey(e)) e.Handled = true;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_controller.Tool == ShowTool.Pointer || _controller.Tool == ShowTool.Laser || _controller.AtEnd) _controller.NextStep();
        };
        MouseRightButtonUp += (_, e) =>
        {
            ShowMenu();
            e.Handled = true;
        };
        MouseWheel += (_, e) =>
        {
            if (e.Delta < 0) _controller.NextStep();
            else _controller.PreviousStep();
        };
        MouseMove += (_, _) =>
        {
            if (_controller.Tool != ShowTool.Laser && _controller.Tool == ShowTool.Pointer) Cursor = null;
            _cursorTimer.Stop();
            _cursorTimer.Start();
        };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorTimer.Stop();
            if (_controller.Tool == ShowTool.Pointer) Cursor = Cursors.None;
        };
        _cursorTimer.Start();
        Closing += (_, _) =>
        {
            if (!_closingFromShow) _controller.End();
        };
    }

    public ShowSurface Surface => _surface;

    public void CloseFromShow()
    {
        _closingFromShow = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetDarkTitleBar(hwnd, true);
        if (_monitor == null) return;
        Native.SetSquareCorners(hwnd);
        var b = _monitor.Bounds;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
    }

    private void ShowMenu()
    {
        Cursor = null;
        var menu = new ContextMenu();
        void Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            if (TryFindResource(icon) is Geometry g) Ui.SetIcon(mi, g);
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item("Next", "Icon.ChevronRight", _controller.NextStep, "→");
        Item("Previous", "Icon.ChevronLeft", _controller.PreviousStep, "←");
        var go = new MenuItem { Header = "Go to slide" };
        for (var i = 0; i < _controller.Slides.Count; i++)
        {
            var index = i;
            var title = _controller.Slides[i].TitleText;
            var mi = new MenuItem { Header = $"{i + 1}  {(title.Length > 0 ? title : "Slide")}", IsChecked = i == _controller.Index };
            mi.Click += (_, _) => _controller.GoTo(index);
            go.Items.Add(mi);
        }
        menu.Items.Add(go);
        menu.Items.Add(new Separator());
        Item("Laser pointer", "Icon.LaserTool", () => _controller.SetTool(ShowTool.Laser), "Ctrl+L");
        Item("Pen", "Icon.Pen", () => _controller.SetTool(ShowTool.Pen), "Ctrl+P");
        Item("Highlighter", "Icon.Highlight", () => _controller.SetTool(ShowTool.Highlighter), "Ctrl+I");
        Item("Erase all ink on this slide", "Icon.Eraser", _controller.ClearInk, "E");
        Item("Black screen", "Icon.Square", () => _controller.SetBlank(BlankMode.Black), "B");
        Item("White screen", "Icon.Square", () => _controller.SetBlank(BlankMode.White), "W");
        menu.Items.Add(new Separator());
        Item("End show", "Icon.Dismiss", _controller.End, "Esc");
        menu.IsOpen = true;
    }
}
