using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Xunit;

namespace Machina.Core.Tests;

public sealed class UiLoweringSnapshotTests
{
    [Fact]
    public void SnapshotWriterIsDeterministic()
    {
        var first = UiLowerer.Lower(CreateSmallSample());
        var second = UiLowerer.Lower(CreateSmallSample());

        Assert.Equal(
            UiLoweringSnapshotWriter.Write(first),
            UiLoweringSnapshotWriter.Write(second));
    }

    [Fact]
    public void SnapshotWriterIncludesAllLoweringSections()
    {
        var result = UiLowerer.Lower(CreateSmallSample());

        var snapshot = UiLoweringSnapshotWriter.Write(result);

        Assert.Contains("rows:", snapshot);
        Assert.Contains("styles:", snapshot);
        Assert.Contains("textStyles:", snapshot);
        Assert.Contains("semantics:", snapshot);
        Assert.Contains("actions:", snapshot);
        Assert.Contains("role=Text", snapshot);
        Assert.Contains("role=Button", snapshot);
        Assert.Contains("save", snapshot);
    }

    [Fact]
    public void SnapshotWriterPreservesRowOrder()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("First"),
                UI.Text("Second"),
                UI.Text("Third"),
            ]);

        var snapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(root));

        Assert.True(snapshot.IndexOf("Text: First", StringComparison.Ordinal) < snapshot.IndexOf("Text: Second", StringComparison.Ordinal));
        Assert.True(snapshot.IndexOf("Text: Second", StringComparison.Ordinal) < snapshot.IndexOf("Text: Third", StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotWriterProducesStableSmallSnapshot()
    {
        var root = UI.Column(
            children:
            [
                UI.Text("Hello", color: ColorToken.White, size: TextSize.H1),
                UI.Button("Save", action: UiAction.Named("save")),
            ],
            gap: 4);

        var snapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(root));

        const string expected = """
rows:
  ui_0 parent=<none> order=0 z=0 frame=Root arrange=Stack(axis=Vertical,gap=4,padding=0,0,0,0,justify=Start,align=Start) slot=<none> view=<none> layer=<none> debug="Column"
  ui_1 parent=ui_0 order=0 z=0 frame=Fixed(width=70,height=36) arrange=<none> slot=<none> view=<none> layer=<none> debug="Text: Hello"
  ui_2 parent=ui_0 order=1 z=0 frame=Fixed(width=80,height=32) arrange=<none> slot=<none> view=<none> layer=<none> debug="Button: Save"

styles:

textStyles:
  ui_1 color=#FFFFFFFF size=H1

semantics:
  ui_1 role=Text label="Hello" disabled=false focusable=false
  ui_2 role=Button label="Save" disabled=false focusable=true

actions:
  ui_2 => save

""";

        Assert.Equal(expected, snapshot);
    }

    [Fact]
    public void IrizaStyleSampleSnapshotIsDeterministicAndReadable()
    {
        var first = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(CreatePausedSample()));
        var second = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(CreatePausedSample()));

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.Contains("Paused", first);
        Assert.Contains("resume", first);
        Assert.Contains("increment", first);
        Assert.Contains($"role={UiRole.Button}", first);
    }

    private static Machina.Core.Nodes.UiNode CreateSmallSample()
    {
        return UI.Column(
            children:
            [
                UI.Text("Hello", color: ColorToken.White, size: TextSize.H1),
                UI.Button("Save", action: UiAction.Named("save")),
            ]);
    }

    private static Machina.Core.Nodes.UiNode CreatePausedSample()
    {
        return UI.Container(
            child: UI.Rect(
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
                    ])),
            alignX: Machina.Core.Nodes.Align.Center,
            alignY: Machina.Core.Nodes.Align.Center);
    }
}
