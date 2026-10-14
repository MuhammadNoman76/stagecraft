using System.Globalization;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Editing;

public sealed class FindOptions
{
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }

    /// <summary>Also search the speaker notes.</summary>
    public bool IncludeNotes { get; init; } = true;
}

public enum FindArea { Text, TableCell, Notes }

/// <summary>
/// One place the search text was found. For slide text: the element, which of its text bodies, the paragraph and the
/// character range in that paragraph. For tables: the cell (row, column). For notes: the range in the notes.
/// </summary>
public sealed record FindMatch(string SlideId, string? ElementId, FindArea Area, int Body, int Paragraph, int Start, int Length);

/// <summary>
/// Find and replace across a presentation: slide text (text boxes and shapes), table cells and speaker notes. A match
/// never crosses paragraphs. Replacing keeps the formatting of the text where the match starts.
/// </summary>
public static class FindReplace
{
    /// <summary>Every match, slide by slide (in the order of the slide's elements, then its notes).</summary>
    public static List<FindMatch> FindAll(Deck deck, string query, FindOptions options)
    {
        var result = new List<FindMatch>();
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var slide in deck.Slides) result.AddRange(FindIn(slide, query, options));
        return result;
    }

    public static List<FindMatch> FindIn(Slide slide, string query, FindOptions options)
    {
        var result = new List<FindMatch>();
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var e in slide.Elements)
        {
            var bodies = Bodies(e);
            for (var b = 0; b < bodies.Count; b++)
            {
                var paragraphs = bodies[b].Content.Paragraphs;
                for (var p = 0; p < paragraphs.Count; p++)
                    foreach (var at in IndexesOf(TextOf(paragraphs[p]), query, options))
                        result.Add(new FindMatch(slide.Id, e.Id, FindArea.Text, b, p, at, query.Length));
            }
            if (e is TableElement table)
            {
                for (var r = 0; r < table.Cells.Count; r++)
                for (var c = 0; c < table.Cells[r].Count; c++)
                    foreach (var at in IndexesOf(table.Cells[r][c] ?? "", query, options))
                        result.Add(new FindMatch(slide.Id, e.Id, FindArea.TableCell, r, c, at, query.Length));
            }
        }
        if (options.IncludeNotes)
            foreach (var at in IndexesOf(slide.Notes ?? "", query, options))
                result.Add(new FindMatch(slide.Id, null, FindArea.Notes, 0, 0, at, query.Length));
        return result;
    }

    /// <summary>The text bodies of an element that find searches (same order as <see cref="FindMatch.Body"/>).</summary>
    public static List<TextBody> Bodies(Element e) => e switch
    {
        TextElement t => new List<TextBody> { t.Text },
        ShapeElement { Text: { } b } => new List<TextBody> { b },
        _ => new List<TextBody>(),
    };

    public static string TextOf(Paragraph p) => string.Concat(p.Runs.Select(r => r.Text ?? ""));

    /// <summary>Start positions of <paramref name="query"/> in <paramref name="text"/> (not overlapping).</summary>
    public static IEnumerable<int> IndexesOf(string text, string query, FindOptions options)
    {
        if (string.IsNullOrEmpty(query) || text.Length < query.Length) yield break;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        var from = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, comparison);
            if (at < 0) yield break;
            // A culture-aware match can have another length (ligatures); keep to exact-length matches.
            if (!options.MatchCase && string.Compare(text, at, query, 0, query.Length, comparison) != 0)
            {
                from = at + 1;
                continue;
            }
            if (!options.WholeWord || IsWholeWord(text, at, query.Length))
            {
                yield return at;
                from = at + query.Length;
            }
            else
            {
                from = at + 1;
            }
        }
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    private static bool IsWholeWord(string text, int start, int length)
    {
        var before = start == 0 || !IsWord(text[start - 1]);
        var end = start + length;
        var after = end >= text.Length || !IsWord(text[end]);
        return before && after;
    }

    /// <summary>Whether the text at a match still says the query (it may have been edited since the search).</summary>
    public static bool StillMatches(Slide slide, FindMatch m, string query, FindOptions options)
    {
        var text = TextAt(slide, m);
        if (text == null || m.Start < 0 || m.Start + m.Length > text.Length || m.Length != query.Length) return false;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        if (string.Compare(text, m.Start, query, 0, query.Length, comparison) != 0) return false;
        return !options.WholeWord || IsWholeWord(text, m.Start, m.Length);
    }

    /// <summary>The whole text a match lives in (a paragraph, a cell or the notes).</summary>
    public static string? TextAt(Slide slide, FindMatch m)
    {
        if (m.Area == FindArea.Notes) return slide.Notes ?? "";
        var e = m.ElementId != null ? slide.Find(m.ElementId) : null;
        if (e == null) return null;
        if (m.Area == FindArea.TableCell)
            return e is TableElement t && m.Body < t.Cells.Count && m.Paragraph < t.Cells[m.Body].Count ? t.Cells[m.Body][m.Paragraph] ?? "" : null;
        var bodies = Bodies(e);
        if (m.Body >= bodies.Count) return null;
        var paragraphs = bodies[m.Body].Content.Paragraphs;
        return m.Paragraph < paragraphs.Count ? TextOf(paragraphs[m.Paragraph]) : null;
    }

    /// <summary>Replaces one match on its slide. Returns false when the text there has changed.</summary>
    public static bool Replace(Slide slide, FindMatch m, string query, string replacement, FindOptions options)
    {
        if (!StillMatches(slide, m, query, options)) return false;
        ReplaceAt(slide, m, replacement);
        return true;
    }

    /// <summary>Replaces every match in the deck. Returns how many were replaced.</summary>
    public static int ReplaceAll(Deck deck, string query, string replacement, FindOptions options)
    {
        var count = 0;
        foreach (var slide in deck.Slides) count += ReplaceAll(slide, query, replacement, options);
        return count;
    }

    public static int ReplaceAll(Slide slide, string query, string replacement, FindOptions options)
    {
        // From the end backwards, so earlier positions stay valid.
        var matches = FindIn(slide, query, options);
        for (var i = matches.Count - 1; i >= 0; i--) ReplaceAt(slide, matches[i], replacement);
        return matches.Count;
    }

    private static void ReplaceAt(Slide slide, FindMatch m, string replacement)
    {
        switch (m.Area)
        {
            case FindArea.Notes:
                slide.Notes = (slide.Notes ?? "").Remove(m.Start, m.Length).Insert(m.Start, replacement);
                return;
            case FindArea.TableCell:
                if (slide.Find(m.ElementId!) is TableElement t)
                    t.Cells[m.Body][m.Paragraph] = (t.Cells[m.Body][m.Paragraph] ?? "").Remove(m.Start, m.Length).Insert(m.Start, replacement);
                return;
            default:
                if (slide.Find(m.ElementId!) is { } e && Bodies(e) is var bodies && m.Body < bodies.Count)
                    ReplaceInParagraph(bodies[m.Body].Content.Paragraphs[m.Paragraph], m.Start, m.Length, replacement);
                return;
        }
    }

    /// <summary>
    /// Replaces characters <paramref name="start"/>..+<paramref name="length"/> of a paragraph that may span several
    /// runs. The new text takes the formatting (and link) of the run where the range starts.
    /// </summary>
    public static void ReplaceInParagraph(Paragraph paragraph, int start, int length, string replacement)
    {
        var runs = paragraph.Runs;
        var position = 0;
        var inserted = false;
        var end = start + length;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var text = run.Text ?? "";
            var runStart = position;
            var runEnd = position + text.Length;
            position = runEnd;
            // The part of this run inside the range.
            var from = Math.Max(start, runStart);
            var to = Math.Min(end, runEnd);
            var startsHere = !inserted && start >= runStart && (start < runEnd || (start == runEnd && length == 0) || (text.Length == 0 && start == runStart));
            if (startsHere)
            {
                var cut = Math.Max(0, to - from);
                run.Text = text.Substring(0, start - runStart) + replacement + text.Substring(start - runStart + cut);
                inserted = true;
                continue;
            }
            if (inserted && to > from)
            {
                run.Text = text.Substring(0, from - runStart) + text.Substring(to - runStart);
            }
        }
        if (!inserted)
        {
            // Range at the very end of the paragraph.
            if (runs.Count == 0) runs.Add(new TextRun { Text = replacement });
            else runs[runs.Count - 1].Text = (runs[runs.Count - 1].Text ?? "") + replacement;
        }
        // Runs emptied by the replacement go (one run always stays).
        if (runs.Count > 1) runs.RemoveAll(r => string.IsNullOrEmpty(r.Text) && runs.Count > 1 && r != runs[0]);
    }
}
