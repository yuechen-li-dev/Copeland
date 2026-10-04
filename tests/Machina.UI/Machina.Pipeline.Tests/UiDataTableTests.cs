using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class UiDataTableTests
{
    [Fact]
    public void EmbeddedTableHasVisibleLabelsAndTheWholeCellSharesItsAction()
    {
        UiNode table = UiDataTable.Build("table",
            [new UiTableColumn("Name", 180, UiAction.Named("sort")), new UiTableColumn("Qty", 60)],
            [new UiTableRow("axe", ["Axe", "1"], UiAction.Named("select"), true)]);
        UiNode root = UI.Surface(width: 500, height: 300,
            children: [UI.Anchor(table, left: 20, top: 30, width: 240, height: 100)]);
        var prepared = new MachinaPresentationPipeline().Prepare(root, 500, 300);
        Assert.Contains(prepared.PresentationFrame.Operations,
            operation => operation is PositionedTextOperation { Text: "Axe" });
        Assert.Equal("sort", prepared.HitTest.HitTest(new PointerPoint(190, 50))?.Action.Name);
        Assert.Equal("select", prepared.HitTest.HitTest(new PointerPoint(190, 80))?.Action.Name);
        Assert.Equal("select", prepared.HitTest.HitTest(new PointerPoint(240, 80))?.Action.Name);
        Assert.Null(prepared.HitTest.HitTest(new PointerPoint(280, 80)));
        Assert.Contains(prepared.PresentationFrame.Operations, operation => operation is PushRectangularClipOperation);
    }

    [Fact]
    public void InvalidCellCountFailsBeforePresentation()
    {
        Assert.Throws<ArgumentException>(() => UiDataTable.Build("bad",
            [new UiTableColumn("Name", 180)], [new UiTableRow("axe", ["Axe", "1"], null)]));
    }
}
