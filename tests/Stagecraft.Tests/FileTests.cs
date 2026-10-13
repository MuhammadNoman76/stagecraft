using System.IO.Compression;
using System.Text;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

public class FileTests
{
    [Fact]
    public void SaveLoad_RoundTrip_KeepsEverything() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        foreach (var sample in DemoDecks.All)
        {
            var doc = sample.Create();
            var path = dir.File(sample.Id + DeckFile.Extension);
            DeckFile.Save(path, doc.Deck, doc.Media);
            var saved = Decks.Json(doc.Deck);

            var loaded = DeckFile.Load(path);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(saved, Decks.Json(loaded.Deck));
            foreach (var name in DeckFile.ReferencedMedia(doc.Deck).Distinct())
                Assert.Equal(doc.Media.GetBytes(name), loaded.Media.GetBytes(name));
        }
    });

    [Fact]
    public void SaveLoad_Unicode() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("swiss"));
        const string text = "Café — 東京 · Ελληνικά · مرحبا · 🎉 “quotes” – ü ß";
        deck.Title = text;
        deck.Slides[0].FindPlaceholder<TextElement>("title")!.Text.Content = RichText.FromPlain(text);
        deck.Slides[0].Notes = "Notes: " + text + "\nSecond line";
        var path = dir.File("Präsentation 東京" + DeckFile.Extension);
        DeckFile.Save(path, deck, new MediaStore());
        var loaded = DeckFile.Load(path);
        Assert.Equal(text, loaded.Deck.Title);
        Assert.Equal(text, loaded.Deck.Slides[0].FindPlaceholder<TextElement>("title")!.Text.Content.PlainText);
        Assert.Equal(deck.Slides[0].Notes, loaded.Deck.Slides[0].Notes);
    });

    [Fact]
    public void Save_StoresMedia_Once_AndOnlyWhatIsUsed() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var deck = DeckOperations.NewDeck();
        var media = new MediaStore();
        var used = media.Add(Decks.Png(), ".png");
        var again = media.Add(Decks.Png(), ".png");
        var unused = media.Add(Decks.Png(32, 32, 0, 0, 255), ".png");
        Assert.Equal(used, again); // same bytes, same name
        deck.Slides[0].Elements.Add(new ImageElement { Media = used, Width = 400, Height = 300 });
        deck.Slides[0].Background = Fill.Image(used);
        var path = dir.File("media" + DeckFile.Extension);
        DeckFile.Save(path, deck, media);

        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.Equal("mimetype", zip.Entries[0].FullName);
            using (var reader = new StreamReader(zip.Entries[0].Open())) Assert.Equal(DeckFile.MimeType, reader.ReadToEnd());
            var mediaEntries = zip.Entries.Where(e => e.FullName.StartsWith("media/", StringComparison.Ordinal)).Select(e => e.FullName).ToList();
            Assert.Equal(new[] { "media/" + used }, mediaEntries);
            Assert.DoesNotContain("media/" + unused, mediaEntries);
            Assert.NotNull(zip.GetEntry(DeckFile.DeckEntry));
        }
        var loaded = DeckFile.Load(path);
        Assert.NotNull(loaded.Media.GetImage(used));
        Assert.Equal(64, loaded.Media.Get(used)!.PixelWidth);
    });

    [Fact]
    public void Save_IsAtomic_NoTempFilesLeft_AndOverwrites() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var deck = DeckOperations.NewDeck();
        var path = dir.File("deck" + DeckFile.Extension);
        DeckFile.Save(path, deck, new MediaStore());
        deck.Title = "Second save";
        DeckFile.Save(path, deck, new MediaStore());
        Assert.Equal("Second save", DeckFile.Load(path).Deck.Title);
        Assert.Equal(new[] { path }, Directory.GetFiles(dir.Path));
    });

    [Fact]
    public void Load_RejectsGarbage_WithFriendlyErrors() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var random = dir.File("random" + DeckFile.Extension);
        var bytes = new byte[5000];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(random, bytes);
        Assert.Equal(DeckFileProblem.NotADeck, Assert.Throws<DeckFileException>(() => DeckFile.Load(random)).Problem);

        var empty = dir.File("empty" + DeckFile.Extension);
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.Throws<DeckFileException>(() => DeckFile.Load(empty));

        var noDeck = dir.File("nodeck" + DeckFile.Extension);
        using (var zip = ZipFile.Open(noDeck, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("hello.txt").Open())) w.Write("hi");
        Assert.Equal(DeckFileProblem.NotADeck, Assert.Throws<DeckFileException>(() => DeckFile.Load(noDeck)).Problem);

        var badJson = dir.File("badjson" + DeckFile.Extension);
        using (var zip = ZipFile.Open(badJson, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry(DeckFile.DeckEntry).Open())) w.Write("{ \"slides\": [ { \"elements\": [ ");
        Assert.Equal(DeckFileProblem.Damaged, Assert.Throws<DeckFileException>(() => DeckFile.Load(badJson)).Problem);

        Assert.Equal(DeckFileProblem.NotFound, Assert.Throws<DeckFileException>(() => DeckFile.Load(dir.File("missing.stagecraft"))).Problem);
    });

    [Fact]
    public void Load_TruncatedFile_Fails_Cleanly() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var doc = DemoDecks.AuroraKickoff();
        var path = dir.File("full" + DeckFile.Extension);
        DeckFile.Save(path, doc.Deck, doc.Media);
        var bytes = File.ReadAllBytes(path);
        var cut = dir.File("cut" + DeckFile.Extension);
        File.WriteAllBytes(cut, bytes.Take(bytes.Length / 2).ToArray());
        var ex = Assert.Throws<DeckFileException>(() => DeckFile.Load(cut));
        Assert.True(ex.Problem is DeckFileProblem.NotADeck or DeckFileProblem.Damaged);
    });

    [Fact]
    public void Load_NewerSchema_IsRefused() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck();
        var json = DeckJson.Serialize(deck).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        Assert.Contains("99", json);
        var ex = Assert.Throws<DeckFileException>(() => DeckFile.ParseDeck(json, out _));
        Assert.Equal(DeckFileProblem.TooNew, ex.Problem);
    });

    [Fact]
    public void Load_SchemaVersionIsWritten() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var path = dir.File("v" + DeckFile.Extension);
        DeckFile.Save(path, DeckOperations.NewDeck(), new MediaStore());
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry(DeckFile.DeckEntry)!.Open(), Encoding.UTF8);
        var json = reader.ReadToEnd();
        Assert.Contains("\"schemaVersion\": " + Deck.CurrentSchemaVersion, json);
    });

    [Fact]
    public void Load_RepairsImpossibleValues_AndReportsMissingPictures() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck();
        deck.Width = -5;
        deck.Slides[0].Elements.Add(new ImageElement { Media = "doesnotexist.png", Width = double.NaN, Height = 100 });
        var json = DeckJson.Serialize(deck);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(zip.CreateEntry(DeckFile.DeckEntry).Open(), new UTF8Encoding(false))) w.Write(json);
        ms.Position = 0;
        var loaded = DeckFile.Load(ms);
        Assert.Equal(1920, loaded.Deck.Width);
        Assert.Contains(loaded.Warnings, w => w.Contains("slide size"));
        Assert.Contains(loaded.Warnings, w => w.Contains("picture"));
        Assert.All(loaded.Deck.Slides[0].Elements, e => Assert.False(double.IsNaN(e.Width)));
    });

    [Fact]
    public void Load_UnknownElementType_IsSkipped_NotFatal() => Sta.Run(() =>
    {
        var deck = DeckOperations.NewDeck();
        var json = DeckJson.Serialize(deck);
        var marker = "\"elements\": [";
        var i = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i > 0);
        json = json.Insert(i + marker.Length, "{ \"type\": \"hologram\", \"id\": \"x1\", \"x\": 1 },");
        var parsed = DeckFile.ParseDeck(json, out var warnings);
        Assert.Single(parsed.Slides);
        Assert.NotEmpty(warnings);
    });

    [Fact]
    public void Recovery_FindsOrphans_AndDiscards() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(Path.Combine(dir.Path, "Recovery"));
        var deck = DeckOperations.NewDeck();
        deck.Title = "Unsaved work";
        var media = new MediaStore();
        var pic = media.Add(Decks.Png(), ".png");
        deck.Slides[0].Elements.Add(new ImageElement { Media = pic, Width = 100, Height = 100 });

        // Written by this (running) process: not offered.
        Assert.True(store.Save(deck, media, "live", "Live deck", null));
        Assert.Empty(store.FindOrphans());

        // Written by a process that has gone away: offered.
        Assert.True(store.Save(deck, media, "crashed", "Crashed deck", @"C:\Decks\plan.stagecraft", processId: 0x7FFFFFF0, processStartUtc: DateTime.UtcNow.AddHours(-1)));
        var orphans = store.FindOrphans();
        var entry = Assert.Single(orphans);
        Assert.Equal("crashed", entry.Key);
        Assert.Equal("Crashed deck", entry.Title);
        Assert.Equal(@"C:\Decks\plan.stagecraft", entry.OriginalPath);

        var recovered = DeckFile.Load(entry.DeckPath);
        Assert.Equal("Unsaved work", recovered.Deck.Title);
        Assert.NotNull(recovered.Media.GetImage(pic));

        store.Discard("crashed");
        store.Discard("live");
        Assert.Empty(store.FindOrphans());
        Assert.Empty(Directory.GetFiles(store.Folder));
    });

    [Fact]
    public void Recovery_SaveAsync_CopiesBeforeWriting() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var store = new RecoveryStore(dir.Path);
        var deck = DeckOperations.NewDeck();
        deck.Title = "Before";
        var task = store.SaveAsync(deck, new MediaStore(), "k", "t", null);
        deck.Title = "After"; // edits made while the copy is written don't leak into it
        task.Wait();
        Assert.Equal("Before", DeckFile.Load(store.DeckPathOf("k")).Deck.Title);
    });

    [Fact]
    public void Cover_IsReadable_FromSavedFile() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        var doc = DemoDecks.QuarterlyReview();
        var ctx = new RenderContext(doc.Deck, doc.Media);
        var cover = MediaStore.EncodePng(SlideRenderer.Render(doc.Deck.Slides[0], ctx, 480));
        var path = dir.File("cover" + DeckFile.Extension);
        DeckFile.Save(path, doc.Deck, doc.Media, new DeckPreviews { CoverPng = cover });
        Assert.Equal(cover, DeckFile.ReadCover(path));
    });
}
