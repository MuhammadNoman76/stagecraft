using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Show;

/// <summary>
/// The slim bar over a video or sound while the pointer is on it: play / pause, a seek bar with the time, and mute.
/// Dark and translucent so it reads on any slide, the same in the show and in the editor.
/// </summary>
internal sealed class MediaControlBar : Border
{
    private readonly Button _play = new();
    private readonly IconView _playIcon = new() { Width = 16, Height = 16, Foreground = Brushes.White };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), Focusable = false };
    private readonly TextBlock _time = new() { Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 74, TextAlignment = TextAlignment.Right };
    private readonly Button _mute = new();
    private readonly IconView _muteIcon = new() { Width = 16, Height = 16, Foreground = Brushes.White };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _syncing;
    private MediaHost? _host;

    public MediaControlBar()
    {
        Height = 40;
        CornerRadius = new CornerRadius(20);
        Padding = new Thickness(6, 0, 12, 0);
        Background = new SolidColorBrush(Color.FromArgb(214, 0x18, 0x18, 0x1C));
        BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        AutomationProperties.SetName(this, "Media controls");

        StyleButton(_play, _playIcon, "Play or pause");
        StyleButton(_mute, _muteIcon, "Mute or unmute");
        _play.Click += (_, _) =>
        {
            if (_host != null) Command?.Invoke(_host, MediaAction.Toggle, null);
        };
        _mute.Click += (_, _) =>
        {
            if (_host != null) Command?.Invoke(_host, MediaAction.ToggleMute, null);
        };
        _seek.ValueChanged += (_, e) =>
        {
            if (_syncing || _host == null) return;
            var target = _host.Start + e.NewValue * Math.Max(0, _host.End - _host.Start);
            Command?.Invoke(_host, MediaAction.Seek, target);
        };
        AutomationProperties.SetName(_seek, "Position");

        var dock = new DockPanel();
        DockPanel.SetDock(_play, Dock.Left);
        dock.Children.Add(_play);
        DockPanel.SetDock(_mute, Dock.Right);
        dock.Children.Add(_mute);
        DockPanel.SetDock(_time, Dock.Right);
        dock.Children.Add(_time);
        dock.Children.Add(_seek);
        Child = dock;
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start();
            else _timer.Stop();
        };
    }

    /// <summary>A button was used (Seek comes with the position in seconds).</summary>
    public event Action<MediaHost, MediaAction, double?>? Command;

    public MediaHost? Host
    {
        get => _host;
        set => _host = value;
    }

    private void StyleButton(Button b, IconView icon, string name)
    {
        b.Content = icon;
        b.Width = 32;
        b.Height = 32;
        b.Focusable = false;
        b.Cursor = System.Windows.Input.Cursors.Hand;
        b.Background = Brushes.Transparent;
        b.BorderThickness = new Thickness(0);
        b.Template = RoundTemplate();
        b.ToolTip = name;
        AutomationProperties.SetName(b, name);
    }

    private static ControlTemplate? s_round;

    private static ControlTemplate RoundTemplate()
    {
        if (s_round != null) return s_round;
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), "Bd"));
        t.Triggers.Add(hover);
        t.Seal();
        s_round = t;
        return t;
    }

    public void Refresh()
    {
        var host = _host;
        if (host == null) return;
        _playIcon.Data = TryFindResource(host.IsPlaying ? "Icon.PauseFilled" : "Icon.PlayFilled") as Geometry;
        _muteIcon.Data = TryFindResource(!host.UserMuted && host.Element.Volume > 0.001 ? "Icon.Speaker2" : "Icon.SpeakerMute") as Geometry;
        var length = Math.Max(0, host.End - host.Start);
        var pos = Math.Max(0, host.Position - host.Start);
        _syncing = true;
        _seek.Value = length > 0 ? MathEx.Clamp(pos / length, 0, 1) : 0;
        _seek.IsEnabled = length > 0 && host.Error == null;
        _syncing = false;
        _time.Text = host.Error != null ? "Can't play" : Format(pos) + " / " + Format(length);
        ToolTip = host.Error;
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
