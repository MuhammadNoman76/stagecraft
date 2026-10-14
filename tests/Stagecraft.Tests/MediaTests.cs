using System.IO.Compression;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Tests;

public class MediaTests
{
    private static Deck DeckWith(params Element[] elements)
    {
        var deck = DeckOperations.NewDeck();
        deck.Slides[0].Elements.AddRange(elements);
        return deck;
    }

    [Fact]
    public void AddFile_CopiesIntoTheCache_NamedByContent()
    {
        var media = new MediaStore();
        var name = media.AddFile(TestMedia.Clip);
        Assert.EndsWith(".mp4", name);
        Assert.Equal(name, media.AddFile(TestMedia.Clip)); // same bytes, same name
        var item = media.Get(name)!;
        Assert.NotNull(item.FilePath);
        Assert.StartsWith(MediaCache.Root, item.FilePath!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new FileInfo(TestMedia.Clip).Length, item.Length);
        Assert.Equal(File.ReadAllBytes(TestMedia.Clip), File.ReadAllBytes(item.PlaybackPath()));
        Assert.Null(item.Image); // not a picture

        // Bytes added in memory with a media extension also end up in a file (playback needs one).
        var fromBytes = media.Add(File.ReadAllBytes(TestMedia.Mp3), ".mp3");
        Assert.NotNull(media.Get(fromBytes)!.FilePath);
    }

    [Fact]
    public void Embedded_VideoAndSound_SaveAndLoad() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var media = new MediaStore();
        var clip = media.AddFile(TestMedia.Clip);
        var tone = media.AddFile(TestMedia.Mp3);
        var poster = media.Add(Decks.Png(160, 90), ".png");
        var video = new VideoElement { Media = clip, Poster = poster, FileName = "test-clip.mp4", X = 100, Y = 100, Width = 640, Height = 360, TrimStart = 0.25, TrimEnd = 1.5, Volume = 0.4, Loop = true, FadeIn = 0.5, FullScreen = true, CornerRadius = 24 };
        var audio = new AudioElement { Media = tone, FileName = "test-tone.mp3", X = 1600, Y = 900, Width = 96, Height = 96, AcrossSlides = true, StopAfterSlides = 3, HideIcon = true };
        var online = new OnlineVideoElement { Url = "https://www.youtube.com/watch?v=abc", Title = "Talk", Provider = "YouTube", X = 900, Y = 200, Width = 640, Height = 360 };
        var deck = DeckWith(video, audio, online);
        var play = EffectCatalog.Create(EffectClass.Media, EffectKind.Play, video.Id);
        play.Start = EffectStart.AfterPrevious;
        deck.Slides[0].Animations.Add(play);
        var path = dir.File("media" + DeckFile.Extension);
        DeckFile.Save(path, deck, media);

        using (var zip = ZipFile.OpenRead(path))
        {
            var entry = zip.GetEntry("media/" + clip)!;
            // Stored as is (.NET Framework wraps it in uncompressed deflate blocks, a few bytes larger).
            Assert.InRange(entry.CompressedLength, entry.Length, entry.Length + entry.Length / 100 + 64);
            Assert.NotNull(zip.GetEntry("media/" + tone));
            Assert.NotNull(zip.GetEntry("media/" + poster));
        }

        var loaded = DeckFile.Load(path);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(Decks.Json(deck), Decks.Json(loaded.Deck));
        var v = loaded.Deck.Slides[0].Elements.OfType<VideoElement>().Single();
        Assert.Equal(File.ReadAllBytes(TestMedia.Clip), File.ReadAllBytes(loaded.Media.ResolvePlayback(v)!));
        Assert.NotNull(loaded.Media.Get(clip)!.FilePath);
        Assert.Equal(1.25, v.PlayLength!.Value, 6);
        var a = loaded.Deck.Slides[0].Elements.OfType<AudioElement>().Single();
        Assert.True(a.AcrossSlides);
        Assert.Equal(3, a.StopAfterSlides);
        Assert.Equal(File.ReadAllBytes(TestMedia.Mp3), File.ReadAllBytes(loaded.Media.ResolvePlayback(a)!));
    });

    [Fact]
    public void Linked_Video_IsStoredRelative_AndFollowsSaveAs() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var videos = Directory.CreateDirectory(Path.Combine(dir.Path, "Videos")).FullName;
        var decks = Directory.CreateDirectory(Path.Combine(dir.Path, "Decks")).FullName;
        var file = Path.Combine(videos, "talk.mp4");
        File.Copy(TestMedia.Clip, file);
        var video = new VideoElement { LinkPath = file, LinkAbsolute = file, FileName = "talk.mp4", Width = 640, Height = 360 };
        var deck = DeckWith(video);
        var media = new MediaStore();
        var path = Path.Combine(decks, "linked" + DeckFile.Extension);
        DeckFile.Save(path, deck, media);
        Assert.Equal(Path.Combine("..", "Videos", "talk.mp4"), video.LinkPath);
        Assert.Equal(decks, media.BaseFolder);
        using (var zip = ZipFile.OpenRead(path)) Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("media/", StringComparison.Ordinal));

        var loaded = DeckFile.Load(path);
        Assert.Empty(loaded.Warnings);
        var v = (VideoElement)loaded.Deck.Slides[0].Elements.Last();
        Assert.True(v.IsLinked);
        Assert.Equal(file, loaded.Media.ResolvePlayback(v));

        // Save as one folder deeper: the relative path is worked out again.
        var deeper = Directory.CreateDirectory(Path.Combine(dir.Path, "Archive", "2026")).FullName;
        var copy = Path.Combine(deeper, "copy" + DeckFile.Extension);
        DeckFile.Save(copy, loaded.Deck, loaded.Media);
        Assert.Equal(Path.Combine("..", "..", "Videos", "talk.mp4"), v.LinkPath);
        Assert.Equal(file, DeckFile.Load(copy).Media.ResolvePlayback((VideoElement)DeckFile.Load(copy).Deck.Slides[0].Elements.Last()));

        // A deck file copied elsewhere by hand: the relative path breaks, the full path still finds the video.
        var moved = Path.Combine(dir.Path, "moved" + DeckFile.Extension);
        File.Copy(path, moved);
        var fromMoved = DeckFile.Load(moved);
        Assert.Equal(file, fromMoved.Media.ResolvePlayback((VideoElement)fromMoved.Deck.Slides[0].Elements.Last()));

        // And when the video is gone, opening says so.
        File.Delete(file);
        Assert.Contains(DeckFile.Load(path).Warnings, w => w.Contains("linked"));
    });

    [Fact]
    public void LargeFile_Linked_KeepsTheDeckSmall() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var big = dir.File("big.mp4");
        using (var s = new FileStream(big, FileMode.CreateNew)) s.SetLength(64L * 1000 * 1000);
        Assert.False(MediaLimits.ShouldOfferLink(new FileInfo(big).Length));
        Assert.True(MediaLimits.ShouldOfferLink(MediaLimits.EmbedWarningBytes + 1));
        Assert.Equal("201 MB", MediaLimits.Describe(201_000_000));
        var deck = DeckWith(new VideoElement { LinkPath = big, LinkAbsolute = big, Width = 640, Height = 360 });
        var path = dir.File("small" + DeckFile.Extension);
        DeckFile.Save(path, deck, new MediaStore());
        Assert.True(new FileInfo(path).Length < 200_000);
    });

    [Fact]
    public void Verify_RefusesATruncatedFile() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var media = new MediaStore();
        var clip = media.AddFile(TestMedia.Clip);
        var deck = DeckWith(new VideoElement { Media = clip, Width = 640, Height = 360 });
        using var ms = new MemoryStream();
        DeckFile.Write(ms, deck, media);
        ms.Position = 0;
        DeckFile.Verify(ms);
        var bytes = ms.ToArray();
        // A write that stopped part-way (full disk, crash) is refused before it can replace the old file.
        foreach (var keep in new[] { bytes.Length - 40, bytes.Length / 2, 200 })
        {
            using var cut = new MemoryStream(bytes.Take(keep).ToArray());
            Assert.ThrowsAny<Exception>(() => DeckFile.Verify(cut));
        }
    });

    [Fact]
    public void Probe_ReadsLengthSizeAndPoster_AndRefusesJunk() => Sta.Run(() =>
    {
        var result = MediaProbe.Probe(TestMedia.Clip, 1.0);
        Assert.True(result.Playable, result.Error);
        Assert.True(result.HasVideo);
        Assert.Equal(320, result.Width);
        Assert.Equal(180, result.Height);
        Assert.InRange(result.Duration ?? 0, 1.8, 2.3);
        Assert.NotNull(result.Frame);
        Assert.False(MediaProbe.IsBlank(result.Frame!));

        var sound = MediaProbe.Probe(TestMedia.Mp3);
        Assert.True(sound.Playable, sound.Error);
        Assert.False(sound.HasVideo);
        Assert.InRange(sound.Duration ?? 0, 0.8, 1.3);

        var wav = MediaProbe.Probe(TestMedia.Wav);
        Assert.True(wav.Playable, wav.Error);
        Assert.InRange(wav.Duration ?? 0, 0.3, 0.8);

        using var dir = new TempDir();
        // The sample decks' sound (M4A / AAC) and video (MP4, 1280 × 720).
        foreach (var (name, video) in new[] { ("soft-pad.m4a", false), ("aurora-loop.mp4", true) })
        {
            var path = dir.File(name);
            using (var resource = typeof(Stagecraft.Core.Samples.DemoDecks).Assembly.GetManifestResourceStream("Stagecraft.SampleMedia." + name)!)
            using (var file = File.Create(path))
                resource.CopyTo(file);
            var probe = MediaProbe.Probe(path, 2.0);
            Assert.True(probe.Playable, name + ": " + probe.Error);
            Assert.Equal(video, probe.HasVideo);
        }

        var junk = dir.File("junk.mp4");
        var bytes = new byte[20000];
        new Random(3).NextBytes(bytes);
        File.WriteAllBytes(junk, bytes);
        var bad = MediaProbe.Probe(junk, timeout: TimeSpan.FromSeconds(8));
        Assert.False(bad.Playable);
        Assert.Contains("MP4", bad.Error);
    });

    [Fact]
    public void Video_DrawsItsPoster_AndHiddenSoundsStayOutOfTheShow() => Sta.Run(() =>
    {
        var media = new MediaStore();
        var poster = media.Add(Decks.Png(160, 90, 255, 0, 0), ".png");
        var video = new VideoElement { Media = media.AddFile(TestMedia.Clip), Poster = poster, X = 0, Y = 0, Width = 1920, Height = 1080 };
        var audio = new AudioElement { Media = media.AddFile(TestMedia.Mp3), X = 860, Y = 440, Width = 200, Height = 200, HideIcon = true, Color = "#00FF00" };
        var deck = DeckWith(video, audio);
        deck.Slides[0].Elements.RemoveAll(e => e is TextElement);
        var show = new RenderContext(deck, media);
        var bitmap = SlideRenderer.Render(deck.Slides[0], show, 192);
        var center = Pixel(bitmap, 89, 54);
        Assert.True(center.R > 200 && center.G < 60, "the red poster fills the slide and the hidden speaker isn't drawn");
        var editing = SlideRenderer.Render(deck.Slides[0], new RenderContext(deck, media, editing: true), 192);
        Assert.True(Pixel(editing, 89, 54).G > 150, "the editor shows the hidden speaker");
    });

    private static System.Windows.Media.Color Pixel(System.Windows.Media.Imaging.BitmapSource b, int x, int y)
    {
        var px = new byte[4];
        b.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
        return System.Windows.Media.Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    [Fact]
    public void ReferencedMedia_IncludesVideosPostersSoundsAndThumbnails()
    {
        var deck = DeckWith(
            new VideoElement { Media = "v.mp4", Poster = "p.png" },
            new AudioElement { Media = "a.mp3" },
            new OnlineVideoElement { Thumbnail = "t.jpg", Url = "https://vimeo.com/1" },
            new VideoElement { LinkPath = "x.mp4" });
        var names = DeckFile.ReferencedMedia(deck).ToList();
        Assert.Contains("v.mp4", names);
        Assert.Contains("p.png", names);
        Assert.Contains("a.mp3", names);
        Assert.Contains("t.jpg", names);
        Assert.DoesNotContain("x.mp4", names);
    }

    [Fact]
    public void Repair_ClampsMediaValues()
    {
        var deck = DeckWith(new VideoElement { Media = "v.mp4", Volume = 7, TrimStart = -2, TrimEnd = -1, FadeIn = double.NaN, Length = 0 });
        var json = DeckJson.Serialize(deck);
        var parsed = Sta.Run(() => DeckFile.ParseDeck(json, out _));
        var v = parsed.Slides[0].Elements.OfType<VideoElement>().Single();
        Assert.Equal(1, v.Volume);
        Assert.Equal(0, v.TrimStart);
        Assert.Null(v.TrimEnd);
        Assert.Equal(0, v.FadeIn);
        Assert.Null(v.Length);
    }
}
