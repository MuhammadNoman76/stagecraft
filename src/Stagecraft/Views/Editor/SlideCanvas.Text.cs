using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Stagecraft.Core.IO;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using TextElement = Stagecraft.Core.Model.TextElement;

namespace Stagecraft.Views.Editor;

public sealed partial class SlideCanvas
{
    private RichTextBox? _textBox;
    private Grid? _textHost;
    private Slide? _textBefore;
    private TextBody? _textBody;
    private bool _textBodyCreated;
    private DispatcherTimer? _fitTimer;
    private double _textScale = 1;

    public Element? EditingElement { get; private set; }

    public bool IsEditingText => _textBox != null;

    /// <summary>The in-place text editor while text is being edited (formatting commands act on its selection).</summary>
    public RichTextBox? TextEditor => _textBox;

    /// <summary>Raised when text editing starts or stops.</summary>
    public event Action? TextEditingChanged;

    /// <summary>Raised when the selection or formatting inside the text editor changes.</summary>
    public event Action? TextSelectionChanged;

    private static ControlTemplate? s_plainTemplate;

    private static ControlTemplate PlainTemplate()
    {
        if (s_plainTemplate != null) return s_plainTemplate;
        var template = new ControlTemplate(typeof(RichTextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.PaddingProperty, new Thickness(0));
        template.VisualTree = host;
        template.Seal();
        s_plainTemplate = template;
        return template;
    }

    private static TextBody? BodyOf(Element e) => e switch
    {
        TextElement t => t.Text,
        ShapeElement s => s.Text,
        _ => null,
    };

    /// <summary>Starts editing an element's text in place.</summary>
    public void BeginTextEdit(Element element, string? typed, Point? at, bool replaceAll = false)
    {
        if (_state?.CurrentSlide is not { } slide) return;
        if (IsEditingText) CommitTextEdit();
        if (element.Locked) return;
        _textBefore = _state.Session.Snapshot(slide);
        _textBodyCreated = false;
        if (element is ShapeElement shape && shape.Text == null)
        {
            if (shape.IsLine) return;
            var style0 = SlideStyle.For(_state.Deck, slide);
            var fillColor = style0.Resolve(shape.Fill.Kind == FillKind.None ? "bg" : shape.Fill.Color);
            shape.Text = new TextBody
            {
                Role = TextRole.Body,
                Align = TextAlign.Center,
                VAlign = VerticalAlign.Middle,
                Inset = 16,
                Color = shape.Fill.Kind == FillKind.None ? null : (SlideStyle.Luminance(fillColor) > 0.45 ? "#16161A" : "white"),
                Content = RichText.FromPlain(""),
            };
            _textBodyCreated = true;
        }
        var body = BodyOf(element);
        if (body == null) return;
        var style = SlideStyle.For(_state.Deck, slide);
        EditingElement = element;
        _textBody = body;

        var box = element is ShapeElement s ? ElementRenderer.TextBoxOf(s, element.Bounds) : element.Bounds;
        var inner = Core.Rendering.TextLayout.Inner(body, box);
        _textScale = Core.Rendering.TextLayout.FitScale(body, inner.Width, inner.Height, style);
        var defaults = RichTextConverter.DefaultsOf(body, style);

        var rtb = new RichTextBox
        {
            Document = RichTextConverter.ToDocument(body, style),
            Template = PlainTemplate(),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            AcceptsTab = true,
            AcceptsReturn = true,
            FocusVisualStyle = null,
            CaretBrush = new SolidColorBrush(defaults.Color),
            SelectionOpacity = 0.35,
            Width = Math.Max(1, inner.Width / _textScale),
            LayoutTransform = new ScaleTransform(_textScale, _textScale),
            VerticalAlignment = body.VAlign switch
            {
                VerticalAlign.Middle => VerticalAlignment.Center,
                VerticalAlign.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            },
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        rtb.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Accent.Fill");
        TextOptions.SetTextFormattingMode(rtb, TextFormattingMode.Ideal);
        System.Windows.Automation.AutomationProperties.SetName(rtb, "Edit text: " + element.Describe);
        DataObject.AddPastingHandler(rtb, OnPasteIntoText);
        TextContextMenu.ApplySpelling(rtb);
        TextContextMenu.Attach(rtb, menu => TextMenuOpening?.Invoke(menu));
        rtb.TextChanged += (_, _) => ScheduleFit();
        rtb.SelectionChanged += (_, _) => TextSelectionChanged?.Invoke();
        rtb.PreviewKeyDown += OnTextKey;

        var host = new Grid { Width = inner.Width, Height = Math.Max(inner.Height, 1), ClipToBounds = false };
        host.Children.Add(rtb);
        _textHost = host;
        _textBox = rtb;
        _stage.Children.Add(host);
        PositionTextEditor();

        if (_visuals.TryGetValue(element.Id, out var visual))
        {
            visual.HideText = true;
            visual.InvalidateVisual();
        }
        _overlay.InvalidateVisual();

        rtb.Loaded += (_, _) =>
        {
            rtb.Focus();
            Keyboard.Focus(rtb);
            if (typed != null)
            {
                if (replaceAll) rtb.SelectAll();
                rtb.Selection.Text = typed;
                rtb.CaretPosition = rtb.Selection.End;
                rtb.Selection.Select(rtb.CaretPosition, rtb.CaretPosition);
            }
            else if (at is { } point)
            {
                var local = new Point((point.X - inner.X) / _textScale, (point.Y - inner.Y) / _textScale);
                var position = rtb.GetPositionFromPoint(local, true);
                if (position != null) rtb.CaretPosition = position;
                else rtb.CaretPosition = rtb.Document.ContentEnd;
            }
            else if (body.Content.IsEmpty)
            {
                rtb.CaretPosition = rtb.Document.ContentStart;
            }
            else
            {
                rtb.SelectAll();
            }
        };
        TextEditingChanged?.Invoke();
    }

    private void PositionTextEditor()
    {
        if (_textHost == null || EditingElement == null) return;
        var e = EditingElement;
        var body = _textBody;
        if (body == null) return;
        var box = e is ShapeElement s ? ElementRenderer.TextBoxOf(s, e.Bounds) : e.Bounds;
        var inner = Core.Rendering.TextLayout.Inner(body, box);
        Canvas.SetLeft(_textHost, inner.X);
        Canvas.SetTop(_textHost, inner.Y);
        _textHost.Width = inner.Width;
        _textHost.Height = Math.Max(1, inner.Height);
        var c = e.Center;
        _textHost.RenderTransform = Math.Abs(e.Rotation % 360) > 0.001 ? new RotateTransform(e.Rotation, c.X - inner.X, c.Y - inner.Y) : Transform.Identity;
    }

    private void ScheduleFit()
    {
        if (_fitTimer == null)
        {
            _fitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _fitTimer.Tick += (_, _) =>
            {
                _fitTimer.Stop();
                UpdateEditorFit();
            };
        }
        _fitTimer.Stop();
        _fitTimer.Start();
    }

    /// <summary>Shrinks the text while typing, exactly like the slide will (auto-fit).</summary>
    private void UpdateEditorFit()
    {
        if (_textBox == null || _textBody == null || EditingElement == null || _state?.CurrentSlide is not { } slide) return;
        var style = SlideStyle.For(_state.Deck, slide);
        var probe = DeckJson.Clone(_textBody);
        probe.Content = RichTextConverter.FromDocument(_textBox.Document, _textBody, style);
        var box = EditingElement is ShapeElement s ? ElementRenderer.TextBoxOf(s, EditingElement.Bounds) : EditingElement.Bounds;
        var inner = Core.Rendering.TextLayout.Inner(probe, box);
        var scale = Core.Rendering.TextLayout.FitScale(probe, inner.Width, inner.Height, style);
        if (Math.Abs(scale - _textScale) < 0.005) return;
        _textScale = scale;
        _textBox.LayoutTransform = new ScaleTransform(scale, scale);
        _textBox.Width = Math.Max(1, inner.Width / scale);
    }

    private void OnTextKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var element = EditingElement;
            CommitTextEdit();
            if (element != null) _state?.Select(new[] { element.Id }, expandGroups: false);
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && _textBox != null)
        {
            // Tab / Shift+Tab indent list items instead of typing a tab.
            var command = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? EditingCommands.DecreaseIndentation : EditingCommands.IncreaseIndentation;
            if (_textBox.Selection.Start.Paragraph?.Parent is ListItem)
            {
                command.Execute(null, _textBox);
                e.Handled = true;
            }
        }
    }

    /// <summary>Pasted text takes the style of the text box (no foreign fonts and colours).</summary>
    private static void OnPasteIntoText(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        var plain = new DataObject();
        plain.SetData(DataFormats.UnicodeText, text);
        e.DataObject = plain;
        e.FormatToApply = DataFormats.UnicodeText;
    }

    /// <summary>Ends editing and records the change (if anything changed).</summary>
    public void CommitTextEdit()
    {
        if (_textBox == null || _state?.CurrentSlide is not { } slide || EditingElement == null || _textBody == null)
        {
            CancelTextEdit();
            return;
        }
        var element = EditingElement;
        var body = _textBody;
        var style = SlideStyle.For(_state.Deck, slide);
        var rich = RichTextConverter.FromDocument(_textBox.Document, body, style);
        var before = DeckJson.ToJson(body.Content);
        var after = DeckJson.ToJson(rich);
        var changed = before != after;
        RemoveTextEditor();

        if (changed)
        {
            body.Content = rich;
            if (element is ShapeElement shape && shape.Text != null && shape.Text.Content.IsEmpty) shape.Text = null;
            if (element is TextElement t && !t.Text.AutoFit && t.Placeholder == null)
            {
                var needed = Core.Rendering.TextLayout.MeasureHeight(t.Text, t.Width, style);
                if (needed > t.Height + 0.5) t.Height = Math.Ceiling(needed);
            }
            _state.Session.Commit(slide, _textBefore!, "Edit text");
        }
        else if (_textBodyCreated && element is ShapeElement shape)
        {
            shape.Text = null;
        }
        _textBefore = null;
        if (_visuals.TryGetValue(element.Id, out var v))
        {
            v.HideText = false;
            v.InvalidateVisual();
        }
        _overlay.InvalidateVisual();
        TextEditingChanged?.Invoke();
    }

    public void CancelTextEdit()
    {
        if (_textBodyCreated && EditingElement is ShapeElement shape) shape.Text = null;
        var element = EditingElement;
        RemoveTextEditor();
        if (element != null && _visuals.TryGetValue(element.Id, out var v))
        {
            v.HideText = false;
            v.InvalidateVisual();
        }
        _textBefore = null;
        TextEditingChanged?.Invoke();
    }

    private void RemoveTextEditor()
    {
        _fitTimer?.Stop();
        if (_textHost != null) _stage.Children.Remove(_textHost);
        _textHost = null;
        _textBox = null;
        _textBody = null;
        EditingElement = null;
    }
}
