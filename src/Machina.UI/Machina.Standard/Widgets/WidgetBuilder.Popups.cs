using System.Collections.Immutable;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetBuilder
{
    internal HashSet<NodeId> TooltipIds { get; } = [];
    public UiNode Select(NodeId id, IReadOnlyList<WidgetOption> options, string? value, bool open,
        UiAction? changed = null, UiAction? openChanged = null, bool disabled = false,
        string placeholder = "Select...", double width = 240)
    {
        ValidateOptions(options);
        if (open && !disabled)
        {
            AddPopup(new(id, id.Child("trigger"), Placeholder, WidgetPopupKind.Select, "Options", width,
                ListPanelHeight(options.Count), WidgetPopupSide.Bottom, false, true, true, openChanged)
            {
                ListHeight = ListContentHeight(options.Count),
                List = rowWidth => ListItems(id, options, value, changed, closeOnSelect: true, rowWidth, false)
            });
        }
        return PopupTrigger(id, (options.FirstOrDefault(option => option.Key == value)?.Label ?? placeholder) + " v",
            open, openChanged, disabled, width, UiRole.ComboBox);
    }

    public UiNode Menu(NodeId id, string label, IReadOnlyList<WidgetOption> items, bool open,
        UiAction? invoked = null, UiAction? openChanged = null, double width = 220)
    {
        ValidateOptions(items);
        if (open) AddPopup(new(id, id.Child("trigger"), Placeholder, WidgetPopupKind.Menu, label, width, ListPanelHeight(items.Count),
            WidgetPopupSide.Bottom, false, true, true, openChanged)
        {
            ListHeight = ListContentHeight(items.Count),
            List = rowWidth => ListItems(id, items, null, invoked, true, rowWidth, true)
        });
        return PopupTrigger(id, label, open, openChanged, false, width, UiRole.Button);
    }

    /// <summary>
    /// Declares a menu at a viewport-local point while the host supplies an anchor.
    /// A secondary-button press or a keyboard invocation may supply the point.
    /// Pass elapsed time while animating, or omit it for an immediate presentation.
    /// </summary>
    public void ContextMenu(NodeId id, string label, PointerPoint? anchor, IReadOnlyList<WidgetOption> items,
        UiAction? invoked = null, UiAction? openChanged = null, double width = 220, TimeSpan? elapsed = null)
    {
        if (anchor is not { } point) return;
        ValidateOptions(items);
        double progress = elapsed is { } time ? Math.Clamp(time / ContextMenuEntranceDuration, 0, 1) : 1;
        double remaining = 1 - progress;
        AddPopup(new(id, id, Placeholder, WidgetPopupKind.Menu, label, width, ListPanelHeight(items.Count),
            WidgetPopupSide.Bottom, false, true, true, openChanged)
        {
            PointAnchor = new Rect(point.X, point.Y, 0, 0),
            EntranceOffset = 12 * remaining * remaining * remaining,
            ListHeight = ListContentHeight(items.Count),
            List = rowWidth => ListItems(id, items, null, invoked, true, rowWidth, true, TextAlignX.Left)
        });
    }

    public static TimeSpan ContextMenuEntranceDuration { get; } = TimeSpan.FromMilliseconds(160);

    public UiNode Popover(NodeId id, string label, UiNode content, bool open, UiAction? openChanged = null,
        double width = 280, double height = 160, WidgetPopupSide side = WidgetPopupSide.Bottom)
    {
        if (open) AddPopup(new(id, id.Child("trigger"), content, WidgetPopupKind.Popover, label,
            width, height, side, false, true, true, openChanged));
        return PopupTrigger(id, label, open, openChanged, false, 180, UiRole.Button);
    }

    public UiNode Dialog(NodeId id, string triggerLabel, string title, UiNode content, bool open,
        UiAction? openChanged = null, bool alert = false, double width = 420, double height = 260)
    {
        if (open) AddPopup(new(id, id.Child("trigger"), content,
            alert ? WidgetPopupKind.AlertDialog : WidgetPopupKind.Dialog, title, width, height,
            WidgetPopupSide.Bottom, true, !alert, true, openChanged));
        return PopupTrigger(id, triggerLabel, open, openChanged, false, 180, UiRole.Button);
    }

    /// <summary>Focus/hover disclosure. Hosts may provide controlled timing by passing open explicitly.</summary>
    public UiNode Tooltip(NodeId id, string label, string text, bool? open = null, double width = 260)
    {
        var trigger = id.Child("trigger");
        TooltipIds.Add(id);
        bool visible = open ?? Session.TooltipVisible(id, trigger);
        if (visible) AddPopup(new(id, trigger, UI.Text(text, size: Style.TextSize, color: Style.Foreground),
            WidgetPopupKind.Tooltip, text, width, Style.Height + 2 * Style.Padding,
            WidgetPopupSide.Top, false, false, true, null));
        var button = PopupTrigger(id, label, visible, null, false, 180, UiRole.Button);
        return button with { Semantics = button.Semantics! with { DescribedBy = id.Child("popup").Value } };
    }

    internal UiNode Compose(UiNode content, IReadOnlyList<UiNode> overlays, int width, int height, bool transparent)
    {
        return UI.Surface(id: new NodeId("machina.widgets.surface"), color: transparent ? null : Style.Background,
            children: [StandardUI.Place(content, 0, 0, width, height), .. overlays]);
    }

    internal UiNode CreateOverlay(NodeId id, Rect anchor, Rect viewport)
    {
        var popup = Popups.Single(item => item.Id == id);
        double width = Math.Min(popup.Width, viewport.Width), height = Math.Min(popup.Height, viewport.Height);
        Rect bounds = popup.Modal
            ? new Rect(viewport.X + (viewport.Width - width) / 2, viewport.Y + (viewport.Height - height) / 2, width, height)
            : WidgetPopupPlacement.Place(anchor, viewport, width, height, popup.Side);
        if (popup.PointAnchor is not null && popup.EntranceOffset > 0)
        {
            double direction = bounds.Y >= anchor.Y ? 1 : -1;
            bounds = new Rect(bounds.X,
                Math.Clamp(bounds.Y + direction * popup.EntranceOffset, viewport.Y, viewport.Y + viewport.Height - height),
                bounds.Width, bounds.Height);
        }
        var layers = new List<UiNode>();
        bool tooltip = popup.Kind == WidgetPopupKind.Tooltip;
        if (!tooltip)
        {
            NodeId backdropId = popup.Id.Child("backdrop");
            var backdrop = UI.Rect(id: backdropId, width: viewport.Width, height: viewport.Height,
                color: popup.Modal ? ColorToken.Hex(0x00000088) : ColorToken.Hex(0x00000000));
            layers.Add(Bind(new(backdropId, popup.Id, WidgetKind.Dismiss)
            { Popup = popup.Id, ReadOnly = !popup.DismissOutside }, backdrop, new(UiRole.Container)));
        }
        var children = new List<UiNode>();
        double top = Style.Padding;
        if (popup.Modal)
        {
            children.Add(StandardUI.Place(UI.Text(popup.Title, id: popup.Id.Child("title"),
                color: Style.Foreground, size: Style.TextSize), Style.Padding, top,
                Math.Max(1, bounds.Width - 100 - 2 * Style.Padding), Style.Height));
            var close = new WidgetBinding(popup.Id.Child("close"), popup.Id, WidgetKind.Dismiss) { Popup = popup.Id, Text = "Close" };
            children.Add(StandardUI.Place(ButtonNode(close, "Close", 80), Math.Max(0, bounds.Width - 80 - Style.Padding), top, 80, Style.Height));
            top += Style.Height + Style.Gap;
        }
        double contentWidth = Math.Max(1, bounds.Width - 2 * Style.Padding);
        double contentHeight = Math.Max(1, bounds.Height - top - Style.Padding);
        UiNode content = popup.Content;
        if (popup.Kind is WidgetPopupKind.Select or WidgetPopupKind.Menu or WidgetPopupKind.Combobox)
        {
            double listHeight = Math.Max(1, popup.ListHeight ?? popup.Height - 2 * Style.Padding);
            if (popup.List is { } list)
                content = list(Math.Max(1, contentWidth - (listHeight > contentHeight ? ScrollbarGutter : 0)));
            content = ScrollArea(popup.Id.Child("scroll"), content, listHeight, contentWidth, contentHeight, popup.Title);
        }
        children.Add(StandardUI.Place(content, Style.Padding, top, contentWidth, contentHeight));
        var panel = Panel(popup.Panel, bounds.Width, bounds.Height, children,
            new UiStyle(Style.Background, BorderColor: Style.Border, BorderThickness: 1, ClipToBounds: true,
                Shape: popup.PointAnchor is null ? null : UiShapeKind.RoundedRect, CornerRadius: popup.PointAnchor is null ? 0 : 6));
        if (!tooltip)
            panel = Bind(new(popup.Panel, popup.Id, WidgetKind.Dismiss) { Popup = popup.Id, ReadOnly = true },
                panel, new(popup.Kind == WidgetPopupKind.AlertDialog ? UiRole.AlertDialog : popup.Modal ? UiRole.Dialog
                    : popup.Kind == WidgetPopupKind.Menu ? UiRole.Menu : UiRole.Group, popup.Title)
                { Modal = popup.Modal, LabelledBy = popup.Modal ? popup.Id.Child("title").Value : null });
        else panel = panel with { Semantics = new(UiRole.Tooltip, popup.Title) };
        layers.Add(StandardUI.Place(panel, bounds.X, bounds.Y, bounds.Width, bounds.Height));
        return Panel(popup.Id.Child("overlay"), viewport.Width, viewport.Height, layers);
    }

    private UiNode ListItems(NodeId id, IReadOnlyList<WidgetOption> options, string? selected, UiAction? changed,
        bool closeOnSelect, double width, bool menu, TextAlignX alignment = TextAlignX.Center)
    {
        var children = new List<UiNode>();
        foreach (var option in options)
        {
            var binding = new WidgetBinding(id.Child("option").Child(option.Key), id, WidgetKind.Choice)
            {
                Key = option.Key, Text = option.Label, Selected = selected is null ? [] : [selected],
                Group = id.Child("options"), Orientation = WidgetOrientation.Vertical, Disabled = option.Disabled,
                Changed = changed, CloseOnSelect = closeOnSelect, Popup = id
            };
            children.Add(ButtonNode(binding, option.Label, Math.Max(1, width),
                new(menu ? UiRole.MenuItem : UiRole.Option, option.Label, option.Disabled, !option.Disabled)
                { Selected = menu ? null : option.Key == selected }, alignment));
        }
        return UI.Column(children, id.Child("options"), gap: Style.Gap)
            with { Semantics = new(menu ? UiRole.Menu : UiRole.ListBox) };
    }

    private static readonly UiNode Placeholder = UI.Layer(children: []);
    private double ListContentHeight(int count) => Math.Max(Style.Height, count * Style.Height + Math.Max(0, count - 1) * Style.Gap);
    private double ListPanelHeight(int count) => ListContentHeight(count) + 2 * Style.Padding;

    private UiNode PopupTrigger(NodeId id, string label, bool open, UiAction? changed, bool disabled, double width, UiRole role)
    {
        return ButtonNode(new(id.Child("trigger"), id, WidgetKind.PopupTrigger)
        { Open = open, Changed = changed, Disabled = disabled, Text = label }, label, width,
            new(role, label, disabled, !disabled) { Expanded = open, Controls = id.Child("popup").Value });
    }

    private void AddPopup(WidgetPopup popup)
    {
        ValidateWidth(popup.Width);
        ValidateWidth(popup.Height);
        if (Popups.Count >= 16) throw new ArgumentException("At most sixteen active popups are supported per view.");
        if (Popups.Any(existing => existing.Id == popup.Id)) throw new ArgumentException($"Duplicate popup '{popup.Id}'.");
        Popups.Add(popup);
    }
}
