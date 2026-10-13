using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;

namespace Stagecraft.Views;

/// <summary>Edit a table's cells (or paste them from a spreadsheet).</summary>
public sealed class TableEditorDialog : FluentWindow
{
    private readonly TableElement _table;
    private readonly CellGrid _grid = new() { FirstColumnWidth = 170, ColumnWidth = 150 };
    private bool _ok;

    private TableEditorDialog(TableElement table)
    {
        _table = DeckJson.Clone(table);
        Title = "Table";
        Width = 900;
        Height = 600;
        MinWidth = 600;
        MinHeight = 400;
        IsDialog = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new DockPanel { Margin = new Thickness(24, 18, 24, 16) };
        var title = new TextBlock { Text = "Table" };
        title.SetResourceReference(StyleProperty, "Text.Subtitle");
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(title);
        var hint = new TextBlock { Text = "Type in the cells, or paste rows copied from a spreadsheet. The first row is the header.", FontSize = 12, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Small("Add row", "Icon.TableInsertRow", () => _grid.AddRow()));
        buttons.Children.Add(Small("Remove row", "Icon.TableDeleteRow", () => _grid.RemoveRow()));
        buttons.Children.Add(Small("Add column", "Icon.TableInsertColumn", () => _grid.AddColumn()));
        buttons.Children.Add(Small("Remove column", "Icon.TableDeleteColumn", () => _grid.RemoveColumn()));
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);
        body.Children.Add(new ScrollViewer { Content = _grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        root.Children.Add(body);

        var footer = new Border();
        footer.SetResourceReference(StyleProperty, "DialogFooter");
        Grid.SetRow(footer, 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = new Button { Content = "Done", MinWidth = 120 };
        done.SetResourceReference(StyleProperty, "AccentButton");
        done.Click += (_, _) =>
        {
            var data = _grid.Read();
            while (data.Count > 1 && data[data.Count - 1].All(c => c.Length == 0)) data.RemoveAt(data.Count - 1);
            _table.Cells = data;
            _ok = true;
            Close();
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        row.Children.Add(done);
        row.Children.Add(cancel);
        footer.Child = row;
        root.Children.Add(footer);
        Content = root;

        var cells = _table.Cells.Count == 0 ? new List<List<string>> { new() { "", "" }, new() { "", "" } } : _table.Cells;
        _grid.BoldFirstRow = _table.HeaderRow;
        _grid.Load(cells.Select(r => (IReadOnlyList<string>)r).ToList());
        Loaded += (_, _) => _grid.FocusCell(0, 0);
    }

    private Button Small(string text, string icon, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
        b.SetResourceReference(StyleProperty, "IconTextButton");
        Ui.SetIcon(b, (Geometry)FindResource(icon));
        b.Click += (_, _) => action();
        return b;
    }

    public static TableElement? Edit(Window owner, TableElement table)
    {
        var dialog = new TableEditorDialog(table) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok ? dialog._table : null;
    }
}
