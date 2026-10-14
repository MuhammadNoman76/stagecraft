using System.Windows.Input;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Themes;
using Stagecraft.Services;
using Stagecraft.Themes;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

/// <summary>A keyboard shortcut.</summary>
public sealed class KeyChord
{
    public KeyChord(Key key, ModifierKeys modifiers = ModifierKeys.None, string? text = null)
    {
        Key = key;
        Modifiers = modifiers;
        Text = text ?? Describe(key, modifiers);
    }

    public Key Key { get; }
    public ModifierKeys Modifiers { get; }
    public string Text { get; }

    public bool Matches(Key key, ModifierKeys modifiers) => key == Key && modifiers == Modifiers;

    private static string Describe(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.Next => "Page Down",
            Key.Prior => "Page Up",
            Key.Delete => "Del",
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }
}

/// <summary>A command: in the command palette, on a shortcut, or both.</summary>
public sealed class AppCommand
{
    public AppCommand(string category, string title, Action execute, KeyChord? gesture = null, string? icon = null, string? keywords = null)
    {
        Category = category;
        Title = title;
        Execute = execute;
        Gesture = gesture;
        Icon = icon;
        Keywords = keywords ?? "";
    }

    public string Category { get; }
    public string Title { get; }
    public Action Execute { get; }
    public KeyChord? Gesture { get; }
    public string? Icon { get; }
    public string Keywords { get; }
    public Func<bool> CanExecute { get; init; } = () => true;

    /// <summary>The shortcut also works while the keyboard is in a text box.</summary>
    public bool WorksWhileTyping { get; init; }

    /// <summary>The shortcut also works while editing slide text in place.</summary>
    public bool WorksInSlideText { get; init; }

    /// <summary>Shown in the palette (some shortcut-only commands aren't).</summary>
    public bool InPalette { get; init; } = true;
}

public partial class EditorView
{
    private List<AppCommand>? _commands;

    public IReadOnlyList<AppCommand> Commands => _commands ??= BuildCommands();

    private bool HasSelection => _state.Selection.Count > 0;
    private bool HasSlide => _state.CurrentSlide != null;
    private bool RailFocused => Rail.ListBox.IsKeyboardFocusWithin;
    private bool CanvasOrRail => !RailFocused || true;

    private List<AppCommand> BuildCommands()
    {
        const ModifierKeys C = ModifierKeys.Control, S = ModifierKeys.Shift, A = ModifierKeys.Alt;
        var list = new List<AppCommand>
        {
            // File
            new("File", "Save", () => Save(), new KeyChord(Key.S, C), "Icon.Save") { WorksWhileTyping = true, WorksInSlideText = true },
            new("File", "Save as…", () => SaveAs(), new KeyChord(Key.S, C | S), "Icon.Save") { WorksWhileTyping = true, WorksInSlideText = true },
            new("File", "Open…", () => OpenDeck(), new KeyChord(Key.O, C), "Icon.FolderOpen") { WorksWhileTyping = true },
            new("File", "New window", () => App.Current.ShowStart(null), new KeyChord(Key.N, C), "Icon.WindowNew", "new presentation start") { WorksWhileTyping = true },
            new("File", "Import PowerPoint…", () => ImportPowerPoint(), null, "Icon.ArrowImport", "pptx powerpoint open"),
            new("File", "Export…", () => ShowExport(null), new KeyChord(Key.E, C | S), "Icon.ArrowExportLtr", "pdf png jpg pptx share") { WorksWhileTyping = true },
            new("File", "Export as PDF…", () => ShowExport("pdf"), null, "Icon.DocumentPdf", "print"),
            new("File", "Export slides as pictures…", () => ShowExport("png"), null, "Icon.Image", "png jpg image"),
            new("File", "Export as PowerPoint (.pptx)…", () => ShowExport("pptx"), null, "Icon.ArrowExportLtr", "pptx office"),
            new("File", "Start screen", () => GoToStart(), null, "Icon.Home", "home recent close") ,
            new("File", "Close", () => _window.Close(), new KeyChord(Key.W, C), "Icon.Dismiss") { WorksWhileTyping = true },
            new("App", "Settings", () => SettingsWindow.ShowSingle(_window), new KeyChord(Key.OemComma, C), "Icon.Settings", "preferences options autosave updates") { WorksWhileTyping = true },
            new("App", "About Stagecraft", () => AboutDialog.Open(_window), null, "Icon.Info", "version licences"),
            new("App", "Use light mode", () => SetAppTheme(AppTheme.Light), null, "Icon.DarkTheme", "appearance theme"),
            new("App", "Use dark mode", () => SetAppTheme(AppTheme.Dark), null, "Icon.DarkTheme", "appearance theme"),
            new("App", "Use my Windows setting for light / dark", () => SetAppTheme(AppTheme.System), null, "Icon.DarkTheme", "appearance theme"),

            // Edit
            new("Edit", "Undo", () => Undo(), new KeyChord(Key.Z, C), "Icon.ArrowUndo") { CanExecute = () => Session.History.CanUndo },
            new("Edit", "Redo", () => Redo(), new KeyChord(Key.Y, C), "Icon.ArrowRedo") { CanExecute = () => Session.History.CanRedo },
            new("Edit", "Redo", () => Redo(), new KeyChord(Key.Z, C | S), "Icon.ArrowRedo") { CanExecute = () => Session.History.CanRedo, InPalette = false },
            new("Edit", "Cut", () => Cut(), new KeyChord(Key.X, C), "Icon.Cut"),
            new("Edit", "Copy", () => Copy(), new KeyChord(Key.C, C), "Icon.Copy"),
            new("Edit", "Paste", () => Paste(), new KeyChord(Key.V, C), "Icon.ClipboardPaste"),
            new("Edit", "Duplicate", () => Duplicate(), new KeyChord(Key.D, C), "Icon.Copy", "copy clone") { CanExecute = () => HasSlide },
            new("Edit", "Delete", () => Delete(), new KeyChord(Key.Delete), "Icon.Delete", "remove") { CanExecute = () => HasSlide },
            new("Edit", "Delete", () => Delete(), new KeyChord(Key.Back), "Icon.Delete") { CanExecute = () => HasSlide && (HasSelection || RailFocused), InPalette = false },
            new("Edit", "Select all", () => SelectAll(), new KeyChord(Key.A, C), null) { CanExecute = () => HasSlide },
            new("Edit", "Find…", () => OpenFind(replace: false), new KeyChord(Key.F, C), "Icon.Search", "search text look for") { WorksWhileTyping = true, WorksInSlideText = true },
            new("Edit", "Replace…", () => OpenFind(replace: true), new KeyChord(Key.H, C), "Icon.ArrowSwap", "find change text") { WorksWhileTyping = true, WorksInSlideText = true },
            new("Edit", "Copy style", () => StartFormatPainter(), new KeyChord(Key.C, C | A), "Icon.PaintBrush", "format painter") { CanExecute = () => HasSelection },
            new("Edit", "Paste style", () => PasteStyle(), new KeyChord(Key.V, C | A), "Icon.PaintBrush", "format painter") { CanExecute = () => HasSelection && _copiedStyle != null },
            new("Edit", "Lock or unlock", () => ToggleLock(), new KeyChord(Key.L, C | S), "Icon.LockClosed") { CanExecute = () => HasSelection },
            new("Edit", "Unlock everything on this slide", () => UnlockAll(), null, "Icon.LockOpen") { CanExecute = () => _state.CurrentSlide?.Elements.Any(e => e.Locked) == true },

            // Insert
            new("Insert", "New slide", () => NewSlide(null), new KeyChord(Key.M, C), "Icon.SlideAdd", "add page") { WorksWhileTyping = true },
            new("Insert", "Text box", () => InsertText(), new KeyChord(Key.T, A), "Icon.TextT", "type words") { CanExecute = () => HasSlide },
            new("Insert", "Rectangle", () => InsertShape(ShapeKind.Rectangle), null, "Icon.Square", "shape box") { CanExecute = () => HasSlide },
            new("Insert", "Rounded rectangle", () => InsertShape(ShapeKind.RoundedRectangle), null, "Icon.Square", "shape card pill") { CanExecute = () => HasSlide },
            new("Insert", "Ellipse", () => InsertShape(ShapeKind.Ellipse), null, "Icon.Circle", "shape circle oval") { CanExecute = () => HasSlide },
            new("Insert", "Triangle", () => InsertShape(ShapeKind.Triangle), null, "Icon.Triangle", "shape") { CanExecute = () => HasSlide },
            new("Insert", "Diamond", () => InsertShape(ShapeKind.Diamond), null, "Icon.Square", "shape rhombus") { CanExecute = () => HasSlide },
            new("Insert", "Line", () => InsertShape(ShapeKind.Line), null, "Icon.LineHorizontal1", "shape rule") { CanExecute = () => HasSlide },
            new("Insert", "Arrow", () => InsertShape(ShapeKind.Arrow), null, "Icon.ArrowRight", "shape pointer") { CanExecute = () => HasSlide },
            new("Insert", "Connector", () => InsertConnector(), null, "Icon.Connector", "link join flow diagram") { CanExecute = () => HasSlide },
            new("Insert", "Callout", () => InsertShape(ShapeKind.Callout), null, "Icon.Chat", "speech bubble shape") { CanExecute = () => HasSlide },
            new("Insert", "Picture…", () => InsertImage(), new KeyChord(Key.P, C | A), "Icon.ImageAdd", "image photo") { CanExecute = () => HasSlide },
            new("Insert", "Icon…", () => PickIcon(null), new KeyChord(Key.I, C | A), "Icon.Icons", "symbol glyph") { CanExecute = () => HasSlide },
            new("Insert", "Bar chart", () => InsertChart(ChartKind.Bar), null, "Icon.DataBarVertical", "graph column data") { CanExecute = () => HasSlide },
            new("Insert", "Line chart", () => InsertChart(ChartKind.Line), null, "Icon.DataLine", "graph trend data") { CanExecute = () => HasSlide },
            new("Insert", "Pie chart", () => InsertChart(ChartKind.Pie), null, "Icon.DataPie", "graph share data") { CanExecute = () => HasSlide },
            new("Insert", "Donut chart", () => InsertChart(ChartKind.Donut), null, "Icon.DataUsage", "graph ring share data") { CanExecute = () => HasSlide },
            new("Insert", "Table", () => InsertTable(), null, "Icon.Table", "grid rows columns") { CanExecute = () => HasSlide },
            new("Insert", "Header and footer…", () => EditHeaderFooter(), null, "Icon.DocumentHeaderFooter", "slide numbers page number date footer text") { CanExecute = () => HasSlide },
            new("Insert", "Link…", () => InsertLink(), new KeyChord(Key.K, C | S), "Icon.Link", "hyperlink url web address e-mail mailto slide jump file") { CanExecute = () => HasSlide, WorksInSlideText = true },
            new("Insert", "Remove link", () => RemoveLinks(), null, "Icon.LinkDismiss", "hyperlink unlink") { CanExecute = () => Canvas.IsEditingText || SelectionHasLink, WorksInSlideText = true },
            new("Insert", "Open link", () => { if (FirstLink() is { } l) FollowLink(l); }, null, "Icon.Open", "hyperlink follow go") { CanExecute = () => FirstLink() != null },
            new("Insert", "Video…", () => InsertVideo(), null, "Icon.VideoClip", "movie film clip mp4 media") { CanExecute = () => HasSlide },
            new("Insert", "Sound…", () => InsertAudio(), null, "Icon.Speaker2", "audio music mp3 media") { CanExecute = () => HasSlide },
            new("Insert", "Online video…", () => InsertOnlineVideo(), null, "Icon.GlobeVideo", "youtube vimeo web link media") { CanExecute = () => HasSlide },
            new("Insert", "Action button: Back", () => InsertActionButton(ActionButtonKind.Previous), null, "Icon.ArrowLeft", "previous slide navigation") { CanExecute = () => HasSlide },
            new("Insert", "Action button: Next", () => InsertActionButton(ActionButtonKind.Next), null, "Icon.ArrowRight", "forward slide navigation") { CanExecute = () => HasSlide },
            new("Insert", "Action button: First slide", () => InsertActionButton(ActionButtonKind.First), null, "Icon.Previous", "beginning home navigation") { CanExecute = () => HasSlide },
            new("Insert", "Action button: Last slide", () => InsertActionButton(ActionButtonKind.Last), null, "Icon.Next", "end navigation") { CanExecute = () => HasSlide },
            new("Insert", "Action button: Return", () => InsertActionButton(ActionButtonKind.Return), null, "Icon.ArrowUndo", "last viewed back navigation") { CanExecute = () => HasSlide },
            new("Insert", "Action button: Link…", () => InsertActionButton(ActionButtonKind.Custom), null, "Icon.Link", "web page file navigation") { CanExecute = () => HasSlide },

            // Picture and media
            new("Picture", "Crop picture", () => StartCrop(), null, "Icon.Crop", "trim cut edges image") { CanExecute = () => _state.SingleSelection is ImageElement { Media: not null } },
            new("Picture", "Replace picture…", () => { if (_state.SingleSelection is ImageElement i) ReplaceImage(i); }, null, "Icon.ImageAdd", "change image photo") { CanExecute = () => _state.SingleSelection is ImageElement },
            new("Media", "Trim video or sound…", () => { if (_state.SingleSelection is MediaClipElement m) TrimMedia(m); }, null, "Icon.Cut", "shorten start end clip") { CanExecute = () => _state.SingleSelection is MediaClipElement },
            new("Media", "Poster frame…", () => { if (_state.SingleSelection is VideoElement v) ChoosePoster(v); }, null, "Icon.FilmstripImage", "cover thumbnail picture video") { CanExecute = () => _state.SingleSelection is VideoElement },

            // Animation
            new("Animation", "Animation pane", () => ShowAnimations(Panel.Mode != PanelMode.Animations), null, "Icon.StarEmphasis", "effects order timing builds") { CanExecute = () => HasSlide },
            new("Animation", "Copy animation (animation painter)", () => StartAnimationPainter(), new KeyChord(Key.C, A | S), "Icon.PaintBrush", "painter effects") { CanExecute = () => HasSelection },
            new("Animation", "Paste animation", () => PasteAnimation(), null, "Icon.PaintBrush", "painter effects") { CanExecute = () => HasSelection && _copiedAnimations != null },
            new("Animation", "Remove animations from the selection", () => RemoveAnimationsOfSelection(), null, "Icon.Delete", "effects none") { CanExecute = () => _state.CurrentSlide is { } s && Core.Animation.AnimationRules.EffectsOf(s, _state.SelectedElements).Any() },
            new("Animation", "Move animation earlier", () => NudgeEffects(-1), null, "Icon.ArrowUp", "order reorder") { CanExecute = () => _state.SelectedEffects.Count > 0 },
            new("Animation", "Move animation later", () => NudgeEffects(1), null, "Icon.ArrowDown", "order reorder") { CanExecute = () => _state.SelectedEffects.Count > 0 },
            new("Animation", "Play from the selected animation", () => PlayFromSelected(), null, "Icon.Play", "preview effects") { CanExecute = () => _state.SelectedEffects.Count > 0 },

            // Arrange
            new("Arrange", "Group", () => GroupSelection(), new KeyChord(Key.G, C), "Icon.Group") { CanExecute = () => _state.Selection.Count > 1 },
            new("Arrange", "Ungroup", () => UngroupSelection(), new KeyChord(Key.G, C | S), "Icon.GroupDismiss") { CanExecute = () => _state.SelectedElements.Any(e => e.GroupId != null) },
            new("Arrange", "Bring to front", () => MoveZ(ZOrderMove.Front), new KeyChord(Key.OemCloseBrackets, C | S), "Icon.PositionToFront") { CanExecute = () => HasSelection },
            new("Arrange", "Bring forward", () => MoveZ(ZOrderMove.Forward), new KeyChord(Key.OemCloseBrackets, C), "Icon.PositionForward") { CanExecute = () => HasSelection },
            new("Arrange", "Send backward", () => MoveZ(ZOrderMove.Backward), new KeyChord(Key.OemOpenBrackets, C), "Icon.PositionBackward") { CanExecute = () => HasSelection },
            new("Arrange", "Send to back", () => MoveZ(ZOrderMove.Back), new KeyChord(Key.OemOpenBrackets, C | S), "Icon.PositionToBack") { CanExecute = () => HasSelection },
            new("Arrange", "Align left", () => Align(AlignMode.Left), null, "Icon.AlignLeft") { CanExecute = () => HasSelection },
            new("Arrange", "Align centres", () => Align(AlignMode.CenterHorizontal), null, "Icon.AlignCenterHorizontal", "center") { CanExecute = () => HasSelection },
            new("Arrange", "Align right", () => Align(AlignMode.Right), null, "Icon.AlignRight") { CanExecute = () => HasSelection },
            new("Arrange", "Align tops", () => Align(AlignMode.Top), null, "Icon.AlignTop") { CanExecute = () => HasSelection },
            new("Arrange", "Align middles", () => Align(AlignMode.CenterVertical), null, "Icon.AlignCenterVertical", "center") { CanExecute = () => HasSelection },
            new("Arrange", "Align bottoms", () => Align(AlignMode.Bottom), null, "Icon.AlignBottom") { CanExecute = () => HasSelection },
            new("Arrange", "Distribute horizontally", () => Distribute(true), null, "Icon.AlignSpaceEvenlyHorizontal", "space evenly") { CanExecute = () => _state.Selection.Count > 2 },
            new("Arrange", "Distribute vertically", () => Distribute(false), null, "Icon.AlignSpaceEvenlyVertical", "space evenly") { CanExecute = () => _state.Selection.Count > 2 },
            new("Arrange", "Tidy up", () => TidySelection(), new KeyChord(Key.T, C | A), "Icon.Sparkle", "clean align distribute neat grid") { CanExecute = () => _state.Selection.Count > 1 },
            new("Arrange", "Flip horizontally", () => Flip(true), null, "Icon.FlipHorizontal", "mirror") { CanExecute = () => HasSelection },
            new("Arrange", "Flip vertically", () => Flip(false), null, "Icon.FlipVertical", "mirror") { CanExecute = () => HasSelection },

            // Text
            new("Text", "Bigger text", () => ChangeFontSize(1), new KeyChord(Key.OemPeriod, C | S), "Icon.TextFontSize", "font size increase") { CanExecute = () => HasSelection, WorksInSlideText = true },
            new("Text", "Smaller text", () => ChangeFontSize(-1), new KeyChord(Key.OemComma, C | S), "Icon.TextFontSize", "font size decrease") { CanExecute = () => HasSelection, WorksInSlideText = true },
            new("Text", "Bold", () => ToggleBold(), new KeyChord(Key.B, C), "Icon.TextBold") { CanExecute = () => HasSelection },
            new("Text", "Italic", () => ToggleItalic(), new KeyChord(Key.I, C), "Icon.TextItalic") { CanExecute = () => HasSelection },
            new("Text", "Underline", () => ToggleUnderline(), new KeyChord(Key.U, C), "Icon.TextUnderline") { CanExecute = () => HasSelection },
            new("Text", "Align text left", () => SetAlign(TextAlign.Left), new KeyChord(Key.L, C), "Icon.TextAlignLeft") { CanExecute = () => HasSelection },
            new("Text", "Centre text", () => SetAlign(TextAlign.Center), new KeyChord(Key.E, C), "Icon.TextAlignCenter", "center") { CanExecute = () => HasSelection },
            new("Text", "Align text right", () => SetAlign(TextAlign.Right), new KeyChord(Key.R, C), "Icon.TextAlignRight") { CanExecute = () => HasSelection },
            new("Text", "Justify text", () => SetAlign(TextAlign.Justify), new KeyChord(Key.J, C), "Icon.TextAlignJustify") { CanExecute = () => HasSelection },
            new("Text", "Bullet list", () => SetList(ListKind.Bullet), null, "Icon.TextBulletListLtr") { CanExecute = () => HasSelection },
            new("Text", "Numbered list", () => SetList(ListKind.Number), null, "Icon.TextNumberListLtr") { CanExecute = () => HasSelection },

            // View
            new("View", "Editor", () => SetView(0), new KeyChord(Key.D1, C), "Icon.SlideText", "normal edit"),
            new("View", "Slide sorter", () => SetView(1), new KeyChord(Key.D2, C), "Icon.Grid", "overview reorder"),
            new("View", "Outline", () => SetView(2), new KeyChord(Key.D3, C), "Icon.TextBulletListSquare", "type text quickly"),
            new("View", "Command palette", () => OpenPalette(), new KeyChord(Key.K, C), "Icon.Search", "search find") { WorksWhileTyping = true, WorksInSlideText = true },
            new("View", "Command palette", () => OpenPalette(), new KeyChord(Key.P, C | S), "Icon.Search") { WorksWhileTyping = true, WorksInSlideText = true, InPalette = false },
            new("View", "Zoom in", () => Canvas.ZoomStep(1), new KeyChord(Key.OemPlus, C), "Icon.ZoomIn"),
            new("View", "Zoom in", () => Canvas.ZoomStep(1), new KeyChord(Key.Add, C), "Icon.ZoomIn") { InPalette = false },
            new("View", "Zoom out", () => Canvas.ZoomStep(-1), new KeyChord(Key.OemMinus, C), "Icon.ZoomOut"),
            new("View", "Zoom out", () => Canvas.ZoomStep(-1), new KeyChord(Key.Subtract, C), "Icon.ZoomOut") { InPalette = false },
            new("View", "Zoom to fit", () => Canvas.FitToWindow(), new KeyChord(Key.D0, C), "Icon.ZoomFit"),
            new("View", "Actual size (100%)", () => Canvas.SetZoom(1), null, "Icon.ZoomFit"),
            new("View", "Show or hide speaker notes", () => SetNotesVisible(NotesPane.Visibility != System.Windows.Visibility.Visible), null, "Icon.Note"),
            new("View", "Design: themes and colours", () => ShowDesign(DesignToggle.IsChecked != true), null, "Icon.Color", "theme palette style look"),
            new("View", "Turn smart guides on or off", () => ToggleSnapping(), null, "Icon.Ruler", "snap snapping guides"),
            new("View", "Show or hide rulers", () => ToggleRulers(), null, "Icon.Ruler", "measure inches centimetres"),
            new("View", "Show or hide gridlines", () => ToggleGrid(), new KeyChord(Key.F9, S), "Icon.Grid", "grid lines"),
            new("View", "Snap to grid on or off", () => ToggleSnapToGrid(), null, "Icon.Grid", "align grid"),
            new("View", "Add vertical guide", () => Canvas.AddGuide(vertical: true), null, "Icon.Add", "guide line align") { CanExecute = () => HasSlide },
            new("View", "Add horizontal guide", () => Canvas.AddGuide(vertical: false), null, "Icon.Add", "guide line align") { CanExecute = () => HasSlide },
            new("View", "Remove all guides", () => RemoveAllGuides(), null, "Icon.Delete", "guide lines") { CanExecute = () => Session.Deck.Guides.Count > 0 },
            new("View", "Next slide", () => GoRelative(1), new KeyChord(Key.Next), "Icon.ChevronDown") { CanExecute = () => !HasSelection && !RailFocused },
            new("View", "Previous slide", () => GoRelative(-1), new KeyChord(Key.Prior), "Icon.ChevronUp") { CanExecute = () => !HasSelection && !RailFocused },

            // Slide
            new("Slide", "Duplicate slide", () => DuplicateSlides(), new KeyChord(Key.D, C | S), "Icon.SlideMultiple", "copy") { CanExecute = () => HasSlide },
            new("Slide", "Delete slide", () => DeleteSlides(), null, "Icon.Delete", "remove") { CanExecute = () => HasSlide },
            new("Slide", "Hide or show slide", () => SetHidden(_state.CurrentSlide?.Hidden != true), null, "Icon.SlideHide", "skip") { CanExecute = () => HasSlide },
            new("Slide", "Move slide up", () => MoveCurrentSlide(-1), new KeyChord(Key.Up, C | S), "Icon.ArrowUp") { CanExecute = () => HasSlide && !HasSelection },
            new("Slide", "Move slide down", () => MoveCurrentSlide(1), new KeyChord(Key.Down, C | S), "Icon.ArrowDown") { CanExecute = () => HasSlide && !HasSelection },
            new("Slide", "Add a section here", () => AskSection(), null, "Icon.TextHeader1", "group chapter part") { CanExecute = () => HasSlide },
            new("Slide", "Background picture…", () => PickBackgroundPicture(), null, "Icon.Image", "wallpaper photo") { CanExecute = () => HasSlide },
            new("Slide", "Reset background to the theme", () => SetSlideBackground(null), null, "Icon.ArrowReset") { CanExecute = () => _state.CurrentSlide?.Background != null },

            // Show
            new("Present", "Present from the beginning", () => Present(fromCurrent: false), new KeyChord(Key.F5), "Icon.PlayFilled", "slide show start") { WorksWhileTyping = true, WorksInSlideText = true },
            new("Present", "Present from this slide", () => Present(fromCurrent: true), new KeyChord(Key.F5, S), "Icon.Play", "slide show current") { WorksWhileTyping = true, WorksInSlideText = true },
            new("Present", "Presenter view on this screen", () => Present(fromCurrent: true, presenterOnly: true), new KeyChord(Key.F5, A), "Icon.Presenter", "rehearse single screen") { WorksWhileTyping = true },
            new("Present", "Rehearse timings", () => Present(fromCurrent: false, presenterOnly: true, rehearse: true), null, "Icon.Timer", "practice time clock"),
            new("Present", "Preview animations on this slide", () => PreviewAnimations(), null, "Icon.Play", "builds effects play") { CanExecute = () => HasSlide },
            new("Present", "Preview transition", () => PreviewTransition(), null, "Icon.SlideTransition", "morph fade push") { CanExecute = () => HasSlide },
        };

        foreach (var cls in new[] { EffectClass.Entrance, EffectClass.Emphasis, EffectClass.Exit, EffectClass.Motion })
        {
            var prefix = cls switch { EffectClass.Entrance => "Entrance", EffectClass.Emphasis => "Emphasis", EffectClass.Exit => "Exit", _ => "Motion path" };
            foreach (var kind in EffectCatalog.KindsOf(cls))
            {
                var (c, k) = (cls, kind);
                list.Add(new AppCommand("Animation", $"Animate: {prefix} – {EffectCatalog.Name(c, k)}", () => AddAnimation(c, k), null, "Icon.StarEmphasis", "add animation effect " + prefix.ToLowerInvariant())
                {
                    CanExecute = () => HasSelection,
                });
            }
        }
        foreach (var layout in LayoutEngine.All)
        {
            var id = layout.Id;
            list.Add(new AppCommand("Layout", "Layout: " + layout.Name, () => ApplyLayout(id), null, "Icon.SlideLayout", layout.Description) { CanExecute = () => HasSlide });
            list.Add(new AppCommand("Insert", "New slide: " + layout.Name, () => NewSlide(id), null, "Icon.SlideAdd", layout.Description));
        }
        foreach (var kind in new[] { TransitionKind.None, TransitionKind.Fade, TransitionKind.Push, TransitionKind.Slide, TransitionKind.Zoom, TransitionKind.Morph })
        {
            var k = kind;
            list.Add(new AppCommand("Transition", "Transition: " + (k == TransitionKind.Slide ? "Slide over" : k.ToString()), () => SetTransition(k), null, "Icon.SlideTransition", "animate") { CanExecute = () => HasSlide });
        }
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var variant in theme.Variants)
            {
                var t = theme;
                var v = variant.Id;
                list.Add(new AppCommand("Theme", $"Theme: {theme.Name} – {variant.Name}", () => ApplyTheme(t, v), null, "Icon.Color", theme.Description));
            }
        }
        list.Add(new AppCommand("Design", "Slide size: widescreen 16:9", () => SetSlideSize(1920, 1080), null, "Icon.SlideSize", "aspect ratio"));
        list.Add(new AppCommand("Design", "Slide size: standard 4:3", () => SetSlideSize(1440, 1080), null, "Icon.SlideSize", "aspect ratio"));
        list.Add(new AppCommand("Design", "Slide size: custom…", () => AskCustomSlideSize(), null, "Icon.SlideSize", "aspect ratio"));
        return list;
    }

    private static void SetAppTheme(AppTheme theme)
    {
        AppSettings.Update(s => s.Theme = theme);
        ThemeManager.SetMode(theme);
    }

    private void ToggleSnapping()
    {
        var on = !AppSettings.Current.SnapToGuides;
        AppSettings.Update(s => s.SnapToGuides = on);
        _window.ShowMessage(on ? "Smart guides are on." : "Smart guides are off. Hold Alt while dragging to skip them once.");
    }

    private void GoToStart()
    {
        if (!ConfirmClose()) return;
        _window.ShowStartView();
    }

    private void Undo()
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        Session.History.Undo();
        _state.Revalidate();
    }

    private void Redo()
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        Session.History.Redo();
        _state.Revalidate();
    }

    private void OnUndo(object sender, System.Windows.RoutedEventArgs e) => Undo();

    private void OnRedo(object sender, System.Windows.RoutedEventArgs e) => Redo();
}
