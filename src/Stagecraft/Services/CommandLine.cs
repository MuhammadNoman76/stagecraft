namespace Stagecraft.Services;

public enum Verb
{
    /// <summary>No arguments: the start screen.</summary>
    Home,
    /// <summary>Stagecraft.exe &lt;file&gt;... - open decks (.stagecraft) or import PowerPoint files (.pptx).</summary>
    Open,
    /// <summary>Stagecraft.exe --present &lt;file&gt; - start the slide show.</summary>
    Present,
    /// <summary>Stagecraft.exe --export-pdf &lt;file&gt; &lt;out.pdf&gt; - export without a window.</summary>
    ExportPdf,
    /// <summary>Stagecraft.exe --settings</summary>
    Settings,
    /// <summary>Hidden: --capture &lt;folder&gt; renders screenshots of Stagecraft's own windows (development).</summary>
    Capture,
}

/// <summary>What Stagecraft was asked to do on its command line.</summary>
public sealed class Command
{
    public Verb Verb { get; init; }
    public List<string> Paths { get; init; } = new();
    public string? Output { get; init; }

    /// <summary>--solo: don't hand the files to a Stagecraft that is already running.</summary>
    public bool Solo { get; init; }

    /// <summary>--updated: started by the installer after an automatic update.</summary>
    public bool Updated { get; init; }

    /// <summary>
    /// Hidden test mode (--present-windowed / --windowed-show): the audience and presenter views open as
    /// ordinary small windows instead of full screen, so testing never covers anyone's screen.
    /// </summary>
    public bool WindowedShow { get; init; }

    /// <summary>Start the show from this slide (1-based) with --slide N.</summary>
    public int StartSlide { get; init; } = 1;

    /// <summary>Handed to the running Stagecraft instead of starting a second process.</summary>
    public bool CanHandOver => !Solo && !WindowedShow && Verb is Verb.Home or Verb.Open or Verb.Settings;

    public static Command Parse(IReadOnlyList<string> raw)
    {
        var args = raw.Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        bool Flag(string name) => args.RemoveAll(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) > 0;
        string? Value(string name)
        {
            var i = args.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            var value = i + 1 < args.Count ? args[i + 1] : null;
            args.RemoveRange(i, value != null ? 2 : 1);
            return value;
        }

        var solo = Flag("--solo");
        var updated = Flag("--updated");
        var windowed = Flag("--present-windowed") | Flag("--windowed-show");
        var slideText = Value("--slide");
        var startSlide = int.TryParse(slideText, out var n) && n > 0 ? n : 1;

        if (Flag("--settings")) return new Command { Verb = Verb.Settings, Updated = updated };

        var capture = Value("--capture");
        if (capture != null) return new Command { Verb = Verb.Capture, Output = FullPath(capture), Solo = true };

        var exportIndex = args.FindIndex(a => string.Equals(a, "--export-pdf", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0)
        {
            var input = exportIndex + 1 < args.Count ? args[exportIndex + 1] : null;
            var output = exportIndex + 2 < args.Count ? args[exportIndex + 2] : null;
            return new Command
            {
                Verb = Verb.ExportPdf,
                Paths = input != null ? new List<string> { FullPath(input) } : new List<string>(),
                Output = output != null ? FullPath(output) : null,
                Solo = true,
            };
        }

        var present = Flag("--present");
        args.RemoveAll(a => a.StartsWith("--", StringComparison.Ordinal));
        var paths = args.Select(FullPath).ToList();
        if (present || windowed)
            return new Command { Verb = Verb.Present, Paths = paths, Solo = true, WindowedShow = windowed, StartSlide = startSlide };
        if (paths.Count == 0) return new Command { Verb = Verb.Home, Solo = solo, Updated = updated };
        return new Command { Verb = Verb.Open, Paths = paths, Solo = solo, Updated = updated };
    }

    private static string FullPath(string arg)
    {
        var trimmed = arg.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    /// <summary>The arguments that reproduce this command (for handing it to another process).</summary>
    public List<string> ToArgs()
    {
        var list = new List<string>();
        switch (Verb)
        {
            case Verb.Settings:
                list.Add("--settings");
                break;
            case Verb.Present:
                list.Add("--present");
                list.AddRange(Paths);
                break;
            default:
                list.AddRange(Paths);
                break;
        }
        return list;
    }

    public static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"").TrimEnd('\\') + (arg.EndsWith("\\") ? "\\\\" : "") + "\"";
}
