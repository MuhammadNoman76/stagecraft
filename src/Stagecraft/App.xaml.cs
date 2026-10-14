using System.Windows;
using System.Windows.Threading;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Export;
using Stagecraft.Core.IO;
using Stagecraft.Services;
using Stagecraft.Themes;
using Stagecraft.Views;
using Stagecraft.Views.Show;

namespace Stagecraft;

public partial class App
{
    private readonly Command _command;

    public App(Command command)
    {
        _command = command;
    }

    public static new App Current => (App)Application.Current;

    /// <summary>Started with --updated after an automatic update.</summary>
    public bool WasUpdated => _command.Updated;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI thread");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Task");
            args.SetObserved();
        };
        // Videos and sounds of decks open in Stagecraft copies that crashed or were killed.
        Task.Run(() =>
        {
            try
            {
                Core.Rendering.MediaCache.CleanStale();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Clean the media cache");
            }
        });

        try
        {
            ThemeManager.Initialize(AppSettings.Load().Theme);
            switch (_command.Verb)
            {
                case Verb.ExportPdf:
                    Shutdown(ExportHeadless(_command));
                    return;
                case Verb.Present:
                    StartShow(_command);
                    return;
                case Verb.Capture:
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    Dispatcher.BeginInvoke(new Action(() => CaptureRunner.Run(_command.Output!)), DispatcherPriority.Loaded);
                    return;
                case Verb.Settings:
                    ShutdownMode = ShutdownMode.OnLastWindowClose;
                    var settings = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    MainWindow = settings;
                    settings.Show();
                    return;
                default:
                    ShutdownMode = ShutdownMode.OnLastWindowClose;
                    InstanceManager.Listen(args => Dispatcher.BeginInvoke(new Action(() => OnRemoteCommand(args))));
                    var paths = _command.Paths;
                    if (paths.Count == 0) ShowStart(null);
                    else foreach (var p in paths.Take(8)) OpenPath(p, null);
                    UpdateService.Start();
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Startup");
            MessageBox.Show("Stagecraft couldn't start:\n\n" + ex.Message, "Stagecraft", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Core.Rendering.MediaCache.DeleteOwnFolder();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete the media cache");
        }
        base.OnExit(e);
    }

    /// <summary>Another launch handed us its command line.</summary>
    private void OnRemoteCommand(string[] args)
    {
        var command = Command.Parse(args);
        if (command.Verb == Verb.Settings)
        {
            SettingsWindow.ShowSingle(Windows.OfType<Window>().FirstOrDefault(w => w.IsActive));
            return;
        }
        if (command.Paths.Count == 0)
        {
            ShowStart(null);
            return;
        }
        foreach (var p in command.Paths.Take(8)) OpenPath(p, null);
    }

    // ------------------------------------------------------------------ windows

    public IEnumerable<MainWindow> DeckWindows => Windows.OfType<MainWindow>();

    /// <summary>A window with the start screen (a new one unless <paramref name="reuse"/> is given).</summary>
    public MainWindow ShowStart(MainWindow? reuse)
    {
        var window = reuse ?? CreateWindow();
        window.ShowStartView();
        if (!window.IsVisible) window.Show();
        window.BringToFront();
        return window;
    }

    private MainWindow CreateWindow()
    {
        var window = new MainWindow();
        MainWindow ??= window;
        return window;
    }

    /// <summary>
    /// Opens a deck (or imports a PowerPoint file). If it's already open, that window comes forward. The start
    /// screen of <paramref name="requester"/> is reused; otherwise a new window opens.
    /// </summary>
    public async void OpenPath(string path, MainWindow? requester)
    {
        var existing = DeckWindows.FirstOrDefault(w => w.Session?.FilePath != null && string.Equals(w.Session.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.BringToFront();
            return;
        }
        var window = requester != null && requester.Session == null ? requester : CreateWindow();
        if (!window.IsVisible)
        {
            window.ShowStartView();
            window.Show();
        }
        window.BringToFront();
        await window.OpenFileAsync(path);
    }

    /// <summary>A window for a session that was created in code (new deck, import, recovery).</summary>
    public MainWindow OpenSession(DeckSession session, MainWindow? requester, string? message = null)
    {
        var window = requester != null && requester.Session == null ? requester : CreateWindow();
        window.ShowEditor(session);
        if (!window.IsVisible) window.Show();
        window.BringToFront();
        if (message != null) window.ShowMessage(message);
        return window;
    }

    // ------------------------------------------------------------------ command-line modes

    /// <summary>--export-pdf &lt;deck&gt; &lt;out.pdf&gt;: no window; exit code 0 on success.</summary>
    private static int ExportHeadless(Command command)
    {
        try
        {
            if (command.Paths.Count == 0 || command.Output == null)
            {
                Console.Error.WriteLine("Usage: Stagecraft.exe --export-pdf <deck.stagecraft|.pptx> <output.pdf>");
                return 2;
            }
            var input = command.Paths[0];
            var doc = DocumentLoader.Load(input);
            PdfExporter.Export(doc.Deck, doc.Media, command.Output, new PdfExportOptions());
            Log.Info($"Exported {input} to {command.Output}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export PDF from the command line");
            try { Console.Error.WriteLine(ex.Message); } catch { /* no console */ }
            return 1;
        }
    }

    /// <summary>--present &lt;deck&gt;: the slide show, then exit.</summary>
    private void StartShow(Command command)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (command.Paths.Count == 0)
        {
            Shutdown(2);
            return;
        }
        try
        {
            var doc = DocumentLoader.Load(command.Paths[0]);
            var session = new DeckSession(doc.Deck, doc.Media, command.Paths[0]);
            var controller = SlideShow.Start(session, Math.Max(0, command.StartSlide - 1), new ShowOptions { Windowed = command.WindowedShow });
            if (controller == null)
            {
                Shutdown(1);
                return;
            }
            controller.ShowEnded += () => Shutdown(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Present from the command line");
            MessageBox.Show("Stagecraft couldn't open this presentation:\n\n" + ex.Message, "Stagecraft", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
