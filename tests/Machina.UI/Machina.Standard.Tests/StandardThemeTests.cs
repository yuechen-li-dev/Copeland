using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Layout.Rows;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardThemeTests
{
    [Fact]
    public void DefaultThemeContainsDeterministicTokens()
    {
        var theme = StandardTheme.Default;

        Assert.NotNull(theme);
        Assert.NotEqual(theme.Colors.Background, theme.Colors.Foreground);
        Assert.NotEqual(theme.Colors.Primary, theme.Colors.PrimaryForeground);
        Assert.NotEqual(theme.Colors.Destructive, theme.Colors.DestructiveForeground);
        Assert.True(theme.Spacing.Xs > 0);
        Assert.True(theme.Spacing.Sm > theme.Spacing.Xs);
        Assert.True(theme.Spacing.Md > theme.Spacing.Sm);
        Assert.True(theme.Spacing.Lg > theme.Spacing.Md);
        Assert.True(theme.Spacing.Xl > theme.Spacing.Lg);
        Assert.True(theme.Radius.Sm > 0);
        Assert.True(theme.Radius.Md > theme.Radius.Sm);
        Assert.True(theme.Radius.Lg > theme.Radius.Md);
    }

    [Fact]
    public void StandardTheme_DefaultFamilies_ArePresent()
    {
        var theme = StandardTheme.Default;

        Assert.NotNull(theme.Button);
        Assert.NotNull(theme.Card);
        Assert.NotNull(theme.Input);
        Assert.NotNull(theme.Checkbox);
        Assert.NotNull(theme.Switch);

        Assert.NotNull(theme.Button.Default);
        Assert.NotNull(theme.Card.Default);
        Assert.NotNull(theme.Input.Default);
        Assert.NotNull(theme.Checkbox.Default);
        Assert.NotNull(theme.Switch.Default);
    }

    [Fact]
    public void DefaultTheme_CheckboxMarkContrast_IsDeterministic()
    {
        var checkbox = StandardTheme.Default.Checkbox.Default;

        Assert.NotEqual(checkbox.BoxBackground, checkbox.MarkColor);
        Assert.NotEqual(ColorToken.Hex(0x00000000), checkbox.MarkColor);
        Assert.NotEqual(checkbox.DisabledBackground, checkbox.DisabledMarkColor);
    }

    [Fact]
    public void LeafTextHelpers_PropagateTextStyle()
    {
        var textStyle = new TextStyle(
            Color: ColorToken.Hex(0xABCDEF12),
            Size: TextSize.H1,
            AlignX: TextAlignX.Right,
            AlignY: TextAlignY.Bottom);

        var textNode = UI.Text("Body", id: "body", style: textStyle);
        var textLowered = UiLowerer.Lower(textNode);

        var bodyTextStyle = textLowered.TextStyles[new NodeId("body")];
        Assert.Equal(textStyle.Color, bodyTextStyle.Color);
        Assert.Equal(TextSize.Md, bodyTextStyle.Size);
        Assert.Equal(TextAlignX.Left, bodyTextStyle.AlignX);
        Assert.Equal(TextAlignY.Top, bodyTextStyle.AlignY);

        var labelLowered = UiLowerer.Lower(StandardUI.Label("Name", id: "label"));
        var labelTextStyle = labelLowered.TextStyles[new NodeId("label")];

        Assert.Equal(UiRole.Label, labelLowered.Semantics[new NodeId("label")].Role);
        Assert.Equal(TextSize.Md, labelTextStyle.Size);
        Assert.Equal(StandardTheme.Default.Colors.Foreground, labelTextStyle.Color);
    }
}
