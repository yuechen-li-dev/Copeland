using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Badge
{
    public static UiNode Create(
        string text,
        NodeId? id = null,
        StandardTheme? theme = null,
        BadgeVariant variant = BadgeVariant.Secondary,
        StandardBadgeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var effectiveStyle = style ?? effectiveTheme.Badge.ForVariant(variant);
        var labelTextStyle = ResolveLabelTextStyle(effectiveStyle);
        var measuredText = DeterministicTextMeasurer.Instance.MeasureText(text, labelTextStyle);
        var width = Math.Max(effectiveStyle.MinWidth, measuredText.Width + effectiveStyle.HorizontalAllowance);
        var height = effectiveStyle.Height;
        var labelRegion = CreateLabelRegion(id, effectiveStyle, labelTextStyle, text);
        var shellStyle = new UiStyle(
            Background: effectiveStyle.Background,
            Foreground: effectiveStyle.Foreground,
            Padding: 0,
            BorderColor: effectiveStyle.BorderColor,
            BorderThickness: effectiveStyle.BorderThickness,
            Shape: effectiveStyle.Shape);

        return UI.Rect(
            child: labelRegion,
            id: id,
            width: width,
            height: height,
            color: null,
            padding: null,
            style: shellStyle);
    }

    private static UiNode CreateLabelRegion(
        NodeId? id,
        StandardBadgeStyle style,
        TextStyle labelTextStyle,
        string text)
    {
        var leftInset = ResolveLeadingInset(style.TextOffsetX);
        var rightInset = ResolveTrailingInset(style.TextOffsetX);
        var topInset = ResolveLeadingInset(style.TextOffsetY);
        var bottomInset = ResolveTrailingInset(style.TextOffsetY);

        return UI.Anchor(
            child: UI.Text(
                text,
                id: CreateChildId(id, "label"),
                color: labelTextStyle.Color,
                size: labelTextStyle.Size,
                alignX: labelTextStyle.AlignX,
                alignY: labelTextStyle.AlignY,
                style: labelTextStyle),
            id: CreateChildId(id, "label-region"),
            left: leftInset,
            right: rightInset,
            top: topInset,
            bottom: bottomInset);
    }

    private static TextStyle ResolveLabelTextStyle(StandardBadgeStyle style)
    {
        var labelColor = style.TextStyle.Color ?? style.Foreground;
        return style.TextStyle with
        {
            Color = labelColor,
            AlignX = style.TextAlignX,
            AlignY = style.TextAlignY,
        };
    }

    private static UiLength ResolveLeadingInset(double offset)
    {
        return Math.Max(0, offset);
    }

    private static UiLength ResolveTrailingInset(double offset)
    {
        return Math.Max(0, -offset);
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
