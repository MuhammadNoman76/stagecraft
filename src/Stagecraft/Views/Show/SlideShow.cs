using System.Windows;
using Stagecraft.Core.Editing;
using Stagecraft.Interop;
using Stagecraft.Services;

namespace Stagecraft.Views.Show;

/// <summary>
/// Starts a slide show: with two or more displays the audience gets the second display (full screen) and the
/// presenter view opens on the main one; with one display the slides fill it. The hidden test mode opens both
/// as ordinary small windows so nothing covers the screen.
/// </summary>
public static class SlideShow
{
    public static SlideShowController? Current { get; private set; }

    /// <summary>Where test-mode windows open (screenshots put them off screen).</summary>
    public static Point? WindowedOrigin { get; set; }

    public static SlideShowController? Start(DeckSession session, int startIndex, ShowOptions options)
    {
        if (Current != null && !Current.Ended) return null;
        if (session.Deck.Slides.Count == 0) return null;
        var controller = new SlideShowController(session, startIndex, options);
        Current = controller;

        var monitors = Native.Monitors();
        var settings = AppSettings.Current;
        AudienceWindow? audience = null;
        PresenterWindow? presenter = null;

        if (options.Windowed)
        {
            if (!options.PresenterOnly) audience = new AudienceWindow(controller, null, windowed: true);
            presenter = new PresenterWindow(controller, null, windowed: true, sound: options.PresenterOnly);
        }
        else if (options.PresenterOnly)
        {
            presenter = new PresenterWindow(controller, monitors.FirstOrDefault(), windowed: false, sound: true);
        }
        else
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            Native.MonitorInfo? audienceMonitor = null;
            if (monitors.Count >= 2)
            {
                audienceMonitor = monitors.FirstOrDefault(m => !string.IsNullOrEmpty(settings.AudienceDisplay) && m.Device == settings.AudienceDisplay)
                                  ?? monitors.FirstOrDefault(m => !m.IsPrimary);
            }
            audienceMonitor ??= primary;
            audience = new AudienceWindow(controller, audienceMonitor, windowed: false);
            if (monitors.Count >= 2 && settings.UsePresenterView)
            {
                var presenterMonitor = monitors.FirstOrDefault(m => m != audienceMonitor) ?? primary;
                presenter = new PresenterWindow(controller, presenterMonitor, windowed: false);
            }
        }

        controller.LinkRequested += link => LinkLauncher.Open(link, (Window?)audience ?? presenter, session.FilePath != null ? System.IO.Path.GetDirectoryName(session.FilePath) : null);
        Native.KeepDisplayOn(true);
        controller.ShowEnded += () =>
        {
            Native.KeepDisplayOn(false);
            audience?.CloseFromShow();
            presenter?.CloseFromShow();
            if (Current == controller) Current = null;
        };

        presenter?.Show();
        audience?.Show();
        if (options.Windowed) presenter?.Activate();
        else (audience as Window ?? presenter)?.Activate();
        return controller;
    }

    /// <summary>The windows of the running show (tests and screenshots).</summary>
    public static IEnumerable<Window> Windows() =>
        Application.Current.Windows.OfType<Window>().Where(w => w is AudienceWindow or PresenterWindow);
}
