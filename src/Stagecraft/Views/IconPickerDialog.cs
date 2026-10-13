using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views;

/// <summary>Search and pick one of the bundled Fluent icons; shown in the deck's accent colour.</summary>
public sealed class IconPickerDialog : FluentWindow
{
    private readonly TextBox _search = new();
    private readonly WrapPanel _grid = new() { Margin = new Thickness(18, 4, 18, 18) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _count = new() { FontSize = 12 };
    private readonly Brush _iconBrush;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private string? _picked;
    private string? _current;

    private IconPickerDialog(string? current, SlideStyle style)
    {
        _current = current;
        Title = "Icons";
        Width = 760;
        Height = 640;
        MinWidth = 520;
        MinHeight = 420;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var accent = style.Resolve("accent1");
        // Keep the icons readable on the app's own background.
        _iconBrush = new SolidColorBrush(accent);

        var root = new DockPanel();
        var top = new StackPanel { Margin = new Thickness(18, 16, 18, 8) };
        var title = new TextBlock { Text = "Insert an icon" };
        title.SetResourceReference(StyleProperty, "Text.Subtitle");
        top.Children.Add(title);
        _search.SetResourceReference(StyleProperty, "SearchBox");
        _search.Margin = new Thickness(0, 12, 0, 6);
        Ui.SetPlaceholder(_search, "Search: rocket, team, growth, idea, money…");
        AutomationProperties.SetName(_search, "Search icons");
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _grid.Children.Count > 0)
            {
                ((Button)_grid.Children[0]).Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _grid.Children.Count > 0)
            {
                Pick((string)((Button)_grid.Children[0]).Tag);
                e.Handled = true;
            }
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Fill();
        };
        top.Children.Add(_search);
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        top.Children.Add(_count);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var credit = new TextBlock { Text = "Fluent UI System Icons by Microsoft (MIT licence). They take the theme's colours on the slide.", FontSize = 11, Margin = new Thickness(18, 4, 18, 12) };
        credit.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        DockPanel.SetDock(credit, Dock.Bottom);
        root.Children.Add(credit);
        _scroll.Content = _grid;
        root.Children.Add(_scroll);
        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Loaded += (_, _) =>
        {
            Fill();
            _search.Focus();
        };
    }

    private void Fill()
    {
        _grid.Children.Clear();
        var results = IconCatalog.Search(_search.Text);
        _count.Text = results.Count == IconCatalog.All.Count ? $"{results.Count} icons" : results.Count == 1 ? "1 icon" : $"{results.Count} icons";
        foreach (var info in results.Take(600))
        {
            var geometry = IconCatalog.Geometry(info.Key);
            if (geometry == null) continue;
            var path = new System.Windows.Shapes.Path { Data = geometry, Fill = _iconBrush, Width = 28, Height = 28, Stretch = Stretch.Uniform };
            var button = new Button { Content = path, Width = 56, Height = 56, Margin = new Thickness(2), Tag = info.Key, ToolTip = info.Name };
            button.SetResourceReference(StyleProperty, "SubtleButton");
            if (info.Key == _current) button.SetResourceReference(BackgroundProperty, "Accent.Selection");
            AutomationProperties.SetName(button, info.Name);
            button.Click += (_, _) => Pick(info.Key);
            _grid.Children.Add(button);
        }
    }

    private void Pick(string key)
    {
        _picked = key;
        Close();
    }

    public static string? Pick(Window owner, string? current, SlideStyle style)
    {
        var dialog = new IconPickerDialog(current, style) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
