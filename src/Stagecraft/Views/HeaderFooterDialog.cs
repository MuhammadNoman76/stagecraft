using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;

namespace Stagecraft.Views;

/// <summary>Slide numbers, date and footer text for every slide, with a preview of the current slide.</summary>
public sealed class HeaderFooterDialog : FluentWindow
{
    private readonly Deck _deck;
    private readonly MediaStore _media;
    private readonly Slide _slide;
    private readonly HeaderFooter _value;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly CheckBox _number = new() { Content = "Slide number" };
    private readonly CheckBox _date = new() { Content = "Date" };
    private readonly RadioButton _auto = new() { Content = "Update automatically", GroupName = "date" };
    private readonly RadioButton _fixed = new() { Content = "Fixed", GroupName = "date" };
    private readonly ComboBox _format = new() { MinWidth = 220 };
    private readonly TextBox _fixedText = new();
    private readonly CheckBox _footer = new() { Content = "Footer" };
    private readonly TextBox _footerText = new();
    private readonly CheckBox _hideOnTitle = new() { Content = "Don't show on title slides" };
    private readonly StackPanel _dateOptions = new() { Margin = new Thickness(28, 8, 0, 0) };
    private bool _ok;
    private bool _loading;

    private HeaderFooterDialog(Deck deck, MediaStore media, Slide slide)
    {
        _deck = deck;
        _media = media;
        _slide = slide;
        _value = deck.Footer.Clone();
        Title = "Header and footer";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid { Margin = new Thickness(24, 22, 24, 24) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var left = new StackPanel();
        var h = new TextBlock { Text = "Header and footer" };
        h.SetResourceReference(StyleProperty, "Text.Subtitle");
        left.Children.Add(h);
        var m = new TextBlock
        {
            Text = "Shown along the bottom of every slide, in the theme's fonts and colours.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 16),
        };
        m.SetResourceReference(StyleProperty, "Text.BodySecondary");
        left.Children.Add(m);

        left.Children.Add(_number);
        _date.Margin = new Thickness(0, 12, 0, 0);
        left.Children.Add(_date);
        var formats = DateFormats();
        foreach (var (key, label) in formats) _format.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        AutomationProperties.SetName(_format, "Date format");
        AutomationProperties.SetName(_fixedText, "Fixed date text");
        _dateOptions.Children.Add(_auto);
        _format.Margin = new Thickness(26, 6, 0, 0);
        _format.HorizontalAlignment = HorizontalAlignment.Left;
        _dateOptions.Children.Add(_format);
        _fixed.Margin = new Thickness(0, 10, 0, 0);
        _dateOptions.Children.Add(_fixed);
        _fixedText.Margin = new Thickness(26, 6, 0, 0);
        Ui.SetPlaceholder(_fixedText, "e.g. Spring meeting 2026");
        _dateOptions.Children.Add(_fixedText);
        left.Children.Add(_dateOptions);

        _footer.Margin = new Thickness(0, 14, 0, 0);
        left.Children.Add(_footer);
        _footerText.Margin = new Thickness(28, 6, 0, 0);
        Ui.SetPlaceholder(_footerText, "Footer text");
        AutomationProperties.SetName(_footerText, "Footer text");
        left.Children.Add(_footerText);
        _hideOnTitle.Margin = new Thickness(0, 18, 0, 0);
        left.Children.Add(_hideOnTitle);
        body.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        var pl = new TextBlock { Text = "Preview", FontSize = 12, Margin = new Thickness(1, 6, 0, 6) };
        pl.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        right.Children.Add(pl);
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = _preview, ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
        right.Children.Add(frame);
        var hint = new TextBlock
        {
            Text = "Turn slide numbers on here and they replace the numbers some themes show.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(1, 10, 0, 0),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        right.Children.Add(hint);
        body.Children.Add(right);
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Apply to all slides", IsDefault = true, MinWidth = 150 };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) =>
        {
            Read();
            _ok = true;
            Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        root.Children.Add(footer);
        Content = root;

        // initial values
        _loading = true;
        _number.IsChecked = _value.SlideNumber;
        _date.IsChecked = _value.Date;
        _auto.IsChecked = !_value.FixedDate;
        _fixed.IsChecked = _value.FixedDate;
        _format.SelectedIndex = Math.Max(0, formats.FindIndex(f => f.Key == _value.DateFormat));
        _fixedText.Text = _value.DateText ?? DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        _footer.IsChecked = !string.IsNullOrWhiteSpace(_value.Text);
        _footerText.Text = _value.Text ?? deck.Title;
        _hideOnTitle.IsChecked = _value.HideOnTitle;
        _loading = false;

        foreach (var c in new System.Windows.Controls.Primitives.ToggleButton[] { _number, _date, _auto, _fixed, _footer, _hideOnTitle })
        {
            c.Checked += (_, _) => Changed();
            c.Unchecked += (_, _) => Changed();
        }
        _format.SelectionChanged += (_, _) => Changed();
        _fixedText.TextChanged += (_, _) => Changed();
        _footerText.TextChanged += (_, _) => Changed();
        Loaded += (_, _) => Changed();
    }

    private static List<(string Key, string Label)> DateFormats()
    {
        var now = DateTime.Now;
        var sample = new HeaderFooter();
        string Show(string key)
        {
            sample.DateFormat = key;
            return sample.DateFor(now);
        }
        return new List<(string, string)> { ("long", Show("long")), ("short", Show("short")), ("iso", Show("iso")) };
    }

    private void Read()
    {
        _value.SlideNumber = _number.IsChecked == true;
        _value.Date = _date.IsChecked == true;
        _value.FixedDate = _fixed.IsChecked == true;
        _value.DateFormat = (_format.SelectedItem as ComboBoxItem)?.Tag as string ?? "long";
        _value.DateText = _fixedText.Text.Trim();
        _value.Text = _footer.IsChecked == true && _footerText.Text.Trim().Length > 0 ? _footerText.Text.Trim() : null;
        _value.HideOnTitle = _hideOnTitle.IsChecked == true;
    }

    private void Changed()
    {
        if (_loading) return;
        var dateOn = _date.IsChecked == true;
        _dateOptions.IsEnabled = dateOn;
        _format.IsEnabled = dateOn && _auto.IsChecked == true;
        _fixedText.IsEnabled = dateOn && _fixed.IsChecked == true;
        _footerText.IsEnabled = _footer.IsChecked == true;
        Read();
        try
        {
            // The current slide with these settings (a light copy of the deck: same slides, other footer).
            var copy = new Deck { Width = _deck.Width, Height = _deck.Height, Theme = _deck.Theme, Slides = _deck.Slides, Title = _deck.Title, Footer = _value.Clone() };
            var slide = _value.HideOnTitle && _slide.Layout == Core.Layouts.LayoutIds.Title
                ? _deck.Slides.FirstOrDefault(s => s.Layout != Core.Layouts.LayoutIds.Title) ?? _slide
                : _slide;
            _preview.Source = SlideRenderer.Render(slide, new RenderContext(copy, _media), 600);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Header and footer preview");
        }
    }

    /// <summary>Shows the dialog. Returns the new settings, or null when cancelled.</summary>
    public static HeaderFooter? Ask(Window owner, Deck deck, MediaStore media, Slide slide)
    {
        var d = new HeaderFooterDialog(deck, media, slide) { Owner = owner };
        d.ShowDialog();
        return d._ok ? d._value : null;
    }
}
