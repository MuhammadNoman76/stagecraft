using System.Windows;
using System.Windows.Documents;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;
using Stagecraft.Views.Editor;
using Hyperlink = Stagecraft.Core.Model.Hyperlink;
using WpfHyperlink = System.Windows.Documents.Hyperlink;

namespace Stagecraft.Views;

/// <summary>Links: on selected text, on whole elements and action buttons; following them with Ctrl+click.</summary>
public partial class EditorView
{
    private string? DeckFolder => Session.FilePath != null ? Path.GetDirectoryName(Session.FilePath) : null;

    private void OnInsertLink(object sender, RoutedEventArgs e) => InsertLink();

    /// <summary>
    /// Ctrl+Shift+K: a link on the selected text (while editing text), on the selected elements, or new linked text.
    /// </summary>
    public void InsertLink()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var rtb = Canvas.TextEditor;
        if (rtb != null)
        {
            LinkTextSelection(rtb);
            return;
        }
        var selected = _state.SelectedElements;
        if (selected.Count > 0)
        {
            var existing = selected.Select(x => x.Link).FirstOrDefault(l => l != null);
            var choice = LinkDialog.Ask(_window, Session.Deck, DeckFolder, existing, null, askDisplay: false);
            if (choice == null) return;
            EditSelected(choice.Removed ? "Remove link" : existing != null ? "Edit link" : "Link", list =>
            {
                foreach (var x in list) x.Link = choice.Removed ? null : choice.Link?.Clone();
            });
            return;
        }
        // Nothing selected: new text with the link on it.
        var created = LinkDialog.Ask(_window, Session.Deck, DeckFolder, null, "", askDisplay: true);
        if (created?.Link == null) return;
        var label = string.IsNullOrWhiteSpace(created.DisplayText) ? created.Link.Summary : created.DisplayText!.Trim();
        var body = new TextBody { Role = TextRole.Body, AutoFit = false, Content = new RichText() };
        var p = new Core.Model.Paragraph();
        p.Runs.Add(new TextRun { Text = label, Link = created.Link });
        body.Content.Paragraphs.Add(p);
        var style = SlideStyle.For(Session.Deck, slide);
        var height = Math.Ceiling(Core.Rendering.TextLayout.MeasureHeight(body, 900, style));
        var text = new Core.Model.TextElement { Text = body };
        text.Bounds = PlaceAtCenter(900, height);
        AddElements("Add link", text);
    }

    /// <summary>Puts a link on the selected words of the text being edited (or inserts linked text at the caret).</summary>
    private void LinkTextSelection(System.Windows.Controls.RichTextBox rtb)
    {
        var selection = rtb.Selection;
        var current = HyperlinksIn(selection).FirstOrDefault();
        Hyperlink? existing = current != null ? RichTextConverter.LinkOf(current) : null;
        var askDisplay = selection.IsEmpty || (current != null && new TextRange(current.ContentStart, current.ContentEnd).Text == selection.Text);
        var display = current != null ? new TextRange(current.ContentStart, current.ContentEnd).Text : selection.Text;
        var choice = LinkDialog.Ask(_window, Session.Deck, DeckFolder, existing, display, askDisplay);
        rtb.Focus();
        if (choice == null) return;
        var style = _state.Style;
        if (style == null) return;
        if (choice.Removed)
        {
            foreach (var link in HyperlinksIn(selection).ToList()) Unwrap(link);
            if (current != null) Unwrap(current);
            return;
        }
        var linkModel = choice.Link!;
        if (current != null)
        {
            // Editing the link the caret is in.
            RichTextConverter.Restyle(current, linkModel, style);
            if (askDisplay && choice.DisplayText is { Length: > 0 } t && t != display)
            {
                current.Inlines.Clear();
                current.Inlines.Add(new Run(t));
            }
            Panel.Rebuild();
            return;
        }
        if (selection.IsEmpty)
        {
            var label = string.IsNullOrWhiteSpace(choice.DisplayText) ? linkModel.Summary : choice.DisplayText!;
            var run = new Run(label, selection.Start.GetInsertionPosition(LogicalDirection.Forward));
            var hl = new WpfHyperlink(run.ContentStart, run.ContentEnd);
            RichTextConverter.Restyle(hl, linkModel, style);
            rtb.CaretPosition = hl.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward) ?? hl.ElementEnd;
            return;
        }
        // Wrap the selection, paragraph by paragraph (a link can't cross paragraphs).
        var start = selection.Start;
        var end = selection.End;
        var block = start.Paragraph;
        while (block != null)
        {
            var from = start.CompareTo(block.ContentStart) > 0 ? start : block.ContentStart;
            var to = end.CompareTo(block.ContentEnd) < 0 ? end : block.ContentEnd;
            if (from.CompareTo(to) < 0 && new TextRange(from, to).Text.Trim().Length > 0)
            {
                foreach (var old in HyperlinksIn(new TextRange(from, to)).ToList()) Unwrap(old);
                try
                {
                    var hl = new WpfHyperlink(from.GetInsertionPosition(LogicalDirection.Forward), to.GetInsertionPosition(LogicalDirection.Backward));
                    RichTextConverter.Restyle(hl, linkModel, style);
                }
                catch (InvalidOperationException ex)
                {
                    Log.Error(ex, "Link text");
                }
            }
            if (block.ContentEnd.CompareTo(end) >= 0) break;
            block = NextParagraph(block);
        }
        Panel.Rebuild();
    }

    private static System.Windows.Documents.Paragraph? NextParagraph(System.Windows.Documents.Paragraph p)
    {
        var next = p.ContentEnd.GetNextContextPosition(LogicalDirection.Forward);
        while (next != null)
        {
            if (next.Paragraph != null && next.Paragraph != p) return next.Paragraph;
            next = next.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    /// <summary>Links that touch a range of the document (or contain the caret).</summary>
    private static IEnumerable<WpfHyperlink> HyperlinksIn(TextRange range)
    {
        var seen = new HashSet<WpfHyperlink>();
        WpfHyperlink? At(TextPointer p)
        {
            DependencyObject? o = p.Parent;
            while (o != null && o is not WpfHyperlink && o is not FlowDocument) o = o is System.Windows.Documents.TextElement te ? te.Parent : null;
            return o as WpfHyperlink;
        }
        if (At(range.Start) is { } a && seen.Add(a)) yield return a;
        var pos = range.Start;
        while (pos != null && pos.CompareTo(range.End) < 0)
        {
            if (At(pos) is { } h && seen.Add(h)) yield return h;
            pos = pos.GetNextContextPosition(LogicalDirection.Forward);
        }
    }

    /// <summary>Removes a link but keeps its text.</summary>
    private static void Unwrap(WpfHyperlink link)
    {
        if (link.Parent is not System.Windows.Documents.Paragraph and not Span) return;
        var inlines = link.SiblingInlines;
        var children = link.Inlines.ToList();
        foreach (var child in children)
        {
            link.Inlines.Remove(child);
            if (child is Run r)
            {
                r.ClearValue(System.Windows.Documents.TextElement.ForegroundProperty);
                r.ClearValue(Inline.TextDecorationsProperty);
            }
            inlines.InsertBefore(link, child);
        }
        inlines.Remove(link);
    }


    /// <summary>Link items for the right-click menu of slide text being edited.</summary>
    private void AddTextLinkItems(System.Windows.Controls.ContextMenu menu)
    {
        var rtb = Canvas.TextEditor;
        if (rtb == null) return;
        menu.Items.Add(new System.Windows.Controls.Separator());
        var links = HyperlinksIn(rtb.Selection).ToList();
        var current = links.Select(RichTextConverter.LinkOf).FirstOrDefault(l => l != null);
        void Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new System.Windows.Controls.MenuItem { Header = header, InputGestureText = gesture };
            Controls.Ui.SetIcon(mi, (System.Windows.Media.Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item(current != null ? "Edit link…" : "Link…", "Icon.Link", InsertLink, "Ctrl+Shift+K");
        if (current != null)
        {
            Item("Open link", "Icon.Open", () => FollowLink(current));
            Item("Remove link", "Icon.LinkDismiss", RemoveLinks);
        }
    }
    /// <summary>Removes the links of the selection (elements and their text).</summary>
    public void RemoveLinks()
    {
        var rtb = Canvas.TextEditor;
        if (rtb != null)
        {
            foreach (var link in HyperlinksIn(rtb.Selection).ToList()) Unwrap(link);
            return;
        }
        EditSelected("Remove link", list =>
        {
            foreach (var e in list)
            {
                e.Link = null;
                foreach (var body in DeckFile.TextBodies(e))
                foreach (var p in body.Content.Paragraphs)
                foreach (var r in p.Runs)
                    r.Link = null;
            }
        });
    }

    private bool SelectionHasLink =>
        _state.SelectedElements.Any(e => e.Link != null || DeckFile.TextBodies(e).Any(b => b.Content.Paragraphs.Any(p => p.Runs.Any(r => r.Link != null))));

    /// <summary>The first link of the selection (an element's own, else the first linked text).</summary>
    private Hyperlink? FirstLink() =>
        _state.SelectedElements.Select(e => e.Link ?? DeckFile.TextBodies(e).SelectMany(b => b.Content.Paragraphs).SelectMany(p => p.Runs).Select(r => r.Link).FirstOrDefault(l => l != null))
            .FirstOrDefault(l => l != null);

    /// <summary>Opens a link from the editor: slide links go to the slide, others open after the usual checks.</summary>
    public void FollowLink(Hyperlink link)
    {
        if (link.Kind == LinkKind.Slide)
        {
            var slides = Session.Deck.Slides;
            var target = LinkSafety.JumpTarget(link, slides, _state.CurrentIndex, _lastViewedInEditor);
            if (target < 0)
            {
                _window.ShowMessage(link.Jump == SlideJump.LastViewed ? "There's no slide viewed before this one yet." : "That slide isn't in the presentation.", 3);
                return;
            }
            _lastViewedInEditor = _state.CurrentIndex;
            _state.GoTo(slides[target]);
            return;
        }
        LinkLauncher.Open(link, _window, DeckFolder);
    }

    private int _lastViewedInEditor = -1;

    // ------------------------------------------------------------------ action buttons

    /// <summary>A button that goes to another slide in the show (back, next, first, last, return) or follows a link.</summary>
    public void InsertActionButton(ActionButtonKind kind)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        Hyperlink? link = kind switch
        {
            ActionButtonKind.Previous => Hyperlink.ToSlide(SlideJump.Previous),
            ActionButtonKind.Next => Hyperlink.ToSlide(SlideJump.Next),
            ActionButtonKind.First => Hyperlink.ToSlide(SlideJump.First),
            ActionButtonKind.Last => Hyperlink.ToSlide(SlideJump.Last),
            ActionButtonKind.Return => Hyperlink.ToSlide(SlideJump.LastViewed),
            _ => null,
        };
        if (kind == ActionButtonKind.Custom)
        {
            var choice = LinkDialog.Ask(_window, Session.Deck, DeckFolder, null, null, askDisplay: false);
            if (choice?.Link == null) return;
            link = choice.Link;
        }
        var deck = Session.Deck;
        var size = Math.Round(deck.Height * 0.1);
        var m = SlideStyle.For(deck, slide).Margin;
        var button = new ShapeElement
        {
            Shape = ShapeKind.ActionButton,
            Action = kind,
            Fill = Fill.Solid("accent1"),
            Link = link,
            Name = kind switch
            {
                ActionButtonKind.Previous => "Back button",
                ActionButtonKind.Next => "Next button",
                ActionButtonKind.First => "First slide button",
                ActionButtonKind.Last => "Last slide button",
                ActionButtonKind.Return => "Return button",
                _ => "Link button",
            },
        };
        // Bottom right, side by side with buttons already there.
        var existing = slide.Elements.OfType<ShapeElement>().Count(s => s.Shape == ShapeKind.ActionButton);
        button.Bounds = new Rect(deck.Width - m - size - existing * (size + 16), deck.Height - m * 0.5 - size, size, size);
        AddElements("Add action button", button);
    }
}
