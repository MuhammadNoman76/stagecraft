using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Export;

public enum ImageFormat { Png, Jpeg }

/// <summary>PNG / JPG export of one slide or all of them (numbered files in a folder).</summary>
public static class ImageExporter
{
    public static string Extension(ImageFormat format) => format == ImageFormat.Png ? ".png" : ".jpg";

    public static byte[] Encode(Deck deck, MediaStore media, Slide slide, ImageFormat format, int pixelWidth)
    {
        var bitmap = SlideRenderer.Render(slide, new RenderContext(deck, media), pixelWidth);
        return format == ImageFormat.Png ? MediaStore.EncodePng(bitmap) : MediaStore.EncodeJpeg(bitmap, 92);
    }

    public static void ExportSlide(Deck deck, MediaStore media, Slide slide, string path, ImageFormat format, int pixelWidth)
    {
        var bytes = Encode(deck, media, slide, format, pixelWidth);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes "&lt;baseName&gt;-01.png", "-02.png", … into <paramref name="folder"/>. Returns the files written.</summary>
    public static List<string> ExportAll(Deck deck, MediaStore media, string folder, string baseName, ImageFormat format, int pixelWidth,
        bool includeHidden = false, Action<int, int>? progress = null)
    {
        Directory.CreateDirectory(folder);
        var slides = deck.Slides.Where(s => includeHidden || !s.Hidden).ToList();
        var digits = Math.Max(2, slides.Count.ToString().Length);
        var safe = SafeName(baseName);
        var files = new List<string>();
        for (var i = 0; i < slides.Count; i++)
        {
            var path = Path.Combine(folder, $"{safe}-{(i + 1).ToString().PadLeft(digits, '0')}{Extension(format)}");
            ExportSlide(deck, media, slides[i], path, format, pixelWidth);
            files.Add(path);
            progress?.Invoke(i + 1, slides.Count);
        }
        return files;
    }

    public static string SafeName(string name)
    {
        var s = string.IsNullOrWhiteSpace(name) ? "Slide" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 80 ? s.Substring(0, 80) : s;
    }
}
