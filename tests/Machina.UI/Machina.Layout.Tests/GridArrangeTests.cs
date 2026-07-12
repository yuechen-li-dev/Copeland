using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class GridArrangeTests
{
    [Fact]
    public void FixedGridSingleCell()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: Grid(
                    [
                        new FixedGridTrack(100.0),
                    ],
                    [
                        new FixedGridTrack(50.0),
                    ])),
            Row("a", new CellFrame(0, 0), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 300, 200));

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 100, 50);
    }

    [Fact]
    public void FixedGridPadding()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [
                        new FixedGridTrack(100.0),
                    ],
                    [
                        new FixedGridTrack(50.0),
                    ],
                    Padding: new EdgeInsets(6, 7, 8, 5))),
            Row("a", new CellFrame(0, 0), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 300, 200));

        AssertRect(resolved.Nodes["a"].Rect, 5, 6, 100, 50);
    }

    [Fact]
    public void FixedGridGaps()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(100), new FixedGridTrack(50)],
                    [new FixedGridTrack(40), new FixedGridTrack(30)],
                    10,
                    5)),
            Row("a", new CellFrame(0, 0), parent: "root"),
            Row("b", new CellFrame(1, 0), parent: "root"),
            Row("c", new CellFrame(0, 1), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 100, 40);
        AssertRect(resolved.Nodes["b"].Rect, 110, 0, 50, 40);
        AssertRect(resolved.Nodes["c"].Rect, 0, 45, 100, 30);
    }

    [Fact]
    public void FillColumns()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: Grid(
                    [new FixedGridTrack(100), new FillGridTrack(1), new FillGridTrack(3)],
                    [new FixedGridTrack(50)])),
            Row("a", new CellFrame(0, 0), parent: "root"),
            Row("b", new CellFrame(1, 0), parent: "root"),
            Row("c", new CellFrame(2, 0), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 400, 100));

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 100, 50);
        AssertRect(resolved.Nodes["b"].Rect, 100, 0, 75, 50);
        AssertRect(resolved.Nodes["c"].Rect, 175, 0, 225, 50);
    }

    [Fact]
    public void FillRows()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: Grid(
                    [new FixedGridTrack(50)],
                    [new FixedGridTrack(100), new FillGridTrack(1), new FillGridTrack(3)])),
            Row("a", new CellFrame(0, 0), parent: "root"),
            Row("b", new CellFrame(0, 1), parent: "root"),
            Row("c", new CellFrame(0, 2), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 100, 400));

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 50, 100);
        AssertRect(resolved.Nodes["b"].Rect, 0, 100, 50, 75);
        AssertRect(resolved.Nodes["c"].Rect, 0, 175, 50, 225);
    }

    [Fact]
    public void GapsReduceFill()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(100), new FillGridTrack(), new FillGridTrack()],
                    [new FixedGridTrack(50)],
                    10,
                    0)),
            Row("a", new CellFrame(0, 0), parent: "root"),
            Row("b", new CellFrame(1, 0), parent: "root"),
            Row("c", new CellFrame(2, 0), parent: "root"),
        };

        var resolved = Resolve(rows, new Rect(0, 0, 420, 100));

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 100, 50);
        AssertRect(resolved.Nodes["b"].Rect, 110, 0, 150, 50);
        AssertRect(resolved.Nodes["c"].Rect, 270, 0, 150, 50);
    }

    [Fact]
    public void CellColumnSpan()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(100), new FixedGridTrack(50), new FixedGridTrack(25)],
                    [new FixedGridTrack(10)],
                    10,
                    0)),
            Row("a", new CellFrame(0, 0, 2, 1), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["a"].Rect, 0, 0, 160, 10);
    }

    [Fact]
    public void CellRowSpan()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(10)],
                    [new FixedGridTrack(20), new FixedGridTrack(30), new FixedGridTrack(40)],
                    0,
                    5)),
            Row("a", new CellFrame(0, 1, 1, 2), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["a"].Rect, 0, 25, 10, 75);
    }

    [Fact]
    public void CellSpansBothAxes()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(50), new FixedGridTrack(60), new FixedGridTrack(70)],
                    [new FixedGridTrack(10), new FixedGridTrack(20), new FixedGridTrack(30)],
                    5,
                    2)),
            Row("a", new CellFrame(1, 1, 2, 2), parent: "root"),
        };

        var resolved = Resolve(rows);

        AssertRect(resolved.Nodes["a"].Rect, 55, 12, 135, 52);
    }

    [Fact]
    public void OrderPreservedPlacementByCell()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(10), new FixedGridTrack(20)],
                    [new FixedGridTrack(10)])),
            Row("a", new CellFrame(1, 0), parent: "root", order: 0),
            Row("b", new CellFrame(0, 0), parent: "root", order: 1),
        };

        var resolved = Resolve(rows);

        Assert.Equal("a", resolved.Children["root"][0].Value);
        Assert.Equal("b", resolved.Children["root"][1].Value);
        AssertRect(resolved.Nodes["a"].Rect, 10, 0, 20, 10);
        AssertRect(resolved.Nodes["b"].Rect, 0, 0, 10, 10);
    }

    [Fact]
    public void CellDirectResolveRejected()
    {
        AssertLayoutError(
            "CellFrameWithoutGrid",
            () => FrameResolver.ResolveFrame(new Rect(0, 0, 10, 10), new CellFrame(0, 0)));
    }

    [Fact]
    public void CellOutsideGridRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame()),
            Row("a", new CellFrame(0, 0), parent: "root"),
        };

        AssertLayoutError("CellFrameWithoutGrid", () => Resolve(rows));
    }

    [Fact]
    public void AbsoluteInsideGridRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FixedGridTrack(10)], [new FixedGridTrack(10)])),
            Row("a", new AbsoluteFrame(0, 0, 1, 1), parent: "root"),
        };

        AssertLayoutError("InvalidGridChildFrame", () => Resolve(rows));
    }

    [Fact]
    public void FixedInsideGridRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FixedGridTrack(10)], [new FixedGridTrack(10)])),
            Row("a", new FixedFrame(1, 1), parent: "root"),
        };

        AssertLayoutError("InvalidGridChildFrame", () => Resolve(rows));
    }

    [Fact]
    public void EmptyColumnsRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new GridArrange(Array.Empty<GridTrack>(), [new FixedGridTrack(10)])),
        };

        AssertLayoutError("InvalidGridColumns", () => Resolve(rows));
    }

    [Fact]
    public void EmptyRowsRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new GridArrange([new FixedGridTrack(10)], Array.Empty<GridTrack>())),
        };

        AssertLayoutError("InvalidGridRows", () => Resolve(rows));
    }

    [Fact]
    public void InvalidGapRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: new GridArrange([new FixedGridTrack(10)], [new FixedGridTrack(10)], -1, 0)),
        };

        AssertLayoutError("InvalidGridGap", () => Resolve(rows));
    }

    [Fact]
    public void InvalidFixedTrackSizeRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FixedGridTrack(-1)], [new FixedGridTrack(10)])),
        };

        AssertLayoutError("InvalidGridTrackSize", () => Resolve(rows));
    }

    [Fact]
    public void InvalidFillTrackWeightRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FillGridTrack(0)], [new FixedGridTrack(10)])),
        };

        AssertLayoutError("InvalidGridTrackWeight", () => Resolve(rows));
    }

    [Fact]
    public void NegativeContentRejected()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(1)],
                    [new FixedGridTrack(1)],
                    Padding: new EdgeInsets(100, 0, 100, 0))),
        };

        AssertLayoutError("NegativeGridContentSize", () => Resolve(rows, new Rect(0, 0, 100, 100)));
    }

    [Fact]
    public void NegativeRemainingRejected()
    {
        var rows = new[]
        {
            Row(
                "root",
                new RootFrame(),
                arrange: new GridArrange(
                    [new FixedGridTrack(200), new FixedGridTrack(200)],
                    [new FixedGridTrack(10)],
                    10,
                    0)),
        };

        AssertLayoutError("NegativeGridRemainingSpace", () => Resolve(rows, new Rect(0, 0, 300, 100)));
    }

    [Fact]
    public void InvalidCellFrameRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FixedGridTrack(10)], [new FixedGridTrack(10)])),
            Row("a", new CellFrame(-1, 0), parent: "root"),
        };

        AssertLayoutError("InvalidCellFrame", () => Resolve(rows));
    }

    [Fact]
    public void CellOutOfRangeRejected()
    {
        var rows = new[]
        {
            Row("root", new RootFrame(), arrange: Grid([new FixedGridTrack(10), new FixedGridTrack(10)], [new FixedGridTrack(10)])),
            Row("a", new CellFrame(1, 0, 2, 1), parent: "root"),
        };

        AssertLayoutError("GridCellOutOfRange", () => Resolve(rows));
    }

    private static GridArrange Grid(IReadOnlyList<GridTrack> columns, IReadOnlyList<GridTrack> rows)
    {
        return new GridArrange(columns, rows);
    }

    private static Documents.ResolvedLayoutDocument Resolve(LayoutRow[] rows, Rect? root = null)
    {
        return LayoutDocumentResolver.ResolveLayoutDocument(
            LayoutCompiler.CompileLayoutRows(rows),
            root ?? new Rect(0, 0, 300, 100));
    }

    private static LayoutRow Row(
        string id,
        FrameSpec frame,
        string? parent = null,
        int order = 0,
        ArrangeSpec? arrange = null)
    {
        return new LayoutRow(
            id,
            frame,
            parent is null ? (NodeId?)null : new NodeId(parent),
            order,
            0,
            null,
            null,
            null,
            null,
            arrange);
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
