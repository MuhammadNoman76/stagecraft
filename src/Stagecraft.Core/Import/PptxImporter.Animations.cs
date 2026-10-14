using System.Globalization;
using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Stagecraft.Core.Import;

public static partial class PptxImporter
{
    /// <summary>Links, videos and sounds, and animations (p:timing).</summary>
    private sealed partial class Importer
    {
        private const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

        private readonly Dictionary<SlidePart, string> _slideIds = new();
        private readonly Dictionary<uint, string> _spids = new(); // shape id on the current slide → element (or group) id
        private readonly Dictionary<string, string> _extracted = new(); // media part → media name
        private OpenXmlPart? _owner;
        private int _approximatedEffects;
        private int _droppedEffects;
        private int _unreadableMedia;

        private static string? Attr(OpenXmlElement? e, string localName) =>
            e?.GetAttributes().FirstOrDefault(a => a.LocalName == localName).Value;

        private static OpenXmlElement? Child(OpenXmlElement? e, string localName) => e?.ChildElements.FirstOrDefault(c => c.LocalName == localName);

        private static IEnumerable<OpenXmlElement> Children(OpenXmlElement? e, string localName) =>
            e?.ChildElements.Where(c => c.LocalName == localName) ?? Enumerable.Empty<OpenXmlElement>();

        private static double Ms(string? value) =>
            value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 1000.0 : 0;

        /// <summary>An element read from the slide: remember its shape id (animations refer to it) and take its link.</summary>
        private void Added(Element e, P.NonVisualDrawingProperties? nv, OpenXmlPart? owner)
        {
            if (owner is SlidePart && nv?.Id?.Value is { } id) _spids[id] = e.Id;
            e.Link ??= ReadLink(nv?.GetFirstChild<A.HyperlinkOnClick>(), owner);
        }

        // ------------------------------------------------------------------ links

        /// <summary>
        /// A PowerPoint hyperlink as a Stagecraft link: web pages and e-mail, jumps to slides (next, previous, first,
        /// last, last viewed, a particular slide) and files. Macros, programs and other actions are left out.
        /// </summary>
        private Hyperlink? ReadLink(OpenXmlElement? h, OpenXmlPart? owner)
        {
            if (h == null || owner == null) return null;
            var action = Attr(h, "action") ?? "";
            var id = Attr(h, "id");
            Hyperlink? link = null;
            if (action.StartsWith("ppaction://hlinkshowjump", StringComparison.Ordinal))
            {
                var jump = action.Substring(action.IndexOf("jump=", StringComparison.Ordinal) + 5);
                link = jump switch
                {
                    "nextslide" => Hyperlink.ToSlide(SlideJump.Next),
                    "previousslide" => Hyperlink.ToSlide(SlideJump.Previous),
                    "firstslide" => Hyperlink.ToSlide(SlideJump.First),
                    "lastslide" => Hyperlink.ToSlide(SlideJump.Last),
                    "lastslideviewed" => Hyperlink.ToSlide(SlideJump.LastViewed),
                    _ => null,
                };
            }
            else if (action == "ppaction://hlinksldjump")
            {
                if (!string.IsNullOrEmpty(id))
                {
                    try
                    {
                        if (owner.GetPartById(id!) is SlidePart target && _slideIds.TryGetValue(target, out var slideId))
                            link = Hyperlink.ToSlide(SlideJump.Specific, slideId);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // a broken link
                    }
                }
            }
            else if (action.Length > 0 && !action.StartsWith("ppaction://hlinkfile", StringComparison.Ordinal))
            {
                return null; // media, macros, programs, OLE verbs
            }
            else if (!string.IsNullOrEmpty(id))
            {
                var rel = owner.HyperlinkRelationships.FirstOrDefault(r => r.Id == id);
                if (rel == null) return null;
                var uri = rel.Uri;
                if (uri.IsAbsoluteUri)
                {
                    switch (uri.Scheme.ToLowerInvariant())
                    {
                        case "http":
                        case "https":
                            link = Hyperlink.Web(uri.OriginalString);
                            break;
                        case "mailto":
                        {
                            var text = uri.OriginalString.Substring("mailto:".Length);
                            var q = text.IndexOf('?');
                            var address = Uri.UnescapeDataString(q >= 0 ? text.Substring(0, q) : text);
                            string? subject = null;
                            if (q >= 0)
                                foreach (var pair in text.Substring(q + 1).Split('&'))
                                    if (pair.StartsWith("subject=", StringComparison.OrdinalIgnoreCase)) subject = Uri.UnescapeDataString(pair.Substring(8));
                            link = Hyperlink.Email(address, subject);
                            break;
                        }
                        case "file":
                            link = Hyperlink.ToFile(uri.LocalPath);
                            break;
                    }
                }
                else
                {
                    link = Hyperlink.ToFile(Uri.UnescapeDataString(uri.OriginalString).Replace('/', Path.DirectorySeparatorChar));
                }
            }
            var tip = Attr(h, "tooltip");
            if (link != null && !string.IsNullOrWhiteSpace(tip)) link.ScreenTip = tip;
            return link;
        }

        // ------------------------------------------------------------------ video and sound

        /// <summary>A PowerPoint video or sound: embedded (copied into the presentation) or linked, with trim and fades.</summary>
        private bool ReadMedia(P.Picture pic, OpenXmlElement app, Slide slide, OpenXmlPart owner, Rect box, double rot, bool fh, bool fv, string? groupId)
        {
            var file = app.ChildElements.FirstOrDefault(c => c.LocalName is "videoFile" or "quickTimeFile" or "audioFile");
            if (file == null) return false;
            var video = file.LocalName != "audioFile";
            var p14 = app.Descendants().FirstOrDefault(e => e.LocalName == "media" && e.NamespaceUri == P14);
            MediaClipElement clip = video ? new VideoElement() : new AudioElement();
            string? name = null;
            foreach (var rid in new[] { Attr(p14, "embed"), Attr(file, "link") })
            {
                if (string.IsNullOrEmpty(rid)) continue;
                var rel = owner.DataPartReferenceRelationships.FirstOrDefault(r => r.Id == rid);
                if (rel?.DataPart is MediaDataPart part)
                {
                    name = Extract(part);
                    clip.FileName = Path.GetFileNameWithoutExtension(part.Uri.OriginalString) + Path.GetExtension(name);
                    break;
                }
            }
            if (name != null) clip.Media = name;
            else
            {
                var linkId = Attr(file, "link") ?? Attr(p14, "link");
                var external = owner.ExternalRelationships.FirstOrDefault(r => r.Id == linkId);
                if (external == null)
                {
                    _unreadableMedia++;
                    return false;
                }
                var uri = external.Uri;
                var path = uri.IsAbsoluteUri ? (uri.IsFile ? uri.LocalPath : uri.OriginalString) : Uri.UnescapeDataString(uri.OriginalString).Replace('/', Path.DirectorySeparatorChar);
                if (uri.IsAbsoluteUri && !uri.IsFile)
                {
                    _unreadableMedia++; // online media PowerPoint links to
                    return false;
                }
                clip.LinkPath = path;
                clip.LinkAbsolute = Path.IsPathRooted(path) ? path : null;
                clip.FileName = Path.GetFileName(path);
            }
            if (!MediaTypes.IsMedia(clip.FileName ?? clip.Media ?? ""))
            {
                _unreadableMedia++;
                return false;
            }

            // Length from the file's header where it can be read; trim and fades from PowerPoint 2010's extension.
            if (clip.Media != null && _media.Get(clip.Media)?.FilePath is { } stored) clip.Length = MediaDuration.TryRead(stored);
            if (Child(p14, "trim") is { } trim)
            {
                clip.TrimStart = Math.Round(Ms(Attr(trim, "st")), 3);
                var end = Ms(Attr(trim, "end"));
                if (end > 0 && clip.Length is { } len && len - end > clip.TrimStart) clip.TrimEnd = Math.Round(len - end, 3);
            }
            if (Child(p14, "fade") is { } fade)
            {
                clip.FadeIn = Math.Round(Ms(Attr(fade, "in")), 3);
                clip.FadeOut = Math.Round(Ms(Attr(fade, "out")), 3);
            }
            if (clip is VideoElement v && pic.BlipFill?.Blip is { } blip && ReadBlip(blip, owner) is { } poster) v.Poster = poster;
            if (clip is VideoElement vg)
            {
                var geometry = pic.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.InnerText;
                if (geometry == "ellipse") vg.Mask = ImageMask.Circle;
                else if (geometry == "roundRect") vg.CornerRadius = Math.Round(Math.Min(box.Width, box.Height) * 0.1667, 1);
            }
            clip.Name = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
            Place(clip, box, rot, fh, fv, groupId);
            slide.Elements.Add(clip);
            Added(clip, pic.NonVisualPictureProperties?.NonVisualDrawingProperties, owner);
            return true;
        }

        /// <summary>Copies a media part into the presentation's media (through a file: videos can be large).</summary>
        private string? Extract(MediaDataPart part)
        {
            var key = part.Uri.OriginalString;
            if (_extracted.TryGetValue(key, out var known)) return known;
            var ext = Path.GetExtension(key).ToLowerInvariant();
            if (!MediaTypes.IsMedia("x" + ext)) ext = MediaTypes.ExtensionFor(part.ContentType) ?? (ext.Length > 0 ? ext : ".bin");
            var temp = MediaCache.PathFor("import-" + Ids.New() + ext);
            try
            {
                MediaCache.WriteAtomically(temp, output =>
                {
                    using var input = part.GetStream(FileMode.Open, FileAccess.Read);
                    input.CopyTo(output, 1 << 16);
                });
                var name = _media.AddFile(temp);
                _extracted[key] = name;
                return name;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return null;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }
        }

        // ------------------------------------------------------------------ animations

        /// <summary>
        /// Reads the slide's animations: the main sequence, trigger sequences and media settings. Effects Stagecraft
        /// doesn't have become the closest one; effects on things that weren't imported are left out (both counted).
        /// </summary>
        private void ReadTiming(P.Slide? data, Slide slide, SlidePart part)
        {
            var timing = data?.ChildElements.FirstOrDefault(c => c.LocalName == "timing");
            if (timing == null) return;
            var root = timing.Descendants().FirstOrDefault(e => e.LocalName == "cTn" && Attr(e, "nodeType") == "tmRoot");
            if (root == null) return;
            foreach (var node in Child(root, "childTnLst")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
            {
                try
                {
                    switch (node.LocalName)
                    {
                        case "seq":
                        {
                            var ctn = Child(node, "cTn");
                            var type = Attr(ctn, "nodeType");
                            if (type == "mainSeq") ReadSequence(ctn!, slide, null);
                            else if (type == "interactiveSeq")
                            {
                                var spid = Attr(ctn!.Descendants().FirstOrDefault(e => e.LocalName == "spTgt" && e.Parent?.Parent?.Parent?.LocalName == "stCondLst"), "spid");
                                if (spid != null && uint.TryParse(spid, out var t) && _spids.TryGetValue(t, out var trigger)) ReadSequence(ctn, slide, trigger);
                                else _droppedEffects += ctn.Descendants().Count(e => e.LocalName == "cTn" && Attr(e, "presetClass") != null);
                            }
                            break;
                        }
                        case "video":
                        case "audio":
                            ReadMediaNode(node, slide);
                            break;
                    }
                }
                catch (Exception)
                {
                    _droppedEffects++;
                }
            }
            Animation.AnimationRules.Prune(slide);
        }

        private void ReadSequence(OpenXmlElement sequence, Slide slide, string? trigger)
        {
            // Paragraph builds come as one effect per paragraph: merge them back into one effect.
            var builds = new Dictionary<(string Target, string Group, string Class, string Preset), AnimationEffect>();
            foreach (var ctn in sequence.Descendants().Where(e => e.LocalName == "cTn" && Attr(e, "presetClass") != null).ToList())
            {
                var cls = Attr(ctn, "presetClass")!;
                int.TryParse(Attr(ctn, "presetID"), out var preset);
                int.TryParse(Attr(ctn, "presetSubtype"), out var subtype);
                var target = ctn.Descendants().FirstOrDefault(e => e.LocalName == "spTgt" && e.Ancestors().Any(a => a.LocalName == "tgtEl"));
                if (!uint.TryParse(Attr(target, "spid"), out var spid) || !_spids.TryGetValue(spid, out var targetId))
                {
                    _droppedEffects++;
                    continue;
                }
                var paragraph = int.TryParse(Attr(Child(Child(target, "txEl"), "pRg"), "st"), out var pr) ? pr : -1;

                // PowerPoint's "When clicked on" for media: play or pause triggered by the media itself (Stagecraft does that
                // for every video and sound without an effect).
                if (cls == "mediacall" && trigger == targetId) continue;

                var effect = Effect(ctn, cls, preset, subtype, targetId, slide);
                if (effect == null)
                {
                    _droppedEffects++;
                    continue;
                }
                effect.Trigger = trigger;
                var node = Attr(ctn, "nodeType");
                effect.Start = node switch { "withEffect" => EffectStart.WithPrevious, "afterEffect" => EffectStart.AfterPrevious, _ => EffectStart.OnClick };

                if (paragraph >= 0 && effect.Class != EffectClass.Media)
                {
                    var key = (targetId, Attr(ctn, "grpId") ?? "", cls, preset.ToString(CultureInfo.InvariantCulture));
                    if (builds.TryGetValue(key, out var first))
                    {
                        if (effect.Start == EffectStart.OnClick) first.ParagraphClicks = true;
                        else if (effect.Delay > first.Delay && first.TextStagger == null) first.TextStagger = Math.Round(effect.Delay - first.Delay, 3);
                        continue;
                    }
                    effect.TextUnit = TextUnit.Paragraph;
                    builds[key] = effect;
                }
                slide.Animations.Add(effect);
            }
        }

        private AnimationEffect? Effect(OpenXmlElement ctn, string cls, int preset, int subtype, string targetId, Slide slide)
        {
            var effectClass = cls switch
            {
                "entr" => EffectClass.Entrance,
                "exit" => EffectClass.Exit,
                "emph" => EffectClass.Emphasis,
                "path" => EffectClass.Motion,
                "mediacall" => EffectClass.Media,
                _ => (EffectClass?)null,
            };
            if (effectClass == null) return null;
            var c = effectClass.Value;
            var behaviours = Child(ctn, "childTnLst");
            EffectKind kind;
            var approximated = false;
            switch (c)
            {
                case EffectClass.Entrance:
                case EffectClass.Exit:
                    kind = preset switch
                    {
                        1 => EffectKind.Appear,
                        2 => EffectKind.Fly,
                        10 => EffectKind.Fade,
                        16 => EffectKind.Split,
                        21 => EffectKind.Wheel,
                        22 => EffectKind.Wipe,
                        23 or 53 => EffectKind.Zoom,
                        26 => EffectKind.Bounce,
                        31 => EffectKind.GrowTurn,
                        37 => EffectKind.Rise,
                        30 or 42 or 47 => EffectKind.Float,
                        19 => EffectKind.Swivel,
                        _ => EffectKind.Fade,
                    };
                    approximated = preset is not (1 or 2 or 10 or 16 or 19 or 21 or 22 or 23 or 53 or 26 or 30 or 31 or 37 or 42 or 47);
                    break;
                case EffectClass.Emphasis:
                    kind = preset switch
                    {
                        6 => EffectKind.Grow,
                        8 => EffectKind.Spin,
                        9 => EffectKind.Transparency,
                        10 => EffectKind.BoldFlash,
                        26 => EffectKind.Pulse,
                        32 => EffectKind.Teeter,
                        34 => EffectKind.Wave,
                        _ => EffectKind.Pulse,
                    };
                    approximated = preset is not (6 or 8 or 9 or 10 or 26 or 32 or 34);
                    break;
                case EffectClass.Motion:
                    kind = EffectKind.Custom;
                    break;
                default:
                    kind = preset switch { 2 => EffectKind.Pause, 3 => EffectKind.Stop, _ => EffectKind.Play };
                    if (!Animation.AnimationRules.Resolve(slide, targetId).Any(e => e is MediaClipElement)) return null;
                    break;
            }
            if (approximated) _approximatedEffects++;
            var effect = EffectCatalog.Create(c, kind, targetId);

            // Timing.
            effect.Delay = Math.Round(Ms(Attr(Child(Child(ctn, "stCondLst"), "cond"), "delay")), 3);
            var durations = behaviours?.Descendants().Where(e => e.LocalName == "cTn").Select(e => Attr(e, "dur")).Where(d => d != null && d != "indefinite")
                .Select(d => Ms(d)).Where(d => d > 0.002).ToList() ?? new List<double>();
            if (c != EffectClass.Media && durations.Count > 0)
            {
                var d = durations.Max();
                if (kind == EffectKind.Pulse && preset == 26 && behaviours!.Descendants().Any(e => Attr(e, "autoRev") is "1" or "true")) d *= 2;
                effect.Duration = Math.Round(d, 3);
            }
            if (kind == EffectKind.Appear) effect.Duration = 0;
            var repeat = Attr(ctn, "repeatCount");
            if (repeat == "indefinite")
                effect.RepeatUntil = Child(ctn, "endCondLst")?.Descendants().Any(e => Attr(e, "evt") == "onNext") == true ? RepeatUntil.NextClick : RepeatUntil.EndOfSlide;
            else if (double.TryParse(repeat, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r >= 2000) effect.Repeat = (int)Math.Round(r / 1000);
            var accel = Attr(ctn, "accel") is { } a && a != "0";
            var decel = Attr(ctn, "decel") is { } de && de != "0";
            effect.Easing = accel && decel ? EffectEasing.EaseInOut : accel ? EffectEasing.EaseIn : decel ? EffectEasing.EaseOut : EffectEasing.Auto;
            effect.Rewind = Attr(ctn, "fill") == "remove";
            if (Child(ctn, "iterate") is { } iterate && c != EffectClass.Media)
            {
                effect.TextUnit = Attr(iterate, "type") == "lt" ? TextUnit.Letter : TextUnit.Word;
                if (Child(iterate, "tmAbs") is { } abs) effect.TextStagger = Math.Round(Ms(Attr(abs, "val")), 3);
                else if (Child(iterate, "tmPct") is { } pct && double.TryParse(Attr(pct, "val"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    effect.TextStagger = Math.Round(p / 100000 * effect.Duration, 3);
            }

            // Direction and amounts.
            if (c is EffectClass.Entrance or EffectClass.Exit)
            {
                if (kind == EffectKind.Fly)
                    effect.Direction = subtype switch
                    {
                        1 => EffectDirection.Top, 2 => EffectDirection.Right, 3 => EffectDirection.TopRight, 6 => EffectDirection.BottomRight,
                        8 => EffectDirection.Left, 9 => EffectDirection.TopLeft, 12 => EffectDirection.BottomLeft, _ => EffectDirection.Bottom,
                    };
                else if (kind == EffectKind.Wipe)
                    effect.Direction = subtype switch { 1 => EffectDirection.Top, 2 => EffectDirection.Right, 8 => EffectDirection.Left, _ => EffectDirection.Bottom };
                else if (kind == EffectKind.Split)
                    effect.Direction = subtype switch { 42 => EffectDirection.VerticalOut, 26 => EffectDirection.HorizontalIn, 37 => EffectDirection.HorizontalOut, _ => EffectDirection.VerticalIn };
            }
            if (kind == EffectKind.Spin && behaviours?.Descendants().FirstOrDefault(e => e.LocalName == "animRot") is { } rot &&
                double.TryParse(Attr(rot, "by"), NumberStyles.Float, CultureInfo.InvariantCulture, out var by))
                effect.Amount = Math.Round(by / 60000, 1);
            if (kind == EffectKind.Grow && behaviours?.Descendants().FirstOrDefault(e => e.LocalName == "by" && e.Parent?.LocalName == "animScale") is { } scale &&
                double.TryParse(Attr(scale, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out var sx))
                effect.Amount = Math.Round(sx / 1000, 1);
            if (kind == EffectKind.Transparency && behaviours?.Descendants().FirstOrDefault(e => e.LocalName == "strVal" && e.Ancestors().Any(x => x.LocalName == "to")) is { } to &&
                double.TryParse(Attr(to, "val"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
                effect.Amount = Math.Round((1 - opacity) * 100);
            if (c == EffectClass.Motion)
            {
                var motion = behaviours?.Descendants().FirstOrDefault(e => e.LocalName == "animMotion");
                var path = ParsePath(Attr(motion, "path"));
                if (path == null) return null;
                effect.Path = path;
                effect.Effect = path.Points.Count == 2 && !path.Smooth ? EffectKind.Line : EffectKind.Custom;
            }
            return effect;
        }

        /// <summary>A PowerPoint motion path ("M 0 0 L 0.25 0.1 C … E": fractions of the slide) as slide units from the start.</summary>
        private MotionPath? ParsePath(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var tokens = text!.Replace(",", " ").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var points = new List<Point>();
            var smooth = false;
            var closed = false;
            var i = 0;
            double Num()
            {
                if (i >= tokens.Length || !double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return double.NaN;
                i++;
                return v;
            }
            var command = 'M';
            while (i < tokens.Length)
            {
                var t = tokens[i];
                if (t.Length == 1 && char.IsLetter(t[0]))
                {
                    command = char.ToUpperInvariant(t[0]);
                    i++;
                    if (command == 'Z') closed = true;
                    if (command is 'Z' or 'E') continue;
                }
                switch (command)
                {
                    case 'M':
                    case 'L':
                    {
                        var x = Num();
                        var y = Num();
                        if (double.IsNaN(x) || double.IsNaN(y)) return Finish();
                        points.Add(new Point(x, y));
                        break;
                    }
                    case 'C':
                    {
                        Num();
                        Num();
                        Num();
                        Num();
                        var x = Num();
                        var y = Num();
                        if (double.IsNaN(x) || double.IsNaN(y)) return Finish();
                        points.Add(new Point(x, y));
                        smooth = true;
                        break;
                    }
                    default:
                        i++;
                        break;
                }
            }
            return Finish();

            MotionPath? Finish()
            {
                if (points.Count < 2) return null;
                var origin = points[0];
                var path = new MotionPath { Smooth = smooth, Closed = closed };
                foreach (var p in points)
                    path.Points.Add(new PathPoint(Math.Round((p.X - origin.X) * _deck.Width, 1), Math.Round((p.Y - origin.Y) * _deck.Height, 1)));
                if (closed && path.Points.Count > 2 && Math.Abs(path.Points[path.Points.Count - 1].X) < 0.5 && Math.Abs(path.Points[path.Points.Count - 1].Y) < 0.5)
                    path.Points.RemoveAt(path.Points.Count - 1);
                return path;
            }
        }

        /// <summary>p:video / p:audio: volume, mute, looping, hiding while stopped, playing across slides, full screen.</summary>
        private void ReadMediaNode(OpenXmlElement node, Slide slide)
        {
            var media = Child(node, "cMediaNode");
            var spid = Attr(media?.Descendants().FirstOrDefault(e => e.LocalName == "spTgt"), "spid");
            if (spid == null || !uint.TryParse(spid, out var id) || !_spids.TryGetValue(id, out var elementId)) return;
            if (slide.Find(elementId) is not MediaClipElement clip) return;
            if (double.TryParse(Attr(media, "vol"), NumberStyles.Float, CultureInfo.InvariantCulture, out var vol)) clip.Volume = MathEx.Clamp(Math.Round(vol / 100000, 2), 0, 1);
            if (Attr(media, "mute") is "1" or "true") clip.Volume = 0;
            if (Attr(Child(media, "cTn"), "repeatCount") == "indefinite") clip.Loop = true;
            var hidden = Attr(media, "showWhenStopped") is "0" or "false";
            if (clip is AudioElement audio)
            {
                audio.HideIcon = hidden;
                if (int.TryParse(Attr(media, "numSld"), out var slides) && slides > 1)
                {
                    audio.AcrossSlides = true;
                    audio.StopAfterSlides = slides >= 999 ? null : slides;
                }
            }
            else if (clip is VideoElement video)
            {
                video.HideWhenNotPlaying = hidden;
                video.FullScreen = Attr(node, "fullScrn") is "1" or "true";
            }
        }

        /// <summary>What couldn't be brought over exactly, for the notice after importing.</summary>
        private void AddMediaAndAnimationWarnings(List<string> warnings)
        {
            if (_approximatedEffects > 0)
                warnings.Add(_approximatedEffects == 1
                    ? "1 animation effect Stagecraft doesn't have was changed to the closest one."
                    : $"{_approximatedEffects} animation effects Stagecraft doesn't have were changed to the closest ones.");
            if (_droppedEffects > 0)
                warnings.Add(_droppedEffects == 1 ? "1 animation couldn't be brought over." : $"{_droppedEffects} animations couldn't be brought over.");
            if (_unreadableMedia > 0)
                warnings.Add(_unreadableMedia == 1 ? "1 video or sound couldn't be brought over (an online video, or a format Windows can't play)." : $"{_unreadableMedia} videos or sounds couldn't be brought over (online videos, or formats Windows can't play).");
        }
    }
}
