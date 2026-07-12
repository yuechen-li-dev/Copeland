using Machina.Core.Actions;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardButtonTests
{
    [Fact]
    public void StandardButtonLowersThroughCoreWithSemanticsActionAndStyle()
    {
        var ui = StandardUI.Button(
            "Save",
            id: "save",
            action: UiAction.Named("save"),
            variant: ButtonVariant.Default);

        var lowered = UiLowerer.Lower(ui);
        var buttonId = new NodeId("save");
        var theme = StandardTheme.Default;

        Assert.NotEmpty(lowered.Rows);
        Assert.Equal(UiRole.Button, lowered.Semantics[buttonId].Role);
        Assert.Equal("Save", lowered.Semantics[buttonId].Label);
        Assert.False(lowered.Semantics[buttonId].Disabled);
        Assert.True(lowered.Semantics[buttonId].Focusable);
        Assert.Equal("save", lowered.Actions[buttonId].Name);
        Assert.Equal(theme.Colors.Primary, lowered.Styles[buttonId].Background);
        Assert.Equal(theme.Colors.PrimaryForeground, lowered.Styles[buttonId].Foreground);
        Assert.Equal(0, lowered.Styles[buttonId].Padding);
    }

    [Fact]
    public void DisabledStandardButtonKeepsSemanticsAndOmitsAction()
    {
        var ui = StandardUI.Button(
            "Save",
            id: "save",
            action: UiAction.Named("save"),
            disabled: true);

        var lowered = UiLowerer.Lower(ui);
        var buttonId = new NodeId("save");

        Assert.True(lowered.Semantics[buttonId].Disabled);
        Assert.False(lowered.Semantics[buttonId].Focusable);
        Assert.Empty(lowered.Actions);
    }

    [Fact]
    public void ButtonVariantsProduceDifferentDeterministicStyles()
    {
        var ui = Machina.Core.Authoring.UI.Row(
            id: "buttons",
            gap: 8,
            children:
            [
                StandardUI.Button("Default", id: "default", variant: ButtonVariant.Default),
                StandardUI.Button("Destructive", id: "destructive", variant: ButtonVariant.Destructive),
                StandardUI.Button("Secondary", id: "secondary", variant: ButtonVariant.Secondary),
            ]);

        var lowered = UiLowerer.Lower(ui);
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);
        var theme = StandardTheme.Default;

        Assert.Equal(theme.Colors.Primary, lowered.Styles[new NodeId("default")].Background);
        Assert.Equal(theme.Colors.Destructive, lowered.Styles[new NodeId("destructive")].Background);
        Assert.Equal(theme.Colors.Secondary, lowered.Styles[new NodeId("secondary")].Background);
        Assert.Contains("default", snapshot);
        Assert.Contains("destructive", snapshot);
        Assert.Contains("secondary", snapshot);
        Assert.Contains("#18181BFF", snapshot);
        Assert.Contains("#DC2626FF", snapshot);
    }

    [Fact]
    public void ButtonSizesMapToExplicitLabelTextSizes()
    {
        var ui = Machina.Core.Authoring.UI.Row(
            id: "sizes",
            children:
            [
                StandardUI.Button("Small", id: "small", size: ButtonSize.Small),
                StandardUI.Button("Large", id: "large", size: ButtonSize.Large),
            ]);

        var lowered = UiLowerer.Lower(ui);
        var theme = StandardTheme.Default;

        Assert.Equal(Machina.Core.Styling.TextSize.Sm, lowered.TextStyles[new NodeId("small.label")].Size);
        Assert.Equal(Machina.Core.Styling.TextSize.Md, lowered.TextStyles[new NodeId("large.label")].Size);
    }

    [Fact]
    public void StandardButton_DefaultStyle_TextFitsDefaultShell()
    {
        var lowered = UiLowerer.Lower(StandardUI.Button("Increment", id: "increment", size: ButtonSize.Medium));
        var themeStyle = StandardTheme.Default.Button.Default;
        var labelStyle = lowered.TextStyles[new NodeId("increment.label")];
        var measured = DeterministicTextMeasurer.Instance.MeasureText("Increment", labelStyle);

        Assert.Equal(TextSize.Md, labelStyle.Size);
        Assert.Equal(themeStyle.TextStyle.Size, labelStyle.Size);
        Assert.True(measured.Width <= themeStyle.Width, $"Text width {measured.Width} exceeds button width {themeStyle.Width}.");
        Assert.True(measured.Height <= themeStyle.Height, $"Text height {measured.Height} exceeds button height {themeStyle.Height}.");
    }
}
