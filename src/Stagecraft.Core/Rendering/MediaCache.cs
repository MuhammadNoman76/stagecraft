using System.Globalization;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Rendering;

/// <summary>
/// Where Stagecraft keeps the video and sound files of open decks while it runs: a folder of its own under
/// %TEMP%\Stagecraft-media (one per running Stagecraft). It's deleted when Stagecraft exits; folders left behind by a
/// crash are removed on the next start. Files are named by the hash of their bytes, so a name always means the same content.
/// </summary>
public static class MediaCache
{
    private static readonly object Gate = new();
    private static string? s_folder;
    private static string s_root = Path.Combine(Path.GetTempPath(), "Stagecraft-media");

    /// <summary>The parent of every process's folder (tests point it somewhere else).</summary>
    public static string Root
    {
        get
        {
            lock (Gate) return s_root;
        }
        set
        {
            lock (Gate)
            {
                s_root = value;
                s_folder = null;
            }
        }
    }

    /// <summary>This process's folder (created on first use).</summary>
    public static string Folder
    {
        get
        {
            lock (Gate)
            {
                if (s_folder != null && Directory.Exists(s_folder)) return s_folder;
                s_folder ??= Path.Combine(s_root, Compat.ProcessId.ToString(CultureInfo.InvariantCulture) + "-" + Ids.New());
                Directory.CreateDirectory(s_folder);
                return s_folder;
            }
        }
    }

    public static string PathFor(string name) => Path.Combine(Folder, name);

    /// <summary>Writes a file under a temporary name first, so a half-written file never has the final name.</summary>
    public static void WriteAtomically(string path, Action<Stream> write)
    {
        var temp = path + "." + Ids.New() + ".part";
        try
        {
            using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16)) write(s);
            // Names are hashes of the content: a complete file already there (another window or thread wrote it at the
            // same moment, and may be reading it) is left alone. Only a damaged one (another length) is replaced.
            if (File.Exists(path) && new FileInfo(path).Length == new FileInfo(temp).Length) return;
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use (playing): names are hashes, so the bytes there are the same.
                    return;
                }
            }
            try
            {
                File.Move(temp, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another window (or thread) wrote the same file at the same moment: names are hashes, the bytes are the same.
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>Deletes this process's folder (at exit). Files still open stay until the next start.</summary>
    public static void DeleteOwnFolder()
    {
        string? folder;
        lock (Gate) folder = s_folder;
        if (folder == null) return;
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch
        {
            // Files still open: CleanStale removes them next time.
        }
    }

    /// <summary>Deletes the folders of Stagecraft processes that aren't running any more.</summary>
    public static void CleanStale()
    {
        string root;
        string? own;
        lock (Gate)
        {
            root = s_root;
            own = s_folder;
        }
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                var dash = name.IndexOf('-');
                if (dash <= 0 || !int.TryParse(name.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                if (string.Equals(dir, own, StringComparison.OrdinalIgnoreCase) || IsStagecraft(pid)) continue;
                try { Directory.Delete(dir, true); } catch { /* in use or already gone */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static bool IsStagecraft(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited && (p.ProcessName.IndexOf("Stagecraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    p.ProcessName.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }
}
