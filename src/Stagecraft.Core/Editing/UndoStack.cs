namespace Stagecraft.Core.Editing;

/// <summary>One undoable change.</summary>
public interface IUndoAction
{
    string Label { get; }
    void Undo();
    void Redo();
}

/// <summary>
/// Undo / redo history. Consecutive small changes with the same merge key (typing, nudging, dragging a slider)
/// become one step. Knows whether the document differs from the last saved state.
/// </summary>
public sealed class UndoStack
{
    private readonly List<IUndoAction> _undo = new();
    private readonly List<IUndoAction> _redo = new();
    private IUndoAction? _saved;
    private bool _savedAtStart = true;
    private string? _lastMergeKey;
    private DateTime _lastPush;

    public int Limit { get; set; } = 300;

    /// <summary>How long consecutive changes with the same merge key keep merging.</summary>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromSeconds(2);

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[_redo.Count - 1].Label : null;
    public int Count => _undo.Count;

    public IUndoAction? Top => _undo.Count > 0 ? _undo[_undo.Count - 1] : null;

    /// <summary>True when the document has changed since <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => _savedAtStart ? _undo.Count > 0 : Top != _saved;

    /// <summary>Adds a change that was already applied.</summary>
    public void Push(IUndoAction action, string? mergeKey = null)
    {
        var now = DateTime.UtcNow;
        if (mergeKey != null && mergeKey == _lastMergeKey && now - _lastPush < MergeWindow && Top is IMergeable top && top != _saved && top.TryMerge(action))
        {
            _lastPush = now;
            _redo.Clear();
            Changed?.Invoke();
            return;
        }
        _undo.Add(action);
        if (_undo.Count > Limit)
        {
            if (_undo[0] == _saved) _saved = null; // the saved state can't be reached any more: stays dirty
            _undo.RemoveAt(0);
        }
        _redo.Clear();
        _lastMergeKey = mergeKey;
        _lastPush = now;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var action = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        action.Undo();
        _redo.Add(action);
        _lastMergeKey = null;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var action = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        action.Redo();
        _undo.Add(action);
        _lastMergeKey = null;
        Changed?.Invoke();
    }

    /// <summary>Ends merging, so the next change starts a new step.</summary>
    public void Seal() => _lastMergeKey = null;

    public void MarkSaved()
    {
        _saved = Top;
        _savedAtStart = _saved == null;
        _lastMergeKey = null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _saved = null;
        _savedAtStart = true;
        _lastMergeKey = null;
        Changed?.Invoke();
    }
}

/// <summary>An action that can absorb the next one (both must describe the same thing).</summary>
public interface IMergeable : IUndoAction
{
    bool TryMerge(IUndoAction next);
}
