using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class LayoutCompilerTests
{
    [Fact]
    public void CompileLayoutRows_CompilesMinimalRootDocument()
    {
        var rows = new[] { new LayoutRow("root", new RootFrame()) };

        var document = LayoutCompiler.CompileLayoutRows(rows);

        Assert.Equal((NodeId)"root", document.RootId);
        Assert.True(document.Nodes.ContainsKey("root"));
        Assert.True(document.Children.ContainsKey("root"));
        Assert.Empty(document.Children["root"]);
    }

    [Fact]
    public void CompileLayoutRows_CompilesParentChildGraph()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root"),
        };

        var document = LayoutCompiler.CompileLayoutRows(rows);

        Assert.True(document.Nodes.ContainsKey("root"));
        Assert.True(document.Nodes.ContainsKey("child"));
        Assert.Equal(new NodeId[] { "child" }, document.Children["root"]);
        Assert.Empty(document.Children["child"]);
    }

    [Fact]
    public void CompileLayoutRows_OrdersChildrenByOrderThenSourceIndex()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child-b", parent: "root", order: 10),
            Row("child-a", parent: "root", order: 0),
            Row("child-c", parent: "root", order: 10),
        };

        var document = LayoutCompiler.CompileLayoutRows(rows);

        Assert.Equal(new NodeId[] { "child-a", "child-b", "child-c" }, document.Children["root"]);
    }

    [Fact]
    public void CompileLayoutRows_RejectsMissingRoot()
    {
        var rows = new[] { Row("child", parent: "root") };

        var error = AssertLayoutError("MissingRoot", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("MissingRoot", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsMultipleRoots()
    {
        var rows = new[]
        {
            Row("root-a", frame: new RootFrame()),
            Row("root-b", frame: new RootFrame()),
        };

        var error = AssertLayoutError("MultipleRoots", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("MultipleRoots", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CompileLayoutRows_RejectsInvalidNodeId(string value)
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row(value, parent: "root"),
        };

        var error = AssertLayoutError("InvalidNodeId", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("InvalidNodeId", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsDuplicateNodeId()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("dup", parent: "root"),
            Row("dup", parent: "root"),
        };

        var error = AssertLayoutError("DuplicateNodeId", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("DuplicateNodeId", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsUnknownParent()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "missing"),
        };

        var error = AssertLayoutError("UnknownParent", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("UnknownParent", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsSelfCycle()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("a", parent: "a"),
        };

        var error = AssertLayoutError("CycleDetected", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("CycleDetected", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsMultiNodeCycle()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("a", parent: "b"),
            Row("b", parent: "a"),
        };

        var error = AssertLayoutError("CycleDetected", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("CycleDetected", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsNonRootRootFrame()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", frame: new RootFrame(), parent: "root"),
        };

        var error = AssertLayoutError("RootFrameNotRoot", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("RootFrameNotRoot", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_RejectsMissingFrame()
    {
        var rows = new[]
        {
            new LayoutRow("root", null!),
        };

        var error = AssertLayoutError("MissingFrame", () => LayoutCompiler.CompileLayoutRows(rows));

        Assert.Equal("MissingFrame", error.Code);
    }

    [Fact]
    public void CompileLayoutRows_PreservesMetadata()
    {
        var rows = new[]
        {
            Row("root", frame: new RootFrame()),
            Row("child", parent: "root", z: 42, slot: "content", view: "Card", debugLabel: "primary", layer: "front"),
        };

        var document = LayoutCompiler.CompileLayoutRows(rows);
        var child = document.Nodes["child"];

        Assert.Equal(42, child.Z);
        Assert.Equal("content", child.Slot);
        Assert.Equal("Card", child.View);
        Assert.Equal("primary", child.DebugLabel);
        Assert.Equal("front", child.Layer);
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

    private static LayoutError AssertLayoutError(string expectedCode, Action action)
    {
        var error = Assert.Throws<LayoutError>(action);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }
}
