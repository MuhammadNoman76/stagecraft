using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Stagecraft.Interop;
using Stagecraft.Themes;

namespace Stagecraft.Controls;

/// <summary>
/// Base for every Stagecraft window: Windows 11 Mica backdrop where available (solid colour elsewhere),
/// a title bar that follows the light / dark theme, Segoe UI Variable, and live theme switching.
/// </summary>
public class FluentWindow : Window
{
    private IntPtr _hwnd;

    public FluentWindow()
    {
        SetResourceReference(FontFamilyProperty, "UiFont");
        FontSize = 14;
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(BackgroundProperty, "Window.Background");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        Loaded += (_, _) => KeepOnScreen();
    }

    /// <summary>
    /// Keeps the whole window, title bar included, on the display it opened on. A window larger than that
    /// display's work area (a small screen, or high display scaling) is shrunk to fit, or maximized when even its
    /// minimum size doesn't fit. Windows placed off every display on purpose (the off-screen test mode) are left alone.
    /// </summary>
    public void KeepOnScreen()
    {
        if (_hwnd == IntPtr.Zero || WindowState != WindowState.Normal) return;
        var rect = Native.WindowRect(_hwnd);
        if (rect == null) return;
        var r = rect.Value;
        var monitors = Native.Monitors();
        if (!monitors.Any(m => m.Bounds.Left < r.Right && r.Left < m.Bounds.Right && m.Bounds.Top < r.Bottom && r.Top < m.Bounds.Bottom)) return;
        var handle = Native.MonitorOf(_hwnd);
        var monitor = monitors.FirstOrDefault(m => m.Handle == handle);
        if (monitor == null) return;
        var work = monitor.WorkArea;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? monitor.DpiScale;
        if (MinWidth * scale > work.Width || MinHeight * scale > work.Height)
        {
            if (ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                WindowState = WindowState.Maximized;
                return;
            }
        }
        var width = Math.Min(r.Width, work.Width);
        var height = Math.Min(r.Height, work.Height);
        var left = MathEx.Clamp(r.Left, work.Left, work.Right - width);
        var top = MathEx.Clamp(r.Top, work.Top, work.Bottom - height);
        if (left == r.Left && top == r.Top && width == r.Width && height == r.Height) return;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>Dialog frame: no minimize / maximize buttons, not in the taskbar when it has an owner.</summary>
    public bool IsDialog { get; set; }

    public bool UsesMica { get; private set; }

    /// <summary>Always dark (the presenter view), whatever the app theme.</summary>
    public bool ForceDark { get; set; }

    public IntPtr Handle => _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (IsDialog)
        {
            Native.MakeDialogFrame(_hwnd);
            if (Owner != null) ShowInTaskbar = false;
        }
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyBackdrop();
    }

    private void OnThemeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnThemeChanged));
            return;
        }
        if (_hwnd != IntPtr.Zero) ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        Native.SetDarkTitleBar(_hwnd, ForceDark || ThemeManager.IsDark);
        var source = HwndSource.FromHwnd(_hwnd);
        if (Native.TrySetMica(_hwnd) && source?.CompositionTarget != null)
        {
            UsesMica = true;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Background = Brushes.Transparent;
        }
        else
        {
            UsesMica = false;
            SetResourceReference(BackgroundProperty, "Window.Background");
        }
    }

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_THEMECHANGED = 0x031A;
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_SETTINGCHANGE or WM_THEMECHANGED or WM_DWMCOLORIZATIONCOLORCHANGED)
        {
            // "ImmersiveColorSet" (light / dark switch) arrives as WM_SETTINGCHANGE; let it settle first.
            Dispatcher.BeginInvoke(new Action(ThemeManager.OnSystemSettingsChanged), System.Windows.Threading.DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }
}
