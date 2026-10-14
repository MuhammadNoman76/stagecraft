using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfList = System.Windows.Documents.List;
using WpfTextElement = System.Windows.Documents.TextElement;
using Hyperlink = Stagecraft.Core.Model.Hyperlink;

namespace Stagecraft.Views.Editor;

/// <summary>
/// Converts a text body to a WPF FlowDocument for in-place editing (RichTextBox) and back. Only what differs
/// from the text box's style is stored on runs, so theme changes keep restyling the text.
/// </summary>
public static class RichTextConverter
{
    public sealed class Defaults
    {
        public FontFamily Family = FontCatalog.Get("Inter");
        public string FamilyName = "Inter";
        public int Weight = 400;
        public bool Italic;
        public double Size = 36;
        public Color Color = Colors.Black;
        public double LineHeight = 1.3;
        public double ParagraphSpacing;
        public bool Uppercase;
    }

    public static Defaults DefaultsOf(TextBody body, SlideStyle style)
    {
        var role = style.RoleFont(body.Role);
        var family = style.FontName(body.Font, role.Family);
        var size = body.Size ?? role.Size;
        return new Defaults
        {
            FamilyName = family,
            Family = FontCatalog.Get(family),
            Weight = Core.Rendering.TextLayout.BaseWeight(body, role),
            Italic = body.Italic ?? role.Italic,
            Size = size,
            Color = style.Resolve(body.Color ?? role.Color),
            LineHeight = body.LineSpacing ?? role.LineHeight,
            ParagraphSpacing = role.ParagraphSpacing * size,
            Uppercase = body.Uppercase ?? role.Uppercase,
        };
    }

    public static FlowDocument ToDocument(TextBody body, SlideStyle style)
    {
        var d = DefaultsOf(body, style);
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = d.Family,
            FontSize = d.Size,
            FontWeight = FontWeight.FromOpenTypeWeight(d.Weight),
            FontStyle = d.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = new SolidColorBrush(d.Color),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = Align(body.Align),
        };

        var paragraphs = body.Content.Paragraphs.Count > 0 ? body.Content.Paragraphs : new List<Core.Model.Paragraph> { new() };
        var listStack = new List<WpfList>();
        foreach (var p in paragraphs)
        {
            var para = new WpfParagraph { Margin = new Thickness(0, 0, 0, d.ParagraphSpacing) };
            var maxSize = p.Runs.Count == 0 ? d.Size : p.Runs.Max(r => r.Size ?? d.Size);
            para.LineHeight = Math.Max(1, maxSize * d.LineHeight);
            if (p.Align != null) para.TextAlignment = Align(p.Align.Value);
            System.Windows.Documents.Hyperlink? openLink = null;
            foreach (var r in p.Runs)
            {
                if (r.Text.Length == 0) continue;
                var run = new Run(r.Text);
                if (r.Bold == true) run.FontWeight = FontWeight.FromOpenTypeWeight(Math.Max(700, d.Weight));
                else if (r.Bold == false) run.FontWeight = FontWeight.FromOpenTypeWeight(Math.Min(d.Weight, 400));
                if (r.Italic is { } it) run.FontStyle = it ? FontStyles.Italic : FontStyles.Normal;
                if (r.Size is { } size) run.FontSize = size;
                if (r.Color != null) run.Foreground = new SolidColorBrush(style.Resolve(r.Color));
                if (r.Font != null) run.FontFamily = FontCatalog.Get(style.FontName(r.Font, d.FamilyName));
                if (r.Underline == true || r.Strike == true)
                {
                    var decorations = new TextDecorationCollection();
                    if (r.Underline == true) decorations.Add(TextDecorations.Underline);
                    if (r.Strike == true) decorations.Add(TextDecorations.Strikethrough);
                    run.TextDecorations = decorations;
                }
                if (r.Link != null)
                {
                    // Neighbouring runs with the same link share one hyperlink.
                    if (openLink == null || !(LinkOf(openLink)?.SameAs(r.Link) ?? false))
                    {
                        openLink = new System.Windows.Documents.Hyperlink();
                        Restyle(openLink, r.Link, style);
                        para.Inlines.Add(openLink);
                    }
                    openLink.Inlines.Add(run);
                    continue;
                }
                openLink = null;
                para.Inlines.Add(run);
            }

            if (p.List == ListKind.None)
            {
                listStack.Clear();
                doc.Blocks.Add(para);
                continue;
            }
            // Lists, nested by level.
            var level = MathEx.Clamp(p.Level, 0, 6);
            var marker = p.List == ListKind.Number ? TextMarkerStyle.Decimal : level % 2 == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle;
            while (listStack.Count > level + 1) listStack.RemoveAt(listStack.Count - 1);
            if (listStack.Count == level + 1 && listStack[level].MarkerStyle != marker) listStack.RemoveAt(level);
            while (listStack.Count < level + 1)
            {
                var list = new WpfList { MarkerStyle = marker, Margin = new Thickness(0), Padding = new Thickness(d.Size * 1.15, 0, 0, 0), MarkerOffset = d.Size * 0.5 };
                if (listStack.Count == 0) doc.Blocks.Add(list);
                else
                {
                    var parent = listStack[listStack.Count - 1];
                    var holder = parent.ListItems.LastListItem ?? new ListItem();
                    if (parent.ListItems.LastListItem == null) parent.ListItems.Add(holder);
                    holder.Blocks.Add(list);
                }
                listStack.Add(list);
            }
            listStack[level].ListItems.Add(new ListItem(para));
        }
        return doc;
    }

    private static TextAlignment Align(TextAlign a) => a switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static TextAlign? FromAlign(TextAlignment a, TextAlign body)
    {
        var mapped = a switch
        {
            TextAlignment.Center => TextAlign.Center,
            TextAlignment.Right => TextAlign.Right,
            TextAlignment.Justify => TextAlign.Justify,
            _ => TextAlign.Left,
        };
        return mapped == body ? null : mapped;
    }

    /// <summary>Reads the edited document back into rich text (only differences from the defaults are kept).</summary>
    public static RichText FromDocument(FlowDocument doc, TextBody body, SlideStyle style)
    {
        var d = DefaultsOf(body, style);
        var rich = new RichText();
        void Walk(BlockCollection blocks, ListKind list, int depth)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case WpfParagraph para:
                        rich.Paragraphs.Add(ReadParagraph(para, list, Math.Max(0, depth - 1), d, body, style));
                        break;
                    case WpfList l:
                        var kind = l.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? ListKind.Number : ListKind.Bullet;
                        foreach (var item in l.ListItems) Walk(item.Blocks, kind, depth + 1);
                        break;
                    case Section section:
                        Walk(section.Blocks, list, depth);
                        break;
                    case Table table:
                        foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            Walk(cell.Blocks, list, depth);
                        break;
                    case BlockUIContainer:
                        break;
                }
            }
        }
        Walk(doc.Blocks, ListKind.None, 0);
        if (rich.Paragraphs.Count == 0) rich.Paragraphs.Add(new Core.Model.Paragraph { Runs = { new TextRun() } });
        return rich;
    }

    private static Core.Model.Paragraph ReadParagraph(WpfParagraph para, ListKind list, int level, Defaults d, TextBody body, SlideStyle style)
    {
        var p = new Core.Model.Paragraph
        {
            List = list,
            Level = list == ListKind.None ? 0 : level,
            Align = FromAlign(para.TextAlignment, body.Align),
        };
        ReadInlines(para.Inlines, p, d, style);
        // Merge neighbours with the same format.
        var merged = new List<TextRun>();
        foreach (var r in p.Runs)
        {
            if (merged.Count > 0 && merged[merged.Count - 1].SameFormat(r)) merged[merged.Count - 1].Text += r.Text;
            else merged.Add(r);
        }
        p.Runs = merged.Count > 0 ? merged : new List<TextRun> { new() };
        return p;
    }

    private static void ReadInlines(InlineCollection inlines, Core.Model.Paragraph p, Defaults d, SlideStyle style)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length > 0) p.Runs.Add(ReadRun(run, run.Text, d, style));
                    break;
                case LineBreak lb:
                    p.Runs.Add(ReadRun(lb, "\n", d, style));
                    break;
                case Span span:
                    ReadInlines(span.Inlines, p, d, style);
                    break;
                case InlineUIContainer:
                    break;
            }
        }
    }

    private static TextRun ReadRun(Inline inline, string text, Defaults d, SlideStyle style, Hyperlink? link = null)
    {
        var run = new TextRun { Text = text, Link = link?.Clone() };
        var weight = inline.FontWeight.ToOpenTypeWeight();
        if (weight != d.Weight) run.Bold = weight >= 600;
        if ((inline.FontStyle != FontStyles.Normal) != d.Italic) run.Italic = inline.FontStyle != FontStyles.Normal;
        if (Math.Abs(inline.FontSize - d.Size) > 0.3) run.Size = Math.Round(inline.FontSize, 1);
        // Linked text shows in the link colour and underlined: that isn't the run's own formatting.
        var linkColor = Core.Rendering.TextLayout.LinkColorOn(style);
        if (inline.Foreground is SolidColorBrush brush && brush.Color != d.Color && !(link != null && brush.Color == linkColor)) run.Color = ColorRefFor(brush.Color, style);
        if (inline.FontFamily != null && !SameFamily(inline.FontFamily, d.Family)) run.Font = FamilyName(inline.FontFamily);
        var decorations = inline.TextDecorations;
        if (decorations != null && decorations.Count > 0)
        {
            if (link == null && decorations.Any(x => x.Location == TextDecorationLocation.Underline)) run.Underline = true;
            if (decorations.Any(x => x.Location == TextDecorationLocation.Strikethrough)) run.Strike = true;
        }
        return run;
    }

    /// <summary>The link a document hyperlink stands for (kept as JSON in its Tag).</summary>
    public static Hyperlink? LinkOf(System.Windows.Documents.Hyperlink hl)
    {
        try
        {
            return hl.Tag is string json ? Core.IO.DeckJson.FromJson<Hyperlink>(json) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Stores a link on a document hyperlink and gives it the theme's link look.</summary>
    public static void Restyle(System.Windows.Documents.Hyperlink hl, Hyperlink link, SlideStyle style)
    {
        hl.Tag = Core.IO.DeckJson.ToJson(link);
        hl.NavigateUri = null;
        hl.Foreground = new SolidColorBrush(Core.Rendering.TextLayout.LinkColorOn(style));
        hl.TextDecorations = TextDecorations.Underline;
        hl.ToolTip = (string.IsNullOrWhiteSpace(link.ScreenTip) ? link.Summary : link.ScreenTip) + Environment.NewLine + "Ctrl+click to follow";
    }

    private static bool SameFamily(FontFamily a, FontFamily b) => string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);

    /// <summary>"./#Inter, Segoe UI, …" or "Inter, Segoe UI" → "Inter".</summary>
    public static string FamilyName(FontFamily family) => FontCatalog.NameOf(family);

    /// <summary>A theme colour reference when the colour is one of the theme's, else hex.</summary>
    public static string ColorRefFor(Color color, SlideStyle style)
    {
        foreach (var name in new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "text", "text2", "bg", "bg2", "white", "black" })
            if (style.Resolve(name) == color) return name;
        return SlideStyle.ToHex(color);
    }
}
