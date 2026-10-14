using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Interop;
using Stagecraft.Views.Show;

namespace Stagecraft.Views;

public partial class EditorView
{
    private ShowSurface? _preview;

    private void OnPresent(object sender, RoutedEventArgs e) => Present(fromCurrent: false);

    private void OnPresentMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PresentMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item("From the beginning", "Icon.PlayFilled", () => Present(fromCurrent: false), "F5");
        Item("From this slide", "Icon.Play", () => Present(fromCurrent: true), "Shift+F5");
        Item("Presenter view on this screen", "Icon.Presenter", () => Present(fromCurrent: true, presenterOnly: true), "Alt+F5");
        Item("Rehearse timings", "Icon.Timer", () => Present(fromCurrent: false, presenterOnly: true, rehearse: true));
        menu.Items.Add(new Separator());
        Item("Slide show settings…", "Icon.Settings", () => SettingsWindow.ShowSingle(_window));
        menu.IsOpen = true;
    }

    /// <summary>Starts the slide show (full screen; see SlideShow for how displays are used).</summary>
    public void Present(bool fromCurrent, bool presenterOnly = false, bool rehearse = false, bool windowed = false)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        if (Session.Deck.Slides.Count == 0) return;
        if (SlideShow.Current != null) return;
        var start = fromCurrent ? Math.Max(0, _state.CurrentIndex) : 0;
        var controller = SlideShow.Start(Session, start, new ShowOptions { PresenterOnly = presenterOnly, Rehearse = rehearse, Windowed = windowed });
        if (controller == null) return;
        controller.ShowEnded += () =>
        {
            _window.BringToFront();
            var last = controller.Current;
            if (last != null && Session.Deck.Slides.Contains(last)) _state.GoTo(last);
            if (rehearse && controller.Timings.Count > 0) OfferTimings(controller);
        };
    }

    private void OfferTimings(SlideShowController controller)
    {
        var total = TimeSpan.FromSeconds(controller.Timings.Values.Sum());
        var choice = MessageDialog.Show(_window, "Keep the rehearsed timings?",
            $"The rehearsal took {(int)total.TotalMinutes}:{total.Seconds:00}. Stagecraft shows each slide's time in the slide panel.",
            "Keep timings", close: "Discard", icon: DialogIcon.Question);
        if (choice != DialogChoice.Primary) return;
        var timings = controller.Timings;
        Session.EditDeck("Rehearsed timings", d =>
        {
            foreach (var s in d.Slides)
                if (timings.TryGetValue(s.Id, out var seconds)) s.RehearsedSeconds = Math.Round(seconds, 1);
        });
    }

    // ------------------------------------------------------------------ previews on the canvas

    public void PreviewTransition() => StartPreview(transition: true, animations: false);

    /// <summary>Plays the slide's animations on the canvas, from the beginning or from a click.</summary>
    public void PreviewAnimations(int fromClick = 0) => StartPreview(transition: false, animations: true, fromClick: fromClick);

    private void StartPreview(bool transition, bool animations, int fromClick = 0, Slide? only = null, bool quiet = false)
    {
        var slide = only ?? _state.CurrentSlide;
        if (slide == null || _altViewIndex != 0) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        StopPreview();
        var index = _state.CurrentIndex;
        var previous = index > 0 ? Session.Deck.Slides[index - 1] : null;
        if (transition && previous == null)
        {
            _window.ShowMessage("This is the first slide, so there's no transition into it to preview.", 4);
            return;
        }
        if (animations && only == null && SlideTimeline.Build(slide).Groups.Count == 0)
        {
            _window.ShowMessage("Nothing on this slide is animated yet. Select something and choose Add animation.", 5);
            return;
        }
        var rect = Canvas.ToView(new Rect(0, 0, Session.Deck.Width, Session.Deck.Height));
        var surface = new ShowSurface
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(rect.X, rect.Y, 0, 0),
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Cursor = quiet ? null : System.Windows.Input.Cursors.Hand,
            ToolTip = quiet ? null : "Click to stop the preview",
            IsHitTestVisible = !quiet,
        };
        surface.MouseLeftButtonDown += (_, _) => StopPreview();
        Canvas.Children.Add(surface);
        _preview = surface;
        _previewQuiet = quiet;
        surface.Preview(previous, slide, Session.CreateRenderContext(), transition, animations, StopPreview, fromClick);
    }

    private bool _previewQuiet;

    /// <summary>The gallery's live preview: plays one effect on the selection (nothing is changed).</summary>
    public void PreviewEffect(EffectClass cls, EffectKind kind)
    {
        var slide = _state.CurrentSlide;
        if (slide == null || _state.Selection.Count == 0) return;
        var probe = DeckJson.Clone(slide);
        probe.Animations.Clear();
        var targets = AnimationOps.TargetsFor(_state.SelectedElements, _state.EnteredGroup);
        var added = AnimationOps.Add(probe, cls, kind, targets);
        foreach (var a in added) a.Start = EffectStart.WithPrevious;
        // Keep the other elements' own entrances out of it: they're just shown.
        StartPreview(transition: false, animations: true, only: probe, quiet: true);
    }

    /// <summary>Stops a quiet (gallery) preview.</summary>
    public void StopEffectPreview()
    {
        if (_previewQuiet) StopPreview();
    }

    private void StopPreview()
    {
        if (_preview == null) return;
        var p = _preview;
        _preview = null;
        p.FinishAll();
        p.Shutdown();
        Canvas.Children.Remove(p);
    }
}
