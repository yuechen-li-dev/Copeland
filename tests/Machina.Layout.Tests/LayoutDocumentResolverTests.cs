using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class LayoutDocumentResolverTests
{
    [Fact]
    public void ResolveLayoutDocument_RootRectComesFromCaller()
    {
        var document = LayoutCompiler.CompileLayoutRows(new[] { Row("root", frame: new RootFrame()) });

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(10, 20, 300, 200));

        AssertRect(resolved.Nodes["root"].Rect, 10, 20, 300, 200);
    }

    [Fact]
    public void ResolveLayoutDocument_IgnoresRootFrameGeometry()
    {
        var document = LayoutCompiler.CompileLayoutRows(new[] { Row("root", frame: new AbsoluteFrame(999, 999, 1, 1)) });

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 300, 200));

        AssertRect(resolved.Nodes["root"].Rect, 0, 0, 300, 200);
    }

    [Fact]
    public void ResolveLayoutDocument_ResolvesAbsoluteChildInRootSpace()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", frame: new AbsoluteFrame(5, 6, 100, 50)),
        };

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(10, 20, 300, 200));

        AssertRect(resolved.Nodes["child"].Rect, 15, 26, 100, 50);
    }

    [Fact]
    public void ResolveLayoutDocument_ResolvesAnchorChildInRootSpace()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", frame: new AnchorFrame(Left: 10, Right: 20, Top: 5, Bottom: 15)),
        };

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(10, 20, 300, 200));

        AssertRect(resolved.Nodes["child"].Rect, 20, 25, 270, 180);
    }

    [Fact]
    public void ResolveLayoutDocument_ResolvesGrandchildAgainstResolvedParent()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("panel", parent: "root", frame: new AbsoluteFrame(10, 20, 100, 80)),
            Row("button", parent: "panel", frame: new AnchorFrame(Left: 5, Width: 20, Top: 6, Height: 10)),
        };

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(0, 0, 300, 200));

        AssertRect(resolved.Nodes["panel"].Rect, 10, 20, 100, 80);
        AssertRect(resolved.Nodes["button"].Rect, 15, 26, 20, 10);
    }

    [Fact]
    public void ResolveLayoutDocument_PreservesChildrenMapOrdering()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("a", parent: "root", order: 1),
            Row("b", parent: "root", order: 0),
            Row("c", parent: "b"),
        };

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(0, 0, 300, 200));

        Assert.Equal(new NodeId[] { "b", "a" }, resolved.Children["root"]);
        Assert.Equal(new NodeId[] { "c" }, resolved.Children["b"]);
        Assert.Empty(resolved.Children["a"]);
        Assert.Empty(resolved.Children["c"]);
    }

    [Fact]
    public void ResolveLayoutDocument_PreservesNodeMetadata()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("node", parent: "root", z: 42, slot: "content", view: "Card", debugLabel: "primary", layer: "front"),
        };

        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(0, 0, 100, 100));
        var node = resolved.Nodes["node"];

        Assert.Equal(42, node.Z);
        Assert.Equal("content", node.Slot);
        Assert.Equal("Card", node.View);
        Assert.Equal("primary", node.DebugLabel);
        Assert.Equal("front", node.Layer);
        Assert.Equal(0, node.Order);
    }

    [Fact]
    public void ResolveLayoutDocument_RejectsInvalidRootRect()
    {
        var document = LayoutCompiler.CompileLayoutRows(new[] { Row("root", frame: new RootFrame()) });

        AssertLayoutError("InvalidRootRect", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, -1, 10)));
        AssertLayoutError("InvalidRootRect", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(double.NaN, 0, 1, 1)));
    }

    [Fact]
    public void ResolveLayoutDocument_RejectsMissingRootNode()
    {
        var document = new LayoutDocument(
            RootId: "missing",
            Nodes: new Dictionary<NodeId, LayoutNode>
            {
                ["root"] = new LayoutNode("root", new RootFrame(), 0, 0, null, null, null, null),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = Array.Empty<NodeId>(),
            });

        AssertLayoutError("MissingRootNode", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void ResolveLayoutDocument_RejectsMissingChildrenEntry()
    {
        var document = new LayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, LayoutNode>
            {
                ["root"] = new LayoutNode("root", new RootFrame(), 0, 0, null, null, null, null),
                ["child"] = new LayoutNode("child", new AbsoluteFrame(0, 0, 1, 1), 0, 0, null, null, null, null),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = new NodeId[] { "child" },
            });

        AssertLayoutError("MissingChildrenEntry", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void ResolveLayoutDocument_RejectsUnknownChildNode()
    {
        var document = new LayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, LayoutNode>
            {
                ["root"] = new LayoutNode("root", new RootFrame(), 0, 0, null, null, null, null),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = new NodeId[] { "ghost" },
            });

        AssertLayoutError("UnknownChildNode", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void ResolveLayoutDocument_RejectsUnreachableNode()
    {
        var document = new LayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, LayoutNode>
            {
                ["root"] = new LayoutNode("root", new RootFrame(), 0, 0, null, null, null, null),
                ["orphan"] = new LayoutNode("orphan", new AbsoluteFrame(0, 0, 1, 1), 0, 0, null, null, null, null),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = Array.Empty<NodeId>(),
                ["orphan"] = Array.Empty<NodeId>(),
            });

        AssertLayoutError("UnreachableNode", () => LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void ResolveLayoutDocument_PropagatesFrameResolverErrors()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", frame: new AnchorFrame(Left: 1, Top: 1, Height: 2)),
        };

        var error = AssertLayoutError("InvalidAnchorHorizontal", () =>
            LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), new Rect(0, 0, 100, 100)));

        Assert.Equal("InvalidAnchorHorizontal", error.Code);
    }

    private static LayoutRow Row(
        string id,
        FrameSpec? frame = null,
        string? parent = null,
        int order = 0,
        int z = 0,
        string? slot = null,
        string? view = null,
        string? debugLabel = null,
        string? layer = null)
    {
        return new LayoutRow(id, frame ?? new AbsoluteFrame(0, 0, 10, 10), parent is null ? (NodeId?)null : new NodeId(parent), order, z, view, slot, debugLabel, layer);
    }

    private static void AssertRect(Rect actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    private static LayoutError AssertLayoutError(string expectedCode, Action action)
    {
        var error = Assert.Throws<LayoutError>(action);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }
}
