using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Rendering;
using Stagecraft.Interop;
using Stagecraft.Services;
using Stagecraft.Themes;

namespace Stagecraft.Views.Show;

/// <summary>
/// The presenter view: the current slide (draw on it), the next slide, speaker notes with adjustable size, the
/// elapsed time and the clock, a slide grid to jump anywhere, and the show tools. Always dark.
/// </summary>
public sealed class PresenterWindow : FluentWindow
{
    private readonly SlideShowController _controller;
    private readonly ShowSurface _surface = new();
    private readonly Image _next = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _nextLabel = new();
    private readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap, LineHeight = double.NaN };
    private readonly TextBlock _timer = new() { FontSize = 30, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _clock = new() { FontSize = 18 };
    private readonly TextBlock _position = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _status = new() { FontSize = 13 };
    private readonly Grid _gridOverlay = new() { Visibility = Visibility.Collapsed };
    private readonly WrapPanel _gridPanel = new() { Margin = new Thickness(24) };
    private readonly Dictionary<ShowTool, ToggleButton> _tools = new();
    private readonly ToggleButton _blackButton;
    private readonly ToggleButton _whiteButton;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Native.MonitorInfo? _monitor;
    private double _notesSize;
    private bool _closingFromShow;

    internal PresenterWindow(SlideShowController controller, Native.MonitorInfo? monitor, bool windowed)
    {
        _controller = controller;
        _monitor = windowed ? null : monitor;
        ForceDark = true;
        Resources.MergedDictionaries.Add(ThemeManager.BuildDictionary(dark: true));
        Title = windowed ? "Presenter view (test window)" : "Presenter view – Stagecraft";
        Width = windowed ? 1280 : 1400;
        Height = windowed ? 800 : 860;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (windowed)
        {
            var work = SystemParameters.WorkArea;
            Left = SlideShow.WindowedOrigin?.X ?? work.Left + 80;
            Top = (SlideShow.WindowedOrigin?.Y + 600) ?? work.Top + 80;
        }
        _notesSize = AppSettings.Current.PresenterNotesSize;

        var root = new Grid { Margin = new Thickness(18, 6, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---------------------------------------------------------- top bar
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var end = new Button { Content = "End show", MinWidth = 110, Height = 34, VerticalAlignment = VerticalAlignment.Center };
        end.SetResourceReference(StyleProperty, "AccentButton");
        end.Click += (_, _) => _controller.End();
        AutomationProperties.SetName(end, "End show");
        DockPanel.SetDock(end, Dock.Right);
        top.Children.Add(end);
        _clock.Margin = new Thickness(0, 0, 22, 0);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        DockPanel.SetDock(_clock, Dock.Right);
        top.Children.Add(_clock);

        var timerPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        timerPanel.Children.Add(MakeIcon("Icon.Timer", 22, new Thickness(0, 0, 10, 0)));
        timerPanel.Children.Add(_timer);
        var pause = ToolButton("Icon.Pause", "Pause or resume the timer", () => _controller.ToggleTimer());
        pause.Margin = new Thickness(12, 0, 0, 0);
        timerPanel.Children.Add(pause);
        timerPanel.Children.Add(ToolButton("Icon.ArrowReset", "Restart the timer", () => _controller.ResetTimer()));
        DockPanel.SetDock(timerPanel, Dock.Left);
        top.Children.Add(timerPanel);
        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _position.HorizontalAlignment = HorizontalAlignment.Center;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Text.Accent");
        middle.Children.Add(_position);
        middle.Children.Add(_status);
        top.Children.Add(middle);
        root.Children.Add(top);

        // ---------------------------------------------------------- main
        var main = new Grid();
        Grid.SetRow(main, 1);
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });

        var left = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        tools.Children.Add(ToolButton("Icon.ChevronLeft", "Previous (←, Page Up)", _controller.PreviousStep));
        tools.Children.Add(ToolButton("Icon.ChevronRight", "Next (→, Page Down, Space)", _controller.NextStep));
        tools.Children.Add(Divider());
        foreach (var (tool, icon, tip) in new[]
                 {
                     (ShowTool.Pointer, "Icon.Cursor", "Pointer (Ctrl+A)"), (ShowTool.Laser, "Icon.LaserTool", "Laser pointer (Ctrl+L)"),
                     (ShowTool.Pen, "Icon.Pen", "Pen (Ctrl+P)"), (ShowTool.Highlighter, "Icon.Highlight", "Highlighter (Ctrl+I)"), (ShowTool.Eraser, "Icon.Eraser", "Eraser (Ctrl+E)"),
                 })
        {
            var t = tool;
            var toggle = ToolToggle(icon, tip, () => _controller.SetTool(t));
            _tools[tool] = toggle;
            tools.Children.Add(toggle);
        }
        tools.Children.Add(ToolButton("Icon.Delete", "Erase all ink on this slide (E)", _controller.ClearInk));
        tools.Children.Add(Divider());
        _blackButton = ToolToggle("Icon.SquareFilled", "Black screen (B)", () => _controller.SetBlank(BlankMode.Black));
        _whiteButton = ToolToggle("Icon.Square", "White screen (W)", () => _controller.SetBlank(BlankMode.White));
        tools.Children.Add(_blackButton);
        tools.Children.Add(_whiteButton);
        tools.Children.Add(Divider());
        tools.Children.Add(ToolButton("Icon.Grid", "All slides (G)", ToggleGrid));
        DockPanel.SetDock(tools, Dock.Bottom);
        left.Children.Add(tools);
        var currentFrame = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _surface, BorderThickness = new Thickness(1) };
        currentFrame.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        var ratio = controller.Session.Deck.Width / controller.Session.Deck.Height;
        left.Children.Add(new AspectPanel { Ratio = ratio, Children = { currentFrame } });
        main.Children.Add(left);

        var right = new DockPanel();
        Grid.SetColumn(right, 2);
        var nextHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _nextLabel.Text = "Next";
        _nextLabel.FontWeight = FontWeights.SemiBold;
        _nextLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        nextHeader.Children.Add(_nextLabel);
        DockPanel.SetDock(nextHeader, Dock.Top);
        right.Children.Add(nextHeader);
        var nextFrame = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, BorderThickness = new Thickness(1), Child = _next, Background = Brushes.Black };
        nextFrame.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        var nextBox = new AspectPanel { Ratio = ratio, AlignTop = true, MaxHeight = 320, Children = { nextFrame } };
        DockPanel.SetDock(nextBox, Dock.Top);
        right.Children.Add(nextBox);
        var notesHeader = new DockPanel { Margin = new Thickness(0, 22, 0, 8) };
        var sizes = new StackPanel { Orientation = Orientation.Horizontal };
        sizes.Children.Add(ToolButton("Icon.ZoomOut", "Smaller notes", () => SetNotesSize(-2)));
        sizes.Children.Add(ToolButton("Icon.ZoomIn", "Bigger notes", () => SetNotesSize(2)));
        DockPanel.SetDock(sizes, Dock.Right);
        notesHeader.Children.Add(sizes);
        var notesLabel = new TextBlock { Text = "Notes", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        notesLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        notesHeader.Children.Add(notesLabel);
        DockPanel.SetDock(notesHeader, Dock.Top);
        right.Children.Add(notesHeader);
        var notesCard = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(1) };
        notesCard.SetResourceReference(Border.BackgroundProperty, "Card.Background");
        notesCard.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        notesCard.Child = new ScrollViewer { Content = _notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        AutomationProperties.SetName(_notes, "Speaker notes");
        right.Children.Add(notesCard);
        main.Children.Add(right);

        // Slide grid overlay
        _gridOverlay.SetResourceReference(Panel.BackgroundProperty, "Window.Solid");
        Grid.SetColumnSpan(_gridOverlay, 3);
        _gridOverlay.Children.Add(new ScrollViewer { Content = _gridPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        main.Children.Add(_gridOverlay);
        root.Children.Add(main);
        Content = root;

        _surface.Attach(controller, acceptsInk: true);
        _surface.MouseLeftButtonUp += (_, _) =>
        {
            if (_controller.Tool is ShowTool.Pointer) _controller.NextStep();
        };
        controller.SlideChanged += (_, _, _) => Update();
        controller.StepAdvanced += _ => Update();
        controller.StepReset += Update;
        controller.BlankChanged += Update;
        controller.ToolChanged += Update;
        controller.DigitsChanged += Update;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.None)
            {
                ToggleGrid();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && _gridOverlay.Visibility == Visibility.Visible)
            {
                _gridOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (_controller.HandleKey(e)) e.Handled = true;
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (_gridOverlay.Visibility == Visibility.Visible || _notes.IsMouseOver) return;
            if (e.Delta < 0) _controller.NextStep();
            else _controller.PreviousStep();
            e.Handled = true;
        };
        _tick.Tick += (_, _) => UpdateClock();
        _tick.Start();
        Closing += (_, _) =>
        {
            _tick.Stop();
            if (!_closingFromShow) _controller.End();
        };
        Update();
        UpdateClock();
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
        if (_monitor == null) return;
        var w = _monitor.WorkArea;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOP, w.Left, w.Top, w.Width, w.Height, Native.SWP_SHOWWINDOW);
        WindowState = WindowState.Maximized;
    }

    // ------------------------------------------------------------------ pieces

    private IconView MakeIcon(string key, double size, Thickness margin)
    {
        var icon = new IconView { Width = size, Height = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center, Data = (Geometry)FindResource(key) };
        icon.SetResourceReference(IconView.ForegroundProperty, "Text.Secondary");
        return icon;
    }

    private Button ToolButton(string icon, string tip, Action action)
    {
        var b = new Button { ToolTip = tip, Width = 38, Height = 38, Focusable = false };
        b.SetResourceReference(StyleProperty, "IconButton");
        Ui.SetIcon(b, (Geometry)FindResource(icon));
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => action();
        return b;
    }

    private ToggleButton ToolToggle(string icon, string tip, Action action)
    {
        var t = new ToggleButton { ToolTip = tip, Width = 38, Height = 38, Focusable = false };
        t.SetResourceReference(StyleProperty, "IconToggle");
        Ui.SetIcon(t, (Geometry)FindResource(icon));
        AutomationProperties.SetName(t, tip);
        t.Click += (_, _) => action();
        return t;
    }

    private static Border Divider()
    {
        var d = new Border { Width = 1, Height = 22, Margin = new Thickness(10, 0, 10, 0) };
        d.SetResourceReference(Border.BackgroundProperty, "Divider");
        return d;
    }

    private void SetNotesSize(double delta)
    {
        _notesSize = MathEx.Clamp(_notesSize + delta, 12, 48);
        _notes.FontSize = _notesSize;
        var size = _notesSize;
        AppSettings.Update(s => s.PresenterNotesSize = size);
    }

    private void UpdateClock()
    {
        var t = _controller.Elapsed;
        _timer.Text = $"{(int)t.TotalHours:0}:{t.Minutes:00}:{t.Seconds:00}";
        _timer.Opacity = _controller.TimerRunning ? 1 : 0.5;
        _clock.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
    }

    private void Update()
    {
        var c = _controller;
        var count = c.Slides.Count;
        _position.Text = c.AtEnd ? "End of the show" : $"Slide {c.Index + 1} of {count}";
        var steps = c.StepCount(c.Current);
        var status = new List<string>();
        if (c.Digits.Length > 0) status.Add($"Go to slide {c.Digits}…  (Enter)");
        if (steps > 0 && !c.AtEnd) status.Add($"Animation {c.Step} of {steps}");
        if (c.Blank != BlankMode.None) status.Add(c.Blank == BlankMode.Black ? "Black screen" : "White screen");
        if (c.Options.Rehearse) status.Add("Rehearsing: timings are recorded");
        _status.Text = string.Join("   ·   ", status);

        _notes.FontSize = _notesSize;
        _notes.Text = c.Current?.Notes is { Length: > 0 } notes ? notes : "No notes for this slide.";
        _notes.Opacity = c.Current?.Notes is { Length: > 0 } ? 1 : 0.5;

        var nextSlide = c.Step < steps ? c.Current : c.Next;
        _nextLabel.Text = c.Step < steps ? "Next: animation on this slide" : c.Next != null ? $"Next: slide {c.Index + 2}" : "Next: end of the show";
        _next.Source = nextSlide != null ? SlideRenderer.Render(nextSlide, c.Context, 640) : null;

        foreach (var (tool, toggle) in _tools) toggle.IsChecked = c.Tool == tool;
        _blackButton.IsChecked = c.Blank == BlankMode.Black;
        _whiteButton.IsChecked = c.Blank == BlankMode.White;
        if (_gridOverlay.Visibility == Visibility.Visible) BuildGrid();
    }

    private void ToggleGrid()
    {
        if (_gridOverlay.Visibility == Visibility.Visible)
        {
            _gridOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        BuildGrid();
        _gridOverlay.Visibility = Visibility.Visible;
    }

    private void BuildGrid()
    {
        _gridPanel.Children.Clear();
        for (var i = 0; i < _controller.Slides.Count; i++)
        {
            var index = i;
            var slide = _controller.Slides[i];
            var image = new Image { Source = SlideRenderer.Render(slide, _controller.Context, 360), Width = 220, Stretch = Stretch.Uniform };
            var frame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(i == _controller.Index ? 3 : 1), Child = image };
            frame.SetResourceReference(Border.BorderBrushProperty, i == _controller.Index ? "Accent.Fill" : "Card.Stroke");
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(new TextBlock { Text = $"{i + 1}  {slide.TitleText}", Margin = new Thickness(2, 6, 0, 0), Width = 220, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Button { Content = stack, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(6) };
            b.SetResourceReference(StyleProperty, "CardButton");
            AutomationProperties.SetName(b, $"Go to slide {i + 1}");
            b.Click += (_, _) =>
            {
                _gridOverlay.Visibility = Visibility.Collapsed;
                _controller.GoTo(index);
            };
            _gridPanel.Children.Add(b);
        }
    }
}
