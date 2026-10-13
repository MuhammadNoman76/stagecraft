using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

public sealed partial class PropertiesPanel
{
    private void BuildElements()
    {
        var editor = _editor!;
        var selected = _state!.SelectedElements;
        if (selected.Count == 0) return;
        var single = selected.Count == 1 ? selected[0] : null;
        var kinds = selected.Select(e => e.KindName).Distinct().ToList();
        var title = single != null ? single.KindName : kinds.Count == 1 ? $"{selected.Count} × {kinds[0]}" : $"{selected.Count} elements";
        var subtitle = single?.Placeholder is { } ph && ph != "decor" ? "Layout placeholder" : selected.Any(e => e.GroupId != null) ? "Grouped" : null;

        // Header with lock and delete.
        var header = new DockPanel { Margin = new Thickness(0, 6, -6, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
        var locked = selected.All(e => e.Locked);
        buttons.Children.Add(IconToggle(locked ? "Icon.LockClosed" : "Icon.LockOpen", locked ? "Unlock (Ctrl+Shift+L)" : "Lock (Ctrl+Shift+L)", locked, _ => editor.ToggleLock()));
        buttons.Children.Add(IconButton("Icon.Delete", "Delete (Del)", () => editor.DeleteSelection()));
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        var titles = new StackPanel();
        header.Children.Add(titles);
        _stack.Children.Add(header);
        var t = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        titles.Children.Add(t);
        if (subtitle != null)
        {
            var s = new TextBlock { Text = subtitle, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            s.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            titles.Children.Add(s);
        }

        if (locked)
        {
            var hint = new TextBlock { Text = "Locked elements can't be moved or changed on the slide. Unlock to edit.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            _stack.Children.Add(hint);
            return;
        }

        var hasText = selected.Any(e => e is TextElement || e is ShapeElement { IsLine: false });
        if (hasText) BuildText(selected);

        if (single is ImageElement img) BuildImage(img);
        if (single is ChartElement chart) BuildChart(chart);
        if (single is TableElement table) BuildTable(table);
        if (selected.All(e => e is IconElement)) BuildIcon(selected.Cast<IconElement>().ToList());

        var fillables = selected.Where(e => e is ShapeElement { IsLine: false } || e is TextElement).ToList();
        if (fillables.Count > 0) BuildFill(fillables);
        var lines = selected.OfType<ShapeElement>().Where(s => s.IsLine).ToList();
        if (lines.Count > 0) BuildLine(lines);
        else if (selected.Any(e => e is ShapeElement or TextElement or ImageElement)) BuildBorder(selected.Where(e => e is ShapeElement or TextElement or ImageElement).ToList());

        BuildArrange(selected);
        BuildAnimation(selected);
        BuildName(selected);
    }

    // ------------------------------------------------------------------ text

    private void BuildText(IReadOnlyList<Element> selected)
    {
        var editor = _editor!;
        var state = editor.ReadTextState();
        var sec = Section("Text");

        // Style (role)
        var roles = new[]
        {
            (TextRole.Title, "Title"), (TextRole.Subtitle, "Subtitle"), (TextRole.Heading, "Heading"), (TextRole.Body, "Body"),
            (TextRole.Caption, "Caption"), (TextRole.Quote, "Quote"), (TextRole.Stat, "Big number"), (TextRole.Label, "Label"), (TextRole.Code, "Code"),
        };
        Row(sec, "Style", Combo(roles, state.Role ?? (TextRole)(-1), r => editor.SetRole(r), "Text style"));

        // Font
        var fonts = new List<(string, string)> { ("heading", $"Theme headings ({_state!.Deck.Theme.Fonts.Heading})"), ("body", $"Theme text ({_state.Deck.Theme.Fonts.Body})") };
        foreach (var f in FontCatalog.Bundled) fonts.Add((f, f));
        foreach (var f in FontCatalog.AvailableSystemChoices()) fonts.Add((f, f));
        var currentFont = state.Font ?? "";
        if (currentFont.Length > 0 && !fonts.Any(f => string.Equals(f.Item1, currentFont, StringComparison.OrdinalIgnoreCase))) fonts.Add((currentFont, currentFont));
        Row(sec, "Font", Combo(fonts, fonts.FirstOrDefault(f => string.Equals(f.Item1, currentFont, StringComparison.OrdinalIgnoreCase)).Item1 ?? "", f => editor.SetFont(f), "Font"));

        // Size with - / +
        var sizeRow = new DockPanel();
        var minus = IconButton("Icon.ZoomOut", "Smaller (Ctrl+Shift+,)", () => editor.ChangeFontSize(-1));
        var plus = IconButton("Icon.ZoomIn", "Bigger (Ctrl+Shift+.)", () => editor.ChangeFontSize(1));
        DockPanel.SetDock(plus, Dock.Right);
        DockPanel.SetDock(minus, Dock.Right);
        sizeRow.Children.Add(plus);
        sizeRow.Children.Add(minus);
        var size = Number((state.Size ?? 0) / 2, 4, 400, 1, v => editor.SetFontSize(v * 2), " pt", 1);
        if (state.Size == null) size.SetMixed();
        size.Margin = new Thickness(0, 0, 4, 0);
        sizeRow.Children.Add(size);
        Row(sec, "Size", sizeRow);

        // B I U S
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toggles.Children.Add(IconToggle("Icon.TextBold", "Bold (Ctrl+B)", state.Bold == true, on => editor.SetBold(on)));
        toggles.Children.Add(IconToggle("Icon.TextItalic", "Italic (Ctrl+I)", state.Italic == true, on => editor.SetItalic(on)));
        toggles.Children.Add(IconToggle("Icon.TextUnderline", "Underline (Ctrl+U)", state.Underline == true, on => editor.SetUnderline(on)));
        toggles.Children.Add(IconToggle("Icon.TextCaseUppercase", "Capitals", state.Uppercase == true, on => editor.SetUppercase(on)));
        toggles.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0) });
        toggles.Children.Add(IconToggle("Icon.TextBulletListLtr", "Bullets", state.List == ListKind.Bullet, on => editor.SetList(on ? ListKind.Bullet : ListKind.None)));
        toggles.Children.Add(IconToggle("Icon.TextNumberListLtr", "Numbering", state.List == ListKind.Number, on => editor.SetList(on ? ListKind.Number : ListKind.None)));
        sec.Children.Add(toggles);

        // Colour
        sec.Children.Add(Swatches(state.Color, c => editor.SetTextColor(c), allowNone: false, "Text colour"));

        // Alignment
        var alignRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var valign = Segments(new[]
        {
            (VerticalAlign.Top, "Top", (string?)"Icon.AlignTop"), (VerticalAlign.Middle, "Middle", "Icon.AlignCenterVertical"), (VerticalAlign.Bottom, "Bottom", "Icon.AlignBottom"),
        }, state.VAlign ?? (VerticalAlign)(-1), v => editor.SetVAlign(v), "Vertical alignment");
        DockPanel.SetDock(valign, Dock.Right);
        alignRow.Children.Add(valign);
        alignRow.Children.Add(Segments(new[]
        {
            (TextAlign.Left, "Left (Ctrl+L)", (string?)"Icon.TextAlignLeft"), (TextAlign.Center, "Centre (Ctrl+E)", "Icon.TextAlignCenter"),
            (TextAlign.Right, "Right (Ctrl+R)", "Icon.TextAlignRight"), (TextAlign.Justify, "Justify (Ctrl+J)", "Icon.TextAlignJustify"),
        }, state.Align ?? (TextAlign)(-1), a => editor.SetAlign(a), "Alignment"));
        sec.Children.Add(alignRow);

        var spacing = new[] { (0.9, "0.9"), (1.0, "1.0"), (1.15, "1.15"), (1.3, "1.3"), (1.5, "1.5"), (2.0, "2.0") };
        var currentSpacing = state.LineSpacing.HasValue ? spacing.OrderBy(s => Math.Abs(s.Item1 - state.LineSpacing.Value)).First().Item1 : -1;
        Row(sec, "Line spacing", Combo(spacing.Select(s => (s.Item1, s.Item2)), currentSpacing, v => editor.SetLineSpacing(v), "Line spacing"));
        Switch(sec, "Shrink text to fit", state.AutoFit ?? true, on => editor.SetAutoFit(on), "Long text gets smaller instead of overflowing");
    }

    // ------------------------------------------------------------------ fill, border, line

    private void BuildFill(IReadOnlyList<Element> elements)
    {
        var editor = _editor!;
        var sec = Section("Fill");
        Fill? fill = elements[0] switch
        {
            ShapeElement s => s.Fill,
            TextElement t => t.Fill,
            _ => null,
        };
        var current = fill == null || fill.Kind == FillKind.None ? null : fill.Kind == FillKind.Solid ? fill.Color : "gradient";
        sec.Children.Add(Swatches(current, c => editor.SetFill(c == null ? Fill.None : Fill.Solid(c)), allowNone: true, "Fill colour"));
        var gradients = new WrapPanel { Margin = new Thickness(0, 2, -6, 6) };
        foreach (var (a, b, angle, name) in new[]
                 {
                     ("accent1", "accent2", 135.0, "Accent 1 to 2"), ("accent2", "accent3", 135.0, "Accent 2 to 3"), ("accent3", "accent4", 135.0, "Accent 3 to 4"),
                     ("accent1", "accent1@40", 90.0, "Accent fade"), ("#000000@0", "#000000@70", 90.0, "Dark scrim"),
                 })
        {
            var brush = CurrentStyle.Brush(Fill.Linear(a, b, angle), new Rect(0, 0, 1, 1)) ?? Brushes.Gray;
            var selected = fill?.Kind == FillKind.Linear && fill.Color == a && fill.Color2 == b;
            gradients.Children.Add(SwatchButton(brush, "Gradient: " + name, selected, () => editor.SetFill(Fill.Linear(a, b, angle))));
        }
        sec.Children.Add(gradients);
        var opacity = elements[0].Opacity;
        Row(sec, "Opacity", Slider(opacity * 100, 5, 100, v => editor.SetOpacity(v / 100), () => editor.SealHistory()));
        if (elements.Any(e => e is TextElement || e is ShapeElement { Shape: ShapeKind.Rectangle or ShapeKind.RoundedRectangle or ShapeKind.Callout }))
        {
            var radius = elements[0] switch
            {
                ShapeElement s => s.CornerRadius,
                TextElement te => te.CornerRadius,
                _ => 0,
            };
            Row(sec, "Corners", Slider(radius, 0, 120, v => editor.SetCornerRadius(v), () => editor.SealHistory()));
        }
    }

    private void BuildBorder(IReadOnlyList<Element> elements)
    {
        var editor = _editor!;
        var sec = Section("Border");
        Stroke? stroke = elements[0] switch
        {
            ShapeElement s => s.Stroke,
            TextElement t => t.Stroke,
            ImageElement i => i.Stroke,
            _ => null,
        };
        sec.Children.Add(Swatches(stroke?.Color, c => editor.SetStroke(c == null ? null : new Stroke { Color = c, Width = stroke?.Width ?? 4, Dash = stroke?.Dash ?? DashStyleKind.Solid }),
            allowNone: true, "Border colour"));
        if (stroke != null)
        {
            Row(sec, "Width", Number(stroke.Width, 0.5, 60, 1, v => editor.SetStrokeWidth(v), "", 1));
            Row(sec, "Style", Segments(new[] { (DashStyleKind.Solid, "Solid", (string?)null), (DashStyleKind.Dash, "Dashes", null), (DashStyleKind.Dot, "Dots", null) },
                stroke.Dash, d => editor.SetStrokeDash(d), "Border style"));
        }
    }

    private void BuildLine(IReadOnlyList<ShapeElement> lines)
    {
        var editor = _editor!;
        var sec = Section("Line");
        var stroke = ElementRenderer.LineStroke(lines[0]);
        sec.Children.Add(Swatches(stroke.Color, c => editor.SetStroke(new Stroke { Color = c ?? "text", Width = stroke.Width, Dash = stroke.Dash }), allowNone: false, "Line colour"));
        Row(sec, "Width", Number(stroke.Width, 0.5, 60, 1, v => editor.SetStrokeWidth(v), "", 1));
        Row(sec, "Style", Segments(new[] { (DashStyleKind.Solid, "Solid", (string?)null), (DashStyleKind.Dash, "Dashes", null), (DashStyleKind.Dot, "Dots", null) },
            stroke.Dash, d => editor.SetStrokeDash(d), "Line style"));
        var heads = new[] { (ArrowHead.None, "None"), (ArrowHead.Arrow, "Arrow"), (ArrowHead.Dot, "Dot") };
        var first = lines[0];
        var end = first.Shape == ShapeKind.Arrow && first.EndHead == ArrowHead.None ? ArrowHead.Arrow : first.EndHead;
        sec.Children.Add(Grid2(
            Labeled("Start", Combo(heads, first.StartHead, h => editor.SetLineHeads(h, null), "Line start")),
            Labeled("End", Combo(heads, end, h => editor.SetLineHeads(null, h), "Line end"))));
        if (lines.All(l => l.Shape == ShapeKind.Connector))
            Switch(sec, "Right-angled route", first.Elbow, on => editor.SetElbow(on), "Connectors follow the elements they join");
    }

    // ------------------------------------------------------------------ pictures, charts, tables, icons

    private void BuildImage(ImageElement img)
    {
        var editor = _editor!;
        var sec = Section("Picture");
        sec.Children.Add(TextButton(img.Media == null ? "Choose a picture…" : "Replace picture…", "Icon.ImageAdd", () => editor.ReplaceImage(img)));
        if (img.Media == null) return;
        Row(sec, "Fit", Segments(new[] { (ImageFit.Cover, "Fill", (string?)null), (ImageFit.Contain, "Fit", null), (ImageFit.Stretch, "Stretch", null) },
            img.Crop != null ? (ImageFit)(-1) : img.Fit, f => editor.EditSelected("Picture fit", list =>
            {
                foreach (var i in list.OfType<ImageElement>())
                {
                    i.Fit = f;
                    i.Crop = null;
                }
            }), "Picture fit"));
        Row(sec, "Shape", Segments(new[] { (ImageMask.Rectangle, "Rectangle", (string?)"Icon.Square"), (ImageMask.Circle, "Circle", "Icon.Circle") },
            img.Mask, m => editor.EditSelected("Picture shape", list =>
            {
                foreach (var i in list.OfType<ImageElement>()) i.Mask = m;
            }), "Picture shape"));
        if (img.Mask == ImageMask.Rectangle)
            Row(sec, "Corners", Slider(img.CornerRadius, 0, 200, v => editor.SetCornerRadius(v), () => editor.SealHistory()));
        if (img.Fit == ImageFit.Cover && img.Crop == null)
        {
            sec.Children.Add(Grid2(
                Labeled("Focus across", Slider(img.FocusX * 100, 0, 100, v => editor.EditSelected("Picture focus", l => { foreach (var i in l.OfType<ImageElement>()) i.FocusX = v / 100; }, "focus"), () => editor.SealHistory())),
                Labeled("Focus down", Slider(img.FocusY * 100, 0, 100, v => editor.EditSelected("Picture focus", l => { foreach (var i in l.OfType<ImageElement>()) i.FocusY = v / 100; }, "focus"), () => editor.SealHistory()))));
        }
        var alt = new TextBox { Text = img.AltText ?? "", TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Tag = "rebuild" };
        Ui.SetPlaceholder(alt, "Describe the picture for screen readers");
        alt.LostKeyboardFocus += (_, _) =>
        {
            if ((img.AltText ?? "") != alt.Text) editor.EditSelected("Alt text", l => { foreach (var i in l.OfType<ImageElement>()) i.AltText = alt.Text.Length == 0 ? null : alt.Text; });
        };
        Row(sec, "Alt text", alt);
    }

    private void BuildChart(ChartElement chart)
    {
        var editor = _editor!;
        var sec = Section("Chart");
        sec.Children.Add(TextButton("Edit data…", "Icon.Table", () => editor.EditChart(chart), accent: false));
        Row(sec, "Type", Segments(new[]
        {
            (ChartKind.Bar, "Bars", (string?)"Icon.DataBarVertical"), (ChartKind.Line, "Line", "Icon.DataLine"), (ChartKind.Pie, "Pie", "Icon.DataPie"), (ChartKind.Donut, "Donut", "Icon.DataUsage"),
        }, chart.Kind, k => editor.EditSelected("Chart type", l => { foreach (var c in l.OfType<ChartElement>()) c.Kind = k; }), "Chart type"));
        Switch(sec, "Legend", chart.ShowLegend, on => editor.EditSelected("Chart legend", l => { foreach (var c in l.OfType<ChartElement>()) c.ShowLegend = on; }));
        Switch(sec, "Values", chart.ShowValues, on => editor.EditSelected("Chart values", l => { foreach (var c in l.OfType<ChartElement>()) c.ShowValues = on; }));
        if (chart.Kind is ChartKind.Bar or ChartKind.Line)
            Switch(sec, "Grid lines", chart.ShowGrid, on => editor.EditSelected("Chart grid", l => { foreach (var c in l.OfType<ChartElement>()) c.ShowGrid = on; }));
        var formats = new[] { ("#,0.##", "1,234.5"), ("#,0", "1,235"), ("0.0", "1234.5"), ("$#,0", "$1,235"), ("#,0'%'", "1,235%"), ("#,0'k'", "1,235k") };
        var current = formats.Any(f => f.Item1 == chart.ValueFormat) ? chart.ValueFormat : "#,0.##";
        Row(sec, "Numbers", Combo(formats, current, f => editor.EditSelected("Number format", l => { foreach (var c in l.OfType<ChartElement>()) c.ValueFormat = f; }), "Number format"));
    }

    private void BuildTable(TableElement table)
    {
        var editor = _editor!;
        var sec = Section("Table");
        sec.Children.Add(TextButton("Edit table…", "Icon.Table", () => editor.EditTable(table)));
        Switch(sec, "Header row", table.HeaderRow, on => editor.EditSelected("Header row", l => { foreach (var t in l.OfType<TableElement>()) t.HeaderRow = on; }));
        Switch(sec, "Banded rows", table.Banded, on => editor.EditSelected("Banded rows", l => { foreach (var t in l.OfType<TableElement>()) t.Banded = on; }));
        Switch(sec, "Bold first column", table.FirstColumnBold, on => editor.EditSelected("First column", l => { foreach (var t in l.OfType<TableElement>()) t.FirstColumnBold = on; }));
        var header = new TextBlock { Text = "Header colour", FontSize = 12, Margin = new Thickness(1, 6, 0, 6) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        sec.Children.Add(header);
        sec.Children.Add(Swatches(table.HeaderColor, c => editor.EditSelected("Header colour", l => { foreach (var t in l.OfType<TableElement>()) t.HeaderColor = c ?? "accent1"; }), false, "Header colour"));
        var size = Number((table.FontSize ?? TableRenderer.FontSizeFor(table, table.Bounds, CurrentStyle)) / 2, 4, 60, 1,
            v => editor.EditSelected("Table text size", l => { foreach (var t in l.OfType<TableElement>()) t.FontSize = v * 2; }, "tablesize"), " pt", 1);
        Row(sec, "Text size", size);
    }

    private void BuildIcon(IReadOnlyList<IconElement> icons)
    {
        var editor = _editor!;
        var sec = Section("Icon");
        if (icons.Count == 1) sec.Children.Add(TextButton("Change icon…", "Icon.Icons", () => editor.PickIcon(icons[0])));
        sec.Children.Add(Swatches(icons[0].Color, c => editor.EditSelected("Icon colour", l => { foreach (var i in l.OfType<IconElement>()) i.Color = c ?? "accent1"; }), false, "Icon colour"));
        Row(sec, "Background", Segments(new[]
        {
            (IconContainer.None, "None", (string?)null), (IconContainer.Circle, "Circle", null), (IconContainer.RoundedSquare, "Square", null),
        }, icons[0].Container, c => editor.EditSelected("Icon background", l => { foreach (var i in l.OfType<IconElement>()) i.Container = c; }), "Icon background"));
    }

    // ------------------------------------------------------------------ arrange

    private void BuildArrange(IReadOnlyList<Element> selected)
    {
        var editor = _editor!;
        var sec = Section("Arrange");
        var bounds = selected.Count == 1 ? selected[0].Bounds : DeckOperations.BoundsOf(selected);
        var x = Number(bounds.X, -10000, 10000, 1, v => editor.SetGeometry(x: v), "", 1);
        var y = Number(bounds.Y, -10000, 10000, 1, v => editor.SetGeometry(y: v), "", 1);
        var w = Number(bounds.Width, 1, 20000, 1, v => editor.SetGeometry(w: v), "", 1);
        var h = Number(bounds.Height, 0, 20000, 1, v => editor.SetGeometry(h: v), "", 1);
        sec.Children.Add(Grid2(Labeled("X", x), Labeled("Y", y)));
        sec.Children.Add(Grid2(Labeled("Width", w), Labeled("Height", h)));
        NumberBox? angle = null;
        if (selected.Count == 1 && !(selected[0] is ShapeElement { IsLine: true }))
        {
            angle = Number(selected[0].Rotation, -360, 360, 1, v => editor.SetGeometry(rotation: v), "°", 1);
            sec.Children.Add(Grid2(Labeled("Rotation", angle), new Border()));
        }
        _refreshNumbers = () =>
        {
            var sel = _state!.SelectedElements;
            if (sel.Count == 0) return;
            var b = sel.Count == 1 ? sel[0].Bounds : DeckOperations.BoundsOf(sel);
            x.Value = b.X;
            y.Value = b.Y;
            w.Value = b.Width;
            h.Value = b.Height;
            if (angle != null) angle.Value = sel[0].Rotation;
        };

        var align = new WrapPanel { Margin = new Thickness(-4, 4, 0, 4) };
        var toSlide = selected.Count == 1 ? " to the slide" : "";
        align.Children.Add(IconButton("Icon.AlignLeft", "Align left" + toSlide, () => editor.Align(AlignMode.Left)));
        align.Children.Add(IconButton("Icon.AlignCenterHorizontal", "Align centres" + toSlide, () => editor.Align(AlignMode.CenterHorizontal)));
        align.Children.Add(IconButton("Icon.AlignRight", "Align right" + toSlide, () => editor.Align(AlignMode.Right)));
        align.Children.Add(IconButton("Icon.AlignTop", "Align tops" + toSlide, () => editor.Align(AlignMode.Top)));
        align.Children.Add(IconButton("Icon.AlignCenterVertical", "Align middles" + toSlide, () => editor.Align(AlignMode.CenterVertical)));
        align.Children.Add(IconButton("Icon.AlignBottom", "Align bottoms" + toSlide, () => editor.Align(AlignMode.Bottom)));
        if (DeckOperations.Units(selected).Count >= 3)
        {
            align.Children.Add(IconButton("Icon.AlignSpaceEvenlyHorizontal", "Distribute horizontally", () => editor.Distribute(true)));
            align.Children.Add(IconButton("Icon.AlignSpaceEvenlyVertical", "Distribute vertically", () => editor.Distribute(false)));
        }
        sec.Children.Add(align);
        if (DeckOperations.Units(selected).Count >= 2)
        {
            var tidy = TextButton("Tidy up", "Icon.Sparkle", () => editor.TidySelection());
            tidy.ToolTip = "Line up, equalise sizes and even out the gaps (Ctrl+Alt+T)";
            sec.Children.Add(tidy);
        }

        var order = new WrapPanel { Margin = new Thickness(-4, 0, 0, 4) };
        order.Children.Add(IconButton("Icon.PositionToFront", "Bring to front (Ctrl+Shift+])", () => editor.MoveZ(ZOrderMove.Front)));
        order.Children.Add(IconButton("Icon.PositionForward", "Bring forward (Ctrl+])", () => editor.MoveZ(ZOrderMove.Forward)));
        order.Children.Add(IconButton("Icon.PositionBackward", "Send backward (Ctrl+[)", () => editor.MoveZ(ZOrderMove.Backward)));
        order.Children.Add(IconButton("Icon.PositionToBack", "Send to back (Ctrl+Shift+[)", () => editor.MoveZ(ZOrderMove.Back)));
        order.Children.Add(new Border { Width = 10 });
        order.Children.Add(IconButton("Icon.FlipHorizontal", "Flip horizontally", () => editor.Flip(true)));
        order.Children.Add(IconButton("Icon.FlipVertical", "Flip vertically", () => editor.Flip(false)));
        sec.Children.Add(order);

        var grouped = selected.Any(e => e.GroupId != null);
        if (selected.Count > 1 || grouped)
        {
            sec.Children.Add(Grid2(
                TextButton("Group", "Icon.Group", () => editor.GroupSelection()),
                TextButton("Ungroup", "Icon.GroupDismiss", () => editor.UngroupSelection())));
        }
        sec.Children.Add(Grid2(TextButton("Duplicate", "Icon.Copy", () => editor.DuplicateSelection()), TextButton("Copy style", "Icon.PaintBrush", () => editor.StartFormatPainter())));
    }

    // ------------------------------------------------------------------ animation

    private void BuildAnimation(IReadOnlyList<Element> selected)
    {
        var editor = _editor!;
        var sec = Section("Animation");
        var build = selected[0].Build;
        var effects = new[]
        {
            (BuildEffect.None, "None (always visible)"), (BuildEffect.Appear, "Appear"), (BuildEffect.Fade, "Fade in"), (BuildEffect.Rise, "Rise"),
            (BuildEffect.Scale, "Scale up"), (BuildEffect.Wipe, "Wipe"),
        };
        Row(sec, "Effect", Combo(effects, build?.Effect ?? BuildEffect.None, e => editor.SetBuild(e), "Build effect"));
        if (build != null && build.Effect != BuildEffect.None)
        {
            sec.Children.Add(Grid2(
                Labeled("On click", Number(build.Step, 1, 50, 1, v => editor.EditBuild(b => b.Step = (int)v))),
                Labeled("Delay", Number(build.Delay, 0, 20, 0.1, v => editor.EditBuild(b => b.Delay = v), " s", 1))));
            sec.Children.Add(Grid2(
                Labeled("Duration", Number(build.Duration, 0.1, 10, 0.1, v => editor.EditBuild(b => b.Duration = v), " s", 1)),
                new Border()));
        }
        sec.Children.Add(TextButton("Preview animations", "Icon.Play", () => editor.PreviewBuilds()));
    }

    private void BuildName(IReadOnlyList<Element> selected)
    {
        if (selected.Count != 1) return;
        var editor = _editor!;
        var e = selected[0];
        var sec = Section("Name");
        var box = new TextBox { Text = e.Name ?? "", Tag = "rebuild" };
        Ui.SetPlaceholder(box, "Give it a name for Morph");
        AutomationProperties.SetName(box, "Element name");
        box.LostKeyboardFocus += (_, _) =>
        {
            var name = box.Text.Trim();
            if ((e.Name ?? "") != name) editor.EditSelected("Rename", l => { foreach (var x in l) x.Name = name.Length == 0 ? null : name; });
        };
        sec.Children.Add(box);
        var hint = new TextBlock { Text = "Elements with the same name on the next slide morph into each other.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        sec.Children.Add(hint);
    }
}
