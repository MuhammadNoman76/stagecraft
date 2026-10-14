using System.Windows;
using System.Windows.Controls;
using Stagecraft.Core.Model;
using Stagecraft.Views.Show;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Videos and sounds on the canvas: a slim playback bar appears while the pointer is on one, and plays it in place
/// (trimmed, with its fades) without starting the slide show.
/// </summary>
public sealed partial class SlideCanvas
{
    private MediaHost? _mediaHost;
    private MediaControlBar? _mediaBar;
    private MediaClipElement? _mediaBarClip;
    private readonly MediaPlayback _editorPlayback = new(sound: true);

    /// <summary>Screenshots: the playback bar of a video or sound, as when the pointer is over it.</summary>
    internal void HoverForCapture(Element e) => UpdateMediaHover(e, ToView(e.Center));

    /// <summary>The pointer is over <paramref name="hit"/> (or nothing): shows or hides the playback bar.</summary>
    private void UpdateMediaHover(Element? hit, Point view)
    {
        if (_state?.CurrentSlide == null || IsEditingText || _mode != DragMode.None)
        {
            HideMediaBar();
            return;
        }
        if (hit is MediaClipElement clip)
        {
            ShowMediaBar(clip);
            return;
        }
        if (_mediaBar != null && _mediaBar.IsMouseOver) return;
        if (_mediaBarClip != null && Near(_mediaBarClip, view)) return;
        HideMediaBar();
    }

    private bool Near(Element e, Point view)
    {
        var r = ToView(e.RotatedBounds);
        r.Inflate(4, 56);
        return r.Contains(view);
    }

    private void ShowMediaBar(MediaClipElement clip)
    {
        if (_mediaBar == null)
        {
            _mediaBar = new MediaControlBar();
            _mediaBar.Command += OnMediaBarCommand;
            _viewport.Children.Add(_mediaBar);
        }
        if (_mediaBarClip?.Id != clip.Id || _mediaHost == null)
        {
            StopMediaPreview();
            _mediaBarClip = clip;
            _mediaHost = CreateHost(clip);
            _mediaBar.Host = _mediaHost;
        }
        PlaceMediaBar();
        _mediaBar.Visibility = Visibility.Visible;
        _mediaBar.Refresh();
    }

    private void PlaceMediaBar()
    {
        if (_mediaBar == null || _mediaBarClip == null) return;
        var r = ToView(_mediaBarClip.RotatedBounds);
        var width = Math.Max(220, Math.Min(460, r.Width - 16));
        _mediaBar.Width = width;
        Canvas.SetLeft(_mediaBar, r.X + r.Width / 2 - width / 2);
        Canvas.SetTop(_mediaBar, _mediaBarClip is AudioElement ? r.Bottom + 8 : r.Bottom - 50);
    }

    private void HideMediaBar()
    {
        if (_mediaBar == null) return;
        _mediaBar.Visibility = Visibility.Collapsed;
        // A playing preview keeps playing until another element is chosen or the slide changes.
        if (_mediaHost == null || !_mediaHost.IsPlaying) StopMediaPreview();
    }

    /// <summary>A player over the element, under the selection overlay (in slide coordinates, like the visuals).</summary>
    private MediaHost CreateHost(MediaClipElement clip)
    {
        var ctx = _state!.RenderContext;
        var style = ctx.StyleFor(_state.CurrentSlide!);
        MediaHost host = clip is VideoElement video
            ? new VideoHost(video, ctx, style, _editorPlayback, editing: true)
            : new AudioHost((AudioElement)clip, ctx, style, _editorPlayback);
        var index = _visuals.TryGetValue(clip.Id, out var v) ? _stage.Children.IndexOf(v) + 1 : _stage.Children.Count;
        _stage.Children.Insert(index, host);
        return host;
    }

    private void OnMediaBarCommand(MediaHost host, MediaAction action, double? position)
    {
        if (_mediaHost == null) return;
        if (_mediaHost.Error != null && Window.GetWindow(this) is MainWindow window) window.ShowMessage(_mediaHost.Error, 8);
        _mediaHost.Run(action, position);
        _mediaBar?.Refresh();
    }

    /// <summary>Stops the in-place preview (the slide changed, the element moved or was deleted).</summary>
    public void StopMediaPreview()
    {
        if (_mediaHost != null)
        {
            _mediaHost.Stop();
            _mediaHost.Release();
            _stage.Children.Remove(_mediaHost);
            _mediaHost = null;
        }
        if (_mediaBar != null)
        {
            _mediaBar.Host = null;
            _mediaBar.Visibility = Visibility.Collapsed;
        }
        _mediaBarClip = null;
    }
}
