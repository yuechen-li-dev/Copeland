# Machina UI.Grid Authoring M17d

## Purpose

M17d adds an authoring-level `UI.Grid(...)` surface to `Machina.Core`.

It is authoring primitive work only.

`UI.Grid` is an authoring surface over the existing Machina.Layout grid engine.

It does not implement a second layout engine.

Regular grids should be authorable as a 2D/matrix shape.

Sparse or advanced grids should be authorable as explicit cells.

Both forms lower to the same `GridArrange` / `CellFrame` machinery.

## Relationship to M17a/M17b/M17c

M17a documented that low-level grid support already existed and that the missing piece was authoring-surface adoption.

M17b then added the stack authoring primitive.

M17c then used that stack tool inside `OblivionCardRenderer`.

M17d builds the grid tool.

M17e will use the tool to refactor the Oblivion page shell.

Do not combine the authoring primitive with the product refactor.

## Existing low-level GridArrange / CellFrame

The low-level layout engine already contained:

- `GridArrange`
- `CellFrame`
- `FixedGridTrack`
- `FillGridTrack`

Those types already had deterministic resolver behavior and layout tests in `Machina.Layout`.

M17d reuses them directly.

## Why UI.Grid exists

The current repo already knew how to resolve explicit grids.

What it lacked was a readable author-facing surface that:

- hides wrapper rows
- exposes fixed/fill track helpers
- supports regular matrix authoring
- still supports sparse explicit cells when the shape is not rectangular

That is the gap M17d closes.

## Track helpers

M17d adds:

```csharp
UI.Track.Fixed(332)
UI.Track.Fill(1)
```

Supported in M17d:

- fixed tracks
- fill tracks

Deferred:

- auto tracks
- minmax tracks
- proportional `UiLength`
- clamped tracks
- fit-content

## Explicit cell authoring

Sparse or advanced grids can be written as explicit cells:

```csharp
UI.Grid(
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
```

Each explicit cell declares one row, one column, and one child.

Cell spanning is deferred.

## Matrix cell authoring

Regular grids can be written as a matrix:

```csharp
UI.Grid(
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
        [UI.Text("Kind", id: "kind.label"), UI.Text(view.Kind, id: "kind.value")],
        [UI.Text("Status", id: "status.label"), UI.Text(view.Status, id: "status.value")],
        [UI.Text("Source", id: "source.label"), UI.Text(view.SourceLabel, id: "source.value")],
    ]);
```

Each inner list is one row.

Each item index inside a row becomes the column index.

When `rows:` is omitted for matrix authoring, M17d derives one fill row per matrix row and still lowers to the same existing grid engine.

## Matrix validation rules

M17d validates matrix authoring eagerly:

- ragged rows are rejected
- `null` cells are rejected
- column count must match the declared column tracks
- explicit row-track count must match matrix row count
- empty matrices are rejected

Explicit-cell validation also rejects:

- duplicate row/column cells
- negative row or column values
- out-of-range row or column references during lowering

## Gap support

`UI.Grid(...)` supports both:

- `columnGap`
- `rowGap`

They lower directly to `GridArrange.ColumnGap` and `GridArrange.RowGap`.

## Deterministic cell ids

Wrapper cell ids are derived deterministically:

```text
<grid-id>.cell-<row>-<column>
```

Examples:

- `page.grid.cell-0-0`
- `page.grid.cell-0-1`
- `metadata.grid.cell-2-1`

Authors do not need to write those wrapper ids manually.

Child ids are preserved.

## Lowering model

Lowering stays inside the existing pipeline:

- `UI.Grid(...)` creates a `GridNode`
- `UiLowerer` lowers that node to `GridArrange`
- each authored cell lowers to one deterministic wrapper row with `CellFrame`
- the authored child lowers under that wrapper through the normal child frame path
- layout resolution still happens in `Machina.Layout`

No new grid math is introduced in `Machina.Core`.

## Relationship to UI.Stack / UI.Row / UI.Column

M17d preserves:

- `UI.Stack(...)`
- `UI.VStack(...)`
- `UI.HStack(...)`
- `UI.Row(...)`
- `UI.Column(...)`
- `UI.Anchor(...)`

M17d does not silently re-lower those APIs through grid.

`UI.Grid(...)` is an additional authoring primitive, not a replacement.

## Examples

Two-column page shell shape:

```csharp
UI.Grid(
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
    children:
    [
        UI.GridCell(row: 0, column: 0, child: cardsPane),
        UI.GridCell(row: 0, column: 1, child: inspectorPane),
    ]);
```

Regular metadata matrix:

```csharp
UI.Grid(
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
        [UI.Text("Kind"), UI.Text("Creature")],
        [UI.Text("Status"), UI.Text("Ready")],
    ]);
```

## What changed

M17d adds:

- `UI.Grid(...)`
- `UI.GridCell(...)`
- `UI.Track.Fixed(...)`
- `UI.Track.Fill(...)`
- matrix/2D grid authoring
- deterministic cell-wrapper ids
- focused lowering and layout tests
- M17d docs and manifests

## What did not change

M17d does not:

- add a new low-level layout engine
- reimplement `GridArrange` math outside `Machina.Layout`
- refactor `OblivionPageLayout`
- refactor the wide or compact page shell
- implement `GuideFrame`
- implement row variants
- implement proportional `UiLength`
- implement `DeusMachine`
- change TOML or playback product behavior intentionally

## Deferred work

Deferred after M17d:

- M17e Oblivion page-shell migration onto `UI.Grid(...)`
- cell spanning
- auto/minmax/clamped tracks
- proportional `UiLength`
- row variants
- guide/reference placement
- DeusMachine parity

That immediate page-shell migration has now landed as [Oblivion Page Grid Refactor M17e](../Oblivion/oblivion-page-grid-refactor-m17e.md).
M17e uses the existing `UI.Grid(...)` surface documented here and does not add new grid primitives.
