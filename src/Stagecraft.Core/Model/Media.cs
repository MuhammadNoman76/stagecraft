using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>
/// A video or a sound on a slide. The file is embedded in the deck (<see cref="Media"/>, in media/ like pictures) or
/// linked (<see cref="LinkPath"/>: relative to the deck's folder when possible, with the last known full path as a
/// fallback). Playback uses Windows Media Foundation through WPF.
/// </summary>
public abstract class MediaClipElement : Element
{
    /// <summary>The embedded file in the deck's media folder (null when the file is linked).</summary>
    public string? Media { get; set; }

    /// <summary>A linked file: relative to the deck's folder when it was saved, otherwise a full path.</summary>
    public string? LinkPath { get; set; }

    /// <summary>A linked file's last known full path (used when the relative path doesn't resolve).</summary>
    public string? LinkAbsolute { get; set; }

    /// <summary>The original file name (shown in the editor and used for exports).</summary>
    public string? FileName { get; set; }

    /// <summary>Length of the whole file in seconds, when known.</summary>
    public double? Length { get; set; }

    /// <summary>Seconds cut from the start.</summary>
    public double TrimStart { get; set; }

    /// <summary>Where playback stops, in seconds from the start of the file (null: the end).</summary>
    public double? TrimEnd { get; set; }

    /// <summary>0..1.</summary>
    public double Volume { get; set; } = 1;

    /// <summary>Starts again from the trim start until stopped.</summary>
    public bool Loop { get; set; }

    /// <summary>Seconds of fade in (sound and picture).</summary>
    public double FadeIn { get; set; }

    /// <summary>Seconds of fade out (sound and picture).</summary>
    public double FadeOut { get; set; }

    [JsonIgnore]
    public bool IsLinked => Media == null && !string.IsNullOrWhiteSpace(LinkPath ?? LinkAbsolute);

    /// <summary>Playing length after trimming (null when the file's length isn't known).</summary>
    [JsonIgnore]
    public double? PlayLength
    {
        get
        {
            var end = TrimEnd ?? Length;
            return end == null ? null : Math.Max(0, end.Value - TrimStart);
        }
    }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! :
        !string.IsNullOrWhiteSpace(FileName) ? FileName! : KindName;
}

/// <summary>A video. Before it plays (and in pictures, PDF and thumbnails) the poster frame shows.</summary>
public sealed class VideoElement : MediaClipElement
{
    /// <summary>The poster picture in the media folder (null: a neutral placeholder).</summary>
    public string? Poster { get; set; }

    /// <summary>Where in the video the poster frame was taken (seconds).</summary>
    public double PosterTime { get; set; }

    /// <summary>Fills the screen while it plays in the slide show.</summary>
    public bool FullScreen { get; set; }

    /// <summary>Invisible in the slide show except while playing.</summary>
    public bool HideWhenNotPlaying { get; set; }

    public double CornerRadius { get; set; }
    public ImageMask Mask { get; set; }
    public Stroke? Stroke { get; set; }

    public override string KindName => "Video";

    public override string ContentKey => "video:" + (Media ?? LinkPath ?? "");
}

/// <summary>A sound: a speaker icon on the slide (which can be hidden in the slide show) that plays the file.</summary>
public sealed class AudioElement : MediaClipElement
{
    /// <summary>Hides the speaker icon during the slide show.</summary>
    public bool HideIcon { get; set; }

    /// <summary>Keeps playing when the show moves on (background music).</summary>
    public bool AcrossSlides { get; set; }

    /// <summary>With <see cref="AcrossSlides"/>: stop after this many slides (null: until the end of the show).</summary>
    public int? StopAfterSlides { get; set; }

    /// <summary>Colour of the speaker icon.</summary>
    public string Color { get; set; } = "accent1";

    public override string KindName => "Sound";

    public override string ContentKey => "audio:" + (Media ?? LinkPath ?? "");
}

/// <summary>A video on a website (YouTube, Vimeo…): a card with its picture that opens the video in the browser.</summary>
public sealed class OnlineVideoElement : Element
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }

    /// <summary>"YouTube", "Vimeo" or the site's name.</summary>
    public string? Provider { get; set; }

    /// <summary>The video's picture (downloaded once when inserted), in the media folder.</summary>
    public string? Thumbnail { get; set; }

    public double CornerRadius { get; set; }

    public override string KindName => "Online video";

    public override string ContentKey => "online:" + Url;

    public override string Describe => !string.IsNullOrWhiteSpace(Name) ? Name! : "Online video: " + (Title ?? Url);
}

/// <summary>File types for video and sound.</summary>
public static class MediaTypes
{
    public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".mpg", ".mpeg", ".ts", ".asf" };
    public static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac", ".ogg", ".opus", ".aif", ".aiff" };

    public static bool IsVideo(string? path) => VideoExtensions.Contains(System.IO.Path.GetExtension(path ?? "").ToLowerInvariant());

    public static bool IsAudio(string? path) => AudioExtensions.Contains(System.IO.Path.GetExtension(path ?? "").ToLowerInvariant());

    public static bool IsMedia(string? path) => IsVideo(path) || IsAudio(path);

    /// <summary>The MIME type (for PowerPoint export).</summary>
    public static string ContentType(string? path) => System.IO.Path.GetExtension(path ?? "").ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".wmv" => "video/x-ms-wmv",
        ".asf" => "video/x-ms-asf",
        ".avi" => "video/avi",
        ".mpg" or ".mpeg" => "video/mpeg",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".3gp" => "video/3gpp",
        ".ts" => "video/mp2t",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".wav" => "audio/wav",
        ".wma" => "audio/x-ms-wma",
        ".flac" => "audio/flac",
        ".ogg" or ".opus" => "audio/ogg",
        ".aif" or ".aiff" => "audio/x-aiff",
        _ => "application/octet-stream",
    };

    /// <summary>A file extension for a media content type (files in other packages may have none of their own).</summary>
    public static string? ExtensionFor(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "video/mp4" => ".mp4",
        "video/quicktime" => ".mov",
        "video/x-ms-wmv" => ".wmv",
        "video/x-ms-asf" or "video/x-ms-asf-plugin" => ".asf",
        "video/avi" or "video/x-msvideo" => ".avi",
        "video/mpeg" => ".mpg",
        "video/x-m4v" => ".m4v",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/x-m4a" or "audio/m4a" => ".m4a",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/x-ms-wma" => ".wma",
        "audio/aac" => ".aac",
        _ => null,
    };
}

/// <summary>Sizes that change how Stagecraft handles media.</summary>
public static class MediaLimits
{
    /// <summary>Above this (200 MB) inserting a video offers to link to the file instead of embedding it.</summary>
    public const long EmbedWarningBytes = 200L * 1000 * 1000;

    public static bool ShouldOfferLink(long bytes) => bytes > EmbedWarningBytes;

    /// <summary>"350 MB", "1.2 GB" (decimal units, like Explorer's size column in MB).</summary>
    public static string Describe(long bytes)
    {
        if (bytes >= 1_000_000_000) return (bytes / 1e9).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " GB";
        if (bytes >= 1_000_000) return (bytes / 1e6).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB";
        return Math.Max(1, bytes / 1000).ToString(System.Globalization.CultureInfo.CurrentCulture) + " KB";
    }
}
