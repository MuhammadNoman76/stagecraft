using System.Windows;
using Stagecraft.Core.Animation;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Themes;

namespace Stagecraft.Core.Editing;

public enum AlignMode { Left, CenterHorizontal, Right, Top, CenterVertical, Bottom }

public enum ZOrderMove { Front, Forward, Backward, Back }

/// <summary>Model operations behind the editor's commands (no UI, no undo: callers wrap them in session edits).</summary>
public static class DeckOperations
{
    /// <summary>A new deck with a title slide, in a theme.</summary>
    public static Deck NewDeck(ThemeDefinition? theme = null, string? variant = null, double width = Deck.DefaultWidth, double height = Deck.DefaultHeight)
    {
        var deck = new Deck
        {
            Width = width,
            Height = height,
            Theme = (theme ?? ThemeCatalog.Default).Create(variant),
        };
        deck.Slides.Add(LayoutEngine.CreateSlide(deck, LayoutIds.Title));
        return deck;
    }

    /// <summary>The layout a new slide after <paramref name="previous"/> should get.</summary>
    public static string NextLayout(Slide? previous) => previous?.Layout switch
    {
        null => LayoutIds.Title,
        LayoutIds.Title or LayoutIds.Section => LayoutIds.TitleContent,
        LayoutIds.ThankYou => LayoutIds.TitleContent,
        LayoutIds.Blank => LayoutIds.Blank,
        _ => previous.Layout,
    };

    /// <summary>A copy of a slide with new ids; its elements keep their morph identity so Morph animates between the two.</summary>
    public static Slide DuplicateSlide(Slide slide)
    {
        var copy = DeckJson.Clone(slide);
        copy.Id = Ids.New();
        copy.Section = null;
        copy.RehearsedSeconds = null;
        RenewIds(copy, keepMorphIdentity: true);
        return copy;
    }

    /// <summary>
    /// Gives every element (and group) of a copied slide a new id and keeps what refers to them working: connectors,
    /// animation targets and triggers. With <paramref name="keepMorphIdentity"/> the elements keep their Morph identity.
    /// Returns the map from old to new ids.
    /// </summary>
    public static Dictionary<string, string> RenewIds(Slide copy, bool keepMorphIdentity)
    {
        var map = new Dictionary<string, string>();
        foreach (var e in copy.Elements)
        {
            var old = e.Id;
            var fresh = Ids.New();
            map[old] = fresh;
            e.MorphKey = keepMorphIdentity ? e.MorphKey ?? old : null;
            e.Id = fresh;
            if (e.GroupId != null)
            {
                if (!map.TryGetValue(e.GroupId, out var g)) map[e.GroupId] = g = Ids.New();
                e.GroupId = g;
            }
        }
        foreach (var c in copy.Elements.OfType<ShapeElement>())
        {
            if (c.StartId != null) c.StartId = map.TryGetValue(c.StartId, out var s) ? s : null;
            if (c.EndId != null) c.EndId = map.TryGetValue(c.EndId, out var t) ? t : null;
        }
        copy.Animations = AnimationRules.CopyFor(copy.Animations, map);
        return map;
    }

    /// <summary>Copies of elements with new ids (and new group ids), moved by an offset.</summary>
    public static List<Element> CopyElements(IEnumerable<Element> elements, double dx, double dy, bool keepMorphIdentity = false) =>
        CopyElements(elements, dx, dy, out _, keepMorphIdentity);

    /// <summary>Copies of elements with new ids; <paramref name="idMap"/> maps old element and group ids to the new ones.</summary>
    public static List<Element> CopyElements(IEnumerable<Element> elements, double dx, double dy, out Dictionary<string, string> idMap, bool keepMorphIdentity = false)
    {
        idMap = new Dictionary<string, string>();
        var result = new List<Element>();
        foreach (var e in elements)
        {
            var copy = DeckJson.Clone(e);
            copy.MorphKey = keepMorphIdentity ? e.MorphKey ?? e.Id : null;
            copy.Id = Ids.New();
            idMap[e.Id] = copy.Id;
            copy.Placeholder = copy.Placeholder == "decor" ? null : copy.Placeholder;
            copy.X += dx;
            copy.Y += dy;
            if (copy.GroupId != null)
            {
                if (!idMap.TryGetValue(copy.GroupId, out var g)) idMap[copy.GroupId] = g = Ids.New();
                copy.GroupId = g;
            }
            result.Add(copy);
        }
        return result;
    }

    /// <summary>Switches the deck to a theme (or palette) and re-positions placeholders for its margins.</summary>
    public static void ApplyTheme(Deck deck, ThemeDefinition theme, string? variant)
    {
        deck.Theme = theme.Create(variant);
        foreach (var slide in deck.Slides) LayoutEngine.Reflow(deck, slide);
    }

    /// <summary>Changes the slide size. Content is scaled uniformly and centred so nothing is cut off.</summary>
    public static void SetSlideSize(Deck deck, double width, double height)
    {
        var oldW = deck.Width;
        var oldH = deck.Height;
        if (Math.Abs(oldW - width) < 0.5 && Math.Abs(oldH - height) < 0.5) return;
        var k = Math.Min(width / oldW, height / oldH);
        var ox = (width - oldW * k) / 2;
        var oy = (height - oldH * k) / 2;
        var textK = k / (height / oldH); // theme text sizes already follow the height
        deck.Width = width;
        deck.Height = height;
        foreach (var slide in deck.Slides)
        {
            foreach (var e in slide.Elements)
            {
                e.X = Math.Round(e.X * k + ox, 1);
                e.Y = Math.Round(e.Y * k + oy, 1);
                e.Width = Math.Round(e.Width * k, 1);
                e.Height = Math.Round(e.Height * k, 1);
                ScaleText(e, textK);
                if (e is ShapeElement s && s.Stroke != null) s.Stroke.Width *= k;
            }
            LayoutEngine.Reflow(deck, slide);
        }
    }

    private static void ScaleText(Element e, double k)
    {
        if (Math.Abs(k - 1) < 0.001) return;
        TextBody? body = e switch
        {
            TextElement t => t.Text,
            ShapeElement s => s.Text,
            _ => null,
        };
        if (body == null) return;
        if (body.Size is { } size) body.Size = Math.Round(size * k, 1);
        foreach (var p in body.Content.Paragraphs)
        foreach (var r in p.Runs)
            if (r.Size is { } rs) r.Size = Math.Round(rs * k, 1);
    }

    // ------------------------------------------------------------------ arrange

    /// <summary>Bounding box of elements (rotated boxes).</summary>
    public static Rect BoundsOf(IEnumerable<Element> elements)
    {
        var rect = Rect.Empty;
        foreach (var e in elements) rect.Union(e.RotatedBounds);
        return rect;
    }

    /// <summary>Aligns elements to each other, or to the slide when there's only one (or <paramref name="toSlide"/>).</summary>
    public static void Align(IReadOnlyList<Element> elements, AlignMode mode, Size slide, bool toSlide = false)
    {
        if (elements.Count == 0) return;
        // Groups move as one.
        var units = Units(elements);
        var target = toSlide || units.Count == 1 ? new Rect(0, 0, slide.Width, slide.Height) : BoundsOf(elements);
        foreach (var unit in units)
        {
            var b = BoundsOf(unit);
            double dx = 0, dy = 0;
            switch (mode)
            {
                case AlignMode.Left: dx = target.Left - b.Left; break;
                case AlignMode.CenterHorizontal: dx = target.Left + target.Width / 2 - (b.Left + b.Width / 2); break;
                case AlignMode.Right: dx = target.Right - b.Right; break;
                case AlignMode.Top: dy = target.Top - b.Top; break;
                case AlignMode.CenterVertical: dy = target.Top + target.Height / 2 - (b.Top + b.Height / 2); break;
                case AlignMode.Bottom: dy = target.Bottom - b.Bottom; break;
            }
            foreach (var e in unit)
            {
                e.X = Math.Round(e.X + dx, 2);
                e.Y = Math.Round(e.Y + dy, 2);
            }
        }
    }

    /// <summary>Spreads elements (or groups) so the gaps between them are equal.</summary>
    public static void Distribute(IReadOnlyList<Element> elements, bool horizontal)
    {
        var units = Units(elements);
        if (units.Count < 3) return;
        var boxes = units.Select(u => (Unit: u, Box: BoundsOf(u))).OrderBy(x => horizontal ? x.Box.Left : x.Box.Top).ToList();
        var first = boxes[0].Box;
        var last = boxes[boxes.Count - 1].Box;
        var span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        var total = boxes.Sum(b => horizontal ? b.Box.Width : b.Box.Height);
        var gap = (span - total) / (boxes.Count - 1);
        var pos = horizontal ? first.Left : first.Top;
        foreach (var (unit, box) in boxes)
        {
            var delta = pos - (horizontal ? box.Left : box.Top);
            foreach (var e in unit)
            {
                if (horizontal) e.X = Math.Round(e.X + delta, 2);
                else e.Y = Math.Round(e.Y + delta, 2);
            }
            pos += (horizontal ? box.Width : box.Height) + gap;
        }
    }

    /// <summary>Elements grouped by GroupId (ungrouped elements are units of their own), in z-order.</summary>
    public static List<List<Element>> Units(IEnumerable<Element> elements)
    {
        var result = new List<List<Element>>();
        var byGroup = new Dictionary<string, List<Element>>();
        foreach (var e in elements)
        {
            if (e.GroupId == null)
            {
                result.Add(new List<Element> { e });
                continue;
            }
            if (!byGroup.TryGetValue(e.GroupId, out var list))
            {
                byGroup[e.GroupId] = list = new List<Element>();
                result.Add(list);
            }
            list.Add(e);
        }
        return result;
    }

    public static void MoveZ(Slide slide, IReadOnlyCollection<string> ids, ZOrderMove move)
    {
        var selected = slide.Elements.Where(e => ids.Contains(e.Id)).ToList();
        if (selected.Count == 0) return;
        var list = slide.Elements;
        switch (move)
        {
            case ZOrderMove.Front:
                list.RemoveAll(e => ids.Contains(e.Id));
                list.AddRange(selected);
                break;
            case ZOrderMove.Back:
                list.RemoveAll(e => ids.Contains(e.Id));
                list.InsertRange(0, selected);
                break;
            case ZOrderMove.Forward:
                for (var i = list.Count - 2; i >= 0; i--)
                    if (ids.Contains(list[i].Id) && !ids.Contains(list[i + 1].Id))
                        (list[i], list[i + 1]) = (list[i + 1], list[i]);
                break;
            case ZOrderMove.Backward:
                for (var i = 1; i < list.Count; i++)
                    if (ids.Contains(list[i].Id) && !ids.Contains(list[i - 1].Id))
                        (list[i], list[i - 1]) = (list[i - 1], list[i]);
                break;
        }
    }

    /// <summary>Groups elements (existing groups among them are merged). Returns the group id.</summary>
    public static string Group(IEnumerable<Element> elements)
    {
        var id = Ids.New();
        foreach (var e in elements) e.GroupId = id;
        return id;
    }

    public static void Ungroup(IEnumerable<Element> elements)
    {
        foreach (var e in elements) e.GroupId = null;
    }

    /// <summary>Scales elements (a group being resized) from an old box to a new box.</summary>
    public static void ScaleInto(IEnumerable<Element> elements, Rect from, Rect to)
    {
        if (from.Width < 0.001 || from.Height < 0.001) return;
        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;
        foreach (var e in elements)
        {
            e.X = to.X + (e.X - from.X) * sx;
            e.Y = to.Y + (e.Y - from.Y) * sy;
            e.Width = Math.Max(1, e.Width * sx);
            e.Height = Math.Max(0, e.Height * sy);
        }
    }

    // ------------------------------------------------------------------ connectors

    /// <summary>Re-routes connectors attached to moved elements so they keep joining them.</summary>
    public static void UpdateConnectors(Slide slide)
    {
        foreach (var c in slide.Elements.OfType<ShapeElement>().Where(s => s.Shape == ShapeKind.Connector && (s.StartId != null || s.EndId != null)))
        {
            var (start, end) = Rendering.ElementRenderer.LineEnds(c, c.Bounds);
            var a = slide.Find(c.StartId ?? "");
            var b = slide.Find(c.EndId ?? "");
            if (a != null && b != null)
            {
                start = Anchor(a, c.StartSide, b.Center);
                end = Anchor(b, c.EndSide, a.Center);
            }
            else if (a != null) start = Anchor(a, c.StartSide, end);
            else if (b != null) end = Anchor(b, c.EndSide, start);
            SetLine(c, start, end);
        }
    }

    /// <summary>The point on an element's edge a connector attaches to (the side facing <paramref name="toward"/> for Auto).</summary>
    public static Point Anchor(Element e, ConnectorSide side, Point toward)
    {
        var b = e.Bounds;
        var c = e.Center;
        if (side == ConnectorSide.Auto)
        {
            var dx = toward.X - c.X;
            var dy = toward.Y - c.Y;
            side = Math.Abs(dx) * b.Height >= Math.Abs(dy) * b.Width
                ? dx >= 0 ? ConnectorSide.Right : ConnectorSide.Left
                : dy >= 0 ? ConnectorSide.Bottom : ConnectorSide.Top;
        }
        return side switch
        {
            ConnectorSide.Left => new Point(b.Left, c.Y),
            ConnectorSide.Right => new Point(b.Right, c.Y),
            ConnectorSide.Top => new Point(c.X, b.Top),
            _ => new Point(c.X, b.Bottom),
        };
    }

    /// <summary>Stores a line from <paramref name="start"/> to <paramref name="end"/> as box + flips.</summary>
    public static void SetLine(ShapeElement line, Point start, Point end)
    {
        line.X = Math.Min(start.X, end.X);
        line.Y = Math.Min(start.Y, end.Y);
        line.Width = Math.Abs(end.X - start.X);
        line.Height = Math.Abs(end.Y - start.Y);
        line.FlipH = end.X < start.X;
        line.FlipV = end.Y < start.Y;
        line.Rotation = 0;
    }
}
