using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetBuilder
{
    /// <summary>
    /// Editable, single-selection suggestions. Query and selected key are separate
    /// controlled values. A text proposal updates query; a selection proposal
    /// identifies an option. The host chooses how selection updates query text.
    /// Filtering is deterministic ordinal case-insensitive substring matching.
    /// </summary>
    public UiNode Combobox(NodeId id, IReadOnlyList<WidgetOption> options, string? selected,
        string query, bool open, string label, UiAction? changed = null, UiAction? openChanged = null,
        bool disabled = false, double width = 240, int maximumLength = 4096)
    {
        ValidateOptions(options);
        ValidateWidth(width);
        if (width < Style.Height + 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (maximumLength < 1 || maximumLength > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        NodeId inputId = id.Child("input");
        var input = TextInput(new(inputId, id, WidgetKind.Input)
        {
            Text = query, Changed = changed, Disabled = disabled, MaximumLength = maximumLength,
            Popup = id, Open = open, OpenChanged = openChanged
        }, label, "", width - Style.Height);
        input = input with
        {
            Semantics = input.Semantics! with
            {
                Role = UiRole.ComboBox, Expanded = open, Controls = id.Child("popup").Value
            }
        };
        var trigger = PopupTrigger(id, "v", open, openChanged, disabled, Style.Height, UiRole.Button);
        if (open && !disabled)
        {
            var filtered = options.Where(option => option.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            UiNode empty = UI.Text("No matches", size: Style.TextSize, color: Style.Muted);
            AddPopup(new(id, inputId, filtered.Length == 0 ? empty : Placeholder, WidgetPopupKind.Combobox, label, width,
                ListPanelHeight(filtered.Length), WidgetPopupSide.Bottom, false, true, true, openChanged)
            {
                ListHeight = ListContentHeight(filtered.Length),
                List = filtered.Length == 0 ? null : rowWidth => ListItems(id, filtered, selected, changed, true, rowWidth, false)
            });
        }
        return Panel(id, width, Style.Height,
        [
            StandardUI.Place(input, 0, 0, width - Style.Height, Style.Height),
            StandardUI.Place(trigger, width - Style.Height, 0, Style.Height, Style.Height)
        ]);
    }
}
