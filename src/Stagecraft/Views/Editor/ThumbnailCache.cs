using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Slide thumbnails, rendered on demand in the background (idle priority, a few per tick) and kept until the
/// slide's revision changes. Decks with hundreds of slides stay responsive: only visible thumbnails are asked for.
/// </summary>
public sealed class ThumbnailCache
{
    private readonly DeckSession _session;
    private readonly Dictionary<string, (long Revision, int Width, BitmapSource Image)> _cache = new();
    private readonly List<(Slide Slide, int Width, Action<BitmapSource> Done)> _queue = new();
    private readonly DispatcherTimer _timer;

    public ThumbnailCache(DeckSession session, Dispatcher dispatcher)
    {
        _session = session;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1) };
        _timer.Tick += (_, _) => Process();
    }

    public BitmapSource? TryGet(Slide slide, int width)
    {
        if (_cache.TryGetValue(slide.Id, out var entry) && entry.Revision == slide.Revision && entry.Width >= width) return entry.Image;
        return null;
    }

    /// <summary>The thumbnail (stale or missing ones are rendered soon and passed to <paramref name="done"/>).</summary>
    public BitmapSource? Get(Slide slide, int width, Action<BitmapSource> done)
    {
        if (_cache.TryGetValue(slide.Id, out var entry) && entry.Width >= width)
        {
            if (entry.Revision == slide.Revision) return entry.Image;
            Enqueue(slide, width, done);
            return entry.Image; // show the old one until the new one is ready
        }
        Enqueue(slide, width, done);
        return null;
    }

    /// <summary>Renders right away (used when saving previews into the file).</summary>
    public BitmapSource Render(Slide slide, int width)
    {
        if (TryGet(slide, width) is { } cached) return cached;
        var image = SlideRenderer.Render(slide, new RenderContext(_session.Deck, _session.Media), width);
        _cache[slide.Id] = (slide.Revision, width, image);
        return image;
    }

    public void Clear()
    {
        _cache.Clear();
        _queue.Clear();
    }

    private void Enqueue(Slide slide, int width, Action<BitmapSource> done)
    {
        _queue.RemoveAll(q => q.Slide.Id == slide.Id && q.Width <= width);
        _queue.Add((slide, width, done));
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Process()
    {
        var started = DateTime.UtcNow;
        while (_queue.Count > 0 && (DateTime.UtcNow - started).TotalMilliseconds < 24)
        {
            // Newest requests first: they are what's on screen now.
            var (slide, width, done) = _queue[_queue.Count - 1];
            _queue.RemoveAt(_queue.Count - 1);
            if (!_session.Deck.Slides.Contains(slide)) continue;
            BitmapSource image;
            if (_cache.TryGetValue(slide.Id, out var entry) && entry.Revision == slide.Revision && entry.Width >= width)
            {
                image = entry.Image;
            }
            else
            {
                try
                {
                    image = SlideRenderer.Render(slide, new RenderContext(_session.Deck, _session.Media), width);
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "Thumbnail");
                    continue;
                }
                _cache[slide.Id] = (slide.Revision, width, image);
            }
            done(image);
        }
        if (_queue.Count == 0) _timer.Stop();
    }
}
