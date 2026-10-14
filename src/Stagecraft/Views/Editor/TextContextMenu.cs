using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Stagecraft.Controls;
using Stagecraft.Services;

namespace Stagecraft.Views.Editor;

/// <summary>
/// The right-click menu of text being edited (slide text and speaker notes): spelling suggestions for the word under
/// the pointer, then cut, copy, paste and, on slides, links. Spell checking follows Settings › Check spelling.
/// </summary>
public static class TextContextMenu
{
    /// <summary>Turns spell checking on or off (and picks the language) for a text box.</summary>
    public static void ApplySpelling(TextBoxBase box)
    {
        var on = AppSettings.Current.SpellCheck;
        try
        {
            if (on) box.Language = XmlLanguage.GetLanguage(SpellingLanguage());
            SpellCheck.SetIsEnabled(box, on);
        }
        catch (Exception ex)
        {
            // No spell checker for this language on this PC: typing still works.
            Log.Error(ex, "Spell check");
        }
    }

    /// <summary>The keyboard's language (what the user types), else the Windows display language.</summary>
    private static string SpellingLanguage()
    {
        try
        {
            if (InputLanguageManager.Current?.CurrentInputLanguage is { } input && !string.IsNullOrEmpty(input.IetfLanguageTag)) return input.IetfLanguageTag;
        }
        catch (Exception)
        {
            // no input language manager on this thread
        }
        return CultureInfo.CurrentUICulture.IetfLanguageTag is { Length: > 0 } tag ? tag : "en-US";
    }

    /// <summary>Gives a text box the menu. <paramref name="extra"/> adds items at the end (links on slide text).</summary>
    public static void Attach(TextBoxBase box, Action<ContextMenu>? extra = null)
    {
        var menu = new ContextMenu { PlacementTarget = box };
        box.ContextMenu = menu; // filled again on every opening
        box.ContextMenuOpening += (_, e) =>
        {
            menu.Items.Clear();
            var fromMouse = e.CursorLeft >= 0 && e.CursorTop >= 0;
            var at = fromMouse ? Mouse.GetPosition(box) : new Point(-1, -1);
            Build(menu, box, at.X, at.Y);
            extra?.Invoke(menu);
            // Nothing after a trailing separator.
            while (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is Separator) menu.Items.RemoveAt(menu.Items.Count - 1);
        };
    }

    private static void Build(ContextMenu menu, TextBoxBase box, double x, double y)
    {
        var error = SpellingErrorAt(box, x, y);
        if (error != null)
        {
            var any = false;
            foreach (var suggestion in error.Suggestions.Take(6))
            {
                var mi = new MenuItem
                {
                    Header = suggestion,
                    FontWeight = FontWeights.SemiBold,
                    Command = EditingCommands.CorrectSpellingError,
                    CommandParameter = suggestion,
                    CommandTarget = box,
                };
                menu.Items.Add(mi);
                any = true;
            }
            if (!any) menu.Items.Add(new MenuItem { Header = "(no suggestions)", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = "Ignore all", Command = EditingCommands.IgnoreSpellingError, CommandTarget = box });
            menu.Items.Add(new Separator());
        }
        Add(menu, "Cut", "Icon.Cut", ApplicationCommands.Cut, box, "Ctrl+X");
        Add(menu, "Copy", "Icon.Copy", ApplicationCommands.Copy, box, "Ctrl+C");
        Add(menu, "Paste", "Icon.ClipboardPaste", ApplicationCommands.Paste, box, "Ctrl+V");
        menu.Items.Add(new Separator());
        Add(menu, "Select all", null, ApplicationCommands.SelectAll, box, "Ctrl+A");
    }

    private static void Add(ContextMenu menu, string header, string? icon, RoutedUICommand command, IInputElement target, string gesture)
    {
        var mi = new MenuItem { Header = header, Command = command, CommandTarget = target, InputGestureText = gesture };
        if (icon != null && Application.Current.TryFindResource(icon) is Geometry g) Ui.SetIcon(mi, g);
        menu.Items.Add(mi);
    }

    /// <summary>The misspelt word under the pointer (or at the caret when the menu came from the keyboard).</summary>
    private static SpellingError? SpellingErrorAt(TextBoxBase box, double x, double y)
    {
        if (!SpellCheck.GetIsEnabled(box)) return null;
        try
        {
            switch (box)
            {
                case RichTextBox rtb:
                {
                    var position = x >= 0 && y >= 0 ? rtb.GetPositionFromPoint(new Point(x, y), true) : rtb.CaretPosition;
                    return position == null ? null : rtb.GetSpellingError(position);
                }
                case TextBox tb:
                {
                    var index = x >= 0 && y >= 0 ? tb.GetCharacterIndexFromPoint(new Point(x, y), true) : tb.CaretIndex;
                    return index < 0 ? null : tb.GetSpellingError(index);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Spelling suggestions");
        }
        return null;
    }
}
