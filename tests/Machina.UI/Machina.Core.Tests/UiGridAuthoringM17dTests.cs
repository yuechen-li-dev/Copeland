using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Core.Tests;

public sealed class UiGridAuthoringM17dTests
{
    [Fact]
    public void UIGrid_CreatesGrid()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("page.grid")).Arrange);
        Assert.Collection(
            arrange.Columns,
            _ => { },
            _ => { });
        Assert.Single(arrange.Rows);
    }

    [Fact]
    public void UIGrid_SupportsFixedTracks()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("page.grid")).Arrange);
        var rightColumn = Assert.IsType<FixedGridTrack>(arrange.Columns[1]);
        Assert.Equal(332, rightColumn.Size);
    }

    [Fact]
    public void UIGrid_SupportsFillTracks()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("page.grid")).Arrange);
        var leftColumn = Assert.IsType<FillGridTrack>(arrange.Columns[0]);
        Assert.Equal(1, leftColumn.Weight);
    }

    [Fact]
    public void UIGrid_SupportsRowGap()
    {
        var result = UiLowerer.Lower(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            rows: [UI.Track.Fixed(10), UI.Track.Fixed(10)],
            rowGap: 4,
            children:
            [
                UI.GridCell(row: 0, column: 0, child: UI.Rect(id: "top")),
                UI.GridCell(row: 1, column: 0, child: UI.Rect(id: "bottom")),
            ]));

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("grid")).Arrange);
        Assert.Equal(4, arrange.RowGap);
    }

    [Fact]
    public void UIGrid_SupportsColumnGap()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("page.grid")).Arrange);
        Assert.Equal(24, arrange.ColumnGap);
    }

    [Fact]
    public void UIGrid_ExplicitCellsLowerToCellFrames()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        var frame = Assert.IsType<CellFrame>(Assert.Single(result.Rows, row => row.Id == new NodeId("page.grid.cell-0-1")).Frame);
        Assert.Equal(0, frame.Row);
        Assert.Equal(1, frame.Column);
    }

    [Fact]
    public void UIGrid_MatrixCellsLowerToCellFrames()
    {
        var result = UiLowerer.Lower(CreateMatrixGrid());

        var frame = Assert.IsType<CellFrame>(Assert.Single(result.Rows, row => row.Id == new NodeId("metadata.grid.cell-1-1")).Frame);
        Assert.Equal(1, frame.Row);
        Assert.Equal(1, frame.Column);
    }

    [Fact]
    public void UIGrid_DerivesCellIdsDeterministically()
    {
        var first = Snapshot(CreateMatrixGrid());
        var second = Snapshot(CreateMatrixGrid());

        Assert.Equal(first, second);
        Assert.Contains("metadata.grid.cell-0-0", first);
        Assert.Contains("metadata.grid.cell-2-1", first);
    }

    [Fact]
    public void UIGrid_PreservesChildIds()
    {
        var result = UiLowerer.Lower(CreateMatrixGrid());

        Assert.Contains(result.Rows, row => row.Id == new NodeId("kind.label"));
        Assert.Contains(result.Rows, row => row.Id == new NodeId("source.value"));
    }

    [Fact]
    public void UIGrid_PreservesChildOrder()
    {
        var resolved = Resolve(CreateExplicitGrid(), new Rect(0, 0, 800, 300));

        Assert.Equal(
            new NodeId[] { "page.grid.cell-0-0", "page.grid.cell-0-1" },
            resolved.Children[new NodeId("page.grid")].ToArray());
    }

    [Fact]
    public void UIGrid_MatrixCells_AssignsRowColumnByPosition()
    {
        var result = UiLowerer.Lower(CreateMatrixGrid());

        AssertCellFrame(result, "metadata.grid.cell-0-0", row: 0, column: 0);
        AssertCellFrame(result, "metadata.grid.cell-0-1", row: 0, column: 1);
        AssertCellFrame(result, "metadata.grid.cell-2-0", row: 2, column: 0);
        AssertCellFrame(result, "metadata.grid.cell-2-1", row: 2, column: 1);
    }

    [Fact]
    public void UIGrid_MatrixCells_RejectsRaggedRows()
    {
        var error = Assert.Throws<ArgumentException>(() => UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10), UI.Track.Fixed(10)],
            cells:
            [
                [UI.Text("A"), UI.Text("B")],
                [UI.Text("C")],
            ]));

        Assert.Contains("row 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UIGrid_MatrixCells_RejectsNullCells()
    {
        UiNode? missing = null;

        var error = Assert.Throws<ArgumentException>(() => UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            cells:
            [
                [missing!],
            ]));

        Assert.Contains("must not be null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UIGrid_MatrixCells_RejectsColumnCountMismatch()
    {
        var error = Assert.Throws<ArgumentException>(() => UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10), UI.Track.Fixed(10)],
            cells:
            [
                [UI.Text("A"), UI.Text("B"), UI.Text("C")],
            ]));

        Assert.Contains("expected 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UIGrid_MatrixCells_RejectsRowCountMismatchWhenRowsExplicit()
    {
        var error = Assert.Throws<ArgumentException>(() => UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            rows: [UI.Track.Fixed(10), UI.Track.Fixed(10)],
            cells:
            [
                [UI.Text("A")],
            ]));

        Assert.Contains("explicit row track count 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UIGrid_ExplicitCells_AssignsRequestedRowColumn()
    {
        var result = UiLowerer.Lower(CreateExplicitGrid());

        AssertCellFrame(result, "page.grid.cell-0-0", row: 0, column: 0);
        AssertCellFrame(result, "page.grid.cell-0-1", row: 0, column: 1);
    }

    [Fact]
    public void UIGrid_ExplicitCells_RejectsDuplicateCell()
    {
        var error = Assert.Throws<ArgumentException>(() => UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            rows: [UI.Track.Fixed(10)],
            children:
            [
                UI.GridCell(row: 0, column: 0, child: UI.Text("A")),
                UI.GridCell(row: 0, column: 0, child: UI.Text("B")),
            ]));

        Assert.Contains("Duplicate cell", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UIGrid_ExplicitCells_RejectsNegativeRow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UI.GridCell(row: -1, column: 0, child: UI.Text("A")));
    }

    [Fact]
    public void UIGrid_ExplicitCells_RejectsNegativeColumn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UI.GridCell(row: 0, column: -1, child: UI.Text("A")));
    }

    [Fact]
    public void UIGrid_ExplicitCells_RejectsOutOfRangeColumn()
    {
        var error = Assert.Throws<UiLoweringError>(() => UiLowerer.Lower(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            rows: [UI.Track.Fixed(10)],
            children:
            [
                UI.GridCell(row: 0, column: 1, child: UI.Text("A")),
            ])));

        Assert.Equal("GridCellColumnOutOfRange", error.Code);
    }

    [Fact]
    public void UIGrid_ExplicitCells_RejectsOutOfRangeRow()
    {
        var error = Assert.Throws<UiLoweringError>(() => UiLowerer.Lower(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            rows: [UI.Track.Fixed(10)],
            children:
            [
                UI.GridCell(row: 1, column: 0, child: UI.Text("A")),
            ])));

        Assert.Equal("GridCellRowOutOfRange", error.Code);
    }

    [Fact]
    public void UIGrid_FixedColumnResolvesExpectedWidth()
    {
        var resolved = Resolve(CreateExplicitGrid(), new Rect(0, 0, 900, 300));

        Assert.Equal(332, resolved.Nodes[new NodeId("page.grid.cell-0-1")].Rect.Width);
    }

    [Fact]
    public void UIGrid_FillColumnGetsRemainingWidth()
    {
        var resolved = Resolve(CreateExplicitGrid(), new Rect(0, 0, 900, 300));

        Assert.Equal(544, resolved.Nodes[new NodeId("page.grid.cell-0-0")].Rect.Width);
    }

    [Fact]
    public void UIGrid_FixedRowResolvesExpectedHeight()
    {
        var resolved = Resolve(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(40)],
            rows: [UI.Track.Fixed(30), UI.Track.Fill(1)],
            children:
            [
                UI.GridCell(row: 0, column: 0, child: UI.Rect(id: "top")),
                UI.GridCell(row: 1, column: 0, child: UI.Rect(id: "bottom")),
            ]), new Rect(0, 0, 100, 100));

        Assert.Equal(30, resolved.Nodes[new NodeId("grid.cell-0-0")].Rect.Height);
    }

    [Fact]
    public void UIGrid_FillRowGetsRemainingHeight()
    {
        var resolved = Resolve(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(40)],
            rows: [UI.Track.Fixed(30), UI.Track.Fill(1)],
            rowGap: 5,
            children:
            [
                UI.GridCell(row: 0, column: 0, child: UI.Rect(id: "top")),
                UI.GridCell(row: 1, column: 0, child: UI.Rect(id: "bottom")),
            ]), new Rect(0, 0, 100, 100));

        Assert.Equal(65, resolved.Nodes[new NodeId("grid.cell-1-0")].Rect.Height);
    }

    [Fact]
    public void UIGrid_RowGapAffectsPositions()
    {
        var resolved = Resolve(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(40)],
            rows: [UI.Track.Fixed(20), UI.Track.Fixed(10)],
            rowGap: 7,
            children:
            [
                UI.GridCell(row: 0, column: 0, child: UI.Rect(id: "top")),
                UI.GridCell(row: 1, column: 0, child: UI.Rect(id: "bottom")),
            ]), new Rect(0, 0, 100, 100));

        Assert.Equal(27, resolved.Nodes[new NodeId("grid.cell-1-0")].Rect.Y);
    }

    [Fact]
    public void UIGrid_ColumnGapAffectsPositions()
    {
        var resolved = Resolve(CreateExplicitGrid(), new Rect(0, 0, 900, 300));

        Assert.Equal(568, resolved.Nodes[new NodeId("page.grid.cell-0-1")].Rect.X);
    }

    [Fact]
    public void UIGrid_MatrixTwoByTwoResolvesExpectedRects()
    {
        var resolved = Resolve(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(20), UI.Track.Fill(1)],
            rows: [UI.Track.Fixed(10), UI.Track.Fixed(30)],
            columnGap: 5,
            rowGap: 3,
            cells:
            [
                [UI.Rect(id: "a"), UI.Rect(id: "b")],
                [UI.Rect(id: "c"), UI.Rect(id: "d")],
            ]), new Rect(0, 0, 100, 100));

        AssertRect(resolved.Nodes[new NodeId("grid.cell-0-0")].Rect, 0, 0, 20, 10);
        AssertRect(resolved.Nodes[new NodeId("grid.cell-0-1")].Rect, 25, 0, 75, 10);
        AssertRect(resolved.Nodes[new NodeId("grid.cell-1-0")].Rect, 0, 13, 20, 30);
        AssertRect(resolved.Nodes[new NodeId("grid.cell-1-1")].Rect, 25, 13, 75, 30);
    }

    [Fact]
    public void UIStack_ExistingBehaviorStillWorks()
    {
        var result = UiLowerer.Lower(UI.Stack(
            id: "stack",
            axis: StackAxis.Vertical,
            children:
            [
                UI.StackItem.Fixed(main: 24, child: UI.Text("Title", id: "title")),
                UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "body")),
            ]));

        Assert.Contains(result.Rows, row => row.Id == new NodeId("stack.item-0"));
        Assert.Contains(result.Rows, row => row.Id == new NodeId("stack.item-1"));
    }

    [Fact]
    public void UIRow_ExistingBehaviorStillWorks()
    {
        var result = UiLowerer.Lower(UI.Row(
            id: "row",
            children:
            [
                UI.Text("A", id: "a"),
                UI.Text("B", id: "b"),
            ]));

        Assert.DoesNotContain(result.Rows, row => row.Id.Value.Contains(".cell-", StringComparison.Ordinal));
        Assert.Equal(new NodeId("row"), result.Rows.Single(row => row.Id == new NodeId("a")).Parent);
        Assert.Equal(new NodeId("row"), result.Rows.Single(row => row.Id == new NodeId("b")).Parent);
    }

    [Fact]
    public void UIColumn_ExistingBehaviorStillWorks()
    {
        var result = UiLowerer.Lower(UI.Column(
            id: "column",
            children:
            [
                UI.Text("A", id: "a"),
                UI.Text("B", id: "b"),
            ]));

        Assert.DoesNotContain(result.Rows, row => row.Id.Value.Contains(".cell-", StringComparison.Ordinal));
        Assert.IsType<StackArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("column")).Arrange);
    }

    [Fact]
    public void M17d_DoesNotImplementGuideFrame()
    {
        Assert.Null(typeof(GridArrange).Assembly.GetType("Machina.Layout.Frames.GuideFrame"));
    }

    [Fact]
    public void M17d_DoesNotImplementRowVariants()
    {
        Assert.Null(typeof(LayoutRow).Assembly.GetType("Machina.Layout.Rows.LayoutRowVariant"));
    }

    [Fact]
    public void M17d_DoesNotImplementDeusMachine()
    {
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies().SelectMany(static assembly => assembly.GetTypes()),
            type => type.Name.Contains("DeusMachine", StringComparison.Ordinal));
    }

    [Fact]
    public void UITrack_Fill_RejectsInvalidWeight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UI.Track.Fill(0));
    }

    [Fact]
    public void UIGrid_MatrixRows_DeriveFillTracksWhenRowsOmitted()
    {
        var result = UiLowerer.Lower(UI.Grid(
            id: "grid",
            columns: [UI.Track.Fixed(10)],
            cells:
            [
                [UI.Text("A", id: "a")],
                [UI.Text("B", id: "b")],
            ]));

        var arrange = Assert.IsType<GridArrange>(Assert.Single(result.Rows, row => row.Id == new NodeId("grid")).Arrange);
        Assert.Collection(
            arrange.Rows,
            row => Assert.IsType<FillGridTrack>(row),
            row => Assert.IsType<FillGridTrack>(row));
    }

    private static UiNode CreateExplicitGrid()
    {
        var cardsPane = UI.Rect(id: "cards-pane");
        var inspectorPane = UI.Rect(id: "inspector-pane");

        return UI.Grid(
            id: "page.grid",
            columns:
            [
                UI.Track.Fill(1),
                UI.Track.Fixed(332),
            ],
            rows:
            [
                UI.Track.Fill(1),
            ],
            columnGap: 24,
            rowGap: 0,
            children:
            [
                UI.GridCell(row: 0, column: 0, child: cardsPane),
                UI.GridCell(row: 0, column: 1, child: inspectorPane),
            ]);
    }

    private static UiNode CreateMatrixGrid()
    {
        return UI.Grid(
            id: "metadata.grid",
            columns:
            [
                UI.Track.Fixed(96),
                UI.Track.Fill(1),
            ],
            rowGap: 4,
            columnGap: 8,
            cells:
            [
                [UI.Text("Kind", id: "kind.label"), UI.Text("Creature", id: "kind.value")],
                [UI.Text("Status", id: "status.label"), UI.Text("Ready", id: "status.value")],
                [UI.Text("Source", id: "source.label"), UI.Text("Catalog", id: "source.value")],
            ]);
    }

    private static ResolvedLayoutDocument Resolve(
        UiNode root,
        Rect? rootRect = null)
    {
        var result = UiLowerer.Lower(root);
        var document = LayoutCompiler.CompileLayoutRows(result.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, rootRect ?? new Rect(0, 0, 200, 100));
    }

    private static string Snapshot(UiNode root)
    {
        return string.Join(
            "\n",
            UiLowerer.Lower(root)
                .Rows
                .Select(row => $"{row.Id.Value}:{row.Parent?.Value ?? "<root>"}:{row.Order}:{row.DebugLabel}"));
    }

    private static void AssertCellFrame(
        UiLoweringResult result,
        string id,
        int row,
        int column)
    {
        var frame = Assert.IsType<CellFrame>(Assert.Single(result.Rows, layoutRow => layoutRow.Id == new NodeId(id)).Frame);
        Assert.Equal(row, frame.Row);
        Assert.Equal(column, frame.Column);
    }

    private static void AssertRect(Rect actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }
}
