using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Stagecraft.Controls;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The animation pane: the slide's effects in order, grouped by click, with their timing on a small timeline; drag to
/// reorder, select one or more to change their options, add effects from the gallery, play from any point.
/// </summary>
public sealed partial class PropertiesPanel
{
    public const string EffectDragFormat = "Stagecraft.Effects";

    /// <summary>The colour that marks each kind of effect (entrances green, emphasis gold, exits red, motion blue).</summary>
    public static Color ClassColor(EffectClass c) => c switch
    {
        EffectClass.Entrance => Color.FromRgb(0x2F, 0xA8, 0x5A),
        EffectClass.Emphasis => Color.FromRgb(0xE0, 0x9B, 0x10),
        EffectClass.Exit => Color.FromRgb(0xE5, 0x48, 0x4D),
        EffectClass.Motion => Color.FromRgb(0x3B, 0x82, 0xF6),
        _ => Color.FromRgb(0x8B, 0x5C, 0xF6),
    };

    public static string ClassIcon(EffectClass c, EffectKind k) => c switch
    {
        EffectClass.Motion => k == EffectKind.Custom ? "Icon.HandDraw" : k == EffectKind.Circle ? "Icon.ArrowSync" : k == EffectKind.Arc ? "Icon.ArrowCurveUpRight" : "Icon.ArrowRight",
        EffectClass.Media => k == EffectKind.Pause ? "Icon.PauseFilled" : k == EffectKind.Stop ? "Icon.Stop" : "Icon.PlayFilled",
        _ => "Icon.StarFilled",
    };

    private Point _dragStart;
    private string? _dragArmed;

    private Button? _addAnimationButton;

    /// <summary>Opens the Add animation gallery from the pane (after the pane is built).</summary>
    public void OpenAddAnimation()
    {
        RebuildNow();
        if (_addAnimationButton is { IsEnabled: true } b && _editor != null)
        {
            b.UpdateLayout();
            AnimationGallery.Show(b, _editor, AnimationGallery.Purpose.Add);
        }
    }

    private void BuildAnimationPane()
    {
        var editor = _editor!;
        var state = _state!;
        var slide = state.CurrentSlide!;
        var timeline = SlideTimeline.Build(slide);
        var items = timeline.Groups.SelectMany(g => g.Items).Concat(timeline.Triggers.Values.SelectMany(t => t).SelectMany(g => g.Items)).ToList();
        var count = slide.Animations.Count;

        // Header.
        var header = new DockPanel { Margin = new Thickness(0, 6, -6, 0) };
        var play = IconButton("Icon.PlayFilled", "Play this slide's animations", () => editor.PreviewAnimations());
        play.Margin = new Thickness(0, 10, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(play, Dock.Right);
        header.Children.Add(play);
        var titles = new StackPanel();
        var t = new TextBlock { Text = "Animations", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        titles.Children.Add(t);
        var sub = new TextBlock
        {
            Text = count == 0 ? $"Slide {state.CurrentIndex + 1}" :
                $"Slide {state.CurrentIndex + 1} · {Plural(count, "effect")} · {(timeline.Clicks == 0 ? "no clicks" : Plural(timeline.Clicks, "click"))}",
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        titles.Children.Add(sub);
        header.Children.Add(titles);
        _stack.Children.Add(header);

        // Add animation, painter, play from the selected effect.
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var painter = IconButton("Icon.PaintBrush", "Animation painter: copy this element's animation to another", () => editor.StartAnimationPainter());
        painter.IsEnabled = state.SelectedElements.Count > 0 && AnimationRules.EffectsOf(slide, state.SelectedElements).Count > 0;
        DockPanel.SetDock(painter, Dock.Right);
        actions.Children.Add(painter);
        var from = IconButton("Icon.Play", "Play from the selected effect", () => editor.PlayFromSelected());
        from.IsEnabled = state.SelectedEffects.Count > 0;
        DockPanel.SetDock(from, Dock.Right);
        actions.Children.Add(from);
        var add = new Button { Content = "Add animation", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 6, 0) };
        add.SetResourceReference(StyleProperty, "AccentButton");
        add.IsEnabled = state.Selection.Count > 0;
        add.ToolTip = state.Selection.Count > 0 ? "Choose an effect for the selection" : "Select something on the slide first";
        AutomationProperties.SetName(add, "Add animation");
        add.Click += (_, _) => AnimationGallery.Show(add, editor, AnimationGallery.Purpose.Add);
        actions.Children.Add(add);
        _addAnimationButton = add;
        _stack.Children.Add(actions);

        if (count == 0)
        {
            var empty = new TextBlock
            {
                Text = state.Selection.Count > 0
                    ? "Nothing on this slide is animated yet. Choose Add animation to bring the selection in, make it stand out, take it away or move it along a path."
                    : "Nothing on this slide is animated yet. Select a title, a picture or any element, then choose Add animation.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Margin = new Thickness(2, 16, 0, 0),
                LineHeight = 19,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _stack.Children.Add(empty);
            return;
        }

        // The list, grouped by click, with a timeline.
        var listSection = Section("Order and timing");
        var span = Math.Max(2, items.Count == 0 ? 2 : items.Max(i => i.Begin + Math.Min(i.Active, 60)));
        span = Math.Ceiling(span);
        listSection.Children.Add(Ruler(span));
        var listHost = new StackPanel { AllowDrop = true, Background = Brushes.Transparent };
        listHost.DragOver += (_, e) => OnPaneDragOver(listHost, e);
        listHost.DragLeave += (_, _) => HideDropMarker(listHost);
        listHost.Drop += (_, e) => OnPaneDrop(listHost, e, slide);
        var order = slide.Animations.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);

        void Group(ClickGroup g, string label)
        {
            var h = new TextBlock { Text = label.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 4) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
            listHost.Children.Add(h);
            var first = true;
            foreach (var item in g.Items)
            {
                listHost.Children.Add(EffectRow(slide, item, first ? g.Click : (int?)null, span, order));
                first = false;
            }
        }
        foreach (var g in timeline.Groups) Group(g, g.Automatic ? "With the slide" : $"On click {g.Click}");
        foreach (var (trigger, groups) in timeline.Triggers)
        {
            var name = AnimationRules.Resolve(slide, trigger).FirstOrDefault()?.Describe ?? "an element";
            foreach (var g in groups) Group(g, groups.Count > 1 ? $"Click on {Short(name)} ({g.Click})" : $"Click on {Short(name)}");
        }
        listSection.Children.Add(listHost);
        var hint = new TextBlock { Text = "Drag to change the order. Ctrl+click to select several.", FontSize = 11.5, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        listSection.Children.Add(hint);

        var selected = state.SelectedEffectList;
        if (selected.Count > 0) BuildEffectOptions(slide, selected, timeline);
    }

    private static string Plural(int n, string word) => n == 1 ? "1 " + word : n + " " + word + "s";

    private static string Short(string s) => s.Length > 28 ? s.Substring(0, 27) + "…" : s;

    /// <summary>Seconds along the top of the timeline column.</summary>
    private FrameworkElement Ruler(double span)
    {
        var grid = new Grid { Height = 16, Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimelineWidth) });
        var canvas = new Canvas { Width = TimelineWidth, Height = 16, ClipToBounds = false };
        Grid.SetColumn(canvas, 1);
        var step = span <= 4 ? 1 : span <= 10 ? 2 : span <= 30 ? 5 : 10;
        for (var s = 0.0; s <= span + 0.001; s += step)
        {
            var x = s / span * (TimelineWidth - 4);
            var label = new TextBlock { Text = s.ToString("0", CultureInfo.CurrentCulture) + "s", FontSize = 9.5 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
            Canvas.SetLeft(label, x);
            canvas.Children.Add(label);
        }
        grid.Children.Add(canvas);
        var caption = new TextBlock { Text = "Effect", FontSize = 10.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(50, 0, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        grid.Children.Add(caption);
        return grid;
    }

    private const double TimelineWidth = 92;

    private FrameworkElement EffectRow(Slide slide, TimelineItem item, int? click, double span, Dictionary<string, int> order)
    {
        var editor = _editor!;
        var state = _state!;
        var a = item.Effect;
        var selected = state.SelectedEffects.Contains(a.Id);
        var targets = AnimationRules.Resolve(slide, a.Target);
        var name = targets.Count > 1 ? "Group of " + targets.Count : targets.FirstOrDefault()?.Describe ?? "?";
        if (item.Paragraph >= 0 && TextUnits.BodyOf(targets.FirstOrDefault()!) is { } body)
            name = "¶ " + body.Content.Paragraphs[item.Paragraph].Text.Trim();

        var grid = new Grid { Height = 40, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimelineWidth) });

        var handle = new IconView { Width = 12, Height = 12, Data = (Geometry)FindResource("Icon.ReOrderDotsVertical"), Cursor = Cursors.SizeAll, Opacity = 0.55 };
        handle.SetResourceReference(IconView.ForegroundProperty, "Text.Tertiary");
        grid.Children.Add(handle);

        if (click is { } c)
        {
            var badge = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
            badge.SetResourceReference(Border.BackgroundProperty, "Accent.Fill");
            var n = new TextBlock { Text = item.Group.Trigger != null ? "⚡" : c.ToString(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            n.SetResourceReference(TextBlock.ForegroundProperty, "Text.OnAccent");
            badge.Child = n;
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);
        }

        var icon = new IconView { Width = 15, Height = 15, Data = (Geometry)FindResource(ClassIcon(a.Class, a.Effect)), Foreground = new SolidColorBrush(ClassColor(a.Class)) };
        Grid.SetColumn(icon, 2);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
        var detail = a.DisplayName + (EffectCatalog.HasDirection(a.Effect) ? " · " + EffectCatalog.DirectionName(a.Direction, a.Class).ToLowerInvariant() : "") +
                     (a.TextUnit != TextUnit.All && item.Paragraph < 0 ? " · by " + a.TextUnit.ToString().ToLowerInvariant() : "");
        var d = new TextBlock { Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        d.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        text.Children.Add(d);
        Grid.SetColumn(text, 3);
        grid.Children.Add(text);

        var startIcon = a.Start switch
        {
            EffectStart.OnClick => "Icon.CursorClick",
            EffectStart.AfterPrevious => "Icon.Clock",
            _ => null,
        };
        if (item.Group.Trigger != null && click != null) startIcon = "Icon.TapSingle";
        if (startIcon != null)
        {
            var si = new IconView { Width = 13, Height = 13, Data = (Geometry)FindResource(startIcon), ToolTip = a.Start switch { EffectStart.OnClick => "On click", EffectStart.AfterPrevious => "After previous", _ => "With previous" } };
            si.SetResourceReference(IconView.ForegroundProperty, "Text.Secondary");
            Grid.SetColumn(si, 4);
            grid.Children.Add(si);
        }

        // Timeline bar: the delay as a gap, the run as a bar, repeats as a lighter tail.
        var bar = new Canvas { Width = TimelineWidth, Height = 40, ClipToBounds = true };
        var k = (TimelineWidth - 4) / span;
        var x = item.Begin * k;
        var runW = Math.Max(3, item.Length * k);
        var color = ClassColor(a.Class);
        var run = new Border { Width = runW, Height = 8, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color) };
        Canvas.SetLeft(run, x);
        Canvas.SetTop(run, 16);
        if (a.Repeat > 1 || a.RepeatUntil != RepeatUntil.None)
        {
            var tail = new Border
            {
                Width = Math.Max(0, Math.Min(TimelineWidth, (a.RepeatUntil != RepeatUntil.None ? span : item.Active) * k) - runW),
                Height = 8,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)),
            };
            Canvas.SetLeft(tail, x + runW);
            Canvas.SetTop(tail, 16);
            bar.Children.Add(tail);
        }
        bar.Children.Add(run);
        bar.ToolTip = $"Starts {item.Begin:0.0#} s after {(item.Group.Automatic ? "the slide appears" : "the click")}, runs {item.Length:0.0#} s" +
                      (a.Repeat > 1 ? $", {a.Repeat} times" : a.RepeatUntil == RepeatUntil.NextClick ? ", until the next click" : a.RepeatUntil == RepeatUntil.EndOfSlide ? ", until the end of the slide" : "");
        Grid.SetColumn(bar, 5);
        grid.Children.Add(bar);

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 0, 4, 0), Margin = new Thickness(-4, 0, -4, 1), Tag = a.Id, Cursor = Cursors.Hand };
        if (selected) row.SetResourceReference(Border.BackgroundProperty, "Accent.Selection");
        else row.Background = Brushes.Transparent;
        row.MouseEnter += (_, _) =>
        {
            if (!state.SelectedEffects.Contains(a.Id)) row.SetResourceReference(Border.BackgroundProperty, "Subtle.FillSecondary");
        };
        row.MouseLeave += (_, _) =>
        {
            if (!state.SelectedEffects.Contains(a.Id)) row.Background = Brushes.Transparent;
        };
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _dragArmed = a.Id;
        };
        row.PreviewMouseMove += (_, e) =>
        {
            if (_dragArmed != a.Id || e.LeftButton != MouseButtonState.Pressed) return;
            if ((e.GetPosition(this) - _dragStart).Length < 6) return;
            _dragArmed = null;
            var ids = state.SelectedEffects.Contains(a.Id) ? state.SelectedEffectList.Select(x => x.Id).ToList() : new List<string> { a.Id };
            var data = new DataObject(EffectDragFormat, string.Join(",", ids));
            DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            _dragArmed = null;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var ids = state.SelectedEffects.ToList();
            if (ctrl)
            {
                if (!ids.Remove(a.Id)) ids.Add(a.Id);
            }
            else if (shift && ids.Count > 0)
            {
                var anchor = order.TryGetValue(ids[0], out var i0) ? i0 : 0;
                var here = order[a.Id];
                ids = slide.Animations.Where(x => order[x.Id] >= Math.Min(anchor, here) && order[x.Id] <= Math.Max(anchor, here)).Select(x => x.Id).ToList();
            }
            else ids = new List<string> { a.Id };
            editor.SelectEffectsFromPane(ids);
            e.Handled = true;
        };
        row.MouseRightButtonUp += (_, e) =>
        {
            if (!state.SelectedEffects.Contains(a.Id)) editor.SelectEffectsFromPane(new[] { a.Id });
            EffectMenu(row).IsOpen = true;
            e.Handled = true;
        };
        AutomationProperties.SetName(row, $"{(click is { } cc ? "Click " + cc + ": " : "")}{name}, {a.DisplayName}");
        return row;
    }

    private ContextMenu EffectMenu(FrameworkElement target)
    {
        var editor = _editor!;
        var menu = new ContextMenu { PlacementTarget = target };
        void Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item("Play from here", "Icon.Play", editor.PlayFromSelected);
        menu.Items.Add(new Separator());
        Item("Start on click", "Icon.CursorClick", () => editor.EditEffects("Start", a => a.Start = EffectStart.OnClick));
        Item("Start with previous", "Icon.Add", () => editor.EditEffects("Start", a => a.Start = EffectStart.WithPrevious));
        Item("Start after previous", "Icon.Clock", () => editor.EditEffects("Start", a => a.Start = EffectStart.AfterPrevious));
        menu.Items.Add(new Separator());
        Item("Move earlier", "Icon.ArrowUp", () => editor.NudgeEffects(-1));
        Item("Move later", "Icon.ArrowDown", () => editor.NudgeEffects(1));
        Item("Remove", "Icon.Delete", editor.RemoveSelectedEffects, "Del");
        return menu;
    }

    private int DropIndex(StackPanel host, DragEventArgs e)
    {
        var rows = host.Children.OfType<Border>().Where(b => b.Tag is string).ToList();
        var slide = _state!.CurrentSlide!;
        var p = e.GetPosition(host);
        foreach (var row in rows)
        {
            var top = row.TranslatePoint(new Point(0, 0), host).Y;
            if (p.Y < top + row.ActualHeight / 2) return slide.Animations.FindIndex(a => a.Id == (string)row.Tag);
        }
        return slide.Animations.Count;
    }

    private void OnPaneDragOver(StackPanel host, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(EffectDragFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var rows = host.Children.OfType<Border>().Where(b => b.Tag is string).ToList();
        if (rows.Count == 0) return;
        var p = e.GetPosition(host);
        Border? before = null;
        foreach (var row in rows)
        {
            var top = row.TranslatePoint(new Point(0, 0), host).Y;
            if (p.Y < top + row.ActualHeight / 2)
            {
                before = row;
                break;
            }
        }
        // A line above the row it will go in front of (or under the last one).
        foreach (var row in rows)
        {
            var mark = row == before ? new Thickness(0, 2, 0, 0) : before == null && row == rows[rows.Count - 1] ? new Thickness(0, 0, 0, 2) : new Thickness(0);
            row.BorderThickness = mark;
            if (mark.Top > 0 || mark.Bottom > 0) row.SetResourceReference(Border.BorderBrushProperty, "Accent.Fill");
        }
    }

    private static void HideDropMarker(StackPanel host)
    {
        foreach (var row in host.Children.OfType<Border>().Where(b => b.Tag is string)) row.BorderThickness = new Thickness(0);
    }

    private void OnPaneDrop(StackPanel host, DragEventArgs e, Slide slide)
    {
        HideDropMarker(host);
        if (e.Data.GetData(EffectDragFormat) is not string text) return;
        var ids = text.Split(',').Where(s => s.Length > 0).ToList();
        var index = DropIndex(host, e);
        _editor!.MoveEffects(ids, index);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ options of the selected effects

    private void BuildEffectOptions(Slide slide, IReadOnlyList<AnimationEffect> selected, SlideTimeline timeline)
    {
        var editor = _editor!;
        var a = selected[0];
        var all = selected.Count;
        var sec = Section(all == 1 ? a.DisplayName : $"{all} effects");

        // Effect (opens the gallery to change it) and direction.
        var change = TextButton(all == 1 ? "Change effect…" : "Change effects…", "Icon.StarEmphasis", () => { });
        change.Click += (_, _) => AnimationGallery.Show(change, editor, AnimationGallery.Purpose.Change);
        change.IsEnabled = selected.All(x => x.Class != EffectClass.Media);
        sec.Children.Add(change);
        if (EffectCatalog.HasDirection(a.Effect) && selected.All(x => x.Effect == a.Effect))
        {
            var dirs = EffectCatalog.Directions(a.Effect).Select(d => (d, EffectCatalog.DirectionName(d, a.Class)));
            Row(sec, "Direction", Combo(dirs, a.Direction, d => editor.EditEffects("Direction", x => x.Direction = d), "Direction"));
        }
        if (a.Class == EffectClass.Media)
        {
            Row(sec, "Action", Combo(new[] { (EffectKind.Play, "Play"), (EffectKind.Pause, "Pause"), (EffectKind.Stop, "Stop") }, a.Effect,
                k => editor.EditEffects("Media action", x =>
                {
                    if (x.Class == EffectClass.Media) x.Effect = k;
                }), "Media action"));
        }

        // Timing.
        Row(sec, "Start", Combo(new[] { (EffectStart.OnClick, "On click"), (EffectStart.WithPrevious, "With previous"), (EffectStart.AfterPrevious, "After previous") },
            selected.All(x => x.Start == a.Start) ? a.Start : (EffectStart)(-1), s => editor.EditEffects("Start", x => x.Start = s), "Start"));
        if (a.Class != EffectClass.Media)
        {
            sec.Children.Add(Grid2(
                Labeled("Duration", Number(a.Duration, 0, 60, 0.1, v => editor.EditEffects("Duration", x => x.Duration = Math.Round(v, 2)), " s", 2)),
                Labeled("Delay", Number(a.Delay, 0, 600, 0.1, v => editor.EditEffects("Delay", x => x.Delay = Math.Round(v, 2)), " s", 2))));
            var repeats = new List<(string, string)> { ("1", "Once"), ("2", "2 times"), ("3", "3 times"), ("4", "4 times"), ("5", "5 times"), ("10", "10 times"), ("click", "Until next click"), ("slide", "Until end of slide") };
            var currentRepeat = a.RepeatUntil == RepeatUntil.NextClick ? "click" : a.RepeatUntil == RepeatUntil.EndOfSlide ? "slide" : a.Repeat.ToString(CultureInfo.InvariantCulture);
            if (!repeats.Any(r => r.Item1 == currentRepeat)) repeats.Add((currentRepeat, a.Repeat + " times"));
            Row(sec, "Repeat", Combo(repeats, currentRepeat, r => editor.EditEffects("Repeat", x =>
            {
                x.RepeatUntil = r == "click" ? RepeatUntil.NextClick : r == "slide" ? RepeatUntil.EndOfSlide : RepeatUntil.None;
                x.Repeat = int.TryParse(r, out var n) ? n : 1;
            }), "Repeat"));
            var easings = new[]
            {
                (EffectEasing.Auto, "Automatic"), (EffectEasing.Linear, "Steady"), (EffectEasing.EaseIn, "Speed up"), (EffectEasing.EaseOut, "Slow down"),
                (EffectEasing.EaseInOut, "Smooth start and end"), (EffectEasing.Overshoot, "Overshoot"),
            };
            if (a.Effect is not (EffectKind.Appear or EffectKind.Bounce))
                Row(sec, "Easing", Combo(easings, a.Easing, ez => editor.EditEffects("Easing", x => x.Easing = ez), "Easing"));
            Switch(sec, "Rewind when done", a.Rewind, on => editor.EditEffects("Rewind", x => x.Rewind = on),
                a.Class == EffectClass.Entrance ? "Hides again at the end" : a.Class == EffectClass.Exit ? "Comes back at the end" : "Goes back to how it was");
        }

        // Strength and colour.
        switch (a.Effect)
        {
            case EffectKind.Spin:
                Row(sec, "Angle", Combo(new[] { (90.0, "Quarter turn"), (180.0, "Half turn"), (360.0, "Full turn"), (720.0, "Two turns"), (-90.0, "Quarter turn back"), (-360.0, "Full turn back") },
                    a.EffectiveAmount, v => editor.EditEffects("Spin angle", x => x.Amount = v), "Spin angle"));
                break;
            case EffectKind.Grow:
                Row(sec, "Size", Number(a.EffectiveAmount, 10, 400, 10, v => editor.EditEffects("Size", x => x.Amount = Math.Round(v)), " %"));
                break;
            case EffectKind.Transparency:
                Row(sec, "See-through", Number(a.EffectiveAmount, 5, 100, 5, v => editor.EditEffects("Transparency", x => x.Amount = Math.Round(v)), " %"));
                break;
            case EffectKind.Zoom:
                Row(sec, "Starts at", Number(a.EffectiveAmount, 1, 99, 5, v => editor.EditEffects("Zoom", x => x.Amount = Math.Round(v)), " %"));
                break;
            case EffectKind.ColorPulse:
                var label = new TextBlock { Text = "Colour", FontSize = 12, Margin = new Thickness(1, 2, 0, 6) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
                sec.Children.Add(label);
                sec.Children.Add(Swatches(a.Color ?? "accent2", c => editor.EditEffects("Pulse colour", x => x.Color = c), false, "Pulse colour"));
                break;
        }

        // Text.
        var targets = AnimationRules.Resolve(slide, a.Target);
        var body = targets.Count == 1 ? TextUnits.BodyOf(targets[0]) : null;
        if (all == 1 && body != null && !body.Content.IsEmpty && a.Class is not (EffectClass.Media or EffectClass.Motion))
        {
            Row(sec, "Text", Combo(new[] { (TextUnit.All, "All at once"), (TextUnit.Paragraph, "By paragraph"), (TextUnit.Word, "By word"), (TextUnit.Letter, "By letter") },
                a.TextUnit, u => editor.EditEffects("Animate text", x => x.TextUnit = u), "Animate text"));
            if (a.TextUnit == TextUnit.Paragraph)
                Switch(sec, "Each paragraph on its own click", a.ParagraphClicks, on => editor.EditEffects("Paragraph clicks", x => x.ParagraphClicks = on), "Bullet points appear one by one");
            if (a.TextUnit != TextUnit.All && !(a.TextUnit == TextUnit.Paragraph && a.ParagraphClicks))
                Row(sec, "Between", Number(a.TextStagger ?? SlideTimeline.DefaultStagger(a.TextUnit), 0, 5, 0.01,
                    v => editor.EditEffects("Text stagger", x => x.TextStagger = Math.Round(v, 3)), " s", 2));
        }

        // Motion path.
        if (all == 1 && a.Class == EffectClass.Motion)
        {
            var hint = new TextBlock { Text = a.Effect == EffectKind.Custom ? "Drag the points on the slide to reshape the path, or draw it again." : "Drag the points on the slide to reshape the path.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            sec.Children.Add(hint);
            sec.Children.Add(Grid2(TextButton("Reverse", "Icon.ArrowSwap", () => editor.ReversePath()),
                TextButton("Draw a path", "Icon.HandDraw", () => editor.SlideCanvas.BeginDrawPath(a.Id))));
        }

        // Trigger: play when an element is clicked instead of in the click order.
        {
            var choices = new List<(string, string)> { ("", "In the click order") };
            foreach (var e in slide.Elements.Where(x => x.Placeholder != "decor"))
                choices.Add((e.Id, "When " + Short(e.Describe) + " is clicked"));
            var current = selected.All(x => x.Trigger == a.Trigger) ? a.Trigger ?? "" : "?";
            Row(sec, "Trigger", Combo(choices, current, id => editor.EditEffects("Trigger", x => x.Trigger = id.Length == 0 ? null : id), "Trigger"));
        }

        var buttons = new WrapPanel { Margin = new Thickness(-4, 6, 0, 0) };
        buttons.Children.Add(IconButton("Icon.ArrowUp", "Move earlier", () => editor.NudgeEffects(-1)));
        buttons.Children.Add(IconButton("Icon.ArrowDown", "Move later", () => editor.NudgeEffects(1)));
        buttons.Children.Add(IconButton("Icon.Play", "Play from here", () => editor.PlayFromSelected()));
        buttons.Children.Add(new Border { Width = 8 });
        var remove = TextButton(all == 1 ? "Remove" : "Remove all " + all, "Icon.Delete", () => editor.RemoveSelectedEffects());
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.Margin = new Thickness(0, 0, 0, 0);
        buttons.Children.Add(remove);
        sec.Children.Add(buttons);
    }
}
