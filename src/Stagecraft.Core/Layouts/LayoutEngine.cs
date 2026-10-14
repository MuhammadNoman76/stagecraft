using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Layouts;

/// <summary>A smart layout offered in the UI.</summary>
public sealed record LayoutInfo(string Id, string Name, string Description, string Icon);

public static class LayoutIds
{
    public const string Title = "title";
    public const string Section = "section";
    public const string TitleContent = "title-content";
    public const string TwoColumns = "two-columns";
    public const string TitleOnly = "title-only";
    public const string BigNumber = "big-number";
    public const string Quote = "quote";
    public const string ImageLeft = "image-left";
    public const string ImageRight = "image-right";
    public const string ImageFull = "image-full";
    public const string Agenda = "agenda";
    public const string Timeline = "timeline";
    public const string Comparison = "comparison";
    public const string Team = "team";
    public const string ThankYou = "thank-you";
    public const string Blank = "blank";
}

/// <summary>The content of a slide's placeholders, carried over when its layout changes.</summary>
public sealed class LayoutContent
{
    public RichText? Title { get; set; }
    public RichText? Subtitle { get; set; }
    public RichText? Label { get; set; }
    public RichText? Body { get; set; }
    public RichText? Body2 { get; set; }
    public RichText? Heading1 { get; set; }
    public RichText? Heading2 { get; set; }
    public RichText? Number { get; set; }
    public RichText? Quote { get; set; }
    public List<RichText> Items { get; set; } = new();
    public List<RichText> Details { get; set; } = new();
    public List<string?> Images { get; set; } = new();

    /// <summary>Elements that held this content, by placeholder, so a new layout can keep their identity (Morph).</summary>
    public Dictionary<string, Element> Sources { get; } = new();

    public static LayoutContent From(Slide slide)
    {
        var c = new LayoutContent();
        RichText? Get(string role)
        {
            var e = slide.Elements.FirstOrDefault(x => x.Placeholder == role);
            if (e != null) c.Sources[role] = e;
            return e switch
            {
                TextElement t when !t.Text.Content.IsEmpty => t.Text.Content.Clone(),
                ShapeElement { Text: { } body } when !body.Content.IsEmpty => body.Content.Clone(),
                _ => null,
            };
        }

        c.Title = Get("title");
        c.Subtitle = Get("subtitle") ?? Get("author");
        c.Label = Get("label");
        c.Body = Get("body");
        c.Body2 = Get("body2");
        c.Heading1 = Get("heading1");
        c.Heading2 = Get("heading2");
        c.Number = Get("number");
        c.Quote = Get("quote");
        for (var i = 1; i <= 8; i++)
        {
            var item = Get("item" + i) ?? Get("step" + i) ?? Get("name" + i);
            if (item != null) c.Items.Add(item);
            var detail = Get("date" + i) ?? Get("role" + i);
            if (detail != null) c.Details.Add(detail);
        }
        foreach (var img in slide.Elements.OfType<ImageElement>().Where(e => e.Placeholder != null && e.Placeholder.StartsWith("image", StringComparison.Ordinal)))
        {
            c.Images.Add(img.Media);
            c.Sources[img.Placeholder!] = img;
        }
        if (c.Title == null)
        {
            var heading = slide.Elements.OfType<TextElement>().FirstOrDefault(t => t.Placeholder == null && t.Text.Role is TextRole.Title or TextRole.Heading);
            if (heading != null && !heading.Text.Content.IsEmpty) c.Title = heading.Text.Content.Clone();
        }
        return c;
    }

    /// <summary>The body's paragraphs as separate items (agenda, timeline).</summary>
    public List<RichText> ItemsOrBody()
    {
        if (Items.Count > 0) return Items;
        if (Body == null) return new List<RichText>();
        return Body.Paragraphs.Where(p => p.Text.Trim().Length > 0)
            .Select(p => new RichText { Paragraphs = { new Paragraph { Runs = p.Runs.Select(r => r.Clone()).ToList() } } })
            .ToList();
    }
}

/// <summary>
/// Builds the elements of the smart layouts for a deck's size and theme (margins, title alignment, radius),
/// and switches a slide to another layout while keeping its content.
/// </summary>
public static class LayoutEngine
{
    public static IReadOnlyList<LayoutInfo> All { get; } = new[]
    {
        new LayoutInfo(LayoutIds.Title, "Title", "Title and subtitle", "slide_text_title"),
        new LayoutInfo(LayoutIds.Section, "Section", "Start a new part", "text_header_1"),
        new LayoutInfo(LayoutIds.TitleContent, "Title + content", "Title and bullet points", "slide_text"),
        new LayoutInfo(LayoutIds.TwoColumns, "Two columns", "Title and two columns of text", "text_column_two"),
        new LayoutInfo(LayoutIds.TitleOnly, "Title only", "Room for a chart, table or picture", "slide_text_title"),
        new LayoutInfo(LayoutIds.BigNumber, "Big number", "One statistic, large", "number_symbol"),
        new LayoutInfo(LayoutIds.Quote, "Quote", "A quotation and who said it", "text_quote"),
        new LayoutInfo(LayoutIds.ImageLeft, "Image left", "Picture on the left, text on the right", "image_split"),
        new LayoutInfo(LayoutIds.ImageRight, "Image right", "Text on the left, picture on the right", "image_split"),
        new LayoutInfo(LayoutIds.ImageFull, "Full-bleed image", "A picture filling the slide with a title", "image"),
        new LayoutInfo(LayoutIds.Agenda, "Agenda", "Numbered list of topics", "text_number_list_ltr"),
        new LayoutInfo(LayoutIds.Timeline, "Timeline", "Milestones on a line", "timeline"),
        new LayoutInfo(LayoutIds.Comparison, "Comparison", "Two options side by side", "column_triple"),
        new LayoutInfo(LayoutIds.Team, "Team", "People with photos", "people_team"),
        new LayoutInfo(LayoutIds.ThankYou, "Thank you", "Closing slide", "heart"),
        new LayoutInfo(LayoutIds.Blank, "Blank", "Start from nothing", "square"),
    };

    public static LayoutInfo? Find(string? id) => All.FirstOrDefault(l => l.Id == id);

    /// <summary>A new slide with this layout (placeholders empty unless <paramref name="content"/> fills them).</summary>
    public static Slide CreateSlide(Deck deck, string layoutId, LayoutContent? content = null)
    {
        var slide = new Slide { Layout = layoutId };
        slide.Elements.AddRange(Build(deck, slide, layoutId, content ?? new LayoutContent()));
        return slide;
    }

    /// <summary>Switches <paramref name="slide"/> to another layout: placeholders are rebuilt with their content, everything the user added stays.</summary>
    public static void Apply(Deck deck, Slide slide, string layoutId)
    {
        var content = LayoutContent.From(slide);
        var kept = slide.Elements.Where(e => e.Placeholder == null).ToList();
        slide.Layout = layoutId;
        var built = Build(deck, slide, layoutId, content);
        slide.Elements.Clear();
        slide.Elements.AddRange(built);
        slide.Elements.AddRange(kept);
        Animation.AnimationRules.Prune(slide);
    }

    /// <summary>
    /// Re-positions a slide's placeholders for the deck's current size and theme (after a theme or slide size change).
    /// Placeholders the user moved or resized stay where they are; text keeps its formatting; decorations are rebuilt.
    /// </summary>
    public static void Reflow(Deck deck, Slide slide)
    {
        if (slide.Layout == LayoutIds.Blank) return;
        var content = LayoutContent.From(slide);
        var built = Build(deck, slide, slide.Layout, content);
        var result = new List<Element>();
        foreach (var fresh in built)
        {
            if (fresh.Placeholder == "decor")
            {
                result.Add(fresh);
                continue;
            }
            var old = slide.Elements.FirstOrDefault(e => e.Placeholder == fresh.Placeholder && e.GetType() == fresh.GetType());
            if (old == null)
            {
                result.Add(fresh);
                continue;
            }
            if (!old.Moved)
            {
                old.X = fresh.X;
                old.Y = fresh.Y;
                old.Width = fresh.Width;
                old.Height = fresh.Height;
                if (old is TextElement ot && fresh is TextElement ft)
                {
                    ot.Text.Align = ft.Text.Align;
                    ot.Text.VAlign = ft.Text.VAlign;
                }
            }
            result.Add(old);
        }
        // Placeholders the layout no longer has and everything the user added stay, above the layout.
        foreach (var e in slide.Elements)
            if (e.Placeholder != "decor" && !result.Contains(e)) result.Add(e);
        slide.Elements.Clear();
        slide.Elements.AddRange(result);
        Animation.AnimationRules.Prune(slide);
    }

    private static List<Element> Build(Deck deck, Slide slide, string layoutId, LayoutContent content)
    {
        var b = new Builder(deck, slide, content);
        switch (layoutId)
        {
            case LayoutIds.Title: b.TitleSlide(); break;
            case LayoutIds.Section: b.Section(); break;
            case LayoutIds.TitleContent: b.TitleContent(); break;
            case LayoutIds.TwoColumns: b.TwoColumns(); break;
            case LayoutIds.TitleOnly: b.TitleOnly(); break;
            case LayoutIds.BigNumber: b.BigNumber(); break;
            case LayoutIds.Quote: b.QuoteSlide(); break;
            case LayoutIds.ImageLeft: b.ImageSide(left: true); break;
            case LayoutIds.ImageRight: b.ImageSide(left: false); break;
            case LayoutIds.ImageFull: b.ImageFull(); break;
            case LayoutIds.Agenda: b.Agenda(); break;
            case LayoutIds.Timeline: b.Timeline(); break;
            case LayoutIds.Comparison: b.Comparison(); break;
            case LayoutIds.Team: b.Team(); break;
            case LayoutIds.ThankYou: b.ThankYou(); break;
        }
        return b.Elements;
    }

    private sealed class Builder
    {
        private readonly Deck _deck;
        private readonly LayoutContent _content;
        private readonly SlideStyle _style;
        private readonly double W, H, M;

        public Builder(Deck deck, Slide slide, LayoutContent content)
        {
            _deck = deck;
            _content = content;
            _style = SlideStyle.For(deck.Theme, slide, deck.Height);
            W = deck.Width;
            H = deck.Height;
            M = _style.Margin;
        }

        public List<Element> Elements { get; } = new();

        private bool Centered => _deck.Theme.TitleAlign == TextAlign.Center;
        private double Radius => _deck.Theme.Radius * _style.Scale;
        private double S => _style.Scale;

        private TextElement Text(string placeholder, TextRole role, double x, double y, double w, double h, RichText? content,
            TextAlign align = TextAlign.Left, VerticalAlign valign = VerticalAlign.Top, string? fallback = null)
        {
            var t = new TextElement
            {
                Placeholder = placeholder,
                X = Math.Round(x),
                Y = Math.Round(y),
                Width = Math.Round(w),
                Height = Math.Round(h),
                Text = new TextBody
                {
                    Role = role,
                    Align = align,
                    VAlign = valign,
                    Content = content?.Clone() ?? (fallback != null ? RichText.FromPlain(fallback) : new RichText()),
                },
            };
            Keep(t, placeholder);
            Elements.Add(t);
            return t;
        }

        /// <summary>Keeps the identity of the element that held this content before (so Morph matches it).</summary>
        private void Keep(Element e, string placeholder)
        {
            if (_content.Sources.TryGetValue(placeholder, out var old))
            {
                e.Id = old.Id;
                e.MorphKey = old.MorphKey;
                e.Name = old.Name;
            }
        }

        private ImageElement Image(string placeholder, double x, double y, double w, double h, int index, ImageMask mask = ImageMask.Rectangle, double radius = 0)
        {
            var img = new ImageElement
            {
                Placeholder = placeholder,
                X = Math.Round(x),
                Y = Math.Round(y),
                Width = Math.Round(w),
                Height = Math.Round(h),
                Media = index < _content.Images.Count ? _content.Images[index] : null,
                Mask = mask,
                CornerRadius = radius,
            };
            Keep(img, placeholder);
            Elements.Add(img);
            return img;
        }

        private ShapeElement Decor(ShapeKind kind, double x, double y, double w, double h, Fill fill, Stroke? stroke = null, double radius = 0)
        {
            var s = new ShapeElement
            {
                Placeholder = "decor",
                Shape = kind,
                X = Math.Round(x),
                Y = Math.Round(y),
                Width = Math.Round(w),
                Height = Math.Round(h),
                Fill = fill,
                Stroke = stroke,
                CornerRadius = radius,
            };
            Elements.Add(s);
            return s;
        }

        private double TitleTop => M * 0.82;
        private double TitleHeight => H * 0.15;
        private double ContentTop => TitleTop + TitleHeight + H * 0.045;

        private void Heading() =>
            Text("title", TextRole.Heading, M, TitleTop, W - 2 * M, TitleHeight, _content.Title, valign: VerticalAlign.Bottom);

        public void TitleSlide()
        {
            if (Centered)
            {
                Text("title", TextRole.Title, M, H * 0.24, W - 2 * M, H * 0.36, _content.Title, TextAlign.Center, VerticalAlign.Bottom);
                Text("subtitle", TextRole.Subtitle, M + W * 0.06, H * 0.63, W - 2 * M - W * 0.12, H * 0.16, _content.Subtitle, TextAlign.Center);
            }
            else
            {
                Text("title", TextRole.Title, M, H * 0.22, W * 0.74, H * 0.4, _content.Title, valign: VerticalAlign.Bottom);
                Text("subtitle", TextRole.Subtitle, M, H * 0.655, W * 0.66, H * 0.16, _content.Subtitle);
            }
        }

        public void Section()
        {
            var align = Centered ? TextAlign.Center : TextAlign.Left;
            var x = Centered ? M : M;
            var w = Centered ? W - 2 * M : W * 0.74;
            Text("label", TextRole.Label, x, H * 0.33 - 64 * S, w, 52 * S, _content.Label, align, VerticalAlign.Bottom);
            Text("title", TextRole.Title, x, H * 0.36, w, H * 0.34, _content.Title, align);
        }

        public void TitleContent()
        {
            Heading();
            var body = Text("body", TextRole.Body, M, ContentTop, W - 2 * M, H - ContentTop - M * 0.95, _content.Body ?? Bullets(_content.ItemsOrBody()));
            EnsureBullets(body);
        }

        public void TwoColumns()
        {
            Heading();
            var gap = W * 0.05;
            var colW = (W - 2 * M - gap) / 2;
            var h = H - ContentTop - M * 0.95;
            EnsureBullets(Text("body", TextRole.Body, M, ContentTop, colW, h, _content.Body));
            EnsureBullets(Text("body2", TextRole.Body, M + colW + gap, ContentTop, colW, h, _content.Body2));
        }

        public void TitleOnly() => Heading();

        public void BigNumber()
        {
            Text("number", TextRole.Stat, M, H * 0.16, W * 0.5 - M, H * 0.62, _content.Number, valign: VerticalAlign.Middle);
            var x = W * 0.54;
            Text("title", TextRole.Heading, x, H * 0.22, W - x - M, H * 0.28, _content.Title, valign: VerticalAlign.Bottom);
            Text("body", TextRole.Body, x, H * 0.53, W - x - M, H * 0.3, _content.Body).Text.Color = "text2";
        }

        public void QuoteSlide()
        {
            var markSize = 300 * S;
            var mark = new TextElement
            {
                Placeholder = "decor",
                X = Math.Round(Centered ? (W - markSize * 0.8) / 2 : M - 12 * S),
                Y = Math.Round(H * 0.06),
                Width = Math.Round(markSize * 0.8),
                Height = Math.Round(markSize * 0.75),
                Text = new TextBody
                {
                    Role = TextRole.Stat,
                    Size = markSize,
                    Font = "heading",
                    Color = "accent1",
                    AutoFit = false,
                    Align = Centered ? TextAlign.Center : TextAlign.Left,
                    Content = RichText.FromPlain("“"),
                },
            };
            Elements.Add(mark);
            var align = Centered ? TextAlign.Center : TextAlign.Left;
            Text("quote", TextRole.Quote, M, H * 0.3, W - 2 * M, H * 0.42, _content.Quote ?? _content.Body, align, VerticalAlign.Middle);
            Text("author", TextRole.Subtitle, M, H * 0.76, W - 2 * M, H * 0.1, _content.Subtitle, align);
        }

        public void ImageSide(bool left)
        {
            var imageW = W * 0.46;
            var imageX = left ? 0 : W - imageW;
            Image("image", imageX, 0, imageW, H, 0);
            var x = left ? imageW + M * 0.85 : M;
            var w = W - imageW - M * 0.85 - M;
            Text("title", TextRole.Heading, x, H * 0.16, w, H * 0.3, _content.Title, valign: VerticalAlign.Bottom);
            Text("body", TextRole.Body, x, H * 0.5, w, H * 0.38, _content.Body);
        }

        public void ImageFull()
        {
            Image("image", 0, 0, W, H, 0);
            Decor(ShapeKind.Rectangle, 0, H * 0.35, W, H * 0.65, Fill.Linear("#000000@0", "#000000@72", 90));
            var title = Text("title", TextRole.Title, M, H * 0.5, W * 0.72, H * 0.27, _content.Title, valign: VerticalAlign.Bottom);
            title.Text.Color = "white";
            var sub = Text("subtitle", TextRole.Subtitle, M, H * 0.79, W * 0.62, H * 0.1, _content.Subtitle);
            sub.Text.Color = "white@82";
        }

        public void Agenda()
        {
            Text("title", TextRole.Heading, M, M, W * 0.3, H * 0.4, _content.Title, fallback: "Agenda");
            var items = _content.ItemsOrBody();
            var n = MathEx.Clamp(items.Count == 0 ? 4 : items.Count, 3, 6);
            var x = W * 0.42;
            var top = M * 0.9;
            var rowH = (H - top - M * 0.9) / n;
            var numberW = 120 * S;
            for (var i = 0; i < n; i++)
            {
                var y = top + rowH * i;
                var num = new TextElement
                {
                    Placeholder = "decor",
                    X = Math.Round(x),
                    Y = Math.Round(y),
                    Width = Math.Round(numberW),
                    Height = Math.Round(rowH),
                    Text = new TextBody
                    {
                        Role = TextRole.Heading,
                        Size = Math.Min(56 * S, rowH * 0.5),
                        Color = "accent1",
                        VAlign = VerticalAlign.Middle,
                        AutoFit = false,
                        Content = RichText.FromPlain((i + 1).ToString("00")),
                    },
                };
                Elements.Add(num);
                var item = Text("item" + (i + 1), TextRole.Subtitle, x + numberW + 24 * S, y, W - M - (x + numberW + 24 * S), rowH,
                    i < items.Count ? items[i] : null, valign: VerticalAlign.Middle);
                item.Text.Color = "text";
                item.Text.Size = Math.Min(44 * S, rowH * 0.36);
                if (i < n - 1) Decor(ShapeKind.Line, x, y + rowH, W - M - x, 0, Fill.None, new Stroke { Color = "text2@28", Width = 2 * S });
            }
        }

        public void Timeline()
        {
            Heading();
            var items = _content.ItemsOrBody();
            var n = MathEx.Clamp(items.Count == 0 ? 4 : items.Count, 3, 6);
            var lineY = H * 0.52;
            Decor(ShapeKind.Line, M, lineY, W - 2 * M, 0, Fill.None, new Stroke { Color = "text2@40", Width = 4 * S });
            var colW = (W - 2 * M) / n;
            var dot = 34 * S;
            for (var i = 0; i < n; i++)
            {
                var cx = M + colW * (i + 0.5);
                Decor(ShapeKind.Ellipse, cx - dot / 2, lineY - dot / 2, dot, dot, Fill.Solid("accent1"), new Stroke { Color = "bg", Width = 6 * S });
                Text("date" + (i + 1), TextRole.Label, cx - colW / 2 + 12 * S, lineY - 110 * S, colW - 24 * S, 64 * S,
                    i < _content.Details.Count ? _content.Details[i] : null, TextAlign.Center, VerticalAlign.Bottom);
                var step = Text("step" + (i + 1), TextRole.Body, cx - colW / 2 + 12 * S, lineY + 48 * S, colW - 24 * S, H - M - (lineY + 48 * S),
                    i < items.Count ? items[i] : null, TextAlign.Center);
                step.Text.Size = (n > 4 ? 32 : 38) * S;
            }
        }

        public void Comparison()
        {
            Heading();
            var gap = W * 0.04;
            var colW = (W - 2 * M - gap) / 2;
            var h = H - ContentTop - M * 0.9;
            var pad = 48 * S;
            for (var i = 0; i < 2; i++)
            {
                var x = M + i * (colW + gap);
                Decor(ShapeKind.RoundedRectangle, x, ContentTop, colW, h, Fill.Solid("bg2"), null, Math.Max(Radius, 4 * S));
                Decor(ShapeKind.Rectangle, x + pad, ContentTop + pad, 56 * S, 8 * S, Fill.Solid(i == 0 ? "accent1" : "accent2"), null, 4 * S);
                var heading = Text("heading" + (i + 1), TextRole.Heading, x + pad, ContentTop + pad + 32 * S, colW - 2 * pad, 80 * S, i == 0 ? _content.Heading1 : _content.Heading2);
                heading.Text.Size = 46 * S;
                EnsureBullets(Text(i == 0 ? "body" : "body2", TextRole.Body, x + pad, ContentTop + pad + 136 * S, colW - 2 * pad, h - 2 * pad - 136 * S,
                    i == 0 ? _content.Body : _content.Body2));
            }
        }

        public void Team()
        {
            Heading();
            var items = _content.Items;
            var n = MathEx.Clamp(Math.Max(items.Count, _content.Images.Count) == 0 ? 4 : Math.Max(items.Count, _content.Images.Count), 2, 5);
            var colW = (W - 2 * M) / n;
            var photo = Math.Min(colW * 0.64, H * 0.34);
            var top = ContentTop + H * 0.03;
            for (var i = 0; i < n; i++)
            {
                var cx = M + colW * (i + 0.5);
                Image("image" + (i + 1), cx - photo / 2, top, photo, photo, i, ImageMask.Circle);
                var name = Text("name" + (i + 1), TextRole.Body, cx - colW / 2 + 8 * S, top + photo + 28 * S, colW - 16 * S, 56 * S,
                    i < items.Count ? items[i] : null, TextAlign.Center);
                name.Text.Bold = true;
                Text("role" + (i + 1), TextRole.Caption, cx - colW / 2 + 8 * S, top + photo + 86 * S, colW - 16 * S, 80 * S,
                    i < _content.Details.Count ? _content.Details[i] : null, TextAlign.Center);
            }
        }

        public void ThankYou()
        {
            var align = Centered ? TextAlign.Center : TextAlign.Left;
            var w = Centered ? W - 2 * M : W * 0.74;
            Text("title", TextRole.Title, M, H * 0.26, w, H * 0.34, _content.Title, align, VerticalAlign.Bottom, "Thank you");
            Text("subtitle", TextRole.Subtitle, Centered ? M + W * 0.1 : M, H * 0.64, Centered ? W - 2 * M - W * 0.2 : W * 0.6, H * 0.16, _content.Subtitle, align);
        }

        private static RichText? Bullets(List<RichText> items)
        {
            if (items.Count == 0) return null;
            var text = new RichText();
            foreach (var item in items)
            foreach (var p in item.Paragraphs)
            {
                var copy = p.Clone();
                copy.List = ListKind.Bullet;
                text.Paragraphs.Add(copy);
            }
            return text;
        }

        /// <summary>Body placeholders start as bullet lists (only when they're empty; filled ones keep their formatting).</summary>
        private static void EnsureBullets(TextElement body)
        {
            if (!body.Text.Content.IsEmpty) return;
            body.Text.Content = RichText.FromPlain("", ListKind.Bullet);
        }
    }
}
