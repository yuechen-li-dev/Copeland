using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Projection;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class ResolvedLayoutTreeBuilderTests
{
    [Fact]
    public void ToResolvedTree_BuildsSingleRootTree()
    {
        var rows = new[] { Row("root", frame: new RootFrame()) };
        var resolved = Resolve(rows);

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(resolved);

        AssertNode(tree, "root", 0, 0, 300, 200);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void ToResolvedTree_BuildsParentChildTree()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", frame: new AbsoluteFrame(5, 6, 100, 50)),
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(Resolve(rows));

        Assert.Single(tree.Children);
        var child = tree.Children[0];
        AssertNode(child, "child", 5, 6, 100, 50);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void ToResolvedTree_PreservesNestedHierarchy()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("panel", parent: "root", frame: new AbsoluteFrame(10, 20, 100, 80)),
            Row("button", parent: "panel", frame: new AnchorFrame(Left: 5, Width: 20, Top: 6, Height: 10)),
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(Resolve(rows));

        Assert.Equal((NodeId)"panel", tree.Children[0].Id);
        Assert.Equal((NodeId)"button", tree.Children[0].Children[0].Id);
        Assert.Empty(tree.Children[0].Children[0].Children);
    }

    [Fact]
    public void ToResolvedTree_PreservesDeterministicChildOrder()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("b", parent: "root", order: 10),
            Row("a", parent: "root", order: 0),
            Row("c", parent: "root", order: 10),
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(Resolve(rows));

        Assert.Equal(new NodeId[] { "a", "b", "c" }, tree.Children.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void ToResolvedTree_PreservesMetadata()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", z: 42, view: "Card", slot: "content", debugLabel: "primary", layer: "front"),
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(Resolve(rows));
        var child = tree.Children[0];

        Assert.Equal(42, child.Z);
        Assert.Equal("Card", child.View);
        Assert.Equal("content", child.Slot);
        Assert.Equal("primary", child.DebugLabel);
        Assert.Equal("front", child.Layer);
    }

    [Fact]
    public void FlattenResolvedTree_ReturnsPreOrderTraversal()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("a", parent: "root", order: 0),
            Row("b", parent: "root", order: 1),
            Row("c", parent: "a"),
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(Resolve(rows));

        var flattened = ResolvedLayoutTreeFlattener.FlattenResolvedTree(tree);

        Assert.Equal(new NodeId[] { "root", "a", "c", "b" }, flattened.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void ToResolvedTree_RejectsMissingRootNode()
    {
        var document = new ResolvedLayoutDocument(
            RootId: "missing",
            Nodes: new Dictionary<NodeId, ResolvedLayoutNode>
            {
                ["root"] = Node("root"),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = Array.Empty<NodeId>(),
            });

        AssertLayoutError("MissingResolvedRootNode", () => ResolvedLayoutTreeBuilder.ToResolvedTree(document));
    }

    [Fact]
    public void ToResolvedTree_RejectsMissingChildrenEntry()
    {
        var document = new ResolvedLayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, ResolvedLayoutNode>
            {
                ["root"] = Node("root"),
                ["child"] = Node("child"),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = new NodeId[] { "child" },
            });

        AssertLayoutError("MissingResolvedChildrenEntry", () => ResolvedLayoutTreeBuilder.ToResolvedTree(document));
    }

    [Fact]
    public void ToResolvedTree_RejectsUnknownChildNode()
    {
        var document = new ResolvedLayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, ResolvedLayoutNode>
            {
                ["root"] = Node("root"),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = new NodeId[] { "ghost" },
            });

        AssertLayoutError("UnknownResolvedChildNode", () => ResolvedLayoutTreeBuilder.ToResolvedTree(document));
    }

    [Fact]
    public void ToResolvedTree_RejectsUnreachableNode()
    {
        var document = new ResolvedLayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, ResolvedLayoutNode>
            {
                ["root"] = Node("root"),
                ["orphan"] = Node("orphan"),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = Array.Empty<NodeId>(),
                ["orphan"] = Array.Empty<NodeId>(),
            });

        AssertLayoutError("UnreachableResolvedNode", () => ResolvedLayoutTreeBuilder.ToResolvedTree(document));
    }

    [Fact]
    public void ToResolvedTree_RejectsCycle()
    {
        var document = new ResolvedLayoutDocument(
            RootId: "root",
            Nodes: new Dictionary<NodeId, ResolvedLayoutNode>
            {
                ["root"] = Node("root"),
                ["a"] = Node("a"),
            },
            Children: new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                ["root"] = new NodeId[] { "a" },
                ["a"] = new NodeId[] { "root" },
            });

        AssertLayoutError("ResolvedDocumentCycleDetected", () => ResolvedLayoutTreeBuilder.ToResolvedTree(document));
    }

    private static ResolvedLayoutDocument Resolve(IReadOnlyList<LayoutRow> rows)
    {
        var document = LayoutCompiler.CompileLayoutRows(rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 300, 200));
    }

    private static ResolvedLayoutNode Node(string id)
    {
        return new ResolvedLayoutNode(id, new Rect(0, 0, 10, 10), new AbsoluteFrame(0, 0, 10, 10), 0, 0, null, null, null, null);
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

    private static void AssertNode(ResolvedLayoutTree node, string id, double x, double y, double width, double height)
    {
        Assert.Equal((NodeId)id, node.Id);
        Assert.Equal(x, node.Rect.X);
        Assert.Equal(y, node.Rect.Y);
        Assert.Equal(width, node.Rect.Width);
        Assert.Equal(height, node.Rect.Height);
    }

    private static LayoutError AssertLayoutError(string expectedCode, Action action)
    {
        var error = Assert.Throws<LayoutError>(action);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }
}
