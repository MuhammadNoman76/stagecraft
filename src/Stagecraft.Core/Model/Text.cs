using System.Text;
using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>Paragraphs of formatted runs.</summary>
public sealed class RichText
{
    public List<Paragraph> Paragraphs { get; set; } = new();

    /// <summary>The text with paragraphs separated by '\n'.</summary>
    [JsonIgnore]
    public string PlainText
    {
        get
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                foreach (var run in Paragraphs[i].Runs) sb.Append(run.Text);
            }
            return sb.ToString();
        }
    }

    [JsonIgnore]
    public bool IsEmpty => Paragraphs.All(p => p.Runs.All(r => string.IsNullOrEmpty(r.Text)));

    /// <summary>One paragraph per line of <paramref name="text"/>.</summary>
    public static RichText FromPlain(string? text, ListKind list = ListKind.None)
    {
        var rich = new RichText();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            var p = new Paragraph { List = list };
            p.Runs.Add(new TextRun { Text = line });
            rich.Paragraphs.Add(p);
        }
        return rich;
    }

    /// <summary>Paragraph per line; lines starting with "- ", "* " or "• " become bullets, "1. " numbered.</summary>
    public static RichText FromOutline(string? text)
    {
        var rich = new RichText();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var level = 0;
            while (line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal))
            {
                level++;
                line = line.StartsWith("\t", StringComparison.Ordinal) ? line.Substring(1) : line.Substring(2);
            }
            var list = ListKind.None;
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            {
                list = ListKind.Bullet;
                line = line.Substring(2);
            }
            else
            {
                var dot = line.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot <= 3 && line.Substring(0, dot).All(char.IsDigit))
                {
                    list = ListKind.Number;
                    line = line.Substring(dot + 2);
                }
            }
            var p = new Paragraph { List = list, Level = Math.Min(level, 4) };
            p.Runs.Add(new TextRun { Text = line });
            rich.Paragraphs.Add(p);
        }
        return rich;
    }

    public RichText Clone()
    {
        var copy = new RichText();
        foreach (var p in Paragraphs) copy.Paragraphs.Add(p.Clone());
        return copy;
    }
}

public enum ListKind { None, Bullet, Number }

public sealed class Paragraph
{
    public List<TextRun> Runs { get; set; } = new();

    /// <summary>Overrides the text box's alignment for this paragraph.</summary>
    public TextAlign? Align { get; set; }

    public ListKind List { get; set; }

    /// <summary>Indent level for lists (0 = first level).</summary>
    public int Level { get; set; }

    [JsonIgnore]
    public string Text => string.Concat(Runs.Select(r => r.Text));

    public Paragraph Clone() => new()
    {
        Runs = Runs.Select(r => r.Clone()).ToList(),
        Align = Align,
        List = List,
        Level = Level,
    };
}

/// <summary>A piece of text with the same formatting. Null properties inherit from the text box.</summary>
public sealed class TextRun
{
    public string Text { get; set; } = "";
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Strike { get; set; }
    public double? Size { get; set; }
    public string? Color { get; set; }
    public string? Font { get; set; }

    public TextRun Clone() => new()
    {
        Text = Text,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strike = Strike,
        Size = Size,
        Color = Color,
        Font = Font,
    };

    public bool SameFormat(TextRun other) =>
        Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Strike == other.Strike &&
        Nullable.Equals(Size, other.Size) && Color == other.Color && Font == other.Font;
}
