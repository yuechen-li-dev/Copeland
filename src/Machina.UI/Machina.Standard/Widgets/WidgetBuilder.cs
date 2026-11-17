using System.Collections.Immutable;
using System.Globalization;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Standard.Widgets;

/// <summary>
/// A single controlled declaration frame. Build widgets, then prepare it through
/// MachinaWidgetPipeline. Keep stable IDs and apply returned changes in the host.
/// </summary>
public sealed partial class WidgetBuilder
{
    internal WidgetSession Session { get; }
    internal Dictionary<NodeId, WidgetBinding> Bindings { get; } = [];
    internal List<WidgetPopup> Popups { get; } = [];
    public StandardTheme Theme { get; }
    public WidgetStyle Style { get; }
    public ITextMeasurer TextMeasurer { get; set; } = new DeterministicTextMeasurer();

    internal WidgetBuilder(WidgetSession session, StandardTheme theme, WidgetStyle? style)
    {
        Session = session;
        Theme = theme;
        var colors = theme.Colors;
        Style = style ?? new WidgetStyle(colors.Background, colors.Foreground, colors.Border,
            colors.Primary, colors.PrimaryForeground, colors.MutedForeground, colors.Primary);
        if (!double.IsFinite(Style.Height) || Style.Height <= 0 || !double.IsFinite(Style.Padding)
            || Style.Padding < 0 || !double.IsFinite(Style.Gap) || Style.Gap < 0)
            throw new ArgumentException("Widget dimensions must be positive and finite.", nameof(style));
    }

    public UiNode Button(NodeId id, string label, UiAction? action = null, bool disabled = false, double width = 112) =>
        ButtonNode(new(id, id, WidgetKind.Button) { Changed = action, Disabled = disabled, Text = label }, label, width);

    public UiNode Checkbox(NodeId id, string label, bool isChecked, UiAction? changed = null,
        bool disabled = false, bool readOnly = false, bool indeterminate = false, double width = 240)
    {
        var binding = new WidgetBinding(id, id, WidgetKind.Check)
        {
            Checked = !indeterminate && isChecked, Changed = changed, Disabled = disabled, ReadOnly = readOnly, Text = label
        };
        UiNode mark = Box(id.Child("mark"), indeterminate ? 10 : 8, indeterminate ? 2 : 8,
            isChecked || indeterminate ? Style.Accent : Style.Background);
        var box = UI.Rect(UI.Anchor(mark, left: 4, top: indeterminate ? 7 : 4, width: indeterminate ? 10 : 8,
            height: indeterminate ? 2 : 8), id: id.Child("indicator"), width: 18, height: 18,
            style: new UiStyle(Style.Background, BorderColor: disabled ? Style.Border : Style.Muted, BorderThickness: 1));
        return Bind(binding, LabelSlot(id, box, 18, 18, label, disabled ? Style.Muted : Style.Foreground, width), new(UiRole.Checkbox, label, disabled, !disabled)
        {
            Checked = isChecked, Indeterminate = indeterminate, ReadOnly = readOnly
        });
    }

    public UiNode Switch(NodeId id, string label, bool value, UiAction? changed = null,
        bool disabled = false, bool readOnly = false, double width = 240)
    {
        var binding = new WidgetBinding(id, id, WidgetKind.Check)
        {
            Checked = value, Changed = changed, Disabled = disabled, ReadOnly = readOnly, Text = label
        };
        var track = Components.Switch.Create(id.Child("visual"), null, value, disabled, theme: Theme)
            with { Semantics = null, DeclaredAction = null };
        var switchStyle = Theme.Switch.Default;
        return Bind(binding, LabelSlot(id, track, switchStyle.TrackWidth, switchStyle.TrackHeight, label,
            disabled ? Style.Muted : Style.Foreground, width), new(UiRole.Switch, label, disabled, !disabled)
        {
            Checked = value, ReadOnly = readOnly
        });
    }

    public UiNode Toggle(NodeId id, string label, bool pressed, UiAction? changed = null,
        bool disabled = false, double width = 112) => ButtonNode(new(id, id, WidgetKind.Check)
        { Checked = pressed, Changed = changed, Disabled = disabled, Text = label }, label, width,
            new(UiRole.Button, label, disabled, !disabled) { Checked = pressed });

    public UiNode Input(NodeId id, string value, string label, UiAction? changed = null,
        string placeholder = "", bool disabled = false, bool readOnly = false, int maximumLength = 4096, double width = 240)
    {
        if (maximumLength < 1 || maximumLength > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        return TextInput(new(id, id, WidgetKind.Input)
        {
            Text = value, Changed = changed, Disabled = disabled, ReadOnly = readOnly, MaximumLength = maximumLength
        }, label, placeholder, width);
    }

    public UiNode NumberField(NodeId id, decimal value, string label, decimal minimum = -1_000_000_000,
        decimal maximum = 1_000_000_000, decimal step = 1, UiAction? changed = null,
        bool disabled = false, bool readOnly = false, double width = 160)
    {
        ValidateRange(minimum, maximum, step);
        return TextInput(new(id, id, WidgetKind.Number)
        {
            Value = Math.Clamp(value, minimum, maximum), Text = FormatDecimal(Math.Clamp(value, minimum, maximum)),
            Minimum = minimum, Maximum = maximum, Step = step, Changed = changed,
            Disabled = disabled, ReadOnly = readOnly, MaximumLength = 64
        }, label, "", width);
    }

    public UiNode Slider(NodeId id, decimal value, string label, decimal minimum = 0, decimal maximum = 100,
        decimal step = 1, UiAction? changed = null, bool disabled = false,
        WidgetOrientation orientation = WidgetOrientation.Horizontal,
        WidgetDirection direction = WidgetDirection.LeftToRight, double width = 240, double? height = null)
    {
        ValidateRange(minimum, maximum, step);
        ValidateWidth(width);
        double h = height ?? (orientation == WidgetOrientation.Horizontal ? Style.Height : 180);
        if (!double.IsFinite(h) || h <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var binding = new WidgetBinding(id, id, WidgetKind.Slider)
        {
            Value = Math.Clamp(value, minimum, maximum), Minimum = minimum, Maximum = maximum,
            Step = step, Changed = changed, Disabled = disabled, Orientation = orientation, Direction = direction, Text = label
        };
        double progress = maximum == minimum ? 0 : (double)((binding.Value - minimum) / (maximum - minimum));
        if (orientation == WidgetOrientation.Horizontal && direction == WidgetDirection.RightToLeft) progress = 1 - progress;
        // Thumb centre spans [SliderInset, extent - SliderInset]; WidgetSession maps the pointer over the same span.
        double thumbX = orientation == WidgetOrientation.Horizontal ? SliderInset - 5 + progress * Math.Max(0, width - 2 * SliderInset) : (width - 10) / 2;
        double thumbY = orientation == WidgetOrientation.Vertical ? SliderInset - 5 + (1 - progress) * Math.Max(0, h - 2 * SliderInset) : (h - 10) / 2;
        UiNode track = orientation == WidgetOrientation.Horizontal
            ? StandardUI.Place(Box(id.Child("track"), Math.Max(1, width - 8), 4, Style.Border), 4, (h - 4) / 2)
            : StandardUI.Place(Box(id.Child("track"), 4, Math.Max(1, h - 8), Style.Border), (width - 4) / 2, 4);
        var root = Panel(id, width, h,
        [track, StandardUI.Place(Box(id.Child("thumb"), 10, 10, disabled ? Style.Muted : Style.Accent), thumbX, thumbY)]);
        return Bind(binding, FocusOutline(id, root, width, h), new(UiRole.Slider, label, disabled, !disabled)
        { Value = FormatDecimal(binding.Value), Minimum = minimum, Maximum = maximum });
    }

    internal const double SliderInset = 8;

    /// <summary>Invariant decimal text without trailing zeros from arithmetic scale (50.0 -> 50, 12.50 -> 12.5).</summary>
    internal static string FormatDecimal(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    public UiNode Progress(NodeId id, decimal? value, string label, decimal maximum = 100, double width = 240) =>
        Indicator(id, value, label, 0, maximum, width, UiRole.ProgressBar);

    public UiNode Meter(NodeId id, decimal value, string label, decimal minimum = 0, decimal maximum = 100, double width = 240) =>
        Indicator(id, value, label, minimum, maximum, width, UiRole.Meter);

    public UiNode Separator(NodeId id, double width = 240) =>
        Box(id, width, 1, Style.Border) with { Semantics = new(UiRole.Separator) };

    public UiNode Fieldset(NodeId id, string legend, IReadOnlyList<UiNode> controls, double width = 300) =>
        UI.Column([UI.Text(legend, id: id.Child("legend"), color: Style.Foreground, size: Style.TextSize), .. controls],
            id, gap: Style.Gap) with { Semantics = new(UiRole.Group, legend) { LabelledBy = id.Child("legend").Value } };

    public UiNode Form(NodeId id, IReadOnlyList<UiNode> fields) =>
        UI.Column(fields, id, gap: Style.Gap) with { Semantics = new(UiRole.Form) };

    public UiNode Field(NodeId id, string label, UiNode control, string? description = null, string? error = null)
    {
        var related = control with
        {
            Semantics = (control.Semantics ?? new(UiRole.Input)) with
            {
                LabelledBy = id.Child("label").Value,
                DescribedBy = error is not null ? id.Child("error").Value : description is not null ? id.Child("description").Value : null,
                Invalid = error is not null
            }
        };
        var children = new List<UiNode> { UI.Text(label, id: id.Child("label"), size: Style.TextSize, color: Style.Foreground), related };
        if (description is not null) children.Add(UI.Text(description, id: id.Child("description"), size: Style.TextSize, color: Style.Muted));
        if (error is not null) children.Add(UI.Text(error, id: id.Child("error"), size: Style.TextSize, color: Theme.Colors.Destructive));
        return UI.Column(children, id, gap: 4);
    }

    private UiNode TextInput(WidgetBinding binding, string label, string placeholder, double width)
    {
        ValidateWidth(width);
        var visual = Session.TextVisual(binding.Id, binding.Text, binding.Kind == WidgetKind.Number);
        string display = visual.Text.Length == 0 ? placeholder : visual.Text;
        var textStyle = new TextStyle(Style.Foreground, Style.TextSize, TextAlignX.Left, TextAlignY.Center);
        double before = TextMeasurer.MeasureText(visual.Text[..Math.Min(visual.Caret, visual.Text.Length)], textStyle).Width;
        double anchor = TextMeasurer.MeasureText(visual.Text[..Math.Min(visual.Anchor, visual.Text.Length)], textStyle).Width;
        double available = Math.Max(1, width - 2 * Style.Padding);
        double scroll = Session.Focused == binding.Id ? Math.Max(0, before - available + 2) : 0;
        var children = new List<UiNode>();
        if (Session.Focused == binding.Id && visual.Caret != visual.Anchor)
            children.Add(StandardUI.Place(Box(binding.Id.Child("selection"), Math.Max(1, Math.Abs(before - anchor)), Style.Height - 8,
                Theme.Colors.Accent), Style.Padding + Math.Min(before, anchor) - scroll, 4));
        children.Add(StandardUI.Place((TextNode)UI.Text(display, id: binding.Id.Child("text"), size: Style.TextSize,
            color: binding.Disabled || visual.Text.Length == 0 ? Style.Muted : Style.Foreground,
            alignY: TextAlignY.Center) with { FillAvailableSpace = true }, Style.Padding - scroll, 0,
            Math.Max(width, TextMeasurer.MeasureText(display, textStyle).Width + 2), Style.Height));
        if (Session.Focused == binding.Id)
            children.Add(StandardUI.Place(Box(binding.Id.Child("caret"), 1, Style.Height - 10, Style.Foreground), Style.Padding + before - scroll, 5));
        var root = Panel(binding.Id, width, Style.Height, children,
            new UiStyle(Style.Background, BorderColor: Border(binding.Id), BorderThickness: 1, ClipToBounds: true));
        return Bind(binding, root, new(binding.Kind == WidgetKind.Number ? UiRole.SpinButton : UiRole.Input,
            label, binding.Disabled, !binding.Disabled)
        { Value = binding.Text, ReadOnly = binding.ReadOnly, Minimum = binding.Kind == WidgetKind.Number ? binding.Minimum : null,
            Maximum = binding.Kind == WidgetKind.Number ? binding.Maximum : null });
    }

    private UiNode Indicator(NodeId id, decimal? value, string label, decimal minimum, decimal maximum, double width, UiRole role)
    {
        ValidateRange(minimum, maximum, 1);
        ValidateWidth(width);
        double fraction = value is null ? .25 : maximum == minimum ? 1 : (double)((Math.Clamp(value.Value, minimum, maximum) - minimum) / (maximum - minimum));
        var fill = Box(id.Child("fill"), Math.Max(1, width * fraction), 12, Style.Accent);
        return UI.Rect(value == minimum ? null : fill, id, width, 12,
            style: new UiStyle(Theme.Colors.Muted, ClipToBounds: true)) with
        { Semantics = new(role, label) { Value = value is { } v ? FormatDecimal(v) : null, Minimum = minimum, Maximum = maximum } };
    }

    private UiNode Bind(WidgetBinding binding, UiNode root, UiSemantics semantics)
    {
        if (!Bindings.TryAdd(binding.Id, binding)) throw new ArgumentException($"Duplicate widget ID '{binding.Id}'.");
        return root with { Id = binding.Id, Semantics = semantics, DeclaredAction = binding.Intent };
    }

    private UiNode ButtonNode(WidgetBinding binding, string label, double width, UiSemantics? semantics = null)
    {
        ValidateWidth(width);
        bool active = binding.Checked || binding.Key is { } key && binding.Selected.Contains(key);
        var buttonStyle = Theme.Button.Outline with
        {
            Width = width, Height = Style.Height,
            Background = active ? Style.Accent : Style.Background,
            Foreground = binding.Disabled ? Style.Muted : active ? Style.AccentForeground : Style.Foreground,
            BorderColor = Border(binding.Id),
            TextStyle = new TextStyle(binding.Disabled ? Style.Muted : active ? Style.AccentForeground : Style.Foreground,
                Style.TextSize, TextAlignX.Center, TextAlignY.Center)
        };
        // The visible label is ellipsized; the accessible name keeps the full text.
        var root = StandardUI.Button(Ellipsize(label, Math.Max(1, width - 2 * Style.Padding)), id: binding.Id, disabled: binding.Disabled, style: buttonStyle);
        return Bind(binding, root, semantics ?? new(UiRole.Button, label, binding.Disabled, !binding.Disabled));
    }

    /// <summary>
    /// Indicator plus label in a fixed slot. The label is ellipsized and clipped: an overflowing row is a layout
    /// error in Machina, so a long label must never be allowed to exceed the control.
    /// </summary>
    private UiNode LabelSlot(NodeId id, UiNode indicator, double indicatorWidth, double indicatorHeight, string label, ColorToken color, double width)
    {
        ValidateWidth(width);
        double inner = Math.Max(1, width - 2 * Style.Padding), textX = indicatorWidth + Style.Gap, textWidth = Math.Max(1, inner - textX);
        string shown = Ellipsize(label, textWidth);
        var text = UI.Rect(UI.Text(shown, id: id.Child("label"), size: Style.TextSize, color: color, alignY: TextAlignY.Center),
            id.Child("label-slot"), textWidth, 20, style: new UiStyle(ClipToBounds: true));
        var body = UI.Layer(children:
        [
            StandardUI.Place(indicator, 0, Math.Max(0, (20 - indicatorHeight) / 2), indicatorWidth, indicatorHeight),
            StandardUI.Place(text, textX, 0, textWidth, 20)
        ]);
        return Shell(id, body, width);
    }

    /// <summary>Trims to the measured width with "...", using the same measurer as layout.</summary>
    internal string Ellipsize(string text, double width)
    {
        var style = new TextStyle(Style.Foreground, Style.TextSize);
        if (TextMeasurer.MeasureText(text, style).Width <= width) return text;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        for (int i = boundaries.Length - 1; i > 0; i--)
        {
            string candidate = text[..boundaries[i]].TrimEnd() + "...";
            if (TextMeasurer.MeasureText(candidate, style).Width <= width) return candidate;
        }
        return TextMeasurer.MeasureText("...", style).Width <= width ? "..." : "";
    }

    private UiNode Shell(NodeId id, UiNode body, double width)
    {
        ValidateWidth(width);
        return UI.Rect(UI.Anchor(body, left: Style.Padding, top: Math.Max(0, (Style.Height - 20) / 2),
            width: Math.Max(1, width - 2 * Style.Padding), height: 20), id, width, Style.Height,
            style: new UiStyle(Style.Background, BorderColor: Border(id), BorderThickness: 1));
    }

    private UiNode FocusOutline(NodeId id, UiNode body, double width, double height) =>
        UI.Rect(body with { Id = id.Child("body") }, id, width, height,
            style: new UiStyle(BorderColor: Border(id), BorderThickness: 1));

    private ColorToken Border(NodeId id) => Session.Focused == id && Session.FocusVisible ? Style.Focus : Style.Border;
    private static UiNode Panel(NodeId id, double width, double height, IReadOnlyList<UiNode> children, UiStyle? style = null) =>
        UI.Rect(UI.Layer(children: children), id, width, height, style: style);
    private static RectNode Box(NodeId id, double width, double height, ColorToken color) => (RectNode)UI.Rect(id: id, width: width, height: height, color: color);

    private static void ValidateWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    }

    private static void ValidateRange(decimal minimum, decimal maximum, decimal step)
    {
        if (maximum < minimum || step <= 0) throw new ArgumentOutOfRangeException(nameof(maximum), "Range must be ordered; step must be positive.");
        try { _ = maximum - minimum; }
        catch (OverflowException error) { throw new ArgumentOutOfRangeException(nameof(maximum), "Range must fit decimal: " + error.Message); }
    }

    private static void ValidateOptions(IReadOnlyList<WidgetOption> options)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
            if (string.IsNullOrWhiteSpace(option.Key) || !keys.Add(option.Key))
                throw new ArgumentException("Options require distinct, nonempty stable keys.", nameof(options));
    }
}
