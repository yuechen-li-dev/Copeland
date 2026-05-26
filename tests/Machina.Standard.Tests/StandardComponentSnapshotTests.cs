using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardComponentSnapshotTests
{
    [Fact]
    public void CardComposesChildAndEmitsStandardStyle()
    {
        var ui = StandardUI.Card(
            id: "card",
            child: UI.Text("Hello", id: "title"));

        var lowered = UiLowerer.Lower(ui);
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);
        var theme = StandardTheme.Default;
        var cardId = new NodeId("card");

        Assert.Contains("card", snapshot);
        Assert.Contains("title", snapshot);
        Assert.Equal(theme.Colors.Background, lowered.Styles[cardId].Background);
        Assert.Equal(theme.Colors.Foreground, lowered.Styles[cardId].Foreground);
        Assert.Equal(0, lowered.Styles[cardId].Padding);
        Assert.Contains(lowered.Rows, row => row.Id.Value == "card.content");
        Assert.Equal(UiRole.Text, lowered.Semantics[new NodeId("title")].Role);
    }

    [Fact]
    public void BadgeLowersDeterministicallyWithTextSemanticsAndStyles()
    {
        var ui = StandardUI.Badge("Admin", id: "badge");

        var first = UiLowerer.Lower(ui);
        var second = UiLowerer.Lower(StandardUI.Badge("Admin", id: "badge"));
        var snapshot = UiLoweringSnapshotWriter.Write(first);
        var theme = StandardTheme.Default;
        var badgeId = new NodeId("badge");

        Assert.Equal(snapshot, UiLoweringSnapshotWriter.Write(second));
        Assert.Equal(theme.Colors.Secondary, first.Styles[badgeId].Background);
        Assert.Equal(theme.Colors.SecondaryForeground, first.Styles[badgeId].Foreground);
        Assert.Contains(first.Semantics.Values, semantic =>
            semantic.Role == UiRole.Text && semantic.Label == "Admin");
        Assert.Contains("badge", snapshot);
        Assert.Contains("Admin", snapshot);
    }

    [Fact]
    public void HorizontalSeparatorLowersToFixedHeightLine()
    {
        var ui = UI.Column(
            id: "root",
            children:
            [
                StandardUI.Separator(id: "rule", thickness: 2),
            ]);

        var lowered = UiLowerer.Lower(ui);
        var row = lowered.Rows.Single(row => row.Id == new NodeId("rule"));
        var frame = Assert.IsType<FixedFrame>(row.Frame);
        var theme = StandardTheme.Default;

        Assert.Equal(100, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(theme.Colors.Border, lowered.Styles[new NodeId("rule")].Background);
    }

    [Fact]
    public void VerticalSeparatorLowersToFixedWidthLine()
    {
        var ui = UI.Row(
            id: "root",
            children:
            [
                StandardUI.Separator(
                    id: "rule",
                    axis: StackAxis.Vertical,
                    thickness: 3),
            ]);

        var lowered = UiLowerer.Lower(ui);
        var row = lowered.Rows.Single(row => row.Id == new NodeId("rule"));
        var frame = Assert.IsType<FixedFrame>(row.Frame);
        var theme = StandardTheme.Default;

        Assert.Equal(3, frame.Width);
        Assert.Equal(100, frame.Height);
        Assert.Equal(theme.Colors.Border, lowered.Styles[new NodeId("rule")].Background);
    }

    [Fact]
    public void StandardSampleSnapshotIsDeterministicAndCompilesThroughLayout()
    {
        var first = UiLowerer.Lower(CreateStandardSample());
        var second = UiLowerer.Lower(CreateStandardSample());
        var firstSnapshot = UiLoweringSnapshotWriter.Write(first);
        var secondSnapshot = UiLoweringSnapshotWriter.Write(second);

        var document = LayoutCompiler.CompileLayoutRows(first.Rows);

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.NotNull(document);
        Assert.Contains("profile-card", firstSnapshot);
        Assert.Contains("profile-content", firstSnapshot);
        Assert.Contains("role", firstSnapshot);
        Assert.Contains("name", firstSnapshot);
        Assert.Contains("description", firstSnapshot);
        Assert.Contains("rule", firstSnapshot);
        Assert.Contains("actions", firstSnapshot);
        Assert.Contains("save", firstSnapshot);
        Assert.Contains("delete", firstSnapshot);
        Assert.Contains("role=Button", firstSnapshot);
        Assert.Contains("save => save", firstSnapshot);
        Assert.Contains("delete => delete", firstSnapshot);
    }

    [Fact]
    public void RepeatedStandardSampleLoweringProducesEqualSnapshots()
    {
        var first = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(CreateStandardSample()));
        var second = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(CreateStandardSample()));

        Assert.Equal(first, second);
    }

    private static Machina.Core.Nodes.UiNode CreateStandardSample()
    {
        return StandardUI.Card(
            id: "profile-card",
            child: UI.Column(
                id: "profile-content",
                gap: 8,
                children:
                [
                    StandardUI.Badge("Admin", id: "role"),
                    UI.Text("Ada Lovelace", id: "name", size: TextSize.H1),
                    UI.Text("Compiler enjoyer", id: "description", color: ColorToken.Gray),
                    StandardUI.Separator(id: "rule"),
                    UI.Row(
                        id: "actions",
                        gap: 8,
                        children:
                        [
                            StandardUI.Button(
                                "Save",
                                id: "save",
                                action: UiAction.Named("save")),
                            StandardUI.Button(
                                "Delete",
                                id: "delete",
                                action: UiAction.Named("delete"),
                                variant: ButtonVariant.Destructive),
                        ]),
                ]));
    }
}
