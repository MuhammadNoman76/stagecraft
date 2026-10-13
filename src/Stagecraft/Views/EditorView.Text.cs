using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using TextElement = Stagecraft.Core.Model.TextElement;
using Stagecraft.Views.Editor;
using WpfTextElement = System.Windows.Documents.TextElement;

namespace Stagecraft.Views;

/// <summary>The text formatting of the selection as the panel shows it (null = mixed or not applicable).</summary>
public sealed class TextState
{
    public TextRole? Role { get; set; }
    public string? Font { get; set; }
    public double? Size { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Uppercase { get; set; }
    public string? Color { get; set; }
    public TextAlign? Align { get; set; }
    public VerticalAlign? VAlign { get; set; }
    public ListKind? List { get; set; }
    public double? LineSpacing { get; set; }
    public bool? AutoFit { get; set; }
}

public partial class EditorView
{
    private static readonly double[] PointSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96, 110, 128, 150, 180 };

    private IEnumerable<TextBody> SelectedBodies() => _state.SelectedElements.Select(e => e switch
    {
        TextElement t => t.Text,
        ShapeElement { IsLine: false, Text: { } b } => b,
        _ => null,
    }).Where(b => b != null)!;

    private static T? Same<T>(IEnumerable<T> values) where T : struct
    {
        var list = values.ToList();
        if (list.Count == 0) return null;
        return list.All(v => EqualityComparer<T>.Default.Equals(v, list[0])) ? list[0] : null;
    }

    private static string? SameText(IEnumerable<string?> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return null;
        return list.All(v => v == list[0]) ? list[0] : null;
    }

    private static bool UsesHeadingFont(TextRole role) => role is TextRole.Title or TextRole.Heading or TextRole.Stat or TextRole.Quote;

    public TextState ReadTextState()
    {
        var style = _state.Style;
        var bodies = SelectedBodies().ToList();
        var state = new TextState();
        if (style == null) return state;
        if (bodies.Count == 0)
        {
            // Shapes without text yet: show the defaults new text would get.
            state.Role = TextRole.Body;
            state.Align = TextAlign.Center;
            state.VAlign = VerticalAlign.Middle;
            state.AutoFit = true;
            state.Size = style.RoleFont(TextRole.Body).Size;
            state.Font = "body";
            return state;
        }
        state.Role = Same(bodies.Select(b => b.Role));
        state.VAlign = Same(bodies.Select(b => b.VAlign));
        state.AutoFit = Same(bodies.Select(b => b.AutoFit));
        state.Uppercase = Same(bodies.Select(b => b.Uppercase ?? style.RoleFont(b.Role).Uppercase));
        state.LineSpacing = Same(bodies.Select(b => b.LineSpacing ?? style.RoleFont(b.Role).LineHeight));

        var rtb = Canvas.TextEditor;
        if (rtb != null)
        {
            var sel = rtb.Selection;
            if (sel.GetPropertyValue(WpfTextElement.FontWeightProperty) is FontWeight w) state.Bold = w.ToOpenTypeWeight() >= 600;
            if (sel.GetPropertyValue(WpfTextElement.FontStyleProperty) is FontStyle fs) state.Italic = fs != FontStyles.Normal;
            if (sel.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection td) state.Underline = td.Any(d => d.Location == TextDecorationLocation.Underline);
            if (sel.GetPropertyValue(WpfTextElement.FontSizeProperty) is double size) state.Size = size;
            if (sel.GetPropertyValue(WpfTextElement.ForegroundProperty) is SolidColorBrush brush) state.Color = RichTextConverter.ColorRefFor(brush.Color, style);
            if (sel.GetPropertyValue(WpfTextElement.FontFamilyProperty) is FontFamily ff) state.Font = FontRef(RichTextConverter.FamilyName(ff), bodies[0], style);
            if (sel.GetPropertyValue(Block.TextAlignmentProperty) is TextAlignment ta)
                state.Align = ta switch { TextAlignment.Center => TextAlign.Center, TextAlignment.Right => TextAlign.Right, TextAlignment.Justify => TextAlign.Justify, _ => TextAlign.Left };
            var paragraph = sel.Start.Paragraph;
            state.List = paragraph?.Parent is ListItem item && item.Parent is List list
                ? list.MarkerStyle == TextMarkerStyle.Decimal ? ListKind.Number : ListKind.Bullet
                : ListKind.None;
            return state;
        }

        state.Size = Same(bodies.Select(b => b.Size ?? style.RoleFont(b.Role).Size));
        state.Bold = Same(bodies.Select(b => b.Bold ?? style.RoleFont(b.Role).Weight >= 600));
        state.Italic = Same(bodies.Select(b => b.Italic ?? style.RoleFont(b.Role).Italic));
        state.Underline = Same(bodies.Select(b => b.Content.Paragraphs.SelectMany(p => p.Runs).Any() && b.Content.Paragraphs.SelectMany(p => p.Runs).All(r => r.Underline == true)));
        state.Color = SameText(bodies.Select(b => b.Color ?? style.RoleFont(b.Role).Color));
        state.Font = SameText(bodies.Select(b => b.Font ?? (b.Role == TextRole.Code ? style.Theme.Fonts.Mono : UsesHeadingFont(b.Role) ? "heading" : "body")));
        state.Align = Same(bodies.Select(b => b.Align));
        state.List = Same(bodies.SelectMany(b => b.Content.Paragraphs).Select(p => p.List));
        return state;
    }

    private static string FontRef(string family, TextBody body, SlideStyle style)
    {
        if (string.Equals(family, style.Theme.Fonts.Heading, StringComparison.OrdinalIgnoreCase) && (body.Font == "heading" || body.Font == null && UsesHeadingFont(body.Role))) return "heading";
        if (string.Equals(family, style.Theme.Fonts.Body, StringComparison.OrdinalIgnoreCase) && (body.Font == "body" || body.Font == null && !UsesHeadingFont(body.Role))) return "body";
        return family;
    }

    /// <summary>Applies a body-level change to the selected text, or a run-level one to the text selection while editing.</summary>
    private void EditText(string label, Action<TextBody> bodyChange, Action<TextRun>? clearRuns, Action<System.Windows.Controls.RichTextBox>? whileEditing, string? mergeKey = null)
    {
        var rtb = Canvas.TextEditor;
        if (rtb != null && whileEditing != null)
        {
            whileEditing(rtb);
            rtb.Focus();
            Panel.Rebuild();
            return;
        }
        if (Canvas.IsEditingText) Canvas.CommitTextEdit();
        var style = _state.Style;
        EditSelected(label, list =>
        {
            foreach (var e in list)
            {
                TextBody? body = e switch
                {
                    TextElement t => t.Text,
                    ShapeElement { IsLine: false } s => s.Text ??= NewShapeText(s, style),
                    _ => null,
                };
                if (body == null) continue;
                bodyChange(body);
                if (clearRuns != null)
                    foreach (var p in body.Content.Paragraphs)
                    foreach (var r in p.Runs) clearRuns(r);
            }
        }, mergeKey);
    }

    private static TextBody NewShapeText(ShapeElement s, SlideStyle? style) => new()
    {
        Role = TextRole.Body,
        Align = TextAlign.Center,
        VAlign = VerticalAlign.Middle,
        Inset = 16,
        Content = RichText.FromPlain(""),
    };

    public void SetRole(TextRole role) => EditText("Text style", b =>
    {
        b.Role = role;
        b.Size = null;
        b.Font = null;
        b.Bold = null;
        b.Italic = null;
        b.Color = null;
        b.Uppercase = null;
        b.LineSpacing = null;
    }, r =>
    {
        r.Size = null;
        r.Font = null;
    }, null);

    public void SetFont(string font) => EditText("Font", b => b.Font = font, r => r.Font = null,
        rtb => rtb.Selection.ApplyPropertyValue(WpfTextElement.FontFamilyProperty, FontCatalog.Get(_state.Style!.FontName(font, _state.Deck.Theme.Fonts.Body))));

    public void SetFontSize(double units) => EditText("Text size", b => b.Size = Math.Round(units, 1), r => r.Size = null,
        rtb => rtb.Selection.ApplyPropertyValue(WpfTextElement.FontSizeProperty, Math.Max(2, units)), "fontsize");

    public void ChangeFontSize(int direction)
    {
        var state = ReadTextState();
        var current = (state.Size ?? _state.Style?.RoleFont(TextRole.Body).Size ?? 36) / 2;
        double next;
        if (direction > 0) next = PointSizes.FirstOrDefault(p => p > current + 0.4);
        else next = PointSizes.LastOrDefault(p => p < current - 0.4);
        if (next <= 0) next = direction > 0 ? current * 1.2 : Math.Max(4, current / 1.2);
        SetFontSize(next * 2);
    }

    public void SetBold(bool on) => EditText(on ? "Bold" : "Not bold", b => b.Bold = on, r => r.Bold = null,
        rtb => rtb.Selection.ApplyPropertyValue(WpfTextElement.FontWeightProperty, on ? FontWeights.Bold : FontWeights.Normal));

    public void SetItalic(bool on) => EditText(on ? "Italic" : "Not italic", b => b.Italic = on, r => r.Italic = null,
        rtb => rtb.Selection.ApplyPropertyValue(WpfTextElement.FontStyleProperty, on ? FontStyles.Italic : FontStyles.Normal));

    public void SetUnderline(bool on) => EditText(on ? "Underline" : "No underline", _ => { }, r => r.Underline = on ? true : null,
        rtb => rtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? TextDecorations.Underline : new TextDecorationCollection()));

    private void ToggleBold() => SetBold(ReadTextState().Bold != true);
    private void ToggleItalic() => SetItalic(ReadTextState().Italic != true);
    private void ToggleUnderline() => SetUnderline(ReadTextState().Underline != true);

    public void SetUppercase(bool on) => EditText("Capitals", b => b.Uppercase = on, null, null);

    public void SetTextColor(string? color) => EditText("Text colour", b => b.Color = color, r => r.Color = null,
        rtb => rtb.Selection.ApplyPropertyValue(WpfTextElement.ForegroundProperty, new SolidColorBrush(_state.Style!.Resolve(color))));

    public void SetAlign(TextAlign align) => EditText("Align text", b =>
    {
        b.Align = align;
        foreach (var p in b.Content.Paragraphs) p.Align = null;
    }, null, rtb => rtb.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, align switch
    {
        TextAlign.Center => TextAlignment.Center,
        TextAlign.Right => TextAlignment.Right,
        TextAlign.Justify => TextAlignment.Justify,
        _ => TextAlignment.Left,
    }));

    public void SetVAlign(VerticalAlign align) => EditText("Vertical alignment", b => b.VAlign = align, null, null);

    public void SetLineSpacing(double spacing) => EditText("Line spacing", b => b.LineSpacing = spacing, null, null);

    public void SetAutoFit(bool on) => EditText(on ? "Shrink text to fit" : "Don't shrink text", b => b.AutoFit = on, null, null);

    public void SetList(ListKind kind)
    {
        var rtb = Canvas.TextEditor;
        if (rtb != null)
        {
            var current = ReadTextState().List;
            if (kind == ListKind.None)
            {
                if (current == ListKind.Bullet) EditingCommands.ToggleBullets.Execute(null, rtb);
                else if (current == ListKind.Number) EditingCommands.ToggleNumbering.Execute(null, rtb);
            }
            else if (current != kind)
            {
                (kind == ListKind.Bullet ? EditingCommands.ToggleBullets : EditingCommands.ToggleNumbering).Execute(null, rtb);
            }
            rtb.Focus();
            Panel.Rebuild();
            return;
        }
        EditText(kind == ListKind.None ? "No list" : kind == ListKind.Bullet ? "Bullets" : "Numbering", b =>
        {
            foreach (var p in b.Content.Paragraphs) p.List = kind;
        }, null, null);
    }
}
