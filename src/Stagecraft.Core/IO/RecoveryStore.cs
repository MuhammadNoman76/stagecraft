using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.IO;

/// <summary>A deck that was being edited when Stagecraft closed unexpectedly.</summary>
public sealed class RecoveryEntry
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? OriginalPath { get; set; }
    public DateTime SavedUtc { get; set; }
    public int ProcessId { get; set; }
    public DateTime ProcessStartUtc { get; set; }

    /// <summary>Set when the entry is read back: the recovered .stagecraft copy.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DeckPath { get; set; } = "";
}

/// <summary>
/// Crash recovery copies in one folder: <c>&lt;key&gt;.stagecraft</c> (the deck) plus <c>&lt;key&gt;.json</c> (title,
/// original path, and the process that wrote it). Copies whose process is still running belong to another open
/// Stagecraft and aren't offered.
/// </summary>
public sealed class RecoveryStore
{
    private static readonly DateTime s_processStart = GetProcessStart(Compat.ProcessId) ?? DateTime.UtcNow;

    public RecoveryStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>Called with errors (saving recovery copies must never interrupt editing).</summary>
    public Action<Exception, string>? OnError { get; set; }

    public string DeckPathOf(string key) => Path.Combine(Folder, key + DeckFile.Extension);

    /// <summary>Copies the deck on this thread, then writes the files in the background.</summary>
    public Task SaveAsync(Deck deck, MediaStore media, string key, string title, string? originalPath)
    {
        var copy = DeckJson.Clone(deck);
        var mediaCopy = media.Clone();
        return Task.Run(() => Save(copy, mediaCopy, key, title, originalPath));
    }

    /// <summary>Writes a recovery copy now. Returns false (and reports the error) if it couldn't be written.</summary>
    public bool Save(Deck deck, MediaStore media, string key, string title, string? originalPath, int? processId = null, DateTime? processStartUtc = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            DeckFile.Save(DeckPathOf(key), deck, media);
            var entry = new RecoveryEntry
            {
                Key = key,
                Title = title,
                OriginalPath = originalPath,
                SavedUtc = DateTime.UtcNow,
                ProcessId = processId ?? Compat.ProcessId,
                ProcessStartUtc = processStartUtc ?? s_processStart,
            };
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            var tmp = Path.Combine(Folder, key + ".json.tmp");
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            Compat.MoveOverwrite(tmp, Path.Combine(Folder, key + ".json"));
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "Recovery save");
            return false;
        }
    }

    public void Discard(string key)
    {
        foreach (var file in new[] { Path.Combine(Folder, key + ".json"), DeckPathOf(key), Path.Combine(Folder, key + ".json.tmp") })
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, "Recovery discard");
            }
        }
    }

    /// <summary>Recovery copies left behind by a Stagecraft that is no longer running, newest first.</summary>
    public List<RecoveryEntry> FindOrphans()
    {
        var result = new List<RecoveryEntry>();
        try
        {
            if (!Directory.Exists(Folder)) return result;
            foreach (var file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<RecoveryEntry>(File.ReadAllText(file, Encoding.UTF8));
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;
                    entry.DeckPath = DeckPathOf(entry.Key);
                    if (!File.Exists(entry.DeckPath) || IsAlive(entry)) continue;
                    result.Add(entry);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex, "Read recovery " + file);
                }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "Find recovery");
        }
        return result.OrderByDescending(e => e.SavedUtc).ToList();
    }

    private static bool IsAlive(RecoveryEntry entry)
    {
        var start = GetProcessStart(entry.ProcessId);
        return start != null && Math.Abs((start.Value - entry.ProcessStartUtc).TotalSeconds) < 2;
    }

    private static DateTime? GetProcessStart(int pid)
    {
        if (pid <= 0) return null;
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }
}
