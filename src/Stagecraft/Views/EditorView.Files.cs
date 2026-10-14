using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Controls;
using Stagecraft.Core.Export;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;

namespace Stagecraft.Views;

public partial class EditorView
{
    // ------------------------------------------------------------------ menu

    private void OnMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string header, string icon, Action action, string gesture = "")
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }
        Item("New window", "Icon.WindowNew", () => App.Current.ShowStart(null), "Ctrl+N");
        Item("Open…", "Icon.FolderOpen", OpenDeck, "Ctrl+O");
        var recent = new MenuItem { Header = "Open recent" };
        Ui.SetIcon(recent, (Geometry)FindResource("Icon.History"));
        foreach (var r in AppSettings.Current.Recent.Take(10))
        {
            var path = r.Path;
            var mi = new MenuItem { Header = Path.GetFileNameWithoutExtension(path), ToolTip = path };
            mi.Click += (_, _) => App.Current.OpenPath(path, null);
            recent.Items.Add(mi);
        }
        recent.IsEnabled = recent.Items.Count > 0;
        menu.Items.Add(recent);
        Item("Import PowerPoint…", "Icon.ArrowImport", ImportPowerPoint);
        menu.Items.Add(new Separator());
        Item("Save", "Icon.Save", () => Save(), "Ctrl+S");
        Item("Save as…", "Icon.Save", () => SaveAs(), "Ctrl+Shift+S");
        Item("Export…", "Icon.ArrowExportLtr", () => ShowExport(null), "Ctrl+Shift+E");
        menu.Items.Add(new Separator());
        Item("Start screen", "Icon.Home", GoToStart);
        Item("Settings", "Icon.Settings", () => SettingsWindow.ShowSingle(_window), "Ctrl+,");
        Item("About Stagecraft", "Icon.Info", () => AboutDialog.Open(_window));
        menu.Items.Add(new Separator());
        Item("Close", "Icon.Dismiss", () => _window.Close(), "Ctrl+W");
        menu.IsOpen = true;
    }

    private void OpenDeck()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a presentation",
            Filter = "Presentations|*.stagecraft;*.pptx|Stagecraft presentations|*.stagecraft|PowerPoint presentations|*.pptx",
            CheckFileExists = true,
        };
        if (Session.FilePath != null) dialog.InitialDirectory = Path.GetDirectoryName(Session.FilePath);
        else if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        App.Current.OpenPath(dialog.FileName, null);
    }

    private void ImportPowerPoint()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a PowerPoint presentation",
            Filter = "PowerPoint presentations|*.pptx",
            CheckFileExists = true,
        };
        if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        App.Current.OpenPath(dialog.FileName, null);
    }

    // ------------------------------------------------------------------ save

    /// <summary>Saves to the deck's file (asks for one the first time). Returns false if not saved.</summary>
    public bool Save()
    {
        if (Session.FilePath == null) return SaveAs();
        return SaveTo(Session.FilePath);
    }

    public bool SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save the presentation",
            Filter = "Stagecraft presentation|*.stagecraft",
            DefaultExt = DeckFile.Extension,
            AddExtension = true,
            FileName = ImageExporter.SafeName(Session.Deck.Title) + DeckFile.Extension,
            OverwritePrompt = true,
        };
        if (Session.FilePath != null) dialog.InitialDirectory = Path.GetDirectoryName(Session.FilePath);
        else if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        else dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (dialog.ShowDialog(_window) != true) return false;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        return SaveTo(dialog.FileName);
    }

    // ------------------------------------------------------------------ automatic save

    /// <summary>How long editing must pause before the file saves itself.</summary>
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromSeconds(4);

    private Task? _autoSaveTask;
    private int _saveGeneration;
    private string? _autoSaveFailedFor;

    /// <summary>
    /// Saves a deck that already has a file, a few seconds after editing pauses (Settings › Save automatically). The
    /// snapshot is taken here; the file is written on a background thread, atomically, like a normal save.
    /// New decks are never saved automatically: they have no file until the user picks one.
    /// </summary>
    private async void AutoSaveFileTick()
    {
        _autoSaveFile.Stop();
        var path = Session.FilePath;
        if (path == null || !AppSettings.Current.AutoSaveFile || !Session.IsDirty || _detached || _autoSaveTask != null) return;
        // Not in the middle of something: typing on the slide, a drag, cropping, a dialog. Try again later.
        if (Canvas.IsEditingText || Canvas.IsCropping || Mouse.LeftButton == MouseButtonState.Pressed || !_window.IsEnabled ||
            System.Windows.Interop.ComponentDispatcher.IsThreadModal)
        {
            _autoSaveFile.Start();
            return;
        }
        Session.History.Seal(); // later edits start a new step, so the snapshot matches an undo state exactly
        var savedTop = Session.History.Top;
        Deck deck;
        MediaStore media;
        DeckPreviews previews;
        try
        {
            deck = Core.IO.DeckJson.Clone(Session.Deck);
            media = Session.Media.Clone();
            previews = BuildPreviews();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Automatic save snapshot");
            return;
        }
        var generation = _saveGeneration;
        var task = Task.Run(() => DeckFile.Save(path, deck, media, previews));
        _autoSaveTask = task;
        try
        {
            await task;
            if (Session.FilePath == path && generation == _saveGeneration)
            {
                Session.MarkSaved(path, savedTop);
                Recovery.Discard(_recoveryKey);
                _autoSaveFailedFor = null;
                _window.UpdateTitle();
                UpdateSaveState();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Automatic save " + path);
            // Say it once per file; Save (Ctrl+S) shows the full error.
            if (_autoSaveFailedFor != path) _window.ShowMessage("Couldn't save automatically: " + ex.Message, 6, warning: true);
            _autoSaveFailedFor = path;
        }
        finally
        {
            _autoSaveTask = null;
        }
    }

    /// <summary>Lets an automatic save that is writing finish first (before a manual save or closing).</summary>
    private void WaitForAutoSave()
    {
        _autoSaveFile.Stop();
        var task = _autoSaveTask;
        if (task == null) return;
        try
        {
            task.Wait(TimeSpan.FromMinutes(2));
        }
        catch (AggregateException)
        {
            // reported by the automatic save itself
        }
    }

    private bool SaveTo(string path)
    {
        WaitForAutoSave();
        _saveGeneration++;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        CommitTitle();
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var previews = BuildPreviews();
            DeckFile.Save(path, Session.Deck, Session.Media, previews);
            Session.MarkSaved(path);
            AppSettings.AddRecent(path);
            Recovery.Discard(_recoveryKey);
            _window.UpdateTitle();
            UpdateSaveState();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Save " + path);
            Mouse.OverrideCursor = null;
            MessageDialog.Show(_window, "The presentation couldn't be saved", ex.Message, "OK", icon: DialogIcon.Error);
            return false;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>The cover picture and small slide pictures stored in the file (the start screen shows the cover).</summary>
    private DeckPreviews BuildPreviews()
    {
        var previews = new DeckPreviews();
        try
        {
            var cover = Session.Deck.Slides.FirstOrDefault(s => !s.Hidden) ?? Session.Deck.Slides.FirstOrDefault();
            if (cover != null) previews.CoverPng = MediaStore.EncodePng(_thumbs.Render(cover, 640));
            foreach (var slide in Session.Deck.Slides.Take(400))
                previews.SlideJpegs.Add(MediaStore.EncodeJpeg(_thumbs.Render(slide, 240), 80));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Build previews");
        }
        return previews;
    }

    // ------------------------------------------------------------------ export

    public void ShowExport(string? format)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        var options = ExportDialog.Ask(_window, Session, _state.CurrentSlide, format);
        if (options == null) return;
        RunExport(options);
    }

    private async void RunExport(ExportChoice choice)
    {
        var deck = DeckJson.Clone(Session.Deck);
        var media = Session.Media;
        var currentIndex = _state.CurrentIndex;
        var title = choice.Format switch
        {
            "pdf" => "Exporting PDF…",
            "pptx" => "Exporting PowerPoint…",
            _ => "Exporting pictures…",
        };
        _window.SetBusy(title, "Preparing", 0);
        var progress = new Action<int, int>((done, total) => Dispatcher.BeginInvoke(new Action(() =>
            _window.SetBusy(title, $"Slide {done} of {total}", total == 0 ? 0 : (double)done / total))));
        Exception? error = null;
        List<string> files = new();
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                switch (choice.Format)
                {
                    case "pdf":
                        PdfExporter.Export(deck, media, choice.Path, new PdfExportOptions { IncludeHidden = choice.IncludeHidden, Vector = choice.Vector, Progress = progress });
                        files.Add(choice.Path);
                        break;
                    case "pptx":
                        PptxExporter.Export(deck, media, choice.Path, new PptxExportOptions { IncludeHidden = choice.IncludeHidden, Progress = progress });
                        files.Add(choice.Path);
                        break;
                    default:
                        var format = choice.Format == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png;
                        if (choice.CurrentSlideOnly && currentIndex >= 0)
                        {
                            ImageExporter.ExportSlide(deck, media, deck.Slides[currentIndex], choice.Path, format, choice.PixelWidth);
                            files.Add(choice.Path);
                        }
                        else
                        {
                            files = ImageExporter.ExportAll(deck, media, choice.Path, deck.Title, format, choice.PixelWidth, choice.IncludeHidden, progress);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        })
        {
            IsBackground = true,
            Name = "Export",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await tcs.Task;
        _window.SetBusy(null);

        if (error != null)
        {
            Log.Error(error, "Export " + choice.Format);
            MessageDialog.Show(_window, "The export didn't work", error.Message, "OK", icon: DialogIcon.Error);
            return;
        }
        AppSettings.Update(s => s.LastExportFolder = Directory.Exists(choice.Path) ? choice.Path : Path.GetDirectoryName(choice.Path));
        var what = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} pictures";
        _window.ShowMessage($"Exported {what}.", 4);
        if (choice.OpenWhenDone && files.Count > 0)
        {
            try
            {
                if (files.Count == 1) Process.Start(new ProcessStartInfo(files[0]) { UseShellExecute = true })?.Dispose();
                else Process.Start(new ProcessStartInfo("explorer.exe", "\"" + choice.Path + "\"") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Open exported file");
            }
        }
    }
}
