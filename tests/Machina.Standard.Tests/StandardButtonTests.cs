using Machina.Core.Actions;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
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
        Assert.Equal(theme.Spacing.Md, lowered.Styles[buttonId].Padding);
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
    public void ButtonSizesAreCapturedAsStylePaddingTokens()
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

        Assert.Equal(theme.Spacing.Sm, lowered.Styles[new NodeId("small")].Padding);
        Assert.Equal(theme.Spacing.Lg, lowered.Styles[new NodeId("large")].Padding);
    }
}
