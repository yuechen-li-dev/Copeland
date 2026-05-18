using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class StandardButton
{
    public static UiNode Create(
        string text,
        NodeId? id = null,
        UiAction? action = null,
        ButtonVariant variant = ButtonVariant.Default,
        ButtonSize size = ButtonSize.Medium,
        bool disabled = false,
        StandardTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var style = CreateStyle(variant, size, effectiveTheme);

        return UI.Button(
            text,
            id,
            action,
            disabled,
            color: null,
            style);
    }

    public static UiStyle CreateStyle(
        ButtonVariant variant,
        ButtonSize size,
        StandardTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var colors = ResolveVariantColors(variant, theme);
        var padding = ResolvePadding(size, theme);

        return new UiStyle(
            Background: colors.Background,
            Foreground: colors.Foreground,
            Padding: padding);
    }

    private static ButtonColors ResolveVariantColors(
        ButtonVariant variant,
        StandardTheme theme)
    {
        var colors = theme.Colors;

        return variant switch
        {
            ButtonVariant.Default => new ButtonColors(
                Background: colors.Primary,
                Foreground: colors.PrimaryForeground),
            ButtonVariant.Destructive => new ButtonColors(
                Background: colors.Destructive,
                Foreground: colors.DestructiveForeground),
            ButtonVariant.Outline => new ButtonColors(
                Background: colors.Background,
                Foreground: colors.Foreground),
            ButtonVariant.Secondary => new ButtonColors(
                Background: colors.Secondary,
                Foreground: colors.SecondaryForeground),
            ButtonVariant.Ghost => new ButtonColors(
                Background: null,
                Foreground: colors.Foreground),
            ButtonVariant.Link => new ButtonColors(
                Background: null,
                Foreground: colors.Primary),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
    }

    private static double ResolvePadding(
        ButtonSize size,
        StandardTheme theme)
    {
        return size switch
        {
            ButtonSize.Small => theme.Spacing.Sm,
            ButtonSize.Medium => theme.Spacing.Md,
            ButtonSize.Large => theme.Spacing.Lg,
            ButtonSize.Icon => theme.Spacing.Sm,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
    }

    private sealed record ButtonColors(
        ColorToken? Background,
        ColorToken Foreground);
}
