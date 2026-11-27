using System.Collections.Immutable;
using Machina.Core.Nodes;
using Machina.Layout.Rows;

namespace Machina.Standard.Widgets;

/// <summary>
/// Typed, bound overloads of the controlled widgets. Each takes its value as a <see cref="Bound{T}"/> (or a handler)
/// instead of a separate value plus an action to match later, so the host's routing switch disappears. Apply the
/// proposals with <see cref="WidgetSession.Apply"/>. These forward to the controlled methods; behaviour is identical.
/// </summary>
public static class BoundWidgets
{
    public static UiNode Button(this WidgetBuilder w, NodeId id, string label, Action invoked, bool disabled = false, double width = 112) =>
        w.Button(id, label, WidgetSink.Invoked(invoked), disabled, width);

    public static UiNode Checkbox(this WidgetBuilder w, NodeId id, string label, Bound<bool> value,
        bool disabled = false, bool readOnly = false, bool indeterminate = false, double width = 240) =>
        w.Checkbox(id, label, value.Value, WidgetSink.Checked(value), disabled, readOnly, indeterminate, width);

    public static UiNode Switch(this WidgetBuilder w, NodeId id, string label, Bound<bool> value,
        bool disabled = false, bool readOnly = false, double width = 240) =>
        w.Switch(id, label, value.Value, WidgetSink.Checked(value), disabled, readOnly, width);

    public static UiNode Toggle(this WidgetBuilder w, NodeId id, string label, Bound<bool> pressed, bool disabled = false, double width = 112) =>
        w.Toggle(id, label, pressed.Value, WidgetSink.Checked(pressed), disabled, width);

    public static UiNode Input(this WidgetBuilder w, NodeId id, Bound<string> value, string label, string placeholder = "",
        bool disabled = false, bool readOnly = false, int maximumLength = 4096, double width = 240) =>
        w.Input(id, value.Value, label, WidgetSink.Text(value), placeholder, disabled, readOnly, maximumLength, width);

    public static UiNode NumberField(this WidgetBuilder w, NodeId id, Bound<decimal> value, string label,
        decimal minimum = -1_000_000_000, decimal maximum = 1_000_000_000, decimal step = 1,
        bool disabled = false, bool readOnly = false, double width = 160) =>
        w.NumberField(id, value.Value, label, minimum, maximum, step, WidgetSink.Number(value), disabled, readOnly, width);

    /// <summary>Use <see cref="Bind.Edit{T}"/> to keep drag previews out of the application's undo history.</summary>
    public static UiNode Slider(this WidgetBuilder w, NodeId id, Bound<decimal> value, string label,
        decimal minimum = 0, decimal maximum = 100, decimal step = 1, bool disabled = false,
        WidgetOrientation orientation = WidgetOrientation.Horizontal, WidgetDirection direction = WidgetDirection.LeftToRight,
        double width = 240, double? height = null) =>
        w.Slider(id, value.Value, label, minimum, maximum, step, WidgetSink.Number(value), disabled, orientation, direction, width, height);

    public static UiNode RadioGroup(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options, Bound<string?> value,
        WidgetOrientation orientation = WidgetOrientation.Vertical, bool disabled = false,
        WidgetDirection direction = WidgetDirection.LeftToRight, double width = 260) =>
        w.RadioGroup(id, options, value.Value, WidgetSink.Single(value), orientation, disabled, direction, width);

    public static UiNode CheckboxGroup(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options,
        Bound<ImmutableArray<string>> values, bool disabled = false, double width = 260) =>
        w.CheckboxGroup(id, options, values.Value, WidgetSink.Multiple(values), disabled, width);

    public static UiNode ToggleGroup(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options,
        Bound<ImmutableArray<string>> values, bool multiple = false, WidgetOrientation orientation = WidgetOrientation.Horizontal,
        WidgetDirection direction = WidgetDirection.LeftToRight, bool disabled = false, double width = 320) =>
        w.ToggleGroup(id, options, values.Value, WidgetSink.Multiple(values), multiple, orientation, direction, disabled, width);

    /// <summary>The handler receives the invoked item's key.</summary>
    public static UiNode Toolbar(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options, Action<string> invoked,
        WidgetOrientation orientation = WidgetOrientation.Horizontal, double width = 360) =>
        w.Toolbar(id, options, WidgetSink.Item(invoked), orientation, width);

    public static UiNode Tabs(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetPanel> panels, Bound<string?> selected,
        WidgetActivation activation = WidgetActivation.Automatic, WidgetOrientation orientation = WidgetOrientation.Horizontal,
        WidgetDirection direction = WidgetDirection.LeftToRight, double width = 400, double contentHeight = 160) =>
        w.Tabs(id, panels, selected.Value, WidgetSink.Single(selected), activation, orientation, direction, width, contentHeight);

    public static UiNode Collapsible(this WidgetBuilder w, NodeId id, string label, Bound<bool> open, UiNode content,
        bool disabled = false, double width = 300, double contentHeight = 100) =>
        w.Collapsible(id, label, open.Value, content, WidgetSink.Open(open), disabled, width, contentHeight);

    public static UiNode Accordion(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetPanel> panels,
        Bound<ImmutableArray<string>> expanded, bool multiple = false, double width = 320, double panelHeight = 100) =>
        w.Accordion(id, panels, expanded.Value, WidgetSink.Multiple(expanded), multiple, width, panelHeight);

    public static UiNode Select(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options, Bound<string?> value,
        Bound<bool> open, bool disabled = false, string placeholder = "Select...", double width = 240) =>
        w.Select(id, options, value.Value, open.Value, WidgetSink.Single(value), WidgetSink.Open(open), disabled, placeholder, width);

    /// <summary>The handler receives the chosen item's key; the menu closes itself through <paramref name="open"/>.</summary>
    public static UiNode Menu(this WidgetBuilder w, NodeId id, string label, IReadOnlyList<WidgetOption> items, Bound<bool> open,
        Action<string> invoked, double width = 220) =>
        w.Menu(id, label, items, open.Value, WidgetSink.Item(invoked), WidgetSink.Open(open), width);

    public static UiNode Popover(this WidgetBuilder w, NodeId id, string label, UiNode content, Bound<bool> open,
        double width = 280, double height = 160, WidgetPopupSide side = WidgetPopupSide.Bottom) =>
        w.Popover(id, label, content, open.Value, WidgetSink.Open(open), width, height, side);

    public static UiNode Dialog(this WidgetBuilder w, NodeId id, string triggerLabel, string title, UiNode content, Bound<bool> open,
        bool alert = false, double width = 420, double height = 260) =>
        w.Dialog(id, triggerLabel, title, content, open.Value, WidgetSink.Open(open), alert, width, height);

    /// <summary>Query text and selected key are separate bindings; the host decides whether selecting updates the query.</summary>
    public static UiNode Combobox(this WidgetBuilder w, NodeId id, IReadOnlyList<WidgetOption> options, Bound<string?> selected,
        Bound<string> query, Bound<bool> open, string label, bool disabled = false, double width = 240, int maximumLength = 4096,
        WidgetOptionFilter? filter = null, bool acceptFirst = false, int maximumRows = 0, string placeholder = "") =>
        w.Combobox(id, options, selected.Value, query.Value, open.Value, label, WidgetSink.Combobox(selected, query),
            WidgetSink.Open(open), disabled, width, maximumLength, filter, acceptFirst, maximumRows, placeholder);
}
