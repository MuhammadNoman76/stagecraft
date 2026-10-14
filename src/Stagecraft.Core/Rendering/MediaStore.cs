using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// One file in a deck: a picture (its original bytes, saved as is in media/, and a decoded copy for drawing) or a
/// video / sound, which lives in a file of its own (see <see cref="MediaCache"/>) so big files are never held in memory.
/// </summary>
public sealed class MediaItem
{
    private BitmapSource? _image;
    private bool _failed;
    private byte[]? _data;
    private readonly long _fileLength;
    private readonly object _gate = new();

    public MediaItem(string name, byte[] data)
    {
        Name = name;
        _data = data;
    }

    /// <summary>A file-backed item (videos and sounds): <paramref name="filePath"/> holds the bytes and must not change.</summary>
    public MediaItem(string name, string filePath)
    {
        Name = name;
        FilePath = filePath;
        _fileLength = new FileInfo(filePath).Length;
    }

    public string Name { get; }

    /// <summary>Where the bytes are when they're kept in a file (videos and sounds), else null.</summary>
    public string? FilePath { get; }

    public long Length => _data?.LongLength ?? _fileLength;

    /// <summary>The bytes (read from the file for file-backed items: use <see cref="OpenRead"/> for big videos).</summary>
    public byte[] Data
    {
        get
        {
            lock (_gate)
            {
                return _data ??= File.ReadAllBytes(FilePath!);
            }
        }
    }

    public bool IsVideoOrSound => MediaTypes.IsMedia(Name);

    public Stream OpenRead()
    {
        if (_data != null) return new MemoryStream(_data, false);
        return new FileStream(FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
    }

    /// <summary>A file Windows can play: the backing file, or a copy of the bytes in the media cache.</summary>
    public string PlaybackPath()
    {
        if (FilePath != null) return FilePath;
        lock (_gate)
        {
            var path = MediaCache.PathFor(Name);
            var data = _data!;
            if (!File.Exists(path) || new FileInfo(path).Length != data.LongLength) MediaCache.WriteAtomically(path, s => s.Write(data, 0, data.Length));
            return path;
        }
    }

    /// <summary>Pixel size of the decoded picture (after EXIF rotation); 0 if it can't be decoded.</summary>
    public int PixelWidth => Image?.PixelWidth ?? 0;
    public int PixelHeight => Image?.PixelHeight ?? 0;

    /// <summary>Decoded (and turned upright) at most <see cref="MediaStore.MaxDecodeSize"/> pixels wide; null if unreadable.</summary>
    public BitmapSource? Image
    {
        get
        {
            lock (_gate)
            {
                if (_image != null || _failed) return _image;
                if (IsVideoOrSound)
                {
                    _failed = true;
                    return null;
                }
                _image = MediaStore.Decode(Data, MediaStore.MaxDecodeSize);
                _failed = _image == null;
                return _image;
            }
        }
    }
}

/// <summary>
/// The pictures, videos and sounds of a deck, stored by content: the file name is a hash of the bytes, so the same
/// file used twice is stored once.
/// </summary>
public sealed class MediaStore
{
    /// <summary>Pictures larger than this are drawn from a smaller decoded copy (the original is kept for saving).</summary>
    public const int MaxDecodeSize = 3200;

    private readonly Dictionary<string, MediaItem> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The deck's folder (linked videos and file links are relative to it); null for unsaved decks.</summary>
    public string? BaseFolder { get; set; }

    public IReadOnlyCollection<MediaItem> Items => _items.Values;

    public IEnumerable<string> Names => _items.Keys;

    public bool Contains(string name) => _items.ContainsKey(name);

    public MediaItem? Get(string? name) => name != null && _items.TryGetValue(name, out var item) ? item : null;

    public BitmapSource? GetImage(string? name) => Get(name)?.Image;

    public byte[]? GetBytes(string? name) => Get(name)?.Data;

    /// <summary>Adds a picture and returns its media name ("3fa2…c1.png"). The same bytes give the same name.</summary>
    public string Add(byte[] data, string? extension)
    {
        var ext = NormalizeExtension(extension, data);
        string hash;
        using (var sha = SHA256.Create()) hash = HashName(sha.ComputeHash(data));
        var name = hash + ext;
        if (_items.ContainsKey(name)) return name;
        if (MediaTypes.IsMedia(name))
        {
            // Videos and sounds always live in a file (playback needs one).
            var path = MediaCache.PathFor(name);
            if (!File.Exists(path) || new FileInfo(path).Length != data.LongLength) MediaCache.WriteAtomically(path, s => s.Write(data, 0, data.Length));
            _items[name] = new MediaItem(name, path);
        }
        else _items[name] = new MediaItem(name, data);
        return name;
    }

    /// <summary>
    /// Adds a video or sound file by copying it into the media cache (so the original can move or be deleted) and
    /// returns its media name. The file is read twice (hash, copy) and never held in memory.
    /// </summary>
    public string AddFile(string path, Action<double>? progress = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length == 0) ext = ".bin";
        string hash;
        using (var sha = SHA256.Create())
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
            hash = HashName(sha.ComputeHash(s));
        var name = hash + ext;
        if (_items.ContainsKey(name)) return name;
        var target = MediaCache.PathFor(name);
        var length = new FileInfo(path).Length;
        if (!File.Exists(target) || new FileInfo(target).Length != length)
        {
            MediaCache.WriteAtomically(target, output =>
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    done += read;
                    progress?.Invoke(done / (double)Math.Max(1, length));
                }
            });
        }
        _items[name] = new MediaItem(name, target);
        return name;
    }

    private static string HashName(byte[] hash)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 10; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Adds an item read from a file under its stored name (used when loading a deck).</summary>
    public void AddExisting(string name, byte[] data) => _items[name] = new MediaItem(name, data);

    /// <summary>Adds a file-backed item under its stored name (videos and sounds extracted while loading a deck).</summary>
    public void AddExistingFile(string name, string path) => _items[name] = new MediaItem(name, path);

    public void Remove(string name) => _items.Remove(name);

    public MediaStore Clone()
    {
        var copy = new MediaStore { BaseFolder = BaseFolder };
        foreach (var item in _items.Values) copy._items[item.Name] = item;
        return copy;
    }

    /// <summary>Takes over the pictures of another store that this one doesn't have (pasting between decks).</summary>
    public void Merge(MediaStore other)
    {
        foreach (var item in other._items.Values)
            if (!_items.ContainsKey(item.Name)) _items[item.Name] = item;
    }

    /// <summary>Takes over one item of another store (pasting a video between decks of the same Stagecraft).</summary>
    public void Adopt(MediaItem item)
    {
        if (!_items.ContainsKey(item.Name)) _items[item.Name] = item;
    }

    /// <summary>
    /// The file a video or sound element plays: the embedded copy, or the linked file (relative to the deck's folder
    /// first, then its last known full path). Null when it can't be found.
    /// </summary>
    public string? ResolvePlayback(MediaClipElement clip)
    {
        if (clip.Media != null) return Get(clip.Media)?.PlaybackPath();
        return ResolveLink(clip);
    }

    /// <summary>A linked clip's file on disk, or null when it isn't there.</summary>
    public string? ResolveLink(MediaClipElement clip)
    {
        foreach (var candidate in new[] { clip.LinkPath, clip.LinkAbsolute })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var full = Path.IsPathRooted(candidate) ? candidate! : BaseFolder != null ? Path.GetFullPath(Path.Combine(BaseFolder, candidate)) : null;
                if (full != null && File.Exists(full)) return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // not a usable path
            }
        }
        return null;
    }

    public static string NormalizeExtension(string? extension, byte[] data)
    {
        var sniffed = Sniff(data);
        if (sniffed != null) return sniffed;
        var ext = (extension ?? "").Trim().ToLowerInvariant();
        if (!ext.StartsWith(".", StringComparison.Ordinal)) ext = "." + ext;
        return ext is ".jpeg" or ".jpe" ? ".jpg" : ext.Length > 1 ? ext : ".bin";
    }

    /// <summary>The real file type from the first bytes.</summary>
    public static string? Sniff(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ".png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ".jpg";
        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F') return ".gif";
        if (d.Length >= 2 && d[0] == 'B' && d[1] == 'M') return ".bmp";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ".webp";
        if (d.Length >= 4 && ((d[0] == 'I' && d[1] == 'I' && d[2] == 42) || (d[0] == 'M' && d[1] == 'M' && d[3] == 42))) return ".tif";
        return null;
    }

    public static bool IsSupportedImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".jpe" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" or ".jfif" or ".ico" or ".heic" or ".avif";
    }

    /// <summary>Decodes a picture, at most <paramref name="maxSize"/> pixels on its longer side, turned upright (EXIF). Null if unreadable.</summary>
    public static BitmapSource? Decode(byte[] data, int maxSize)
    {
        try
        {
            int width, height;
            var rotation = 0;
            using (var probe = new MemoryStream(data, false))
            {
                var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                rotation = ExifRotation(frame);
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(data, false);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (Math.Max(width, height) > maxSize)
            {
                if (width >= height) image.DecodePixelWidth = maxSize;
                else image.DecodePixelHeight = maxSize;
            }
            image.EndInit();
            image.Freeze();

            BitmapSource result = image;
            if (rotation != 0)
            {
                var turned = new TransformedBitmap(image, new RotateTransform(rotation));
                turned.Freeze();
                result = turned;
            }
            if (Math.Abs(result.DpiX - 96) > 0.5 || Math.Abs(result.DpiY - 96) > 0.5)
            {
                // Normalise to 96 dpi so DrawImage sizes behave the same for every picture.
                var stride = result.PixelWidth * 4;
                var pixels = new byte[stride * result.PixelHeight];
                var converted = new FormatConvertedBitmap(result, PixelFormats.Pbgra32, null, 0);
                converted.CopyPixels(pixels, stride, 0);
                var normal = BitmapSource.Create(result.PixelWidth, result.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                normal.Freeze();
                result = normal;
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static int ExifRotation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta)
            {
                object? value = null;
                if (meta.ContainsQuery("/app1/ifd/{ushort=274}")) value = meta.GetQuery("/app1/ifd/{ushort=274}");
                else if (meta.ContainsQuery("System.Photo.Orientation")) value = meta.GetQuery("System.Photo.Orientation");
                var orientation = value == null ? 1 : Convert.ToInt32(value);
                return orientation switch
                {
                    3 => 180,
                    6 => 90,
                    8 => 270,
                    _ => 0,
                };
            }
        }
        catch
        {
            // No or unreadable metadata.
        }
        return 0;
    }

    /// <summary>Encodes a bitmap as PNG.</summary>
    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static byte[] EncodeJpeg(BitmapSource bitmap, int quality = 90)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        // JPEG has no transparency: flatten on white.
        var flat = Flatten(bitmap, Colors.White);
        encoder.Frames.Add(BitmapFrame.Create(flat));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource Flatten(BitmapSource bitmap, Color background)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            dc.DrawRectangle(new SolidColorBrush(background), null, rect);
            dc.DrawImage(bitmap, rect);
        }
        var target = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
