using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Core.Samples;

public sealed record SampleInfo(string Id, string Title, string Description, Func<DeckDocument> Create);

/// <summary>Sample decks with neutral, original content (start screen, tests, screenshots).</summary>
public static class DemoDecks
{
    public static IReadOnlyList<SampleInfo> All { get; } = new[]
    {
        new SampleInfo("aurora-kickoff", "Aurora Project kickoff", "A product team kicks off a new app", AuroraKickoff),
        new SampleInfo("quarterly-review", "Quarterly review", "Results, charts and a plan", QuarterlyReview),
        new SampleInfo("coffee-pitch", "Coffee shop pitch", "A small business pitch with pictures", CoffeePitch),
    };

    // ------------------------------------------------------------------ helpers

    private sealed class Builder
    {
        public Builder(string themeId, string variant, string title)
        {
            var theme = ThemeCatalog.Find(themeId)!;
            Deck = new Deck { Theme = theme.Create(variant), Title = title };
        }

        public Deck Deck { get; }
        public MediaStore Media { get; } = new();

        public Slide Add(string layout, Action<Slide> fill, TransitionKind transition = TransitionKind.Fade, string notes = "")
        {
            var slide = LayoutEngine.CreateSlide(Deck, layout);
            fill(slide);
            slide.Transition = new Transition { Kind = transition, Duration = Transition.DefaultDuration(transition) };
            slide.Notes = notes;
            Deck.Slides.Add(slide);
            return slide;
        }

        public string Picture(byte[] data) => Media.Add(data, ".jpg");

        public DeckDocument Done()
        {
            foreach (var s in Deck.Slides) DeckOperations.UpdateConnectors(s);
            return new DeckDocument(Deck, Media);
        }
    }

    private static void Set(Slide slide, string placeholder, string text)
    {
        var e = slide.Elements.FirstOrDefault(x => x.Placeholder == placeholder);
        switch (e)
        {
            case TextElement t:
                var list = t.Text.Content.Paragraphs.FirstOrDefault()?.List ?? ListKind.None;
                t.Text.Content = RichText.FromPlain(text, list);
                break;
            case ShapeElement { Text: { } body }:
                body.Content = RichText.FromPlain(text);
                break;
        }
    }

    private static void Bullets(Slide slide, string placeholder, params string[] lines)
    {
        if (slide.Elements.FirstOrDefault(x => x.Placeholder == placeholder) is TextElement t)
            t.Text.Content = RichText.FromPlain(string.Join("\n", lines), ListKind.Bullet);
    }

    private static void Image(Slide slide, string placeholder, string media)
    {
        if (slide.Elements.FirstOrDefault(x => x.Placeholder == placeholder) is ImageElement img) img.Media = media;
    }

    /// <summary>Adds an effect for the element in a placeholder (or an element itself) to the end of the slide's animation order.</summary>
    private static AnimationEffect? Animate(Slide slide, string placeholder, EffectKind kind, EffectStart start, double delay = 0, double? duration = null,
        EffectClass cls = EffectClass.Entrance, EffectDirection? direction = null)
    {
        var e = slide.Elements.FirstOrDefault(x => x.Placeholder == placeholder);
        return e == null ? null : Animate(slide, e, kind, start, delay, duration, cls, direction);
    }

    private static AnimationEffect Animate(Slide slide, Element e, EffectKind kind, EffectStart start, double delay = 0, double? duration = null,
        EffectClass cls = EffectClass.Entrance, EffectDirection? direction = null)
    {
        var effect = EffectCatalog.Create(cls, kind, e.Id);
        effect.Start = start;
        effect.Delay = delay;
        if (duration != null) effect.Duration = duration.Value;
        if (direction != null) effect.Direction = direction.Value;
        slide.Animations.Add(effect);
        return effect;
    }

    /// <summary>A file of Assets\SampleMedia (original, generated with ffmpeg: see tools\make-sample-media.ps1).</summary>
    private static byte[] SampleMedia(string name)
    {
        using var stream = typeof(DemoDecks).Assembly.GetManifestResourceStream("Stagecraft.SampleMedia." + name)
                           ?? throw new FileNotFoundException("Missing sample media " + name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static TextElement? Placeholder(Slide slide, string name) => slide.Elements.FirstOrDefault(x => x.Placeholder == name) as TextElement;

    /// <summary>Puts a link on the part of a text placeholder that says <paramref name="words"/>.</summary>
    private static void LinkWords(Slide slide, string placeholder, string words, Hyperlink link)
    {
        if (Placeholder(slide, placeholder) is not { } t) return;
        foreach (var p in t.Text.Content.Paragraphs)
        {
            var text = Editing.FindReplace.TextOf(p);
            var at = text.IndexOf(words, StringComparison.Ordinal);
            if (at < 0) continue;
            var before = text.Substring(0, at);
            var after = text.Substring(at + words.Length);
            var template = p.Runs.FirstOrDefault() ?? new TextRun();
            p.Runs.Clear();
            TextRun Piece(string s)
            {
                var r = template.Clone();
                r.Text = s;
                r.Link = null;
                return r;
            }
            if (before.Length > 0) p.Runs.Add(Piece(before));
            var linked = Piece(words);
            linked.Link = link;
            p.Runs.Add(linked);
            if (after.Length > 0) p.Runs.Add(Piece(after));
            return;
        }
    }

    private static ChartElement Chart(Deck deck, ChartKind kind, string[] categories, params (string Name, double[] Values)[] series)
    {
        var m = deck.Theme.Margin * deck.Height / 1080;
        var top = m * 0.82 + deck.Height * 0.15 + deck.Height * 0.06;
        var chart = new ChartElement
        {
            Kind = kind,
            X = m,
            Y = top,
            Width = deck.Width - 2 * m,
            Height = deck.Height - top - m * 0.9,
            Categories = categories.ToList(),
            Series = series.Select(s => new ChartSeries { Name = s.Name, Values = s.Values.ToList() }).ToList(),
            Name = "Chart",
        };
        return chart;
    }

    // ------------------------------------------------------------------ 1. Aurora Project kickoff

    public static DeckDocument AuroraKickoff()
    {
        var b = new Builder("aurora", "violet", "Aurora Project kickoff");
        var aurora = b.Picture(SampleArt.Aurora());
        var team = new[]
        {
            ("Ava Martin", "Product lead", b.Picture(SampleArt.Avatar("AM", "#7C5CFF", "#FF6B9A"))),
            ("Jonas Lee", "Design", b.Picture(SampleArt.Avatar("JL", "#00B8D9", "#6C5CE7"))),
            ("Sara Rossi", "Engineering", b.Picture(SampleArt.Avatar("SR", "#FFB547", "#FF6B9A"))),
            ("Kai Okafor", "Research", b.Picture(SampleArt.Avatar("KO", "#22C55E", "#00B8D9"))),
        };

        b.Add(LayoutIds.Title, s =>
        {
            Set(s, "title", "Aurora Project kickoff");
            Set(s, "subtitle", "Building the next version of our customer app · October 2026");
        }, notes: "Welcome everyone. Today is about why Aurora matters, what we will build and how we'll work together.");

        var agenda = b.Add(LayoutIds.Agenda, s =>
        {
            Set(s, "title", "Agenda");
            Set(s, "item1", "Why we're here");
            Set(s, "item2", "What we heard from customers");
            Set(s, "item3", "How we'll get there");
            Set(s, "item4", "Who's on the team");
        }, TransitionKind.Morph, "Four parts, about twenty minutes, then questions.");
        for (var i = 1; i <= 4; i++) Animate(agenda, "item" + i, EffectKind.Rise, i == 1 ? EffectStart.OnClick : EffectStart.WithPrevious, (i - 1) * 0.12, 0.6);

        var part1 = b.Add(LayoutIds.Section, s =>
        {
            Set(s, "label", "Part 01");
            Set(s, "title", "Why we're here");
        }, TransitionKind.Push);

        var goal = b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "3×");
            Set(s, "title", "faster onboarding is our goal");
            Set(s, "body", "New customers should reach their first success in minutes, not hours.");
        }, notes: "This is the one number to remember. Everything else supports it.");
        Animate(goal, "number", EffectKind.Zoom, EffectStart.AfterPrevious, 0.2, 0.7);
        Animate(goal, "number", EffectKind.Pulse, EffectStart.AfterPrevious, 0.1, 0.8, EffectClass.Emphasis);

        var heard = b.Add(LayoutIds.TitleContent, s =>
        {
            Set(s, "title", "What we heard from customers");
            Bullets(s, "body", "Setting up takes too many steps", "Key features are hard to find", "The phone app lags behind the desktop", "People love the reports once they find them");
        }, notes: "Quotes from 40 interviews and 1,200 survey answers.");
        var heardBody = Animate(heard, "body", EffectKind.Fade, EffectStart.OnClick, 0, 0.6);
        if (heardBody != null) heardBody.TextUnit = TextUnit.Paragraph;

        var chartSlide = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Weekly active users (thousands)"));
        chartSlide.Elements.Add(Chart(b.Deck, ChartKind.Bar, new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" },
            ("2025", new double[] { 18, 21, 24, 23, 27, 29 }), ("2026", new double[] { 26, 30, 35, 39, 44, 52 })));

        b.Add(LayoutIds.ImageRight, s =>
        {
            Image(s, "image", aurora);
            Set(s, "title", "A calmer, faster app");
            Bullets(s, "body", "One home screen for what matters", "Guided setup in under five minutes", "The same experience on every device");
        }, TransitionKind.Fade);

        // A short clip of the new app's look (original footage made with ffmpeg), played on click.
        var look = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "A first look"), TransitionKind.Fade, "The video plays on the next click (or click it). It runs for eight seconds.");
        var clipW = 1180.0;
        var clipH = clipW * 9 / 16;
        var clip = new VideoElement
        {
            Media = b.Media.Add(SampleMedia("aurora-loop.mp4"), ".mp4"),
            Poster = b.Media.Add(SampleMedia("aurora-poster.jpg"), ".jpg"),
            FileName = "aurora-loop.mp4",
            Length = 8,
            X = (b.Deck.Width - clipW) / 2,
            Y = 300,
            Width = clipW,
            Height = clipH,
            CornerRadius = 28,
            Volume = 0.7,
            FadeIn = 0.5,
            Shadow = Shadow.Lifted,
            Name = "Product video",
        };
        look.Elements.Add(clip);
        Editing.AnimationOps.SetStart(look, clip, Editing.MediaStart.OnClick);

        var timeline = b.Add(LayoutIds.Timeline, s =>
        {
            Set(s, "title", "How we'll get there");
            var dates = new[] { "Q4 2026", "Q1 2027", "Q2 2027", "Q3 2027" };
            var steps = new[] { "Discovery and research", "Design and prototypes", "Beta with 500 customers", "Launch to everyone" };
            for (var i = 0; i < 4; i++)
            {
                Set(s, "date" + (i + 1), dates[i]);
                Set(s, "step" + (i + 1), steps[i]);
            }
        }, notes: "Each phase ends with a demo to the whole company.");
        for (var i = 1; i <= 4; i++)
        {
            Animate(timeline, "date" + i, EffectKind.Rise, EffectStart.OnClick, 0, 0.6);
            Animate(timeline, "step" + i, EffectKind.Rise, EffectStart.WithPrevious, 0.1, 0.6);
        }

        var teamSlide = b.Add(LayoutIds.Team, s =>
        {
            Set(s, "title", "Who's on the team");
            for (var i = 0; i < team.Length; i++)
            {
                Set(s, "name" + (i + 1), team[i].Item1);
                Set(s, "role" + (i + 1), team[i].Item2);
                Image(s, "image" + (i + 1), team[i].Item3);
            }
        });

        b.Add(LayoutIds.Quote, s =>
        {
            Set(s, "quote", "The best product is the one our customers never have to think about.");
            Set(s, "author", "— Aurora team principle");
        });

        var thanks = b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Thank you");
            Set(s, "subtitle", "Questions and ideas: aurora-team@example.com");
        }, TransitionKind.Zoom);
        LinkWords(thanks, "subtitle", "aurora-team@example.com", Hyperlink.Email("aurora-team@example.com", "Aurora kickoff"));

        // The agenda links to its parts (Ctrl+click in the editor, a click in the show).
        if (Placeholder(agenda, "item1") is { } i1) i1.Link = Hyperlink.ToSlide(SlideJump.Specific, part1.Id);
        if (Placeholder(agenda, "item4") is { } i4) i4.Link = Hyperlink.ToSlide(SlideJump.Specific, teamSlide.Id);
        return b.Done();
    }

    // ------------------------------------------------------------------ 2. Quarterly review


    public static DeckDocument QuarterlyReview()
    {
        var b = new Builder("corporate", "blue", "Quarterly review");
        var skyline = b.Picture(SampleArt.Skyline());

        b.Add(LayoutIds.Title, s =>
        {
            Set(s, "title", "Q3 2026 quarterly review");
            Set(s, "subtitle", "Results, highlights and the plan for Q4");
        });

        var growth = b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "+28%");
            Set(s, "title", "revenue growth year over year");
            Set(s, "body", "Our strongest quarter so far, led by subscriptions and two new regions.");
        }, TransitionKind.Morph);
        Animate(growth, "number", EffectKind.Zoom, EffectStart.AfterPrevious, 0.3, 0.7);

        var line = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Monthly revenue (thousands)"));
        var lc = Chart(b.Deck, ChartKind.Line, new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep" },
            ("2025", new double[] { 410, 425, 440, 438, 455, 470, 468, 480, 495 }),
            ("2026", new double[] { 470, 490, 520, 540, 565, 590, 610, 628, 655 }));
        lc.ValueFormat = "#,0";
        lc.ShowValues = false;
        line.Elements.Add(lc);
        Animate(line, lc, EffectKind.Wipe, EffectStart.OnClick, 0, 1.2, direction: EffectDirection.Left);

        var looking = b.Add(LayoutIds.Comparison, s =>
        {
            Set(s, "title", "Looking back");
            Set(s, "heading1", "What went well");
            Bullets(s, "body", "Subscriptions grew in every region", "Support answers within two hours", "Two new partners signed");
            Set(s, "heading2", "What we'll improve");
            Bullets(s, "body2", "Onboarding still takes too long", "Hardware margins are thin", "Hiring is behind plan");
        });
        foreach (var (body, from) in new[] { ("body", EffectDirection.Left), ("body2", EffectDirection.Right) })
        {
            var fly = Animate(looking, body, EffectKind.Fly, EffectStart.OnClick, 0, 0.5, direction: from);
            if (fly != null) fly.TextUnit = TextUnit.Paragraph;
        }

        var tableSlide = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Results by region"));
        var m = b.Deck.Theme.Margin;
        var top = m * 0.82 + 1080 * 0.15 + 1080 * 0.06;
        tableSlide.Elements.Add(new TableElement
        {
            X = m,
            Y = top,
            Width = 1920 - 2 * m,
            Height = 1080 - top - m * 1.4,
            Cells = new List<List<string>>
            {
                new() { "Region", "Revenue", "Growth", "Customers" },
                new() { "North", "$1.24M", "+31%", "420" },
                new() { "South", "$0.96M", "+24%", "355" },
                new() { "East", "$0.88M", "+35%", "298" },
                new() { "West", "$1.10M", "+22%", "387" },
            },
            FirstColumnBold = true,
            Name = "Results table",
        });

        var donutSlide = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Revenue by product"));
        var donut = Chart(b.Deck, ChartKind.Donut, new[] { "Subscriptions", "Services", "Hardware" }, ("Total revenue", new double[] { 1240, 575, 325 }));
        donut.ValueFormat = "'$'#,0'K'";
        donutSlide.Elements.Add(donut);
        Animate(donutSlide, donut, EffectKind.Wheel, EffectStart.AfterPrevious, 0.2, 1.0);

        b.Add(LayoutIds.ImageRight, s =>
        {
            Image(s, "image", skyline);
            Set(s, "title", "Focus for Q4");
            Bullets(s, "body", "Launch the new onboarding flow", "Open the second office", "Hire twelve engineers", "Grow the partner programme");
        });

        var end = b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Thank you");
            Set(s, "subtitle", "Results by region are on slide 5");
        });
        LinkWords(end, "subtitle", "slide 5", Hyperlink.ToSlide(SlideJump.Specific, tableSlide.Id));
        return b.Done();
    }

    // ------------------------------------------------------------------ 3. Coffee shop pitch

    public static DeckDocument CoffeePitch()
    {
        var b = new Builder("botanical", "desert", "Coffee shop pitch");
        var coffee = b.Picture(SampleArt.Coffee());
        var dawn = b.Picture(SampleArt.Dawn(sun: false)); // the slide animates its own sun

        var cover = b.Add(LayoutIds.ImageFull, s =>
        {
            Image(s, "image", coffee);
            Set(s, "title", "Ember & Oak Coffee");
            Set(s, "subtitle", "A neighbourhood café with a roastery inside");
        }, notes: "Click the speaker for some quiet music; it keeps playing for the next three slides.");
        // Calm background music (an original synthesized pad), started by clicking its speaker.
        cover.Elements.Add(new AudioElement
        {
            Media = b.Media.Add(SampleMedia("soft-pad.m4a"), ".m4a"),
            FileName = "soft-pad.m4a",
            Length = 16,
            X = b.Deck.Width - 150,
            Y = b.Deck.Height - 150,
            Width = 84,
            Height = 84,
            AcrossSlides = true,
            StopAfterSlides = 3,
            Volume = 0.6,
            FadeIn = 1,
            FadeOut = 2,
            Color = "#FFFFFF@85",
            Name = "Background music",
        });

        var opportunity = b.Add(LayoutIds.TitleContent, s =>
        {
            Set(s, "title", "The opportunity");
            Bullets(s, "body", "A growing neighbourhood with no specialty café", "Commuters pass by every morning", "Local offices want a place to meet");
        });
        var rise = Animate(opportunity, "body", EffectKind.Rise, EffectStart.OnClick, 0, 0.6);
        if (rise != null) rise.TextUnit = TextUnit.Paragraph;

        var locals = b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "1 in 3");
            Set(s, "title", "locals buy coffee every day");
            Set(s, "body", "Yet the nearest specialty café is a 15-minute walk away.");
        }, TransitionKind.Morph);
        Animate(locals, "number", EffectKind.GrowTurn, EffectStart.AfterPrevious, 0.2, 0.8);

        b.Add(LayoutIds.Comparison, s =>
        {
            Set(s, "title", "What makes us different");
            Set(s, "heading1", "Our menu");
            Bullets(s, "body", "Beans roasted on site every week", "Seasonal drinks and fresh pastries", "Oat and almond milk at no extra cost");
            Set(s, "heading2", "The experience");
            Bullets(s, "body2", "Big shared tables and quiet corners", "Coffee workshops on weekends", "A zero-waste counter");
        });

        var visitors = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Projected visitors per day"));
        var vc = Chart(b.Deck, ChartKind.Bar, new[] { "Month 1", "Month 2", "Month 3", "Month 4", "Month 5", "Month 6" }, ("Visitors", new double[] { 120, 165, 210, 240, 275, 310 }));
        vc.ShowLegend = false;
        visitors.Elements.Add(vc);
        Animate(visitors, vc, EffectKind.Wipe, EffectStart.OnClick, 0, 1.0, direction: EffectDirection.Bottom);

        var morning = b.Add(LayoutIds.ImageLeft, s =>
        {
            Image(s, "image", dawn);
            Set(s, "title", "Open from 7 am");
            Bullets(s, "body", "Morning rush: grab-and-go window", "Afternoons: meetings and study", "Evenings: tastings twice a month");
        });
        // The sun rises over the picture along a curved motion path.
        if (morning.Elements.FirstOrDefault(e => e.Placeholder == "image") is { } picture)
        {
            var sun = new ShapeElement
            {
                Shape = ShapeKind.Ellipse,
                Fill = Fill.Radial("#FFE6A8", "#FFB547"),
                Stroke = null,
                Shadow = new Shadow { Color = "#FFB547@70", Blur = 46, Distance = 0, Angle = 90 },
                Width = 130,
                Height = 130,
                X = picture.X + picture.Width * 0.1,
                Y = picture.Y + picture.Height * 0.36,
                Name = "Sun",
            };
            morning.Elements.Insert(morning.Elements.IndexOf(picture) + 1, sun);
            var path = Animate(morning, sun, EffectKind.Custom, EffectStart.AfterPrevious, 0.3, 2.6, EffectClass.Motion);
            path.Path = new MotionPath
            {
                Smooth = true,
                Points = { new PathPoint(0, 0), new PathPoint(picture.Width * 0.24, -picture.Height * 0.15), new PathPoint(picture.Width * 0.5, -picture.Height * 0.22) },
            };
            path.Easing = EffectEasing.EaseOut;
        }

        b.Add(LayoutIds.Team, s =>
        {
            Set(s, "title", "The founders");
            var people = new[]
            {
                ("Lena Ortiz", "Head roaster", SampleArt.Avatar("LO", "#B5562D", "#D9A441")),
                ("Theo Park", "Operations", SampleArt.Avatar("TP", "#5E7D4E", "#4F7C8A")),
                ("Mira Haddad", "Community", SampleArt.Avatar("MH", "#8A6B5A", "#B5562D")),
            };
            for (var i = 0; i < people.Length; i++)
            {
                Set(s, "name" + (i + 1), people[i].Item1);
                Set(s, "role" + (i + 1), people[i].Item2);
                Image(s, "image" + (i + 1), b.Picture(people[i].Item3));
            }
            // Three founders: drop the fourth slot.
            s.Elements.RemoveAll(e => e.Placeholder is "image4" or "name4" or "role4");
            LayoutEngine.Reflow(b.Deck, s);
        });

        b.Add(LayoutIds.Quote, s =>
        {
            Set(s, "quote", "Great coffee is a daily ritual, not a luxury.");
            Set(s, "author", "— From our founders' notebook");
        });

        var brew = b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Let's brew something together");
            Set(s, "subtitle", "hello@example.com · ember-and-oak.example");
        });
        LinkWords(brew, "subtitle", "hello@example.com", Hyperlink.Email("hello@example.com", "Ember & Oak"));
        return b.Done();
    }
}
