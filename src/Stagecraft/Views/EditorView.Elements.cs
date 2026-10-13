using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

public partial class EditorView
{
    private Element? _copiedStyle;
    private bool _painterArmed;
    private int _insertCascade;

    /// <summary>Changes the selected elements as one undoable step.</summary>
    public void EditSelected(string label, Action<List<Element>> change, string? mergeKey = null)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        var ids = _state.Selection.ToList();
        if (ids.Count == 0) return;
        Session.EditSlide(slide, label, s =>
        {
            var targets = s.Elements.Where(e => ids.Contains(e.Id)).ToList();
            change(targets);
            DeckOperations.UpdateConnectors(s);
        }, mergeKey);
    }

    /// <summary>Ends merging (after a slider drag), so the next change is a new undo step.</summary>
    public void SealHistory() => Session.History.Seal();

    // ------------------------------------------------------------------ insert

    private Rect PlaceAtCenter(double width, double height)
    {
        var deck = Session.Deck;
        var offset = (_insertCascade++ % 6) * 28;
        return new Rect(Math.Round((deck.Width - width) / 2 + offset), Math.Round((deck.Height - height) / 2 + offset), width, height);
    }

    private void AddElements(string label, params Element[] elements)
    {
        var slide = _state.CurrentSlide;
        if (slide == null || elements.Length == 0) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        Session.EditSlide(slide, label, s => s.Elements.AddRange(elements));
        _state.Select(elements.Select(e => e.Id));
        Canvas.Focus();
    }

    public void InsertText()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var style = SlideStyle.For(Session.Deck, slide);
        var body = new TextBody { Role = TextRole.Body, AutoFit = false, Content = RichText.FromPlain("") };
        var probe = new TextBody { Role = TextRole.Body, Content = RichText.FromPlain("Text") };
        var height = Math.Ceiling(Core.Rendering.TextLayout.MeasureHeight(probe, 900, style));
        var text = new TextElement { Text = body };
        text.Bounds = PlaceAtCenter(900, height);
        AddElements("Add text", text);
        Canvas.BeginTextEdit(text, null, null);
    }

    private void OnInsertText(object sender, RoutedEventArgs e) => InsertText();

    public void InsertShape(ShapeKind kind)
    {
        var deck = Session.Deck;
        var radius = deck.Theme.Radius * 2;
        var (w, h) = kind switch
        {
            ShapeKind.Ellipse => (360.0, 360.0),
            ShapeKind.Triangle => (400.0, 350.0),
            ShapeKind.Diamond => (380.0, 380.0),
            ShapeKind.Line or ShapeKind.Arrow or ShapeKind.Connector => (600.0, 0.0),
            ShapeKind.Callout => (520.0, 280.0),
            _ => (480.0, 300.0),
        };
        var shape = new ShapeElement
        {
            Shape = kind,
            Fill = kind is ShapeKind.Line or ShapeKind.Arrow or ShapeKind.Connector ? Fill.None : Fill.Solid(kind == ShapeKind.Callout ? "bg2" : "accent1"),
            CornerRadius = kind == ShapeKind.RoundedRectangle ? Math.Max(24, radius) : 0,
            EndHead = kind == ShapeKind.Arrow || kind == ShapeKind.Connector ? ArrowHead.Arrow : ArrowHead.None,
            Stroke = kind is ShapeKind.Line or ShapeKind.Arrow or ShapeKind.Connector ? new Stroke { Color = "text", Width = 5 } : null,
        };
        shape.Bounds = PlaceAtCenter(w, h);
        if (kind == ShapeKind.Callout)
        {
            shape.Text = new TextBody { Role = TextRole.Body, Align = TextAlign.Center, VAlign = VerticalAlign.Middle, Inset = 24, Content = RichText.FromPlain("") };
        }
        AddElements("Add " + shape.KindName.ToLowerInvariant(), shape);
    }

    /// <summary>A connector between the two selected elements, or a free one.</summary>
    public void InsertConnector()
    {
        var selected = _state.SelectedElements.Where(e => !(e is ShapeElement { IsLine: true })).ToList();
        if (selected.Count == 2 && _state.CurrentSlide is { } slide)
        {
            var a = selected[0];
            var b = selected[1];
            var connector = new ShapeElement
            {
                Shape = ShapeKind.Connector,
                Fill = Fill.None,
                Stroke = new Stroke { Color = "text2", Width = 4 },
                EndHead = ArrowHead.Arrow,
                StartId = a.Id,
                EndId = b.Id,
                Elbow = Math.Abs(a.Center.X - b.Center.X) > 40 && Math.Abs(a.Center.Y - b.Center.Y) > 40,
            };
            DeckOperations.SetLine(connector, DeckOperations.Anchor(a, ConnectorSide.Auto, b.Center), DeckOperations.Anchor(b, ConnectorSide.Auto, a.Center));
            Session.EditSlide(slide, "Connect", s =>
            {
                s.Elements.Insert(0, connector);
                DeckOperations.UpdateConnectors(s);
            });
            _state.Select(new[] { connector.Id });
            return;
        }
        InsertShape(ShapeKind.Connector);
        _window.ShowMessage("Tip: select two elements first and the connector joins them, following when they move.", 6);
    }

    private void OnInsertShapeMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ShapeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string header, string icon, Action action)
        {
            var mi = new MenuItem { Header = header };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Item("Rectangle", "Icon.Square", () => InsertShape(ShapeKind.Rectangle));
        Item("Rounded rectangle", "Icon.Square", () => InsertShape(ShapeKind.RoundedRectangle));
        Item("Ellipse", "Icon.Circle", () => InsertShape(ShapeKind.Ellipse));
        Item("Triangle", "Icon.Triangle", () => InsertShape(ShapeKind.Triangle));
        Item("Diamond", "Icon.Square", () => InsertShape(ShapeKind.Diamond));
        menu.Items.Add(new Separator());
        Item("Line", "Icon.LineHorizontal1", () => InsertShape(ShapeKind.Line));
        Item("Arrow", "Icon.ArrowRight", () => InsertShape(ShapeKind.Arrow));
        Item("Connector", "Icon.Connector", InsertConnector);
        Item("Callout", "Icon.Chat", () => InsertShape(ShapeKind.Callout));
        menu.IsOpen = true;
    }

    /// <summary>Asks for a picture file. Returns its bytes and extension.</summary>
    private (byte[] Data, string Extension)? PickPictureFile(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.jfif|All files|*.*",
            CheckFileExists = true,
        };
        if (AppSettings.Current.LastFolder is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(_window) != true) return null;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (MediaStore.Decode(bytes, 64) == null)
            {
                MessageDialog.Show(_window, "Stagecraft can't read this picture", "Try a PNG, JPG, GIF, BMP or TIFF file.", "OK", icon: DialogIcon.Warning);
                return null;
            }
            AppSettings.Update(s => s.LastFolder = Path.GetDirectoryName(dialog.FileName));
            return (bytes, Path.GetExtension(dialog.FileName));
        }
        catch (Exception ex)
        {
            _window.ShowError(ex);
            return null;
        }
    }

    public void InsertImage()
    {
        var picture = PickPictureFile("Insert a picture");
        if (picture == null) return;
        InsertPictures(new List<(byte[], string)> { picture.Value }, null, null);
    }

    private void OnInsertImage(object sender, RoutedEventArgs e) => InsertImage();

    public void ReplaceImage(ImageElement image)
    {
        var picture = PickPictureFile(image.Media == null ? "Choose a picture" : "Replace the picture");
        if (picture == null) return;
        var name = Session.Media.Add(picture.Value.Data, picture.Value.Extension);
        var id = image.Id;
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        Session.EditSlide(slide, "Replace picture", s =>
        {
            if (s.Find(id) is ImageElement img)
            {
                img.Media = name;
                img.Crop = null;
            }
        });
    }

    /// <summary>Puts pictures on the slide: into an empty picture placeholder if there is one, else centred.</summary>
    private void InsertPictures(List<(byte[] Data, string Extension)> pictures, Point? at, ImageElement? target)
    {
        var slide = _state.CurrentSlide;
        if (slide == null || pictures.Count == 0) return;
        var names = pictures.Select(p => Session.Media.Add(p.Data, p.Extension)).ToList();
        target ??= _state.SingleSelection as ImageElement;
        target ??= slide.Elements.OfType<ImageElement>().FirstOrDefault(i => i.Media == null && i.Placeholder != null);
        var added = new List<Element>();
        Session.EditSlide(slide, pictures.Count == 1 ? "Add picture" : "Add pictures", s =>
        {
            var queue = new Queue<string>(names);
            if (target != null && s.Find(target.Id) is ImageElement img && (img.Media == null || pictures.Count == 1 && img.Placeholder != null || at != null))
            {
                img.Media = queue.Dequeue();
                img.Crop = null;
                added.Add(img);
            }
            // Fill other empty picture placeholders first (team photos).
            foreach (var empty in s.Elements.OfType<ImageElement>().Where(i => i.Media == null && i.Placeholder != null).ToList())
            {
                if (queue.Count == 0) break;
                empty.Media = queue.Dequeue();
                added.Add(empty);
            }
            var i = 0;
            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                var item = Session.Media.Get(name);
                var w = item?.PixelWidth ?? 1600;
                var h = item?.PixelHeight ?? 1000;
                var max = Math.Min(Session.Deck.Width * 0.6, Session.Deck.Height * 0.6 * w / Math.Max(1, h));
                var width = Math.Min(max, w);
                var height = width * h / Math.Max(1, w);
                var element = new ImageElement { Media = name, Fit = ImageFit.Cover, CornerRadius = 0 };
                var center = at ?? new Point(Session.Deck.Width / 2, Session.Deck.Height / 2);
                element.Bounds = new Rect(Math.Round(center.X - width / 2 + i * 32), Math.Round(center.Y - height / 2 + i * 32), Math.Round(width), Math.Round(height));
                s.Elements.Add(element);
                added.Add(element);
                i++;
            }
        });
        _state.Select(added.Select(a => a.Id));
    }

    private void OnImagesDropped(List<(byte[] Data, string Extension)> images, Point at, ImageElement? target) => InsertPictures(images, at, target);

    public void PickIcon(IconElement? existing)
    {
        var picked = IconPickerDialog.Pick(_window, existing?.Icon, SlideStyle.For(Session.Deck, _state.CurrentSlide ?? new Slide()));
        if (picked == null) return;
        if (existing != null)
        {
            var id = existing.Id;
            Session.EditSlide(_state.CurrentSlide!, "Change icon", s =>
            {
                if (s.Find(id) is IconElement icon) icon.Icon = picked;
            });
            return;
        }
        var element = new IconElement { Icon = picked, Color = "accent1" };
        element.Bounds = PlaceAtCenter(200, 200);
        AddElements("Add icon", element);
    }

    private void OnInsertIcon(object sender, RoutedEventArgs e) => PickIcon(null);

    public void InsertChart(ChartKind kind)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var chart = new ChartElement
        {
            Kind = kind,
            Categories = kind is ChartKind.Pie or ChartKind.Donut ? new List<string> { "North", "South", "East", "West" } : new List<string> { "Q1", "Q2", "Q3", "Q4" },
            Series = kind is ChartKind.Pie or ChartKind.Donut
                ? new List<ChartSeries> { new() { Name = "Share", Values = new List<double> { 42, 26, 18, 14 } } }
                : new List<ChartSeries>
                {
                    new() { Name = "This year", Values = new List<double> { 32, 41, 48, 57 } },
                    new() { Name = "Last year", Values = new List<double> { 28, 30, 36, 40 } },
                },
        };
        chart.Bounds = ContentArea(slide);
        var edited = ChartEditorDialog.Edit(_window, chart, SlideStyle.For(Session.Deck, slide));
        if (edited == null) return;
        AddElements("Add chart", edited);
    }

    private void OnInsertChart(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string header, string icon, ChartKind kind)
        {
            var mi = new MenuItem { Header = header };
            Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => InsertChart(kind);
            menu.Items.Add(mi);
        }
        Item("Bar chart", "Icon.DataBarVertical", ChartKind.Bar);
        Item("Line chart", "Icon.DataLine", ChartKind.Line);
        Item("Pie chart", "Icon.DataPie", ChartKind.Pie);
        Item("Donut chart", "Icon.DataUsage", ChartKind.Donut);
        menu.IsOpen = true;
    }

    /// <summary>Where new charts and tables go: below the title if there is one.</summary>
    private Rect ContentArea(Slide slide)
    {
        var deck = Session.Deck;
        var m = SlideStyle.For(deck, slide).Margin;
        var title = slide.Elements.FirstOrDefault(e => e.Placeholder == "title");
        var top = title != null && title.Y < deck.Height * 0.4 ? title.Y + title.Height + deck.Height * 0.05 : m;
        var body = slide.Elements.FirstOrDefault(e => e.Placeholder is "body" && e is TextElement t && t.Text.Content.IsEmpty);
        if (body != null) return body.Bounds;
        return new Rect(m, top, deck.Width - 2 * m, Math.Max(200, deck.Height - top - m * 0.9));
    }

    public void InsertTable()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var table = new TableElement
        {
            Cells = new List<List<string>>
            {
                new() { "Item", "Q1", "Q2", "Q3" },
                new() { "Revenue", "", "", "" },
                new() { "Customers", "", "", "" },
                new() { "Satisfaction", "", "", "" },
            },
        };
        var area = ContentArea(slide);
        table.Bounds = new Rect(area.X, area.Y, area.Width, Math.Min(area.Height, 4 * 96));
        var edited = TableEditorDialog.Edit(_window, table);
        if (edited == null) return;
        edited.Height = Math.Min(area.Height, Math.Max(3, edited.RowCount) * 96);
        AddElements("Add table", edited);
    }

    private void OnInsertTable(object sender, RoutedEventArgs e) => InsertTable();

    public void EditChart(ChartElement chart)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var edited = ChartEditorDialog.Edit(_window, chart, SlideStyle.For(Session.Deck, slide));
        if (edited == null) return;
        var id = chart.Id;
        Session.EditSlide(slide, "Edit chart", s =>
        {
            var i = s.Elements.FindIndex(e => e.Id == id);
            if (i >= 0) s.Elements[i] = edited;
        });
    }

    public void EditTable(TableElement table)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var edited = TableEditorDialog.Edit(_window, table);
        if (edited == null) return;
        var id = table.Id;
        Session.EditSlide(slide, "Edit table", s =>
        {
            var i = s.Elements.FindIndex(e => e.Id == id);
            if (i >= 0) s.Elements[i] = edited;
        });
    }

    private void OnElementActivated(Element element)
    {
        switch (element)
        {
            case ChartElement chart:
                EditChart(chart);
                break;
            case TableElement table:
                EditTable(table);
                break;
            case IconElement icon:
                PickIcon(icon);
                break;
            case ImageElement image:
                ReplaceImage(image);
                break;
        }
    }

    // ------------------------------------------------------------------ delete, duplicate, select

    public void DeleteSelection()
    {
        var slide = _state.CurrentSlide;
        if (slide == null || _state.Selection.Count == 0) return;
        var ids = _state.Selection.ToList();
        Session.EditSlide(slide, ids.Count == 1 ? "Delete" : "Delete elements", s =>
        {
            s.Elements.RemoveAll(e => ids.Contains(e.Id));
            // Connectors that lose an end become free lines.
            foreach (var c in s.Elements.OfType<ShapeElement>())
            {
                if (c.StartId != null && ids.Contains(c.StartId)) c.StartId = null;
                if (c.EndId != null && ids.Contains(c.EndId)) c.EndId = null;
            }
        });
        _state.ClearSelection();
    }

    private void Delete()
    {
        if (RailFocused || _altViewIndex == 1) DeleteSlides();
        else if (HasSelection) DeleteSelection();
    }

    private void Duplicate()
    {
        if (RailFocused || _altViewIndex == 1 || !HasSelection) DuplicateSlides();
        else DuplicateSelection();
    }

    public void DuplicateSelection()
    {
        var slide = _state.CurrentSlide;
        var selected = _state.SelectedElements;
        if (slide == null || selected.Count == 0) return;
        var copies = DeckOperations.CopyElements(selected, 24, 24);
        Session.EditSlide(slide, "Duplicate", s =>
        {
            var at = selected.Max(e => s.Elements.FindIndex(x => x.Id == e.Id)) + 1;
            s.Elements.InsertRange(at, copies);
        });
        _state.Select(copies.Select(c => c.Id));
    }

    private void SelectAll()
    {
        if (RailFocused || _altViewIndex == 1)
        {
            foreach (var s in Session.Deck.Slides) _state.SelectedSlides.Add(s.Id);
            Rail.SyncSelection();
            _alt?.Refresh();
            return;
        }
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        _state.EnteredGroup = null;
        _state.Select(slide.Elements.Where(e => !e.Locked).Select(e => e.Id));
        Canvas.Focus();
    }

    public void ToggleLock()
    {
        var selected = _state.SelectedElements;
        if (selected.Count == 0) return;
        var lockIt = !selected.All(e => e.Locked);
        EditSelected(lockIt ? "Lock" : "Unlock", list => { foreach (var e in list) e.Locked = lockIt; });
        if (lockIt) _window.ShowMessage("Locked. Locked elements can't be clicked on the slide; use Tab to reach them or Unlock all from the slide's menu.", 6);
        Panel.Rebuild();
    }

    private void UnlockAll()
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var locked = slide.Elements.Where(e => e.Locked).Select(e => e.Id).ToList();
        Session.EditSlide(slide, "Unlock all", s => { foreach (var e in s.Elements) e.Locked = false; });
        _state.Select(locked);
    }

    // ------------------------------------------------------------------ arrange

    public void Align(AlignMode mode)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var size = new Size(Session.Deck.Width, Session.Deck.Height);
        EditSelected("Align", list =>
        {
            DeckOperations.Align(list, mode, size);
            foreach (var e in list.Where(x => x.Placeholder != null)) e.Moved = true;
        });
    }

    public void Distribute(bool horizontal) => EditSelected("Distribute", list => DeckOperations.Distribute(list, horizontal));

    public void TidySelection()
    {
        var selected = _state.SelectedElements;
        var units = DeckOperations.Units(selected);
        if (units.Count < 2) return;
        EditSelected("Tidy up", list =>
        {
            var byId = list.ToDictionary(e => e.Id);
            var groups = DeckOperations.Units(list);
            var rects = groups.Select(g => DeckOperations.BoundsOf(g)).ToList();
            var arranged = Tidy.Arrange(rects);
            for (var i = 0; i < groups.Count; i++)
            {
                var from = rects[i];
                var to = arranged[i];
                if (groups[i].Count == 1 && Math.Abs(groups[i][0].Rotation) < 0.01)
                {
                    var e = groups[i][0];
                    e.Bounds = to;
                }
                else DeckOperations.ScaleInto(groups[i], from, to);
                foreach (var e in groups[i].Where(x => x.Placeholder != null)) e.Moved = true;
            }
        });
        _window.ShowMessage("Tidied: lined up, same sizes and even gaps.", 3);
    }

    public void MoveZ(ZOrderMove move)
    {
        var slide = _state.CurrentSlide;
        if (slide == null || _state.Selection.Count == 0) return;
        var ids = _state.Selection.ToList();
        Session.EditSlide(slide, move switch
        {
            ZOrderMove.Front => "Bring to front",
            ZOrderMove.Back => "Send to back",
            ZOrderMove.Forward => "Bring forward",
            _ => "Send backward",
        }, s => DeckOperations.MoveZ(s, ids, move));
    }

    public void Flip(bool horizontal) => EditSelected(horizontal ? "Flip horizontally" : "Flip vertically", list =>
    {
        var bounds = DeckOperations.BoundsOf(list);
        foreach (var e in list)
        {
            if (horizontal)
            {
                e.FlipH = !e.FlipH;
                if (list.Count > 1) e.X = bounds.Right - (e.X - bounds.Left) - e.Width;
            }
            else
            {
                e.FlipV = !e.FlipV;
                if (list.Count > 1) e.Y = bounds.Bottom - (e.Y - bounds.Top) - e.Height;
            }
            if (Math.Abs(e.Rotation) > 0.01) e.Rotation = -e.Rotation;
        }
    });

    public void GroupSelection()
    {
        if (_state.Selection.Count < 2) return;
        EditSelected("Group", list => DeckOperations.Group(list));
        _state.EnteredGroup = null;
        _state.RaiseSelectionChanged();
    }

    public void UngroupSelection()
    {
        EditSelected("Ungroup", list => DeckOperations.Ungroup(list));
        _state.EnteredGroup = null;
        _state.RaiseSelectionChanged();
    }

    public void SetGeometry(double? x = null, double? y = null, double? w = null, double? h = null, double? rotation = null)
    {
        var selected = _state.SelectedElements;
        if (selected.Count == 0) return;
        EditSelected("Position", list =>
        {
            if (list.Count == 1)
            {
                var e = list[0];
                if (x is { } xv) e.X = xv;
                if (y is { } yv) e.Y = yv;
                if (w is { } wv) e.Width = Math.Max(1, wv);
                if (h is { } hv) e.Height = Math.Max(0, hv);
                if (rotation is { } r) e.Rotation = r;
                if (e.Placeholder != null) e.Moved = true;
                return;
            }
            var from = DeckOperations.BoundsOf(list);
            var to = new Rect(x ?? from.X, y ?? from.Y, Math.Max(1, w ?? from.Width), Math.Max(1, h ?? from.Height));
            if (w == null && h == null)
            {
                foreach (var e in list)
                {
                    e.X += to.X - from.X;
                    e.Y += to.Y - from.Y;
                }
            }
            else DeckOperations.ScaleInto(list, from, to);
        }, "geometry");
    }

    // ------------------------------------------------------------------ fill, border, shape

    public void SetFill(Fill fill) => EditSelected("Fill", list =>
    {
        foreach (var e in list)
        {
            if (e is ShapeElement { IsLine: false } s) s.Fill = fill.Clone();
            else if (e is TextElement t) t.Fill = fill.Kind == FillKind.None ? null : fill.Clone();
        }
    });

    public void SetOpacity(double opacity) => EditSelected("Opacity", list => { foreach (var e in list) e.Opacity = Math.Round(opacity, 2); }, "opacity");

    public void SetCornerRadius(double radius) => EditSelected("Corners", list =>
    {
        foreach (var e in list)
        {
            switch (e)
            {
                case ShapeElement s:
                    s.CornerRadius = Math.Round(radius);
                    if (s.Shape == ShapeKind.Rectangle && radius > 0) s.Shape = ShapeKind.RoundedRectangle;
                    break;
                case TextElement t:
                    t.CornerRadius = Math.Round(radius);
                    break;
                case ImageElement i:
                    i.CornerRadius = Math.Round(radius);
                    break;
            }
        }
    }, "radius");

    public void SetStroke(Stroke? stroke) => EditSelected("Border", list =>
    {
        foreach (var e in list)
        {
            switch (e)
            {
                case ShapeElement s:
                    s.Stroke = stroke?.Clone() ?? (s.IsLine ? new Stroke() : null);
                    break;
                case TextElement t:
                    t.Stroke = stroke?.Clone();
                    break;
                case ImageElement i:
                    i.Stroke = stroke?.Clone();
                    break;
            }
        }
    });

    public void SetStrokeWidth(double width) => EditSelected("Border width", list =>
    {
        foreach (var e in list)
        {
            var stroke = e switch
            {
                ShapeElement s => s.Stroke ??= s.IsLine ? ElementRenderer.LineStroke(s) : null,
                TextElement t => t.Stroke,
                ImageElement i => i.Stroke,
                _ => null,
            };
            if (stroke != null) stroke.Width = width;
        }
    }, "strokewidth");

    public void SetStrokeDash(DashStyleKind dash) => EditSelected("Border style", list =>
    {
        foreach (var e in list)
        {
            var stroke = e switch
            {
                ShapeElement s => s.Stroke ??= s.IsLine ? ElementRenderer.LineStroke(s) : null,
                TextElement t => t.Stroke,
                ImageElement i => i.Stroke,
                _ => null,
            };
            if (stroke != null) stroke.Dash = dash;
        }
    });

    public void SetLineHeads(ArrowHead? start, ArrowHead? end) => EditSelected("Line ends", list =>
    {
        foreach (var s in list.OfType<ShapeElement>().Where(x => x.IsLine))
        {
            if (start is { } a) s.StartHead = a;
            if (end is { } b)
            {
                s.EndHead = b;
                if (s.Shape == ShapeKind.Arrow && b == ArrowHead.None) s.Shape = ShapeKind.Line;
            }
        }
    });

    public void SetElbow(bool on) => EditSelected("Connector route", list => { foreach (var s in list.OfType<ShapeElement>()) s.Elbow = on; });

    // ------------------------------------------------------------------ animation (builds)

    public void SetBuild(BuildEffect effect)
    {
        var slide = _state.CurrentSlide;
        if (slide == null) return;
        var nextStep = slide.Elements.Where(e => e.Build != null && e.Build.Effect != BuildEffect.None).Select(e => e.Build!.Step).DefaultIfEmpty(0).Max() + 1;
        EditSelected("Animation", list =>
        {
            foreach (var e in list)
            {
                if (effect == BuildEffect.None)
                {
                    e.Build = null;
                    continue;
                }
                if (e.Build == null || e.Build.Effect == BuildEffect.None) e.Build = new Build { Effect = effect, Step = nextStep };
                else e.Build.Effect = effect;
            }
        });
    }

    public void EditBuild(Action<Build> change) => EditSelected("Animation", list =>
    {
        foreach (var e in list.Where(e => e.Build != null)) change(e.Build!);
    }, "build");

    // ------------------------------------------------------------------ format painter

    public void StartFormatPainter()
    {
        var source = _state.SingleSelection ?? _state.SelectedElements.FirstOrDefault();
        if (source == null) return;
        _copiedStyle = DeckJson.Clone(source);
        _painterArmed = true;
        Canvas.Cursor = System.Windows.Input.Cursors.Pen;
        _window.ShowMessage("Style copied. Click an element to apply it (Esc cancels), or select elements and press Ctrl+Alt+V.", 5);
        _state.SelectionChanged -= ApplyPainterOnce;
        _state.SelectionChanged += ApplyPainterOnce;
    }

    private void ApplyPainterOnce()
    {
        if (!_painterArmed) return;
        var targets = _state.SelectedElements;
        if (targets.Count == 0 || targets.Any(t => t.Id == _copiedStyle?.Id)) return;
        _painterArmed = false;
        _state.SelectionChanged -= ApplyPainterOnce;
        Canvas.Cursor = null;
        Dispatcher.BeginInvoke(new Action(PasteStyle), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void PasteStyle()
    {
        var source = _copiedStyle;
        if (source == null) return;
        EditSelected("Paste style", list =>
        {
            foreach (var target in list) StyleCopier.Apply(source, target);
        });
    }
}

/// <summary>What "copy style" carries from one element to another.</summary>
public static class StyleCopier
{
    public static void Apply(Element source, Element target)
    {
        target.Opacity = source.Opacity;
        var sb = BodyOf(source);
        var tb = BodyOf(target);
        if (sb != null && tb != null)
        {
            tb.Role = sb.Role;
            tb.Size = sb.Size;
            tb.Font = sb.Font;
            tb.Color = sb.Color;
            tb.Bold = sb.Bold;
            tb.Italic = sb.Italic;
            tb.Uppercase = sb.Uppercase;
            tb.Align = sb.Align;
            tb.VAlign = sb.VAlign;
            tb.LineSpacing = sb.LineSpacing;
            tb.Inset = sb.Inset;
            foreach (var p in tb.Content.Paragraphs)
            foreach (var r in p.Runs)
            {
                r.Size = null;
                r.Color = null;
                r.Font = null;
                r.Bold = null;
                r.Italic = null;
            }
        }
        switch (source, target)
        {
            case (ShapeElement s, ShapeElement t) when s.IsLine == t.IsLine:
                t.Fill = s.Fill.Clone();
                t.Stroke = s.Stroke?.Clone();
                t.CornerRadius = s.CornerRadius;
                t.StartHead = s.StartHead;
                t.EndHead = s.EndHead;
                break;
            case (ShapeElement s, TextElement t) when !s.IsLine:
                t.Fill = s.Fill.Kind == FillKind.None ? null : s.Fill.Clone();
                t.Stroke = s.Stroke?.Clone();
                t.CornerRadius = s.CornerRadius;
                break;
            case (TextElement s, ShapeElement t) when !t.IsLine:
                t.Fill = s.Fill?.Clone() ?? Fill.None;
                t.Stroke = s.Stroke?.Clone();
                t.CornerRadius = s.CornerRadius;
                break;
            case (TextElement s, TextElement t):
                t.Fill = s.Fill?.Clone();
                t.Stroke = s.Stroke?.Clone();
                t.CornerRadius = s.CornerRadius;
                break;
            case (ImageElement s, ImageElement t):
                t.CornerRadius = s.CornerRadius;
                t.Mask = s.Mask;
                t.Stroke = s.Stroke?.Clone();
                break;
            case (IconElement s, IconElement t):
                t.Color = s.Color;
                t.Container = s.Container;
                t.ContainerColor = s.ContainerColor;
                break;
            case (TableElement s, TableElement t):
                t.HeaderColor = s.HeaderColor;
                t.Banded = s.Banded;
                t.HeaderRow = s.HeaderRow;
                t.FontSize = s.FontSize;
                break;
            case (ChartElement s, ChartElement t):
                t.ShowGrid = s.ShowGrid;
                t.ShowLegend = s.ShowLegend;
                t.ShowValues = s.ShowValues;
                t.ValueFormat = s.ValueFormat;
                break;
        }
    }

    private static TextBody? BodyOf(Element e) => e switch
    {
        TextElement t => t.Text,
        ShapeElement { Text: { } b } => b,
        _ => null,
    };
}
