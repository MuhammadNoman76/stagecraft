using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Core.Samples;
using Stagecraft.Core.Themes;
using Stagecraft.Services;

namespace Stagecraft.Views;

/// <summary>The start screen: new from a theme, samples, recent decks, recovered work, open and import.</summary>
public partial class StartView
{
    private readonly MainWindow _window;
    private static readonly Dictionary<string, BitmapSource> s_themePreviews = new();
    private static readonly Dictionary<string, BitmapSource> s_samplePreviews = new();

    public const double CardWidth = 232;
    public const double CardHeight = CardWidth * 9 / 16;

    public StartView(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        BuildThemes();
        BuildSamples();
        PreviewKeyDown += OnKey;
    }

    public void FocusFirst()
    {
        if (ThemeCards.Items.Count > 0 && ThemeCards.Items[0] is Button b) b.Focus();
    }

    public void Refresh()
    {
        BuildRecent();
        BuildRecovery();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.O)
        {
            OnOpen(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma)
        {
            OnSettings(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ cards

    /// <summary>A card: preview picture with rounded corners, title and a caption.</summary>
    public static Button MakeCard(ImageSource? image, string title, string caption, string automationName, out Border picture)
    {
        // The picture fills a rounded 16:9 frame that grows with the card (the grid stretches cards to fill rows).
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Thumb.Stroke");
        frame.SetResourceReference(Border.BackgroundProperty, "Card.Background");
        if (image != null) SetCardImage(frame, image);
        picture = frame;
        var shadow = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = new AspectPanel { Ratio = 16.0 / 9, Children = { frame } },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.14 },
        };
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        var sub = new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(2, 2, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        var stack = new StackPanel();
        stack.Children.Add(shadow);
        stack.Children.Add(name);
        if (caption.Length > 0) stack.Children.Add(sub);
        var button = new Button
        {
            Content = stack,
            Padding = new Thickness(8, 8, 8, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = automationName,
        };
        button.SetResourceReference(StyleProperty, "CardButton");
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>Shows a picture in a card's frame (cropped to fill, with the frame's rounded corners).</summary>
    public static void SetCardImage(Border frame, ImageSource image)
    {
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        frame.Background = brush;
    }

    private static BitmapSource ThemePreview(ThemeDefinition theme)
    {
        if (s_themePreviews.TryGetValue(theme.Id, out var cached)) return cached;
        var deck = DeckOperations.NewDeck(theme);
        var slide = deck.Slides[0];
        slide.FindPlaceholder<TextElement>("title")!.Text.Content = RichText.FromPlain("Your big idea");
        slide.FindPlaceholder<TextElement>("subtitle")!.Text.Content = RichText.FromPlain(theme.Description);
        var bitmap = SlideRenderer.Render(slide, new RenderContext(deck, new MediaStore()), (int)(CardWidth * 2.6));
        s_themePreviews[theme.Id] = bitmap;
        return bitmap;
    }

    private void BuildThemes()
    {
        ThemeCards.Items.Clear();
        foreach (var theme in ThemeCatalog.All)
        {
            var card = MakeCard(ThemePreview(theme), theme.Name, theme.Description, $"New presentation with the {theme.Name} theme", out _);
            card.Click += (_, _) => NewDeck(theme);
            ThemeCards.Items.Add(card);
        }
    }

    private void NewDeck(ThemeDefinition theme)
    {
        var deck = DeckOperations.NewDeck(theme);
        AppSettings.Update(s =>
        {
            s.LastTheme = theme.Id;
            s.LastVariant = null;
        });
        App.Current.OpenSession(new DeckSession(deck, new MediaStore()), _window);
    }

    private void BuildSamples()
    {
        SampleCards.Items.Clear();
        foreach (var sample in DemoDecks.All)
        {
            if (!s_samplePreviews.TryGetValue(sample.Id, out var preview))
            {
                try
                {
                    var doc = sample.Create();
                    preview = SlideRenderer.Render(doc.Deck.Slides[0], new RenderContext(doc.Deck, doc.Media), (int)(CardWidth * 2.6));
                    s_samplePreviews[sample.Id] = preview;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Sample preview " + sample.Id);
                    continue;
                }
            }
            var card = MakeCard(preview, sample.Title, sample.Description, "Open the sample " + sample.Title, out _);
            card.Click += (_, _) =>
            {
                var doc = sample.Create();
                var session = new DeckSession(doc.Deck, doc.Media);
                App.Current.OpenSession(session, _window);
            };
            SampleCards.Items.Add(card);
        }
    }

    private void BuildRecent()
    {
        RecentCards.Items.Clear();
        var recent = AppSettings.Current.Recent.OrderByDescending(r => r.Pinned).ThenByDescending(r => r.LastOpened).Take(16).ToList();
        NoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in recent)
        {
            var path = item.Path;
            var exists = File.Exists(path);
            var caption = (item.Pinned ? "Pinned · " : "") + When(item.LastOpened) + " · " + (Path.GetDirectoryName(path) ?? "");
            var card = MakeCard(null, Path.GetFileNameWithoutExtension(path), exists ? caption : "Not found: " + path, "Open " + Path.GetFileName(path), out var image);
            card.ToolTip = path;
            card.Opacity = exists ? 1 : 0.55;
            card.Click += (_, _) => App.Current.OpenPath(path, _window);
            card.ContextMenu = RecentMenu(item);
            RecentCards.Items.Add(card);
            if (exists) LoadCover(path, image);
        }
    }

    private static async void LoadCover(string path, Border target)
    {
        var bytes = await Task.Run(() => DeckFile.ReadCover(path));
        if (bytes == null) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            SetCardImage(target, bmp);
        }
        catch
        {
            // no preview
        }
    }

    private ContextMenu RecentMenu(RecentDeck item)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, string icon, Action action)
        {
            var mi = new MenuItem { Header = header };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }
        Item("Open", "Icon.Open", () => App.Current.OpenPath(item.Path, _window));
        Item("Open in a new window", "Icon.WindowNew", () => App.Current.OpenPath(item.Path, null));
        Item("Show in folder", "Icon.FolderOpen", () => ShowInFolder(item.Path));
        menu.Items.Add(new Separator());
        Item(item.Pinned ? "Unpin" : "Pin to the top", item.Pinned ? "Icon.PinOff" : "Icon.Pin", () =>
        {
            AppSettings.Update(s =>
            {
                var r = s.Recent.FirstOrDefault(x => string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                if (r != null) r.Pinned = !r.Pinned;
            });
            BuildRecent();
        });
        Item("Remove from this list", "Icon.Dismiss", () =>
        {
            AppSettings.RemoveRecent(item.Path);
            BuildRecent();
        });
        return menu;
    }

    public static void ShowInFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Show in folder");
        }
    }

    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var days = (DateTime.Now.Date - local.Date).TotalDays;
        if (days < 1) return "Today " + local.ToString("t", CultureInfo.CurrentCulture);
        if (days < 2) return "Yesterday";
        if (days < 7) return local.ToString("dddd", CultureInfo.CurrentCulture);
        return local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    // ------------------------------------------------------------------ recovery

    private void BuildRecovery()
    {
        RecoveryPanel.Children.Clear();
        var orphans = Recovery.FindOrphans()
            .Where(o => !App.Current.DeckWindows.Any(w => w.Editor?.RecoveryKey == o.Key))
            .ToList();
        RecoveryPanel.Visibility = orphans.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var entry in orphans.Take(4))
        {
            var card = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(16, 12, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "Status.CautionBackground");
            card.SetResourceReference(Border.BorderBrushProperty, "Card.Stroke");
            var dock = new DockPanel();
            var icon = new IconView { Width = 18, Height = 18, Margin = new Thickness(0, 1, 14, 0), VerticalAlignment = VerticalAlignment.Top, Data = (Geometry)FindResource("Icon.History") };
            icon.SetResourceReference(IconView.ForegroundProperty, "Status.Caution");
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(buttons, Dock.Right);
            var open = new Button { Content = "Recover", MinWidth = 96 };
            open.SetResourceReference(StyleProperty, "AccentButton");
            var discard = new Button { Content = "Discard", Margin = new Thickness(8, 0, 0, 0), MinWidth = 88 };
            buttons.Children.Add(open);
            buttons.Children.Add(discard);
            dock.Children.Add(buttons);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"Unsaved changes to “{entry.Title}” were kept", FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = $"Stagecraft closed unexpectedly. A copy was saved {When(entry.SavedUtc).ToLowerInvariant()}.",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            });
            dock.Children.Add(text);
            card.Child = dock;
            open.Click += (_, _) => Recover(entry);
            discard.Click += (_, _) =>
            {
                Recovery.Discard(entry.Key);
                BuildRecovery();
            };
            RecoveryPanel.Children.Add(card);
        }
    }

    private async void Recover(RecoveryEntry entry)
    {
        var doc = await _window.RunBusyAsync("Recovering “" + entry.Title + "”…", "", () => DeckFile.Load(entry.DeckPath));
        if (doc == null) return;
        var path = entry.OriginalPath != null && File.Exists(entry.OriginalPath) ? entry.OriginalPath : null;
        var session = new DeckSession(doc.Deck, doc.Media, path);
        session.MarkDirty();
        var window = App.Current.OpenSession(session, _window, "Recovered. Save to keep these changes.");
        window.Editor?.AdoptRecoveryKey(entry.Key);
    }

    // ------------------------------------------------------------------ buttons

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a presentation",
            Filter = "Presentations|*.stagecraft;*.pptx|Stagecraft presentations|*.stagecraft|PowerPoint presentations|*.pptx",
            CheckFileExists = true,
        };
        if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        App.Current.OpenPath(dialog.FileName, _window);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a PowerPoint presentation",
            Filter = "PowerPoint presentations|*.pptx",
            CheckFileExists = true,
        };
        if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return;
        AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
        App.Current.OpenPath(dialog.FileName, _window);
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsWindow.ShowSingle(_window);

    private void OnAbout(object sender, RoutedEventArgs e) => AboutDialog.Open(_window);
}
