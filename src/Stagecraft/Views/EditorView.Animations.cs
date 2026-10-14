using System.Windows;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

/// <summary>Animations: adding and changing effects, their order, the animation painter and motion paths.</summary>
public partial class EditorView
{
    private List<AnimationEffect>? _copiedAnimations;
    private bool _animationPainterArmed;

    /// <summary>Shows or hides the animation pane (the canvas shows click badges and motion paths while it's open).</summary>
    public void ShowAnimations(bool on)
    {
        AnimationsToggle.IsChecked = on;
        if (on) DesignToggle.IsChecked = false;
        Panel.Mode = on ? PanelMode.Animations : DesignToggle.IsChecked == true ? PanelMode.Design : PanelMode.Properties;
        _state.SetAnimationMode(on);
        PanelColumn.Width = new GridLength(on ? 372 : 312);
        if (on && _state.SelectedEffects.Count == 0 && _state.CurrentSlide is { } slide)
            _state.SelectEffects(AnimationRules.EffectsOf(slide, _state.SelectedElements).Select(a => a.Id));
        Panel.RebuildNow();
        Canvas.InvalidateOverlay();
    }

    private void OnAnimationsToggle(object sender, RoutedEventArgs e) => ShowAnimations(AnimationsToggle.IsChecked == true);

    private bool _effectSelectionFromPane;

    /// <summary>Rows picked in the animation pane: their elements are selected on the slide too.</summary>
    public void SelectEffectsFromPane(IEnumerable<string> ids)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var list = ids.ToList();
        _effectSelectionFromPane = true;
        try
        {
            var targets = slide.Animations.Where(a => list.Contains(a.Id)).SelectMany(a => AnimationRules.Resolve(slide, a.Target)).Select(e => e.Id).Distinct().ToList();
            _state.Select(targets, expandGroups: false);
        }
        finally
        {
            _effectSelectionFromPane = false;
        }
        _state.SelectEffects(list);
        Panel.Rebuild();
    }

    /// <summary>Elements picked on the slide while the pane is open: their effects are selected in the pane.</summary>
    private void SyncEffectSelection()
    {
        if (!_state.AnimationMode || _effectSelectionFromPane || _state.CurrentSlide is not { } slide) return;
        _state.SelectEffects(AnimationRules.EffectsOf(slide, _state.SelectedElements).Select(a => a.Id));
    }

    /// <summary>Changes the current slide's animation list as one undoable step.</summary>
    public void EditAnimations(string label, Action<Slide> change, string? mergeKey = null)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        Session.EditSlide(slide, label, change, mergeKey);
    }

    /// <summary>Adds an effect to every selected element (the first starts on a click, the others with it).</summary>
    public void AddAnimation(EffectClass cls, EffectKind kind)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        StopEffectPreview();
        if (_state.Selection.Count == 0)
        {
            _window.ShowMessage("Select something on the slide first, then choose an effect for it.", 4);
            return;
        }
        var targets = AnimationOps.TargetsFor(_state.SelectedElements, _state.EnteredGroup);
        List<AnimationEffect> added = new();
        EditAnimations("Add animation", s => added = AnimationOps.Add(s, cls, kind, targets));
        if (added.Count == 0) return;
        if (!_state.AnimationMode) ShowAnimations(true);
        _state.SelectEffects(added.Select(a => a.Id));
        Panel.Rebuild();
        if (cls == EffectClass.Motion && kind == EffectKind.Custom) Canvas.BeginDrawPath(added[0].Id);
    }

    /// <summary>Changes the selected effects to another effect (timing kept).</summary>
    public void ChangeEffect(EffectClass cls, EffectKind kind)
    {
        var ids = _state.SelectedEffects.ToList();
        if (ids.Count == 0) return;
        EditAnimations("Change effect", s => AnimationOps.ChangeEffect(s, ids, cls, kind));
        if (cls == EffectClass.Motion && kind == EffectKind.Custom && ids.Count == 1) Canvas.BeginDrawPath(ids[0]);
    }

    /// <summary>Changes a setting of the selected effects (merging quick successive changes into one undo step).</summary>
    public void EditEffects(string label, Action<AnimationEffect> change, string? mergeKey = null)
    {
        var ids = _state.SelectedEffects.ToList();
        if (ids.Count == 0) return;
        EditAnimations(label, s =>
        {
            foreach (var a in s.Animations.Where(x => ids.Contains(x.Id))) change(a);
        }, mergeKey);
    }

    public void RemoveSelectedEffects()
    {
        var ids = _state.SelectedEffects.ToList();
        if (ids.Count == 0) return;
        EditAnimations(ids.Count == 1 ? "Remove animation" : "Remove animations", s => AnimationOps.Remove(s, ids));
        _state.SelectEffects(Array.Empty<string>());
    }

    /// <summary>Removes every effect of the selected elements.</summary>
    public void RemoveAnimationsOfSelection()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var ids = AnimationRules.EffectsOf(slide, _state.SelectedElements).Select(a => a.Id).ToList();
        if (ids.Count == 0) return;
        EditAnimations("Remove animations", s => AnimationOps.Remove(s, ids));
    }

    public void MoveEffects(IReadOnlyCollection<string> ids, int insertIndex)
    {
        if (ids.Count == 0) return;
        EditAnimations("Reorder animations", s => AnimationOps.Move(s, ids, insertIndex));
    }

    public void NudgeEffects(int direction)
    {
        var ids = _state.SelectedEffects.ToList();
        if (ids.Count == 0) return;
        EditAnimations(direction < 0 ? "Move animation earlier" : "Move animation later", s => AnimationOps.Nudge(s, ids, direction), "nudge-animation");
    }

    /// <summary>Plays the slide's animations from the first selected effect's click.</summary>
    public void PlayFromSelected()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var first = _state.SelectedEffectList.FirstOrDefault();
        var click = 0;
        if (first != null)
        {
            var group = SlideTimeline.Build(slide).Groups.FirstOrDefault(g => g.Items.Any(i => i.Effect.Id == first.Id));
            click = group?.Click ?? 0;
        }
        PreviewAnimations(click);
    }

    /// <summary>Saves a new route for a motion path (dragged nodes or a drawn path).</summary>
    public void SetMotionPath(string effectId, MotionPath path, EffectKind? kind = null)
    {
        EditAnimations("Motion path", s =>
        {
            var a = s.Animations.FirstOrDefault(x => x.Id == effectId);
            if (a == null) return;
            a.Path = path;
            if (kind != null) a.Effect = kind.Value;
        });
    }

    /// <summary>Turns a motion path around (it ends where it started).</summary>
    public void ReversePath()
    {
        EditEffects("Reverse path", a =>
        {
            if (a.Path == null || a.Path.Points.Count < 2) return;
            var pts = a.Path.Points.Select(p => p.Clone()).ToList();
            pts.Reverse();
            var first = pts[0];
            a.Path.Points = pts.Select(p => new PathPoint(Math.Round(p.X - first.X, 2), Math.Round(p.Y - first.Y, 2))).ToList();
        });
    }

    // ------------------------------------------------------------------ animation painter

    /// <summary>Copies the selected element's animations; the next element clicked gets them.</summary>
    public void StartAnimationPainter()
    {
        var slide = _state.CurrentSlide;
        var source = _state.SingleSelection ?? _state.SelectedElements.FirstOrDefault();
        if (slide == null || source == null) return;
        var copied = AnimationOps.CopyOf(slide, source);
        if (copied.Count == 0)
        {
            _window.ShowMessage("This element has no animations to copy.", 3);
            return;
        }
        _copiedAnimations = copied;
        _animationPainterArmed = true;
        Canvas.Cursor = System.Windows.Input.Cursors.Pen;
        _window.ShowMessage("Animation copied. Click an element to give it the same animation (Esc cancels).", 5);
        _state.SelectionChanged -= ApplyAnimationPainterOnce;
        _state.SelectionChanged += ApplyAnimationPainterOnce;
    }

    private void ApplyAnimationPainterOnce()
    {
        if (!_animationPainterArmed || _copiedAnimations == null) return;
        var targets = _state.SelectedElements;
        if (targets.Count == 0) return;
        if (targets.Any(t => _copiedAnimations.Any(a => a.Target == t.Id || a.Target == t.GroupId))) return;
        _animationPainterArmed = false;
        _state.SelectionChanged -= ApplyAnimationPainterOnce;
        Canvas.Cursor = null;
        Dispatcher.BeginInvoke(new Action(PasteAnimation), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Gives the selected elements the copied animation.</summary>
    public void PasteAnimation()
    {
        var copied = _copiedAnimations;
        if (copied == null || _state.Selection.Count == 0) return;
        var targets = AnimationOps.TargetsFor(_state.SelectedElements, _state.EnteredGroup);
        List<AnimationEffect> added = new();
        EditAnimations("Paste animation", s => added = AnimationOps.Paint(s, copied, targets));
        _state.SelectEffects(added.Select(a => a.Id));
    }

    public void CancelPainters()
    {
        if (!_animationPainterArmed && !_painterArmed) return;
        _animationPainterArmed = false;
        _painterArmed = false;
        _state.SelectionChanged -= ApplyAnimationPainterOnce;
        _state.SelectionChanged -= ApplyPainterOnce;
        Canvas.Cursor = null;
    }

    // ------------------------------------------------------------------ video and sound start

    public void SetMediaStart(MediaClipElement clip, MediaStart start)
    {
        var id = clip.Id;
        EditAnimations("Start", s =>
        {
            if (s.Find(id) is MediaClipElement c) AnimationOps.SetStart(s, c, start);
        });
    }
}
