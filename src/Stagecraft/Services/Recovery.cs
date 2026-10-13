using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;

namespace Stagecraft.Services;

/// <summary>
/// Crash recovery: every few minutes (Settings › Autosave) a copy of each deck with unsaved changes is written to
/// %APPDATA%\Stagecraft\Recovery. Saving or closing normally deletes it; if Stagecraft stops unexpectedly, the
/// start screen offers it the next time.
/// </summary>
internal static class Recovery
{
    private static readonly RecoveryStore Store = new(Path.Combine(AppSettings.DataDirectory, "Recovery")) { OnError = Log.Error };

    public static string Folder => Store.Folder;

    public static Task SaveAsync(DeckSession session, string key) =>
        Store.SaveAsync(session.Deck, session.Media, key, session.DisplayName, session.FilePath);

    public static void Discard(string key) => Store.Discard(key);

    public static List<RecoveryEntry> FindOrphans() => Store.FindOrphans();
}
