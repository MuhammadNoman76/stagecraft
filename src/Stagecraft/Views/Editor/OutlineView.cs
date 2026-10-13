using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Stagecraft.Controls;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The outline: type a talk quickly. One row per slide with its title and its points (one per line; indent with
/// two spaces). Enter in a title jumps to its points; Ctrl+Enter starts a new slide.
/// </summary>
public sealed class OutlineView : UserControl, IAlternateView
{
    private readonly EditorView _editor;
    private readonly EditorState _state;
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 20, 0, 60), MaxWidth = 860 };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly List<(Slide Slide, TextBox Title, TextBox? Body)> _items = new();
    private bool _refreshing;

    public OutlineView(EditorView editor, EditorState state)
    {
        _editor = editor;
        _state = state;
        Focusable = false;
        AutomationProperties.SetName(this, "Outline");
        var root = new StackPanel();
        var header = new TextBlock { Text = "Outline", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 28, 0, 2), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch };
        header.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var hint = new TextBlock
        {
            Text = "Type your talk: a title and its points for each slide. Enter moves to the points, Ctrl+Enter starts a new slide. Start a line with two spaces to indent it.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 860,
            Margin = new Thickness(0, 0, 0, 6),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        root.Children.Add(Center(header));
        root.Children.Add(Center(hint));
        root.Children.Add(Center(_rows));
        var add = new Button { Content = "New slide", Margin = new Thickness(0, 0, 0, 40), HorizontalAlignment = HorizontalAlignment.Center };
        add.SetResourceReference(StyleProperty, "IconTextButton");
        Ui.SetIcon(add, (System.Windows.Media.Geometry)FindResource("Icon.Add"));
        add.Click += (_, _) => AddAfter(_state.Deck.Slides.LastOrDefault());
        root.Children.Add(add);
        _scroll.Content = root;
        var layer = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), BorderThickness = new Thickness(1, 1, 1, 0), Child = _scroll, Margin = new Thickness(0, 0, 0, 0) };
        layer.SetResourceReference(Border.BackgroundProperty, "Layer.Background");
        layer.SetResourceReference(Border.BorderBrushProperty, "Layer.Stroke");
        Content = layer;
    }

    private static FrameworkElement Center(FrameworkElement e)
    {
        e.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new Border { Padding = new Thickness(40, 0, 40, 0), Child = e, HorizontalAlignment = HorizontalAlignment.Center, Width = 940 };
    }

    public void FocusView()
    {
        var item = _items.FirstOrDefault(i => i.Slide == _state.CurrentSlide);
        (item.Title ?? _items.FirstOrDefault().Title)?.Focus();
    }

    public void Refresh()
    {
        if (_refreshing) return;
        // Don't rebuild under the cursor while typing; the boxes already hold the text.
        if (IsKeyboardFocusWithin && _items.Count == _state.Deck.Slides.Count && _items.Select(i => i.Slide).SequenceEqual(_state.Deck.Slides)) return;
        _rows.Children.Clear();
        _items.Clear();
        var slides = _state.Deck.Slides;
        for (var i = 0; i < slides.Count; i++) _rows.Children.Add(BuildRow(slides[i], i + 1));
    }

    private FrameworkElement BuildRow(Slide slide, int number)
    {
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var badge = new Border { Width = 30, Height = 24, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 4, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.SetResourceReference(Border.BackgroundProperty, slide == _state.CurrentSlide ? "Accent.Selection" : "Subtle.FillSecondary");
        badge.Child = new TextBlock { Text = number.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);

        var stack = new StackPanel();
        var titleElement = slide.FindPlaceholder<TextElement>("title");
        var title = new TextBox
        {
            Text = titleElement?.Text.Content.PlainText.Replace('\n', ' ') ?? slide.TitleText,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            IsReadOnly = titleElement == null,
            Padding = new Thickness(4, 2, 4, 2),
        };
        Ui.SetPlaceholder(title, titleElement == null ? "(This layout has no title)" : "Slide title");
        AutomationProperties.SetName(title, $"Slide {number} title");
        stack.Children.Add(title);

        var bodyElement = slide.FindPlaceholder<TextElement>("body");
        TextBox? body = null;
        if (bodyElement != null)
        {
            body = new TextBox
            {
                Text = ToOutline(bodyElement.Text.Content),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(22, 2, 4, 4),
                MinHeight = 28,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            Ui.SetPlaceholder(body, "Points, one per line");
            AutomationProperties.SetName(body, $"Slide {number} points");
            stack.Children.Add(body);
        }
        else
        {
            var other = string.Join("  ·  ", slide.Elements.OfType<TextElement>().Where(t => t.Placeholder != "title" && t.Placeholder != "decor" && !t.Text.Content.IsEmpty)
                .Select(t => t.Text.Content.PlainText.Replace('\n', ' ')).Take(4));
            var note = new TextBlock { Text = other.Length > 0 ? other : LayoutEngine.Find(slide.Layout)?.Name ?? "", Margin = new Thickness(26, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
            stack.Children.Add(note);
        }
        row.Children.Add(stack);

        title.GotKeyboardFocus += (_, _) => Select(slide);
        if (body != null) body.GotKeyboardFocus += (_, _) => Select(slide);
        title.LostKeyboardFocus += (_, _) => CommitTitle(slide, title);
        if (body != null) body.LostKeyboardFocus += (_, _) => CommitBody(slide, body);
        title.PreviewKeyDown += (_, e) => OnKey(slide, title, body, e, isTitle: true);
        if (body != null) body.PreviewKeyDown += (_, e) => OnKey(slide, title, body, e, isTitle: false);
        _items.Add((slide, title, body));
        return row;
    }

    private static string ToOutline(RichText text) => string.Join("\n", text.Paragraphs.Select(p => new string(' ', p.Level * 2) + p.Text));

    private void Select(Slide slide)
    {
        if (_state.CurrentSlide == slide) return;
        _refreshing = true;
        _state.GoTo(slide);
        _refreshing = false;
    }

    private void CommitTitle(Slide slide, TextBox box)
    {
        var element = slide.FindPlaceholder<TextElement>("title");
        if (element == null || element.Text.Content.PlainText.Replace('\n', ' ') == box.Text) return;
        _refreshing = true;
        var id = element.Id;
        _editor.Session.EditSlide(slide, "Edit title", s =>
        {
            if (s.Find(id) is TextElement t) t.Text.Content = RichText.FromPlain(box.Text.Trim());
        }, "outline-title:" + slide.Id);
        _refreshing = false;
    }

    private void CommitBody(Slide slide, TextBox box)
    {
        var element = slide.FindPlaceholder<TextElement>("body");
        if (element == null || ToOutline(element.Text.Content) == box.Text) return;
        var list = element.Text.Content.Paragraphs.FirstOrDefault()?.List ?? ListKind.Bullet;
        if (list == ListKind.None) list = ListKind.None;
        var rich = new RichText();
        foreach (var raw in box.Text.Replace("\r\n", "\n").Split('\n'))
        {
            var level = 0;
            var line = raw;
            while (line.StartsWith("  ", StringComparison.Ordinal))
            {
                level++;
                line = line.Substring(2);
            }
            rich.Paragraphs.Add(new Paragraph { List = list, Level = Math.Min(level, 4), Runs = { new TextRun { Text = line.TrimEnd() } } });
        }
        _refreshing = true;
        var id = element.Id;
        _editor.Session.EditSlide(slide, "Edit text", s =>
        {
            if (s.Find(id) is TextElement t) t.Text.Content = rich;
        }, "outline-body:" + slide.Id);
        _refreshing = false;
    }

    private void OnKey(Slide slide, TextBox title, TextBox? body, KeyEventArgs e, bool isTitle)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.Enter && ctrl)
        {
            CommitTitle(slide, title);
            if (body != null) CommitBody(slide, body);
            AddAfter(slide);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && isTitle)
        {
            CommitTitle(slide, title);
            if (body != null)
            {
                body.Focus();
                body.CaretIndex = body.Text.Length;
            }
            else AddAfter(slide);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && !isTitle && body != null)
        {
            // Tab indents the current line, Shift+Tab outdents it.
            var start = body.GetCharacterIndexFromLineIndex(body.GetLineIndexFromCharacterIndex(body.CaretIndex));
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (body.Text.Length >= start + 2 && body.Text.Substring(start, 2) == "  ")
                {
                    var caret = body.CaretIndex;
                    body.Text = body.Text.Remove(start, 2);
                    body.CaretIndex = Math.Max(start, caret - 2);
                }
            }
            else
            {
                var caret = body.CaretIndex;
                body.Text = body.Text.Insert(start, "  ");
                body.CaretIndex = caret + 2;
            }
            e.Handled = true;
        }
    }

    private void AddAfter(Slide? slide)
    {
        var deck = _state.Deck;
        var layout = slide == null ? LayoutIds.Title : slide.Layout is LayoutIds.Title or LayoutIds.Section ? LayoutIds.TitleContent : slide.FindPlaceholder<TextElement>("body") != null ? slide.Layout : LayoutIds.TitleContent;
        var created = LayoutEngine.CreateSlide(deck, layout);
        var index = slide == null ? deck.Slides.Count : deck.Slides.IndexOf(slide) + 1;
        _editor.Session.EditStructure("New slide", d => d.Slides.Insert(index, created));
        _state.GoTo(created);
        _items.Clear();
        Refresh();
        var item = _items.FirstOrDefault(i => i.Slide == created);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            item.Title?.Focus();
            item.Title?.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
