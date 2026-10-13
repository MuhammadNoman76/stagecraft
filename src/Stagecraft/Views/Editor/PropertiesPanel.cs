using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The contextual panel on the right: only what applies to the selection (text, fill, border, picture, chart,
/// table, icon, arrange, animation), the slide (layout, background, transition) when nothing is selected, or the
/// design (themes, palettes, slide size).
/// </summary>
public sealed partial class PropertiesPanel : UserControl
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly StackPanel _stack = new() { Margin = new Thickness(18, 4, 18, 24) };
    private EditorView? _editor;
    private EditorState? _state;
    private readonly DispatcherTimer _rebuildTimer;
    private Action? _refreshNumbers;

    public PropertiesPanel()
    {
        Focusable = false;
        _scroll.Content = _stack;
        Content = _scroll;
        AutomationProperties.SetName(this, "Properties");
        _rebuildTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            RebuildNow();
        };
    }

    /// <summary>Shows themes, palettes and slide size instead of the selection's properties.</summary>
    public bool DesignMode { get; set; }

    public void Attach(EditorView editor, EditorState state)
    {
        _editor = editor;
        _state = state;
        state.SelectionChanged += Rebuild;
        state.CurrentSlideChanged += Rebuild;
        state.LiveChange += () => _refreshNumbers?.Invoke();
        RebuildNow();
    }

    /// <summary>Rebuilds soon (several changes in a row rebuild once).</summary>
    public void Rebuild()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    public void RebuildNow()
    {
        if (_editor == null || _state == null) return;
        // Don't rebuild under the user's fingers while they type in one of our boxes.
        if (_stack.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox { IsReadOnly: false } box && box is not NumberBox && box.Tag as string != "rebuild")
        {
            return;
        }
        var offset = _scroll.VerticalOffset;
        _stack.Children.Clear();
        _refreshNumbers = null;
        try
        {
            if (DesignMode) BuildDesign();
            else if (_state.CurrentSlide == null) Empty();
            else if (_state.Selection.Count == 0) BuildSlide();
            else BuildElements();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Properties panel");
        }
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Empty()
    {
        _stack.Children.Add(new TextBlock { Text = "No slides yet. Add one with New slide.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
    }

    // ------------------------------------------------------------------ building blocks

    private SlideStyle CurrentStyle => _state!.Style ?? SlideStyle.For(_state.Deck, new Slide());

    private void Title(string text, string? subtitle = null)
    {
        var t = new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _stack.Children.Add(t);
        if (subtitle != null)
        {
            var s = new TextBlock { Text = subtitle, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            s.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _stack.Children.Add(s);
        }
    }

    private StackPanel Section(string header)
    {
        var divider = new Border { Height = 1, Margin = new Thickness(-18, 18, -18, 14) };
        divider.SetResourceReference(Border.BackgroundProperty, "Divider");
        _stack.Children.Add(divider);
        var h = new TextBlock { Text = header };
        h.SetResourceReference(StyleProperty, "PanelHeader");
        _stack.Children.Add(h);
        var body = new StackPanel();
        _stack.Children.Add(body);
        return body;
    }

    private static Grid Row(Panel parent, string label, UIElement control, double labelWidth = 84)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label };
        l.SetResourceReference(StyleProperty, "PanelLabel");
        grid.Children.Add(l);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (control is FrameworkElement fe && string.IsNullOrEmpty(AutomationProperties.GetName(fe))) AutomationProperties.SetName(fe, label);
        parent.Children.Add(grid);
        return grid;
    }

    private Button IconButton(string icon, string tip, Action click)
    {
        var b = new Button { ToolTip = tip };
        b.SetResourceReference(StyleProperty, "IconButton");
        Ui.SetIcon(b, (Geometry)FindResource(icon));
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private Button TextButton(string text, string? icon, Action click, bool accent = false)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
        if (icon != null)
        {
            b.SetResourceReference(StyleProperty, "IconTextButton");
            Ui.SetIcon(b, (Geometry)FindResource(icon));
        }
        else if (accent) b.SetResourceReference(StyleProperty, "AccentButton");
        b.Click += (_, _) => click();
        return b;
    }

    private ToggleButton IconToggle(string icon, string tip, bool? isChecked, Action<bool> changed)
    {
        var t = new ToggleButton { ToolTip = tip, IsChecked = isChecked, IsThreeState = false };
        t.SetResourceReference(StyleProperty, "IconToggle");
        Ui.SetIcon(t, (Geometry)FindResource(icon));
        AutomationProperties.SetName(t, tip);
        t.Click += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    private CheckBox Switch(Panel parent, string label, bool value, Action<bool> changed, string? hint = null)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var sw = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        sw.SetResourceReference(StyleProperty, "ToggleSwitch");
        AutomationProperties.SetName(sw, label);
        sw.Click += (_, _) => changed(sw.IsChecked == true);
        DockPanel.SetDock(sw, Dock.Right);
        dock.Children.Add(sw);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (hint != null)
        {
            var h = new TextBlock { Text = hint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
            text.Children.Add(h);
        }
        dock.Children.Add(text);
        parent.Children.Add(dock);
        return sw;
    }

    private NumberBox Number(double value, double min, double max, double step, Action<double> changed, string suffix = "", int decimals = 0)
    {
        var n = new NumberBox { Minimum = min, Maximum = max, Step = step, Suffix = suffix, Decimals = decimals, Value = value };
        n.ValueChanged += changed;
        return n;
    }

    private Slider Slider(double value, double min, double max, Action<double> changed, Action? committed = null)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, IsMoveToPointEnabled = true };
        s.ValueChanged += (_, e) =>
        {
            if (s.IsLoaded) changed(e.NewValue);
        };
        if (committed != null)
        {
            s.PreviewMouseLeftButtonUp += (_, _) => committed();
            s.LostKeyboardFocus += (_, _) => committed();
        }
        return s;
    }

    private ComboBox Combo<T>(IEnumerable<(T Value, string Text)> items, T selected, Action<T> changed, string name)
    {
        var combo = new ComboBox();
        AutomationProperties.SetName(combo, name);
        var list = items.ToList();
        foreach (var (_, text) in list) combo.Items.Add(new ComboBoxItem { Content = text });
        var index = list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected));
        combo.SelectedIndex = index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) changed(list[combo.SelectedIndex].Value);
        };
        return combo;
    }

    private Border Segments<T>(IEnumerable<(T Value, string Text, string? Icon)> items, T? selected, Action<T> changed, string name)
    {
        var tray = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        tray.SetResourceReference(StyleProperty, "SegmentTray");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var group = name + Guid.NewGuid().ToString("N");
        foreach (var (value, text, icon) in items)
        {
            var rb = new RadioButton { GroupName = group, IsChecked = EqualityComparer<T?>.Default.Equals(selected, value), ToolTip = text };
            AutomationProperties.SetName(rb, text);
            if (icon != null)
            {
                rb.SetResourceReference(StyleProperty, "IconSegment");
                Ui.SetIcon(rb, (Geometry)FindResource(icon));
            }
            else
            {
                rb.SetResourceReference(StyleProperty, "TextSegment");
                rb.Content = text;
            }
            var v = value;
            rb.Click += (_, _) => changed(v);
            row.Children.Add(rb);
        }
        tray.Child = row;
        AutomationProperties.SetName(tray, name);
        return tray;
    }

    /// <summary>A row of colour swatches: theme colours, then optional "none" and "custom".</summary>
    private WrapPanel Swatches(string? current, Action<string?> picked, bool allowNone, string name, bool includeText = true)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, -6, 2) };
        AutomationProperties.SetName(panel, name);
        var style = CurrentStyle;
        var refs = new List<string> { "accent1", "accent2", "accent3", "accent4", "accent5" };
        if (includeText) refs.AddRange(new[] { "text", "text2" });
        refs.AddRange(new[] { "bg", "bg2", "white", "black" });
        var names = new Dictionary<string, string>
        {
            ["accent1"] = "Accent 1", ["accent2"] = "Accent 2", ["accent3"] = "Accent 3", ["accent4"] = "Accent 4", ["accent5"] = "Accent 5",
            ["text"] = "Text", ["text2"] = "Muted text", ["bg"] = "Background", ["bg2"] = "Surface", ["white"] = "White", ["black"] = "Black",
        };
        var currentBase = current?.Split('@')[0];
        if (allowNone)
        {
            var none = SwatchButton(Brushes.Transparent, "None", currentBase == null || currentBase == "none" || currentBase == "transparent", () => picked(null));
            none.Content = new Border { Width = 16, Height = 2, Background = Brushes.IndianRed, RenderTransform = new RotateTransform(-45), RenderTransformOrigin = new Point(0.5, 0.5) };
            panel.Children.Add(none);
        }
        foreach (var r in refs)
        {
            var color = style.Resolve(r);
            panel.Children.Add(SwatchButton(new SolidColorBrush(color), names[r], string.Equals(currentBase, r, StringComparison.OrdinalIgnoreCase), () => picked(r)));
        }
        var isCustom = currentBase != null && currentBase.StartsWith("#", StringComparison.Ordinal);
        var customColor = isCustom ? style.Resolve(current) : Colors.Gray;
        var custom = SwatchButton(isCustom ? new SolidColorBrush(customColor) : RainbowBrush(), "Custom colour…", isCustom, () => { });
        custom.Click += (_, _) => ColorPicker.Show(custom, isCustom ? customColor : style.Resolve("accent1"), c => picked(ColorPicker.Hex(c)));
        panel.Children.Add(custom);
        return panel;
    }

    private static Brush RainbowBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x5E, 0x7E), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xC1, 0x45), 0.35));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xDC, 0x97), 0.65));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x6D, 0x4C, 0xFF), 1));
        b.Freeze();
        return b;
    }

    private Button SwatchButton(Brush brush, string tip, bool selected, Action click)
    {
        var b = new Button { Background = brush, ToolTip = tip, Tag = selected ? "selected" : null };
        b.SetResourceReference(StyleProperty, "Swatch");
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static UniformGrid Grid2(params UIElement[] children)
    {
        var g = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var c in children) g.Children.Add(c);
        return g;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
        var t = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(1, 0, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        sp.Children.Add(t);
        sp.Children.Add(control);
        AutomationProperties.SetName(control, label);
        return sp;
    }
}
