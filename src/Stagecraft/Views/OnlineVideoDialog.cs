using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Stagecraft.Controls;

namespace Stagecraft.Views;

/// <summary>Asks for the address of an online video (YouTube, Vimeo…), an optional title, and whether to fetch its picture.</summary>
public sealed class OnlineVideoDialog : FluentWindow
{
    private readonly TextBox _url = new();
    private readonly TextBox _title = new();
    private readonly CheckBox _picture = new() { Content = "Download the video's picture and title from the site", IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private bool _ok;

    private OnlineVideoDialog()
    {
        Title = "Online video";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 24) };
        var h = new TextBlock { Text = "Online video" };
        h.SetResourceReference(StyleProperty, "Text.Subtitle");
        body.Children.Add(h);
        var m = new TextBlock
        {
            Text = "Paste the address of a video on YouTube, Vimeo or another site. It shows as a card on the slide and opens in your browser when you click it in the slide show.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14),
        };
        m.SetResourceReference(StyleProperty, "Text.BodySecondary");
        body.Children.Add(m);
        Ui.SetPlaceholder(_url, "https://www.youtube.com/watch?v=…");
        AutomationProperties.SetName(_url, "Video address");
        body.Children.Add(_url);
        var titleLabel = new TextBlock { Text = "Title (optional)", FontSize = 12, Margin = new Thickness(1, 14, 0, 5) };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        body.Children.Add(titleLabel);
        AutomationProperties.SetName(_title, "Title");
        body.Children.Add(_title);
        body.Children.Add(_picture);
        var note = new TextBlock
        {
            Text = "Only the picture and title are downloaded, once, from the video site (no account or key). Turn it off to work fully offline: the card shows the address instead.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(28, 2, 0, 0),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Text.Tertiary");
        body.Children.Add(note);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Status.Critical");
        body.Children.Add(_error);
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var ok = new Button { Content = "Insert", IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) =>
        {
            if (Services.OnlineVideos.Describe(_url.Text) == null)
            {
                _error.Text = "That isn't a web address. Paste the address of the video's page.";
                _error.Visibility = Visibility.Visible;
                _url.Focus();
                return;
            }
            _ok = true;
            Close();
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        Grid.SetColumn(cancel, 2);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        root.Children.Add(footer);
        Content = root;
        Loaded += (_, _) =>
        {
            try
            {
                if (!Services.LinkLauncher.Suppress && Clipboard.ContainsText() && Services.OnlineVideos.Describe(Clipboard.GetText().Trim()) is { Provider: "YouTube" or "Vimeo" })
                    _url.Text = Clipboard.GetText().Trim();
            }
            catch
            {
                // clipboard busy
            }
            _url.Focus();
            _url.SelectAll();
        };
    }

    public static (string Url, string? Title, bool FetchPicture)? Ask(Window owner)
    {
        var d = new OnlineVideoDialog { Owner = owner };
        d.ShowDialog();
        return d._ok ? (d._url.Text.Trim(), d._title.Text, d._picture.IsChecked == true) : null;
    }
}
