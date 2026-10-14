using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Core.IO;

public enum DeckFileProblem { NotFound, NotADeck, Damaged, TooNew, AccessDenied }

/// <summary>A deck file that can't be opened, with a message for people.</summary>
public sealed class DeckFileException : Exception
{
    public DeckFileException(DeckFileProblem problem, string message, Exception? inner = null) : base(message, inner)
    {
        Problem = problem;
    }

    public DeckFileProblem Problem { get; }
}

/// <summary>A deck with its pictures, as read from or written to a .stagecraft file.</summary>
public sealed class DeckDocument
{
    public DeckDocument(Deck deck, MediaStore media)
    {
        Deck = deck;
        Media = media;
    }

    public Deck Deck { get; }
    public MediaStore Media { get; }

    /// <summary>Things that were repaired or left out while loading (missing pictures, unknown values).</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>Previews stored with a deck (the start screen shows the cover).</summary>
public sealed class DeckPreviews
{
    public byte[]? CoverPng { get; set; }
    /// <summary>Small JPEG per slide, in slide order.</summary>
    public List<byte[]> SlideJpegs { get; set; } = new();
}

/// <summary>
/// The .stagecraft format: a ZIP container with
/// <c>mimetype</c> (stored first, "application/vnd.stagecraft.deck+zip"),
/// <c>deck.json</c> (the deck, with <c>schemaVersion</c>),
/// <c>media/</c> (pictures, named by a hash of their bytes) and
/// <c>thumbs/</c> (cover.png and one small JPEG per slide).
/// Saving writes a temporary file next to the target and swaps it in only when it's complete.
/// </summary>
public static class DeckFile
{
    public const string Extension = ".stagecraft";
    public const string MimeType = "application/vnd.stagecraft.deck+zip";
    public const string DeckEntry = "deck.json";
    public const string CoverEntry = "thumbs/cover.png";

    // ------------------------------------------------------------------ load

    public static DeckDocument Load(string path)
    {
        if (!File.Exists(path)) throw new DeckFileException(DeckFileProblem.NotFound, "The file doesn't exist any more: " + path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Load(stream, Path.GetDirectoryName(Path.GetFullPath(path)));
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DeckFileException(DeckFileProblem.AccessDenied, "Stagecraft isn't allowed to read this file.", ex);
        }
        catch (IOException ex) when (ex is not FileNotFoundException)
        {
            throw new DeckFileException(DeckFileProblem.AccessDenied, "The file can't be read: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads a deck. Pictures are kept in memory; videos and sounds are copied into the <see cref="MediaCache"/>.
    /// <paramref name="baseFolder"/> is the deck's folder (linked media and file links are relative to it).
    /// </summary>
    public static DeckDocument Load(Stream stream, string? baseFolder = null)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new DeckFileException(DeckFileProblem.NotADeck, "This isn't a Stagecraft presentation, or the file is damaged.", ex);
        }

        using (zip)
        {
            var entry = zip.GetEntry(DeckEntry);
            if (entry == null) throw new DeckFileException(DeckFileProblem.NotADeck, "This file doesn't contain a Stagecraft presentation (deck.json is missing).");

            string json;
            try
            {
                using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
                json = reader.ReadToEnd();
            }
            catch (InvalidDataException ex)
            {
                throw new DeckFileException(DeckFileProblem.Damaged, "The presentation inside the file is damaged and can't be read.", ex);
            }

            var deck = ParseDeck(json, out var warnings);
            var media = new MediaStore { BaseFolder = baseFolder };
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith("media/", StringComparison.Ordinal) || e.Length == 0) continue;
                var name = e.FullName.Substring("media/".Length);
                if (!IsSafeMediaName(name)) continue;
                try
                {
                    if (MediaTypes.IsMedia(name))
                    {
                        // Videos and sounds: into a file of their own (playback needs one; big files stay out of memory).
                        var path = MediaCache.PathFor(name);
                        if (!File.Exists(path) || new FileInfo(path).Length != e.Length)
                            MediaCache.WriteAtomically(path, output =>
                            {
                                using var s = e.Open();
                                s.CopyTo(output, 1 << 16);
                            });
                        media.AddExistingFile(name, path);
                        continue;
                    }
                    using (var s = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        media.AddExisting(name, ms.ToArray());
                    }
                }
                catch (InvalidDataException)
                {
                    warnings.Add(MediaTypes.IsMedia(name) ? $"The video or sound {name} is damaged and was left out." : $"The picture {name} is damaged and was left out.");
                }
            }

            var doc = new DeckDocument(deck, media);
            doc.Warnings.AddRange(warnings);
            var missing = ReferencedMedia(deck).Where(m => !media.Contains(m)).Distinct().ToList();
            var missingMedia = missing.Count(MediaTypes.IsMedia);
            var missingPictures = missing.Count - missingMedia;
            if (missingPictures > 0)
                doc.Warnings.Add(missingPictures == 1 ? "One picture is missing from the file." : $"{missingPictures} pictures are missing from the file.");
            if (missingMedia > 0)
                doc.Warnings.Add(missingMedia == 1 ? "One video or sound is missing from the file." : $"{missingMedia} videos or sounds are missing from the file.");
            var unlinked = deck.Slides.SelectMany(s => s.Elements).OfType<MediaClipElement>().Count(c => c.Media == null && c.IsLinked && media.ResolveLink(c) == null);
            if (unlinked > 0)
                doc.Warnings.Add(unlinked == 1 ? "A linked video or sound can't be found. Choose its file again in the panel." : $"{unlinked} linked videos or sounds can't be found.");
            return doc;
        }
    }

    /// <summary>A media entry name that is a plain file name (no folders, no tricks).</summary>
    public static bool IsSafeMediaName(string name) =>
        name.Length > 0 && name.Length < 200 && name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0 && name != "." && name != "..";

    /// <summary>
    /// Checks a written deck without keeping anything: deck.json must parse and every entry must read to the end
    /// (a truncated or damaged file fails here). Used before a save replaces the old file.
    /// </summary>
    public static void Verify(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new DeckFileException(DeckFileProblem.Damaged, "The written file is damaged.", ex);
        }
        using (zip)
        {
            var entry = zip.GetEntry(DeckEntry) ?? throw new DeckFileException(DeckFileProblem.Damaged, "The written file has no deck.json.");
            using (var reader = new StreamReader(entry.Open(), new UTF8Encoding(false))) ParseDeck(reader.ReadToEnd(), out _);
            var buffer = new byte[1 << 16];
            foreach (var e in zip.Entries)
            {
                if (e == entry) continue;
                using var s = e.Open();
                long total = 0;
                int read;
                while ((read = s.Read(buffer, 0, buffer.Length)) > 0) total += read;
                if (total != e.Length) throw new DeckFileException(DeckFileProblem.Damaged, "The written file is incomplete (" + e.FullName + ").");
            }
        }
    }

    /// <summary>Parses deck.json: checks the schema version, migrates older files and repairs what it can.</summary>
    public static Deck ParseDeck(string json, out List<string> warnings)
    {
        warnings = new List<string>();
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new DeckFileException(DeckFileProblem.Damaged, "The presentation inside the file is damaged (it isn't a JSON object).");
        }
        catch (JsonException ex)
        {
            throw new DeckFileException(DeckFileProblem.Damaged, "The presentation inside the file is damaged and can't be read.", ex);
        }

        var version = 0;
        if (root.TryGetPropertyValue("schemaVersion", out var v) && v is JsonValue jv && jv.TryGetValue<int>(out var parsed)) version = parsed;
        if (version > Deck.CurrentSchemaVersion)
            throw new DeckFileException(DeckFileProblem.TooNew,
                "This presentation was made with a newer version of Stagecraft. Update Stagecraft to open it.");

        root = Migrations.Upgrade(root, version, warnings);

        Deck? deck;
        try
        {
            deck = root.Deserialize<Deck>(DeckJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            // Leave out the elements that can't be read (a newer kind of element, a damaged value) and try again.
            try
            {
                var dropped = DropUnreadableElements(root);
                if (dropped == 0) throw;
                deck = root.Deserialize<Deck>(DeckJson.Options);
                warnings.Add(dropped == 1 ? "1 item on a slide couldn't be read and was left out." : $"{dropped} items on slides couldn't be read and were left out.");
            }
            catch (Exception inner) when (inner is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            {
                throw new DeckFileException(DeckFileProblem.Damaged, "The presentation inside the file is damaged and can't be read.", ex);
            }
        }
        if (deck == null) throw new DeckFileException(DeckFileProblem.Damaged, "The presentation inside the file is empty.");
        Repair(deck, warnings);
        return deck;
    }

    private static int DropUnreadableElements(JsonObject root)
    {
        var dropped = 0;
        if (root["slides"] is not JsonArray slides) return 0;
        foreach (var slide in slides.OfType<JsonObject>())
        {
            if (slide["elements"] is not JsonArray elements) continue;
            for (var i = elements.Count - 1; i >= 0; i--)
            {
                var ok = false;
                try
                {
                    ok = elements[i] is JsonObject && elements[i].Deserialize<Element>(DeckJson.Options) != null;
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
                {
                    ok = false;
                }
                if (ok) continue;
                elements.RemoveAt(i);
                dropped++;
            }
        }
        return dropped;
    }

    /// <summary>Fixes impossible values so a hand-edited or partly damaged deck still opens.</summary>
    public static void Repair(Deck deck, List<string> warnings)
    {
        deck.SchemaVersion = Deck.CurrentSchemaVersion;
        deck.Title ??= "Untitled presentation";
        deck.Info ??= new DeckInfo();
        if (!IsSane(deck.Width) || !IsSane(deck.Height) || deck.Width < 100 || deck.Height < 100 || deck.Width > 20000 || deck.Height > 20000)
        {
            deck.Width = Deck.DefaultWidth;
            deck.Height = Deck.DefaultHeight;
            warnings.Add("The slide size was invalid and was reset to 16:9.");
        }
        if (deck.Theme == null || deck.Theme.Main == null || deck.Theme.Emphasis == null || deck.Theme.Fonts == null)
        {
            deck.Theme = ThemeCatalog.Find(deck.Theme?.Id)?.Create(deck.Theme?.Variant) ?? ThemeCatalog.Default.Create();
            warnings.Add("The theme was incomplete and was restored.");
        }
        deck.Theme.Accents ??= new List<string>();
        while (deck.Theme.Accents.Count < 5) deck.Theme.Accents.Add("#6C5CE7");
        deck.Theme.EmphasisLayouts ??= new List<string>();
        deck.Slides ??= new List<Slide>();
        deck.Slides.RemoveAll(s => s == null);

        var slideIds = new HashSet<string>();
        foreach (var slide in deck.Slides)
        {
            if (string.IsNullOrEmpty(slide.Id) || !slideIds.Add(slide.Id))
            {
                slide.Id = Ids.New();
                slideIds.Add(slide.Id);
            }
            slide.Layout ??= "blank";
            slide.Notes ??= "";
            slide.Transition ??= new Transition();
            slide.Elements ??= new List<Element>();
            slide.Elements.RemoveAll(e => e == null);
            var elementIds = new HashSet<string>();
            foreach (var e in slide.Elements)
            {
                if (string.IsNullOrEmpty(e.Id) || !elementIds.Add(e.Id))
                {
                    e.Id = Ids.New();
                    elementIds.Add(e.Id);
                }
                if (!IsSane(e.X)) e.X = 0;
                if (!IsSane(e.Y)) e.Y = 0;
                if (!IsSane(e.Width) || e.Width < 0) e.Width = 100;
                if (!IsSane(e.Height) || e.Height < 0) e.Height = 100;
                if (!IsSane(e.Rotation)) e.Rotation = 0;
                if (!IsSane(e.Opacity)) e.Opacity = 1;
                e.Opacity = MathEx.Clamp(e.Opacity, 0, 1);
                switch (e)
                {
                    case TextElement t:
                        t.Text ??= new TextBody();
                        RepairText(t.Text);
                        break;
                    case ShapeElement s:
                        s.Fill ??= Fill.None;
                        if (s.Text != null) RepairText(s.Text);
                        break;
                    case ChartElement c:
                        c.Categories ??= new List<string>();
                        c.Series ??= new List<ChartSeries>();
                        c.Series.RemoveAll(x => x == null);
                        foreach (var s in c.Series) s.Values ??= new List<double>();
                        c.ValueFormat ??= "#,0.##";
                        break;
                    case TableElement tb:
                        tb.Cells ??= new List<List<string>>();
                        tb.Cells.RemoveAll(r => r == null);
                        tb.HeaderColor ??= "accent1";
                        break;
                    case IconElement ic:
                        ic.Icon ??= "star";
                        ic.Color ??= "accent1";
                        break;
                    case MediaClipElement clip:
                        if (!IsSane(clip.Volume)) clip.Volume = 1;
                        clip.Volume = MathEx.Clamp(clip.Volume, 0, 1);
                        if (!IsSane(clip.TrimStart) || clip.TrimStart < 0) clip.TrimStart = 0;
                        if (clip.TrimEnd is { } end && (!IsSane(end) || end <= clip.TrimStart)) clip.TrimEnd = null;
                        if (clip.Length is { } length && (!IsSane(length) || length <= 0)) clip.Length = null;
                        if (!IsSane(clip.FadeIn) || clip.FadeIn < 0) clip.FadeIn = 0;
                        if (!IsSane(clip.FadeOut) || clip.FadeOut < 0) clip.FadeOut = 0;
                        if (clip is AudioElement audio)
                        {
                            audio.Color ??= "accent1";
                            if (audio.StopAfterSlides is { } n && n < 1) audio.StopAfterSlides = null;
                        }
                        if (clip is VideoElement video && (!IsSane(video.PosterTime) || video.PosterTime < 0)) video.PosterTime = 0;
                        break;
                    case OnlineVideoElement online:
                        online.Url ??= "";
                        break;
                }
                if (e.Link != null) RepairLink(e.Link);
                if (e.Shadow != null) RepairShadow(e.Shadow);
            }
            slide.Animations ??= new List<AnimationEffect>();
            var before = slide.Animations.Count;
            AnimationRules.Repair(slide);
            if (slide.Animations.Count < before)
                warnings.Add(before - slide.Animations.Count == 1 ? "An animation without its element was removed." : $"{before - slide.Animations.Count} animations without their elements were removed.");
        }
        deck.Footer ??= new HeaderFooter();
        deck.Footer.DateFormat ??= "long";
        deck.Guides ??= new List<GuideLine>();
        deck.Guides.RemoveAll(g => g == null || !IsSane(g.Position));
    }

    private static void RepairText(TextBody body)
    {
        body.Content ??= new RichText();
        body.Content.Paragraphs ??= new List<Paragraph>();
        body.Content.Paragraphs.RemoveAll(p => p == null);
        foreach (var p in body.Content.Paragraphs)
        {
            p.Runs ??= new List<TextRun>();
            p.Runs.RemoveAll(r => r == null);
            foreach (var r in p.Runs)
            {
                r.Text ??= "";
                if (r.Link != null) RepairLink(r.Link);
            }
        }
        if (body.Shadow != null) RepairShadow(body.Shadow);
    }

    private static void RepairLink(Hyperlink link)
    {
        link.Address ??= "";
        if (link.Address.Length > 4096) link.Address = link.Address.Substring(0, 4096);
    }

    private static void RepairShadow(Shadow shadow)
    {
        shadow.Color ??= "#000000@30";
        if (!IsSane(shadow.Blur) || shadow.Blur < 0) shadow.Blur = 0;
        shadow.Blur = Math.Min(shadow.Blur, 400);
        if (!IsSane(shadow.Distance)) shadow.Distance = 0;
        shadow.Distance = MathEx.Clamp(shadow.Distance, -400, 400);
        if (!IsSane(shadow.Angle)) shadow.Angle = 90;
    }

    private static bool IsSane(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>Every media name the deck uses (pictures, image fills, backgrounds, videos and their posters, sounds).</summary>
    public static IEnumerable<string> ReferencedMedia(Deck deck)
    {
        foreach (var slide in deck.Slides)
        {
            if (slide.Background?.Media is { } bg) yield return bg;
            foreach (var e in slide.Elements)
                foreach (var name in MediaOf(e))
                    yield return name;
        }
        if (deck.Theme?.Main?.Background?.Media is { } t1) yield return t1;
        if (deck.Theme?.Emphasis?.Background?.Media is { } t2) yield return t2;
    }

    /// <summary>The media names one element uses.</summary>
    public static IEnumerable<string> MediaOf(Element e)
    {
        switch (e)
        {
            case ImageElement { Media: { } m }:
                yield return m;
                break;
            case ShapeElement { Fill.Media: { } f }:
                yield return f;
                break;
            case TextElement { Fill.Media: { } tf }:
                yield return tf;
                break;
            case VideoElement v:
                if (v.Media != null) yield return v.Media;
                if (v.Poster != null) yield return v.Poster;
                break;
            case AudioElement { Media: { } a }:
                yield return a;
                break;
            case OnlineVideoElement { Thumbnail: { } thumb }:
                yield return thumb;
                break;
        }
    }

    /// <summary>The cover thumbnail of a deck file (for the start screen), or null.</summary>
    public static byte[]? ReadCover(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(CoverEntry);
            if (entry == null || entry.Length > 8_000_000) return null;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ save

    /// <summary>Writes the deck atomically: a temporary file next to <paramref name="path"/> replaces it only when complete.</summary>
    public static void Save(string path, Deck deck, MediaStore media, DeckPreviews? previews = null)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(dir);
        // Linked videos and file links are stored relative to the deck's (new) folder when possible.
        RebaseLinks(deck, media, dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Ids.New() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                Write(stream, deck, media, previews);
                stream.Flush(true);
            }
            // Read it back before it replaces anything: a file that can't be loaded must never overwrite a good one.
            using (var check = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
                Verify(check);

            if (File.Exists(full))
            {
                try
                {
                    File.Replace(temp, full, null, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    Compat.MoveOverwrite(temp, full);
                }
            }
            else
            {
                File.Move(temp, full);
            }
            media.BaseFolder = dir;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Re-expresses linked media and file links relative to <paramref name="folder"/> (the folder the deck is being
    /// saved into), keeping each link's full path as well. Links that can't be found keep what they had.
    /// </summary>
    public static void RebaseLinks(Deck deck, MediaStore media, string folder)
    {
        foreach (var slide in deck.Slides)
        {
            foreach (var e in slide.Elements)
            {
                if (e is MediaClipElement { Media: null } clip && clip.IsLinked)
                {
                    var file = media.ResolveLink(clip);
                    if (file != null)
                    {
                        clip.LinkAbsolute = file;
                        clip.LinkPath = Compat.RelativePath(folder, file);
                    }
                }
                if (e.Link is { Kind: LinkKind.File } link) RebaseFileLink(link, media.BaseFolder, folder);
                foreach (var body in TextBodies(e))
                foreach (var p in body.Content.Paragraphs)
                foreach (var r in p.Runs)
                    if (r.Link is { Kind: LinkKind.File } runLink) RebaseFileLink(runLink, media.BaseFolder, folder);
            }
        }
    }

    private static void RebaseFileLink(Hyperlink link, string? oldFolder, string newFolder)
    {
        var resolved = LinkSafety.ResolveFile(link.Address, oldFolder);
        if (resolved == null || !File.Exists(resolved)) return;
        link.Address = Compat.RelativePath(newFolder, resolved);
    }

    /// <summary>The text bodies of an element (text boxes and shapes with text).</summary>
    public static IEnumerable<TextBody> TextBodies(Element e)
    {
        switch (e)
        {
            case TextElement t:
                yield return t.Text;
                break;
            case ShapeElement { Text: { } b }:
                yield return b;
                break;
        }
    }

    public static void Write(Stream stream, Deck deck, MediaStore media, DeckPreviews? previews = null)
    {
        deck.SchemaVersion = Deck.CurrentSchemaVersion;
        deck.Info ??= new DeckInfo();
        deck.Info.Modified = DateTime.UtcNow;
        deck.Info.SavedWith = typeof(DeckFile).Assembly.GetName().Version?.ToString(3);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8);
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mime.Open())
        {
            var bytes = Encoding.ASCII.GetBytes(MimeType);
            s.Write(bytes, 0, bytes.Length);
        }

        var deckEntry = zip.CreateEntry(DeckEntry, CompressionLevel.Optimal);
        using (var s = deckEntry.Open())
        {
            var bytes = new UTF8Encoding(false).GetBytes(DeckJson.Serialize(deck));
            s.Write(bytes, 0, bytes.Length);
        }

        var used = new HashSet<string>(ReferencedMedia(deck), StringComparer.OrdinalIgnoreCase);
        foreach (var item in media.Items)
        {
            if (!used.Contains(item.Name)) continue; // media no longer used aren't saved
            // Pictures, videos and sounds are compressed already.
            var stored = MediaTypes.IsMedia(item.Name) || item.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         item.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || item.Name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                         item.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            var e = zip.CreateEntry("media/" + item.Name, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var s = e.Open();
            using var input = item.OpenRead();
            input.CopyTo(s, 1 << 16);
        }

        if (previews?.CoverPng != null)
        {
            var e = zip.CreateEntry(CoverEntry, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(previews.CoverPng, 0, previews.CoverPng.Length);
        }
        if (previews != null)
        {
            for (var i = 0; i < previews.SlideJpegs.Count; i++)
            {
                var data = previews.SlideJpegs[i];
                if (data == null || data.Length == 0) continue;
                var e = zip.CreateEntry($"thumbs/{i + 1:000}.jpg", CompressionLevel.NoCompression);
                using var s = e.Open();
                s.Write(data, 0, data.Length);
            }
        }
    }
}

/// <summary>Upgrades deck.json from older schema versions, one step at a time.</summary>
public static class Migrations
{
    public static JsonObject Upgrade(JsonObject root, int fromVersion, List<string> warnings)
    {
        var version = fromVersion;
        // Version 0: files without "schemaVersion" (written before the format was versioned) have the version 1 shape.
        if (version < 1)
        {
            root["schemaVersion"] = 1;
            version = 1;
        }
        // Version 1 → 2 (Stagecraft 1.1): each element's "build" becomes an entrance effect in its slide's "animations".
        if (version == 1)
        {
            BuildsToAnimations(root);
            root["schemaVersion"] = 2;
            version = 2;
        }
        return root;
    }

    /// <summary>
    /// 1.0 builds: an element appeared on click number "step" (elements with the same step together, each with its own
    /// delay after the click). In 1.1 the slide has an ordered list of effects: per step, the first element starts
    /// "On click" and the others "With previous", keeping their delays, so they play exactly as before.
    /// </summary>
    internal static void BuildsToAnimations(JsonObject root)
    {
        if (Get(root, "slides") is not JsonArray slides) return;
        foreach (var slide in slides.OfType<JsonObject>())
        {
            if (Get(slide, "elements") is not JsonArray elements) continue;
            var builds = new List<(int Step, int Index, string Target, JsonObject Build)>();
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i] is not JsonObject element) continue;
                var key = Key(element, "build");
                if (key == null) continue;
                var build = element[key] as JsonObject;
                element.Remove(key);
                if (build == null) continue;
                var effect = (Text(build, "effect") ?? "fade").ToLowerInvariant();
                if (effect == "none") continue;
                var id = Text(element, "id");
                if (string.IsNullOrEmpty(id))
                {
                    id = Ids.New();
                    element["id"] = id;
                }
                builds.Add(((int)Math.Round(Number(build, "step") ?? 1), i, id!, build));
            }
            if (builds.Count == 0) continue;

            var animations = Get(slide, "animations") as JsonArray ?? new JsonArray();
            int? lastStep = null;
            foreach (var (step, _, target, build) in builds.OrderBy(b => b.Step).ThenBy(b => b.Index))
            {
                var effect = (Text(build, "effect") ?? "fade").ToLowerInvariant();
                var delay = Math.Max(0, Number(build, "delay") ?? 0);
                var duration = Math.Max(0.05, Number(build, "duration") ?? 0.5);
                var item = new JsonObject
                {
                    ["id"] = Ids.New(),
                    ["target"] = target,
                    ["class"] = "entrance",
                    ["start"] = lastStep == step ? "withPrevious" : "onClick",
                    ["delay"] = Math.Round(delay, 3),
                };
                switch (effect)
                {
                    case "appear":
                        item["effect"] = "appear";
                        item["duration"] = 0;
                        break;
                    case "rise":
                        item["effect"] = "rise";
                        item["duration"] = duration;
                        break;
                    case "scale":
                        // 1.0's "Scale up" grew from 82 %: a Zoom starting at that size.
                        item["effect"] = "zoom";
                        item["duration"] = duration;
                        item["amount"] = 82;
                        break;
                    case "wipe":
                        item["effect"] = "wipe";
                        item["direction"] = "left";
                        item["duration"] = duration;
                        break;
                    default:
                        item["effect"] = "fade";
                        item["duration"] = duration;
                        break;
                }
                animations.Add(item);
                lastStep = step;
            }
            slide["animations"] = animations;
        }
    }

    private static string? Key(JsonObject o, string name) => o.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Get(JsonObject o, string name) => Key(o, name) is { } k ? o[k] : null;

    private static string? Text(JsonObject o, string name) =>
        Get(o, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Number(JsonObject o, string name)
    {
        if (Get(o, name) is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }
}
