using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Xunit;

namespace Machina.Core.Tests.Flat;

public sealed class UiDocumentSnapshotWriterTests
{
    [Fact]
    public void UiDocumentSnapshotWriter_WritesStableRowTable()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root", view: View.Rect(background: ColorToken.Hex(0xEDEFF0FF))),
                Row.Anchor("settings-card", "root", left: 72, top: 24, width: 500, height: 292, view: View.Rect(background: ColorToken.Hex(0xF8FAFCFF), borderColor: ColorToken.Hex(0xD4D4D8FF), borderThickness: 1)),
                Row.Anchor("title", "settings-card", left: 20, right: 20, top: 20, height: 30, view: View.Text("Machina Presenter", size: TextSize.Md, color: ColorToken.Hex(0x18181BFF))),
                Row.Anchor("actions", "settings-card", left: 20, right: 20, bottom: 20, height: 36, arrange: new StackArrange(StackAxis.Horizontal, Gap: 8)),
                Row.Fixed("save", "actions", height: 36, view: new UiView(Style: new UiStyle(Background: ColorToken.Hex(0x18181BFF), Foreground: ColorToken.White, Padding: 12, BorderColor: ColorToken.Hex(0xD4D4D8FF), BorderThickness: 1), TextStyle: new TextStyle(Color: ColorToken.White, Size: TextSize.Md), Semantics: new UiSemantics(UiRole.Button, "Save", Focusable: true), Action: UiAction.Named("save")))
            ]);

        var snapshot = UiDocumentSnapshotWriter.Write(document);

        const string expected =
            "document:\n" +
            "  rows:\n" +
            "    root parent=<none> order=0 frame=Root view=Rect bg=#EDEFF0FF role=Container\n" +
            "    settings-card parent=root order=0 frame=Anchor left=72Px top=24Px width=500Px height=292Px view=Rect bg=#F8FAFCFF border=#D4D4D8FF borderThickness=1 role=Container\n" +
            "    title parent=settings-card order=0 frame=Anchor left=20Px right=20Px top=20Px height=30Px view=Text textColor=#18181BFF size=Md role=Text label=\"Machina Presenter\"\n" +
            "    actions parent=settings-card order=0 frame=Anchor left=20Px right=20Px bottom=20Px height=36Px arrange=Stack axis=Horizontal gap=8 justify=Start align=Start\n" +
            "    save parent=actions order=0 frame=Fixed width=0 height=36 view=Rect bg=#18181BFF fg=#FFFFFFFF border=#D4D4D8FF borderThickness=1 padding=12 textColor=#FFFFFFFF size=Md role=Button label=\"Save\" focusable=true action=save\n";

        Assert.Equal(expected, snapshot);
    }

    [Fact]
    public void UiDocumentLowerer_PreservesNoWrapperRows()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root", view: View.Rect(background: ColorToken.White)),
                Row.Absolute("panel", "root", 10, 20, 300, 160),
                Row.Anchor("title", "panel", left: 8, top: 8, width: 100, height: 20, view: View.Text("Hi"))
            ]);

        var lowered = UiDocumentLowerer.Lower(document);

        Assert.Equal(document.Rows.Count, lowered.Rows.Count);
        Assert.Equal(document.Rows.Select(x => x.Id.Value), lowered.Rows.Select(x => x.Id.Value));
        Assert.DoesNotContain(lowered.Rows, row => row.Id.Value.Contains("wrapper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UiDocumentLowerer_PreservesMetadataMaps()
    {
        var view = new UiView(
            Style: new UiStyle(
                Background: ColorToken.Hex(0x11223344),
                Foreground: ColorToken.Hex(0x55667788),
                Padding: 7,
                BorderColor: ColorToken.Hex(0xFFEEDDCC),
                BorderThickness: 2),
            TextStyle: new TextStyle(Color: ColorToken.Hex(0xABCDEF12), Size: TextSize.H1),
            Semantics: new UiSemantics(UiRole.Button, "Press", Disabled: true, Focusable: true),
            Action: UiAction.Named("press.action"));

        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([Row.Root("root", view)]));

        Assert.Equal(ColorToken.Hex(0x11223344), lowered.Styles["root"].Background);
        Assert.Equal(TextSize.H1, lowered.TextStyles["root"].Size);
        Assert.Equal("Press", lowered.Semantics["root"].Label);
        Assert.Equal("press.action", lowered.Actions["root"].Name);
    }

    [Fact]
    public void FlatDocument_DuplicateIds_FailsThroughLayoutCompiler()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root"),
                Row.Absolute("dup", "root", 0, 0, 100, 30),
                Row.Absolute("dup", "root", 0, 40, 100, 30)
            ]);

        var lowering = UiDocumentLowerer.Lower(document);

        var error = Assert.Throws<LayoutError>(() => LayoutCompiler.CompileLayoutRows(lowering.Rows));
        Assert.Equal("DuplicateNodeId", error.Code);
    }

    [Fact]
    public void FlatDocument_UnknownParent_FailsThroughLayoutCompiler()
    {
        var document = UiDocument.Create(
            [
                Row.Root("root"),
                Row.Absolute("orphan", "missing-parent", 0, 0, 100, 30)
            ]);

        var lowering = UiDocumentLowerer.Lower(document);

        var error = Assert.Throws<LayoutError>(() => LayoutCompiler.CompileLayoutRows(lowering.Rows));
        Assert.Equal("UnknownParent", error.Code);
    }
}
