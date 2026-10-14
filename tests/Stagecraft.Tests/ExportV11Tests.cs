using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Export;
using Stagecraft.Core.Import;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Themes;

namespace Stagecraft.Tests;

/// <summary>Export of 1.1's additions: animations, links, video and sound, header and footer (.pptx and PDF).</summary>
public class ExportV11Tests
{
    private readonly ITestOutputHelper _output;

    public ExportV11Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Three slides: every kind of animation (with triggers, text by paragraph and word, motion paths), links of every
    /// kind, and media (an embedded video and sound, a linked video).
    /// </summary>
    internal static (Deck Deck, MediaStore Media) RichDeck(string? linkedVideo = null)
    {
        var deck = DeckOperations.NewDeck(ThemeCatalog.Find("aurora"));
        deck.Slides.Clear();
        var media = new MediaStore();

        // 1: animations
        var s1 = new Slide { Layout = "blank" };
        var title = Text("Animated title", 100, 80, 1200, 160);
        var body = Text("First point\nSecond point\nThird point", 100, 300, 1000, 400);
        body.Text.Content = RichText.FromPlain("First point\nSecond point\nThird point", ListKind.Bullet);
        var words = Text("Word by word", 1200, 300, 600, 120);
        var box = new ShapeElement { Shape = ShapeKind.RoundedRectangle, X = 1300, Y = 600, Width = 300, Height = 200, Fill = Fill.Solid("accent2") };
        var ball = new ShapeElement { Shape = ShapeKind.Ellipse, X = 200, Y = 800, Width = 120, Height = 120, Fill = Fill.Solid("accent1") };
        var trigger = new ShapeElement { Shape = ShapeKind.Rectangle, X = 1600, Y = 900, Width = 200, Height = 100, Fill = Fill.Solid("accent3"), Text = new TextBody { Content = RichText.FromPlain("Click me") } };
        s1.Elements.AddRange(new Element[] { title, body, words, box, ball, trigger });
        void Add(Element target, EffectClass cls, EffectKind kind, EffectStart start = EffectStart.OnClick, Action<AnimationEffect>? more = null)
        {
            var a = EffectCatalog.Create(cls, kind, target.Id);
            a.Start = start;
            more?.Invoke(a);
            s1.Animations.Add(a);
        }
        Add(title, EffectClass.Entrance, EffectKind.Fade, EffectStart.WithPrevious);
        Add(body, EffectClass.Entrance, EffectKind.Fly, more: a => a.TextUnit = TextUnit.Paragraph);
        Add(words, EffectClass.Entrance, EffectKind.Float, EffectStart.AfterPrevious, a => a.TextUnit = TextUnit.Word);
        Add(box, EffectClass.Emphasis, EffectKind.Spin, more: a => a.Repeat = 2);
        Add(box, EffectClass.Emphasis, EffectKind.Grow, EffectStart.WithPrevious, a => a.Easing = EffectEasing.EaseInOut);
        Add(box, EffectClass.Emphasis, EffectKind.Pulse, EffectStart.AfterPrevious, a => a.RepeatUntil = RepeatUntil.NextClick);
        Add(box, EffectClass.Emphasis, EffectKind.Transparency);
        Add(box, EffectClass.Emphasis, EffectKind.Teeter, more: a => a.Rewind = true);
        foreach (var k in EffectCatalog.EntranceKinds) Add(ball, EffectClass.Entrance, k, EffectStart.AfterPrevious);
        foreach (var k in EffectCatalog.KindsOf(EffectClass.Exit)) Add(ball, EffectClass.Exit, k, EffectStart.AfterPrevious);
        Add(ball, EffectClass.Motion, EffectKind.Line, more: a => a.Path = EffectCatalog.DefaultPath(EffectKind.Line, 120, 120));
        Add(ball, EffectClass.Motion, EffectKind.Arc, more: a => a.Path = EffectCatalog.DefaultPath(EffectKind.Arc, 120, 120));
        Add(ball, EffectClass.Motion, EffectKind.Circle, more: a => a.Path = EffectCatalog.DefaultPath(EffectKind.Circle, 120, 120));
        Add(box, EffectClass.Exit, EffectKind.Wipe, more: a => a.Trigger = trigger.Id);
        deck.Slides.Add(s1);

        // 2: links
        var s2 = new Slide { Layout = "blank" };
        var linked = Text("Visit the site or write to us", 100, 100, 1200, 120);
        var p = linked.Text.Content.Paragraphs[0];
        p.Runs.Clear();
        p.Runs.Add(new TextRun { Text = "Visit " });
        p.Runs.Add(new TextRun { Text = "the site", Link = Hyperlink.Web("https://example.com/page?a=1&b=2", "Our site") });
        p.Runs.Add(new TextRun { Text = " or " });
        p.Runs.Add(new TextRun { Text = "write to us", Link = Hyperlink.Email("hello@example.com", "Hi there") });
        var toThird = new ShapeElement { Shape = ShapeKind.Rectangle, X = 100, Y = 400, Width = 300, Height = 160, Fill = Fill.Solid("accent1"), Link = Hyperlink.ToSlide(SlideJump.Specific) };
        var file = new ShapeElement { Shape = ShapeKind.Ellipse, X = 500, Y = 400, Width = 200, Height = 200, Fill = Fill.Solid("accent2"), Link = Hyperlink.ToFile(@"C:\Docs\report.pdf") };
        s2.Elements.AddRange(new Element[] { linked, toThird, file });
        var x = 800.0;
        foreach (var (kind, jump) in new[] { (ActionButtonKind.Previous, SlideJump.Previous), (ActionButtonKind.Next, SlideJump.Next), (ActionButtonKind.First, SlideJump.First), (ActionButtonKind.Last, SlideJump.Last), (ActionButtonKind.Return, SlideJump.LastViewed) })
        {
            s2.Elements.Add(new ShapeElement { Shape = ShapeKind.ActionButton, Action = kind, X = x, Y = 800, Width = 108, Height = 108, Fill = Fill.Solid("accent1"), Link = Hyperlink.ToSlide(jump) });
            x += 140;
        }
        deck.Slides.Add(s2);

        // 3: media
        var s3 = new Slide { Layout = "blank" };
        var video = new VideoElement
        {
            X = 100, Y = 100, Width = 960, Height = 540, Media = media.AddFile(TestMedia.Clip), FileName = "test-clip.mp4", Length = 2,
            TrimStart = 0.5, TrimEnd = 1.5, FadeIn = 0.3, FadeOut = 0.4, CornerRadius = 24, Volume = 0.6,
            Poster = media.Add(Decks.Png(160, 90), ".png"),
        };
        var sound = new AudioElement { X = 1700, Y = 900, Width = 96, Height = 96, Media = media.AddFile(TestMedia.Mp3), FileName = "test-tone.mp3", Length = 1, AcrossSlides = true, HideIcon = true, Loop = true };
        s3.Elements.Add(video);
        s3.Elements.Add(sound);
        if (linkedVideo != null)
            s3.Elements.Add(new VideoElement { X = 1100, Y = 100, Width = 640, Height = 360, LinkPath = linkedVideo, LinkAbsolute = linkedVideo, FileName = Path.GetFileName(linkedVideo), Length = 3 });
        s3.Elements.Add(new OnlineVideoElement { X = 1100, Y = 600, Width = 480, Height = 270, Url = "https://www.youtube.com/watch?v=abc123", Title = "A talk", Provider = "YouTube" });
        AnimationOps.SetStart(s3, video, MediaStart.OnClick);
        AnimationOps.SetStart(s3, sound, MediaStart.Automatically);
        deck.Slides.Add(s3);

        toThird.Link!.SlideId = s3.Id;
        deck.Footer = new HeaderFooter { SlideNumber = true, Date = true, Text = "Stagecraft tests", HideOnTitle = false };
        return (deck, media);
    }

    private static TextElement Text(string text, double x, double y, double w, double h) =>
        new() { X = x, Y = y, Width = w, Height = h, Text = new TextBody { Role = TextRole.Body, Content = RichText.FromPlain(text) } };

    private MemoryStream ExportPptx(Deck deck, MediaStore media)
    {
        var ms = new MemoryStream();
        PptxExporter.Export(deck, media, ms, new PptxExportOptions());
        ms.Position = 0;
        return ms;
    }

    private void AssertValid(MemoryStream ms)
    {
        ms.Position = 0;
        using (var doc = PresentationDocument.Open(ms, false))
        {
            var errors = new OpenXmlValidator(FileFormatVersions.Office2016).Validate(doc).ToList();
            foreach (var e in errors.Take(30)) _output.WriteLine($"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}");
            Assert.Empty(errors);
        }
        ms.Position = 0;
    }

    private static string Xml(SlidePart part)
    {
        using var reader = new StreamReader(part.GetStream());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Pptx_AnimationsLinksAndMedia_AreValidAndPresent() => Sta.Run(() =>
    {
        var (deck, media) = RichDeck(linkedVideo: TestMedia.Clip);
        using var ms = ExportPptx(deck, media);
        AssertValid(ms);
        using var doc = PresentationDocument.Open(ms, false);
        var parts = doc.PresentationPart!.SlideParts.ToList();
        var order = doc.PresentationPart.Presentation!.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(id => (SlidePart)doc.PresentationPart.GetPartById(id.RelationshipId!.Value!)).ToList();

        // Animations
        var a = Xml(order[0]);
        Assert.Contains("nodeType=\"mainSeq\"", a);
        Assert.Contains("presetID=\"10\" presetClass=\"entr\"", a); // fade
        Assert.Contains("presetID=\"2\" presetClass=\"entr\"", a); // fly
        Assert.Contains("presetID=\"22\" presetClass=\"entr\"", a); // wipe
        Assert.Contains("presetID=\"53\" presetClass=\"entr\"", a); // zoom
        Assert.Contains("presetID=\"1\" presetClass=\"entr\"", a); // appear
        Assert.Contains("presetClass=\"exit\"", a);
        Assert.Contains("<p:animRot by=\"21600000\"", a); // spin 360°
        Assert.Contains("<p:animScale>", a); // grow and pulse
        Assert.Contains("<p:animMotion", a);
        Assert.Contains("<p:iterate type=\"wd\">", a); // word by word
        Assert.Contains("<p:pRg st=\"2\" end=\"2\"/>", a); // the third paragraph
        Assert.Contains("build=\"p\"", a);
        Assert.Contains("nodeType=\"interactiveSeq\"", a); // trigger
        Assert.Contains("repeatCount=\"indefinite\"", a);
        Assert.Contains("fill=\"remove\"", a); // rewind

        // Links
        var l = order[1];
        var lx = Xml(l);
        var external = l.HyperlinkRelationships.Select(r => r.Uri.OriginalString).ToList();
        Assert.Contains("https://example.com/page?a=1&b=2", external);
        Assert.Contains(external, u => u.StartsWith("mailto:hello@example.com?subject=Hi%20there", StringComparison.Ordinal));
        Assert.Contains(external, u => u.Contains("report.pdf"));
        Assert.Contains("tooltip=\"Our site\"", lx);
        foreach (var jump in new[] { "nextslide", "previousslide", "firstslide", "lastslide", "lastslideviewed" })
            Assert.Contains("ppaction://hlinkshowjump?jump=" + jump, lx);
        Assert.Contains("ppaction://hlinksldjump", lx);
        Assert.Contains(l.Parts, pp => pp.OpenXmlPart == order[2]); // the jump's relationship to slide 3

        // Media
        var m = order[2];
        var mx = Xml(m);
        Assert.Equal(2, doc.DataParts.OfType<MediaDataPart>().Count()); // video and sound, embedded once
        Assert.Contains(m.DataPartReferenceRelationships, r => r is VideoReferenceRelationship);
        Assert.Contains(m.DataPartReferenceRelationships, r => r is AudioReferenceRelationship);
        Assert.Contains(m.DataPartReferenceRelationships, r => r is MediaReferenceRelationship);
        Assert.Contains(m.ExternalRelationships, r => r.RelationshipType.EndsWith("/video", StringComparison.Ordinal)); // the linked one
        Assert.Contains("<p14:trim st=\"500\" end=\"500\"/>", mx);
        Assert.Contains("<p14:fade in=\"300\" out=\"400\"/>", mx);
        Assert.Contains("cmd=\"playFrom(0.5)\"", mx);
        Assert.Contains("<p:video>", mx);
        Assert.Contains("<p:audio>", mx);
        Assert.Contains("numSld=\"999\"", mx);
        Assert.Contains("showWhenStopped=\"0\"", mx);
        Assert.Contains("youtube.com", string.Join(" ", m.HyperlinkRelationships.Select(r => r.Uri.ToString())));

        // Footer
        Assert.Contains("type=\"slidenum\"", mx);
        Assert.Contains("Stagecraft tests", mx);
    });

    [Fact]
    public void Pptx_EveryEffect_OnItsOwn_IsValid() => Sta.Run(() =>
    {
        foreach (var info in EffectCatalog.All())
        {
            if (info.Class == EffectClass.Media) continue;
            var deck = DeckOperations.NewDeck(ThemeCatalog.Find("corporate"));
            deck.Slides.Clear();
            var slide = new Slide { Layout = "blank" };
            var t = Text("One\nTwo", 100, 100, 600, 300);
            slide.Elements.Add(t);
            var effect = EffectCatalog.Create(info.Class, info.Kind, t.Id);
            if (info.Class == EffectClass.Motion) effect.Path = EffectCatalog.DefaultPath(info.Kind, 600, 300);
            slide.Animations.Add(effect);
            deck.Slides.Add(slide);
            using var ms = ExportPptx(deck, new MediaStore());
            AssertValid(ms);
        }
    });

    [Fact]
    public void Pdf_LinkAnnotations_PointAtUrlsAndPages() => Sta.Run(() =>
    {
        var (deck, media) = RichDeck(linkedVideo: TestMedia.Clip);
        using var ms = new MemoryStream();
        PdfExporter.Export(deck, media, ms, new PdfExportOptions());
        ms.Position = 0;
        using var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Equal(3, pdf.PageCount);

        var uris = new List<string>();
        var gotoPages = new List<int>();
        var launches = new List<string>();
        var rects = new List<PdfRectangle>();
        var page = pdf.Pages[1];
        var annotations = page.Elements.GetArray("/Annots");
        Assert.NotNull(annotations);
        foreach (var item in annotations!.Elements)
        {
            var annot = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;
            Assert.NotNull(annot);
            Assert.Equal("/Link", annot!.Elements.GetName("/Subtype"));
            rects.Add(annot.Elements.GetRectangle("/Rect"));
            var action = annot.Elements.GetDictionary("/A");
            if (action != null)
            {
                var kind = action.Elements.GetName("/S");
                if (kind == "/URI") uris.Add(action.Elements.GetString("/URI"));
                if (kind == "/Launch") launches.Add(FileOf(action.Elements["/F"]));
                if (kind == "/GoTo" && action.Elements["/D"] is { } d) gotoPages.Add(PageOf(pdf, d));
            }
            var dest = annot.Elements["/Dest"];
            if (dest != null) gotoPages.Add(PageOf(pdf, dest));
        }
        foreach (var u in uris) _output.WriteLine("uri " + u);
        Assert.Contains("https://example.com/page?a=1&b=2", uris);
        Assert.Contains(uris, u => u.StartsWith("mailto:hello@example.com", StringComparison.Ordinal));
        Assert.Contains(2, gotoPages); // the specific-slide link and "last slide" → page 3 (index 2)
        Assert.Contains(0, gotoPages); // previous and first → page 1
        Assert.Contains(launches, f => f.Contains("report.pdf"));
        // Rectangles are inside the page, in PDF coordinates (from the bottom).
        var height = deck.Height * PdfExporter.PointsPerUnit;
        Assert.All(rects, r => Assert.InRange(r.Y1, 0, height));
        // The shape linking to slide 3 sits at y 400..560 units: 280..200 pt from the top.
        Assert.Contains(rects, r => Math.Abs(r.Y2 - (height - 200)) < 1 && Math.Abs(r.Y1 - (height - 280)) < 1);

        // Slide 3: the online video page and the linked video's file.
        var third = pdf.Pages[2].Elements.GetArray("/Annots")!;
        var all = string.Join(" ", third.Elements.Select(e => ((e as PdfReference)?.Value as PdfDictionary)?.Elements.GetDictionary("/A")?.ToString() ?? ""));
        _output.WriteLine(all);
        Assert.Contains("youtube.com", all);
        Assert.Contains("test-clip.mp4", all);
    });


    [Fact]
    public void Pptx_Import_OwnExport_KeepsAnimationsLinksAndMedia() => Sta.Run(() =>
    {
        var (deck, media) = RichDeck(linkedVideo: TestMedia.Clip);
        using var ms = ExportPptx(deck, media);
        var doc = PptxImporter.Import(ms);
        foreach (var w in doc.Warnings) _output.WriteLine("warning: " + w);
        Assert.Equal(3, doc.Deck.Slides.Count);

        // Animations: same count of effects on slide 1 (paragraph effects merged back), kinds that PowerPoint has kept.
        var a = doc.Deck.Slides[0].Animations;
        var original = deck.Slides[0].Animations;
        Assert.Equal(original.Count, a.Count);
        Assert.Contains(a, e => e.Class == EffectClass.Entrance && e.Effect == EffectKind.Fly && e.TextUnit == TextUnit.Paragraph);
        Assert.Contains(a, e => e.TextUnit == TextUnit.Word);
        Assert.Contains(a, e => e.Effect == EffectKind.Spin && Math.Abs(e.EffectiveAmount - 360) < 0.5 && e.Repeat == 2);
        Assert.Contains(a, e => e.Effect == EffectKind.Grow && e.Easing == EffectEasing.EaseInOut);
        Assert.Contains(a, e => e.Effect == EffectKind.Pulse && e.RepeatUntil == RepeatUntil.NextClick);
        Assert.Contains(a, e => e.Effect == EffectKind.Transparency);
        Assert.Contains(a, e => e.Rewind);
        Assert.Contains(a, e => e.Class == EffectClass.Exit && e.Effect == EffectKind.Wipe && e.Trigger != null);
        var motions = a.Where(e => e.Class == EffectClass.Motion).ToList();
        Assert.Equal(3, motions.Count);
        Assert.Contains(motions, m => m.Path!.Closed);
        // The line path ends where it ended (slide units, a unit or two of rounding).
        var line = original.First(e => e.Effect == EffectKind.Line).Path!.Points;
        var back = motions.First(m => m.Effect == EffectKind.Line).Path!.Points;
        Assert.Equal(line[line.Count - 1].X - line[0].X, back[back.Count - 1].X, 0);
        Assert.Equal(line[line.Count - 1].Y - line[0].Y, back[back.Count - 1].Y, 0);
        // Starts: the first effect plays with the slide, then clicks.
        Assert.Equal(EffectStart.WithPrevious, a[0].Start);
        Assert.Equal(EffectStart.OnClick, a[1].Start);

        // Links
        var s2 = doc.Deck.Slides[1];
        var runs = s2.Elements.OfType<TextElement>().SelectMany(t => t.Text.Content.Paragraphs).SelectMany(p => p.Runs).ToList();
        Assert.Contains(runs, r => r.Link is { Kind: LinkKind.Web, Address: "https://example.com/page?a=1&b=2", ScreenTip: "Our site" });
        Assert.Contains(runs, r => r.Link is { Kind: LinkKind.Email, Address: "hello@example.com", Subject: "Hi there" });
        var links = s2.Elements.Where(e => e.Link != null).Select(e => e.Link!).ToList();
        Assert.Contains(links, l => l.Kind == LinkKind.Slide && l.Jump == SlideJump.Specific && l.SlideId == doc.Deck.Slides[2].Id);
        Assert.Contains(links, l => l.Kind == LinkKind.File && l.Address.EndsWith("report.pdf", StringComparison.OrdinalIgnoreCase));
        foreach (var jump in new[] { SlideJump.Next, SlideJump.Previous, SlideJump.First, SlideJump.Last, SlideJump.LastViewed })
            Assert.Contains(links, l => l.Kind == LinkKind.Slide && l.Jump == jump);

        // Media
        var s3 = doc.Deck.Slides[2];
        var videos = s3.Elements.OfType<VideoElement>().ToList();
        Assert.Equal(2, videos.Count);
        var embedded = videos.Single(v => v.Media != null);
        Assert.Equal(File.ReadAllBytes(TestMedia.Clip), File.ReadAllBytes(doc.Media.ResolvePlayback(embedded)!));
        Assert.Equal(0.5, embedded.TrimStart, 2);
        Assert.NotNull(embedded.Length);
        Assert.Equal(1.5, embedded.TrimEnd!.Value, 2);
        Assert.Equal(0.3, embedded.FadeIn, 2);
        Assert.Equal(0.4, embedded.FadeOut, 2);
        Assert.Equal(0.6, embedded.Volume, 2);
        Assert.NotNull(embedded.Poster);
        Assert.True(embedded.CornerRadius > 0);
        var linkedClip = videos.Single(v => v.Media == null);
        Assert.Equal(TestMedia.Clip, linkedClip.LinkAbsolute);
        var sound = s3.Elements.OfType<AudioElement>().Single();
        Assert.True(sound.AcrossSlides);
        Assert.True(sound.HideIcon);
        Assert.True(sound.Loop);
        Assert.Equal(File.ReadAllBytes(TestMedia.Mp3), File.ReadAllBytes(doc.Media.ResolvePlayback(sound)!));
        // Play on click for the video, automatically for the sound.
        Assert.Equal(MediaStart.OnClick, AnimationOps.StartOf(s3, embedded));
        Assert.Equal(MediaStart.Automatically, AnimationOps.StartOf(s3, sound));
    });

    [Fact]
    public void MediaDuration_ReadsMp4AndWavHeaders()
    {
        Assert.Equal(2.0, Stagecraft.Core.IO.MediaDuration.TryRead(TestMedia.Clip)!.Value, 2);
        Assert.Equal(0.5, Stagecraft.Core.IO.MediaDuration.TryRead(TestMedia.Wav)!.Value, 2);
        Assert.Null(Stagecraft.Core.IO.MediaDuration.TryRead(TestMedia.Mp3)); // MP3: measured when played
        Assert.Null(Stagecraft.Core.IO.MediaDuration.TryRead(TestMedia.File("missing.mp4")));
    }

    /// <summary>
    /// Not a check by itself: with STAGECRAFT_PPT_CHECK set to a folder, writes the rich deck there (for opening in
    /// PowerPoint) and imports "from-powerpoint.pptx" from it if PowerPoint made one, printing what came over.
    /// </summary>
    [Fact]
    public void Pptx_PowerPointCheck_WhenAsked() => Sta.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable("STAGECRAFT_PPT_CHECK");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var (deck, media) = RichDeck(linkedVideo: TestMedia.Clip);
        PptxExporter.Export(deck, media, Path.Combine(folder!, "stagecraft-export.pptx"), new PptxExportOptions());
        var fromPowerPoint = Path.Combine(folder!, "from-powerpoint.pptx");
        if (!File.Exists(fromPowerPoint)) return;
        var doc = PptxImporter.Import(fromPowerPoint);
        var report = new List<string>();
        foreach (var w in doc.Warnings) report.Add("warning: " + w);
        for (var i = 0; i < doc.Deck.Slides.Count; i++)
        {
            var s = doc.Deck.Slides[i];
            report.Add($"slide {i + 1}: {s.Elements.Count} elements, {s.Animations.Count} effects");
            foreach (var a in s.Animations)
                report.Add($"  {a.Class} {a.Effect} {a.Direction} start={a.Start} delay={a.Delay} dur={a.Duration} unit={a.TextUnit} clicks={a.ParagraphClicks} trigger={(a.Trigger != null)} amount={a.Amount} repeat={a.Repeat}/{a.RepeatUntil}");
            foreach (var e in s.Elements)
                report.Add($"  {e.KindName} link={e.Link?.Kind}:{e.Link?.Address}{e.Link?.Jump} " + (e is MediaClipElement m ? $"media={m.Media} linked={m.LinkPath} len={m.Length} trim={m.TrimStart}-{m.TrimEnd} vol={m.Volume} loop={m.Loop}" : ""));
            foreach (var r in s.Elements.SelectMany(FindReplace.Bodies).SelectMany(b => b.Content.Paragraphs).SelectMany(p => p.Runs).Where(r => r.Link != null))
                report.Add($"  run '{r.Text}' → {r.Link!.Kind} {r.Link.Address} {r.Link.Jump} {r.Link.ScreenTip}");
        }
        File.WriteAllLines(Path.Combine(folder!, "import-report.txt"), report);
    });

    /// <summary>With STAGECRAFT_WRITE_SAMPLES set to a folder, saves the sample decks there (the repository's samples\).</summary>
    [Fact]
    public void Samples_WriteFiles_WhenAsked() => Sta.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable("STAGECRAFT_WRITE_SAMPLES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var sample in Stagecraft.Core.Samples.DemoDecks.All)
        {
            var doc = sample.Create();
            // With the cover and slide pictures the app stores, so the start screen shows them.
            var ctx = new RenderContext(doc.Deck, doc.Media);
            var previews = new Stagecraft.Core.IO.DeckPreviews { CoverPng = MediaStore.EncodePng(SlideRenderer.Render(doc.Deck.Slides[0], ctx, 640)) };
            foreach (var slide in doc.Deck.Slides) previews.SlideJpegs.Add(MediaStore.EncodeJpeg(SlideRenderer.Render(slide, ctx, 240), 80));
            Stagecraft.Core.IO.DeckFile.Save(Path.Combine(folder!, sample.Title + ".stagecraft"), doc.Deck, doc.Media, previews);
        }
    });

    [Fact]
    public void Samples_HaveAnimationsLinksAndMedia_AndLoadBack() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        foreach (var sample in Stagecraft.Core.Samples.DemoDecks.All)
        {
            var doc = sample.Create();
            Assert.Contains(doc.Deck.Slides, s => s.Animations.Count > 0);
            Assert.Contains(doc.Deck.Slides.SelectMany(s => s.Elements), e => e.Link != null || FindReplace.Bodies(e).Any(b => b.Content.Paragraphs.Any(p => p.Runs.Any(r => r.Link != null))));
            foreach (var slide in doc.Deck.Slides) Assert.Equal(0, AnimationRules.Prune(slide));
            var path = dir.File(sample.Id + ".stagecraft");
            Stagecraft.Core.IO.DeckFile.Save(path, doc.Deck, doc.Media);
            Assert.True(new FileInfo(path).Length < 5_000_000, sample.Id + " stays a few MB");
            var loaded = Stagecraft.Core.IO.DeckFile.Load(path);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(doc.Deck.Slides.Sum(s => s.Animations.Count), loaded.Deck.Slides.Sum(s => s.Animations.Count));
            foreach (var clip in loaded.Deck.Slides.SelectMany(s => s.Elements).OfType<MediaClipElement>())
                Assert.True(File.Exists(loaded.Media.ResolvePlayback(clip)), clip.DisplayName);
        }
        Assert.Contains(Stagecraft.Core.Samples.DemoDecks.AuroraKickoff().Deck.Slides.SelectMany(s => s.Elements), e => e is VideoElement { Poster: not null });
        Assert.Contains(Stagecraft.Core.Samples.DemoDecks.CoffeePitch().Deck.Slides.SelectMany(s => s.Elements), e => e is AudioElement);
        Assert.Contains(Stagecraft.Core.Samples.DemoDecks.CoffeePitch().Deck.Slides.SelectMany(s => s.Animations), a => a.Class == EffectClass.Motion);
    });
    private static string FileOf(PdfItem? item)
    {
        if (item is PdfString s) return s.Value;
        var dict = item as PdfDictionary ?? (item as PdfReference)?.Value as PdfDictionary;
        return dict?.Elements["/F"] is PdfString f ? f.Value : dict?.Elements["/UF"] is PdfString uf ? uf.Value : "";
    }

    private static int PageOf(PdfDocument pdf, PdfItem dest)
    {
        var array = dest as PdfArray ?? (dest as PdfReference)?.Value as PdfArray;
        if (array == null || array.Elements.Count == 0) return -1;
        var target = array.Elements[0] as PdfReference;
        for (var i = 0; i < pdf.PageCount; i++)
            if (ReferenceEquals(pdf.Pages[i].Reference, target) || pdf.Pages[i].Reference?.ObjectID == target?.ObjectID) return i;
        return -1;
    }

    [Fact]
    public void Pdf_FooterIsOnThePage_AndRenders() => Sta.Run(() =>
    {
        var (deck, media) = RichDeck();
        var ctx = new RenderContext(deck, media) { Now = new DateTime(2026, 10, 14) };
        Assert.Equal(3, ctx.NumberOf(deck.Slides[2]));
        var bitmap = SlideRenderer.Render(deck.Slides[0], ctx, 640);
        Assert.Equal(640, bitmap.PixelWidth);
    });
}
