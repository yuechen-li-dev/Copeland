using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class StackArrangeTests
{
    [Fact]
    public void HorizontalFixedStart()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal, 10)),
            Row("a", new FixedFrame(100, 20), parent: "root"),
            Row("b", new FixedFrame(50, 30), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 100, 20);
        AssertRect(resolved.Nodes["b"].Rect, 110, 0, 50, 30);
    }

    [Fact]
    public void HorizontalFill()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal, 10)),
            Row("a", new FixedFrame(100, 20), parent: "root"),
            Row("fill", new FillFrame(1), parent: "root"),
            Row("b", new FixedFrame(50, 20), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["fill"].Rect, 110, 0, 130, 100);
    }

    [Fact]
    public void VerticalFixed()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Vertical, 10)),
            Row("a", new FixedFrame(50, 100), parent: "root"),
            Row("b", new FixedFrame(30, 50), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 200, 300));

        AssertRect(resolved.Nodes["b"].Rect, 0, 110, 30, 50);
    }

    [Fact]
    public void DirectFixedRejected()
    {
        AssertLayoutError(
            "FixedFrameWithoutArranger",
            () => FrameResolver.ResolveFrame(new Rect(0, 0, 10, 10), new FixedFrame(1, 1)));
    }

    [Fact]
    public void DirectFillRejected()
    {
        AssertLayoutError(
            "FillFrameWithoutArranger",
            () => FrameResolver.ResolveFrame(new Rect(0, 0, 10, 10), new FillFrame()));
    }

    [Fact]
    public void InvalidStackChild()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal)),
            Row("a", new AbsoluteFrame(0, 0, 10, 10), parent: "root"),
        };

        AssertLayoutError("InvalidStackChildFrame", () => Resolve(rows));
    }

    [Fact]
    public void InvalidWeight()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal)),
            Row("a", new FillFrame(0), parent: "root"),
        };

        AssertLayoutError("InvalidFillWeight", () => Resolve(rows));
    }

    [Fact]
    public void InvalidFixedSize()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal)),
            Row("a", new FixedFrame(-1, 10), parent: "root"),
        };

        AssertLayoutError("InvalidFixedFrameSize", () => Resolve(rows));
    }

    [Fact]
    public void NegativeContent()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new StackArrange(
                    StackAxis.Horizontal,
                    Padding: new EdgeInsets(0, 200, 0, 200))),
            Row("a", new FixedFrame(1, 1), parent: "root"),
        };

        AssertLayoutError("NegativeStackContentSize", () => Resolve(rows, new Rect(0, 0, 300, 100)));
    }

    [Fact]
    public void NegativeRemaining()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal, 10)),
            Row("a", new FixedFrame(200, 10), parent: "root"),
            Row("b", new FixedFrame(200, 10), parent: "root"),
        };

        AssertLayoutError("NegativeStackRemainingSpace", () => Resolve(rows, new Rect(0, 0, 300, 100)));
    }

    private static Documents.ResolvedLayoutDocument Resolve(LayoutRow[] rows, Rect? root = null)
    {
        return LayoutDocumentResolver.ResolveLayoutDocument(
            LayoutCompiler.CompileLayoutRows(rows),
            root ?? new Rect(0, 0, 300, 100));
    }

    private static LayoutRow Row(string id, FrameSpec frame, string? parent = null, int order = 0, ArrangeSpec? arrange = null)
    {
        return new LayoutRow(id, frame, parent is null ? (NodeId?)null : new NodeId(parent), order, 0, null, null, null, null, arrange);
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
