using System.Collections.Immutable;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetBuilder
{
    public UiNode RadioGroup(NodeId id, IReadOnlyList<WidgetOption> options, string? value,
        UiAction? changed = null, WidgetOrientation orientation = WidgetOrientation.Vertical,
        bool disabled = false, WidgetDirection direction = WidgetDirection.LeftToRight, double width = 260) =>
        ChoiceGroup(id, options, value is null ? [] : [value], changed, orientation, direction,
            disabled, false, false, true, width, UiRole.RadioGroup, UiRole.Radio);

    public UiNode CheckboxGroup(NodeId id, IReadOnlyList<WidgetOption> options, ImmutableArray<string> values,
        UiAction? changed = null, bool disabled = false, double width = 260) =>
        ChoiceGroup(id, options, values, changed, WidgetOrientation.Vertical, WidgetDirection.LeftToRight,
            disabled, true, true, false, width, UiRole.Group, UiRole.Checkbox);

    public UiNode ToggleGroup(NodeId id, IReadOnlyList<WidgetOption> options, ImmutableArray<string> values,
        UiAction? changed = null, bool multiple = false, WidgetOrientation orientation = WidgetOrientation.Horizontal,
        WidgetDirection direction = WidgetDirection.LeftToRight, bool disabled = false, double width = 320) =>
        ChoiceGroup(id, options, values, changed, orientation, direction, disabled, multiple,
            true, false, width, UiRole.Group, UiRole.Button);

    public UiNode Toolbar(NodeId id, IReadOnlyList<WidgetOption> options, UiAction? invoked = null,
        WidgetOrientation orientation = WidgetOrientation.Horizontal, double width = 360) =>
        ChoiceGroup(id, options, [], invoked, orientation, WidgetDirection.LeftToRight,
            false, false, false, false, width, UiRole.Toolbar, UiRole.Button);

    public UiNode Tabs(NodeId id, IReadOnlyList<WidgetPanel> panels, string? selected,
        UiAction? changed = null, WidgetActivation activation = WidgetActivation.Automatic,
        WidgetOrientation orientation = WidgetOrientation.Horizontal,
        WidgetDirection direction = WidgetDirection.LeftToRight, double width = 400, double contentHeight = 160)
    {
        var options = panels.Select(panel => new WidgetOption(panel.Key, panel.Label, panel.Disabled)).ToArray();
        ValidateOptions(options);
        var list = ChoiceGroup(id.Child("tabs"), options, selected is null ? [] : [selected], changed,
            orientation, direction, false, false, false, activation == WidgetActivation.Automatic,
            width, UiRole.TabList, UiRole.Tab, owner: id);
        UiNode? content = panels.FirstOrDefault(panel => panel.Key == selected)?.Content;
        double listHeight = orientation == WidgetOrientation.Horizontal ? Style.Height : Math.Max(Style.Height, panels.Count * (Style.Height + Style.Gap));
        var children = new List<UiNode> { StandardUI.Place(list, 0, 0, width, listHeight) };
        if (content is not null)
        {
            var panelId = id.Child("panel");
            children.Add(StandardUI.Place(UI.Rect(content, panelId, width, contentHeight,
                style: new(Style.Background, Padding: Style.Padding, BorderColor: Style.Border, BorderThickness: 1))
                with { Semantics = new(UiRole.TabPanel) { LabelledBy = id.Child("tabs").Child(selected!).Value } },
                0, listHeight + Style.Gap, width, contentHeight));
        }
        return Panel(id, width, listHeight + Style.Gap + (content is null ? 0 : contentHeight), children);
    }

    public UiNode Collapsible(NodeId id, string label, bool open, UiNode content, UiAction? changed = null,
        bool disabled = false, double width = 300, double contentHeight = 100)
    {
        var binding = new WidgetBinding(id.Child("trigger"), id, WidgetKind.Disclosure)
        { Open = open, Disabled = disabled, Changed = changed, Text = label };
        var trigger = ButtonNode(binding, label, width,
            new(UiRole.Button, label, disabled, !disabled) { Expanded = open, Controls = id.Child("panel").Value });
        var children = new List<UiNode> { StandardUI.Place(trigger, 0, 0, width, Style.Height) };
        if (open) children.Add(StandardUI.Place(UI.Rect(content, id.Child("panel"), width, contentHeight,
            style: new(Style.Background, ClipToBounds: true)), 0, Style.Height + Style.Gap, width, contentHeight));
        return Panel(id, width, Style.Height + (open ? Style.Gap + contentHeight : 0), children);
    }

    public UiNode Accordion(NodeId id, IReadOnlyList<WidgetPanel> panels, ImmutableArray<string> expanded,
        UiAction? changed = null, bool multiple = false, double width = 320, double panelHeight = 100)
    {
        ValidateOptions(panels.Select(panel => new WidgetOption(panel.Key, panel.Label, panel.Disabled)).ToArray());
        var children = new List<UiNode>();
        double y = 0;
        foreach (var panel in panels)
        {
            bool open = expanded.Contains(panel.Key);
            var triggerId = id.Child(panel.Key).Child("trigger");
            var panelId = id.Child(panel.Key).Child("panel");
            var binding = new WidgetBinding(triggerId, id, WidgetKind.Choice)
            {
                Group = id, Orientation = WidgetOrientation.Vertical, Multiple = multiple, ToggleSelection = true,
                RovingTabStop = false,
                Key = panel.Key, Text = panel.Label, Selected = expanded, Changed = changed, Disabled = panel.Disabled
            };
            children.Add(StandardUI.Place(ButtonNode(binding, panel.Label, width,
                new(UiRole.Button, panel.Label, panel.Disabled, !panel.Disabled) { Expanded = open, Controls = panelId.Value }),
                0, y, width, Style.Height));
            y += Style.Height + Style.Gap;
            if (!open) continue;
            children.Add(StandardUI.Place(UI.Rect(panel.Content, panelId, width, panelHeight,
                style: new(Style.Background, ClipToBounds: true)), 0, y, width, panelHeight));
            y += panelHeight + Style.Gap;
        }
        return Panel(id, width, Math.Max(1, y), children);
    }

    private UiNode ChoiceGroup(NodeId id, IReadOnlyList<WidgetOption> options, ImmutableArray<string> selected,
        UiAction? changed, WidgetOrientation orientation, WidgetDirection direction, bool disabled,
        bool multiple, bool toggle, bool activateOnFocus, double width, UiRole groupRole, UiRole itemRole, NodeId? owner = null)
    {
        ValidateOptions(options);
        ValidateWidth(width);
        if (!multiple && selected.Length > 1) throw new ArgumentException("A single-selection group accepts at most one value.", nameof(selected));
        var children = new List<UiNode>();
        bool horizontal = orientation == WidgetOrientation.Horizontal;
        double itemWidth = horizontal ? Math.Max(1, (width - Style.Gap * Math.Max(0, options.Count - 1)) / Math.Max(1, options.Count)) : width;
        for (int index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var binding = new WidgetBinding(id.Child(option.Key), owner ?? id,
                groupRole == UiRole.Toolbar ? WidgetKind.Button : WidgetKind.Choice)
            {
                Key = option.Key, Text = option.Label, Selected = selected, Changed = changed,
                Disabled = disabled || option.Disabled, Group = itemRole == UiRole.Checkbox ? null : (NodeId?)id, Orientation = orientation,
                Direction = direction, Multiple = multiple, ToggleSelection = toggle, ActivateOnFocus = activateOnFocus
            };
            bool active = selected.Contains(option.Key);
            var item = ButtonNode(binding, option.Label, itemWidth,
                new(itemRole, option.Label, binding.Disabled, !binding.Disabled)
                {
                    Selected = active,
                    Checked = itemRole is UiRole.Radio or UiRole.Checkbox ? active : null,
                    Controls = itemRole == UiRole.Tab ? (owner ?? id).Child("panel").Value : null
                });
            int visualIndex = horizontal && direction == WidgetDirection.RightToLeft ? options.Count - index - 1 : index;
            children.Add(StandardUI.Place(item, horizontal ? visualIndex * (itemWidth + Style.Gap) : 0,
                horizontal ? 0 : index * (Style.Height + Style.Gap), itemWidth, Style.Height));
        }
        double height = horizontal ? Style.Height : Math.Max(1, options.Count * (Style.Height + Style.Gap) - Style.Gap);
        return Panel(id, width, height, children) with { Semantics = new(groupRole) };
    }
}
