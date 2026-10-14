using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Services;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

/// <summary>
/// The deck editor: command bar, slide rail, canvas, notes and the contextual properties panel (plus the slide
/// sorter, the outline and the command palette). The work is split over the EditorView.*.cs files.
/// </summary>
public partial class EditorView
{
    private readonly MainWindow _window;
    private readonly EditorState _state;
    private readonly ThumbnailCache _thumbs;
    private readonly DispatcherTimer _autosave;
    private readonly DispatcherTimer _autoSaveFile;
    private readonly DispatcherTimer _notesTimer;
    private string _recoveryKey = Ids.New();
    private bool _closeConfirmed;
    private bool _syncingNotes;
    private Slide? _notesSlide;
    private bool _detached;
    private DateTime _lastRecoverySave = DateTime.MinValue;
    private long _recoveryVersion = -1;
    private long _changeCounter;

    public EditorView(MainWindow window, DeckSession session)
    {
        _window = window;
        Session = session;
        InitializeComponent();
        _state = new EditorState(session);
        _thumbs = new ThumbnailCache(session, Dispatcher);

        Canvas.Attach(_state);
        Rail.Attach(_state, _thumbs);
        Panel.Attach(this, _state);

        session.Changed += OnSessionChanged;
        session.DirtyChanged += OnDirtyChanged;
        _state.CurrentSlideChanged += OnCurrentSlideChanged;
        _state.SelectionChanged += OnSelectionChanged;

        Rail.AddRequested += () => NewSlide(null);
        Rail.ContextRequested += ShowRailMenu;
        Rail.DeleteRequested += DeleteSlides;
        Rail.DuplicateRequested += DuplicateSlides;
        Rail.MoveRequested += MoveSlides;

        Canvas.ViewChanged += () => ZoomText.Content = Canvas.ZoomText;
        Canvas.SlideWheel += dir => GoRelative(dir);
        Canvas.ElementActivated += OnElementActivated;
        Canvas.ImagesDropped += OnImagesDropped;
        Canvas.ContextRequested += ShowCanvasMenu;
        Canvas.TextEditingChanged += () => Panel.Rebuild();
        Canvas.TextSelectionChanged += () => Panel.Rebuild();
        Canvas.PathDrawn += (id, path) => SetMotionPath(id, path, Core.Model.EffectKind.Custom);
        Canvas.LinkFollowed += FollowLink;
        Canvas.MediaDropped += (paths, at) => InsertDroppedMedia(paths, at);
        Canvas.CropEnded += () => Panel.Rebuild();
        Canvas.TextMenuOpening += AddTextLinkItems;
        TextContextMenu.ApplySpelling(NotesBox);
        TextContextMenu.Attach(NotesBox);
        AppSettings.Changed += OnSettingsChanged;

        TitleBox.Text = session.Deck.Title;
        TitleBox.LostKeyboardFocus += (_, _) => CommitTitle();
        TitleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitTitle();
                Canvas.Focus();
                e.Handled = true;
            }
        };

        _notesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _notesTimer.Tick += (_, _) =>
        {
            _notesTimer.Stop();
            CommitNotes();
        };
        NotesBox.TextChanged += (_, _) =>
        {
            if (_syncingNotes) return;
            _notesTimer.Stop();
            _notesTimer.Start();
        };
        NotesBox.LostKeyboardFocus += (_, _) => CommitNotes();

        var settings = AppSettings.Current;
        SetNotesVisible(settings.ShowNotes, save: false);
        NotesRow.Height = new GridLength(settings.NotesHeight);

        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autosave.Tick += (_, _) => AutosaveTick();
        _autosave.Start();
        _autoSaveFile = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveFile.Tick += (_, _) => AutoSaveFileTick();

        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) => UpdateCommandBarLayout();
        Loaded += (_, _) =>
        {
            Canvas.Focus();
            UpdateUndoButtons();
            UpdateSaveState();
        };
        AllowDrop = true;

        OnCurrentSlideChanged();
        UpdateUndoButtons();
        UpdateSaveState();
    }

    public DeckSession Session { get; }
    public EditorState State => _state;
    public SlideCanvas SlideCanvas => Canvas;
    public string RecoveryKey => _recoveryKey;

    /// <summary>A recovered deck keeps writing to (and finally deleting) its recovery copy.</summary>
    public void AdoptRecoveryKey(string key) => _recoveryKey = key;

    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _autosave.Stop();
        _autoSaveFile.Stop();
        _notesTimer.Stop();
        Session.Changed -= OnSessionChanged;
        AppSettings.Changed -= OnSettingsChanged;
        Session.DirtyChanged -= OnDirtyChanged;
    }

    // ------------------------------------------------------------------ session changes

    private void OnSessionChanged(SessionChange change)
    {
        _changeCounter++;
        if (Session.FilePath != null && AppSettings.Current.AutoSaveFile)
        {
            _autoSaveFile.Stop();
            _autoSaveFile.Start();
        }
        switch (change.Kind)
        {
            case ChangeKind.Slide:
                if (change.SlideId != null) Rail.RefreshSlide(change.SlideId);
                if (_state.CurrentSlide?.Id == change.SlideId)
                {
                    _state.Revalidate();
                    Canvas.Refresh();
                    SyncNotes();
                }
                break;
            default:
                _state.Revalidate();
                Rail.Rebuild();
                Canvas.Refresh();
                SyncNotes();
                TitleBox.Text = Session.Deck.Title;
                break;
        }
        _alt?.Refresh();
        Panel.Rebuild();
        UpdateUndoButtons();
        UpdatePosition();
    }

    private void OnDirtyChanged()
    {
        UpdateSaveState();
        UpdateUndoButtons();
        _window.UpdateTitle();
    }

    private void OnCurrentSlideChanged()
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        Canvas.Refresh();
        Rail.SyncSelection();
        SyncNotes();
        UpdatePosition();
        Panel.Rebuild();
    }

    private void OnSelectionChanged()
    {
        SyncEffectSelection();
        Canvas.InvalidateOverlay();
    }

    private void UpdatePosition()
    {
        var count = Session.Deck.Slides.Count;
        var index = _state.CurrentIndex + 1;
        SlidePosition.Text = count == 0 ? "" : $"Slide {index} of {count}";
    }

    public void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = Session.History.CanUndo;
        RedoButton.IsEnabled = Session.History.CanRedo;
        UndoButton.ToolTip = Session.History.CanUndo ? $"Undo {Session.History.UndoLabel} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = Session.History.CanRedo ? $"Redo {Session.History.RedoLabel} (Ctrl+Y)" : "Redo (Ctrl+Y)";
    }

    /// <summary>Settings changed (here or in the Settings window): spelling, rulers and grid follow.</summary>
    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnSettingsChanged));
            return;
        }
        TextContextMenu.ApplySpelling(NotesBox);
        if (Canvas.TextEditor is { } rtb) TextContextMenu.ApplySpelling(rtb);
        Canvas.ApplyViewAids();
    }

    private void UpdateSaveState()
    {
        if (Session.FilePath == null) SaveState.Text = Session.IsDirty ? "Not saved yet" : "New presentation";
        else SaveState.Text = Session.IsDirty ? "Unsaved changes" : "Saved";
    }

    private void CommitTitle()
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            TitleBox.Text = Session.Deck.Title;
            return;
        }
        if (title == Session.Deck.Title) return;
        Session.EditStructure("Rename presentation", d => d.Title = title);
        _window.UpdateTitle();
    }

    // ------------------------------------------------------------------ command bar

    private int _barLevel = -1;

    /// <summary>Narrow windows: the insert buttons lose their labels, then the palette pill shrinks.</summary>
    private void UpdateCommandBarLayout()
    {
        // The width the bar can have (not its own: with every label it can be wider than the window).
        var w = ActualWidth - CommandBar.Margin.Left - CommandBar.Margin.Right;
        var level = w >= 1980 ? 0 : w >= 1640 ? 1 : 2;
        if (level == _barLevel) return;
        _barLevel = level;
        foreach (var b in InsertBar.Children.OfType<Button>())
        {
            if (b == NewSlideButton || b == LayoutDrop) continue;
            b.Tag ??= b.Content;
            if (level >= 1)
            {
                b.Content = null;
                b.SetResourceReference(StyleProperty, "IconButton");
                b.Width = 36;
                b.Height = 36;
            }
            else
            {
                b.Content = b.Tag;
                b.SetResourceReference(StyleProperty, "CommandButton");
                b.Width = double.NaN;
            }
        }
        PaletteButton.Width = 220;
        DesignToggle.Content = level >= 2 ? null : "Design";
        AnimationsToggle.Content = level >= 2 ? null : "Animations";
        NewSlideButton.Content = level >= 2 ? null : "New slide";
    }

    // ------------------------------------------------------------------ notes

    private void SyncNotes()
    {
        _syncingNotes = true;
        _notesSlide = _state.CurrentSlide;
        NotesBox.Text = _state.CurrentSlide?.Notes ?? "";
        NotesBox.IsEnabled = _state.CurrentSlide != null;
        _syncingNotes = false;
    }

    private void CommitNotes()
    {
        _notesTimer.Stop();
        // The box belongs to the slide it was filled from (the current slide may already have changed).
        var slide = _notesSlide;
        if (slide == null || _syncingNotes || !Session.Deck.Slides.Contains(slide)) return;
        var text = NotesBox.Text;
        if (text == slide.Notes) return;
        Session.EditSlide(slide, "Edit notes", s => s.Notes = text, "notes:" + slide.Id);
    }

    private void SetNotesVisible(bool visible, bool save = true)
    {
        NotesPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NotesSplitter.Visibility = NotesPane.Visibility;
        ShowNotesButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        NotesRow.Height = visible ? new GridLength(Math.Max(80, AppSettings.Current.NotesHeight)) : GridLength.Auto;
        if (save) AppSettings.Update(s => s.ShowNotes = visible);
    }

    private void OnToggleNotes(object sender, RoutedEventArgs e) => SetNotesVisible(NotesPane.Visibility != Visibility.Visible);

    private void OnNotesResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var h = NotesRow.ActualHeight;
        if (h > 40) AppSettings.Update(s => s.NotesHeight = h);
    }

    // ------------------------------------------------------------------ navigation

    public void GoTo(Slide? slide)
    {
        if (slide == null) return;
        _state.GoTo(slide);
    }

    public void GoRelative(int direction)
    {
        var slides = Session.Deck.Slides;
        if (slides.Count == 0) return;
        var index = MathEx.Clamp(_state.CurrentIndex + direction, 0, slides.Count - 1);
        _state.GoTo(slides[index]);
    }

    // ------------------------------------------------------------------ zoom

    private void OnZoomIn(object sender, RoutedEventArgs e) => Canvas.ZoomStep(1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Canvas.ZoomStep(-1);
    private void OnZoomFit(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    // ------------------------------------------------------------------ design / views

    private void OnDesign(object sender, RoutedEventArgs e) => ShowDesign(DesignToggle.IsChecked == true);

    public void ShowDesign(bool on)
    {
        DesignToggle.IsChecked = on;
        if (on && AnimationsToggle.IsChecked == true) ShowAnimations(false);
        Panel.Mode = on ? PanelMode.Design : AnimationsToggle.IsChecked == true ? PanelMode.Animations : PanelMode.Properties;
        Panel.RebuildNow();
    }

    // ------------------------------------------------------------------ autosave (crash recovery)

    private void AutosaveTick()
    {
        if (MainWindow.IsTestOffscreen || Services.CaptureRunner.Running) return; // test runs leave the recovery folder alone
        var minutes = AppSettings.Current.AutosaveMinutes;
        if (minutes <= 0 || !Session.IsDirty) return;
        if (DateTime.UtcNow - _lastRecoverySave < TimeSpan.FromMinutes(minutes)) return;
        if (_recoveryVersion == _changeCounter) return;
        if (Canvas.IsEditingText) return; // try again on the next tick
        _lastRecoverySave = DateTime.UtcNow;
        _recoveryVersion = _changeCounter;
        _ = Recovery.SaveAsync(Session, _recoveryKey);
    }

    /// <summary>Asks to save unsaved changes. Returns false when the user cancels.</summary>
    public bool ConfirmClose()
    {
        if (_closeConfirmed) return true;
        WaitForAutoSave();
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        CommitTitle();
        if (!Session.IsDirty)
        {
            Recovery.Discard(_recoveryKey);
            _closeConfirmed = true;
            return true;
        }
        var choice = MessageDialog.Show(_window, $"Save changes to “{Session.DisplayName}”?",
            "Your changes will be lost if you don't save them.", "Save", "Don't save", "Cancel", DialogIcon.Question);
        if (choice == DialogChoice.Close) return false;
        if (choice == DialogChoice.Primary && !Save()) return false;
        Recovery.Discard(_recoveryKey);
        _closeConfirmed = true;
        return true;
    }

    // ------------------------------------------------------------------ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_paletteOpen) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        var inCanvasText = Canvas.IsEditingText && Keyboard.FocusedElement == Canvas.TextEditor;

        if (key == Key.Escape && (_painterArmed || _animationPainterArmed))
        {
            CancelPainters();
            e.Handled = true;
            return;
        }

        foreach (var command in Commands)
        {
            if (command.Gesture == null || !command.Gesture.Matches(key, mods)) continue;
            if (typing && !command.WorksWhileTyping && !(inCanvasText && command.WorksInSlideText)) continue;
            if (!command.CanExecute()) continue;
            command.Execute();
            e.Handled = true;
            return;
        }
    }
}
