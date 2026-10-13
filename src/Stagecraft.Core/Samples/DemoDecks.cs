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

    private static void Build(Slide slide, string placeholder, BuildEffect effect, int step, double delay = 0)
    {
        var e = slide.Elements.FirstOrDefault(x => x.Placeholder == placeholder);
        if (e != null) e.Build = new Build { Effect = effect, Step = step, Delay = delay, Duration = 0.6 };
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
        for (var i = 1; i <= 4; i++) Build(agenda, "item" + i, BuildEffect.Rise, 1, (i - 1) * 0.12);

        b.Add(LayoutIds.Section, s =>
        {
            Set(s, "label", "Part 01");
            Set(s, "title", "Why we're here");
        }, TransitionKind.Push);

        b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "3×");
            Set(s, "title", "faster onboarding is our goal");
            Set(s, "body", "New customers should reach their first success in minutes, not hours.");
        }, notes: "This is the one number to remember. Everything else supports it.");

        var heard = b.Add(LayoutIds.TitleContent, s =>
        {
            Set(s, "title", "What we heard from customers");
            Bullets(s, "body", "Setting up takes too many steps", "Key features are hard to find", "The phone app lags behind the desktop", "People love the reports once they find them");
        }, notes: "Quotes from 40 interviews and 1,200 survey answers.");
        Build(heard, "body", BuildEffect.Fade, 1);

        var chartSlide = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Weekly active users (thousands)"));
        chartSlide.Elements.Add(Chart(b.Deck, ChartKind.Bar, new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" },
            ("2025", new double[] { 18, 21, 24, 23, 27, 29 }), ("2026", new double[] { 26, 30, 35, 39, 44, 52 })));

        b.Add(LayoutIds.ImageRight, s =>
        {
            Image(s, "image", aurora);
            Set(s, "title", "A calmer, faster app");
            Bullets(s, "body", "One home screen for what matters", "Guided setup in under five minutes", "The same experience on every device");
        }, TransitionKind.Fade);

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
            Build(timeline, "date" + i, BuildEffect.Rise, i);
            Build(timeline, "step" + i, BuildEffect.Rise, i, 0.1);
        }

        b.Add(LayoutIds.Team, s =>
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

        b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Thank you");
            Set(s, "subtitle", "Questions and ideas: aurora-team@example.com");
        }, TransitionKind.Zoom);
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

        b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "+28%");
            Set(s, "title", "revenue growth year over year");
            Set(s, "body", "Our strongest quarter so far, led by subscriptions and two new regions.");
        }, TransitionKind.Morph);

        var line = b.Add(LayoutIds.TitleOnly, s => Set(s, "title", "Monthly revenue (thousands)"));
        var lc = Chart(b.Deck, ChartKind.Line, new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep" },
            ("2025", new double[] { 410, 425, 440, 438, 455, 470, 468, 480, 495 }),
            ("2026", new double[] { 470, 490, 520, 540, 565, 590, 610, 628, 655 }));
        lc.ValueFormat = "#,0";
        lc.ShowValues = false;
        line.Elements.Add(lc);

        b.Add(LayoutIds.Comparison, s =>
        {
            Set(s, "title", "Looking back");
            Set(s, "heading1", "What went well");
            Bullets(s, "body", "Subscriptions grew in every region", "Support answers within two hours", "Two new partners signed");
            Set(s, "heading2", "What we'll improve");
            Bullets(s, "body2", "Onboarding still takes too long", "Hardware margins are thin", "Hiring is behind plan");
        });

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

        b.Add(LayoutIds.ImageRight, s =>
        {
            Image(s, "image", skyline);
            Set(s, "title", "Focus for Q4");
            Bullets(s, "body", "Launch the new onboarding flow", "Open the second office", "Hire twelve engineers", "Grow the partner programme");
        });

        b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Thank you");
            Set(s, "subtitle", "Full numbers are in the appendix");
        });
        return b.Done();
    }

    // ------------------------------------------------------------------ 3. Coffee shop pitch

    public static DeckDocument CoffeePitch()
    {
        var b = new Builder("botanical", "desert", "Coffee shop pitch");
        var coffee = b.Picture(SampleArt.Coffee());
        var dawn = b.Picture(SampleArt.Dawn());

        b.Add(LayoutIds.ImageFull, s =>
        {
            Image(s, "image", coffee);
            Set(s, "title", "Ember & Oak Coffee");
            Set(s, "subtitle", "A neighbourhood café with a roastery inside");
        });

        var opportunity = b.Add(LayoutIds.TitleContent, s =>
        {
            Set(s, "title", "The opportunity");
            Bullets(s, "body", "A growing neighbourhood with no specialty café", "Commuters pass by every morning", "Local offices want a place to meet");
        });
        Build(opportunity, "body", BuildEffect.Rise, 1);

        b.Add(LayoutIds.BigNumber, s =>
        {
            Set(s, "number", "1 in 3");
            Set(s, "title", "locals buy coffee every day");
            Set(s, "body", "Yet the nearest specialty café is a 15-minute walk away.");
        }, TransitionKind.Morph);

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

        b.Add(LayoutIds.ImageLeft, s =>
        {
            Image(s, "image", dawn);
            Set(s, "title", "Open from 7 am");
            Bullets(s, "body", "Morning rush: grab-and-go window", "Afternoons: meetings and study", "Evenings: tastings twice a month");
        });

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

        b.Add(LayoutIds.ThankYou, s =>
        {
            Set(s, "title", "Let's brew something together");
            Set(s, "subtitle", "hello@example.com");
        });
        return b.Done();
    }
}
