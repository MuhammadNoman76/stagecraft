using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stagecraft.Core.Rendering;

/// <summary>One picture in a deck: its original bytes (saved as is in media/) and a decoded copy for drawing.</summary>
public sealed class MediaItem
{
    private BitmapSource? _image;
    private bool _failed;
    private readonly object _gate = new();

    public MediaItem(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }

    public string Name { get; }
    public byte[] Data { get; }

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
                _image = MediaStore.Decode(Data, MediaStore.MaxDecodeSize);
                _failed = _image == null;
                return _image;
            }
        }
    }
}

/// <summary>
/// The pictures of a deck, stored by content: the file name is a hash of the bytes, so the same picture used
/// twice is stored once.
/// </summary>
public sealed class MediaStore
{
    /// <summary>Pictures larger than this are drawn from a smaller decoded copy (the original is kept for saving).</summary>
    public const int MaxDecodeSize = 3200;

    private readonly Dictionary<string, MediaItem> _items = new(StringComparer.OrdinalIgnoreCase);

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
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(data);
            var sb = new StringBuilder();
            for (var i = 0; i < 10; i++) sb.Append(bytes[i].ToString("x2"));
            hash = sb.ToString();
        }
        var name = hash + ext;
        if (!_items.ContainsKey(name)) _items[name] = new MediaItem(name, data);
        return name;
    }

    /// <summary>Adds an item read from a file under its stored name (used when loading a deck).</summary>
    public void AddExisting(string name, byte[] data) => _items[name] = new MediaItem(name, data);

    public void Remove(string name) => _items.Remove(name);

    public MediaStore Clone()
    {
        var copy = new MediaStore();
        foreach (var item in _items.Values) copy._items[item.Name] = item;
        return copy;
    }

    /// <summary>Takes over the pictures of another store that this one doesn't have (pasting between decks).</summary>
    public void Merge(MediaStore other)
    {
        foreach (var item in other._items.Values)
            if (!_items.ContainsKey(item.Name)) _items[item.Name] = item;
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
