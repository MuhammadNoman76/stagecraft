using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagecraft.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme { System, Light, Dark }

public sealed class RecentDeck
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public bool Pinned { get; set; }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Preferences and recent decks, stored as JSON in %APPDATA%\Stagecraft\settings.json.
/// Every change is applied to the file's current contents under a lock and written atomically.
/// </summary>
public sealed class AppSettings
{
    public const int MaxRecent = 24;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Minutes between recovery snapshots of unsaved work (0 = off).</summary>
    public int AutosaveMinutes { get; set; } = 2;

    // Updates (see Updater): check the feed now and then, remember when, and a version the user chose to skip.
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }

    // Editor
    public bool ShowNotes { get; set; } = true;
    public double NotesHeight { get; set; } = 132;
    public bool SnapToGuides { get; set; } = true;
    public string? LastTheme { get; set; }
    public string? LastVariant { get; set; }

    // Slide show
    public bool UsePresenterView { get; set; } = true;
    /// <summary>Device name of the display for the audience ("" = automatic: the second display).</summary>
    public string AudienceDisplay { get; set; } = "";
    public double PresenterNotesSize { get; set; } = 20;

    public string? LastFolder { get; set; }
    public string? LastExportFolder { get; set; }
    public WindowPlacement? Window { get; set; }
    public List<RecentDeck> Recent { get; set; } = new();

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagecraft");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AppSettings? s_current;

    public static AppSettings Current => s_current ??= Load();

    public static event Action? Changed;

    public static AppSettings Load()
    {
        var settings = WithFileLock(ReadFile) ?? new AppSettings();
        settings.Normalize();
        s_current = settings;
        return settings;
    }

    /// <summary>Applies <paramref name="change"/> to the latest settings on disk and saves them.</summary>
    public static void Update(Action<AppSettings> change)
    {
        try
        {
            WithFileLock<object>(() =>
            {
                var settings = ReadFile() ?? s_current ?? new AppSettings();
                settings.Normalize();
                change(settings);
                settings.Normalize();
                Directory.CreateDirectory(DataDirectory);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
                Compat.MoveOverwrite(tmp, FilePath);
                s_current = settings;
                return null;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Save settings");
            if (s_current != null) change(s_current);
        }
        Changed?.Invoke();
    }

    public static DateTime? ReadLastUpdateCheck() => WithFileLock(ReadFile)?.LastUpdateCheck;

    public static void AddRecent(string path) => Update(s =>
    {
        var existing = s.Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        s.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        s.Recent.Insert(0, new RecentDeck { Path = path, LastOpened = DateTime.UtcNow, Pinned = existing?.Pinned ?? false });
    });

    public static void RemoveRecent(string path) => Update(s => s.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)));

    private static AppSettings? ReadFile()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8), JsonOptions)
                : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Read settings");
            return null;
        }
    }

    private static T? WithFileLock<T>(Func<T?> action)
    {
        using var mutex = new Mutex(false, @"Local\Stagecraft-settings-" + Environment.UserName);
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { owned = true; }
            return action();
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    private void Normalize()
    {
        Recent ??= new List<RecentDeck>();
        Recent = Recent.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Path))
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .ToList();
        var unpinned = Recent.Where(r => !r.Pinned).OrderByDescending(r => r.LastOpened).Skip(MaxRecent).ToList();
        foreach (var old in unpinned) Recent.Remove(old);
        if (AutosaveMinutes < 0 || AutosaveMinutes > 60) AutosaveMinutes = 2;
        if (PresenterNotesSize < 10 || PresenterNotesSize > 64) PresenterNotesSize = 20;
        if (NotesHeight < 60 || NotesHeight > 600) NotesHeight = 132;
        AudienceDisplay ??= "";
    }
}
