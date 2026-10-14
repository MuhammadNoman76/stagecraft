using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Editing;

public enum ChangeKind
{
    /// <summary>One slide's content changed.</summary>
    Slide,
    /// <summary>Slides were added, removed, reordered, hidden or grouped into sections.</summary>
    Structure,
    /// <summary>The theme, slide size or something that affects every slide changed.</summary>
    Deck,
}

public sealed class SessionChange
{
    public SessionChange(ChangeKind kind, string? slideId = null)
    {
        Kind = kind;
        SlideId = slideId;
    }

    public ChangeKind Kind { get; }
    public string? SlideId { get; }
}

/// <summary>
/// An open deck: the model, its pictures, the undo history, where it's saved and whether it has unsaved changes.
/// Every change goes through <see cref="EditSlide"/>, <see cref="EditStructure"/> or <see cref="EditDeck"/> so it
/// can be undone.
/// </summary>
public sealed class DeckSession
{
    private bool _forcedDirty;

    public DeckSession(Deck deck, MediaStore media, string? filePath = null)
    {
        Deck = deck;
        Media = media;
        FilePath = filePath;
        History.Changed += () => DirtyChanged?.Invoke();
    }

    public Deck Deck { get; private set; }
    public MediaStore Media { get; }
    public UndoStack History { get; } = new();

    /// <summary>The .stagecraft file, or null for a new (or imported) deck that hasn't been saved yet.</summary>
    public string? FilePath { get; set; }

    /// <summary>A name for title bars: the file name, else the deck title.</summary>
    public string DisplayName => FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : Deck.Title;

    public bool IsDirty => _forcedDirty || History.IsDirty;

    public event Action<SessionChange>? Changed;
    public event Action? DirtyChanged;

    public RenderContext CreateRenderContext(bool editing = false) => new(Deck, Media, editing);

    public void MarkSaved(string path)
    {
        FilePath = path;
        _forcedDirty = false;
        History.MarkSaved();
        DirtyChanged?.Invoke();
    }

    /// <summary>
    /// Marks a snapshot as saved: the deck as it was when <paramref name="savedTop"/> was the newest undo step (changes
    /// made while an automatic save was writing keep the deck unsaved).
    /// </summary>
    public void MarkSaved(string path, IUndoAction? savedTop)
    {
        FilePath = path;
        _forcedDirty = false;
        History.MarkSavedAt(savedTop);
        DirtyChanged?.Invoke();
    }

    /// <summary>Marks the deck as unsaved without an undo step (recovered or imported decks).</summary>
    public void MarkDirty()
    {
        _forcedDirty = true;
        DirtyChanged?.Invoke();
    }

    public void Raise(SessionChange change) => Changed?.Invoke(change);

    // ------------------------------------------------------------------ slide edits

    /// <summary>Changes one slide; the change can be undone. Same <paramref name="mergeKey"/> within two seconds = one step.</summary>
    public void EditSlide(Slide slide, string label, Action<Slide> change, string? mergeKey = null)
    {
        var before = DeckJson.Clone(slide);
        change(slide);
        Commit(slide, before, label, mergeKey);
    }

    /// <summary>Records a change made directly to <paramref name="slide"/> since <paramref name="before"/> was taken (interactive drags).</summary>
    public void Commit(Slide slide, Slide before, string label, string? mergeKey = null)
    {
        slide.Revision++;
        var after = DeckJson.Clone(slide);
        History.Push(new SlideEdit(this, slide.Id, before, after, label, mergeKey), mergeKey);
        Changed?.Invoke(new SessionChange(ChangeKind.Slide, slide.Id));
    }

    /// <summary>A snapshot to pass to <see cref="Commit"/> after changing the slide directly.</summary>
    public Slide Snapshot(Slide slide) => DeckJson.Clone(slide);

    /// <summary>Puts a snapshot back without recording anything (cancelling a drag).</summary>
    public void Revert(Slide slide, Slide snapshot)
    {
        CopyInto(slide, DeckJson.Clone(snapshot));
        slide.Revision++;
        Changed?.Invoke(new SessionChange(ChangeKind.Slide, slide.Id));
    }

    internal static void CopyInto(Slide target, Slide source)
    {
        target.Layout = source.Layout;
        target.Background = source.Background;
        target.ShowDecorations = source.ShowDecorations;
        target.Elements = source.Elements;
        target.Notes = source.Notes;
        target.Hidden = source.Hidden;
        target.Section = source.Section;
        target.Transition = source.Transition;
        target.Animations = source.Animations;
        target.RehearsedSeconds = source.RehearsedSeconds;
    }

    private sealed class SlideEdit : IMergeable
    {
        private readonly DeckSession _session;
        private readonly string _slideId;
        private readonly Slide _before;
        private Slide _after;
        private readonly string? _mergeKey;

        public SlideEdit(DeckSession session, string slideId, Slide before, Slide after, string label, string? mergeKey)
        {
            _session = session;
            _slideId = slideId;
            _before = before;
            _after = after;
            _mergeKey = mergeKey;
            Label = label;
        }

        public string Label { get; }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(Slide state)
        {
            var slide = _session.Deck.FindSlide(_slideId);
            if (slide == null) return;
            CopyInto(slide, DeckJson.Clone(state));
            slide.Revision++;
            _session.Changed?.Invoke(new SessionChange(ChangeKind.Slide, _slideId));
        }

        public bool TryMerge(IUndoAction next)
        {
            if (next is not SlideEdit other || other._slideId != _slideId || other._mergeKey != _mergeKey) return false;
            _after = other._after;
            return true;
        }
    }

    // ------------------------------------------------------------------ structure and deck edits

    /// <summary>
    /// Adds, removes, reorders or changes slide-level flags (hidden, section) without touching slide content.
    /// Slides are kept by reference, so this is cheap even for long decks.
    /// </summary>
    public void EditStructure(string label, Action<Deck> change, bool redrawSlides = true)
    {
        var before = StructureState.Take(Deck);
        change(Deck);
        var after = StructureState.Take(Deck);
        // Guides don't change how slides look: their thumbnails stay.
        if (redrawSlides) foreach (var s in Deck.Slides) s.Revision++;
        History.Push(new StructureEdit(this, before, after, label));
        Changed?.Invoke(new SessionChange(ChangeKind.Structure));
    }

    private sealed class StructureState
    {
        public List<Slide> Slides = new();
        public List<(bool Hidden, string? Section)> Flags = new();
        public string Title = "";
        public HeaderFooter Footer = new();
        public List<GuideLine> Guides = new();

        public static StructureState Take(Deck deck) => new()
        {
            Slides = deck.Slides.ToList(),
            Flags = deck.Slides.Select(s => (s.Hidden, s.Section)).ToList(),
            Title = deck.Title,
            Footer = deck.Footer.Clone(),
            Guides = deck.Guides.Select(g => g.Clone()).ToList(),
        };

        public void Restore(Deck deck)
        {
            deck.Slides = Slides.ToList();
            for (var i = 0; i < Slides.Count; i++)
            {
                Slides[i].Hidden = Flags[i].Hidden;
                Slides[i].Section = Flags[i].Section;
                Slides[i].Revision++;
            }
            deck.Title = Title;
            deck.Footer = Footer.Clone();
            deck.Guides = Guides.Select(g => g.Clone()).ToList();
        }
    }

    private sealed class StructureEdit : IUndoAction
    {
        private readonly DeckSession _session;
        private readonly StructureState _before;
        private readonly StructureState _after;

        public StructureEdit(DeckSession session, StructureState before, StructureState after, string label)
        {
            _session = session;
            _before = before;
            _after = after;
            Label = label;
        }

        public string Label { get; }

        public void Undo()
        {
            _before.Restore(_session.Deck);
            _session.Changed?.Invoke(new SessionChange(ChangeKind.Structure));
        }

        public void Redo()
        {
            _after.Restore(_session.Deck);
            _session.Changed?.Invoke(new SessionChange(ChangeKind.Structure));
        }
    }

    /// <summary>Changes that touch everything (theme, slide size): the whole deck is snapshotted.</summary>
    public void EditDeck(string label, Action<Deck> change)
    {
        var before = DeckJson.Clone(Deck);
        change(Deck);
        var after = DeckJson.Clone(Deck);
        foreach (var s in Deck.Slides) s.Revision++;
        History.Push(new DeckEdit(this, before, after, label));
        Changed?.Invoke(new SessionChange(ChangeKind.Deck));
    }

    private sealed class DeckEdit : IUndoAction
    {
        private readonly DeckSession _session;
        private readonly Deck _before;
        private readonly Deck _after;

        public DeckEdit(DeckSession session, Deck before, Deck after, string label)
        {
            _session = session;
            _before = before;
            _after = after;
            Label = label;
        }

        public string Label { get; }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(Deck state)
        {
            var copy = DeckJson.Clone(state);
            var deck = _session.Deck;
            deck.Title = copy.Title;
            deck.Width = copy.Width;
            deck.Height = copy.Height;
            deck.Theme = copy.Theme;
            deck.Footer = copy.Footer;
            deck.Guides = copy.Guides;
            // Keep slide objects (the UI holds them) and copy content into them; add or drop the rest.
            var result = new List<Slide>();
            foreach (var s in copy.Slides)
            {
                var existing = deck.FindSlide(s.Id);
                if (existing != null)
                {
                    CopyInto(existing, s);
                    existing.Revision++;
                    result.Add(existing);
                }
                else
                {
                    result.Add(s);
                }
            }
            deck.Slides = result;
            _session.Changed?.Invoke(new SessionChange(ChangeKind.Deck));
        }
    }
}
