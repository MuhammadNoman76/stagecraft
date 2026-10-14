using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Find and replace (Ctrl+F, Ctrl+H): a small card over the top right of the slide. Searches slide text, tables and,
/// optionally, speaker notes across the whole presentation. Enter goes to the next match, Shift+Enter to the previous.
/// </summary>
public sealed class FindBar : Border
{
    private readonly TextBox _find = new() { MinWidth = 200 };
    private readonly TextBox _replace = new() { MinWidth = 200 };
    private readonly TextBlock _count = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), MinWidth = 54, TextAlignment = TextAlignment.Right };
    private readonly ToggleButton _matchCase;
    private readonly ToggleButton _wholeWord;
    private readonly ToggleButton _notes;
    private readonly ToggleButton _expand;
    private readonly Grid _replaceRow = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer _searchTimer;

    public FindBar()
    {
        Visibility = Visibility.Collapsed;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 12, 16, 0);
        SetResourceReference(BackgroundProperty, "Flyout.Background");
        SetResourceReference(BorderBrushProperty, "Flyout.Stroke");
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.18 };
        AutomationProperties.SetName(this, "Find and replace");

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            SearchChanged?.Invoke();
        };

        _expand = Toggle("Icon.ChevronRight", "Replace (Ctrl+H)", false);
        _expand.Click += (_, _) => SetReplaceVisible(_expand.IsChecked == true);
        _matchCase = Toggle("Icon.TextCaseTitle", "Match case", false);
        _wholeWord = Toggle("Icon.TextWholeWord", "Whole words only", false);
        _notes = Toggle("Icon.Note", "Also search speaker notes", true);
        foreach (var t in new[] { _matchCase, _wholeWord, _notes }) t.Click += (_, _) => SearchChanged?.Invoke();

        Ui.SetPlaceholder(_find, "Find");
        AutomationProperties.SetName(_find, "Find");
        _find.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _find.PreviewKeyDown += OnKey;
        Ui.SetPlaceholder(_replace, "Replace with");
        AutomationProperties.SetName(_replace, "Replace with");
        _replace.PreviewKeyDown += OnKey;

        var top = new DockPanel();
        top.Children.Add(_expand);
        DockPanel.SetDock(_expand, Dock.Left);
        var close = Button("Icon.Dismiss", "Close (Esc)", () => Closed?.Invoke());
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        foreach (var b in new UIElement[]
                 {
                     _notes, _wholeWord, _matchCase,
                     new Border { Width = 1, Margin = new Thickness(4, 6, 4, 6) }.WithResource(Border.BackgroundProperty, "Divider"),
                     Button("Icon.ChevronDown", "Next match (Enter)", () => Navigate?.Invoke(1)),
                     Button("Icon.ChevronUp", "Previous match (Shift+Enter)", () => Navigate?.Invoke(-1)),
                 })
        {
            DockPanel.SetDock(b, Dock.Right);
            top.Children.Add(b);
        }
        DockPanel.SetDock(_count, Dock.Right);
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        top.Children.Add(_count);
        _find.Margin = new Thickness(4, 0, 0, 0);
        _find.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_find);

        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition());
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _replaceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_replace, 1);
        _replaceRow.Children.Add(_replace);
        var one = new Button { Content = "Replace", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
        one.Click += (_, _) => ReplaceOne?.Invoke();
        Grid.SetColumn(one, 2);
        _replaceRow.Children.Add(one);
        var all = new Button { Content = "Replace all", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
        all.Click += (_, _) => ReplaceAll?.Invoke();
        Grid.SetColumn(all, 3);
        _replaceRow.Children.Add(all);
        _replaceRow.Visibility = Visibility.Collapsed;

        var stack = new StackPanel { Width = 460 };
        stack.Children.Add(top);
        stack.Children.Add(_replaceRow);
        Child = stack;
    }

    /// <summary>The text or the options changed: search again.</summary>
    public event Action? SearchChanged;

    /// <summary>Go to the next (+1) or previous (-1) match.</summary>
    public event Action<int>? Navigate;

    public event Action? ReplaceOne;
    public event Action? ReplaceAll;
    public event Action? Closed;

    public string Query => _find.Text;
    public string Replacement => _replace.Text;
    public bool IsOpen => Visibility == Visibility.Visible;

    public FindOptions Options => new()
    {
        MatchCase = _matchCase.IsChecked == true,
        WholeWord = _wholeWord.IsChecked == true,
        IncludeNotes = _notes.IsChecked == true,
    };

    /// <summary>Shows the bar (with the replace row for Ctrl+H) and puts the keyboard in the search box.</summary>
    public void Open(bool replace, string? initial)
    {
        Visibility = Visibility.Visible;
        if (!string.IsNullOrEmpty(initial) && initial!.IndexOf('\n') < 0 && initial.Length <= 200) _find.Text = initial;
        SetReplaceVisible(replace || _replaceRow.Visibility == Visibility.Visible);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var box = replace && _find.Text.Length > 0 ? _replace : _find;
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }), DispatcherPriority.Input);
        SearchChanged?.Invoke();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _searchTimer.Stop();
    }

    /// <summary>"3 of 12", "No results" or nothing.</summary>
    public void ShowCount(int current, int total)
    {
        _count.Text = _find.Text.Length == 0 ? "" : total == 0 ? "No results" : current < 0 ? $"{total} found" : $"{current + 1} of {total}";
    }


    /// <summary>Screenshots: fills in the boxes as if typed.</summary>
    internal void SetForCapture(string find, string replace)
    {
        _find.Text = find;
        _replace.Text = replace;
        SetReplaceVisible(true);
        SearchChanged?.Invoke();
    }
    private void SetReplaceVisible(bool on)
    {
        _replaceRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _expand.IsChecked = on;
        Ui.SetIcon(_expand, (Geometry)FindResource(on ? "Icon.ChevronDown" : "Icon.ChevronRight"));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when sender == _replace && (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                ReplaceAll?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter when sender == _replace:
                ReplaceOne?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter:
                _searchTimer.Stop();
                Navigate?.Invoke((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true;
                break;
            case Key.F3:
                Navigate?.Invoke((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape:
                Closed?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private Button Button(string icon, string tip, Action click)
    {
        var b = new Button { ToolTip = tip, Width = 30, Height = 30 };
        b.SetResourceReference(StyleProperty, "IconButton");
        Ui.SetIcon(b, (Geometry)FindResource(icon));
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private ToggleButton Toggle(string icon, string tip, bool on)
    {
        var t = new ToggleButton { ToolTip = tip, IsChecked = on, Width = 30, Height = 30 };
        t.SetResourceReference(StyleProperty, "IconToggle");
        Ui.SetIcon(t, (Geometry)FindResource(icon));
        AutomationProperties.SetName(t, tip);
        return t;
    }
}

internal static class FrameworkElementExtensions
{
    public static T WithResource<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
