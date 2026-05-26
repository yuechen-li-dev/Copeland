using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class StandardLayoutPaddingPipelineTests
{
    [Fact]
    public void PipelineResolvedCardTextIsInsetFromCardOrigin()
    {
        var lowered = UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("card", "root", left: 20, top: 20, width: 200, height: 100, component: StandardUI.Card(id: "shell", child: UI.Text("Inset", id: "text")))
        ]));

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(
            LayoutCompiler.CompileLayoutRows(lowered.Rows),
            new Rect(0, 0, 300, 200));

        var card = resolved.Nodes[new NodeId("card")].Rect;
        var text = resolved.Nodes[new NodeId("card/text")].Rect;
        Assert.True(text.X > card.X);
        Assert.True(text.Y > card.Y);
    }
}
