using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Model;

namespace Stagecraft.Views.Editor;

/// <summary>Panel sections for video and sound, online videos, links, action buttons and shadows.</summary>
public sealed partial class PropertiesPanel
{
    private ComboBox DashCombo(DashStyleKind current, Action<DashStyleKind> changed, string name) =>
        Combo(new[]
        {
            (DashStyleKind.Solid, "Solid"), (DashStyleKind.Dash, "Dashes"), (DashStyleKind.LongDash, "Long dashes"), (DashStyleKind.Dot, "Dots"), (DashStyleKind.DashDot, "Dash dot"),
        }, current, changed, name);

    private TextBlock Hint(string text, double top = 0)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        return t;
    }

    // ------------------------------------------------------------------ video and sound

    private void MediaFileRows(Panel sec, MediaClipElement clip)
    {
        var editor = _editor!;
        var media = _state!.Session.Media;
        var item = media.Get(clip.Media);
        var linkedPath = clip.IsLinked ? media.ResolveLink(clip) : null;
        var info = clip.Media != null
            ? $"{clip.FileName ?? "Embedded file"} · {Core.Model.MediaLimits.Describe(item?.Length ?? 0)} · in the presentation"
            : linkedPath != null ? $"Linked: {linkedPath}" : "The linked file can't be found: " + (clip.LinkAbsolute ?? clip.LinkPath);
        var t = Hint(info);
        if (clip.IsLinked && linkedPath == null) t.SetResourceReference(TextBlock.ForegroundProperty, "Status.Critical");
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.ToolTip = info;
        sec.Children.Add(t);
        if (clip.Media != null)
            sec.Children.Add(Grid2(TextButton("Trim…", "Icon.Cut", () => editor.TrimMedia(clip)), TextButton("Link instead…", "Icon.Link", () => editor.LinkEmbeddedMedia(clip))));
        else if (linkedPath != null)
            sec.Children.Add(Grid2(TextButton("Trim…", "Icon.Cut", () => editor.TrimMedia(clip)), TextButton("Embed", "Icon.ArrowDownload", () => editor.EmbedLinkedMedia(clip))));
        else
            sec.Children.Add(TextButton("Find the file…", "Icon.FolderOpen", () => editor.RelinkMedia(clip)));
    }

    private void MediaPlaybackRows(Panel sec, MediaClipElement clip)
    {
        var editor = _editor!;
        var slide = _state!.CurrentSlide!;
        var start = AnimationOps.StartOf(slide, clip);
        Row(sec, "Start", Combo(new[]
        {
            (MediaStart.OnClick, "In click order"), (MediaStart.Automatically, "Automatically"), (MediaStart.WhenClicked, "When clicked on"),
        }, start, s => editor.SetMediaStart(clip, s), "Start"));
        Row(sec, "Volume", Slider(clip.Volume * 100, 0, 100, v => editor.EditMedia("Volume", c => c.Volume = Math.Round(v / 100, 2), "volume"), () => editor.SealHistory()));
        sec.Children.Add(Grid2(
            Labeled("Fade in", Number(clip.FadeIn, 0, 30, 0.25, v => editor.EditMedia("Fade in", c => c.FadeIn = Math.Round(v, 2)), " s", 2)),
            Labeled("Fade out", Number(clip.FadeOut, 0, 30, 0.25, v => editor.EditMedia("Fade out", c => c.FadeOut = Math.Round(v, 2)), " s", 2))));
        Switch(sec, "Loop until stopped", clip.Loop, on => editor.EditMedia("Loop", c => c.Loop = on));
        if (clip.PlayLength is { } length)
        {
            var span = TimeSpan.FromSeconds(Math.Round(length, 1));
            sec.Children.Add(Hint((clip.TrimStart > 0 || clip.TrimEnd != null ? "Plays " : "Length ") + (span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : span.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture)), 2));
        }
    }

    private void BuildVideo(VideoElement video)
    {
        var editor = _editor!;
        var sec = Section("Video");
        MediaPlaybackRows(sec, video);
        Switch(sec, "Play full screen", video.FullScreen, on => editor.EditMedia("Full screen", c => { if (c is VideoElement v) v.FullScreen = on; }), "Fills the screen while it plays");
        Switch(sec, "Hide when not playing", video.HideWhenNotPlaying, on => editor.EditMedia("Hide when not playing", c => { if (c is VideoElement v) v.HideWhenNotPlaying = on; }), "Invisible in the slide show until it plays");
        var look = Section("Look");
        look.Children.Add(TextButton("Poster frame…", "Icon.FilmstripImage", () => editor.ChoosePoster(video)));
        Row(look, "Shape", Segments(new[] { (0, "Rectangle", (string?)"Icon.ShapeRectangle"), (1, "Rounded", "Icon.ShapeRounded"), (2, "Circle", "Icon.Circle") },
            video.Mask == ImageMask.Circle ? 2 : video.CornerRadius > 0 ? 1 : 0, v => editor.EditMedia("Video shape", c =>
            {
                if (c is not VideoElement vv) return;
                vv.Mask = v == 2 ? ImageMask.Circle : ImageMask.Rectangle;
                vv.CornerRadius = v == 1 ? (vv.CornerRadius > 0 ? vv.CornerRadius : Math.Round(_state!.Deck.Theme.Radius * 2)) : 0;
            }), "Video shape"));
        if (video.Mask == ImageMask.Rectangle && video.CornerRadius > 0)
            Row(look, "Corners", Slider(video.CornerRadius, 0, 200, v => editor.SetCornerRadius(v), () => editor.SealHistory()));
        var file = Section("File");
        MediaFileRows(file, video);
    }

    private void BuildAudio(AudioElement audio)
    {
        var editor = _editor!;
        var sec = Section("Sound");
        MediaPlaybackRows(sec, audio);
        Switch(sec, "Play across slides", audio.AcrossSlides, on => editor.EditMedia("Play across slides", c => { if (c is AudioElement a) a.AcrossSlides = on; }), "Background music: keeps playing when the show moves on");
        if (audio.AcrossSlides)
        {
            var choices = new List<(int, string)> { (0, "Until the end of the show") };
            for (var n = 2; n <= 20; n++) choices.Add((n, $"For {n} slides"));
            if (audio.StopAfterSlides is { } current && current > 20) choices.Add((current, $"For {current} slides"));
            Row(sec, "Stop", Combo(choices, audio.StopAfterSlides ?? 0, n => editor.EditMedia("Stop after", c => { if (c is AudioElement a) a.StopAfterSlides = n == 0 ? null : n; }), "Stop after"));
        }
        Switch(sec, "Hide the icon in the show", audio.HideIcon, on => editor.EditMedia("Hide icon", c => { if (c is AudioElement a) a.HideIcon = on; }), "Set it to start automatically, or it can't be clicked");
        var colour = new TextBlock { Text = "Icon colour", FontSize = 12, Margin = new Thickness(1, 4, 0, 6) };
        colour.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        sec.Children.Add(colour);
        sec.Children.Add(Swatches(audio.Color, c => editor.EditMedia("Icon colour", x => { if (x is AudioElement a) a.Color = c ?? "accent1"; }), false, "Icon colour"));
        var file = Section("File");
        MediaFileRows(file, audio);
    }

    private void BuildOnlineVideo(OnlineVideoElement online)
    {
        var editor = _editor!;
        var sec = Section("Online video");
        var url = new TextBox { Text = online.Url, Tag = "rebuild" };
        url.LostKeyboardFocus += (_, _) =>
        {
            var value = url.Text.Trim();
            if (value == online.Url) return;
            var info = Services.OnlineVideos.Describe(value);
            if (info == null)
            {
                url.Text = online.Url;
                return;
            }
            editor.EditSelected("Video address", l => { foreach (var o in l.OfType<OnlineVideoElement>()) { o.Url = info.Url; o.Provider = info.Provider; } });
        };
        Row(sec, "Address", url);
        var title = new TextBox { Text = online.Title ?? "", Tag = "rebuild" };
        Ui.SetPlaceholder(title, "Shown on the card");
        title.LostKeyboardFocus += (_, _) =>
        {
            if ((online.Title ?? "") != title.Text.Trim()) editor.EditSelected("Video title", l => { foreach (var o in l.OfType<OnlineVideoElement>()) o.Title = title.Text.Trim().Length == 0 ? null : title.Text.Trim(); });
        };
        Row(sec, "Title", title);
        sec.Children.Add(Hint("Opens in the browser when it's clicked in the slide show. Online videos don't play inside the slide.", 2));
        sec.Children.Add(Grid2(TextButton("Open", "Icon.Open", () => Services.LinkLauncher.Open(Hyperlink.Web(online.Url), Window.GetWindow(this), null)),
            new Border()));
        Row(sec, "Corners", Slider(online.CornerRadius, 0, 120, v => editor.SetCornerRadius(v), () => editor.SealHistory()));
    }

    private void BuildActionButton(ShapeElement button)
    {
        var editor = _editor!;
        var sec = Section("Action button");
        Row(sec, "Symbol", Combo(new[]
        {
            (ActionButtonKind.Previous, "Back"), (ActionButtonKind.Next, "Next"), (ActionButtonKind.First, "First slide"), (ActionButtonKind.Last, "Last slide"),
            (ActionButtonKind.Return, "Return"), (ActionButtonKind.Custom, "Link"),
        }, button.Action, k => editor.EditSelected("Button symbol", l => { foreach (var s in l.OfType<ShapeElement>()) s.Action = k; }), "Button symbol"));
        sec.Children.Add(Hint("The button's link is under Link below. Add text to the button to show a label instead of the symbol."));
    }

    // ------------------------------------------------------------------ links

    private void BuildLink(IReadOnlyList<Element> selected)
    {
        var editor = _editor!;
        var sec = Section("Link");
        var link = selected.Select(e => e.Link).FirstOrDefault(l => l != null);
        var textLinks = selected.SelectMany(e => Core.IO.DeckFile.TextBodies(e)).SelectMany(b => b.Content.Paragraphs).SelectMany(p => p.Runs).Count(r => r.Link != null);
        if (link == null && selected.Count == 1 && selected[0] is OnlineVideoElement)
        {
            sec.Children.Add(Hint("Clicking it in the slide show opens the video's page."));
        }
        if (link != null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var icon = new IconView
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                Data = (Geometry)FindResource(link.Kind switch { LinkKind.Email => "Icon.Mail", LinkKind.Slide => "Icon.SlideLink", LinkKind.File => "Icon.DocumentLink", _ => "Icon.Globe" }),
            };
            icon.SetResourceReference(IconView.ForegroundProperty, "Text.Accent");
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = link.Kind == LinkKind.Slide && link.Jump == SlideJump.Specific ? SlideName(link.SlideId) : link.Summary, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = link.Summary });
            sec.Children.Add(row);
            sec.Children.Add(Grid2(TextButton("Edit…", "Icon.Edit", () => editor.InsertLink()), TextButton("Remove", "Icon.LinkDismiss", () => editor.RemoveLinks())));
            var follow = TextButton("Follow (Ctrl+click)", "Icon.Open", () => editor.FollowLink(link));
            sec.Children.Add(follow);
        }
        else
        {
            sec.Children.Add(TextButton("Add a link…", "Icon.Link", () => editor.InsertLink()));
            if (textLinks > 0) sec.Children.Add(Hint(textLinks == 1 ? "Some of its text has a link (double-click the text to edit it)." : $"{textLinks} pieces of its text have links."));
        }
    }

    private string SlideName(string? id)
    {
        var deck = _state!.Deck;
        var i = deck.Slides.FindIndex(s => s.Id == id);
        if (i < 0) return "A slide that was deleted";
        var title = deck.Slides[i].TitleText;
        return $"Slide {i + 1}" + (title.Length > 0 ? ": " + title.Replace('\n', ' ') : "");
    }

    // ------------------------------------------------------------------ shadow

    private void BuildEffects(IReadOnlyList<Element> selected)
    {
        var editor = _editor!;
        var sec = Section("Shadow");
        var shadow = selected[0].Shadow;
        var presets = new (string Key, string Name, Shadow? Value)[]
        {
            ("none", "None", null), ("soft", "Soft", Shadow.Soft), ("lifted", "Lifted", Shadow.Lifted), ("close", "Close", Shadow.Close), ("glow", "Glow", Shadow.Glow),
        };
        var current = shadow == null ? "none" : presets.FirstOrDefault(p => p.Value != null && p.Value.Blur == shadow.Blur && p.Value.Distance == shadow.Distance && p.Value.Angle == shadow.Angle).Key ?? "custom";
        var tiles = new UniformGrid5();
        foreach (var (key, name, value) in presets)
        {
            var preview = new Border
            {
                Width = 30,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            preview.SetResourceReference(Border.BackgroundProperty, "Card.Background");
            preview.SetResourceReference(Border.BorderBrushProperty, "Control.StrokeSecondary");
            preview.BorderThickness = new Thickness(1);
            if (value != null)
            {
                var c = _state!.Style?.Resolve(value.Color) ?? Colors.Black;
                preview.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(c.R, c.G, c.B),
                    Opacity = c.A / 255.0,
                    BlurRadius = Math.Min(14, value.Blur / 3),
                    ShadowDepth = Math.Min(6, value.Distance / 3),
                    Direction = -value.Angle,
                };
            }
            var stack = new StackPanel();
            stack.Children.Add(preview);
            stack.Children.Add(new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button { Content = stack, Padding = new Thickness(2, 4, 2, 4), Margin = new Thickness(2), ToolTip = name + " shadow" };
            b.SetResourceReference(StyleProperty, "CardButton");
            if (key == current) b.SetResourceReference(BackgroundProperty, "Accent.Selection");
            System.Windows.Automation.AutomationProperties.SetName(b, "Shadow: " + name);
            var v = value;
            b.Click += (_, _) => editor.EditSelected("Shadow", l => { foreach (var e in l) e.Shadow = v?.Clone(); });
            tiles.Children.Add(b);
        }
        sec.Children.Add(tiles);
        if (shadow != null)
        {
            var colour = new TextBlock { Text = "Shadow colour", FontSize = 12, Margin = new Thickness(1, 6, 0, 6) };
            colour.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            sec.Children.Add(colour);
            var at = shadow.Color.IndexOf('@');
            var alpha = at > 0 ? shadow.Color.Substring(at) : "@35";
            sec.Children.Add(Swatches(shadow.Color, c => editor.EditSelected("Shadow colour", l =>
            {
                foreach (var e in l.Where(x => x.Shadow != null)) e.Shadow!.Color = (c ?? "black").Split('@')[0] + alpha;
            }), false, "Shadow colour"));
            sec.Children.Add(Grid2(
                Labeled("Blur", Number(shadow.Blur, 0, 200, 2, v => editor.EditSelected("Shadow blur", l => { foreach (var e in l.Where(x => x.Shadow != null)) e.Shadow!.Blur = v; }, "shadowblur"))),
                Labeled("Distance", Number(shadow.Distance, 0, 200, 2, v => editor.EditSelected("Shadow distance", l => { foreach (var e in l.Where(x => x.Shadow != null)) e.Shadow!.Distance = v; }, "shadowdist")))));
        }
    }

    /// <summary>Five equal columns.</summary>
    private sealed class UniformGrid5 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid5()
        {
            Columns = 5;
            Margin = new Thickness(-2, 0, -2, 4);
        }
    }
}
