using System.Windows;

namespace Stagecraft.Core.Editing;

/// <summary>
/// One-click "Tidy": turns a messy selection into a neat row, column or grid. Similar sizes become equal, edges
/// line up, and the gaps become even (the average gap, or a sensible default when things overlap).
/// </summary>
public static class Tidy
{
    public static IReadOnlyList<Rect> Arrange(IReadOnlyList<Rect> rects, double defaultGap = 32)
    {
        if (rects.Count < 2) return rects.ToList();
        var items = rects.Select((r, i) => (Index: i, Rect: r)).ToList();
        var rows = ClusterRows(items);
        var result = new Rect[rects.Count];

        if (rows.Count == 1)
        {
            foreach (var (i, r) in ArrangeLine(rows[0], horizontal: true, defaultGap)) result[i] = r;
            return result;
        }
        var columns = ClusterColumns(items);
        if (columns.Count == 1)
        {
            foreach (var (i, r) in ArrangeLine(columns[0], horizontal: false, defaultGap)) result[i] = r;
            return result;
        }

        // A grid: rows of cells lined up in columns.
        var columnCount = rows.Max(r => r.Count);
        var ordered = rows.Select(r => r.OrderBy(x => x.Rect.Left).ToList()).ToList();
        var widths = Similar(items.Select(x => x.Rect.Width).ToList(), out var w) ? Enumerable.Repeat(w, columnCount).ToList()
            : Enumerable.Range(0, columnCount).Select(c => ordered.Where(r => c < r.Count).Max(r => r[c].Rect.Width)).ToList();
        var heights = Similar(items.Select(x => x.Rect.Height).ToList(), out var h) ? Enumerable.Repeat(h, rows.Count).ToList()
            : ordered.Select(r => r.Max(x => x.Rect.Height)).ToList();
        var equalW = Similar(items.Select(x => x.Rect.Width).ToList(), out _);
        var equalH = Similar(items.Select(x => x.Rect.Height).ToList(), out _);

        var gapX = AverageGap(ordered.SelectMany(r => Gaps(r.Select(x => x.Rect).ToList(), true)), defaultGap);
        var gapY = AverageGap(Gaps(ordered.Select(r => new Rect(r.Min(x => x.Rect.Left), r.Min(x => x.Rect.Top), 1, r.Max(x => x.Rect.Bottom) - r.Min(x => x.Rect.Top))).ToList(), false), defaultGap);
        var left = items.Min(x => x.Rect.Left);
        var top = items.Min(x => x.Rect.Top);

        var y = top;
        for (var ri = 0; ri < ordered.Count; ri++)
        {
            var x = left;
            for (var ci = 0; ci < ordered[ri].Count; ci++)
            {
                var (index, rect) = ordered[ri][ci];
                var cw = widths[ci];
                var rh = heights[ri];
                var rw = equalW ? cw : rect.Width;
                var rhh = equalH ? rh : rect.Height;
                result[index] = Round(new Rect(x + (cw - rw) / 2, y + (rh - rhh) / 2, rw, rhh));
                x += cw + gapX;
            }
            y += heights[ri] + gapY;
        }
        return result;
    }

    private static List<List<(int Index, Rect Rect)>> ClusterRows(List<(int Index, Rect Rect)> items)
    {
        var rows = new List<List<(int Index, Rect Rect)>>();
        foreach (var item in items.OrderBy(x => x.Rect.Top + x.Rect.Height / 2))
        {
            var cy = item.Rect.Top + item.Rect.Height / 2;
            var row = rows.FirstOrDefault(r =>
            {
                var rcy = r.Average(x => x.Rect.Top + x.Rect.Height / 2);
                var minH = Math.Min(r.Min(x => x.Rect.Height), item.Rect.Height);
                return Math.Abs(rcy - cy) < Math.Max(minH * 0.5, 8);
            });
            if (row == null) rows.Add(new List<(int, Rect)> { item });
            else row.Add(item);
        }
        return rows;
    }

    private static List<List<(int Index, Rect Rect)>> ClusterColumns(List<(int Index, Rect Rect)> items)
    {
        var cols = new List<List<(int Index, Rect Rect)>>();
        foreach (var item in items.OrderBy(x => x.Rect.Left + x.Rect.Width / 2))
        {
            var cx = item.Rect.Left + item.Rect.Width / 2;
            var col = cols.FirstOrDefault(c =>
            {
                var ccx = c.Average(x => x.Rect.Left + x.Rect.Width / 2);
                var minW = Math.Min(c.Min(x => x.Rect.Width), item.Rect.Width);
                return Math.Abs(ccx - cx) < Math.Max(minW * 0.5, 8);
            });
            if (col == null) cols.Add(new List<(int, Rect)> { item });
            else col.Add(item);
        }
        return cols;
    }

    private static IEnumerable<(int Index, Rect Rect)> ArrangeLine(List<(int Index, Rect Rect)> line, bool horizontal, double defaultGap)
    {
        var sorted = line.OrderBy(x => horizontal ? x.Rect.Left : x.Rect.Top).ToList();
        var rects = sorted.Select(x => x.Rect).ToList();
        var sameMain = Similar(rects.Select(r => horizontal ? r.Width : r.Height).ToList(), out var main);
        var sameCross = Similar(rects.Select(r => horizontal ? r.Height : r.Width).ToList(), out var cross);
        var gap = AverageGap(Gaps(rects, horizontal), defaultGap);

        // Line up on the cross axis: start edges when sizes match, centres otherwise.
        var crossStart = Median(rects.Select(r => horizontal ? r.Top : r.Left).ToList());
        var crossCenter = Median(rects.Select(r => horizontal ? r.Top + r.Height / 2 : r.Left + r.Width / 2).ToList());

        var pos = horizontal ? rects[0].Left : rects[0].Top;
        foreach (var (index, r) in sorted)
        {
            var m = sameMain ? main : (horizontal ? r.Width : r.Height);
            var c = sameCross ? cross : (horizontal ? r.Height : r.Width);
            var cs = sameCross ? crossStart : crossCenter - c / 2;
            var rect = horizontal ? new Rect(pos, cs, m, c) : new Rect(cs, pos, c, m);
            yield return (index, Round(rect));
            pos += m + gap;
        }
    }

    private static IEnumerable<double> Gaps(IReadOnlyList<Rect> rects, bool horizontal)
    {
        var sorted = rects.OrderBy(r => horizontal ? r.Left : r.Top).ToList();
        for (var i = 1; i < sorted.Count; i++)
            yield return horizontal ? sorted[i].Left - sorted[i - 1].Right : sorted[i].Top - sorted[i - 1].Bottom;
    }

    private static double AverageGap(IEnumerable<double> gaps, double fallback)
    {
        var positive = gaps.Where(g => g > 1).ToList();
        if (positive.Count == 0) return fallback;
        return Math.Round(positive.Average());
    }

    /// <summary>True when all values are within 15 % of their median (returned in <paramref name="median"/>).</summary>
    private static bool Similar(IReadOnlyList<double> values, out double median)
    {
        median = Median(values);
        if (median <= 0) return false;
        var m = median;
        return values.All(v => Math.Abs(v - m) <= m * 0.15);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static Rect Round(Rect r) => new(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height));
}
