using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.Editing;
using Stagecraft.Core.Layouts;
using Stagecraft.Core.Model;
using Stagecraft.Views.Editor;

namespace Stagecraft.Views;

/// <summary>Sorter and outline: views that replace the rail and the canvas.</summary>
public interface IAlternateView
{
    void Refresh();
    void FocusView();
}

public partial class EditorView
{
    private int _altViewIndex;
    private IAlternateView? _alt;
    private SorterView? _sorter;
    private OutlineView? _outline;
    private bool _paletteOpen;

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (_state == null || (!IsLoaded && sender != ViewEditor)) return;
        var index = ViewSorter.IsChecked == true ? 1 : ViewOutline.IsChecked == true ? 2 : 0;
        SetView(index);
    }

    public void SetView(int index)
    {
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        CommitNotes();
        _altViewIndex = index;
        ViewEditor.IsChecked = index == 0;
        ViewSorter.IsChecked = index == 1;
        ViewOutline.IsChecked = index == 2;
        if (index == 0)
        {
            AltView.Visibility = Visibility.Collapsed;
            AltView.Content = null;
            _alt = null;
            Rail.Visibility = Visibility.Visible;
            CenterLayer.Visibility = Visibility.Visible;
            Panel.Visibility = Visibility.Visible;
            InsertBar.IsEnabled = true;
            Rail.Rebuild();
            Canvas.Refresh();
            Canvas.Focus();
            return;
        }
        Rail.Visibility = Visibility.Collapsed;
        CenterLayer.Visibility = Visibility.Collapsed;
        InsertBar.IsEnabled = false;
        if (index == 1)
        {
            _sorter ??= new SorterView(this, _state, _thumbs);
            _alt = _sorter;
            AltView.Content = _sorter;
            Panel.Visibility = Visibility.Visible;
        }
        else
        {
            _outline ??= new OutlineView(this, _state);
            _alt = _outline;
            AltView.Content = _outline;
            Panel.Visibility = Visibility.Collapsed;
        }
        AltView.Visibility = Visibility.Visible;
        Grid.SetColumnSpan(AltView, index == 2 ? 3 : 2);
        _alt.Refresh();
        Dispatcher.BeginInvoke(new Action(() => _alt?.FocusView()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The sorter opens a slide in the editor (double-click / Enter).</summary>
    public void OpenInEditor(Slide slide)
    {
        _state.GoTo(slide);
        SetView(0);
    }

    // ------------------------------------------------------------------ command palette

    private void OnPalette(object sender, RoutedEventArgs e) => OpenPalette();

    public void OpenPalette()
    {
        if (_paletteOpen) return;
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        _paletteOpen = true;
        var palette = new CommandPalette(this);
        palette.Closed += () =>
        {
            OverlayLayer.Children.Remove(palette);
            _paletteOpen = false;
            if (_altViewIndex == 0) Canvas.Focus();
            else _alt?.FocusView();
        };
        OverlayLayer.Children.Add(palette);
        palette.Open();
    }

    // ------------------------------------------------------------------ canvas context menu

    private void ShowCanvasMenu(Point at)
    {
        var menu = new ContextMenu();
        MenuItem Item(ItemsControl parent, string header, string? icon, Action action, string gesture = "", bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            if (icon != null) Ui.SetIcon(mi, (Geometry)FindResource(icon));
            mi.Click += (_, _) => action();
            parent.Items.Add(mi);
            return mi;
        }
        var selected = _state.SelectedElements;
        if (selected.Count > 0)
        {
            var single = _state.SingleSelection;
            switch (single)
            {
                case TextElement or ShapeElement { IsLine: false }:
                    Item(menu, "Edit text", "Icon.Edit", () => Canvas.BeginTextEdit(single!, null, null), "Enter");
                    break;
                case ChartElement chart:
                    Item(menu, "Edit chart data…", "Icon.Table", () => EditChart(chart), "Enter");
                    break;
                case TableElement table:
                    Item(menu, "Edit table…", "Icon.Table", () => EditTable(table), "Enter");
                    break;
                case ImageElement image:
                    Item(menu, image.Media == null ? "Choose picture…" : "Replace picture…", "Icon.ImageAdd", () => ReplaceImage(image), "Enter");
                    break;
                case IconElement icon:
                    Item(menu, "Change icon…", "Icon.Icons", () => PickIcon(icon), "Enter");
                    break;
            }
            if (single != null) menu.Items.Add(new Separator());
            Item(menu, "Cut", "Icon.Cut", () => CopyElements(cut: true), "Ctrl+X");
            Item(menu, "Copy", "Icon.Copy", () => CopyElements(cut: false), "Ctrl+C");
            Item(menu, "Paste", "Icon.ClipboardPaste", Paste, "Ctrl+V");
            Item(menu, "Duplicate", "Icon.Copy", DuplicateSelection, "Ctrl+D");
            Item(menu, "Delete", "Icon.Delete", DeleteSelection, "Del");
            menu.Items.Add(new Separator());
            var order = Item(menu, "Order", "Icon.Layer", () => { });
            Item(order, "Bring to front", "Icon.PositionToFront", () => MoveZ(ZOrderMove.Front), "Ctrl+Shift+]");
            Item(order, "Bring forward", "Icon.PositionForward", () => MoveZ(ZOrderMove.Forward), "Ctrl+]");
            Item(order, "Send backward", "Icon.PositionBackward", () => MoveZ(ZOrderMove.Backward), "Ctrl+[");
            Item(order, "Send to back", "Icon.PositionToBack", () => MoveZ(ZOrderMove.Back), "Ctrl+Shift+[");
            var align = Item(menu, selected.Count == 1 ? "Align to slide" : "Align", "Icon.AlignLeft", () => { });
            Item(align, "Left", "Icon.AlignLeft", () => Align(AlignMode.Left));
            Item(align, "Centre", "Icon.AlignCenterHorizontal", () => Align(AlignMode.CenterHorizontal));
            Item(align, "Right", "Icon.AlignRight", () => Align(AlignMode.Right));
            Item(align, "Top", "Icon.AlignTop", () => Align(AlignMode.Top));
            Item(align, "Middle", "Icon.AlignCenterVertical", () => Align(AlignMode.CenterVertical));
            Item(align, "Bottom", "Icon.AlignBottom", () => Align(AlignMode.Bottom));
            if (selected.Count > 1)
            {
                Item(menu, "Tidy up", "Icon.Sparkle", TidySelection, "Ctrl+Alt+T");
                Item(menu, "Group", "Icon.Group", GroupSelection, "Ctrl+G");
            }
            if (selected.Any(e => e.GroupId != null)) Item(menu, "Ungroup", "Icon.GroupDismiss", UngroupSelection, "Ctrl+Shift+G");
            if (selected.Count == 2 && selected.All(e => !(e is ShapeElement { IsLine: true }))) Item(menu, "Connect with an arrow", "Icon.Connector", InsertConnector);
            menu.Items.Add(new Separator());
            Item(menu, "Copy style", "Icon.PaintBrush", StartFormatPainter, "Ctrl+Alt+C");
            Item(menu, "Paste style", "Icon.PaintBrush", PasteStyle, "Ctrl+Alt+V", _copiedStyle != null);
            Item(menu, selected.All(e => e.Locked) ? "Unlock" : "Lock", selected.All(e => e.Locked) ? "Icon.LockOpen" : "Icon.LockClosed", ToggleLock, "Ctrl+Shift+L");
        }
        else
        {
            Item(menu, "Paste", "Icon.ClipboardPaste", Paste, "Ctrl+V");
            Item(menu, "Select all", null, SelectAll, "Ctrl+A");
            menu.Items.Add(new Separator());
            Item(menu, "New slide", "Icon.SlideAdd", () => NewSlide(null), "Ctrl+M");
            var layouts = Item(menu, "Layout", "Icon.SlideLayout", () => { });
            foreach (var l in LayoutEngine.All)
            {
                var id = l.Id;
                Item(layouts, l.Name, null, () => ApplyLayout(id));
            }
            Item(menu, "Background picture…", "Icon.Image", PickBackgroundPicture);
            if (_state.CurrentSlide?.Elements.Any(e => e.Locked) == true) Item(menu, "Unlock everything", "Icon.LockOpen", UnlockAll);
            menu.Items.Add(new Separator());
            Item(menu, "Present from this slide", "Icon.Play", () => Present(fromCurrent: true), "Shift+F5");
        }
        menu.PlacementTarget = Canvas;
        menu.IsOpen = true;
    }
}
