using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Switch
{
    public static UiNode Create(NodeId? id = null, string? label = null, bool isOn = false, bool disabled = false, UiAction? changed = null, StandardTheme? theme = null, StandardSwitchStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var effectiveStyle = style ?? effectiveTheme.Switch.Default;

        var trackBackground = disabled ? effectiveStyle.DisabledTrackBackground : (isOn ? effectiveStyle.TrackOnBackground : effectiveStyle.TrackOffBackground);
        var trackBorder = disabled ? effectiveStyle.DisabledTrackBorderColor : effectiveStyle.TrackBorderColor;
        var thumbBackground = disabled ? effectiveStyle.DisabledThumbBackground : effectiveStyle.ThumbBackground;
        var thumbBorder = disabled ? effectiveStyle.DisabledThumbBorderColor : effectiveStyle.ThumbBorderColor;

        var maxThumbLeft = effectiveStyle.TrackWidth - effectiveStyle.ThumbInset - effectiveStyle.ThumbSize;
        var thumbLeft = isOn ? maxThumbLeft : effectiveStyle.ThumbInset;

        var thumb = UI.Anchor(
            UI.Rect(id: CreateChildId(id, "thumb"), width: effectiveStyle.ThumbSize, height: effectiveStyle.ThumbSize, style: new UiStyle(thumbBackground, null, 0, thumbBorder, effectiveStyle.ThumbBorderThickness)),
            id: CreateChildId(id, "thumb-slot"),
            left: thumbLeft,
            top: effectiveStyle.ThumbInset,
            width: effectiveStyle.ThumbSize,
            height: effectiveStyle.ThumbSize);

        var shell = UI.Rect(thumb, id: CreateChildId(id, "track"), width: effectiveStyle.TrackWidth, height: effectiveStyle.TrackHeight, style: new UiStyle(trackBackground, null, 0, trackBorder, effectiveStyle.TrackBorderThickness));

        UiNode root;
        if (string.IsNullOrEmpty(label))
        {
            root = UI.Rect(shell, id: id, width: effectiveStyle.TrackWidth, height: effectiveStyle.TrackHeight, style: new UiStyle(null, null, 0));
        }
        else
        {
            var labelStyle = effectiveStyle.LabelTextStyle with { Color = disabled ? effectiveStyle.DisabledLabelColor : effectiveStyle.LabelColor, AlignY = TextAlignY.Center };
            root = UI.Row(id: id, gap: effectiveStyle.Gap, children: [shell, UI.Text(label, id: CreateChildId(id, "label"), size: labelStyle.Size, alignX: labelStyle.AlignX, alignY: labelStyle.AlignY, style: labelStyle)]);
        }

        return root with
        {
            Semantics = new UiSemantics(UiRole.Switch, label, Disabled: disabled, Focusable: !disabled),
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
