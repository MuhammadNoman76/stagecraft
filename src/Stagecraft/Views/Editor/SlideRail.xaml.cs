using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>One row of the slide rail.</summary>
public sealed class RailItem : INotifyPropertyChanged
{
    private BitmapSource? _thumb;
    private int _number;
    private bool _hidden;
    private string? _section;
    private bool _isCurrent;
    private bool _hasTransition;
    private double _thumbHeight = 100;

    public RailItem(Slide slide)
    {
        Slide = slide;
    }

    public Slide Slide { get; }

    public int Number { get => _number; set => Set(ref _number, value, nameof(Number)); }
    public bool Hidden { get => _hidden; set => Set(ref _hidden, value, nameof(Hidden)); }
    public bool HasTransition { get => _hasTransition; set => Set(ref _hasTransition, value, nameof(HasTransition)); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value, nameof(IsCurrent)); }
    public double ThumbHeight { get => _thumbHeight; set => Set(ref _thumbHeight, value, nameof(ThumbHeight)); }
    public BitmapSource? Thumb { get => _thumb; set => Set(ref _thumb, value, nameof(Thumb)); }

    public string? Section
    {
        get => _section;
        set
        {
            Set(ref _section, value, nameof(Section));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSection)));
        }
    }

    public bool HasSection => !string.IsNullOrWhiteSpace(_section);

    public override string ToString() => $"Slide {Number}" + (Hidden ? ", hidden" : "") + ": " + (Slide.TitleText.Length > 0 ? Slide.TitleText : "no title");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>The slide thumbnails on the left: virtualized, drag to reorder, sections, context menu.</summary>
public partial class SlideRail
{
    public const string SlideDragFormat = "Stagecraft.Slides";

    private readonly ObservableCollection<RailItem> _items = new();
    private EditorState? _state;
    private ThumbnailCache? _thumbs;
    private bool _syncing;
    private Point _dragStart;
    private bool _dragArmed;

    public SlideRail()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        List.SelectionChanged += OnSelectionChanged;
        List.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(List);
            _dragArmed = ItemAt(e.OriginalSource) != null;
        };
        List.PreviewMouseMove += OnPreviewMouseMove;
        List.DragOver += OnDragOver;
        List.DragLeave += (_, _) => DropLine.Visibility = Visibility.Collapsed;
        List.Drop += OnDrop;
        List.PreviewKeyDown += OnKey;
        List.MouseRightButtonUp += (_, e) =>
        {
            ContextRequested?.Invoke();
            e.Handled = true;
        };
        AddButton.Click += (_, _) => AddRequested?.Invoke();
        SizeChanged += (_, _) => UpdateThumbHeights();
    }

    public event Action? AddRequested;
    public event Action? ContextRequested;
    public event Action? DeleteRequested;
    public event Action? DuplicateRequested;
    public event Action<IReadOnlyList<Slide>, int>? MoveRequested;

    public ListBox ListBox => List;

    public void Attach(EditorState state, ThumbnailCache thumbs)
    {
        _state = state;
        _thumbs = thumbs;
        state.CurrentSlideChanged += SyncSelection;
        Rebuild();
    }

    private double ThumbWidth => Math.Max(60, ActualWidth - 26 - 4 - 22);

    private double ThumbHeightFor => _state == null ? 100 : ThumbWidth * _state.Deck.Height / _state.Deck.Width;

    private int PixelWidth
    {
        get
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return (int)Math.Ceiling(Math.Max(200, ThumbWidth) * Math.Max(1, dpi));
        }
    }

    /// <summary>Rebuilds the list after slides were added, removed, reordered, hidden or the deck changed.</summary>
    public void Rebuild()
    {
        if (_state == null) return;
        _syncing = true;
        try
        {
            var slides = _state.Deck.Slides;
            // Reuse items for slides that are still there (keeps thumbnails while they re-render).
            var existing = _items.ToDictionary(i => i.Slide);
            _items.Clear();
            var number = 0;
            foreach (var s in slides)
            {
                var item = existing.TryGetValue(s, out var old) ? old : new RailItem(s);
                item.Number = ++number;
                _items.Add(item);
                UpdateItem(item);
            }
            CountText.Text = slides.Count == 1 ? "1 slide" : $"{slides.Count} slides";
        }
        finally
        {
            _syncing = false;
        }
        SyncSelection();
    }

    /// <summary>A slide's content changed: refresh its thumbnail and flags.</summary>
    public void RefreshSlide(string slideId)
    {
        var item = _items.FirstOrDefault(i => i.Slide.Id == slideId);
        if (item != null) UpdateItem(item);
    }

    private void UpdateItem(RailItem item)
    {
        item.Hidden = item.Slide.Hidden;
        item.Section = item.Slide.Section;
        item.HasTransition = item.Slide.Transition.Kind != TransitionKind.None;
        item.ThumbHeight = ThumbHeightFor;
        item.IsCurrent = _state?.CurrentSlide == item.Slide;
        if (_thumbs == null) return;
        var image = _thumbs.Get(item.Slide, PixelWidth, bmp => item.Thumb = bmp);
        if (image != null) item.Thumb = image;
    }

    private void UpdateThumbHeights()
    {
        foreach (var item in _items)
        {
            item.ThumbHeight = ThumbHeightFor;
        }
    }

    public void SyncSelection()
    {
        if (_state == null) return;
        _syncing = true;
        try
        {
            List.SelectedItems.Clear();
            foreach (var item in _items)
            {
                item.IsCurrent = item.Slide == _state.CurrentSlide;
                if (_state.SelectedSlides.Contains(item.Slide.Id)) List.SelectedItems.Add(item);
            }
            var current = _items.FirstOrDefault(i => i.IsCurrent);
            if (current != null) List.ScrollIntoView(current);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _state == null) return;
        var selected = List.SelectedItems.Cast<RailItem>().ToList();
        if (selected.Count == 0) return;
        _state.SelectedSlides.Clear();
        foreach (var item in selected) _state.SelectedSlides.Add(item.Slide.Id);
        var target = e.AddedItems.Count > 0 ? ((RailItem)e.AddedItems[e.AddedItems.Count - 1]!).Slide : selected[0].Slide;
        if (!selected.Any(i => i.Slide == _state.CurrentSlide) || selected.Count == 1) _state.GoTo(target, keepSlideSelection: true);
        foreach (var item in _items) item.IsCurrent = item.Slide == _state.CurrentSlide;
    }

    public IReadOnlyList<Slide> SelectedSlides =>
        _items.Where(i => _state != null && _state.SelectedSlides.Contains(i.Slide.Id)).Select(i => i.Slide).ToList();

    // ------------------------------------------------------------------ keyboard

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key is Key.Delete or Key.Back && !ctrl)
        {
            DeleteRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.D && ctrl)
        {
            DuplicateRequested?.Invoke();
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.Up or Key.Down && _state != null)
        {
            var slides = SelectedSlides;
            if (slides.Count == 0) return;
            var first = _state.Deck.Slides.IndexOf(slides[0]);
            var last = _state.Deck.Slides.IndexOf(slides[slides.Count - 1]);
            var target = e.Key == Key.Up ? Math.Max(0, first - 1) : Math.Min(_state.Deck.Slides.Count, last + 2);
            MoveRequested?.Invoke(slides, target);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ drag to reorder

    private RailItem? ItemAt(object source)
    {
        var node = source as DependencyObject;
        while (node != null && node is not ListBoxItem) node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return (node as ListBoxItem)?.DataContext as RailItem;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _state == null) return;
        var pos = e.GetPosition(List);
        if (Math.Abs(pos.Y - _dragStart.Y) < 6 && Math.Abs(pos.X - _dragStart.X) < 6) return;
        _dragArmed = false;
        var slides = SelectedSlides;
        if (slides.Count == 0) return;
        var data = new DataObject(SlideDragFormat, string.Join(",", slides.Select(s => s.Id)));
        try
        {
            DragDrop.DoDragDrop(List, data, DragDropEffects.Move);
        }
        finally
        {
            DropLine.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>The index a drop at <paramref name="y"/> (list coordinates) inserts at.</summary>
    private int InsertIndexAt(double y, out double lineY)
    {
        lineY = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container || !container.IsVisible) continue;
            var top = container.TranslatePoint(new Point(0, 0), List).Y;
            var bottom = top + container.ActualHeight;
            if (y < top + container.ActualHeight / 2)
            {
                lineY = top;
                return i;
            }
            lineY = bottom;
            if (y < bottom) return i + 1;
        }
        return _items.Count;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SlideDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        var pos = e.GetPosition(List);
        InsertIndexAt(pos.Y, out var lineY);
        DropLine.Width = Math.Max(10, List.ActualWidth - 44);
        Canvas.SetLeft(DropLine, 32);
        Canvas.SetTop(DropLine, lineY - 1.5);
        DropLine.Visibility = Visibility.Visible;
        // Scroll near the edges.
        var sv = FindScrollViewer(List);
        if (sv != null)
        {
            if (pos.Y < 30) sv.ScrollToVerticalOffset(sv.VerticalOffset - 12);
            else if (pos.Y > List.ActualHeight - 30) sv.ScrollToVerticalOffset(sv.VerticalOffset + 12);
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropLine.Visibility = Visibility.Collapsed;
        if (_state == null || e.Data.GetData(SlideDragFormat) is not string ids) return;
        var set = ids.Split(',');
        var slides = _state.Deck.Slides.Where(s => set.Contains(s.Id)).ToList();
        var index = InsertIndexAt(e.GetPosition(List).Y, out _);
        if (slides.Count > 0) MoveRequested?.Invoke(slides, index);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }
}
