using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Themes;

namespace Stagecraft.Views.Editor;

public sealed partial class PropertiesPanel
{
    // ------------------------------------------------------------------ slide (nothing selected)

    private void BuildSlide()
    {
        var editor = _editor!;
        var slide = _state!.CurrentSlide!;
        var index = _state.CurrentIndex + 1;
        var many = _state.SelectedSlides.Count > 1;
        var layout = LayoutEngine.Find(slide.Layout)?.Name ?? "Custom";
        Title(many ? $"{_state.SelectedSlides.Count} slides" : $"Slide {index}", many ? "Changes apply to every selected slide" : layout);

        // Layout gallery
        var layouts = Section("Layout");
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var info in LayoutEngine.All)
        {
            var image = new Image { Source = Previews.Layout(_state.Deck, info.Id, 168), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(slide.Layout == info.Id ? 2 : 1),
                Child = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = image },
                Padding = new Thickness(slide.Layout == info.Id ? 0 : 1),
            };
            frame.SetResourceReference(Border.BorderBrushProperty, slide.Layout == info.Id ? "Accent.Fill" : "Thumb.Stroke");
            var label = new TextBlock { Text = info.Name, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(label);
            var button = new Button { Content = stack, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 2), ToolTip = info.Name + " – " + info.Description };
            button.SetResourceReference(StyleProperty, "CardButton");
            AutomationProperties.SetName(button, "Layout: " + info.Name);
            var id = info.Id;
            button.Click += (_, _) => editor.ApplyLayout(id);
            grid.Children.Add(button);
        }
        layouts.Children.Add(grid);

        // Background
        var bg = Section("Background");
        var current = slide.Background;
        var swatches = Swatches(current?.Kind == FillKind.Solid ? current.Color : current == null ? "theme" : null, c =>
        {
            if (c == null) editor.SetSlideBackground(null);
            else editor.SetSlideBackground(Fill.Solid(c));
        }, allowNone: false, "Background colour");
        // The first swatch resets to the theme's background.
        var themeSwatch = SwatchButton((Brush?)CurrentStyle.Brush(_state.Deck.Theme.Main.Background, new Rect(0, 0, 1, 1)) ?? Brushes.White, "Theme background", current == null, () => editor.SetSlideBackground(null));
        themeSwatch.Content = new IconView { Width = 12, Height = 12, Data = (Geometry)FindResource("Icon.Checkmark"), Foreground = Brushes.Gray, Visibility = current == null ? Visibility.Visible : Visibility.Collapsed };
        swatches.Children.Insert(0, themeSwatch);
        bg.Children.Add(swatches);
        var gradients = new WrapPanel { Margin = new Thickness(0, 2, -6, 4) };
        foreach (var (a, b, angle, name) in new[]
                 {
                     ("accent1", "accent2", 135.0, "Accent 1 to 2"), ("accent2", "accent3", 135.0, "Accent 2 to 3"),
                     ("accent1", "accent3", 160.0, "Accent 1 to 3"), ("bg", "bg2", 90.0, "Soft background"), ("#14142B", "accent1", 135.0, "Night"),
                 })
        {
            var brush = CurrentStyle.Brush(Fill.Linear(a, b, angle), new Rect(0, 0, 1, 1)) ?? Brushes.Gray;
            var selected = current?.Kind == FillKind.Linear && current.Color == a && current.Color2 == b;
            gradients.Children.Add(SwatchButton(brush, "Gradient: " + name, selected, () => editor.SetSlideBackground(Fill.Linear(a, b, angle))));
        }
        bg.Children.Add(gradients);
        var pictureRow = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        var picture = TextButton(current?.Kind == FillKind.Image ? "Change picture…" : "Picture…", "Icon.Image", () => editor.PickBackgroundPicture());
        picture.Margin = new Thickness(0);
        pictureRow.Children.Add(picture);
        bg.Children.Add(pictureRow);
        if (current?.Kind == FillKind.Image)
        {
            Row(bg, "Darken", Slider(current.Dim, 0, 0.8, v => editor.SetBackgroundDim(v), () => editor.SealHistory()));
        }
        Switch(bg, "Theme decorations", slide.ShowDecorations, on => editor.SetDecorations(on), "The shapes the theme draws behind the content");

        // Transition
        var tr = Section("Transition");
        var kinds = new[]
        {
            (TransitionKind.None, "None", "Icon.Dismiss"), (TransitionKind.Fade, "Fade", "Icon.SquareMultiple"), (TransitionKind.Push, "Push", "Icon.ArrowLeft"),
            (TransitionKind.Slide, "Slide over", "Icon.SlideMultiple"), (TransitionKind.Zoom, "Zoom", "Icon.ZoomIn"), (TransitionKind.Morph, "Morph", "Icon.Sparkle"),
        };
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 0, -3, 6) };
        foreach (var (kind, name, icon) in kinds)
        {
            var isOn = slide.Transition.Kind == kind;
            var content = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            var iv = new IconView { Width = 18, Height = 18, Data = (Geometry)FindResource(icon) };
            if (isOn) iv.SetResourceReference(IconView.ForegroundProperty, "Text.Accent");
            content.Children.Add(iv);
            content.Children.Add(new TextBlock { Text = name, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button { Content = content, Margin = new Thickness(3), ToolTip = kind == TransitionKind.Morph ? "Morph: elements that are on both slides glide into place" : name };
            b.SetResourceReference(StyleProperty, "CardButton");
            if (isOn) b.SetResourceReference(BackgroundProperty, "Accent.Selection");
            AutomationProperties.SetName(b, "Transition: " + name);
            var k = kind;
            b.Click += (_, _) => editor.SetTransition(k);
            tiles.Children.Add(b);
        }
        tr.Children.Add(tiles);
        if (slide.Transition.Kind != TransitionKind.None)
        {
            Row(tr, "Duration", Number(slide.Transition.Duration, 0.1, 5, 0.1, v => editor.SetTransitionDuration(v), " s", 1));
            if (slide.Transition.Kind is TransitionKind.Push or TransitionKind.Slide)
            {
                Row(tr, "From", Segments(new[]
                {
                    (TransitionDirection.Left, "From the right", (string?)"Icon.ArrowLeft"), (TransitionDirection.Right, "From the left", "Icon.ArrowRight"),
                    (TransitionDirection.Up, "From below", "Icon.ArrowUp"), (TransitionDirection.Down, "From above", "Icon.ArrowDown"),
                }, slide.Transition.Direction, d => editor.SetTransitionDirection(d), "Direction"));
            }
            if (slide.Transition.Kind == TransitionKind.Morph)
            {
                var hint = new TextBlock
                {
                    Text = "Duplicate a slide (Ctrl+D) and move, resize or recolour things: they glide between the two. Elements with the same name on both slides match too.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
                tr.Children.Add(hint);
            }
            tr.Children.Add(Grid2(TextButton("Preview", "Icon.Play", () => editor.PreviewTransition()), TextButton("Apply to all", "Icon.SlideMultiple", () => editor.ApplyTransitionToAll())));
        }

        // Options
        var opts = Section("Slide");
        Switch(opts, "Hide in the slide show", slide.Hidden, on => editor.SetHidden(on), "Hidden slides are skipped and left out of exports");
        var sectionBox = new TextBox { Text = slide.Section ?? "", Tag = "rebuild" };
        Ui.SetPlaceholder(sectionBox, "Starts a new section…");
        sectionBox.LostKeyboardFocus += (_, _) => editor.SetSection(sectionBox.Text);
        sectionBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) editor.SetSection(sectionBox.Text);
        };
        Row(opts, "Section", sectionBox);
        if (slide.RehearsedSeconds is { } secs)
        {
            var t = TimeSpan.FromSeconds(secs);
            var text = new TextBlock { Text = $"Rehearsed: {(int)t.TotalMinutes}:{t.Seconds:00}", FontSize = 12 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            opts.Children.Add(text);
        }
    }

    // ------------------------------------------------------------------ design (themes, palettes, size)

    private void BuildDesign()
    {
        var editor = _editor!;
        var deck = _state!.Deck;
        Title("Design", $"{deck.Theme.Name} · {Core.Model.SlideSizes.Describe(deck.Width, deck.Height)}");

        var themes = Section("Theme");
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var theme in ThemeCatalog.All)
        {
            var active = deck.Theme.Id == theme.Id;
            var image = new Image { Source = Previews.Theme(theme, active ? deck.Theme.Variant : null, 260, "Aa"), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(active ? 2 : 1),
                Padding = new Thickness(active ? 0 : 1),
                Child = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = image },
            };
            frame.SetResourceReference(Border.BorderBrushProperty, active ? "Accent.Fill" : "Thumb.Stroke");
            var label = new TextBlock { Text = theme.Name, FontSize = 12, Margin = new Thickness(1, 5, 0, 0), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal };
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(label);
            var b = new Button { Content = stack, Padding = new Thickness(4), ToolTip = theme.Description };
            b.SetResourceReference(StyleProperty, "CardButton");
            AutomationProperties.SetName(b, "Theme: " + theme.Name);
            var t = theme;
            b.Click += (_, _) => editor.ApplyTheme(t, null);
            grid.Children.Add(b);
        }
        themes.Children.Add(grid);

        var def = ThemeCatalog.Find(deck.Theme.Id);
        if (def != null)
        {
            var palettes = Section("Colours");
            foreach (var variant in def.Variants)
            {
                var sample = def.Create(variant.Id);
                var active = variant.Id == deck.Theme.Variant;
                var row = new DockPanel();
                var dots = new StackPanel { Orientation = Orientation.Horizontal };
                var bgColor = Core.Rendering.SlideStyle.ParseHex(sample.Main.Background.Color) ?? Colors.White;
                dots.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(bgColor), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 0, 4, 0) });
                foreach (var a in sample.Accents.Take(5))
                    dots.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Core.Rendering.SlideStyle.ParseHex(a) ?? Colors.Gray), Margin = new Thickness(0, 0, 4, 0) });
                DockPanel.SetDock(dots, Dock.Right);
                row.Children.Add(dots);
                row.Children.Add(new TextBlock { Text = variant.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal });
                var b = new Button { Content = row, Padding = new Thickness(10, 7, 8, 7), Margin = new Thickness(-10, 0, -8, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                b.SetResourceReference(StyleProperty, "CardButton");
                if (active) b.SetResourceReference(BackgroundProperty, "Accent.Selection");
                AutomationProperties.SetName(b, "Colours: " + variant.Name);
                var v = variant.Id;
                b.Click += (_, _) => editor.ApplyTheme(def, v);
                palettes.Children.Add(b);
            }
        }

        var fonts = Section("Fonts");
        var fontText = new TextBlock { Text = $"Headings: {deck.Theme.Fonts.Heading}\nText: {deck.Theme.Fonts.Body}", TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        fonts.Children.Add(fontText);

        var size = Section("Slide size");
        var current = Math.Abs(deck.Width / deck.Height - 16.0 / 9) < 0.01 ? "16:9" : Math.Abs(deck.Width / deck.Height - 4.0 / 3) < 0.01 ? "4:3" : "custom";
        size.Children.Add(Segments(new[] { ("16:9", "Widescreen 16:9", (string?)null), ("4:3", "Standard 4:3", null), ("custom", "Custom…", null) }, current, v =>
        {
            if (v == "16:9") editor.SetSlideSize(1920, 1080);
            else if (v == "4:3") editor.SetSlideSize(1440, 1080);
            else editor.AskCustomSlideSize();
        }, "Slide size"));
        var sizeText = new TextBlock
        {
            Text = $"{deck.Width / 144:0.##} × {deck.Height / 144:0.##} in ({deck.Width:0} × {deck.Height:0} units)",
            FontSize = 12,
            Margin = new Thickness(2, 8, 0, 0),
        };
        sizeText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        size.Children.Add(sizeText);
    }
}
