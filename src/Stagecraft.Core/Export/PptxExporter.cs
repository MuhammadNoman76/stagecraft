using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Core.Export;

public sealed class PptxExportOptions
{
    public bool IncludeHidden { get; set; }
    public Action<int, int>? Progress { get; set; }
}

/// <summary>
/// PowerPoint (.pptx) export. Text boxes, shapes, lines, pictures and tables become native, editable PowerPoint
/// objects with the deck's slide size, transitions (Morph becomes a fade) and speaker notes. Animations become
/// PowerPoint animations (<see cref="PptxTimingWriter"/>), links become hyperlinks and actions, and videos and
/// sounds become PowerPoint media. Charts, icons, action buttons, online video cards and the theme's background
/// artwork are embedded as high-resolution pictures (keeping their links).
/// </summary>
public static class PptxExporter
{
    public const long EmuPerUnit = 6350; // 1 unit = 1/144 inch = 6350 EMU
    private const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Export(Deck deck, MediaStore media, string path, PptxExportOptions options)
    {
        var temp = path + ".part";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Export(deck, media, stream, options);
            Compat.MoveOverwrite(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public static void Export(Deck deck, MediaStore media, Stream output, PptxExportOptions options)
    {
        var slides = deck.Slides.Where(s => options.IncludeHidden || !s.Hidden).ToList();
        var ctx = new RenderContext(deck, media);
        using var doc = PresentationDocument.Create(output, DocumentFormat.OpenXml.PresentationDocumentType.Presentation, true);
        var presentation = doc.AddPresentationPart();

        var master = presentation.AddNewPart<SlideMasterPart>();
        var theme = master.AddNewPart<ThemePart>();
        Write(theme, ThemeXml(deck.Theme, "Stagecraft"));
        presentation.AddPart(theme);
        var layout = master.AddNewPart<SlideLayoutPart>();
        layout.AddPart(master);
        Write(layout, LayoutXml());
        Write(master, MasterXml(master.GetIdOfPart(layout)));

        var notesMaster = presentation.AddNewPart<NotesMasterPart>();
        var notesTheme = notesMaster.AddNewPart<ThemePart>();
        Write(notesTheme, ThemeXml(deck.Theme, "Stagecraft notes"));
        Write(notesMaster, NotesMasterXml());

        var slideIds = new List<string>();
        // Every slide part first: links can jump to slides further on.
        var parts = new Dictionary<string, SlidePart>();
        foreach (var slide in slides)
        {
            var part = presentation.AddNewPart<SlidePart>();
            part.AddPart(layout);
            parts[slide.Id] = part;
        }
        var shared = new SharedState(doc, parts, slides);
        for (var i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            var part = parts[slide.Id];
            var writer = new SlideWriter(deck, media, ctx, slide, part, shared);
            Write(part, writer.Build());
            if (!string.IsNullOrWhiteSpace(slide.Notes))
            {
                var notes = part.AddNewPart<NotesSlidePart>();
                notes.AddPart(notesMaster);
                notes.AddPart(part);
                Write(notes, NotesXml(slide.Notes));
            }
            slideIds.Add(presentation.GetIdOfPart(part));
            options.Progress?.Invoke(i + 1, slides.Count);
        }

        Write(presentation.AddNewPart<PresentationPropertiesPart>(), $"<p:presentationPr xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"/>");
        Write(presentation.AddNewPart<ViewPropertiesPart>(),
            $"<p:viewPr xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>");
        Write(presentation.AddNewPart<TableStylesPart>(), $"<a:tblStyleLst xmlns:a=\"{NsA}\" def=\"{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}\"/>");

        var sb = new StringBuilder();
        sb.Append($"<p:presentation xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\" saveSubsetFonts=\"1\">");
        sb.Append($"<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"{presentation.GetIdOfPart(master)}\"/></p:sldMasterIdLst>");
        sb.Append($"<p:notesMasterIdLst><p:notesMasterId r:id=\"{presentation.GetIdOfPart(notesMaster)}\"/></p:notesMasterIdLst>");
        if (slideIds.Count > 0)
        {
            sb.Append("<p:sldIdLst>");
            for (var i = 0; i < slideIds.Count; i++) sb.Append($"<p:sldId id=\"{256 + i}\" r:id=\"{slideIds[i]}\"/>");
            sb.Append("</p:sldIdLst>");
        }
        sb.Append($"<p:sldSz cx=\"{Emu(deck.Width)}\" cy=\"{Emu(deck.Height)}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>");
        sb.Append("<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr></p:defaultTextStyle>");
        sb.Append("</p:presentation>");
        Write(presentation, sb.ToString());

        var core = doc.AddCoreFilePropertiesPart();
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
        Write(core,
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{Esc(deck.Title)}</dc:title><dc:creator>Stagecraft</dc:creator>" +
            $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified></cp:coreProperties>");
    }

    // ------------------------------------------------------------------ helpers

    public static long Emu(double units) => (long)Math.Round(units * EmuPerUnit);

    private static string Esc(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    // XML 1.0 can't hold most control characters.
                    if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
                    if (char.IsSurrogate(ch) || ch >= 0x20) sb.Append(ch);
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void Write(OpenXmlPart part, string xml)
    {
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        var bytes = new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Hex(string? hex) => (SlideStyle.ParseHex(hex) is { } c) ? Hex(c) : "000000";

    // ------------------------------------------------------------------ fixed parts

    private static string ThemeXml(DeckTheme t, string name)
    {
        var main = t.Main;
        var dark1 = Hex(main.IsDark ? main.Background.Color : main.Text);
        var light1 = Hex(main.IsDark ? main.Text : main.Background.Color);
        var dark2 = Hex(t.Emphasis.IsDark ? t.Emphasis.Background.Color : t.Emphasis.Text);
        var light2 = Hex(main.Surface);
        var accents = Enumerable.Range(0, 6).Select(i => Hex(t.Accents.Count > 0 ? t.Accents[i % t.Accents.Count] : "#4472C4")).ToList();
        string Fill(string kind) => kind switch
        {
            "solid" => "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>",
            _ => "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"/></a:gs><a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"80000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>",
        };
        var line = "<a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>";
        var effect = "<a:effectStyle><a:effectLst/></a:effectStyle>";
        return $"<a:theme xmlns:a=\"{NsA}\" name=\"{Esc(name)}\"><a:themeElements>" +
               $"<a:clrScheme name=\"{Esc(t.Name)}\"><a:dk1><a:srgbClr val=\"{dark1}\"/></a:dk1><a:lt1><a:srgbClr val=\"{light1}\"/></a:lt1>" +
               $"<a:dk2><a:srgbClr val=\"{dark2}\"/></a:dk2><a:lt2><a:srgbClr val=\"{light2}\"/></a:lt2>" +
               string.Concat(accents.Select((a, i) => $"<a:accent{i + 1}><a:srgbClr val=\"{a}\"/></a:accent{i + 1}>")) +
               $"<a:hlink><a:srgbClr val=\"{accents[0]}\"/></a:hlink><a:folHlink><a:srgbClr val=\"{accents[1]}\"/></a:folHlink></a:clrScheme>" +
               $"<a:fontScheme name=\"{Esc(t.Name)}\"><a:majorFont><a:latin typeface=\"{Esc(t.Fonts.Heading)}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
               $"<a:minorFont><a:latin typeface=\"{Esc(t.Fonts.Body)}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
               "<a:fmtScheme name=\"Stagecraft\"><a:fillStyleLst>" + Fill("solid") + Fill("grad") + Fill("grad") + "</a:fillStyleLst>" +
               "<a:lnStyleLst>" + line + line + line + "</a:lnStyleLst><a:effectStyleLst>" + effect + effect + effect + "</a:effectStyleLst>" +
               "<a:bgFillStyleLst>" + Fill("solid") + Fill("solid") + Fill("grad") + "</a:bgFillStyleLst></a:fmtScheme>" +
               "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }

    private const string EmptyTree =
        "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr></p:spTree>";

    private const string ClrMap = "bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"";

    private static string MasterXml(string layoutRel) =>
        $"<p:sldMaster xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:cSld>" +
        "<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>" + EmptyTree + "</p:cSld>" +
        $"<p:clrMap {ClrMap}/>" +
        $"<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"{layoutRel}\"/></p:sldLayoutIdLst>" +
        "<p:txStyles><p:titleStyle><a:lvl1pPr><a:defRPr sz=\"4400\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mj-lt\"/></a:defRPr></a:lvl1pPr></p:titleStyle>" +
        "<p:bodyStyle><a:lvl1pPr><a:defRPr sz=\"1800\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:bodyStyle>" +
        "<p:otherStyle><a:lvl1pPr><a:defRPr sz=\"1800\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:otherStyle></p:txStyles>" +
        "</p:sldMaster>";

    private static string LayoutXml() =>
        $"<p:sldLayout xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\" type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\">" + EmptyTree +
        "</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static string NotesMasterXml() =>
        $"<p:notesMaster xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:cSld>" +
        "<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>" +
        "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>" +
        "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>" +
        "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
        "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>" +
        "</p:spTree></p:cSld>" +
        $"<p:clrMap {ClrMap}/>" +
        "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\"><a:defRPr sz=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:notesStyle>" +
        "</p:notesMaster>";

    private static string NotesXml(string notes)
    {
        var paragraphs = notes.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Length == 0 ? "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>" : $"<a:p><a:r><a:rPr lang=\"en-US\" dirty=\"0\"/><a:t>{Esc(l)}</a:t></a:r></a:p>");
        return $"<p:notes xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:cSld><p:spTree>" +
               "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
               "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
               "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
               "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/>" +
               "<p:txBody><a:bodyPr/><a:lstStyle/>" + string.Concat(paragraphs) + "</p:txBody></p:sp>" +
               "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>";
    }

    // ------------------------------------------------------------------ slides

    /// <summary>What every slide writer shares: the package, the slide parts (for slide links), pictures and media written once.</summary>
    private sealed class SharedState
    {
        public SharedState(PresentationDocument doc, Dictionary<string, SlidePart> parts, List<Slide> slides)
        {
            Doc = doc;
            Parts = parts;
            Slides = slides;
        }

        public PresentationDocument Doc { get; }
        public Dictionary<string, SlidePart> Parts { get; }
        public List<Slide> Slides { get; }
        public Dictionary<string, ImagePart> Images { get; } = new();
        public Dictionary<string, MediaDataPart> MediaParts { get; } = new();
    }

    private sealed class SlideWriter
    {
        private readonly Deck _deck;
        private readonly MediaStore _media;
        private readonly RenderContext _ctx;
        private readonly Slide _slide;
        private readonly SlidePart _part;
        private readonly SlideStyle _style;
        private readonly SharedState _shared;
        private readonly Dictionary<string, ImagePart> _images;
        private readonly StringBuilder _sb = new();
        private int _nextId = 2;

        // Shape ids of the elements written (animations refer to them), the ones with text, and the media.
        private readonly Dictionary<string, int> _spids = new();
        private readonly HashSet<int> _textShapes = new();
        private readonly List<PptxMediaInfo> _mediaShapes = new();

        public SlideWriter(Deck deck, MediaStore media, RenderContext ctx, Slide slide, SlidePart part, SharedState shared)
        {
            _deck = deck;
            _media = media;
            _ctx = ctx;
            _slide = slide;
            _part = part;
            _shared = shared;
            _images = shared.Images;
            _style = ctx.StyleFor(slide);
        }

        public string Build()
        {
            _sb.Append($"<p:sld xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"{(_slide.Hidden ? " show=\"0\"" : "")}><p:cSld>");
            Background();
            _sb.Append("<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>");
            _sb.Append("<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");
            foreach (var e in _slide.Elements)
            {
                try
                {
                    Element(e);
                }
                catch (Exception)
                {
                    // Something unusual: keep the look as a picture.
                    PictureOf(e);
                }
            }
            Footer();
            _sb.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>");
            Transition();
            string? timing = null;
            try
            {
                timing = new PptxTimingWriter(_slide, _deck, _spids, _textShapes, _mediaShapes).Build();
            }
            catch (Exception)
            {
                // A slide whose animations can't be written keeps everything else.
            }
            if (timing != null) _sb.Append(timing);
            _sb.Append("</p:sld>");
            return _sb.ToString();
        }


        /// <summary>The header and footer as PowerPoint text boxes: the slide number and an automatic date as fields.</summary>
        private void Footer()
        {
            var footer = _deck.Footer;
            if (footer == null || !footer.ShowsOn(_slide)) return;
            var m = _style.Margin;
            var size = 22 * _style.Scale;
            var third = (_deck.Width - 2 * m) / 3;
            var height = size * 1.7;
            var top = _deck.Height - m * 0.42 - height / 2;
            var color = Hex(_style.Resolve("text2"));
            var font = Esc(_style.Theme.Fonts.Body);
            string Props() => $"<a:rPr lang=\"en-US\" sz=\"{FontSize(size)}\" dirty=\"0\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:latin typeface=\"{font}\"/><a:cs typeface=\"{font}\"/></a:rPr>";
            void Box(string name, double x, double width, string align, string content)
            {
                var id = _nextId++;
                _sb.Append($"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr>");
                _sb.Append($"<p:spPr><a:xfrm><a:off x=\"{Emu(x)}\" y=\"{Emu(top)}\"/><a:ext cx=\"{Emu(width)}\" cy=\"{Emu(height)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>");
                _sb.Append($"<p:txBody><a:bodyPr wrap=\"none\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" anchor=\"ctr\"><a:noAutofit/></a:bodyPr><a:lstStyle/><a:p><a:pPr algn=\"{align}\"/>{content}");
                _sb.Append($"<a:endParaRPr lang=\"en-US\" sz=\"{FontSize(size)}\" dirty=\"0\"/></a:p></p:txBody></p:sp>");
            }
            if (footer.Date)
            {
                var text = Esc(footer.DateFor(_ctx.Now));
                var content = footer.FixedDate
                    ? $"<a:r>{Props()}<a:t>{text}</a:t></a:r>"
                    : $"<a:fld id=\"{Guid.NewGuid().ToString("B").ToUpperInvariant()}\" type=\"{(footer.DateFormat == "long" ? "datetime4" : "datetime1")}\">{Props()}<a:t>{text}</a:t></a:fld>";
                if (text.Length > 0) Box("Date", m, third, "l", content);
            }
            if (!string.IsNullOrWhiteSpace(footer.Text))
                Box("Footer", _deck.Width / 2 - third * 0.7, third * 1.4, "ctr", $"<a:r>{Props()}<a:t>{Esc(footer.Text!.Trim())}</a:t></a:r>");
            if (footer.SlideNumber)
            {
                var number = _ctx.NumberOf(_slide);
                if (number > 0)
                    Box("Slide number", _deck.Width - m - third, third, "r",
                        $"<a:fld id=\"{Guid.NewGuid().ToString("B").ToUpperInvariant()}\" type=\"slidenum\">{Props()}<a:t>{number.ToString(Inv)}</a:t></a:fld>");
            }
        }
        // ------------------------------------------------------------------ links

        /// <summary>
        /// The a:hlinkClick for a link (null when there's none or it can't be written): web pages and e-mail as
        /// hyperlinks, slide jumps as PowerPoint show actions, files as links to the file.
        /// </summary>
        private string? HlinkClick(Hyperlink? link)
        {
            if (link == null) return null;
            string? rid = null;
            string? action = null;
            switch (link.Kind)
            {
                case LinkKind.Web:
                case LinkKind.Email:
                {
                    var uri = LinkSafety.SafeUri(link);
                    if (uri == null) return null;
                    rid = _part.AddHyperlinkRelationship(new Uri(uri, UriKind.Absolute), true).Id;
                    break;
                }
                case LinkKind.File:
                {
                    var path = LinkSafety.ResolveFile(link.Address, _media.BaseFolder) ?? link.Address;
                    if (string.IsNullOrWhiteSpace(path)) return null;
                    try
                    {
                        var uri = Path.IsPathRooted(path) ? new Uri(path, UriKind.Absolute) : new Uri(path.Replace('\\', '/'), UriKind.Relative);
                        rid = _part.AddHyperlinkRelationship(uri, true).Id;
                    }
                    catch (UriFormatException)
                    {
                        return null;
                    }
                    break;
                }
                case LinkKind.Slide:
                {
                    var jump = link.Jump switch
                    {
                        SlideJump.Next => "nextslide",
                        SlideJump.Previous => "previousslide",
                        SlideJump.First => "firstslide",
                        SlideJump.Last => "lastslide",
                        SlideJump.LastViewed => "lastslideviewed",
                        _ => null,
                    };
                    if (jump != null)
                    {
                        rid = "";
                        action = "ppaction://hlinkshowjump?jump=" + jump;
                        break;
                    }
                    if (link.SlideId == null || !_shared.Parts.TryGetValue(link.SlideId, out var target) || target == _part) return null;
                    rid = _part.CreateRelationshipToPart(target);
                    action = "ppaction://hlinksldjump";
                    break;
                }
            }
            if (rid == null) return null;
            var tip = string.IsNullOrWhiteSpace(link.ScreenTip) ? "" : $" tooltip=\"{Esc(link.ScreenTip!)}\"";
            return $"<a:hlinkClick r:id=\"{rid}\"{(action != null ? $" action=\"{action}\"" : "")}{tip}/>";
        }

        /// <summary>p:cNvPr with the element's link (or <paramref name="extra"/>, e.g. the media action) inside.</summary>
        private string CNvPr(int id, string name, string? descr, Hyperlink? link, string? extra = null)
        {
            var inner = extra ?? HlinkClick(link);
            var d = descr != null ? $" descr=\"{Esc(descr)}\"" : "";
            return inner == null ? $"<p:cNvPr id=\"{id}\" name=\"{name}\"{d}/>" : $"<p:cNvPr id=\"{id}\" name=\"{name}\"{d}>{inner}</p:cNvPr>";
        }

        // ------------------------------------------------------------------ video and sound

        /// <summary>The package part holding a video or sound file (written once, shared by slides).</summary>
        private MediaDataPart? MediaPart(MediaClipElement clip, out bool linked, out string? linkedPath)
        {
            linked = false;
            linkedPath = null;
            if (clip.Media != null)
            {
                if (_shared.MediaParts.TryGetValue(clip.Media, out var existing)) return existing;
                var item = _media.Get(clip.Media);
                if (item == null) return null;
                var ext = Path.GetExtension(clip.Media).TrimStart('.').ToLowerInvariant();
                var part = _shared.Doc.CreateMediaDataPart(MediaTypes.ContentType(clip.Media), "." + ext);
                using (var s = item.OpenRead()) part.FeedData(s);
                _shared.MediaParts[clip.Media] = part;
                return part;
            }
            linkedPath = _media.ResolveLink(clip) ?? clip.LinkAbsolute ?? clip.LinkPath;
            linked = linkedPath != null;
            return null;
        }

        private const string MediaRelType = "http://schemas.microsoft.com/office/2007/relationships/media";
        private const string VideoRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video";
        private const string AudioRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";

        /// <summary>A video or sound as PowerPoint media: its poster (or icon) picture, the file, trim and fades.</summary>
        private void Media(MediaClipElement clip)
        {
            var video = clip is VideoElement;
            var part = MediaPart(clip, out var linked, out var linkedPath);
            if (part == null && !linked)
            {
                PictureOf(clip); // the file is missing: keep the look
                return;
            }
            string fileRel, mediaRel;
            if (part != null)
            {
                fileRel = video ? _part.AddVideoReferenceRelationship(part).Id : _part.AddAudioReferenceRelationship(part).Id;
                mediaRel = _part.AddMediaReferenceRelationship(part).Id;
            }
            else
            {
                var uri = new Uri(linkedPath!, Path.IsPathRooted(linkedPath!) ? UriKind.Absolute : UriKind.Relative);
                fileRel = _part.AddExternalRelationship(video ? VideoRelType : AudioRelType, uri).Id;
                mediaRel = _part.AddExternalRelationship(MediaRelType, uri).Id;
            }

            // The picture PowerPoint shows: the poster frame, else the look Stagecraft draws (the speaker icon for sounds).
            var bounds = clip.Bounds;
            string poster;
            if (clip is VideoElement { Poster: { } p } && _media.GetImage(p) is { } posterImage)
                poster = AddImage(MediaStore.EncodeJpeg(posterImage, 90), "jpg");
            else
                poster = AddImage(MediaStore.EncodePng(RenderElement(IconLook(clip), out bounds)), "png");

            var id = _nextId++;
            _spids[clip.Id] = id;
            _mediaShapes.Add(new PptxMediaInfo(clip, id));
            var trim = "";
            if (clip.TrimStart > 0 || clip.TrimEnd != null)
            {
                var end = clip.TrimEnd is { } te && clip.Length is { } len && len > te ? $" end=\"{Ms(len - te)}\"" : "";
                trim = $"<p14:trim st=\"{Ms(clip.TrimStart)}\"{end}/>";
            }
            var fade = clip.FadeIn > 0 || clip.FadeOut > 0 ? $"<p14:fade in=\"{Ms(clip.FadeIn)}\" out=\"{Ms(clip.FadeOut)}\"/>" : "";
            var mediaAttr = part != null ? $"r:embed=\"{mediaRel}\"" : $"r:link=\"{mediaRel}\"";
            _sb.Append("<p:pic><p:nvPicPr>").Append(CNvPr(id, Name(clip, video ? "Video" : "Sound"), clip.DisplayName, null, "<a:hlinkClick r:id=\"\" action=\"ppaction://media\"/>"));
            _sb.Append("<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr>");
            _sb.Append(video ? $"<a:videoFile r:link=\"{fileRel}\"/>" : $"<a:audioFile r:link=\"{fileRel}\"/>");
            _sb.Append("<p:extLst><p:ext uri=\"{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}\">");
            _sb.Append($"<p14:media xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" {mediaAttr}>{trim}{fade}</p14:media>");
            _sb.Append("</p:ext></p:extLst></p:nvPr></p:nvPicPr>");
            _sb.Append($"<p:blipFill><a:blip r:embed=\"{poster}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>");
            var geometry = clip is VideoElement v
                ? v.Mask == ImageMask.Circle ? "<a:prstGeom prst=\"ellipse\"><a:avLst/></a:prstGeom>"
                : v.CornerRadius > 0 ? $"<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val {(int)Math.Round(MathEx.Clamp(v.CornerRadius / Math.Max(1, Math.Min(v.Width, v.Height)), 0, 0.5) * 100000)}\"/></a:avLst></a:prstGeom>"
                : "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>"
                : "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>";
            var xfrm = video ? Xfrm(clip) : $"<a:xfrm><a:off x=\"{Emu(bounds.X)}\" y=\"{Emu(bounds.Y)}\"/><a:ext cx=\"{Emu(bounds.Width)}\" cy=\"{Emu(bounds.Height)}\"/></a:xfrm>";
            _sb.Append("<p:spPr>").Append(xfrm).Append(geometry);
            if (clip is VideoElement { Stroke: { } stroke }) _sb.Append(LineXml(stroke, clip.Opacity));
            _sb.Append("</p:spPr></p:pic>");
        }

        private static string Ms(double seconds) => ((long)Math.Round(Math.Max(0, seconds) * 1000)).ToString(Inv);

        /// <summary>A sound's speaker icon is always drawn for PowerPoint (it hides it in the show itself).</summary>
        private static Element IconLook(MediaClipElement clip)
        {
            if (clip is not AudioElement { HideIcon: true }) return clip;
            var copy = IO.DeckJson.Clone<Element>(clip);
            ((AudioElement)copy).HideIcon = false;
            return copy;
        }

        /// <summary>An outer shadow (a:effectLst), or nothing.</summary>
        private string ShadowXml(Shadow? shadow)
        {
            if (shadow == null) return "";
            var dir = (long)Math.Round(((shadow.Angle % 360) + 360) % 360 * 60000);
            return $"<a:effectLst><a:outerShdw blurRad=\"{Emu(shadow.Blur)}\" dist=\"{Emu(shadow.Distance)}\" dir=\"{dir}\" algn=\"ctr\" rotWithShape=\"0\">{Color(shadow.Color)}</a:outerShdw></a:effectLst>";
        }

        private void Transition()
        {
            var t = _slide.Transition;
            if (t.Kind == TransitionKind.None) return;
            var speed = t.Duration < 0.45 ? "fast" : t.Duration < 0.9 ? "med" : "slow";
            var dir = t.Direction switch { TransitionDirection.Right => "r", TransitionDirection.Up => "u", TransitionDirection.Down => "d", _ => "l" };
            // PowerPoint's direction is where the new slide moves to; ours is where it comes from.
            var pptDir = dir switch { "l" => "l", "r" => "r", "u" => "u", _ => "d" };
            var inner = t.Kind switch
            {
                TransitionKind.Push => $"<p:push dir=\"{pptDir}\"/>",
                TransitionKind.Slide => $"<p:cover dir=\"{pptDir}\"/>",
                TransitionKind.Zoom => "<p:zoom/>",
                _ => "<p:fade/>",
            };
            _sb.Append($"<p:transition spd=\"{speed}\">{inner}</p:transition>");
        }

        private void Background()
        {
            var fill = _style.Background;
            var plain = fill.Kind == FillKind.Solid && (!_slide.ShowDecorations || _slide.Background != null || _slide.Layout is "image-full" or "blank");
            if (plain)
            {
                var c = _style.Resolve(fill.Color);
                _sb.Append($"<p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"{Hex(c)}\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>");
                return;
            }
            // Gradients, pictures and the theme's decorations: one picture of the background.
            var w = (int)Math.Min(2560, Math.Max(960, _deck.Width));
            var h = (int)Math.Round(w * _deck.Height / _deck.Width);
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(w / _deck.Width, h / _deck.Height));
                DecorPainter.Paint(dc, _slide, _style, _ctx, _deck.Width, _deck.Height, footer: false);
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var rid = AddImage(MediaStore.EncodePng(bmp), "png");
            _sb.Append($"<p:bg><p:bgPr><a:blipFill dpi=\"0\" rotWithShape=\"1\"><a:blip r:embed=\"{rid}\"/><a:srcRect/><a:stretch><a:fillRect/></a:stretch></a:blipFill><a:effectLst/></p:bgPr></p:bg>");
        }

        private string AddImage(byte[] data, string type)
        {
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(data)) + type;
            if (!_images.TryGetValue(hash, out var image))
            {
                image = _part.AddImagePart(type == "png" ? ImagePartType.Png : ImagePartType.Jpeg);
                using (var s = image.GetStream(FileMode.Create, FileAccess.Write)) s.Write(data, 0, data.Length);
                _images[hash] = image;
                return _part.GetIdOfPart(image);
            }
            try
            {
                return _part.GetIdOfPart(image);
            }
            catch (ArgumentOutOfRangeException)
            {
                return _part.CreateRelationshipToPart(image);
            }
        }

        private string Xfrm(Element e, bool allowFlip = true)
        {
            var rot = ((long)Math.Round(((e.Rotation % 360) + 360) % 360 * 60000)) % 21600000;
            var attrs = rot != 0 ? $" rot=\"{rot}\"" : "";
            if (allowFlip && e.FlipH) attrs += " flipH=\"1\"";
            if (allowFlip && e.FlipV) attrs += " flipV=\"1\"";
            return $"<a:xfrm{attrs}><a:off x=\"{Emu(e.X)}\" y=\"{Emu(e.Y)}\"/><a:ext cx=\"{Emu(Math.Max(0, e.Width))}\" cy=\"{Emu(Math.Max(0, e.Height))}\"/></a:xfrm>";
        }

        private string Color(string? reference, double opacity = 1)
        {
            var c = _style.Resolve(reference);
            var alpha = c.A / 255.0 * opacity;
            return alpha >= 0.999
                ? $"<a:srgbClr val=\"{Hex(c)}\"/>"
                : $"<a:srgbClr val=\"{Hex(c)}\"><a:alpha val=\"{(int)Math.Round(alpha * 100000)}\"/></a:srgbClr>";
        }

        private string? FillXml(Fill? fill, double opacity)
        {
            if (fill == null || fill.Kind == FillKind.None) return "<a:noFill/>";
            switch (fill.Kind)
            {
                case FillKind.Solid:
                    return $"<a:solidFill>{Color(fill.Color, opacity)}</a:solidFill>";
                case FillKind.Linear:
                    var ang = (long)Math.Round(((fill.Angle % 360) + 360) % 360 * 60000);
                    return $"<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\">{Color(fill.Color, opacity)}</a:gs><a:gs pos=\"100000\">{Color(fill.Color2 ?? fill.Color, opacity)}</a:gs></a:gsLst><a:lin ang=\"{ang}\" scaled=\"0\"/></a:gradFill>";
                case FillKind.Radial:
                    return $"<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\">{Color(fill.Color, opacity)}</a:gs><a:gs pos=\"100000\">{Color(fill.Color2 ?? fill.Color, opacity)}</a:gs></a:gsLst><a:path path=\"circle\"><a:fillToRect l=\"50000\" t=\"50000\" r=\"50000\" b=\"50000\"/></a:path></a:gradFill>";
                default:
                    return null; // picture fills: drawn as a picture instead
            }
        }

        private string LineXml(Stroke? stroke, double opacity, ArrowHead start = ArrowHead.None, ArrowHead end = ArrowHead.None)
        {
            if (stroke == null || stroke.Width <= 0) return "<a:ln><a:noFill/></a:ln>";
            var dash = stroke.Dash switch { DashStyleKind.Dash => "dash", DashStyleKind.Dot => "sysDot", DashStyleKind.LongDash => "lgDash", DashStyleKind.DashDot => "dashDot", _ => "solid" };
            string Head(ArrowHead h) => h switch { ArrowHead.Arrow => "triangle", ArrowHead.Dot => "oval", _ => "none" };
            var heads = (start != ArrowHead.None ? $"<a:headEnd type=\"{Head(start)}\" w=\"med\" len=\"med\"/>" : "") +
                        (end != ArrowHead.None ? $"<a:tailEnd type=\"{Head(end)}\" w=\"med\" len=\"med\"/>" : "");
            return $"<a:ln w=\"{Emu(stroke.Width)}\" cap=\"rnd\"><a:solidFill>{Color(stroke.Color, opacity)}</a:solidFill><a:prstDash val=\"{dash}\"/><a:round/>{heads}</a:ln>";
        }

        private string Name(Element e, string fallback) => Esc(string.IsNullOrWhiteSpace(e.Name) ? fallback + " " + (_nextId - 1) : e.Name!);

        private void Element(Element e)
        {
            switch (e)
            {
                case TextElement t:
                    if (t.Text.Content.IsEmpty && t.Fill == null && t.Stroke == null) return; // empty placeholder
                    if (t.Fill?.Kind == FillKind.Image)
                    {
                        PictureOf(e);
                        return;
                    }
                    Shape(e, t.CornerRadius > 0 ? "roundRect" : "rect", t.CornerRadius, t.Fill, t.Stroke, t.Text, textBox: true);
                    break;
                case ShapeElement s when s.IsLine:
                    Line(s);
                    break;
                case ShapeElement { Shape: ShapeKind.ActionButton } button:
                    // Drawn as Stagecraft draws it (the symbol included), keeping its link.
                    PictureOf(button);
                    break;
                case MediaClipElement clip:
                    Media(clip);
                    break;
                case ShapeElement s:
                    if (s.Fill.Kind == FillKind.Image)
                    {
                        PictureOf(e);
                        return;
                    }
                    var prst = s.Shape switch
                    {
                        ShapeKind.RoundedRectangle => "roundRect",
                        ShapeKind.Ellipse => "ellipse",
                        ShapeKind.Triangle => "triangle",
                        ShapeKind.Diamond => "diamond",
                        ShapeKind.Callout => "wedgeRoundRectCallout",
                        _ => s.CornerRadius > 0 ? "roundRect" : "rect",
                    };
                    var radius = s.Shape == ShapeKind.RoundedRectangle && s.CornerRadius <= 0 ? Math.Min(s.Width, s.Height) * 0.2 : s.CornerRadius;
                    Shape(e, prst, radius, s.Fill, s.Stroke, s.Text, textBox: false, callout: s);
                    break;
                case ImageElement img:
                    Picture(img);
                    break;
                case TableElement table:
                    Table(table);
                    break;
                default:
                    PictureOf(e);
                    break;
            }
        }

        private void Shape(Element e, string prst, double radius, Fill? fill, Stroke? stroke, TextBody? body, bool textBox, ShapeElement? callout = null)
        {
            var id = _nextId++;
            var geometry = prst switch
            {
                "roundRect" => $"<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val {(int)Math.Round(MathEx.Clamp(radius / Math.Max(1, Math.Min(e.Width, e.Height)), 0, 0.5) * 100000)}\"/></a:avLst></a:prstGeom>",
                "wedgeRoundRectCallout" when callout != null =>
                    $"<a:prstGeom prst=\"wedgeRoundRectCallout\"><a:avLst><a:gd name=\"adj1\" fmla=\"val {(int)Math.Round((callout.TailX - 0.5) * 100000)}\"/><a:gd name=\"adj2\" fmla=\"val {(int)Math.Round((callout.TailY - 0.5) * 100000)}\"/><a:gd name=\"adj3\" fmla=\"val 16667\"/></a:avLst></a:prstGeom>",
                _ => $"<a:prstGeom prst=\"{prst}\"><a:avLst/></a:prstGeom>",
            };
            _spids[e.Id] = id;
            if (body != null || textBox) _textShapes.Add(id);
            _sb.Append("<p:sp><p:nvSpPr>").Append(CNvPr(id, Name(e, textBox ? "Text" : "Shape"), null, e.Link)).Append($"<p:cNvSpPr{(textBox ? " txBox=\"1\"" : "")}/><p:nvPr/></p:nvSpPr>");
            _sb.Append("<p:spPr>").Append(Xfrm(e, allowFlip: !textBox)).Append(geometry).Append(FillXml(fill, e.Opacity) ?? "<a:noFill/>").Append(LineXml(stroke, e.Opacity)).Append(ShadowXml(e.Shadow)).Append("</p:spPr>");
            TextBodyXml(body, e, textBox ? e.Bounds : ElementRenderer.TextBoxOf((ShapeElement)e, e.Bounds), textBox);
            _sb.Append("</p:sp>");
        }

        private void TextBodyXml(TextBody? body, Element e, Rect box, bool textBox)
        {
            if (body == null)
            {
                if (!textBox) return;
                body = new TextBody();
            }
            var inner = TextLayout.Inner(body, box);
            var scale = TextLayout.FitScale(body, inner.Width, inner.Height, _style);
            var insetL = Emu(inner.X - e.X);
            var insetT = Emu(inner.Y - e.Y);
            var insetR = Emu(e.X + e.Width - inner.Right);
            var insetB = Emu(e.Y + e.Height - inner.Bottom);
            var anchor = body.VAlign switch { VerticalAlign.Middle => "ctr", VerticalAlign.Bottom => "b", _ => "t" };
            _sb.Append($"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"{Math.Max(0, insetL)}\" tIns=\"{Math.Max(0, insetT)}\" rIns=\"{Math.Max(0, insetR)}\" bIns=\"{Math.Max(0, insetB)}\" rtlCol=\"0\" anchor=\"{anchor}\">");
            _sb.Append(body.AutoFit ? "<a:normAutofit/>" : "<a:noAutofit/>");
            _sb.Append("</a:bodyPr><a:lstStyle/>");

            var role = _style.RoleFont(body.Role);
            var uppercase = body.Uppercase ?? role.Uppercase;
            var lineSpacing = body.LineSpacing ?? role.LineHeight;
            var baseSize = (body.Size ?? role.Size) * scale;
            var counters = new int[8];
            var paragraphs = body.Content.Paragraphs.Count > 0 ? body.Content.Paragraphs : new List<Paragraph> { new() };
            for (var pi = 0; pi < paragraphs.Count; pi++)
            {
                var p = paragraphs[pi];
                var align = (p.Align ?? body.Align) switch { TextAlign.Center => "ctr", TextAlign.Right => "r", TextAlign.Justify => "just", _ => "l" };
                var level = MathEx.Clamp(p.Level, 0, 7);
                var first = TextLayout.Resolve(p.Runs.FirstOrDefault() ?? new TextRun(), body, _style, scale);
                var unit = first.Size;
                var indent = p.List != ListKind.None ? unit * 1.15 * (level + 1) : unit * 1.15 * level;
                var pPr = new StringBuilder($"<a:pPr marL=\"{Emu(indent)}\" indent=\"{(p.List != ListKind.None ? -Emu(unit * 1.15) : 0)}\" algn=\"{align}\">");
                pPr.Append($"<a:lnSpc><a:spcPct val=\"{(int)Math.Round(lineSpacing / 1.2 * 100000)}\"/></a:lnSpc>");
                if (pi < paragraphs.Count - 1) pPr.Append($"<a:spcAft><a:spcPts val=\"{(int)Math.Round(role.ParagraphSpacing * baseSize * 50)}\"/></a:spcAft>");
                if (p.List == ListKind.Bullet)
                {
                    var bulletColor = body.Role == TextRole.Body && body.Color == null ? _style.Resolve("accent1") : first.Color;
                    pPr.Append($"<a:buClr><a:srgbClr val=\"{Hex(bulletColor)}\"/></a:buClr><a:buFont typeface=\"Arial\"/><a:buChar char=\"{(level % 2 == 0 ? "•" : "–")}\"/>");
                }
                else if (p.List == ListKind.Number) pPr.Append("<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>");
                else pPr.Append("<a:buNone/>");
                pPr.Append("</a:pPr>");
                _sb.Append("<a:p>").Append(pPr);
                foreach (var run in p.Runs)
                {
                    if (run.Text.Length == 0) continue;
                    var f = TextLayout.Resolve(run, body, _style, scale);
                    var pieces = (uppercase ? run.Text.ToUpper(CultureInfo.GetCultureInfo("en-US")) : run.Text).Replace("\r\n", "\n").Split('\n');
                    for (var k = 0; k < pieces.Length; k++)
                    {
                        if (k > 0) _sb.Append($"<a:br>{RunProps(f, e.Opacity, body.Shadow, null)}</a:br>");
                        if (pieces[k].Length == 0) continue;
                        _sb.Append("<a:r>").Append(RunProps(f, e.Opacity, body.Shadow, run.Link)).Append("<a:t>").Append(Esc(pieces[k])).Append("</a:t></a:r>");
                    }
                }
                _sb.Append($"<a:endParaRPr lang=\"en-US\" sz=\"{FontSize(first.Size)}\" dirty=\"0\"/></a:p>");
            }
            _sb.Append("</p:txBody>");
        }

        private static int FontSize(double units) => (int)MathEx.Clamp(Math.Round(units * 50), 100, 400000);

        private string RunProps(TextLayout.RunFormat f, double opacity, Shadow? shadow, Hyperlink? link)
        {
            var attrs = $" lang=\"en-US\" sz=\"{FontSize(f.Size)}\"";
            if (f.Weight >= 600) attrs += " b=\"1\"";
            if (f.Italic) attrs += " i=\"1\"";
            if (f.Underline) attrs += " u=\"sng\"";
            if (f.Strike) attrs += " strike=\"sngStrike\"";
            var alpha = f.Color.A / 255.0 * opacity;
            var color = alpha >= 0.999 ? $"<a:srgbClr val=\"{Hex(f.Color)}\"/>" : $"<a:srgbClr val=\"{Hex(f.Color)}\"><a:alpha val=\"{(int)Math.Round(alpha * 100000)}\"/></a:srgbClr>";
            return $"<a:rPr{attrs} dirty=\"0\"><a:solidFill>{color}</a:solidFill>{ShadowXml(shadow)}<a:latin typeface=\"{Esc(f.Family)}\"/><a:cs typeface=\"{Esc(f.Family)}\"/>{HlinkClick(link)}</a:rPr>";
        }

        private void Line(ShapeElement s)
        {
            var id = _nextId++;
            var stroke = ElementRenderer.LineStroke(s);
            var end = s.Shape == ShapeKind.Arrow && s.EndHead == ArrowHead.None ? ArrowHead.Arrow : s.EndHead;
            var prst = s.Shape == ShapeKind.Connector && s.Elbow ? "bentConnector3" : "line";
            _spids[s.Id] = id;
            _sb.Append("<p:cxnSp><p:nvCxnSpPr>").Append(CNvPr(id, Name(s, "Line"), null, s.Link)).Append("<p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>");
            _sb.Append("<p:spPr>").Append(Xfrm(s)).Append($"<a:prstGeom prst=\"{prst}\"><a:avLst/></a:prstGeom>").Append(LineXml(stroke, s.Opacity, s.StartHead, end)).Append("</p:spPr></p:cxnSp>");
        }

        private void Picture(ImageElement img)
        {
            var bitmap = _media.GetImage(img.Media);
            if (bitmap == null) return; // empty placeholder or missing picture
            var transparent = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32;
            var rid = AddImage(transparent ? MediaStore.EncodePng(bitmap) : MediaStore.EncodeJpeg(bitmap, 92), transparent ? "png" : "jpg");
            var dest = ElementRenderer.ImageDestination(img, img.Bounds, bitmap.PixelWidth, bitmap.PixelHeight);
            int Frac(double v) => (int)Math.Round(v * 100000);
            var l = Frac((img.X - dest.X) / dest.Width);
            var t = Frac((img.Y - dest.Y) / dest.Height);
            var r = Frac((dest.Right - img.Bounds.Right) / dest.Width);
            var b = Frac((dest.Bottom - img.Bounds.Bottom) / dest.Height);
            var geometry = img.Mask == ImageMask.Circle ? "<a:prstGeom prst=\"ellipse\"><a:avLst/></a:prstGeom>"
                : img.CornerRadius > 0 ? $"<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val {(int)Math.Round(MathEx.Clamp(img.CornerRadius / Math.Max(1, Math.Min(img.Width, img.Height)), 0, 0.5) * 100000)}\"/></a:avLst></a:prstGeom>"
                : "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>";
            var id = _nextId++;
            var alpha = img.Opacity < 0.999 ? $"<a:alphaModFix amt=\"{(int)Math.Round(img.Opacity * 100000)}\"/>" : "";
            _spids[img.Id] = id;
            _sb.Append("<p:pic><p:nvPicPr>").Append(CNvPr(id, Name(img, "Picture"), img.AltText ?? "", img.Link)).Append("<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>");
            _sb.Append($"<p:blipFill><a:blip r:embed=\"{rid}\">{alpha}</a:blip>");
            _sb.Append(l != 0 || t != 0 || r != 0 || b != 0 ? $"<a:srcRect l=\"{l}\" t=\"{t}\" r=\"{r}\" b=\"{b}\"/>" : "<a:srcRect/>");
            _sb.Append("<a:stretch><a:fillRect/></a:stretch></p:blipFill>");
            _sb.Append("<p:spPr>").Append(Xfrm(img)).Append(geometry).Append(LineXml(img.Stroke, img.Opacity)).Append(ShadowXml(img.Shadow)).Append("</p:spPr></p:pic>");
        }

        /// <summary>Anything without a native equivalent (charts, icons): a high-resolution picture in its place.</summary>
        /// <summary>An element drawn as Stagecraft draws it (its shadow included), and the area the picture covers.</summary>
        private BitmapSource RenderElement(Element e, out Rect bounds)
        {
            bounds = e.RotatedBounds;
            if (e.Shadow is { } shadow)
            {
                var spread = shadow.Blur + shadow.Distance;
                bounds.Inflate(spread, spread);
            }
            var scale = Math.Min(3, 3000 / Math.Max(1, Math.Max(bounds.Width, bounds.Height)));
            var w = (int)Math.Ceiling(bounds.Width * scale);
            var h = (int)Math.Ceiling(bounds.Height * scale);
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                ElementRenderer.Draw(dc, e, _style, _ctx);
            }
            var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private void PictureOf(Element e)
        {
            if (e.RotatedBounds.Width < 1 || e.RotatedBounds.Height < 1) return;
            var bmp = RenderElement(e, out var bounds);
            var rid = AddImage(MediaStore.EncodePng(bmp), "png");
            var id = _nextId++;
            var descr = e is ChartElement c ? c.KindName + ": " + string.Join(", ", c.Categories) : e.Describe;
            _spids[e.Id] = id;
            var link = e.Link ?? (e is OnlineVideoElement online && LinkSafety.NormalizeWeb(online.Url) is { } url ? Hyperlink.Web(url, online.Title) : null);
            _sb.Append("<p:pic><p:nvPicPr>").Append(CNvPr(id, Name(e, e.KindName), descr, link)).Append("<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>");
            _sb.Append($"<p:blipFill><a:blip r:embed=\"{rid}\"/><a:srcRect/><a:stretch><a:fillRect/></a:stretch></p:blipFill>");
            _sb.Append($"<p:spPr><a:xfrm><a:off x=\"{Emu(bounds.X)}\" y=\"{Emu(bounds.Y)}\"/><a:ext cx=\"{Emu(bounds.Width)}\" cy=\"{Emu(bounds.Height)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
        }

        private void Table(TableElement table)
        {
            var rows = table.RowCount;
            var cols = table.ColumnCount;
            if (rows == 0 || cols == 0) return;
            var id = _nextId++;
            var widths = TableRenderer.ColumnWidths(table, table.Width);
            var rowHeight = table.Height / rows;
            var size = TableRenderer.FontSizeFor(table, table.Bounds, _style);
            var header = _style.Resolve(table.HeaderColor);
            var headerText = ChartRenderer.ContrastOn(header);
            var band = _style.Resolve("bg2");
            var text = _style.Resolve("text");
            var font = Esc(_style.Theme.Fonts.Body);
            _spids[table.Id] = id;
            _sb.Append($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{id}\" name=\"{Name(table, "Table")}\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
            _sb.Append($"<p:xfrm><a:off x=\"{Emu(table.X)}\" y=\"{Emu(table.Y)}\"/><a:ext cx=\"{Emu(table.Width)}\" cy=\"{Emu(table.Height)}\"/></p:xfrm>");
            _sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl>");
            _sb.Append($"<a:tblPr{(table.HeaderRow ? " firstRow=\"1\"" : "")}{(table.Banded ? " bandRow=\"1\"" : "")}/><a:tblGrid>");
            foreach (var w in widths) _sb.Append($"<a:gridCol w=\"{Emu(w)}\"/>");
            _sb.Append("</a:tblGrid>");
            for (var r = 0; r < rows; r++)
            {
                var isHeader = table.HeaderRow && r == 0;
                Color? fill = isHeader ? header : table.Banded && (r - (table.HeaderRow ? 1 : 0)) % 2 == 1 ? band : null;
                _sb.Append($"<a:tr h=\"{Emu(rowHeight)}\">");
                for (var c = 0; c < cols; c++)
                {
                    var value = c < table.Cells[r].Count ? table.Cells[r][c] ?? "" : "";
                    var bold = isHeader || (table.FirstColumnBold && c == 0);
                    var color = isHeader ? headerText : text;
                    _sb.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p>");
                    if (value.Length > 0)
                        _sb.Append($"<a:r><a:rPr lang=\"en-US\" sz=\"{FontSize(size)}\"{(bold ? " b=\"1\"" : "")} dirty=\"0\"><a:solidFill><a:srgbClr val=\"{Hex(color)}\"/></a:solidFill><a:latin typeface=\"{font}\"/></a:rPr><a:t>{Esc(value)}</a:t></a:r>");
                    _sb.Append($"<a:endParaRPr lang=\"en-US\" sz=\"{FontSize(size)}\" dirty=\"0\"/></a:p></a:txBody>");
                    var pad = Emu(Math.Max(size * 0.6, 8));
                    _sb.Append($"<a:tcPr marL=\"{pad}\" marR=\"{pad}\" marT=\"{pad / 2}\" marB=\"{pad / 2}\" anchor=\"ctr\">");
                    _sb.Append(fill is { } f ? $"<a:solidFill><a:srgbClr val=\"{Hex(f)}\"/></a:solidFill>" : "<a:noFill/>");
                    _sb.Append("</a:tcPr></a:tc>");
                }
                _sb.Append("</a:tr>");
            }
            _sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        }
    }
}
