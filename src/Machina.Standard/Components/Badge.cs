using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Badge
{
    public static UiNode Create(
        string text,
        NodeId? id = null,
        StandardTheme? theme = null,
        BadgeVariant variant = BadgeVariant.Secondary)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var colors = ResolveVariantColors(variant, effectiveTheme);
        var textNode = UI.Text(
            text,
            color: colors.Foreground,
            size: TextSize.Sm,
            alignX: TextAlignX.Center,
            alignY: TextAlignY.Center);
        var style = new UiStyle(
            Background: colors.Background,
            Foreground: colors.Foreground,
            Padding: effectiveTheme.Spacing.Xs);

        return UI.Rect(
            child: textNode,
            id: id,
            color: null,
            padding: null,
            style: style);
    }

    private static BadgeColors ResolveVariantColors(
        BadgeVariant variant,
        StandardTheme theme)
    {
        var colors = theme.Colors;

        return variant switch
        {
            BadgeVariant.Default => new BadgeColors(
                Background: colors.Primary,
                Foreground: colors.PrimaryForeground),
            BadgeVariant.Secondary => new BadgeColors(
                Background: colors.Secondary,
                Foreground: colors.SecondaryForeground),
            BadgeVariant.Destructive => new BadgeColors(
                Background: colors.Destructive,
                Foreground: colors.DestructiveForeground),
            BadgeVariant.Outline => new BadgeColors(
                Background: colors.Background,
                Foreground: colors.Foreground),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
    }

    private sealed record BadgeColors(
        ColorToken Background,
        ColorToken Foreground);
}
