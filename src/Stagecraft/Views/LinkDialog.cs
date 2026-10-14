using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Model;
using Stagecraft.Services;

namespace Stagecraft.Views;

/// <summary>What the link dialog was closed with.</summary>
public sealed class LinkChoice
{
    /// <summary>The link (null when it was removed).</summary>
    public Hyperlink? Link { get; init; }

    /// <summary>The text to show, when the dialog asked for it.</summary>
    public string? DisplayText { get; init; }

    public bool Removed { get; init; }
}

/// <summary>
/// Insert or edit a link: a web page, an e-mail (with subject), a slide of this presentation (next, previous, first,
/// last, last viewed or a specific one) or a file, with the text to show and a ScreenTip.
/// </summary>
public sealed class LinkDialog : FluentWindow
{
    private readonly Deck _deck;
    private readonly string? _deckFolder;
    private readonly StackPanel _page = new();
    private readonly List<(LinkKind Kind, RadioButton Tab)> _tabs = new();
    private readonly TextBox _display = new();
    private readonly TextBox _tip = new();
    private readonly TextBox _web = new();
    private readonly TextBox _email = new();
    private readonly TextBox _subject = new();
    private readonly TextBox _file = new();
    private readonly ListBox _slides = new() { Height = 236, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private LinkKind _kind;
    private LinkChoice? _result;

    private LinkDialog(Deck deck, string? deckFolder, Hyperlink? existing, string? displayText, bool askDisplay)
    {
        _deck = deck;
        _deckFolder = deckFolder;
        Title = existing != null ? "Edit link" : "Insert link";
        Width = 680;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Link kinds down the left.
        var nav = new StackPanel { Margin = new Thickness(-6, 0, 18, 0) };
        var heading = new TextBlock { Text = Title, Margin = new Thickness(6, 0, 0, 14) };
        heading.SetResourceReference(StyleProperty, "Text.Subtitle");
        nav.Children.Add(heading);
        foreach (var (kind, label, icon) in new[]
                 {
                     (LinkKind.Web, "Web page", "Icon.Globe"), (LinkKind.Email, "E-mail", "Icon.Mail"),
                     (LinkKind.Slide, "A slide in this presentation", "Icon.SlideLink"), (LinkKind.File, "A file", "Icon.DocumentLink"),
                 })
        {
            var k = kind;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new IconView { Width = 16, Height = 16, Data = (Geometry)FindResource(icon), Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 130, VerticalAlignment = VerticalAlignment.Center });
            var tab = new RadioButton { GroupName = "linkkind", Content = row, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
            tab.SetResourceReference(StyleProperty, "NavItem");
            AutomationProperties.SetName(tab, label);
            tab.Checked += (_, _) => ShowPage(k);
            _tabs.Add((kind, tab));
            nav.Children.Add(tab);
        }
        body.Children.Add(nav);

        var right = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(right, 1);
        right.Children.Add(_page);
        if (askDisplay)
        {
            right.Children.Add(Label("Text to show"));
            _display.Text = displayText ?? "";
            AutomationProperties.SetName(_display, "Text to show");
            right.Children.Add(_display);
        }
        right.Children.Add(Label("ScreenTip (shown when the pointer rests on the link)"));
        _tip.Text = existing?.ScreenTip ?? "";
        Ui.SetPlaceholder(_tip, "Optional");
        AutomationProperties.SetName(_tip, "ScreenTip");
        right.Children.Add(_tip);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Status.Critical");
        right.Children.Add(_error);
        body.Children.Add(right);
        root.Children.Add(body);

        // Footer: remove (when editing), OK, cancel.
        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var bar = new DockPanel();
        if (existing != null)
        {
            var remove = new Button { Content = "Remove link", MinWidth = 120 };
            remove.Click += (_, _) =>
            {
                _result = new LinkChoice { Removed = true };
                Close();
            };
            DockPanel.SetDock(remove, Dock.Left);
            bar.Children.Add(remove);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = existing != null ? "Save" : "Insert link", MinWidth = 130, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        bar.Children.Add(buttons);
        footer.Child = bar;
        root.Children.Add(footer);
        Content = root;

        // Fill the fields from the existing link.
        _kind = existing?.Kind ?? LinkKind.Web;
        if (existing != null)
        {
            switch (existing.Kind)
            {
                case LinkKind.Web: _web.Text = existing.Address; break;
                case LinkKind.Email:
                    _email.Text = existing.Address;
                    _subject.Text = existing.Subject ?? "";
                    break;
                case LinkKind.File: _file.Text = existing.Address; break;
            }
        }
        FillSlides(existing);
        _tabs.First(t => t.Kind == _kind).Tab.IsChecked = true;
        Loaded += (_, _) => FocusFirst();
    }

    private TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(1, 14, 0, 5) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        return t;
    }

    private TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 6, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        return t;
    }

    private void FillSlides(Hyperlink? existing)
    {
        void Add(string text, object tag)
        {
            var item = new ListBoxItem { Content = new TextBlock { Text = text, Margin = new Thickness(0, 6, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis }, Tag = tag };
            _slides.Items.Add(item);
        }
        Add("Next slide", SlideJump.Next);
        Add("Previous slide", SlideJump.Previous);
        Add("First slide", SlideJump.First);
        Add("Last slide", SlideJump.Last);
        Add("Last slide viewed", SlideJump.LastViewed);
        for (var i = 0; i < _deck.Slides.Count; i++)
        {
            var s = _deck.Slides[i];
            var title = s.TitleText.Replace('\n', ' ');
            Add($"{i + 1}.  {(title.Length > 0 ? title : "Slide " + (i + 1))}{(s.Hidden ? "  (hidden)" : "")}", s.Id);
        }
        _slides.SelectedIndex = 0;
        if (existing?.Kind == LinkKind.Slide)
        {
            foreach (ListBoxItem item in _slides.Items)
            {
                if (existing.Jump == SlideJump.Specific ? item.Tag as string == existing.SlideId : item.Tag is SlideJump j && j == existing.Jump)
                {
                    _slides.SelectedItem = item;
                    Loaded += (_, _) => _slides.ScrollIntoView(item);
                    break;
                }
            }
        }
        _slides.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "PaletteItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(_slides, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_slides, "Slides");
    }

    private void ShowPage(LinkKind kind)
    {
        _kind = kind;
        _page.Children.Clear();
        _error.Visibility = Visibility.Collapsed;
        switch (kind)
        {
            case LinkKind.Web:
                _page.Children.Add(Label("Address"));
                Ui.SetPlaceholder(_web, "https://example.com");
                AutomationProperties.SetName(_web, "Web address");
                _page.Children.Add(_web);
                _page.Children.Add(Note("Opens in your web browser. Only http:// and https:// addresses are used; Stagecraft adds https:// if you leave it out."));
                break;
            case LinkKind.Email:
                _page.Children.Add(Label("E-mail address"));
                Ui.SetPlaceholder(_email, "name@example.com");
                AutomationProperties.SetName(_email, "E-mail address");
                _page.Children.Add(_email);
                _page.Children.Add(Label("Subject"));
                Ui.SetPlaceholder(_subject, "Optional");
                AutomationProperties.SetName(_subject, "Subject");
                _page.Children.Add(_subject);
                _page.Children.Add(Note("Starts a new e-mail in your mail app."));
                break;
            case LinkKind.Slide:
                _page.Children.Add(Label("Go to"));
                if (_slides.Parent is Border old) old.Child = null;
                var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(0, 4, 0, 4), Child = _slides };
                frame.SetResourceReference(Border.BackgroundProperty, "Card.Background");
                frame.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
                _page.Children.Add(frame);
                break;
            case LinkKind.File:
                _page.Children.Add(Label("File"));
                var row = new DockPanel();
                var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), MinWidth = 96 };
                browse.Click += (_, _) => Browse();
                DockPanel.SetDock(browse, Dock.Right);
                row.Children.Add(browse);
                AutomationProperties.SetName(_file, "File path");
                row.Children.Add(_file);
                _page.Children.Add(row);
                _page.Children.Add(Note(_deckFolder != null
                    ? "Opens with its usual app after asking. Stored relative to the presentation's folder, so it still works when both move together. Programs and scripts are never opened."
                    : "Opens with its usual app after asking. Save the presentation first to store the path relative to it. Programs and scripts are never opened."));
                break;
        }
    }

    private void FocusFirst()
    {
        Control? target = _kind switch { LinkKind.Web => _web, LinkKind.Email => _email, LinkKind.File => _file, _ => _slides };
        target.Focus();
        if (target is TextBox t) t.SelectAll();
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Link to a file", CheckFileExists = true, Filter = "All files|*.*" };
        if (_deckFolder != null) dialog.InitialDirectory = _deckFolder;
        else if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) != true) return;
        _file.Text = _deckFolder != null ? Compat.RelativePath(_deckFolder, dialog.FileName) : dialog.FileName;
    }

    private void Fail(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private void Accept()
    {
        Hyperlink link;
        switch (_kind)
        {
            case LinkKind.Web:
            {
                var url = LinkSafety.NormalizeWeb(_web.Text);
                if (url == null)
                {
                    Fail(string.IsNullOrWhiteSpace(_web.Text) ? "Type a web address." : "That isn't a web address Stagecraft can open. Use one that starts with https:// or http://.");
                    _web.Focus();
                    return;
                }
                link = Hyperlink.Web(url);
                break;
            }
            case LinkKind.Email:
            {
                var address = _email.Text.Trim();
                if (address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) address = address.Substring(7);
                if (!LinkSafety.IsEmail(address))
                {
                    Fail("That doesn't look like an e-mail address (name@example.com).");
                    _email.Focus();
                    return;
                }
                link = Hyperlink.Email(address, string.IsNullOrWhiteSpace(_subject.Text) ? null : _subject.Text.Trim());
                break;
            }
            case LinkKind.Slide:
            {
                if (_slides.SelectedItem is not ListBoxItem item) return;
                link = item.Tag is SlideJump jump ? Hyperlink.ToSlide(jump) : Hyperlink.ToSlide(SlideJump.Specific, (string)item.Tag);
                break;
            }
            default:
            {
                var path = _file.Text.Trim().Trim('"');
                if (path.Length == 0)
                {
                    Fail("Choose a file.");
                    return;
                }
                if (LinkSafety.IsBlockedFile(path))
                {
                    Fail("Links can't open programs or scripts (" + System.IO.Path.GetExtension(path) + " files). Link to a document, picture or folder of files instead.");
                    return;
                }
                link = Hyperlink.ToFile(path);
                break;
            }
        }
        link.ScreenTip = string.IsNullOrWhiteSpace(_tip.Text) ? null : _tip.Text.Trim();
        _result = new LinkChoice { Link = link, DisplayText = _display.Text };
        Close();
    }

    /// <summary>
    /// Asks for a link. <paramref name="askDisplay"/> shows "Text to show" (inserting new linked text). Returns null when cancelled.
    /// </summary>
    public static LinkChoice? Ask(Window owner, Deck deck, string? deckFolder, Hyperlink? existing, string? displayText, bool askDisplay)
    {
        var dialog = new LinkDialog(deck, deckFolder, existing, displayText, askDisplay) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
