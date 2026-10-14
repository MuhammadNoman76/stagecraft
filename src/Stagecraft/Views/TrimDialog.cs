using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Views.Show;

namespace Stagecraft.Views;

/// <summary>What the trim dialog was closed with.</summary>
public sealed class TrimResult
{
    public double Start { get; init; }
    public double End { get; init; }
    public double Length { get; init; }
    public double Position { get; init; }

    /// <summary>Poster mode: the chosen frame.</summary>
    public BitmapSource? Frame { get; init; }
}

/// <summary>
/// Trims a video or sound (drag the two handles; play previews just the kept part), or chooses a video's poster
/// frame (move to the frame, then "Use this frame").
/// </summary>
public sealed class TrimDialog : FluentWindow
{
    public enum Mode { Trim, Poster }

    private readonly Mode _mode;
    private readonly string _path;
    private readonly MediaElement _player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform };
    private readonly RangeBar _bar = new();
    private readonly Button _play = new() { Width = 40, Height = 40 };
    private readonly TextBlock _time = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly NumberBox _start = new() { Minimum = 0, Step = 0.1, Decimals = 2, Suffix = " s", Width = 110 };
    private readonly NumberBox _end = new() { Minimum = 0, Step = 0.1, Decimals = 2, Suffix = " s", Width = 110 };
    private readonly TextBlock _kept = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Button _ok = new();
    private bool _opened;
    private bool _playing;
    private double _length;
    private TrimResult? _result;

    private TrimDialog(MediaClipElement clip, string path, Mode mode, ImageSource? poster)
    {
        _mode = mode;
        _path = path;
        var video = clip is VideoElement;
        Title = mode == Mode.Poster ? "Poster frame" : video ? "Trim video" : "Trim sound";
        Width = 760;
        Height = video ? 640 : 380;
        MinWidth = 560;
        MinHeight = video ? 460 : 340;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _length = clip.Length ?? 0;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new DockPanel { Margin = new Thickness(24, 18, 24, 16) };
        var title = new TextBlock { Text = Title };
        title.SetResourceReference(StyleProperty, "Text.Subtitle");
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(title);
        var hint = new TextBlock
        {
            Text = mode == Mode.Poster
                ? "Move to the frame that should show before the video plays (and in pictures and PDF), then choose Use this frame."
                : "Drag the handles to cut the start and end. Play previews the part that's kept. The file itself isn't changed.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);

        // Controls under the picture.
        var controls = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(controls, Dock.Bottom);
        _bar.SingleMode = mode == Mode.Poster;
        controls.Children.Add(_bar);
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        _play.SetResourceReference(StyleProperty, "IconButton");
        Ui.SetIcon(_play, (Geometry)FindResource("Icon.PlayFilled"));
        _play.ToolTip = "Play or pause (Space)";
        AutomationProperties.SetName(_play, "Play or pause");
        _play.Click += (_, _) => TogglePlay();
        DockPanel.SetDock(_play, Dock.Left);
        row.Children.Add(_play);
        DockPanel.SetDock(_time, Dock.Left);
        row.Children.Add(_time);
        if (mode == Mode.Trim)
        {
            var range = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            range.Children.Add(Caption("Start"));
            AutomationProperties.SetName(_start, "Start");
            range.Children.Add(_start);
            range.Children.Add(Caption("End"));
            AutomationProperties.SetName(_end, "End");
            range.Children.Add(_end);
            _kept.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            range.Children.Add(_kept);
            row.Children.Add(range);
        }
        controls.Children.Add(row);
        body.Children.Add(controls);

        // The picture (or a speaker for sounds).
        var stage = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Black, ClipToBounds = true };
        var grid = new Grid();
        if (video)
        {
            // The poster frame shows while the video opens.
            if (poster != null) grid.Children.Add(new Image { Source = poster, Stretch = Stretch.Uniform });
            grid.Children.Add(_player);
        }
        else
        {
            grid.Children.Add(_player);
            _player.Visibility = Visibility.Collapsed;
            grid.Children.Add(new IconView { Width = 64, Height = 64, Data = (Geometry)FindResource("Icon.Speaker2"), Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) });
        }
        grid.Children.Add(_status);
        stage.Child = grid;
        body.Children.Add(stage);
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _ok.Content = mode == Mode.Poster ? "Use this frame" : "Done";
        _ok.MinWidth = 130;
        _ok.IsDefault = true;
        _ok.IsEnabled = false;
        _ok.SetResourceReference(StyleProperty, "AccentButton");
        _ok.Click += async (_, _) => await Accept();
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        root.Children.Add(footer);
        Content = root;

        // Wiring.
        // Scrubbing (frames while dragging) is for video only: sound-only MP4 / M4A files don't open with it on.
        _player.ScrubbingEnabled = video;
        _player.IsMuted = MediaPlayback.ForceMute;
        _player.Volume = MediaPlayback.ForceMute ? 0 : MathEx.Clamp(clip.Volume, 0, 1);
        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            if (_player.NaturalDuration.HasTimeSpan) _length = _player.NaturalDuration.TimeSpan.TotalSeconds;
            if (_length <= 0) _length = clip.Length ?? 1;
            _bar.Length = _length;
            _bar.Start = mode == Mode.Trim ? MathEx.Clamp(clip.TrimStart, 0, _length) : 0;
            _bar.End = mode == Mode.Trim ? MathEx.Clamp(clip.TrimEnd ?? _length, 0, _length) : _length;
            var at = mode == Mode.Poster && clip is VideoElement v ? v.PosterTime : _bar.Start;
            _bar.Position = at;
            _start.Maximum = _end.Maximum = _length;
            SyncNumbers();
            _player.Play();
            _player.Pause();
            Seek(at);
            _status.Visibility = Visibility.Collapsed;
            _ok.IsEnabled = true;
            UpdateTime();
        };
        _player.MediaFailed += (_, e) =>
        {
            _status.Text = "This file can't be played here.\n" + MediaProbe.CantPlayAdvice;
            _status.Visibility = Visibility.Visible;
            Services.Log.Error(e.ErrorException, "Trim dialog " + path);
        };
        _player.MediaEnded += (_, _) => Pause();
        _status.Text = "Opening…";
        // The stored length lets the handles and numbers show (and trimming work) before the file has opened.
        if (mode == Mode.Trim && clip.Length is > 0)
        {
            _bar.Length = _length;
            _bar.Start = MathEx.Clamp(clip.TrimStart, 0, _length);
            _bar.End = MathEx.Clamp(clip.TrimEnd ?? _length, 0, _length);
            _bar.Position = _bar.Start;
            _start.Maximum = _end.Maximum = _length;
            SyncNumbers();
            _ok.IsEnabled = true;
        }
        _bar.PositionChanged += () =>
        {
            Seek(_bar.Position);
            UpdateTime();
        };
        _bar.RangeChanged += SyncNumbers;
        _start.ValueChanged += v =>
        {
            _bar.Start = Math.Min(v, _bar.End - 0.1);
            _bar.Position = _bar.Start;
            Seek(_bar.Start);
            SyncNumbers();
        };
        _end.ValueChanged += v =>
        {
            _bar.End = Math.Max(v, _bar.Start + 0.1);
            SyncNumbers();
        };
        _timer.Tick += (_, _) =>
        {
            if (!_playing) return;
            var pos = _player.Position.TotalSeconds;
            if (_mode == Mode.Trim && pos >= _bar.End - 0.02)
            {
                Pause();
                Seek(_bar.Start);
                pos = _bar.Start;
            }
            _bar.Position = pos;
            UpdateTime();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Space && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
            {
                TogglePlay();
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            try
            {
                _player.Source = new Uri(path);
                _player.Pause(); // with manual loading, this opens the file (MediaOpened) without playing it
            }
            catch (Exception ex)
            {
                _status.Text = "This file can't be opened. " + ex.Message;
            }
            _bar.Focus();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _player.Stop();
            _player.Close();
        };
    }

    private TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        return t;
    }

    private void SyncNumbers()
    {
        _start.Value = Math.Round(_bar.Start, 2);
        _end.Value = Math.Round(_bar.End, 2);
        _kept.Text = "Keeps " + (_bar.End - _bar.Start).ToString("0.0", CultureInfo.CurrentCulture) + " s";
    }

    private void Seek(double seconds)
    {
        if (!_opened) return;
        _player.Position = TimeSpan.FromSeconds(MathEx.Clamp(seconds, 0, _length));
    }

    private void TogglePlay()
    {
        if (!_opened) return;
        if (_playing)
        {
            Pause();
            return;
        }
        var pos = _player.Position.TotalSeconds;
        if (_mode == Mode.Trim && (pos < _bar.Start - 0.02 || pos >= _bar.End - 0.05)) Seek(_bar.Start);
        _player.Play();
        _playing = true;
        _timer.Start();
        Ui.SetIcon(_play, (Geometry)FindResource("Icon.PauseFilled"));
    }

    private void Pause()
    {
        _player.Pause();
        _playing = false;
        _timer.Stop();
        Ui.SetIcon(_play, (Geometry)FindResource("Icon.PlayFilled"));
    }

    private void UpdateTime()
    {
        _time.Text = Format(_bar.Position) + " / " + Format(_length);
    }

    private static string Format(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    private async Task Accept()
    {
        Pause();
        if (_mode == Mode.Poster)
        {
            _ok.IsEnabled = false;
            var probe = await MediaProbe.ProbeAsync(_path, _bar.Position, TimeSpan.FromSeconds(12));
            if (probe.Frame == null)
            {
                _ok.IsEnabled = true;
                MessageDialog.Show(this, "That frame couldn't be taken", probe.Error ?? "Try another moment of the video.", "OK", icon: DialogIcon.Warning);
                return;
            }
            _result = new TrimResult { Position = _bar.Position, Length = _length, Frame = probe.Frame, Start = 0, End = _length };
        }
        else _result = new TrimResult { Start = _bar.Start, End = _bar.End, Length = _length, Position = _bar.Position };
        Close();
    }

    public static TrimResult? Edit(Window owner, MediaClipElement clip, string path, Mode mode, ImageSource? poster = null)
    {
        var dialog = new TrimDialog(clip, path, mode, poster) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
