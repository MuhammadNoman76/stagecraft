using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;
using Stagecraft.Services;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

public partial class EditorView
{
    private IReadOnlyList<Slide> TargetSlides()
    {
        var ids = _state.SelectedSlides;
        var list = Session.Deck.Slides.Where(s => ids.Contains(s.Id)).ToList();
        if (list.Count == 0 && _state.CurrentSlide != null) list.Add(_state.CurrentSlide);
        return list;
    }

    // ------------------------------------------------------------------ add, duplicate, delete, move

    public void NewSlide(string? layoutId)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        var current = _state.CurrentSlide;
        var layout = layoutId ?? DeckOperations.NextLayout(current);
        var slide = LayoutEngine.CreateSlide(Session.Deck, layout);
        // Keep the transition style of the slide before it.
        if (current != null && current.Transition.Kind != TransitionKind.None && current.Transition.Kind != TransitionKind.Morph) slide.Transition = current.Transition.Clone();
        var index = current == null ? Session.Deck.Slides.Count : _state.CurrentIndex + 1;
        Session.EditStructure("New slide", d => d.Slides.Insert(index, slide));
        _state.GoTo(slide);
        // Start typing the title right away.
        var title = slide.Elements.FirstOrDefault(e => e.Placeholder == "title");
        if (title != null && _altViewIndex == 0) Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_state.CurrentSlide == slide) Canvas.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnNewSlide(object sender, RoutedEventArgs e) => NewSlide(null);

    private void OnNewSlideMenu(object sender, RoutedEventArgs e) => ShowLayoutPicker(LayoutDrop, id => NewSlide(id), "New slide");

    /// <summary>A popup grid of layout previews.</summary>
    public void ShowLayoutPicker(FrameworkElement target, Action<string> picked, string title)
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(6) };
        Popup? popup = null;
        foreach (var info in LayoutEngine.All)
        {
            var image = new Image { Source = Previews.Layout(Session.Deck, info.Id, 200), Width = 132, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = image } };
            frame.SetResourceReference(Border.BorderBrushProperty, "Thumb.Stroke");
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(new TextBlock { Text = info.Name, FontSize = 12, Margin = new Thickness(1, 5, 0, 0) });
            var button = new Button { Content = stack, Padding = new Thickness(6), ToolTip = info.Description };
            button.SetResourceReference(StyleProperty, "CardButton");
            System.Windows.Automation.AutomationProperties.SetName(button, info.Name);
            var id = info.Id;
            button.Click += (_, _) =>
            {
                popup!.IsOpen = false;
                picked(id);
            };
            grid.Children.Add(button);
        }
        var header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 10, 0, 0) };
        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(grid);
        var surface = new Border { Child = root, Margin = new Thickness(8, 4, 8, 16) };
        surface.SetResourceReference(StyleProperty, "FlyoutSurface");
        popup = new Popup { Child = surface, PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        popup.IsOpen = true;
        if (grid.Children.Count > 0) ((Button)grid.Children[0]).Focus();
    }

    public void DuplicateSlides()
    {
        var slides = TargetSlides();
        if (slides.Count == 0) return;
        var copies = new List<Slide>();
        Session.EditStructure(slides.Count == 1 ? "Duplicate slide" : "Duplicate slides", d =>
        {
            var insertAt = slides.Max(s => d.Slides.IndexOf(s)) + 1;
            foreach (var s in slides)
            {
                var copy = DeckOperations.DuplicateSlide(s);
                // A duplicate is the classic Morph set-up: morph into the copy.
                if (s.Transition.Kind == TransitionKind.None) copy.Transition = new Transition { Kind = TransitionKind.Morph, Duration = 1.0 };
                copies.Add(copy);
            }
            d.Slides.InsertRange(insertAt, copies);
        });
        _state.GoTo(copies.Last());
        _window.ShowMessage("Duplicated, with a Morph transition: move, resize or recolour things and they'll glide between the two slides.", 6);
    }

    public void DeleteSlides()
    {
        var slides = TargetSlides();
        if (slides.Count == 0) return;
        var index = _state.CurrentIndex;
        Session.EditStructure(slides.Count == 1 ? "Delete slide" : "Delete slides", d =>
        {
            foreach (var s in slides) d.Slides.Remove(s);
        });
        var remaining = Session.Deck.Slides;
        _state.GoTo(remaining.Count == 0 ? null : remaining[MathEx.Clamp(index, 0, remaining.Count - 1)]);
    }

    public void MoveSlides(IReadOnlyList<Slide> slides, int insertIndex)
    {
        var deck = Session.Deck;
        var before = deck.Slides.ToList();
        var moving = before.Where(slides.Contains).ToList();
        if (moving.Count == 0) return;
        // Index among the remaining slides.
        var targetIndex = insertIndex - before.Take(insertIndex).Count(slides.Contains);
        var remaining = before.Where(s => !slides.Contains(s)).ToList();
        targetIndex = MathEx.Clamp(targetIndex, 0, remaining.Count);
        remaining.InsertRange(targetIndex, moving);
        if (remaining.SequenceEqual(before)) return;
        Session.EditStructure(moving.Count == 1 ? "Move slide" : "Move slides", d => d.Slides = remaining);
        _state.GoTo(moving[0], keepSlideSelection: true);
        Rail.SyncSelection();
    }

    private void MoveCurrentSlide(int direction)
    {
        var slides = TargetSlides();
        if (slides.Count == 0) return;
        var first = Session.Deck.Slides.IndexOf(slides[0]);
        var last = Session.Deck.Slides.IndexOf(slides[slides.Count - 1]);
        MoveSlides(slides, direction < 0 ? Math.Max(0, first - 1) : Math.Min(Session.Deck.Slides.Count, last + 2));
    }

    // ------------------------------------------------------------------ slide properties

    public void ApplyLayout(string layoutId)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        var slides = TargetSlides();
        if (slides.Count == 1)
        {
            Session.EditSlide(slides[0], "Change layout", s => LayoutEngine.Apply(Session.Deck, s, layoutId));
        }
        else
        {
            Session.EditDeck("Change layout", d =>
            {
                foreach (var s in d.Slides.Where(x => slides.Any(t => t.Id == x.Id))) LayoutEngine.Apply(d, s, layoutId);
            });
        }
        _state.ClearSelection();
    }

    private void EditSlides(string label, Action<Slide> change, string? mergeKey = null)
    {
        var slides = TargetSlides();
        if (slides.Count == 1) Session.EditSlide(slides[0], label, change, mergeKey);
        else if (slides.Count > 1)
        {
            var ids = slides.Select(s => s.Id).ToList();
            Session.EditDeck(label, d =>
            {
                foreach (var s in d.Slides.Where(x => ids.Contains(x.Id))) change(s);
            });
        }
    }

    public void SetSlideBackground(Fill? fill) => EditSlides("Background", s => s.Background = fill?.Clone());

    public void SetBackgroundDim(double dim) => EditSlides("Darken background", s =>
    {
        if (s.Background is { Kind: FillKind.Image } bg) bg.Dim = Math.Round(dim, 2);
    }, "bgdim");

    public void SetDecorations(bool on) => EditSlides(on ? "Show decorations" : "Hide decorations", s => s.ShowDecorations = on);

    public void PickBackgroundPicture()
    {
        var picture = PickPictureFile("Choose a background picture");
        if (picture == null) return;
        var name = Session.Media.Add(picture.Value.Data, picture.Value.Extension);
        EditSlides("Background picture", s => s.Background = Fill.Image(name, 0.35));
    }

    public void SetTransition(TransitionKind kind) => EditSlides("Transition", s =>
    {
        var previous = s.Transition.Kind;
        s.Transition.Kind = kind;
        if (previous == TransitionKind.None || Math.Abs(s.Transition.Duration - Transition.DefaultDuration(previous)) < 0.01)
            s.Transition.Duration = Transition.DefaultDuration(kind);
    });

    public void SetTransitionDuration(double seconds) => EditSlides("Transition duration", s => s.Transition.Duration = seconds, "trduration");

    public void SetTransitionDirection(TransitionDirection direction) => EditSlides("Transition direction", s => s.Transition.Direction = direction);

    public void ApplyTransitionToAll()
    {
        var source = _state.CurrentSlide?.Transition;
        if (source == null) return;
        Session.EditDeck("Transition for all slides", d =>
        {
            foreach (var s in d.Slides) s.Transition = source.Clone();
        });
        _window.ShowMessage("Every slide now uses this transition.");
    }

    public void SetHidden(bool hidden)
    {
        var slides = TargetSlides();
        Session.EditStructure(hidden ? "Hide slide" : "Show slide", _ =>
        {
            foreach (var s in slides) s.Hidden = hidden;
        });
    }

    public void SetSection(string? name)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var value = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        if (value == slide.Section) return;
        Session.EditStructure(value == null ? "Remove section" : "Section", _ => slide.Section = value);
    }

    private void AskSection()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var name = InputDialog.Ask(_window, "Start a section", "Sections group slides in the slide rail and the sorter.", slide.Section ?? "New section", "Add section");
        if (name != null) SetSection(name);
    }

    // ------------------------------------------------------------------ design

    public void ApplyTheme(ThemeDefinition theme, string? variant)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        Session.EditDeck("Theme", d => DeckOperations.ApplyTheme(d, theme, variant));
        AppSettings.Update(s =>
        {
            s.LastTheme = theme.Id;
            s.LastVariant = variant;
        });
        _thumbs.Clear();
        Rail.Rebuild();
        Panel.RebuildNow();
    }

    public void SetSlideSize(double width, double height)
    {
        if (Math.Abs(Session.Deck.Width - width) < 0.5 && Math.Abs(Session.Deck.Height - height) < 0.5) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        Session.EditDeck("Slide size", d => DeckOperations.SetSlideSize(d, width, height));
        _thumbs.Clear();
        Canvas.FitToWindow();
        Rail.Rebuild();
        Panel.RebuildNow();
    }

    public void AskCustomSlideSize()
    {
        var deck = Session.Deck;
        var text = InputDialog.Ask(_window, "Custom slide size", "Width × height in inches (for example 10 × 5.625).",
            string.Format(CultureInfo.CurrentCulture, "{0:0.##} × {1:0.##}", deck.Width / 144, deck.Height / 144), "Set size");
        if (text == null) return;
        var parts = text.Split(new[] { 'x', 'X', '×', '*', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out var w) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.CurrentCulture, out var h) || w < 2 || h < 2 || w > 56 || h > 56)
        {
            MessageDialog.Show(_window, "That size doesn't work", "Use two numbers between 2 and 56 inches, like 10 × 7.5.", "OK", icon: DialogIcon.Warning);
            return;
        }
        // Keep the height at 1080 units so the theme's type sizes stay as designed.
        SetSlideSize(Math.Round(1080 * w / h), 1080);
    }

    // ------------------------------------------------------------------ rail context menu

    private void ShowRailMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, string icon, Action action, string? gesture = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }
        var hasSlide = _state.CurrentSlide != null;
        Item("New slide", "Icon.SlideAdd", () => NewSlide(null), "Ctrl+M");
        var layouts = new MenuItem { Header = "New slide with layout" };
        Ui.SetIcon(layouts, (Geometry)FindResource("Icon.SlideLayout"));
        foreach (var l in LayoutEngine.All)
        {
            var id = l.Id;
            var mi = new MenuItem { Header = l.Name };
            mi.Click += (_, _) => NewSlide(id);
            layouts.Items.Add(mi);
        }
        menu.Items.Add(layouts);
        Item("Duplicate", "Icon.SlideMultiple", DuplicateSlides, "Ctrl+D", hasSlide);
        Item("Delete", "Icon.Delete", DeleteSlides, "Del", hasSlide);
        menu.Items.Add(new Separator());
        var hidden = _state.CurrentSlide?.Hidden == true;
        Item(hidden ? "Show slide" : "Hide slide", hidden ? "Icon.Eye" : "Icon.EyeOff", () => SetHidden(!hidden), null, hasSlide);
        Item(_state.CurrentSlide?.Section != null ? "Rename section…" : "Add section…", "Icon.TextHeader1", AskSection, null, hasSlide);
        if (_state.CurrentSlide?.Section != null) Item("Remove section", "Icon.Dismiss", () => SetSection(null));
        menu.Items.Add(new Separator());
        Item("Copy", "Icon.Copy", CopySlides, "Ctrl+C", hasSlide);
        Item("Paste", "Icon.ClipboardPaste", Paste, "Ctrl+V", SlideClipboard.HasSlides());
        Item("Present from this slide", "Icon.Play", () => Present(fromCurrent: true), "Shift+F5", hasSlide);
        menu.PlacementTarget = Rail;
        menu.IsOpen = true;
    }
}
