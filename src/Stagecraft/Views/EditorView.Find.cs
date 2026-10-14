using System.Windows;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;

namespace Stagecraft.Views;

/// <summary>Find and replace across the presentation (Ctrl+F, Ctrl+H).</summary>
public partial class EditorView
{
    private List<FindMatch> _matches = new();
    private int _matchIndex = -1;
    private bool _findWired;

    private void WireFind()
    {
        if (_findWired) return;
        _findWired = true;
        FindPanel.SearchChanged += () => RunSearch(keepPosition: false);
        FindPanel.Navigate += NavigateMatch;
        FindPanel.ReplaceOne += ReplaceCurrent;
        FindPanel.ReplaceAll += ReplaceEverywhere;
        FindPanel.Closed += CloseFind;
        Session.Changed += _ =>
        {
            if (FindPanel.IsOpen) Dispatcher.BeginInvoke(new Action(() => RunSearch(keepPosition: true)), System.Windows.Threading.DispatcherPriority.Background);
        };
        _state.CurrentSlideChanged += () =>
        {
            if (FindPanel.IsOpen) ShowHighlights();
        };
    }

    /// <summary>Opens the find bar (Ctrl+F) or find and replace (Ctrl+H), starting with the selected text.</summary>
    public void OpenFind(bool replace)
    {
        WireFind();
        string? initial = null;
        if (Canvas.TextEditor is { } rtb && !rtb.Selection.IsEmpty) initial = rtb.Selection.Text.Trim();
        else if (NotesBox.IsKeyboardFocusWithin && NotesBox.SelectionLength > 0) initial = NotesBox.SelectedText.Trim();
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        if (_altViewIndex != 0) SetView(0);
        FindPanel.Open(replace, initial);
    }

    private void CloseFind()
    {
        FindPanel.Close();
        _matches = new List<FindMatch>();
        _matchIndex = -1;
        Canvas.SetFindHighlights(Array.Empty<FindMatch>(), null);
        Canvas.Focus();
    }

    /// <summary>Searches again; the current match stays (or the next one after it) when <paramref name="keepPosition"/>.</summary>
    private void RunSearch(bool keepPosition)
    {
        if (!FindPanel.IsOpen) return;
        var previous = _matchIndex >= 0 && _matchIndex < _matches.Count ? _matches[_matchIndex] : null;
        _matches = FindReplace.FindAll(Session.Deck, FindPanel.Query, FindPanel.Options);
        if (_matches.Count == 0) _matchIndex = -1;
        else if (keepPosition && previous != null) _matchIndex = IndexAtOrAfter(previous);
        else
        {
            // The first match on this slide or after it.
            var current = _state.CurrentIndex;
            _matchIndex = -1;
            for (var i = 0; i < _matches.Count; i++)
            {
                if (Session.Deck.IndexOf(_matches[i].SlideId) >= Math.Max(0, current))
                {
                    _matchIndex = i;
                    break;
                }
            }
            if (_matchIndex < 0) _matchIndex = 0;
            if (!keepPosition) ShowMatch(select: false);
        }
        FindPanel.ShowCount(_matchIndex, _matches.Count);
        ShowHighlights();
    }

    /// <summary>The index of the match at the same place as <paramref name="m"/>, or the next one after it.</summary>
    private int IndexAtOrAfter(FindMatch m)
    {
        var slideIndex = Session.Deck.IndexOf(m.SlideId);
        for (var i = 0; i < _matches.Count; i++)
        {
            var x = _matches[i];
            var xi = Session.Deck.IndexOf(x.SlideId);
            if (xi < slideIndex) continue;
            if (xi > slideIndex) return i;
            if (x == m || (x.ElementId == m.ElementId && x.Area == m.Area && (x.Body, x.Paragraph, x.Start).CompareTo((m.Body, m.Paragraph, m.Start)) >= 0)) return i;
            if (OrderOnSlide(x) > OrderOnSlide(m)) return i;
        }
        return _matches.Count > 0 ? 0 : -1;
    }

    private int OrderOnSlide(FindMatch m)
    {
        var slide = Session.Deck.FindSlide(m.SlideId);
        if (slide == null) return int.MaxValue;
        return m.Area == FindArea.Notes ? int.MaxValue - 1 : slide.Elements.FindIndex(e => e.Id == m.ElementId);
    }

    private void NavigateMatch(int direction)
    {
        if (_matches.Count == 0)
        {
            RunSearch(keepPosition: false);
            if (_matches.Count == 0) return;
        }
        _matchIndex = (_matchIndex + direction + _matches.Count) % _matches.Count;
        FindPanel.ShowCount(_matchIndex, _matches.Count);
        ShowMatch(select: true);
    }

    /// <summary>Goes to the current match: its slide, its element (selected) or the notes.</summary>
    private void ShowMatch(bool select)
    {
        if (_matchIndex < 0 || _matchIndex >= _matches.Count) return;
        var m = _matches[_matchIndex];
        var slide = Session.Deck.FindSlide(m.SlideId);
        if (slide == null) return;
        if (_state.CurrentSlide != slide) _state.GoTo(slide);
        if (m.Area == FindArea.Notes)
        {
            SetNotesVisible(true);
            NotesBox.IsInactiveSelectionHighlightEnabled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (m.Start + m.Length <= NotesBox.Text.Length) NotesBox.Select(m.Start, m.Length);
            }), System.Windows.Threading.DispatcherPriority.Background);
            _state.ClearSelection();
        }
        else if (select && m.ElementId != null && slide.Find(m.ElementId) is { } e)
        {
            _state.Select(new[] { e.Id }, expandGroups: false);
        }
        ShowHighlights();
    }

    private void ShowHighlights()
    {
        var slide = _state.CurrentSlide;
        if (slide == null || !FindPanel.IsOpen)
        {
            Canvas.SetFindHighlights(Array.Empty<FindMatch>(), null);
            return;
        }
        var current = _matchIndex >= 0 && _matchIndex < _matches.Count ? _matches[_matchIndex] : null;
        Canvas.SetFindHighlights(_matches.Where(m => m.SlideId == slide.Id).ToList(), current);
    }

    private void ReplaceCurrent()
    {
        var query = FindPanel.Query;
        if (query.Length == 0) return;
        if (_matches.Count == 0 || _matchIndex < 0)
        {
            RunSearch(keepPosition: false);
            return;
        }
        var m = _matches[_matchIndex];
        var slide = Session.Deck.FindSlide(m.SlideId);
        if (slide == null) return;
        var options = FindPanel.Options;
        if (!FindReplace.StillMatches(slide, m, query, options))
        {
            RunSearch(keepPosition: true);
            return;
        }
        var replacement = FindPanel.Replacement;
        Session.EditSlide(slide, "Replace", s => FindReplace.Replace(s, m, query, replacement, options));
        if (m.Area == FindArea.Notes) SyncNotes();
        // The next match after the replaced text.
        _matches = FindReplace.FindAll(Session.Deck, query, options);
        var after = m with { Start = m.Start + replacement.Length, Length = 0 };
        _matchIndex = _matches.Count == 0 ? -1 : IndexAtOrAfter(after);
        FindPanel.ShowCount(_matchIndex, _matches.Count);
        ShowMatch(select: true);
    }

    private void ReplaceEverywhere()
    {
        var query = FindPanel.Query;
        if (query.Length == 0) return;
        var options = FindPanel.Options;
        var replacement = FindPanel.Replacement;
        var total = FindReplace.FindAll(Session.Deck, query, options).Count;
        if (total == 0)
        {
            _window.ShowMessage("Nothing to replace.", 2);
            return;
        }
        var count = 0;
        Session.EditDeck(total == 1 ? "Replace" : "Replace all", d => count = FindReplace.ReplaceAll(d, query, replacement, options));
        SyncNotes();
        _window.ShowMessage(count == 1 ? "Replaced 1 match." : $"Replaced {count} matches.", 3);
        RunSearch(keepPosition: false);
    }
}
