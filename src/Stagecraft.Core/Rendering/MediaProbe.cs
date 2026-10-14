using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Stagecraft.Core.Rendering;

/// <summary>What Windows could tell about a video or sound file.</summary>
public sealed class MediaProbeResult
{
    /// <summary>Windows can play it.</summary>
    public bool Playable { get; init; }

    /// <summary>Why it can't be played (for people), when it can't.</summary>
    public string? Error { get; init; }

    /// <summary>Seconds, when known.</summary>
    public double? Duration { get; init; }

    public bool HasVideo { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>A frame of the video (the poster), when one could be taken.</summary>
    public BitmapSource? Frame { get; init; }
}

/// <summary>
/// Opens a video or sound with WPF's MediaPlayer (Windows Media Foundation) without playing it out loud: checks that
/// Windows can play it, reads its length and picture size, and takes a frame for the poster. Needs a thread with a
/// running WPF dispatcher (the UI thread).
/// </summary>
public static class MediaProbe
{
    /// <summary>The message for files Windows can't play.</summary>
    public const string CantPlayAdvice =
        "Windows can't play this file. MP4 with H.264 video and AAC sound plays everywhere: convert the file to that (for example with the free HandBrake) and insert it again. On Windows \"N\" editions, install the Media Feature Pack from Microsoft first.";

    public static Task<MediaProbeResult> ProbeAsync(string path, double frameAt = 0.5, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<MediaProbeResult>();
        MediaPlayer player;
        try
        {
            // Scrubbing only once a video stream is known: with it on, sound-only MP4 / M4A files never open.
            player = new MediaPlayer { IsMuted = true, Volume = 0 };
        }
        catch (Exception ex)
        {
            // Windows without Media Player components ("N" editions without the Media Feature Pack).
            tcs.SetResult(new MediaProbeResult { Error = CantPlayAdvice + " (" + ex.Message + ")" });
            return tcs.Task;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        var deadline = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = timeout ?? TimeSpan.FromSeconds(12) };
        var finished = false;

        void Finish(MediaProbeResult result)
        {
            if (finished) return;
            finished = true;
            deadline.Stop();
            try
            {
                player.Close();
            }
            catch
            {
                // closing anyway
            }
            tcs.TrySetResult(result);
        }

        deadline.Tick += (_, _) => Finish(new MediaProbeResult { Error = "The file took too long to open. " + CantPlayAdvice });
        player.MediaFailed += (_, e) => Finish(new MediaProbeResult { Error = CantPlayAdvice + (e.ErrorException != null ? " (" + e.ErrorException.Message + ")" : "") });
        player.MediaOpened += (_, _) =>
        {
            double? duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : null;
            var hasVideo = player.HasVideo && player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0;
            if (!hasVideo)
            {
                Finish(new MediaProbeResult { Playable = true, Duration = duration });
                return;
            }
            var w = player.NaturalVideoWidth;
            var h = player.NaturalVideoHeight;
            player.ScrubbingEnabled = true;
            var at = duration is { } d ? Math.Max(0, Math.Min(frameAt, Math.Max(0, d - 0.05))) : Math.Max(0, frameAt);
            // Paused at the frame; with scrubbing on, the picture updates after the seek. Try a few times until it's not black.
            player.Play();
            player.Pause();
            player.Position = TimeSpan.FromSeconds(at);
            var attempts = 0;
            var grab = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
            grab.Tick += (_, _) =>
            {
                attempts++;
                var frame = Grab(player, w, h);
                if (frame != null && (!IsBlank(frame) || attempts >= 8))
                {
                    grab.Stop();
                    Finish(new MediaProbeResult { Playable = true, Duration = duration, HasVideo = true, Width = w, Height = h, Frame = frame });
                }
                else if (attempts >= 12)
                {
                    grab.Stop();
                    Finish(new MediaProbeResult { Playable = true, Duration = duration, HasVideo = true, Width = w, Height = h });
                }
            };
            grab.Start();
        };
        try
        {
            deadline.Start();
            player.Open(new Uri(System.IO.Path.GetFullPath(path)));
        }
        catch (Exception ex)
        {
            Finish(new MediaProbeResult { Error = CantPlayAdvice + " (" + ex.Message + ")" });
        }
        return tcs.Task;
    }

    /// <summary>The current picture of a player, at most 1920 pixels wide.</summary>
    public static BitmapSource? Grab(MediaPlayer player, int width, int height)
    {
        try
        {
            if (width <= 0 || height <= 0) return null;
            var k = Math.Min(1.0, 1920.0 / width);
            var w = Math.Max(1, (int)Math.Round(width * k));
            var h = Math.Max(1, (int)Math.Round(height * k));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawVideo(player, new Rect(0, 0, w, h));
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>All (nearly) black or transparent: the frame hasn't arrived yet.</summary>
    public static bool IsBlank(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        long sum = 0;
        var samples = 0;
        for (var i = 0; i < pixels.Length; i += 4 * 61)
        {
            sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
            samples++;
        }
        return samples == 0 || sum / (double)samples < 4;
    }

    /// <summary>
    /// Runs <see cref="ProbeAsync"/> on the calling thread and pumps its dispatcher until the answer is in
    /// (for code without a message loop: command-line tools and tests).
    /// </summary>
    public static MediaProbeResult Probe(string path, double frameAt = 0.5, TimeSpan? timeout = null)
    {
        var task = ProbeAsync(path, frameAt, timeout);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        return task.Result;
    }
}
