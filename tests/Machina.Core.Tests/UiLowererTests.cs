using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Core.Tests;

public sealed class UiLowererTests
{
    [Fact]
    public void LowerTextNodeEmitsDeterministicTextSemanticsAndStyle()
    {
        var root = UI.Text("Hello", color: ColorToken.White, size: TextSize.Md);

        var result = UiLowerer.Lower(root);

        Assert.Single(result.Rows);
        Assert.Equal(new NodeId("ui_0"), result.Rows[0].Id);
        Assert.IsType<RootFrame>(result.Rows[0].Frame);

        var textId = new NodeId("ui_0");
        var semantics = Assert.Single(result.Semantics);
        Assert.Equal(textId, semantics.Key);
        Assert.Equal(UiRole.Text, semantics.Value.Role);
        Assert.Equal("Hello", semantics.Value.Label);
        Assert.Equal(ColorToken.White, result.TextStyles[textId].Color);
        Assert.Equal(TextSize.Md, result.TextStyles[textId].Size);
    }


    [Fact]
    public void LowerTextInStackUsesDeterministicTextMeasurer()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("Hello", size: TextSize.Md),
            ]);

        var result = UiLowerer.Lower(root);

        var textRow = result.Rows.Single(row => row.DebugLabel == "Text: Hello");
        var frame = Assert.IsType<FixedFrame>(textRow.Frame);
        Assert.Equal(40, frame.Width);
        Assert.Equal(20, frame.Height);
    }

    [Fact]
    public void LowerTextUsesConfiguredTextMeasurer()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("Hello", size: TextSize.Md),
            ]);
        var options = new UiLoweringOptions(new ConstantTextMeasurer(123, 45));

        var result = UiLowerer.Lower(root, options);

        var textRow = result.Rows.Single(row => row.DebugLabel == "Text: Hello");
        var frame = Assert.IsType<FixedFrame>(textRow.Frame);
        Assert.Equal(123, frame.Width);
        Assert.Equal(45, frame.Height);
    }

    [Fact]
    public void LowerButtonUsesMeasuredTextWithPaddingAndMinimumSize()
    {
        var root = UI.Row(
            children:
            [
                UI.Button("Save", action: UiAction.Named("save")),
                UI.Button("Longer Button", action: UiAction.Named("long")),
            ]);

        var result = UiLowerer.Lower(root);

        var shortButtonFrame = Assert.IsType<FixedFrame>(
            result.Rows.Single(row => row.DebugLabel == "Button: Save").Frame);
        Assert.Equal(80, shortButtonFrame.Width);
        Assert.Equal(32, shortButtonFrame.Height);

        var longButtonFrame = Assert.IsType<FixedFrame>(
            result.Rows.Single(row => row.DebugLabel == "Button: Longer Button").Frame);
        Assert.Equal(128, longButtonFrame.Width);
        Assert.Equal(32, longButtonFrame.Height);
    }

    [Fact]
    public void LowerButtonNodeEmitsSemanticsAndActionMetadata()
    {
        var root = UI.Button("Save", action: UiAction.Named("save"));

        var result = UiLowerer.Lower(root);

        var buttonId = new NodeId("ui_0");
        Assert.Equal(UiRole.Button, result.Semantics[buttonId].Role);
        Assert.Equal("Save", result.Semantics[buttonId].Label);
        Assert.False(result.Semantics[buttonId].Disabled);
        Assert.True(result.Semantics[buttonId].Focusable);
        Assert.Equal("save", result.Actions[buttonId].Name);
    }

    [Fact]
    public void LowerDisabledButtonMarksDisabledAndOmitsAction()
    {
        var root = UI.Button("Save", action: UiAction.Named("save"), disabled: true);

        var result = UiLowerer.Lower(root);

        var buttonId = new NodeId("ui_0");
        Assert.True(result.Semantics[buttonId].Disabled);
        Assert.False(result.Semantics[buttonId].Focusable);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void LowerColumnProducesFlatRowsWithVerticalStackArrange()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("A"),
                UI.Text("B"),
            ],
            gap: 4);

        var result = UiLowerer.Lower(root);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(new NodeId("ui_0"), result.Rows[0].Id);
        Assert.Null(result.Rows[0].Parent);

        var arrange = Assert.IsType<StackArrange>(result.Rows[0].Arrange);
        Assert.Equal(StackAxis.Vertical, arrange.Axis);
        Assert.Equal(4, arrange.Gap);

        Assert.Equal(new NodeId("ui_0"), result.Rows[1].Parent);
        Assert.Equal(new NodeId("ui_0"), result.Rows[2].Parent);
        Assert.Equal(["ui_0", "ui_1", "ui_2"], result.Rows.Select(row => row.Id.Value).ToArray());
    }

    [Fact]
    public void LowerRowProducesHorizontalStackArrange()
    {
        var root = UI.Row(
            children:
            [
                UI.Text("A"),
                UI.HSpace(12),
                UI.Text("B"),
            ],
            gap: 2);

        var result = UiLowerer.Lower(root);

        var arrange = Assert.IsType<StackArrange>(result.Rows[0].Arrange);
        Assert.Equal(StackAxis.Horizontal, arrange.Axis);
        Assert.Equal(2, arrange.Gap);

        var spacer = result.Rows.Single(row => row.DebugLabel == "HSpace");
        var frame = Assert.IsType<FixedFrame>(spacer.Frame);
        Assert.Equal(12, frame.Width);
        Assert.Equal(0, frame.Height);
    }

    [Fact]
    public void LowerRectWithChildCapturesStyleAndChildParentLink()
    {
        var root = UI.Rect(
            height: 200,
            color: ColorToken.Hex(0x101820FF),
            padding: 16,
            child: UI.Text("Inside"));

        var result = UiLowerer.Lower(root);

        var rectId = new NodeId("ui_0");
        var childId = new NodeId("ui_1");
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(rectId, result.Rows[1].Parent);
        Assert.Equal(childId, result.Rows[1].Id);
        Assert.Equal(ColorToken.Hex(0x101820FF), result.Styles[rectId].Background);
        Assert.Equal(16, result.Styles[rectId].Padding);
    }

    [Fact]
    public void IrizaStylePausedSampleLowersDeterministically()
    {
        var first = UiLowerer.Lower(CreatePausedSample(paused: true));
        var second = UiLowerer.Lower(CreatePausedSample(paused: true));

        Assert.Equal(Snapshot(first), Snapshot(second));
        Assert.Contains(first.Actions.Values, action => action.Name == "resume");
        Assert.Contains(first.Actions.Values, action => action.Name == "increment");
        Assert.Contains(first.Semantics.Values, semantic => semantic.Role == UiRole.Button && semantic.Label == "Resume");
        Assert.Contains(first.Semantics.Values, semantic => semantic.Role == UiRole.Button && semantic.Label == "Increment");
    }

    [Fact]
    public void DuplicateExplicitIdsAreRejected()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("A") with { Id = "same" },
                UI.Text("B") with { Id = "same" },
            ]);

        var error = Assert.Throws<UiLoweringError>(() => UiLowerer.Lower(root));
        Assert.Equal("DuplicateUiNodeId", error.Code);
    }

    [Fact]
    public void LoweredRowsCompileThroughMachinaLayout()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("A"),
                UI.Button("Save", action: UiAction.Named("save")),
            ]);
        var result = UiLowerer.Lower(root);

        var document = LayoutCompiler.CompileLayoutRows(result.Rows);

        Assert.Equal(new NodeId("ui_0"), document.RootId);
    }

    private sealed class ConstantTextMeasurer(double width, double height) : ITextMeasurer
    {
        public IntrinsicSize MeasureText(string text, TextStyle style)
        {
            return new IntrinsicSize(width, height);
        }
    }

    private static UiNode CreatePausedSample(bool paused)
    {
        UiNode content;

        if (!paused)
        {
            content = UI.Rect(
                height: 200,
                color: ColorToken.Hex(0x101820FF),
                padding: 16,
                child: UI.Column(
                    children:
                    [
                        UI.Text("Running... Count: 3", color: ColorToken.White, size: TextSize.Md),
                        UI.Button("Pause", action: UiAction.Named("pause"), color: ColorToken.White),
                    ]));
        }
        else
        {
            content = UI.Rect(
                height: 400,
                color: ColorToken.Hex(0x101820DD),
                padding: 20,
                child: UI.Column(
                    children:
                    [
                        UI.Text("Paused", color: ColorToken.White, size: TextSize.H1),
                        UI.VSpace(100),
                        UI.Text("Count: 3", color: ColorToken.Gray, size: TextSize.Sm),
                        UI.Row(
                            children:
                            [
                                UI.Button("Resume", action: UiAction.Named("resume")),
                                UI.HSpace(50),
                                UI.Button("Increment", action: UiAction.Named("increment")),
                            ]),
                    ]));
        }

        return UI.Container(
            child: content,
            alignX: Align.Center,
            alignY: Align.Center);
    }

    private static string Snapshot(UiLoweringResult result)
    {
        var rowLines = result.Rows.Select(row =>
            $"row:{row.Id.Value}:{row.Parent?.Value ?? "<root>"}:{row.Order}:{row.Frame.GetType().Name}:{row.Arrange?.GetType().Name ?? "none"}:{row.DebugLabel}");

        var actionLines = result.Actions
            .OrderBy(pair => pair.Key.Value)
            .Select(pair => $"action:{pair.Key.Value}:{pair.Value.Name}");

        var semanticLines = result.Semantics
            .OrderBy(pair => pair.Key.Value)
            .Select(pair => $"semantic:{pair.Key.Value}:{pair.Value.Role}:{pair.Value.Label}:{pair.Value.Disabled}:{pair.Value.Focusable}");

        return string.Join("\n", rowLines.Concat(actionLines).Concat(semanticLines));
    }
}
