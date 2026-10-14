using System.Globalization;
using System.Text;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.Export;

/// <summary>What the timing writer needs to know about a video or sound on the slide.</summary>
internal sealed class PptxMediaInfo
{
    public PptxMediaInfo(MediaClipElement clip, int spid)
    {
        Clip = clip;
        Spid = spid;
    }

    public MediaClipElement Clip { get; }
    public int Spid { get; }
}

/// <summary>
/// Writes a slide's animations as PowerPoint timing (p:timing): the main click sequence, trigger sequences and the
/// media nodes PowerPoint needs for videos and sounds. Effects PowerPoint has are written with their preset ids;
/// the others become the closest one (see <see cref="Mapping"/>). Text by paragraph targets each paragraph, by word
/// and letter uses PowerPoint's iterate.
/// </summary>
internal sealed class PptxTimingWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Slide _slide;
    private readonly Deck _deck;
    private readonly IReadOnlyDictionary<string, int> _spids;
    private readonly ISet<int> _textShapes;
    private readonly IReadOnlyList<PptxMediaInfo> _media;
    private readonly StringBuilder _sb = new();
    private readonly Dictionary<int, int> _nextGrp = new();
    private readonly Dictionary<(string Effect, int Spid), int> _paragraphGroups = new();
    private readonly List<(int Spid, int Grp, bool ByParagraph)> _builds = new();
    private int _id;

    public PptxTimingWriter(Slide slide, Deck deck, IReadOnlyDictionary<string, int> spids, ISet<int> textShapes, IReadOnlyList<PptxMediaInfo> media)
    {
        _slide = slide;
        _deck = deck;
        _spids = spids;
        _textShapes = textShapes;
        _media = media;
    }

    /// <summary>
    /// How Stagecraft's effects are written (the effect PowerPoint gets, its preset id). Effects PowerPoint doesn't
    /// have under the same name become the closest one.
    /// </summary>
    public static (EffectKind Kind, int PresetId) Mapping(EffectClass cls, EffectKind kind) => cls switch
    {
        EffectClass.Entrance or EffectClass.Exit => kind switch
        {
            EffectKind.Appear => (EffectKind.Appear, 1),
            EffectKind.Fly => (EffectKind.Fly, 2),
            EffectKind.Bounce => (EffectKind.Fly, 2),
            EffectKind.Wipe => (EffectKind.Wipe, 22),
            EffectKind.Split => (EffectKind.Split, 16),
            EffectKind.Wheel => (EffectKind.Wheel, 21),
            EffectKind.Zoom => (EffectKind.Zoom, 53),
            EffectKind.GrowTurn => (EffectKind.Zoom, 53),
            _ => (EffectKind.Fade, 10), // Fade, Float, Rise, Swivel
        },
        EffectClass.Emphasis => kind switch
        {
            EffectKind.Spin => (EffectKind.Spin, 8),
            EffectKind.Grow => (EffectKind.Grow, 6),
            EffectKind.Transparency => (EffectKind.Transparency, 9),
            _ => (EffectKind.Pulse, 26), // Pulse, Teeter, Wave, Colour pulse, Bold flash
        },
        EffectClass.Media => (kind, kind switch { EffectKind.Pause => 2, EffectKind.Stop => 3, _ => 1 }),
        _ => (kind, 0), // motion paths: custom paths
    };

    /// <summary>The p:timing element, or null when the slide has nothing animated and no media.</summary>
    public string? Build()
    {
        var timeline = SlideTimeline.Build(_slide);
        var hasMain = timeline.Groups.Any(g => g.Items.Any(Writable));
        var triggers = timeline.Triggers.Where(t => _spids.ContainsKey(t.Key) && t.Value.Any(g => g.Items.Any(Writable))).ToList();
        if (!hasMain && triggers.Count == 0 && _media.Count == 0) return null;

        _id = 1;
        _sb.Append("<p:timing><p:tnLst><p:par>");
        _sb.Append($"<p:cTn id=\"{_id++}\" dur=\"indefinite\" restart=\"never\" nodeType=\"tmRoot\"><p:childTnLst>");
        if (hasMain)
        {
            _sb.Append($"<p:seq concurrent=\"1\" nextAc=\"seek\"><p:cTn id=\"{_id++}\" dur=\"indefinite\" nodeType=\"mainSeq\"><p:childTnLst>");
            foreach (var group in timeline.Groups.Where(g => g.Items.Any(Writable))) Group(group, interactive: false);
            _sb.Append("</p:childTnLst></p:cTn>");
            _sb.Append("<p:prevCondLst><p:cond evt=\"onPrev\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst>");
            _sb.Append("<p:nextCondLst><p:cond evt=\"onNext\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst></p:seq>");
        }
        foreach (var (trigger, groups) in triggers) Interactive(_spids[trigger], () =>
        {
            foreach (var g in groups.Where(g => g.Items.Any(Writable))) Group(g, interactive: true);
        });
        // Clicking a video or sound plays and pauses it (PowerPoint's "When clicked on").
        foreach (var m in _media) Interactive(m.Spid, () => MediaToggle(m));
        foreach (var m in _media) MediaNode(m);
        _sb.Append("</p:childTnLst></p:cTn></p:par></p:tnLst>");
        if (_builds.Count > 0)
        {
            _sb.Append("<p:bldLst>");
            foreach (var (spid, grp, byParagraph) in _builds)
                _sb.Append(byParagraph ? $"<p:bldP spid=\"{spid}\" grpId=\"{grp}\" build=\"p\"/>" : $"<p:bldP spid=\"{spid}\" grpId=\"{grp}\" animBg=\"1\"/>");
            _sb.Append("</p:bldLst>");
        }
        _sb.Append("</p:timing>");
        return _sb.ToString();
    }

    private bool Writable(TimelineItem item) => TargetsOf(item.Effect).Count > 0;

    private List<int> TargetsOf(AnimationEffect effect)
    {
        var result = new List<int>();
        foreach (var e in AnimationRules.Resolve(_slide, effect.Target))
            if (_spids.TryGetValue(e.Id, out var spid)) result.Add(spid);
        return result;
    }

    private static string Ms(double seconds) => ((long)Math.Round(Math.Max(0, seconds) * 1000)).ToString(Inv);

    private void Interactive(int spid, Action content)
    {
        var trigger = $"<p:cond evt=\"onClick\" delay=\"0\"><p:tgtEl><p:spTgt spid=\"{spid}\"/></p:tgtEl></p:cond>";
        _sb.Append($"<p:seq concurrent=\"1\" nextAc=\"seek\"><p:cTn id=\"{_id++}\" restart=\"whenNotActive\" fill=\"hold\" evtFilter=\"cancelBubble\" nodeType=\"interactiveSeq\">");
        _sb.Append("<p:stCondLst>").Append(trigger).Append("</p:stCondLst>");
        _sb.Append("<p:endSync evt=\"end\" delay=\"0\"><p:rtn val=\"all\"/></p:endSync><p:childTnLst>");
        content();
        _sb.Append("</p:childTnLst></p:cTn><p:nextCondLst>").Append(trigger).Append("</p:nextCondLst></p:seq>");
    }

    /// <summary>One click's effects: grouped into the runs that start together ("after previous" starts a new run).</summary>
    private void Group(ClickGroup group, bool interactive)
    {
        _sb.Append($"<p:par><p:cTn id=\"{_id++}\" fill=\"hold\"><p:stCondLst>");
        if (interactive) _sb.Append("<p:cond delay=\"0\"/>");
        else
        {
            _sb.Append("<p:cond delay=\"indefinite\"/>");
            if (group.Automatic) _sb.Append("<p:cond evt=\"onBegin\" delay=\"0\"><p:tn val=\"2\"/></p:cond>");
        }
        _sb.Append("</p:stCondLst><p:childTnLst>");
        var items = group.Items.Where(Writable).ToList();
        var runs = items.GroupBy(i => (long)Math.Round((i.Begin - Math.Max(0, i.Effect.Delay)) * 1000)).OrderBy(g => g.Key);
        var first = true;
        foreach (var run in runs)
        {
            _sb.Append($"<p:par><p:cTn id=\"{_id++}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"{run.Key.ToString(Inv)}\"/></p:stCondLst><p:childTnLst>");
            foreach (var item in run)
            {
                var node = first && !group.Automatic ? "clickEffect"
                    : item.Effect.Start == EffectStart.AfterPrevious ? "afterEffect"
                    : "withEffect";
                Effect(item, node);
                first = false;
            }
            _sb.Append("</p:childTnLst></p:cTn></p:par>");
        }
        _sb.Append("</p:childTnLst></p:cTn></p:par>");
    }

    private void Effect(TimelineItem item, string node)
    {
        var effect = item.Effect;
        var targets = TargetsOf(effect);
        for (var t = 0; t < targets.Count; t++)
        {
            var spid = targets[t];
            var body = AnimationRules.Resolve(_slide, effect.Target).Select(TextUnits.BodyOf).ElementAtOrDefault(t);
            var textShape = _textShapes.Contains(spid) && body != null;
            // By paragraph (all on one click): one effect per paragraph, staggered.
            if (item.Paragraph < 0 && textShape && effect.TextUnit == TextUnit.Paragraph && effect.Class != EffectClass.Media)
            {
                var paragraphs = TextUnits.NonEmptyParagraphs(body!);
                var stagger = item.Stagger > 0 ? item.Stagger : SlideTimeline.DefaultStagger(TextUnit.Paragraph);
                var grp = NextGroup(spid, byParagraph: true);
                for (var k = 0; k < paragraphs.Count; k++)
                    One(effect, spid, paragraphs[k], grp, t == 0 && k == 0 ? node : "withEffect", effect.Delay + k * stagger, iterate: null);
                continue;
            }
            var paragraph = textShape ? item.Paragraph : -1;
            int group;
            if (effect.Class == EffectClass.Media) group = 0;
            else if (paragraph >= 0 && _paragraphGroups.TryGetValue((effect.Id, spid), out var shared)) group = shared; // each paragraph on its own click: one build
            else
            {
                group = NextGroup(spid, byParagraph: paragraph >= 0 && textShape);
                if (paragraph >= 0) _paragraphGroups[(effect.Id, spid)] = group;
            }
            string? iterate = null;
            if (textShape && effect.TextUnit is TextUnit.Word or TextUnit.Letter && effect.Class != EffectClass.Media)
            {
                var stagger = effect.TextStagger ?? SlideTimeline.DefaultStagger(effect.TextUnit);
                iterate = $"<p:iterate type=\"{(effect.TextUnit == TextUnit.Word ? "wd" : "lt")}\"><p:tmAbs val=\"{Ms(stagger)}\"/></p:iterate>";
            }
            One(effect, spid, paragraph, group, t == 0 ? node : "withEffect", effect.Delay, iterate);
        }
    }

    private int NextGroup(int spid, bool byParagraph)
    {
        _nextGrp.TryGetValue(spid, out var grp);
        _nextGrp[spid] = grp + 1;
        if (_textShapes.Contains(spid)) _builds.Add((spid, grp, byParagraph));
        return grp;
    }

    private static string Target(int spid, int paragraph) => paragraph >= 0
        ? $"<p:tgtEl><p:spTgt spid=\"{spid}\"><p:txEl><p:pRg st=\"{paragraph}\" end=\"{paragraph}\"/></p:txEl></p:spTgt></p:tgtEl>"
        : $"<p:tgtEl><p:spTgt spid=\"{spid}\"/></p:tgtEl>";

    /// <summary>One effect node (with its behaviours) on one shape or paragraph.</summary>
    private void One(AnimationEffect effect, int spid, int paragraph, int grp, string node, double delay, string? iterate)
    {
        var (kind, preset) = Mapping(effect.Class, effect.Effect);
        var cls = effect.Class switch
        {
            EffectClass.Entrance => "entr",
            EffectClass.Exit => "exit",
            EffectClass.Emphasis => "emph",
            EffectClass.Motion => "path",
            _ => "mediacall",
        };
        var subtype = Subtype(effect, kind);
        var dur = effect.Class == EffectClass.Media ? MediaLength(effect) : Math.Max(0.01, effect.Duration);
        var attrs = new StringBuilder($" id=\"{_id++}\" presetID=\"{preset}\" presetClass=\"{cls}\" presetSubtype=\"{subtype}\"");
        if (effect.Class != EffectClass.Media)
        {
            if (effect.RepeatUntil != RepeatUntil.None) attrs.Append(" repeatCount=\"indefinite\"");
            else if (effect.Repeat > 1) attrs.Append($" repeatCount=\"{effect.Repeat * 1000}\"");
            switch (effect.Easing)
            {
                case EffectEasing.EaseIn:
                    attrs.Append(" accel=\"50000\"");
                    break;
                case EffectEasing.EaseOut:
                    attrs.Append(" decel=\"50000\"");
                    break;
                case EffectEasing.EaseInOut:
                case EffectEasing.Auto when effect.Class is EffectClass.Emphasis or EffectClass.Motion:
                    attrs.Append(" accel=\"50000\" decel=\"50000\"");
                    break;
            }
        }
        attrs.Append(effect.Rewind && effect.Class != EffectClass.Media ? " fill=\"remove\"" : " fill=\"hold\"");
        attrs.Append($" grpId=\"{grp}\" nodeType=\"{node}\"");
        _sb.Append($"<p:par><p:cTn{attrs}><p:stCondLst><p:cond delay=\"{Ms(delay)}\"/></p:stCondLst>");
        if (effect.RepeatUntil == RepeatUntil.NextClick)
            _sb.Append("<p:endCondLst><p:cond evt=\"onNext\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:endCondLst>");
        if (iterate != null) _sb.Append(iterate);
        _sb.Append("<p:childTnLst>");
        Behaviours(effect, kind, Target(spid, paragraph), dur);
        _sb.Append("</p:childTnLst></p:cTn></p:par>");
    }

    private double MediaLength(AnimationEffect effect) =>
        AnimationRules.Resolve(_slide, effect.Target).OfType<MediaClipElement>().FirstOrDefault()?.PlayLength is { } l && l > 0 ? l : 0.001;

    private static int Subtype(AnimationEffect effect, EffectKind kind)
    {
        if (effect.Class is EffectClass.Entrance or EffectClass.Exit)
        {
            var d = effect.Effect == EffectKind.Bounce ? EffectDirection.Top : effect.Direction;
            switch (kind)
            {
                case EffectKind.Fly:
                    return d switch
                    {
                        EffectDirection.Top => 1, EffectDirection.Right => 2, EffectDirection.TopRight => 3, EffectDirection.Bottom => 4,
                        EffectDirection.BottomRight => 6, EffectDirection.Left => 8, EffectDirection.TopLeft => 9, EffectDirection.BottomLeft => 12,
                        _ => 4,
                    };
                case EffectKind.Wipe:
                    return d switch { EffectDirection.Top => 1, EffectDirection.Right => 2, EffectDirection.Left => 8, _ => 4 };
                case EffectKind.Zoom:
                    return 16;
                case EffectKind.Split:
                    return d switch { EffectDirection.VerticalOut => 42, EffectDirection.HorizontalIn => 26, EffectDirection.HorizontalOut => 37, _ => 21 };
                case EffectKind.Wheel:
                    return 1;
            }
        }
        return 0;
    }

    private string CTn(double seconds, string? extra = null, bool hold = true) =>
        $"<p:cTn id=\"{_id++}\" dur=\"{Ms(seconds)}\"{(hold ? " fill=\"hold\"" : "")}{extra}/>";

    private void Set(string target, string attribute, string value, double delaySeconds)
    {
        _sb.Append($"<p:set><p:cBhvr><p:cTn id=\"{_id++}\" dur=\"1\" fill=\"hold\"><p:stCondLst><p:cond delay=\"{Ms(delaySeconds)}\"/></p:stCondLst></p:cTn>");
        _sb.Append(target).Append($"<p:attrNameLst><p:attrName>{attribute}</p:attrName></p:attrNameLst></p:cBhvr><p:to><p:strVal val=\"{value}\"/></p:to></p:set>");
    }

    private void Filter(string target, bool entering, string filter, double dur) =>
        _sb.Append($"<p:animEffect transition=\"{(entering ? "in" : "out")}\" filter=\"{filter}\"><p:cBhvr>{CTn(dur, hold: false)}{target}</p:cBhvr></p:animEffect>");

    private void Anim(string target, string attribute, string from, string to, double dur)
    {
        _sb.Append($"<p:anim calcmode=\"lin\" valueType=\"num\"><p:cBhvr additive=\"base\">{CTn(dur)}{target}");
        _sb.Append($"<p:attrNameLst><p:attrName>{attribute}</p:attrName></p:attrNameLst></p:cBhvr><p:tavLst>");
        _sb.Append($"<p:tav tm=\"0\"><p:val><p:strVal val=\"{from}\"/></p:val></p:tav><p:tav tm=\"100000\"><p:val><p:strVal val=\"{to}\"/></p:val></p:tav>");
        _sb.Append("</p:tavLst></p:anim>");
    }

    private void Behaviours(AnimationEffect effect, EffectKind kind, string target, double dur)
    {
        switch (effect.Class)
        {
            case EffectClass.Entrance:
            case EffectClass.Exit:
            {
                var entering = effect.Class == EffectClass.Entrance;
                if (entering) Set(target, "style.visibility", "visible", 0);
                switch (kind)
                {
                    case EffectKind.Appear:
                        break;
                    case EffectKind.Fade:
                        Filter(target, entering, "fade", dur);
                        break;
                    case EffectKind.Wipe:
                    {
                        var d = Subtype(effect, kind);
                        // The filter names the way the edge moves: from the bottom it moves up.
                        var way = d switch { 1 => "down", 2 => "left", 8 => "right", _ => "up" };
                        if (!entering) way = way switch { "down" => "up", "up" => "down", "left" => "right", _ => "left" };
                        Filter(target, entering, $"wipe({way})", dur);
                        break;
                    }
                    case EffectKind.Split:
                        Filter(target, entering, effect.Direction switch
                        {
                            EffectDirection.VerticalOut => "barn(outVertical)",
                            EffectDirection.HorizontalIn => "barn(inHorizontal)",
                            EffectDirection.HorizontalOut => "barn(outHorizontal)",
                            _ => "barn(inVertical)",
                        }, dur);
                        break;
                    case EffectKind.Wheel:
                        Filter(target, entering, "wheel(1)", dur);
                        break;
                    case EffectKind.Zoom:
                        Anim(target, "ppt_w", entering ? "0" : "#ppt_w", entering ? "#ppt_w" : "0", dur);
                        Anim(target, "ppt_h", entering ? "0" : "#ppt_h", entering ? "#ppt_h" : "0", dur);
                        Filter(target, entering, "fade", dur);
                        break;
                    case EffectKind.Fly:
                    {
                        var d = effect.Effect == EffectKind.Bounce ? EffectDirection.Top : effect.Direction;
                        var (x, y) = d switch
                        {
                            EffectDirection.Left => ("0-#ppt_w/2", "#ppt_y"),
                            EffectDirection.Right => ("1+#ppt_w/2", "#ppt_y"),
                            EffectDirection.Top => ("#ppt_x", "0-#ppt_h/2"),
                            EffectDirection.TopLeft => ("0-#ppt_w/2", "0-#ppt_h/2"),
                            EffectDirection.TopRight => ("1+#ppt_w/2", "0-#ppt_h/2"),
                            EffectDirection.BottomLeft => ("0-#ppt_w/2", "1+#ppt_h/2"),
                            EffectDirection.BottomRight => ("1+#ppt_w/2", "1+#ppt_h/2"),
                            _ => ("#ppt_x", "1+#ppt_h/2"),
                        };
                        Anim(target, "ppt_x", entering ? x : "#ppt_x", entering ? "#ppt_x" : x, dur);
                        Anim(target, "ppt_y", entering ? y : "#ppt_y", entering ? "#ppt_y" : y, dur);
                        break;
                    }
                }
                if (!entering) Set(target, "style.visibility", "hidden", Math.Max(0, dur - 0.001));
                break;
            }
            case EffectClass.Emphasis:
                switch (kind)
                {
                    case EffectKind.Spin:
                        _sb.Append($"<p:animRot by=\"{(long)Math.Round(effect.EffectiveAmount * 60000)}\"><p:cBhvr>{CTn(dur)}{target}<p:attrNameLst><p:attrName>r</p:attrName></p:attrNameLst></p:cBhvr></p:animRot>");
                        break;
                    case EffectKind.Grow:
                    {
                        var by = (long)Math.Round(effect.EffectiveAmount * 1000);
                        _sb.Append($"<p:animScale><p:cBhvr>{CTn(dur)}{target}</p:cBhvr><p:by x=\"{by}\" y=\"{by}\"/></p:animScale>");
                        break;
                    }
                    case EffectKind.Transparency:
                    {
                        var opacity = (1 - MathEx.Clamp(effect.EffectiveAmount, 0, 100) / 100).ToString("0.##", Inv);
                        _sb.Append($"<p:set><p:cBhvr>{CTn(dur)}{target}<p:attrNameLst><p:attrName>style.opacity</p:attrName></p:attrNameLst></p:cBhvr><p:to><p:strVal val=\"{opacity}\"/></p:to></p:set>");
                        _sb.Append($"<p:animEffect filter=\"image\" prLst=\"opacity: {opacity}\"><p:cBhvr rctx=\"IE\">{CTn(dur)}{target}</p:cBhvr></p:animEffect>");
                        break;
                    }
                    default:
                        // Pulse: grows a little and back.
                        _sb.Append($"<p:animScale><p:cBhvr><p:cTn id=\"{_id++}\" dur=\"{Ms(dur / 2)}\" autoRev=\"1\" fill=\"hold\"/>{target}</p:cBhvr><p:by x=\"105000\" y=\"105000\"/></p:animScale>");
                        break;
                }
                break;
            case EffectClass.Motion:
            {
                var path = PathText(effect);
                _sb.Append($"<p:animMotion origin=\"layout\" path=\"{path}\" pathEditMode=\"relative\"><p:cBhvr>{CTn(dur)}{target}");
                _sb.Append("<p:attrNameLst><p:attrName>ppt_x</p:attrName><p:attrName>ppt_y</p:attrName></p:attrNameLst></p:cBhvr></p:animMotion>");
                break;
            }
            case EffectClass.Media:
            {
                var clip = AnimationRules.Resolve(_slide, effect.Target).OfType<MediaClipElement>().FirstOrDefault();
                var cmd = kind switch
                {
                    EffectKind.Pause => "togglePause",
                    EffectKind.Stop => "stop",
                    _ => "playFrom(" + (clip?.TrimStart ?? 0).ToString("0.0##", Inv) + ")",
                };
                _sb.Append($"<p:cmd type=\"call\" cmd=\"{cmd}\"><p:cBhvr>{CTn(dur)}{target}</p:cBhvr></p:cmd>");
                break;
            }
        }
    }

    /// <summary>A motion path in PowerPoint's form: fractions of the slide, starting at 0 0, "E" at the end.</summary>
    private string PathText(AnimationEffect effect)
    {
        var path = effect.Path ?? EffectCatalog.DefaultPath(effect.Effect, 200, 200);
        var points = path.Smooth || path.Closed ? PathMath.Flatten(path, 8) : path.Points.Select(p => new System.Windows.Point(p.X, p.Y)).ToList();
        if (points.Count == 0) return "M 0 0 E";
        var origin = points[0];
        string F(double v) => v.ToString("0.#####", Inv);
        var sb = new StringBuilder("M 0 0");
        foreach (var p in points.Skip(1)) sb.Append($" L {F((p.X - origin.X) / _deck.Width)} {F((p.Y - origin.Y) / _deck.Height)}");
        if (path.Closed) sb.Append(" Z");
        sb.Append(" E");
        return sb.ToString();
    }

    private void MediaToggle(PptxMediaInfo m)
    {
        _sb.Append($"<p:par><p:cTn id=\"{_id++}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>");
        _sb.Append($"<p:par><p:cTn id=\"{_id++}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>");
        _sb.Append($"<p:par><p:cTn id=\"{_id++}\" presetID=\"2\" presetClass=\"mediacall\" presetSubtype=\"0\" fill=\"hold\" nodeType=\"clickEffect\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>");
        _sb.Append($"<p:cmd type=\"call\" cmd=\"togglePause\"><p:cBhvr>{CTn(0.001)}<p:tgtEl><p:spTgt spid=\"{m.Spid}\"/></p:tgtEl></p:cBhvr></p:cmd>");
        _sb.Append("</p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par>");
    }

    /// <summary>The p:video or p:audio node: volume, looping, hiding while stopped, playing across slides.</summary>
    private void MediaNode(PptxMediaInfo m)
    {
        var clip = m.Clip;
        var video = clip is VideoElement;
        var vol = (long)Math.Round(MathEx.Clamp(clip.Volume, 0, 1) * 100000);
        var attrs = $" vol=\"{vol}\"";
        if (clip.Volume <= 0) attrs += " mute=\"1\"";
        if (clip is AudioElement { AcrossSlides: true } a) attrs += $" numSld=\"{(a.StopAfterSlides is { } n && n > 0 ? n : 999)}\"";
        if (clip is AudioElement { HideIcon: true } || clip is VideoElement { HideWhenNotPlaying: true }) attrs += " showWhenStopped=\"0\"";
        _sb.Append(video ? $"<p:video{(clip is VideoElement { FullScreen: true } ? " fullScrn=\"1\"" : "")}>" : "<p:audio>");
        _sb.Append($"<p:cMediaNode{attrs}><p:cTn id=\"{_id++}\"{(clip.Loop ? " repeatCount=\"indefinite\"" : "")} fill=\"hold\" display=\"0\">");
        _sb.Append("<p:stCondLst><p:cond delay=\"indefinite\"/></p:stCondLst>");
        if (clip is AudioElement { AcrossSlides: true })
            _sb.Append("<p:endCondLst><p:cond evt=\"onStopAudio\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:endCondLst>");
        _sb.Append($"</p:cTn><p:tgtEl><p:spTgt spid=\"{m.Spid}\"/></p:tgtEl></p:cMediaNode>");
        _sb.Append(video ? "</p:video>" : "</p:audio>");
    }
}
