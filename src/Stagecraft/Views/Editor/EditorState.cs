using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>What the editor's parts share: the session, the current slide and the selection.</summary>
public sealed class EditorState
{
    private readonly HashSet<string> _selection = new();

    public EditorState(DeckSession session)
    {
        Session = session;
        CurrentSlide = session.Deck.Slides.FirstOrDefault();
    }

    public DeckSession Session { get; }
    public Deck Deck => Session.Deck;

    public Slide? CurrentSlide { get; private set; }

    /// <summary>Selected slides in the rail / sorter (always includes the current one).</summary>
    public HashSet<string> SelectedSlides { get; } = new();

    /// <summary>Selected element ids on the current slide.</summary>
    public IReadOnlyCollection<string> Selection => _selection;

    /// <summary>A group the user double-clicked into: its members can be selected one by one.</summary>
    public string? EnteredGroup { get; set; }

    public event Action? SelectionChanged;
    public event Action? CurrentSlideChanged;

    /// <summary>Raised while elements change interactively (drag, resize) so panels can refresh numbers cheaply.</summary>
    public event Action? LiveChange;

    public RenderContext RenderContext => new(Session.Deck, Session.Media, editing: true);

    public SlideStyle? Style => CurrentSlide == null ? null : SlideStyle.For(Deck, CurrentSlide);

    public IReadOnlyList<Element> SelectedElements =>
        CurrentSlide == null ? Array.Empty<Element>() : CurrentSlide.Elements.Where(e => _selection.Contains(e.Id)).ToList();

    public Element? SingleSelection
    {
        get
        {
            var list = SelectedElements;
            return list.Count == 1 ? list[0] : null;
        }
    }

    public int CurrentIndex => CurrentSlide == null ? -1 : Deck.Slides.IndexOf(CurrentSlide);

    public void GoTo(Slide? slide, bool keepSlideSelection = false)
    {
        if (slide != null && !Deck.Slides.Contains(slide)) slide = null;
        slide ??= Deck.Slides.FirstOrDefault();
        var changed = slide != CurrentSlide;
        CurrentSlide = slide;
        if (!keepSlideSelection)
        {
            SelectedSlides.Clear();
            if (slide != null) SelectedSlides.Add(slide.Id);
        }
        if (changed)
        {
            _selection.Clear();
            EnteredGroup = null;
            CurrentSlideChanged?.Invoke();
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Re-reads the current slide after undo / structure changes (it may have been removed).</summary>
    public void Revalidate()
    {
        if (CurrentSlide == null || !Deck.Slides.Contains(CurrentSlide))
        {
            var byId = CurrentSlide != null ? Deck.FindSlide(CurrentSlide.Id) : null;
            CurrentSlide = byId ?? Deck.Slides.FirstOrDefault();
            CurrentSlideChanged?.Invoke();
        }
        SelectedSlides.RemoveWhere(id => Deck.FindSlide(id) == null);
        if (CurrentSlide != null) SelectedSlides.Add(CurrentSlide.Id);
        var before = _selection.Count;
        if (CurrentSlide == null) _selection.Clear();
        else _selection.RemoveWhere(id => CurrentSlide.Find(id) == null);
        if (_selection.Count != before) SelectionChanged?.Invoke();
    }

    /// <summary>Selects elements; elements in a group bring the whole group (unless the group was entered).</summary>
    public void Select(IEnumerable<string> ids, bool expandGroups = true)
    {
        _selection.Clear();
        AddInternal(ids, expandGroups);
        SelectionChanged?.Invoke();
    }

    public void AddToSelection(IEnumerable<string> ids)
    {
        AddInternal(ids, true);
        SelectionChanged?.Invoke();
    }

    public void RemoveFromSelection(IEnumerable<string> ids)
    {
        foreach (var id in ExpandGroups(ids)) _selection.Remove(id);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0 && EnteredGroup == null) return;
        _selection.Clear();
        EnteredGroup = null;
        SelectionChanged?.Invoke();
    }

    public bool IsSelected(string id) => _selection.Contains(id);

    private void AddInternal(IEnumerable<string> ids, bool expandGroups)
    {
        foreach (var id in expandGroups ? ExpandGroups(ids) : ids) _selection.Add(id);
    }

    private IEnumerable<string> ExpandGroups(IEnumerable<string> ids)
    {
        if (CurrentSlide == null) yield break;
        foreach (var id in ids)
        {
            var e = CurrentSlide.Find(id);
            if (e == null) continue;
            if (e.GroupId != null && e.GroupId != EnteredGroup)
            {
                foreach (var member in CurrentSlide.Elements.Where(x => x.GroupId == e.GroupId)) yield return member.Id;
            }
            else yield return id;
        }
    }

    public void RaiseSelectionChanged() => SelectionChanged?.Invoke();

    public void RaiseLiveChange() => LiveChange?.Invoke();
}
