using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;
using Stagecraft.Core.Themes;
using Stagecraft.Interop;
using Stagecraft.Themes;
using Stagecraft.Views;
using Stagecraft.Views.Editor;
using Stagecraft.Views.Show;

namespace Stagecraft.Services;

/// <summary>
/// Development aid (hidden --capture &lt;folder&gt;): opens Stagecraft's own windows with the sample decks and saves
/// pictures of them. Only Stagecraft windows are captured (PrintWindow on our own window handles, never the
/// screen), nothing is ever full screen, and it waits while another app is in full-screen mode.
/// </summary>
internal static class CaptureRunner
{
    /// <summary>A capture run is going on (settings stay in memory, see <see cref="AppSettings.Update"/>).</summary>
    public static bool Running { get; private set; }

    private static string s_folder = "";
    private static readonly List<string> s_written = new();

    public static async void Run(string folder)
    {
        Running = true;
        s_folder = folder;
        var exitCode = 0;
        try
        {
            Directory.CreateDirectory(folder);
            await WaitWhileBusy();
            SlideShow.WindowedOrigin = new Point(OffscreenLeft, SystemParameters.VirtualScreenTop + 20);
            var only = Environment.GetEnvironmentVariable("STAGECRAFT_CAPTURE_ONLY");
            await Script(only);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Capture");
            File.WriteAllText(Path.Combine(folder, "capture-error.txt"), ex.ToString());
            exitCode = 1;
        }
        File.WriteAllLines(Path.Combine(folder, "captured.txt"), s_written);
        foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
        {
            try
            {
                if (w is MainWindow mw && mw.Session != null) mw.Session.History.Clear();
                if (w is MainWindow { Editor: { } editor }) editor.Detach();
                w.Close();
            }
            catch
            {
                // closing anyway
            }
        }
        Application.Current.Shutdown(exitCode);
    }

    private static async Task WaitWhileBusy()
    {
        var waited = 0;
        while (Native.IsUserBusy() && waited < 600)
        {
            await Task.Delay(2000);
            waited += 2;
        }
    }

    private static async Task Settle(int ms = 450)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static bool Wanted(string? only, string name) => string.IsNullOrEmpty(only) || only!.Split(',').Any(o => name.StartsWith(o.Trim(), StringComparison.OrdinalIgnoreCase));

    private static async Task Script(string? only)
    {
        foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.SetMode(mode);
            var suffix = mode == AppTheme.Light ? "light" : "dark";
            var window = NewWindow();
            window.ShowStartView();
            await Settle(900);
            if (Wanted(only, "start")) await Capture(window, $"start-{suffix}");

            // A deck file (STAGECRAFT_CAPTURE_FILE), opened the way File › Open does.
            var file = Environment.GetEnvironmentVariable("STAGECRAFT_CAPTURE_FILE");
            if (!string.IsNullOrEmpty(file) && Wanted(only, "file"))
            {
                var loaded = Core.IO.DocumentLoader.Load(file!);
                window.ShowEditor(new DeckSession(loaded.Deck, loaded.Media, Core.IO.DocumentLoader.IsDeck(file!) ? file : null));
                await Settle(900);
                await Capture(window, $"file-{suffix}");
            }

            // The editor with each sample deck.
            foreach (var sample in DemoDecks.All)
            {
                if (!Wanted(only, "editor")) break;
                var doc = sample.Create();
                window.ShowEditor(new DeckSession(doc.Deck, doc.Media));
                await Settle(900);
                var editor = window.Editor!;
                var slideIndex = sample.Id == "aurora-kickoff" ? 3 : sample.Id == "quarterly-review" ? 2 : 0;
                editor.State.GoTo(doc.Deck.Slides[slideIndex]);
                await Settle(700);
                await Capture(window, $"editor-{sample.Id}-{suffix}");
            }

            if (mode == AppTheme.Light && Wanted(only, "smoke")) await Smoke(window);
            if (Wanted(only, "v11")) await V11(window, suffix);

            if (mode == AppTheme.Light && Wanted(only, "theme"))
            {
                // One deck in every theme.
                foreach (var theme in ThemeCatalog.All)
                {
                    var doc = DemoDecks.AuroraKickoff();
                    DeckOperations.ApplyTheme(doc.Deck, theme, null);
                    window.ShowEditor(new DeckSession(doc.Deck, doc.Media));
                    await Settle(700);
                    window.Editor!.State.GoTo(doc.Deck.Slides[0]);
                    await Settle(500);
                    await Capture(window, $"theme-{theme.Id}");
                }
            }

            if (Wanted(only, "ui"))
            {
                var doc = DemoDecks.AuroraKickoff();
                window.ShowEditor(new DeckSession(doc.Deck, doc.Media));
                await Settle(800);
                var editor = window.Editor!;

                // A selection with the properties panel.
                editor.State.GoTo(doc.Deck.Slides[4]);
                await Settle(300);
                var body = doc.Deck.Slides[4].Elements.FirstOrDefault(e => e.Placeholder == "body");
                if (body != null) editor.State.Select(new[] { body.Id });
                await Settle(600);
                await Capture(window, $"selection-{suffix}");

                // Design panel.
                editor.State.ClearSelection();
                editor.ShowDesign(true);
                await Settle(800);
                await Capture(window, $"design-{suffix}");
                editor.ShowDesign(false);

                // Command palette.
                editor.OpenPalette();
                await Settle(400);
                TypeInPalette(window, "lay");
                await Settle(500);
                await Capture(window, $"palette-{suffix}");
                CloseOverlays(window);

                // Sorter and outline.
                editor.SetView(1);
                await Settle(1200);
                await Capture(window, $"sorter-{suffix}");
                editor.SetView(2);
                await Settle(700);
                await Capture(window, $"outline-{suffix}");
                editor.SetView(0);
                await Settle(500);

                // Dialogs (captured while they're open, then closed).
                var chartSlide = doc.Deck.Slides[5];
                editor.State.GoTo(chartSlide);
                await Settle(300);
                var chart = chartSlide.Elements.OfType<Core.Model.ChartElement>().First();
                await CaptureDialog<ChartEditorDialog>($"chart-editor-{suffix}", () => editor.EditChart(chart));
                await CaptureDialog<IconPickerDialog>($"icon-picker-{suffix}", () => editor.PickIcon(null));
                await CaptureDialog<ExportDialog>($"export-{suffix}", () => editor.ShowExport("pdf"));
                await CaptureDialog<AboutDialog>($"about-{suffix}", () => AboutDialog.Open(window));
                var settings = new SettingsWindow { Owner = window, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = OffscreenLeft, Top = SystemParameters.VirtualScreenTop + 20 };
                settings.Show();
                await Settle(700);
                await Capture(settings, $"settings-{suffix}");
                settings.Close();

                // Presenter view (windowed test mode only).
                editor.State.GoTo(doc.Deck.Slides[0]);
                editor.Present(fromCurrent: false, windowed: true);
                await Settle(1200);
                var controller = SlideShow.Current!;
                controller.GoTo(3);
                await Settle(900);
                foreach (var w in SlideShow.Windows().ToList())
                    await Capture(w, w is PresenterWindow ? $"presenter-{suffix}" : $"audience-{suffix}");
                controller.End();
                await Settle(400);
            }
            window.Close();
            await Settle(300);
        }
    }

    /// <summary>
    /// 1.1's screens: the animation pane and gallery, motion path editing, links (dialog and the show's tip),
    /// video in the editor with its bar and options, the trim dialog, header and footer, find and replace, crop,
    /// rulers and guides. All in Stagecraft's own off-screen windows.
    /// </summary>
    private static async Task V11(MainWindow window, string suffix)
    {
        // Animation pane with its timeline, then the gallery over it (shown in the window, not as a popup).
        var aurora = DemoDecks.AuroraKickoff();
        window.ShowEditor(new DeckSession(aurora.Deck, aurora.Media));
        await Settle(900);
        var editor = window.Editor!;
        var agenda = aurora.Deck.Slides[1];
        editor.State.GoTo(agenda);
        await Settle(300);
        editor.ShowAnimations(true);
        var first = agenda.Animations[0];
        editor.State.Select(new[] { first.Target });
        editor.State.SelectEffects(new[] { first.Id });
        await Settle(700);
        await Capture(window, $"animation-pane-{suffix}");

        FrameworkElement? gallery = null;
        AnimationGallery.Intercept = g => gallery = g;
        editor.Panel.OpenAddAnimation();
        AnimationGallery.Intercept = null;
        if (gallery != null)
        {
            gallery.HorizontalAlignment = HorizontalAlignment.Right;
            gallery.VerticalAlignment = VerticalAlignment.Top;
            gallery.Margin = new Thickness(0, 200, 20, 0);
            editor.OverlayLayer.Children.Add(gallery);
            await Settle(500);
            editor.PreviewEffect(Core.Model.EffectClass.Entrance, Core.Model.EffectKind.Fly);
            await Task.Delay(260);
            await Capture(window, $"animation-gallery-{suffix}");
            editor.StopEffectPreview();
            editor.OverlayLayer.Children.Remove(gallery);
            await Settle(200);
        }

        // Header and footer, then the dialog.
        window.Session!.EditStructure("Header and footer", d => d.Footer = new Core.Model.HeaderFooter { SlideNumber = true, Date = true, Text = "Aurora Project kickoff" });
        editor.State.GoTo(aurora.Deck.Slides[4]);
        await Settle(500);
        await CaptureDialog<HeaderFooterDialog>($"header-footer-{suffix}", () => editor.EditHeaderFooter());

        // Link dialog (the agenda's first item links to a slide).
        editor.ShowAnimations(false);
        editor.State.GoTo(agenda);
        await Settle(300);
        var item1 = agenda.Elements.First(e => e.Placeholder == "item1");
        editor.State.Select(new[] { item1.Id });
        await Settle(400);
        await CaptureDialog<LinkDialog>($"link-dialog-{suffix}", () => editor.InsertLink());
        await Capture(window, $"link-panel-{suffix}");

        // A video on a slide, its playback bar and options; then the trim dialog.
        var look = aurora.Deck.Slides.First(s => s.Elements.OfType<Core.Model.VideoElement>().Any());
        var video = look.Elements.OfType<Core.Model.VideoElement>().First();
        editor.State.GoTo(look);
        await Settle(400);
        editor.State.Select(new[] { video.Id });
        await Settle(500);
        editor.SlideCanvas.HoverForCapture(video);
        await Settle(900);
        await Capture(window, $"video-editor-{suffix}");
        editor.SlideCanvas.StopMediaPreview();
        await CaptureDialog<TrimDialog>($"trim-dialog-{suffix}", () => editor.TrimMedia(video));

        // Find and replace.
        editor.State.GoTo(aurora.Deck.Slides[4]);
        await Settle(400);
        editor.OpenFind(replace: true);
        editor.FindPanel.SetForCapture("customer", "client");
        await Settle(700);
        await Capture(window, $"find-replace-{suffix}");
        editor.FindPanel.Close();
        editor.SlideCanvas.SetFindHighlights(Array.Empty<Core.Editing.FindMatch>(), null);

        // Rulers, gridlines and a guide (settings changed in memory only, never saved).
        var settings = AppSettings.Current;
        var (rulers, grid) = (settings.ShowRulers, settings.ShowGrid);
        settings.ShowRulers = true;
        settings.ShowGrid = true;
        window.Session.EditStructure("Add guide", d => d.Guides.Add(new Core.Model.GuideLine { Vertical = true, Position = 960 }), redrawSlides: false);
        editor.SlideCanvas.ApplyViewAids();
        editor.State.ClearSelection();
        await Settle(600);
        await Capture(window, $"view-aids-{suffix}");
        settings.ShowRulers = rulers;
        settings.ShowGrid = grid;
        editor.SlideCanvas.ApplyViewAids();

        // A link's tip in the slide show (windowed test mode).
        var thanks = aurora.Deck.Slides[aurora.Deck.Slides.Count - 1];
        editor.State.GoTo(thanks);
        await Settle(300);
        editor.Present(fromCurrent: true, windowed: true);
        await Settle(1400);
        var link = LinkRegions.For(thanks, new RenderContext(aurora.Deck, aurora.Media)).FirstOrDefault();
        foreach (var w in SlideShow.Windows().ToList())
        {
            if (w is not AudienceWindow audience) continue;
            var surface = FindChild<ShowSurface>(audience, _ => true);
            if (surface != null && link != null) surface.HoverForCapture(new Point(link.Bounds.X + link.Bounds.Width / 2, link.Bounds.Y + link.Bounds.Height / 2));
            await Settle(500);
            await Capture(audience, $"show-link-tip-{suffix}");
        }
        SlideShow.Current?.End();
        await Settle(500);

        // Motion path editing and crop (the coffee deck: the sun's path over the morning picture).
        var coffee = DemoDecks.CoffeePitch();
        window.ShowEditor(new DeckSession(coffee.Deck, coffee.Media));
        await Settle(900);
        editor = window.Editor!;
        var morning = coffee.Deck.Slides.First(s => s.Animations.Any(a => a.Class == Core.Model.EffectClass.Motion));
        var motion = morning.Animations.First(a => a.Class == Core.Model.EffectClass.Motion);
        editor.State.GoTo(morning);
        await Settle(300);
        editor.ShowAnimations(true);
        editor.State.Select(new[] { motion.Target });
        editor.State.SelectEffects(new[] { motion.Id });
        await Settle(800);
        await Capture(window, $"motion-path-{suffix}");
        editor.ShowAnimations(false);
        var picture = morning.Elements.OfType<Core.Model.ImageElement>().First();
        editor.StartCrop(picture);
        await Settle(700);
        await Capture(window, $"crop-{suffix}");
        editor.SlideCanvas.EndCrop(commit: false);
        await Settle(300);
        window.Session!.History.Clear();
    }

    /// <summary>
    /// Runs (almost) every editor command on a new deck, then undoes and redoes them all, and writes smoke.txt with
    /// anything that threw. Dialogs that commands open (chart, table, section name) are accepted through UI
    /// Automation on our own window; nothing touches the clipboard, files or the screen.
    /// </summary>
    private static async Task Smoke(MainWindow window)
    {
        var report = new List<string>();
        var deck = DeckOperations.NewDeck(ThemeCatalog.Default);
        window.ShowEditor(new DeckSession(deck, new Core.Rendering.MediaStore()));
        await Settle(600);
        var editor = window.Editor!;

        var accept = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        accept.Tick += (_, _) =>
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w is ChartEditorDialog or TableEditorDialog or InputDialog or HeaderFooterDialog).ToList())
            {
                MoveOffscreen(w);
                var button = FindChild<System.Windows.Controls.Button>(w, b => b.IsDefault || b.Content as string == "Done");
                if (button != null && new System.Windows.Automation.Peers.ButtonAutomationPeer(button).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)
                        is System.Windows.Automation.Provider.IInvokeProvider invoke)
                    invoke.Invoke();
            }
            // Dialogs that need input of their own (links, online videos, trimming, messages): cancelled.
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w is LinkDialog or OnlineVideoDialog or TrimDialog or MessageDialog).ToList())
            {
                MoveOffscreen(w);
                w.Close();
            }
        };
        accept.Start();

        // Not run: clipboard, files, windows of their own, the slide show and app-wide settings.
        var skip = new HashSet<string>
        {
            "Cut", "Copy", "Paste", "Picture…", "Icon…", "Background picture…", "Command palette", "Slide size: custom…",
            "Present from the beginning", "Present from this slide", "Presenter view on this screen", "Rehearse timings",
            // These are saved in the settings.
            "Show or hide speaker notes", "Turn smart guides on or off", "Show or hide rulers", "Show or hide gridlines", "Snap to grid on or off",
            // File pickers (Windows' own dialogs).
            "Video…", "Sound…", "Replace picture…",
        };
        var order = new[] { "Insert", "Arrange", "Text", "Edit", "Slide", "Layout", "Transition", "Design", "Theme", "View", "Animation", "Picture", "Media", "Present" };
        var commands = editor.Commands.Where(c => order.Contains(c.Category) && !skip.Contains(c.Title)).OrderBy(c => Array.IndexOf(order, c.Category)).ToList();
        var themes = 0;
        var ran = 0;
        foreach (var command in commands)
        {
            if (command.Category == "Theme" && ++themes > 4) continue;
            if (editor.SlideCanvas.IsEditingText) editor.SlideCanvas.CommitTextEdit();
            // Most commands work on a selection: select everything on the slide first (except for Insert).
            if (command.Category != "Insert" && editor.State.CurrentSlide is { } slide)
                editor.State.Select(slide.Elements.Where(e => e.Placeholder != "decor").Select(e => e.Id));
            File.AppendAllText(Path.Combine(s_folder, "smoke-progress.txt"), command.Category + " › " + command.Title + Environment.NewLine);
            try
            {
                if (!command.CanExecute()) continue;
                command.Execute();
                ran++;
            }
            catch (Exception ex)
            {
                report.Add($"{command.Category} › {command.Title}: {ex.GetType().Name}: {ex.Message}");
                Log.Error(ex, "Smoke " + command.Title);
            }
            await Settle(40);
        }
        if (editor.SlideCanvas.IsEditingText) editor.SlideCanvas.CommitTextEdit();
        await Settle(600);

        var history = window.Session!.History;
        var steps = history.Count;
        try
        {
            while (history.CanUndo) history.Undo();
            await Settle(200);
            while (history.CanRedo) history.Redo();
        }
        catch (Exception ex)
        {
            report.Add($"Undo / redo: {ex.GetType().Name}: {ex.Message}");
            Log.Error(ex, "Smoke undo / redo");
        }
        accept.Stop();
        await Settle(500);
        report.Insert(0, $"{ran} commands ran, {steps} undo steps, {window.Session.Deck.Slides.Count} slides, {report.Count} problems");
        File.WriteAllLines(Path.Combine(s_folder, "smoke.txt"), report);
        editor.SetView(0);
        await Settle(400);
        await Capture(window, "smoke");
        history.Clear();
    }

    private static MainWindow NewWindow()
    {
        // Off screen (left of every display): nothing shows up over the owner's work, and clicks can't reach it.
        var window = new MainWindow
        {
            Width = 1440,
            Height = 900,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = OffscreenLeft,
            Top = SystemParameters.VirtualScreenTop + 20,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.WindowState = WindowState.Normal;
        window.Show();
        return window;
    }

    private static double OffscreenLeft => SystemParameters.VirtualScreenLeft - 3200;

    private static void MoveOffscreen(Window w)
    {
        w.Left = OffscreenLeft;
        w.Top = SystemParameters.VirtualScreenTop + 20;
    }

    private static void TypeInPalette(MainWindow window, string text)
    {
        var box = FindChild<System.Windows.Controls.TextBox>(window, t => System.Windows.Automation.AutomationProperties.GetName(t) == "Search commands");
        if (box != null) box.Text = text;
    }

    private static void CloseOverlays(MainWindow window)
    {
        var palette = FindChild<Views.Editor.CommandPalette>(window, _ => true);
        if (palette?.Parent is System.Windows.Controls.Panel panel) panel.Children.Remove(palette);
        var field = typeof(EditorView).GetField("_paletteOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (window.Editor != null) field?.SetValue(window.Editor, false);
    }

    private static T? FindChild<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            var found = FindChild(child, match);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Opens a modal dialog, captures it once it's drawn, then closes it.</summary>
    private static async Task CaptureDialog<T>(string name, Action open) where T : Window
    {
        var done = new TaskCompletionSource<bool>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var dialog = Application.Current.Windows.OfType<T>().FirstOrDefault();
            if (dialog != null)
            {
                MoveOffscreen(dialog);
                await Settle(400);
                await Capture(dialog, name);
                dialog.Close();
            }
            done.TrySetResult(true);
        };
        timer.Start();
        open();
        await done.Task;
        await Settle(300);
    }

    public static async Task Capture(Window window, string name)
    {
        await Settle(150);
        var hwnd = new WindowInteropHelper(window).Handle;
        BitmapSource? bitmap = null;
        var shot = Native.CaptureOwnWindow(hwnd);
        if (shot is { } s && !MostlyBlack(s.Pixels))
        {
            // PrintWindow gives the window including its frame; Mica regions come back transparent, so put them on the window colour.
            var raw = BitmapSource.Create(s.Width, s.Height, 96, 96, PixelFormats.Bgra32, null, s.Pixels, s.Width * 4);
            bitmap = Flatten(raw, window);
        }
        if (bitmap == null && window.Content is FrameworkElement content)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var rtb = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(content);
            bitmap = Flatten(rtb, window);
        }
        if (bitmap == null) return;
        var path = Path.Combine(s_folder, name + ".png");
        File.WriteAllBytes(path, MediaStore.EncodePng(bitmap));
        s_written.Add(path);
    }

    private static bool MostlyBlack(byte[] pixels)
    {
        long sum = 0;
        var samples = 0;
        for (var i = 0; i < pixels.Length; i += 4 * 97)
        {
            sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
            samples++;
        }
        return samples == 0 || sum / (double)samples < 6;
    }

    private static BitmapSource Flatten(BitmapSource source, Window window)
    {
        var dark = window is PresenterWindow or AudienceWindow || ThemeManager.IsDark;
        var bg = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            dc.DrawRectangle(new SolidColorBrush(bg), null, rect);
            dc.DrawImage(source, rect);
        }
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
