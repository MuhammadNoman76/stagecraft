using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>Where a link goes.</summary>
public enum LinkKind { Web, Email, Slide, File }

/// <summary>Slide links: which slide.</summary>
public enum SlideJump { Next, Previous, First, Last, LastViewed, Specific }

/// <summary>
/// A link on text, an element or an action button. Web links are http(s) only, e-mail links mailto:, file links a
/// path on disk (relative to the deck's folder when possible); nothing else is ever opened (see <see cref="LinkSafety"/>).
/// </summary>
public sealed class Hyperlink
{
    public LinkKind Kind { get; set; } = LinkKind.Web;

    /// <summary>Web: the address. Email: the address (without mailto:). File: the path as stored.</summary>
    public string Address { get; set; } = "";

    /// <summary>Email: the subject line.</summary>
    public string? Subject { get; set; }

    /// <summary>Slide links: which slide.</summary>
    public SlideJump Jump { get; set; }

    /// <summary>Slide links to a specific slide: its id.</summary>
    public string? SlideId { get; set; }

    /// <summary>Shown when the pointer rests on the link (the slide show and the editor).</summary>
    public string? ScreenTip { get; set; }

    public Hyperlink Clone() => new()
    {
        Kind = Kind,
        Address = Address,
        Subject = Subject,
        Jump = Jump,
        SlideId = SlideId,
        ScreenTip = ScreenTip,
    };

    public bool SameAs(Hyperlink? other) =>
        other != null && Kind == other.Kind && Address == other.Address && Subject == other.Subject && Jump == other.Jump &&
        SlideId == other.SlideId && ScreenTip == other.ScreenTip;

    public static Hyperlink Web(string url, string? tip = null) => new() { Kind = LinkKind.Web, Address = url, ScreenTip = tip };

    public static Hyperlink Email(string address, string? subject = null) => new() { Kind = LinkKind.Email, Address = address, Subject = subject };

    public static Hyperlink ToSlide(SlideJump jump, string? slideId = null) => new() { Kind = LinkKind.Slide, Jump = jump, SlideId = slideId };

    public static Hyperlink ToFile(string path) => new() { Kind = LinkKind.File, Address = path };

    /// <summary>A short description for menus and tooltips.</summary>
    [JsonIgnore]
    public string Summary => Kind switch
    {
        LinkKind.Web => Address,
        LinkKind.Email => "Email " + Address + (string.IsNullOrWhiteSpace(Subject) ? "" : " (" + Subject + ")"),
        LinkKind.File => "Open " + System.IO.Path.GetFileName(Address),
        _ => Jump switch
        {
            SlideJump.Next => "Next slide",
            SlideJump.Previous => "Previous slide",
            SlideJump.First => "First slide",
            SlideJump.Last => "Last slide",
            SlideJump.LastViewed => "Last slide viewed",
            _ => "A slide in this presentation",
        },
    };
}

/// <summary>
/// Which links Stagecraft will follow. Only http, https and mailto addresses are ever handed to Windows, plus files
/// (after asking); "javascript:", "data:", "vbscript:", "file:" URLs and the like are refused, and programs and scripts
/// are never started from a link.
/// </summary>
public static class LinkSafety
{
    private static readonly string[] BlockedExtensions =
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".pif",
        ".cpl", ".hta", ".jar", ".lnk", ".reg", ".inf", ".application", ".appref-ms", ".gadget", ".msc", ".dll", ".sys", ".url",
    };

    /// <summary>
    /// Turns what someone typed into a web address: adds https:// when there's no scheme. Returns null when it isn't
    /// a usable http(s) address (other schemes, spaces, no host).
    /// </summary>
    public static string? NormalizeWeb(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0 || s.Length > 2048) return null;
        if (s.Any(char.IsControl)) return null;
        var colon = s.IndexOf(':');
        var hasScheme = colon > 0 && s.Substring(0, colon).All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.') &&
                        !(colon < s.Length - 1 && char.IsDigit(s[colon + 1]) && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase)); // "example.com:8080"
        if (!hasScheme) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host) || s.Contains(' ')) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>A plausible e-mail address (one @, something before it, a dot after it, no spaces).</summary>
    public static bool IsEmail(string? address)
    {
        var s = (address ?? "").Trim();
        if (s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
        var at = s.IndexOf('@');
        if (at <= 0 || at != s.LastIndexOf('@') || at == s.Length - 1) return false;
        var domain = s.Substring(at + 1);
        return domain.Contains('.') && !domain.StartsWith(".", StringComparison.Ordinal) && !domain.EndsWith(".", StringComparison.Ordinal) &&
               !s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or '"' or ',' or ';');
    }

    /// <summary>The mailto: URL for an e-mail link (the subject is URL-encoded), or null if the address isn't valid.</summary>
    public static string? MailtoUri(string? address, string? subject)
    {
        var s = (address ?? "").Trim();
        if (s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
        if (!IsEmail(s)) return null;
        var uri = "mailto:" + s;
        if (!string.IsNullOrWhiteSpace(subject)) uri += "?subject=" + Uri.EscapeDataString(subject!.Trim());
        return uri;
    }

    /// <summary>Files that are programs or scripts: links never open these.</summary>
    public static bool IsBlockedFile(string? path)
    {
        var ext = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext.Length == 0 || BlockedExtensions.Contains(ext);
    }

    /// <summary>
    /// The URL Windows should open for a web or e-mail link, or null when it isn't one Stagecraft allows.
    /// File and slide links aren't URLs (the slide show handles them itself).
    /// </summary>
    public static string? SafeUri(Hyperlink? link) => link?.Kind switch
    {
        LinkKind.Web => NormalizeWeb(link.Address),
        LinkKind.Email => MailtoUri(link.Address, link.Subject),
        _ => null,
    };

    /// <summary>Whether a link has everything it needs (a valid address, an existing target slide…).</summary>
    public static bool IsValid(Hyperlink? link, Deck? deck = null) => link?.Kind switch
    {
        LinkKind.Web => NormalizeWeb(link.Address) != null,
        LinkKind.Email => IsEmail(link.Address),
        LinkKind.File => !string.IsNullOrWhiteSpace(link.Address) && !IsBlockedFile(link.Address),
        LinkKind.Slide => link.Jump != SlideJump.Specific || (link.SlideId != null && (deck == null || deck.FindSlide(link.SlideId) != null)),
        _ => false,
    };

    /// <summary>Resolves a file link against the deck's folder. Null when the path can't be used.</summary>
    public static string? ResolveFile(string? stored, string? deckFolder)
    {
        var s = (stored ?? "").Trim().Trim('"');
        if (s.Length == 0 || IsBlockedFile(s)) return null;
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(s, UriKind.Absolute, out var fileUri) && fileUri.IsFile) s = fileUri.LocalPath;
        try
        {
            if (System.IO.Path.IsPathRooted(s)) return System.IO.Path.GetFullPath(s);
            if (deckFolder == null) return null;
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(deckFolder, s));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Where a slide link goes from the slide at <paramref name="current"/> (0-based among <paramref name="slides"/>); -1 = nowhere.</summary>
    public static int JumpTarget(Hyperlink link, IReadOnlyList<Slide> slides, int current, int lastViewed = -1)
    {
        if (link.Kind != LinkKind.Slide || slides.Count == 0) return -1;
        return link.Jump switch
        {
            SlideJump.Next => current + 1 < slides.Count ? current + 1 : -1,
            SlideJump.Previous => current > 0 ? current - 1 : -1,
            SlideJump.First => 0,
            SlideJump.Last => slides.Count - 1,
            SlideJump.LastViewed => lastViewed >= 0 && lastViewed < slides.Count ? lastViewed : -1,
            _ => IndexOf(slides, link.SlideId),
        };
    }

    private static int IndexOf(IReadOnlyList<Slide> slides, string? id)
    {
        for (var i = 0; i < slides.Count; i++)
            if (slides[i].Id == id) return i;
        return -1;
    }
}
