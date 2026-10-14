using System.Diagnostics;
using System.Windows;
using Stagecraft.Core.Model;
using Stagecraft.Views;

namespace Stagecraft.Services;

/// <summary>
/// Opens web, e-mail and file links with the default app. Only http(s) and mailto addresses are handed to Windows
/// (see <see cref="LinkSafety"/>); files are opened only after asking, and programs and scripts never.
/// </summary>
internal static class LinkLauncher
{
    /// <summary>Tests and screenshots: links are logged, never opened.</summary>
    public static bool Suppress { get; set; } = MainWindow.IsTestOffscreen;

    /// <summary>The last link that would have been opened (tests).</summary>
    public static string? LastTarget { get; private set; }

    public static void Open(Hyperlink link, Window? owner, string? deckFolder)
    {
        switch (link.Kind)
        {
            case LinkKind.Web:
            case LinkKind.Email:
            {
                var uri = LinkSafety.SafeUri(link);
                if (uri == null)
                {
                    MessageDialog.Show(owner, "This link can't be opened", link.Kind == LinkKind.Email
                        ? "The e-mail address isn't valid: " + link.Address
                        : "Stagecraft only opens web addresses that start with http:// or https://.", "OK", icon: DialogIcon.Warning);
                    return;
                }
                Start(uri);
                return;
            }
            case LinkKind.File:
            {
                if (LinkSafety.IsBlockedFile(link.Address))
                {
                    MessageDialog.Show(owner, "Stagecraft doesn't open programs from links", "For your safety, links can't start programs or scripts: " + System.IO.Path.GetFileName(link.Address), "OK", icon: DialogIcon.Warning);
                    return;
                }
                var path = LinkSafety.ResolveFile(link.Address, deckFolder);
                if (path == null || !File.Exists(path))
                {
                    MessageDialog.Show(owner, "The linked file isn't there", path ?? link.Address, "OK", icon: DialogIcon.Warning);
                    return;
                }
                var choice = MessageDialog.Show(owner, $"Open “{System.IO.Path.GetFileName(path)}”?",
                    "The link opens this file with its usual app:\n" + path, "Open", close: "Cancel", icon: DialogIcon.Question);
                if (choice != DialogChoice.Primary) return;
                Start(path);
                return;
            }
        }
    }

    private static void Start(string target)
    {
        LastTarget = target;
        if (Suppress)
        {
            Log.Info("Link (not opened in test mode): " + target);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Open link " + target);
            MessageDialog.Show(null, "The link couldn't be opened", ex.Message, "OK", icon: DialogIcon.Error);
        }
    }
}
