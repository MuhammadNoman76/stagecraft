using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;

namespace Stagecraft.Views;

/// <summary>Video, sound and online video: inserting (embedded or linked), checking that Windows can play it, options.</summary>
public partial class EditorView
{
    private const string VideoFilter = "Videos|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.3gp;*.mpg;*.mpeg;*.ts;*.asf|All files|*.*";
    private const string AudioFilter = "Sounds|*.mp3;*.m4a;*.aac;*.wav;*.wma;*.flac;*.ogg;*.opus;*.aif;*.aiff|All files|*.*";

    private void OnInsertMediaMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MediaButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string header, string icon, Action action)
        {
            var mi = new MenuItem { Header = header };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item("Video from a file…", "Icon.VideoClip", () => InsertVideo());
        Item("Sound from a file…", "Icon.MusicNote2", () => InsertAudio());
        Item("Online video…", "Icon.GlobeVideo", () => InsertOnlineVideo());
        menu.IsOpen = true;
    }

    public void InsertVideo()
    {
        var path = PickMediaFile("Insert a video", VideoFilter);
        if (path != null) _ = InsertMediaFileAsync(path, null);
    }

    public void InsertAudio()
    {
        var path = PickMediaFile("Insert a sound", AudioFilter);
        if (path != null) _ = InsertMediaFileAsync(path, null);
    }

    private string? PickMediaFile(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return null;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        return dialog.FileName;
    }

    /// <summary>
    /// Inserts a video or sound file: checks Windows can play it (and takes the poster frame), asks before embedding
    /// a very big file, then embeds it in the deck (or links to it).
    /// </summary>
    public async Task InsertMediaFileAsync(string path, Point? at)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            _window.ShowError(ex);
            return;
        }
        var video = MediaTypes.IsVideo(path);
        var name = Path.GetFileName(path);

        _window.SetBusy(video ? "Checking the video…" : "Checking the sound…", name, 0);
        MediaProbeResult probe;
        try
        {
            probe = await MediaProbe.ProbeAsync(path, 0.5, TimeSpan.FromSeconds(15));
        }
        finally
        {
            _window.SetBusy(null);
        }
        if (!probe.Playable)
        {
            var choice = MessageDialog.Show(_window, video ? "Windows can't play this video" : "Windows can't play this sound", probe.Error ?? MediaProbe.CantPlayAdvice,
                "Insert anyway", close: "Cancel", icon: DialogIcon.Warning);
            if (choice != DialogChoice.Primary) return;
        }
        if (video && probe.Playable && !probe.HasVideo) video = false; // a sound in a video container

        // Very big files: linking keeps the presentation small.
        var link = false;
        if (MediaLimits.ShouldOfferLink(length))
        {
            var choice = MessageDialog.Show(_window, $"This file is {MediaLimits.Describe(length)}",
                "Embedding it makes the presentation file just as big. Linking keeps the presentation small, but the video must stay where it is (or next to the presentation) to play.",
                "Link to the file instead", "Embed it", "Cancel", DialogIcon.Question);
            if (choice == DialogChoice.Close) return;
            link = choice == DialogChoice.Primary;
        }

        string? media = null;
        if (!link)
        {
            _window.SetBusy("Adding the file…", name, 0);
            try
            {
                var store = Session.Media;
                var progress = new Action<double>(f => Dispatcher.BeginInvoke(new Action(() => _window.SetBusy("Adding the file…", name, f))));
                media = await Task.Run(() => store.AddFile(path, progress));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Add media " + path);
                _window.SetBusy(null);
                MessageDialog.Show(_window, "The file couldn't be added", ex.Message, "OK", icon: DialogIcon.Error);
                return;
            }
            _window.SetBusy(null);
        }

        var deck = Session.Deck;
        Element element;
        if (video)
        {
            string? poster = null;
            if (probe.Frame != null) poster = Session.Media.Add(MediaStore.EncodePng(probe.Frame), ".png");
            var w = probe.Width > 0 ? probe.Width : 1280;
            var h = probe.Height > 0 ? probe.Height : 720;
            var width = Math.Min(deck.Width * 0.62, deck.Height * 0.62 * w / Math.Max(1, h));
            var height = width * h / Math.Max(1, w);
            var center = at ?? new Point(deck.Width / 2, deck.Height / 2);
            element = new VideoElement
            {
                Media = media,
                LinkPath = link ? path : null,
                LinkAbsolute = link ? path : null,
                FileName = name,
                Poster = poster,
                PosterTime = 0.5,
                Length = probe.Duration,
                CornerRadius = Math.Round(deck.Theme.Radius * deck.Height / 1080),
                Bounds = new Rect(Math.Round(center.X - width / 2), Math.Round(center.Y - height / 2), Math.Round(width), Math.Round(height)),
            };
        }
        else
        {
            var size = Math.Round(deck.Height * 0.11);
            var m = SlideStyle.For(deck, slide).Margin;
            var center = at ?? new Point(deck.Width - m - size / 2, deck.Height - m - size / 2);
            element = new AudioElement
            {
                Media = media,
                LinkPath = link ? path : null,
                LinkAbsolute = link ? path : null,
                FileName = name,
                Length = probe.Duration,
                Bounds = new Rect(Math.Round(center.X - size / 2), Math.Round(center.Y - size / 2), size, size),
            };
        }
        var clip = (MediaClipElement)element;
        Session.EditSlide(slide, video ? "Add video" : "Add sound", s =>
        {
            s.Elements.Add(clip);
            // Like PowerPoint: it plays on the next click (clicking it plays it too).
            AnimationOps.SetStart(s, clip, MediaStart.OnClick);
        });
        _state.Select(new[] { element.Id });
        Canvas.Focus();
        if (link) _window.ShowMessage("Linked to the file. Keep it in the same place (or next to the presentation) so it plays.", 6);
    }

    /// <summary>Video and sound files dropped from Explorer, one after another.</summary>
    private async void InsertDroppedMedia(List<string> paths, Point at)
    {
        var offset = 0.0;
        foreach (var path in paths.Take(6))
        {
            await InsertMediaFileAsync(path, new Point(at.X + offset, at.Y + offset));
            offset += 32;
        }
    }

    // ------------------------------------------------------------------ options

    /// <summary>Changes the selected videos and sounds.</summary>
    public void EditMedia(string label, Action<MediaClipElement> change, string? mergeKey = null) =>
        EditSelected(label, list =>
        {
            foreach (var c in list.OfType<MediaClipElement>()) change(c);
        }, mergeKey);

    /// <summary>The trim dialog for a video or sound (it previews the cut).</summary>
    public void TrimMedia(MediaClipElement clip)
    {
        var path = Session.Media.ResolvePlayback(clip);
        if (path == null)
        {
            MessageDialog.Show(_window, "The file can't be found", clip.IsLinked ? "The linked file isn't there any more: " + (clip.LinkAbsolute ?? clip.LinkPath) : "The file is missing from the presentation.", "OK", icon: DialogIcon.Warning);
            return;
        }
        var result = TrimDialog.Edit(_window, clip, path, TrimDialog.Mode.Trim, clip is VideoElement { Poster: { } p } ? Session.Media.GetImage(p) : null);
        if (result == null) return;
        EditMedia("Trim", c =>
        {
            c.TrimStart = Math.Round(result.Start, 2);
            c.TrimEnd = result.End >= result.Length - 0.05 ? null : Math.Round(result.End, 2);
            if (result.Length > 0) c.Length = Math.Round(result.Length, 3);
        });
    }

    /// <summary>Chooses the poster frame of a video.</summary>
    public void ChoosePoster(VideoElement video)
    {
        var path = Session.Media.ResolvePlayback(video);
        if (path == null) return;
        var result = TrimDialog.Edit(_window, video, path, TrimDialog.Mode.Poster, video.Poster != null ? Session.Media.GetImage(video.Poster) : null);
        if (result?.Frame == null) return;
        var poster = Session.Media.Add(MediaStore.EncodePng(result.Frame), ".png");
        EditMedia("Poster frame", c =>
        {
            if (c is VideoElement v)
            {
                v.Poster = poster;
                v.PosterTime = Math.Round(result.Position, 2);
            }
        });
    }

    /// <summary>Puts a file that is only linked into the deck.</summary>
    public async void EmbedLinkedMedia(MediaClipElement clip)
    {
        var path = Session.Media.ResolveLink(clip);
        if (path == null)
        {
            MessageDialog.Show(_window, "The linked file can't be found", clip.LinkAbsolute ?? clip.LinkPath ?? "", "OK", icon: DialogIcon.Warning);
            return;
        }
        _window.SetBusy("Embedding the file…", Path.GetFileName(path), 0);
        string name;
        try
        {
            var store = Session.Media;
            name = await Task.Run(() => store.AddFile(path));
        }
        catch (Exception ex)
        {
            _window.SetBusy(null);
            _window.ShowError(ex);
            return;
        }
        _window.SetBusy(null);
        EditMedia("Embed", c =>
        {
            c.Media = name;
            c.LinkPath = null;
            c.LinkAbsolute = null;
        });
    }

    /// <summary>Keeps the file outside the deck (for very big videos): saves a copy to a chosen place and links to it.</summary>
    public void LinkEmbeddedMedia(MediaClipElement clip)
    {
        var item = Session.Media.Get(clip.Media);
        if (item == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save the video next to the presentation",
            FileName = clip.FileName ?? item.Name,
            Filter = "All files|*.*",
            OverwritePrompt = true,
        };
        if (DeckFolder != null) dialog.InitialDirectory = DeckFolder;
        if (dialog.ShowDialog(_window) != true) return;
        try
        {
            using (var input = item.OpenRead())
            using (var output = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write))
                input.CopyTo(output);
        }
        catch (Exception ex)
        {
            _window.ShowError(ex);
            return;
        }
        var file = dialog.FileName;
        EditMedia("Link to file", c =>
        {
            c.Media = null;
            c.LinkPath = file;
            c.LinkAbsolute = file;
        });
        _window.ShowMessage("The presentation now links to " + Path.GetFileName(file) + ". It gets smaller when you save.", 5);
    }

    /// <summary>Points a linked video or sound at its file again (moved or renamed).</summary>
    public void RelinkMedia(MediaClipElement clip)
    {
        var path = PickMediaFile("Find the file", clip is VideoElement ? VideoFilter : AudioFilter);
        if (path == null) return;
        EditMedia("Find file", c =>
        {
            if (c.Id != clip.Id) return;
            c.LinkPath = path;
            c.LinkAbsolute = path;
            c.FileName = Path.GetFileName(path);
        });
    }

    // ------------------------------------------------------------------ online video

    public async void InsertOnlineVideo()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var input = OnlineVideoDialog.Ask(_window);
        if (input == null) return;
        var info = OnlineVideos.Describe(input.Value.Url);
        if (info == null)
        {
            MessageDialog.Show(_window, "That isn't a web address", "Paste the address of the video page, like https://www.youtube.com/watch?v=… or https://vimeo.com/….", "OK", icon: DialogIcon.Warning);
            return;
        }
        string? thumbnail = null;
        var title = string.IsNullOrWhiteSpace(input.Value.Title) ? null : input.Value.Title!.Trim();
        if (input.Value.FetchPicture)
        {
            _window.SetBusy("Getting the video's picture…", info.Provider, 0);
            try
            {
                var fetched = await OnlineVideos.FetchThumbnailAsync(info);
                if (fetched.Bytes != null) thumbnail = Session.Media.Add(fetched.Bytes, ".jpg");
                title ??= fetched.Title;
            }
            finally
            {
                _window.SetBusy(null);
            }
        }
        var deck = Session.Deck;
        var width = Math.Round(deck.Width * 0.5);
        var height = Math.Round(width * 9 / 16);
        var element = new OnlineVideoElement
        {
            Url = info.Url,
            Provider = info.Provider,
            Title = title,
            Thumbnail = thumbnail,
            CornerRadius = Math.Round(deck.Theme.Radius * deck.Height / 1080),
        };
        element.Bounds = PlaceAtCenter(width, height);
        AddElements("Add online video", element);
        if (input.Value.FetchPicture && thumbnail == null) _window.ShowMessage("The video's picture couldn't be downloaded, so the card shows its title instead. It opens in your browser during the slide show.", 6);
    }
}
