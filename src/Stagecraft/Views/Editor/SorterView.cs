using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The slide sorter: every slide as a large thumbnail grouped by section. Drag to reorder, multi-select with
/// Ctrl / Shift, double-click (or Enter) opens a slide in the editor, Delete / Ctrl+D / H act on the selection.
/// </summary>
public sealed class SorterView : UserControl, IAlternateView
{
    private const double ThumbWidth = 248;
    private readonly EditorView _editor;
    private readonly EditorState _state;
    private readonly ThumbnailCache _thumbs;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly WrapPanel _panel = new() { Margin = new Thickness(28, 16, 28, 40) };
    private readonly Canvas _dropLayer = new() { IsHitTestVisible = false };
    private readonly Border _dropLine = new() { Width = 4, CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed };
    private readonly List<(Slide Slide, Border Frame, FrameworkElement Card)> _cards = new();
    private Slide? _anchor;
    private Point _dragStart;
    private Slide? _dragCandidate;

    public SorterView(EditorView editor, EditorState state, ThumbnailCache thumbs)
    {
        _editor = editor;
        _state = state;
        _thumbs = thumbs;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        AutomationProperties.SetName(this, "Slide sorter");
        _scroll.Content = _panel;
        _dropLine.SetResourceReference(Border.BackgroundProperty, "Accent.Fill");
        _dropLayer.Children.Add(_dropLine);
        var root = new Grid();
        root.Children.Add(_scroll);
        root.Children.Add(_dropLayer);
        var layer = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), BorderThickness = new Thickness(1, 1, 1, 0), Child = root, ClipToBounds = true };
        layer.SetResourceReference(Border.BackgroundProperty, "Canvas.Background");
        layer.SetResourceReference(Border.BorderBrushProperty, "Layer.Stroke");
        Content = layer;
        KeyDown += OnKey;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _dropLine.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        PreviewMouseMove += OnPreviewMouseMove;
        SizeChanged += (_, _) => Refresh();
    }

    public void FocusView() => Focus();

    public void Refresh()
    {
        _panel.Children.Clear();
        _cards.Clear();
        var slides = _state.Deck.Slides;
        var thumbHeight = ThumbWidth * _state.Deck.Height / _state.Deck.Width;
        var pixel = (int)(ThumbWidth * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        var available = Math.Max(300, ActualWidth - 70);
        for (var i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            if (!string.IsNullOrWhiteSpace(slide.Section))
            {
                var header = new DockPanel { Width = available, Margin = new Thickness(4, i == 0 ? 4 : 18, 0, 10) };
                var bar = new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
                bar.SetResourceReference(Border.BackgroundProperty, "Accent.Fill");
                header.Children.Add(bar);
                header.Children.Add(new TextBlock { Text = slide.Section, FontSize = 16, FontWeight = FontWeights.SemiBold });
                _panel.Children.Add(header);
            }
            var picture = new Border { CornerRadius = new CornerRadius(6), Width = ThumbWidth, Height = thumbHeight };
            var brush = new ImageBrush { Stretch = Stretch.UniformToFill };
            picture.Background = brush;
            var image = _thumbs.Get(slide, pixel, bmp => brush.ImageSource = bmp);
            if (image != null) brush.ImageSource = image;
            if (slide.Hidden) picture.Opacity = 0.4;
            var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = picture };
            var back = new Border { CornerRadius = new CornerRadius(6), Width = ThumbWidth, Height = thumbHeight, BorderThickness = new Thickness(1) };
            back.SetResourceReference(Border.BackgroundProperty, "Card.Background");
            back.SetResourceReference(Border.BorderBrushProperty, "Thumb.Stroke");
            var stack = new Grid();
            stack.Children.Add(new Border { Padding = new Thickness(2), Child = back });
            stack.Children.Add(frame);
            var label = new DockPanel { Margin = new Thickness(4, 6, 4, 0) };
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
            label.Children.Add(number);
            if (slide.Hidden)
            {
                var hidden = new IconView { Width = 12, Height = 12, Data = (Geometry)FindResource("Icon.EyeOff"), Margin = new Thickness(0, 0, 6, 0) };
                hidden.SetResourceReference(IconView.ForegroundProperty, "Text.Tertiary");
                label.Children.Add(hidden);
            }
            if (slide.Transition.Kind != TransitionKind.None)
            {
                var tr = new TextBlock { Text = slide.Transition.Kind == TransitionKind.Slide ? "Slide over" : slide.Transition.Kind.ToString(), FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
                tr.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
                DockPanel.SetDock(tr, Dock.Right);
                label.Children.Add(tr);
            }
            var title = new TextBlock { Text = slide.TitleText.Length > 0 ? slide.TitleText : " ", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            label.Children.Add(title);
            var card = new StackPanel { Width = ThumbWidth + 8, Margin = new Thickness(0, 0, 18, 20), Background = Brushes.Transparent };
            card.Children.Add(stack);
            card.Children.Add(label);
            AutomationProperties.SetName(card, $"Slide {i + 1}{(slide.Hidden ? ", hidden" : "")}: {slide.TitleText}");
            var s = slide;
            card.MouseLeftButtonDown += (_, e) => OnCardDown(s, e);
            card.MouseRightButtonUp += (_, e) =>
            {
                if (!_state.SelectedSlides.Contains(s.Id)) SelectOnly(s);
                ShowMenu();
                e.Handled = true;
            };
            _panel.Children.Add(card);
            _cards.Add((slide, frame, card));
        }
        UpdateSelectionLook();
    }

    private void UpdateSelectionLook()
    {
        foreach (var (slide, frame, _) in _cards)
        {
            var on = _state.SelectedSlides.Contains(slide.Id);
            if (on) frame.SetResourceReference(Border.BorderBrushProperty, "Accent.Fill");
            else frame.BorderBrush = Brushes.Transparent;
        }
    }

    private void SelectOnly(Slide slide)
    {
        _state.GoTo(slide);
        _anchor = slide;
        UpdateSelectionLook();
    }

    private void OnCardDown(Slide slide, MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            _editor.OpenInEditor(slide);
            e.Handled = true;
            return;
        }
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0)
        {
            if (!_state.SelectedSlides.Remove(slide.Id)) _state.SelectedSlides.Add(slide.Id);
            if (_state.SelectedSlides.Count == 0) _state.SelectedSlides.Add(slide.Id);
            _state.GoTo(_state.SelectedSlides.Contains(slide.Id) ? slide : _state.CurrentSlide, keepSlideSelection: true);
            _anchor = slide;
        }
        else if ((mods & ModifierKeys.Shift) != 0 && _anchor != null)
        {
            var slides = _state.Deck.Slides;
            var a = slides.IndexOf(_anchor);
            var b = slides.IndexOf(slide);
            _state.SelectedSlides.Clear();
            for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) _state.SelectedSlides.Add(slides[i].Id);
            _state.GoTo(slide, keepSlideSelection: true);
        }
        else if (!_state.SelectedSlides.Contains(slide.Id))
        {
            SelectOnly(slide);
        }
        _dragStart = e.GetPosition(this);
        _dragCandidate = slide;
        UpdateSelectionLook();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if ((pos - _dragStart).Length < 8) return;
        _dragCandidate = null;
        var ids = string.Join(",", _state.SelectedSlides);
        DragDrop.DoDragDrop(this, new DataObject(SlideRail.SlideDragFormat, ids), DragDropEffects.Move);
        _dropLine.Visibility = Visibility.Collapsed;
    }

    /// <summary>Insertion index for a drop point, and where to draw the caret.</summary>
    private int InsertIndexAt(Point p, out Point caret)
    {
        caret = new Point();
        var best = _cards.Count;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i].Card;
            var topLeft = card.TranslatePoint(new Point(0, 0), _dropLayer);
            var rect = new Rect(topLeft, new Size(card.ActualWidth, card.ActualHeight));
            foreach (var (x, index) in new[] { (rect.Left - 9, i), (rect.Right + 9, i + 1) })
            {
                var cy = rect.Top + rect.Height / 2;
                var d = Math.Abs(p.X - x) + Math.Abs(p.Y - cy) * 2;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = index;
                    caret = new Point(x, rect.Top);
                }
            }
        }
        return best;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SlideRail.SlideDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        var p = e.GetPosition(_dropLayer);
        InsertIndexAt(p, out var caret);
        _dropLine.Height = ThumbWidth * _state.Deck.Height / _state.Deck.Width + 8;
        Canvas.SetLeft(_dropLine, caret.X - 2);
        Canvas.SetTop(_dropLine, caret.Y);
        _dropLine.Visibility = Visibility.Visible;
        if (p.Y < 40) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - 14);
        else if (p.Y > ActualHeight - 40) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + 14);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropLine.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(SlideRail.SlideDragFormat) is not string ids) return;
        var set = ids.Split(',');
        var slides = _state.Deck.Slides.Where(s => set.Contains(s.Id)).ToList();
        var index = InsertIndexAt(e.GetPosition(_dropLayer), out _);
        _editor.MoveSlides(slides, index);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var slides = _state.Deck.Slides;
        var current = _state.CurrentIndex;
        switch (e.Key)
        {
            case Key.Enter:
                if (_state.CurrentSlide != null) _editor.OpenInEditor(_state.CurrentSlide);
                e.Handled = true;
                break;
            case Key.H:
                if (Keyboard.Modifiers == ModifierKeys.None && _state.CurrentSlide != null) _editor.SetHidden(!_state.CurrentSlide.Hidden);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                if (slides.Count == 0 || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                var perRow = Math.Max(1, (int)((ActualWidth - 56) / (ThumbWidth + 26)));
                var delta = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -perRow, _ => perRow };
                var next = MathEx.Clamp(current + delta, 0, slides.Count - 1);
                SelectOnly(slides[next]);
                _cards.FirstOrDefault(c => c.Slide == slides[next]).Card?.BringIntoView();
                e.Handled = true;
                break;
        }
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        void Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        var hidden = _state.CurrentSlide?.Hidden == true;
        Item("Open in the editor", "Icon.Edit", () => { if (_state.CurrentSlide != null) _editor.OpenInEditor(_state.CurrentSlide); }, "Enter");
        Item("Duplicate", "Icon.SlideMultiple", _editor.DuplicateSlides, "Ctrl+D");
        Item("Delete", "Icon.Delete", _editor.DeleteSlides, "Del");
        Item(hidden ? "Show slide" : "Hide slide", hidden ? "Icon.Eye" : "Icon.EyeOff", () => _editor.SetHidden(!hidden), "H");
        menu.IsOpen = true;
    }
}
