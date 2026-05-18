using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Core.Tests;

public sealed class AuthoringSurfaceTests
{
    [Fact]
    public void PleasantPausedSampleCompilesAndSnapshotsDeterministically()
    {
        var root = CreatePausedSample();

        var first = UiLowerer.Lower(root);
        var second = UiLowerer.Lower(root);
        var firstSnapshot = UiLoweringSnapshotWriter.Write(first);
        var secondSnapshot = UiLoweringSnapshotWriter.Write(second);

        Assert.Equal(first.Rows, second.Rows);
        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.Contains(first.Actions.Values, action => action.Name == "resume");
        Assert.Contains(first.Actions.Values, action => action.Name == "increment");
        Assert.Contains(first.Semantics.Values, HasResumeButtonSemantics);
        Assert.Contains(first.Semantics.Values, HasIncrementButtonSemantics);
        Assert.Contains("root", firstSnapshot);
        Assert.Contains("paused-panel", firstSnapshot);
        Assert.Contains("title", firstSnapshot);
        Assert.Contains("resume", firstSnapshot);

        var document = LayoutCompiler.CompileLayoutRows(first.Rows);
        Assert.Equal(new NodeId("root"), document.RootId);
    }

    [Fact]
    public void SameDeclarationLowersIdenticallyTwice()
    {
        var root = CreatePausedSample();

        var first = UiLowerer.Lower(root);
        var second = UiLowerer.Lower(root);

        Assert.Equal(first.Rows, second.Rows);
        Assert.Equal(
            UiLoweringSnapshotWriter.Write(first),
            UiLoweringSnapshotWriter.Write(second));
    }

    [Fact]
    public void ExplicitIdsMakeSnapshotReadable()
    {
        var snapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(CreatePausedSample()));

        Assert.Contains("root parent=<none>", snapshot);
        Assert.Contains("paused-panel parent=root", snapshot);
        Assert.Contains("title parent=paused-content", snapshot);
        Assert.Contains("resume parent=buttons", snapshot);
        Assert.DoesNotContain("ui_0 parent=<none>", snapshot);
    }

    [Fact]
    public void ShortcutStyleMergeUsesStyleAsBaseAndShortcutsAsOverrides()
    {
        var root = UI.Column(
            id: "root",
            children:
            [
                UI.Text(
                    "Hello",
                    id: "title",
                    color: ColorToken.White,
                    size: TextSize.H1,
                    style: new TextStyle(Color: ColorToken.Gray, Size: TextSize.Sm)),

                UI.Rect(
                    id: "panel",
                    color: ColorToken.White,
                    style: new UiStyle(Background: ColorToken.Gray, Padding: 4),
                    child: UI.VSpace(1, id: "panel-child")),

                UI.Button(
                    "Save",
                    id: "save",
                    color: ColorToken.White,
                    style: new UiStyle(Background: ColorToken.Gray)),
            ]);

        var result = UiLowerer.Lower(root);

        var titleStyle = result.TextStyles[new NodeId("title")];
        Assert.Equal(ColorToken.White, titleStyle.Color);
        Assert.Equal(TextSize.H1, titleStyle.Size);

        var panelStyle = result.Styles[new NodeId("panel")];
        Assert.Equal(ColorToken.White, panelStyle.Background);
        Assert.Equal(4, panelStyle.Padding);

        var buttonStyle = result.Styles[new NodeId("save")];
        Assert.Equal(ColorToken.Gray, buttonStyle.Background);
        Assert.Equal(ColorToken.White, buttonStyle.Foreground);
    }

    [Fact]
    public void OldSimpleCallStyleStillLowers()
    {
        var ui = UI.Column(
            children:
            [
                UI.Text("Hello"),
                UI.Button("Save", action: UiAction.Named("save")),
            ]);

        var result = UiLowerer.Lower(ui);
        var document = LayoutCompiler.CompileLayoutRows(result.Rows);

        Assert.Equal(new NodeId("ui_0"), document.RootId);
        Assert.Contains(result.Actions.Values, action => action.Name == "save");
    }

    private static UiNode CreatePausedSample()
    {
        var content = UI.Rect(
            id: "paused-panel",
            height: 400,
            color: ColorToken.Hex(0x101820DD),
            padding: 20,
            child: UI.Column(
                id: "paused-content",
                gap: 12,
                children:
                [
                    UI.Text(
                        "Paused",
                        id: "title",
                        color: ColorToken.White,
                        size: TextSize.H1),

                    UI.VSpace(100, id: "title-gap"),

                    UI.Text(
                        "Count: 3",
                        id: "count",
                        color: ColorToken.Gray,
                        size: TextSize.Sm),

                    UI.Row(
                        id: "buttons",
                        gap: 8,
                        children:
                        [
                            UI.Button(
                                "Resume",
                                id: "resume",
                                action: UiAction.Named("resume"),
                                color: ColorToken.Gold),

                            UI.HSpace(50, id: "button-gap"),

                            UI.Button(
                                "Increment",
                                id: "increment",
                                action: UiAction.Named("increment"),
                                color: ColorToken.White),
                        ]),
                ]));

        return UI.Container(
            id: "root",
            alignX: Align.Center,
            alignY: Align.Center,
            child: content);
    }

    private static bool HasResumeButtonSemantics(UiSemantics semantics)
    {
        return semantics.Role == UiRole.Button && semantics.Label == "Resume";
    }

    private static bool HasIncrementButtonSemantics(UiSemantics semantics)
    {
        return semantics.Role == UiRole.Button && semantics.Label == "Increment";
    }
}
