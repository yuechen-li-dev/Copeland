using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetBuilder
{
    /// <summary>
    /// Editable, single-selection suggestions. Query and selected key are separate
    /// controlled values. A text proposal updates query; a selection proposal
    /// identifies an option. The host chooses how selection updates query text.
    /// </summary>
    /// <param name="filter">Chooses and orders suggestions; defaults to <see cref="WidgetOptionFilters.Substring"/>.
    /// <see cref="WidgetOptionFilters.Ranked"/> suits command palettes.</param>
    /// <param name="acceptFirst">Enter in the input selects the first enabled suggestion, which is drawn highlighted
    /// so the outcome of Enter is visible before it is pressed (inline-autocomplete "list" behavior).</param>
    /// <param name="maximumRows">Caps the list's visible rows; the rest scroll. Zero leaves it to the viewport.</param>
    /// <param name="placeholder">Shown in the empty input.</param>
    /// <param name="showTrigger">Draws the open/close button beside the input. A command palette, always open while
    /// shown, leaves it out and gives the input the full width.</param>
    public UiNode Combobox(NodeId id, IReadOnlyList<WidgetOption> options, string? selected,
        string query, bool open, string label, UiAction? changed = null, UiAction? openChanged = null,
        bool disabled = false, double width = 240, int maximumLength = 4096,
        WidgetOptionFilter? filter = null, bool acceptFirst = false, int maximumRows = 0, string placeholder = "",
        bool showTrigger = true)
    {
        if (maximumRows < 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
        ValidateOptions(options);
        ValidateWidth(width);
        if (width < Style.Height + 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (maximumLength < 1 || maximumLength > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        NodeId inputId = id.Child("input");
        double inputWidth = showTrigger ? width - Style.Height : width;
        var input = TextInput(new(inputId, id, WidgetKind.Input)
        {
            Text = query, Changed = changed, Disabled = disabled, MaximumLength = maximumLength,
            Popup = id, Open = open, OpenChanged = openChanged, AcceptFirst = acceptFirst
        }, label, placeholder, inputWidth);
        input = input with
        {
            Semantics = input.Semantics! with
            {
                Role = UiRole.ComboBox, Expanded = open, Controls = id.Child("popup").Value
            }
        };
        if (open && !disabled)
        {
            var filtered = (filter ?? WidgetOptionFilters.Substring)(options, query).ToArray();
            if (filtered.Any(option => !options.Contains(option)))
                throw new ArgumentException("A combobox filter may only choose and order the supplied options.", nameof(filter));
            string? highlight = selected ?? (acceptFirst ? filtered.FirstOrDefault(option => !option.Disabled)?.Key : null);
            int rows = maximumRows > 0 ? Math.Min(filtered.Length, maximumRows) : filtered.Length;
            bool details = filtered.Any(option => option.Detail is not null);
            UiNode empty = UI.Text("No matches", size: Style.TextSize, color: Style.Muted);
            AddPopup(new(id, inputId, filtered.Length == 0 ? empty : Placeholder, WidgetPopupKind.Combobox, label, width,
                ListPanelHeight(rows), WidgetPopupSide.Bottom, false, true, true, openChanged)
            {
                ListHeight = ListContentHeight(filtered.Length),
                List = filtered.Length == 0 ? null
                    : rowWidth => ListItems(id, filtered, highlight, changed, true, rowWidth, false, details ? TextAlignX.Left : TextAlignX.Center)
            });
        }
        return Panel(id, width, Style.Height, showTrigger
            // The trigger is only declared when drawn, so a hidden one never becomes a tab stop.
            ? [StandardUI.Place(input, 0, 0, inputWidth, Style.Height),
               StandardUI.Place(PopupTrigger(id, "v", open, openChanged, disabled, Style.Height, UiRole.Button), inputWidth, 0, Style.Height, Style.Height)]
            : [StandardUI.Place(input, 0, 0, inputWidth, Style.Height)]);
    }
}
