using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Pictures from drag-and-drop and the clipboard: files from Explorer, PNG data copied in a browser, pictures
/// dragged from a browser (virtual files), and plain bitmaps. Links to web images are not downloaded
/// (Stagecraft doesn't go online except to check for updates).
/// </summary>
public static class ClipboardImages
{
    public static bool HasImages(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                return files.Any(MediaStore.IsSupportedImageFile);
            return data.GetDataPresent("PNG") || data.GetDataPresent("FileContents") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib);
        }
        catch
        {
            return false;
        }
    }

    public static List<(byte[] Data, string Extension)> Read(IDataObject data)
    {
        var result = new List<(byte[], string)>();
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files.Where(MediaStore.IsSupportedImageFile).Take(20))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(f);
                        if (MediaStore.Decode(bytes, 64) != null) result.Add((bytes, Path.GetExtension(f)));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Read dropped picture " + f);
                    }
                }
                if (result.Count > 0) return result;
            }
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                var bytes = png.ToArray();
                if (MediaStore.Decode(bytes, 64) != null)
                {
                    result.Add((bytes, ".png"));
                    return result;
                }
            }
            if (data.GetDataPresent("FileContents"))
            {
                // A picture dragged out of a browser arrives as a virtual file.
                if (data.GetData("FileContents") is MemoryStream contents)
                {
                    var bytes = contents.ToArray();
                    if (MediaStore.Sniff(bytes) != null && MediaStore.Decode(bytes, 64) != null)
                    {
                        result.Add((bytes, MediaStore.Sniff(bytes)!));
                        return result;
                    }
                }
            }
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                result.Add((MediaStore.EncodePng(bitmap), ".png"));
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException or OutOfMemoryException)
        {
            Log.Error(ex, "Read pictures from data object");
        }
        return result;
    }
}
