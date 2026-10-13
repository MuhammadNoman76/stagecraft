using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Export;
using Stagecraft.Core.Model;
using Stagecraft.Interop;
using Stagecraft.Services;

namespace Stagecraft.Views;

public sealed class ExportChoice
{
    public string Format { get; set; } = "pdf";
    public string Path { get; set; } = "";
    public bool IncludeHidden { get; set; }
    public bool Vector { get; set; } = true;
    public bool CurrentSlideOnly { get; set; }
    public int PixelWidth { get; set; } = 1920;
    public bool OpenWhenDone { get; set; } = true;
}

/// <summary>Export to PDF, PNG / JPG pictures or PowerPoint, with honest notes about what each keeps.</summary>
public sealed class ExportDialog : FluentWindow
{
    private readonly DeckSession _session;
    private readonly ExportChoice _choice = new();
    private readonly StackPanel _options = new();
    private readonly List<(string Format, ToggleButton Card)> _cards = new();
    private bool _ok;

    private ExportDialog(DeckSession session, Slide? current, string? format)
    {
        _session = session;
        Title = "Export";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _choice.Format = format ?? "pdf";

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var title = new TextBlock { Text = "Export “" + session.Deck.Title + "”", TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(StyleProperty, "Text.Subtitle");
        body.Children.Add(title);

        var cards = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 14, -4, 6) };
        cards.Children.Add(Card("pdf", "PDF", "Icon.DocumentPdf", "Share and print"));
        cards.Children.Add(Card("png", "PNG", "Icon.Image", "Sharp pictures"));
        cards.Children.Add(Card("jpg", "JPG", "Icon.Image", "Smaller pictures"));
        cards.Children.Add(Card("pptx", "PowerPoint", "Icon.ArrowExportLtr", "Edit in Office"));
        body.Children.Add(cards);
        body.Children.Add(_options);
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var export = new Button { Content = "Export…", MinWidth = 130, IsDefault = true };
        export.SetResourceReference(StyleProperty, "AccentButton");
        export.Click += (_, _) => Export(current);
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(export);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        root.Children.Add(footer);
        Content = root;
        Select(_choice.Format);
    }

    private ToggleButton Card(string format, string name, string icon, string caption)
    {
        var stack = new StackPanel { Margin = new Thickness(6, 12, 6, 10) };
        stack.Children.Add(new IconView { Width = 26, Height = 26, Data = (Geometry)FindResource(icon) });
        stack.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        var sub = new TextBlock { Text = caption, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        stack.Children.Add(sub);
        var t = new ToggleButton { Content = stack, Margin = new Thickness(4), Padding = new Thickness(0) };
        t.SetResourceReference(StyleProperty, "SubtleToggle");
        t.Height = double.NaN;
        t.Template = (ControlTemplate)FindResource("ExportCardTemplate");
        AutomationProperties.SetName(t, name + ": " + caption);
        t.Click += (_, _) => Select(format);
        _cards.Add((format, t));
        return t;
    }

    private void Select(string format)
    {
        _choice.Format = format;
        foreach (var (f, card) in _cards) card.IsChecked = f == format;
        _options.Children.Clear();
        var hidden = _session.Deck.Slides.Count(s => s.Hidden);
        TextBlock Note(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 10, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            return t;
        }
        RadioButton Radio(string group, string text, bool on, Action set)
        {
            var r = new RadioButton { GroupName = group, Content = text, IsChecked = on, Margin = new Thickness(0, 0, 18, 0) };
            r.Checked += (_, _) => set();
            if (on) set();
            return r;
        }
        StackPanel Row(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        switch (format)
        {
            case "pdf":
                _options.Children.Add(Row(
                    Radio("pdf", "Vector (sharp at any zoom, small files)", true, () => _choice.Vector = true),
                    Radio("pdf", "Pictures", false, () => _choice.Vector = false)));
                _options.Children.Add(Note("Vector pages keep text, shapes and gradients sharp at any zoom and look exactly like your slides; the text can still be selected and searched. Soft glows are embedded as high-resolution pictures. Picture pages are images of each slide."));
                break;
            case "png":
            case "jpg":
                _options.Children.Add(Row(
                    Radio("scope", "All slides (into a folder)", true, () => _choice.CurrentSlideOnly = false),
                    Radio("scope", "This slide", false, () => _choice.CurrentSlideOnly = true)));
                string SizeLabel(int w, string extra = "") => $"{w} × {(int)Math.Round(w / _session.Deck.AspectRatio)}{extra}";
                _options.Children.Add(Row(
                    Radio("size", SizeLabel(1920), true, () => _choice.PixelWidth = 1920),
                    Radio("size", SizeLabel(2560), false, () => _choice.PixelWidth = 2560),
                    Radio("size", SizeLabel(3840, " (4K)"), false, () => _choice.PixelWidth = 3840)));
                break;
            case "pptx":
                _options.Children.Add(Note("Text, shapes, lines, pictures and tables become editable PowerPoint objects, with your speaker notes and slide size. Charts, icons and the theme's background artwork are exported as pictures. Morph becomes a fade, and build animations aren't exported."));
                break;
        }
        if (hidden > 0)
        {
            var include = new CheckBox { Content = $"Include hidden slides ({hidden})", Margin = new Thickness(0, 8, 0, 0), IsChecked = _choice.IncludeHidden };
            include.Click += (_, _) => _choice.IncludeHidden = include.IsChecked == true;
            _options.Children.Add(include);
        }
        var open = new CheckBox { Content = format is "png" or "jpg" ? "Show the pictures when done" : "Open the file when done", Margin = new Thickness(0, 6, 0, 0), IsChecked = _choice.OpenWhenDone };
        open.Click += (_, _) => _choice.OpenWhenDone = open.IsChecked == true;
        _options.Children.Add(open);
    }

    private void Export(Slide? current)
    {
        var deck = _session.Deck;
        var baseName = ImageExporter.SafeName(deck.Title);
        var folder = AppSettings.Current.LastExportFolder is { } f && Directory.Exists(f) ? f
            : _session.FilePath != null ? System.IO.Path.GetDirectoryName(_session.FilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (_choice.Format is "png" or "jpg" && !_choice.CurrentSlideOnly)
        {
            var picked = FolderPicker.Pick(this, "Choose a folder for the pictures", folder);
            if (picked == null) return;
            _choice.Path = System.IO.Path.Combine(picked, baseName);
        }
        else
        {
            var ext = _choice.Format switch { "pdf" => ".pdf", "pptx" => ".pptx", "jpg" => ".jpg", _ => ".png" };
            var name = baseName;
            if (_choice.CurrentSlideOnly && current != null) name += " - slide " + (deck.Slides.IndexOf(current) + 1);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export",
                FileName = name + ext,
                DefaultExt = ext,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = folder,
                Filter = _choice.Format switch
                {
                    "pdf" => "PDF document|*.pdf",
                    "pptx" => "PowerPoint presentation|*.pptx",
                    "jpg" => "JPG picture|*.jpg",
                    _ => "PNG picture|*.png",
                },
            };
            if (dialog.ShowDialog(this) != true) return;
            _choice.Path = dialog.FileName;
        }
        _ok = true;
        Close();
    }

    public static ExportChoice? Ask(Window owner, DeckSession session, Slide? current, string? format)
    {
        var dialog = new ExportDialog(session, current, format) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok ? dialog._choice : null;
    }
}
