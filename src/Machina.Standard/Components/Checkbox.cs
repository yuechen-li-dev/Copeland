using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Checkbox
{
    public static UiNode Create(NodeId? id = null, string? label = null, bool isChecked = false, bool disabled = false, UiAction? changed = null, StandardTheme? theme = null, StandardCheckboxStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var effectiveStyle = style ?? effectiveTheme.Checkbox.Default;
        var boxColor = disabled ? effectiveStyle.DisabledBackground : effectiveStyle.BoxBackground;
        var borderColor = disabled ? effectiveStyle.DisabledBorderColor : effectiveStyle.BoxBorderColor;
        var markColor = disabled ? effectiveStyle.DisabledMarkColor : effectiveStyle.MarkColor;

        var markInset = (effectiveStyle.BoxSize - effectiveStyle.MarkSize) / 2;
        var mark = UI.Anchor(UI.Rect(id: CreateChildId(id, "mark"), width: effectiveStyle.MarkSize, height: effectiveStyle.MarkSize, style: new UiStyle(isChecked ? markColor : ColorToken.Hex(0x00000000), null, 0)), id: CreateChildId(id, "mark-slot"), left: markInset, top: markInset, width: effectiveStyle.MarkSize, height: effectiveStyle.MarkSize);
        var box = UI.Rect(mark, id: CreateChildId(id, "box"), width: effectiveStyle.BoxSize, height: effectiveStyle.BoxSize, style: new UiStyle(boxColor, null, 0, borderColor, effectiveStyle.BoxBorderThickness));

        UiNode root;
        if (string.IsNullOrEmpty(label))
        {
            root = UI.Rect(box, id: id, width: effectiveStyle.BoxSize, height: effectiveStyle.BoxSize, style: new UiStyle(null, null, 0));
        }
        else
        {
            var labelStyle = effectiveStyle.LabelTextStyle with { Color = disabled ? effectiveStyle.DisabledLabelColor : effectiveStyle.LabelColor, AlignY = TextAlignY.Center };
            root = UI.Row(id: id, gap: effectiveStyle.Gap, children: [box, UI.Text(label, id: CreateChildId(id, "label"), size: labelStyle.Size, alignX: labelStyle.AlignX, alignY: labelStyle.AlignY, style: labelStyle)]);
        }

        return root with
        {
            Semantics = new UiSemantics(UiRole.Checkbox, label, Disabled: disabled, Focusable: !disabled),
            DeclaredAction = disabled ? null : changed,
        };
    }

    private static NodeId? CreateChildId(NodeId? id, string suffix)
    {
        if (id is not { } value)
        {
            return null;
        }

        return new NodeId($"{value.Value}.{suffix}");
    }
}
