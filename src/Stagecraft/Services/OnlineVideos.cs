using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Stagecraft.Services;

/// <summary>A video page on the web, as far as Stagecraft understands its address.</summary>
public sealed class OnlineVideoInfo
{
    public OnlineVideoInfo(string url, string provider, string? id)
    {
        Url = url;
        Provider = provider;
        Id = id;
    }

    /// <summary>The page to open (http or https).</summary>
    public string Url { get; }

    /// <summary>"YouTube", "Vimeo" or the site's host name.</summary>
    public string Provider { get; }

    /// <summary>The video's id on YouTube or Vimeo.</summary>
    public string? Id { get; }
}

/// <summary>
/// Online videos (YouTube, Vimeo and others): recognises the site from the address and, only when asked, downloads
/// the video's picture and title through the site's public oEmbed / thumbnail addresses (no account or key). Nothing
/// else is sent; the slide show opens the page in the browser.
/// </summary>
public static class OnlineVideos
{
    public static OnlineVideoInfo? Describe(string? text)
    {
        var url = Core.Model.LinkSafety.NormalizeWeb(text);
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
        if (host.StartsWith("m.", StringComparison.Ordinal)) host = host.Substring(2);
        string? id = null;
        if (host is "youtube.com" or "youtube-nocookie.com" or "music.youtube.com")
        {
            id = Query(uri, "v");
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (id == null && segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" or "v") id = segments[1];
            return new OnlineVideoInfo(url, "YouTube", Clean(id));
        }
        if (host == "youtu.be")
        {
            id = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault();
            return new OnlineVideoInfo(url, "YouTube", Clean(id));
        }
        if (host is "vimeo.com" or "player.vimeo.com")
        {
            id = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault(s => s.Length > 0 && s.All(char.IsDigit));
            return new OnlineVideoInfo(url, "Vimeo", id);
        }
        var name = Enumerable.Reverse(host.Split('.')).Skip(1).FirstOrDefault() ?? host;
        name = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : host;
        return new OnlineVideoInfo(url, name, null);
    }

    private static string? Clean(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var clean = new string(id.TakeWhile(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length is >= 6 and <= 20 ? clean : null;
    }

    private static string? Query(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part.Substring(0, eq) == key) return Uri.UnescapeDataString(part.Substring(eq + 1));
        }
        return null;
    }

    /// <summary>The video's picture (JPEG bytes) and title, or nulls when they can't be had quickly.</summary>
    public static async Task<(byte[]? Bytes, string? Title)> FetchThumbnailAsync(OnlineVideoInfo info)
    {
        if (LinkLauncher.Suppress) return (null, null); // tests never go online
        try
        {
#if NETFRAMEWORK
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Stagecraft/" + Updater.CurrentVersionText);
            string? title = null;
            string? pictureUrl = null;
            string? oembed = info.Provider switch
            {
                "YouTube" => "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(info.Url),
                "Vimeo" => "https://vimeo.com/api/oembed.json?url=" + Uri.EscapeDataString(info.Url),
                _ => null,
            };
            if (oembed != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await client.GetStringAsync(oembed).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) title = t.GetString();
                    if (doc.RootElement.TryGetProperty("thumbnail_url", out var u) && u.ValueKind == JsonValueKind.String) pictureUrl = u.GetString();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    Log.Info("oEmbed for " + info.Url + ": " + ex.Message);
                }
            }
            if (info.Provider == "YouTube" && info.Id != null) pictureUrl = "https://img.youtube.com/vi/" + info.Id + "/hqdefault.jpg";
            if (pictureUrl == null || !pictureUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return (null, title);
            var bytes = await client.GetByteArrayAsync(pictureUrl).ConfigureAwait(false);
            if (bytes.Length < 100 || bytes.Length > 8_000_000 || Core.Rendering.MediaStore.Decode(bytes, 64) == null) return (null, title);
            return (bytes, title);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
        {
            Log.Info("Online video picture for " + info.Url + ": " + ex.Message);
            return (null, null);
        }
    }
}
