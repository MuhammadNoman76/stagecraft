using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views;

/// <summary>Edit a chart's data in a small grid (or paste it from a spreadsheet) with a live, themed preview.</summary>
public sealed class ChartEditorDialog : FluentWindow
{
    private readonly ChartElement _chart;
    private readonly SlideStyle _style;
    private readonly CellGrid _grid = new();
    private readonly ChartPreview _preview;
    private readonly TextBlock _hint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private bool _ok;

    private ChartEditorDialog(ChartElement chart, SlideStyle style)
    {
        _chart = DeckJson.Clone(chart);
        _style = style;
        Title = "Chart";
        Width = 1040;
        Height = 640;
        MinWidth = 800;
        MinHeight = 480;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _preview = new ChartPreview(_chart, style);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid { Margin = new Thickness(24, 18, 24, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });

        var left = new DockPanel();
        var title = new TextBlock { Text = "Chart data" };
        title.SetResourceReference(StyleProperty, "Text.Subtitle");
        DockPanel.SetDock(title, Dock.Top);
        left.Children.Add(title);
        _hint.Margin = new Thickness(0, 4, 0, 12);
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        DockPanel.SetDock(_hint, Dock.Top);
        left.Children.Add(_hint);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Small("Add row", "Icon.TableInsertRow", () => _grid.AddRow()));
        buttons.Children.Add(Small("Remove row", "Icon.TableDeleteRow", () => _grid.RemoveRow()));
        buttons.Children.Add(Small("Add series", "Icon.TableInsertColumn", () => _grid.AddColumn()));
        buttons.Children.Add(Small("Remove series", "Icon.TableDeleteColumn", () => _grid.RemoveColumn()));
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new ScrollViewer { Content = _grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        body.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        var previewFrame = new Border { Height = 280, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), BorderThickness = new Thickness(1), Child = _preview };
        previewFrame.Background = new SolidColorBrush(style.Resolve("bg"));
        previewFrame.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        right.Children.Add(previewFrame);
        var typeTray = new Border { Margin = new Thickness(0, 16, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };
        typeTray.SetResourceReference(StyleProperty, "SegmentTray");
        var types = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (kind, name, icon) in new[] { (ChartKind.Bar, "Bars", "Icon.DataBarVertical"), (ChartKind.Line, "Line", "Icon.DataLine"), (ChartKind.Pie, "Pie", "Icon.DataPie"), (ChartKind.Donut, "Donut", "Icon.DataUsage") })
        {
            var rb = new RadioButton { GroupName = "kind", IsChecked = _chart.Kind == kind, Content = name, Padding = new Thickness(14, 3, 14, 4) };
            rb.SetResourceReference(StyleProperty, "TextSegment");
            AutomationProperties.SetName(rb, name + " chart");
            var k = kind;
            rb.Checked += (_, _) =>
            {
                _chart.Kind = k;
                UpdateHint();
                _preview.InvalidateVisual();
            };
            types.Children.Add(rb);
        }
        typeTray.Child = types;
        right.Children.Add(typeTray);
        right.Children.Add(Toggle("Legend", _chart.ShowLegend, v => _chart.ShowLegend = v));
        right.Children.Add(Toggle("Values on the chart", _chart.ShowValues, v => _chart.ShowValues = v));
        right.Children.Add(Toggle("Grid lines", _chart.ShowGrid, v => _chart.ShowGrid = v));
        body.Children.Add(right);
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = new Button { Content = "Done", MinWidth = 120, IsDefault = false };
        done.SetResourceReference(StyleProperty, "AccentButton");
        done.Click += (_, _) =>
        {
            ReadGrid();
            _ok = true;
            Close();
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        footerButtons.Children.Add(done);
        footerButtons.Children.Add(cancel);
        footer.Child = footerButtons;
        root.Children.Add(footer);
        Content = root;

        var data = new List<IReadOnlyList<string>> { new[] { "" }.Concat(_chart.Series.Select(s => s.Name)).ToList() };
        var count = Math.Max(_chart.Categories.Count, _chart.Series.Count == 0 ? 0 : _chart.Series.Max(s => s.Values.Count));
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var row = new List<string> { i < _chart.Categories.Count ? _chart.Categories[i] : "" };
            foreach (var s in _chart.Series) row.Add(i < s.Values.Count ? s.Values[i].ToString("0.###", CultureInfo.CurrentCulture) : "");
            data.Add(row);
        }
        _grid.Load(data);
        _grid.Changed += () =>
        {
            ReadGrid();
            _preview.InvalidateVisual();
        };
        UpdateHint();
        Loaded += (_, _) => _grid.FocusCell(1, 1);
    }

    private Button Small(string text, string icon, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
        b.SetResourceReference(StyleProperty, "IconTextButton");
        Ui.SetIcon(b, (Geometry)FindResource(icon));
        b.Click += (_, _) => action();
        return b;
    }

    private CheckBox Toggle(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 4) };
        c.Click += (_, _) =>
        {
            set(c.IsChecked == true);
            _preview.InvalidateVisual();
        };
        return c;
    }

    private void UpdateHint()
    {
        _hint.Text = _chart.Kind is ChartKind.Pie or ChartKind.Donut
            ? "One value per row (the first series). Type, or paste rows from a spreadsheet."
            : "Categories down the first column, one series per column. Type, or paste rows from a spreadsheet.";
    }

    private void ReadGrid()
    {
        var data = _grid.Read();
        if (data.Count == 0) return;
        var header = data[0];
        var rows = data.Skip(1).Where(r => r.Any(c => c.Length > 0)).ToList();
        _chart.Categories = rows.Select(r => r.Count > 0 ? r[0] : "").ToList();
        _chart.Series = new List<ChartSeries>();
        for (var c = 1; c < header.Count; c++)
        {
            if (rows.All(r => c >= r.Count || r[c].Length == 0) && header[c].Length == 0) continue;
            _chart.Series.Add(new ChartSeries
            {
                Name = header[c].Length > 0 ? header[c] : $"Series {c}",
                Values = rows.Select(r => c < r.Count ? Parse(r[c]) : 0).ToList(),
            });
        }
    }

    private static double Parse(string text)
    {
        var t = text.Trim().Replace("%", "").Replace("$", "").Replace("€", "").Replace("£", "");
        if (double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v)) return v;
        return 0;
    }

    /// <summary>Returns the edited chart, or null when cancelled.</summary>
    public static ChartElement? Edit(Window owner, ChartElement chart, SlideStyle style)
    {
        var dialog = new ChartEditorDialog(chart, style) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok ? dialog._chart : null;
    }

    /// <summary>Draws the chart with the slide's theme.</summary>
    private sealed class ChartPreview : FrameworkElement
    {
        private readonly ChartElement _chart;
        private readonly SlideStyle _style;

        public ChartPreview(ChartElement chart, SlideStyle style)
        {
            _chart = chart;
            _style = style;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 10 || ActualHeight < 10) return;
            // Draw at slide scale (so text sizes match the slide) and shrink to fit.
            var w = 1100.0;
            var h = w * ActualHeight / ActualWidth;
            var k = ActualWidth / w;
            dc.PushTransform(new ScaleTransform(k, k));
            ChartRenderer.Draw(dc, _chart, new Rect(0, 0, w, h), _style);
            dc.Pop();
        }
    }
}
