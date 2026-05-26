# GridArrange design contract (M5a)

## 1. Executive summary

This document defines the **design contract** for a future `GridArrange` runtime implementation in M5b.

> Status (May 11, 2026): M5b runtime implementation is now available in core.

`GridArrange` is introduced as an explicit, deterministic 2D arranger that fits the existing Machina model:

- rows are still authored as flat `LayoutRow[]`,
- parents own coordinate spaces,
- arrangers place direct children,
- resolved output remains rectangles,
- adapters (including React) continue to render resolved rectangles.

`GridArrange` is a narrow, explicit model for row/column placement and **is not** a CSS Grid clone.

## 2. Goals

- Add a first-class 2D arranger as `ArrangeSpec`.
- Keep frame vs arrange responsibility intact:
  - `frame` defines node rectangle,
  - `arrange` defines how node places direct children.
- Require explicit child placement using a new `CellFrame` for direct children of grid parents.
- Keep resolution parent-rect driven and deterministic.
- Integrate with existing features: responsive variants, offset, z metadata, resolved tree, interpolation.
- Keep v1 scope narrow and implementation-ready for M5b.

## 3. Non-goals

M5a and M5b v1 explicitly do **not** include:

- CSS Grid syntax or compatibility,
- auto-placement,
- implicit row/column creation,
- template areas,
- named lines,
- `minmax` or intrinsic track sizing,
- content measurement / DOM measurement,
- subgrid,
- masonry,
- per-cell margins,
- percentage string units,
- `UiLength` tracks,
- grid-level align/justify negotiation,
- graph mutation (add/remove nodes via variants).

## 4. Type proposal

### 4.1 `ArrangeSpec`

`GridArrange` is added as an arrange type.

```ts
export type ArrangeSpec =
  | StackArrange
  | GridArrange;
```

### 4.2 `CellFrame`

Direct children of grid parents use a new `CellFrame`.

```ts
export type CellFrame = {
  kind: "cell";
  col: number;
  row: number;
  colSpan?: number;
  rowSpan?: number;
};
```

`FrameSpec` proposal:

```ts
export type FrameSpec =
  | RootFrame
  | AbsoluteFrame
  | AnchorFrame
  | FixedFrame
  | FillFrame
  | CellFrame;
```

### 4.3 `GridTrack`

Grid tracks are explicit records (not `number | "fill"`).

```ts
export type GridTrack =
  | { kind: "fixed"; size: number }
  | { kind: "fill"; weight?: number };
```

### 4.4 `GridArrange`

```ts
export type GridArrange = {
  kind: "grid";
  columns: GridTrack[];
  rows: GridTrack[];
  columnGap?: number;
  rowGap?: number;
  padding?: number | EdgeInsets;
};
```

Defaults:

- `columnGap = 0`
- `rowGap = 0`
- `padding = 0`

## 5. Semantics

### 5.1 Placement model

- `GridArrange` only affects placement of **direct children**.
- If a parent has `arrange.kind === "grid"`, every direct child frame must be `CellFrame`.
- Children of those child nodes resolve using normal existing rules (their own frame + optional arrange).

### 5.2 Indexing and spans

- `row` and `col` are **zero-based** indexes.
- `colSpan` defaults to `1`.
- `rowSpan` defaults to `1`.
- Spans must be positive integers.
- No implicit placement, no next-cell flow.

### 5.3 Track semantics

- `fixed`: explicit px size, finite and `>= 0`.
- `fill`: proportional share of remaining space by weight.
- `fill.weight` default is `1`, must be finite and `> 0`.
- Fractional resolved sizes are allowed.
- No rounding step.
- No `UiLength` tracks in v1.

### 5.4 Content rect semantics

Given parent rect `R` and normalized padding `P`:

- `content.x = R.x + P.left`
- `content.y = R.y + P.top`
- `content.width = R.width - P.left - P.right`
- `content.height = R.height - P.top - P.bottom`

If `content.width < 0` or `content.height < 0`, resolution fails with `GridContentNegative`.

## 6. Validation and error codes

New grid-specific error codes:

```ts
| "CellFrameWithoutGrid"
| "GridChildMustBeCell"
| "InvalidGridTrack"
| "InvalidGridCell"
| "GridContentNegative"
| "GridOverflow"
```

### 6.1 `InvalidGridTrack`

Used for invalid grid arrange/track config, including:

- `columns.length === 0`,
- `rows.length === 0`,
- fixed size non-finite or `< 0`,
- fill weight non-finite or `<= 0`,
- `columnGap`/`rowGap` non-finite or `< 0`.

### 6.2 `InvalidGridCell`

Used for invalid cell coordinate/span config, including:

- `row`/`col` non-integer,
- `row`/`col < 0`,
- span non-integer,
- span `<= 0`,
- out-of-range span (`row + rowSpan > rows.length`, `col + colSpan > columns.length`).

### 6.3 `GridChildMustBeCell`

Used when a node under a `grid` parent has any non-`cell` frame kind.

### 6.4 `CellFrameWithoutGrid`

Used when:

- `resolveFrame` is called directly with `CellFrame`,
- or a child uses `CellFrame` under a non-grid parent.

### 6.5 `GridContentNegative`

Used when normalized padding yields negative content width or height.

### 6.6 `GridOverflow`

Used when, for an axis:

`contentAxisSize - fixedTotal - gapTotal < 0`.

## 7. Resolution algorithm

### 7.1 Axis track resolution (columns or rows)

Inputs:

- `contentAxisSize`,
- `tracks`,
- `gap`,
- axis label for diagnostics.

Compute:

1. `fixedTotal = sum(fixed.size)`
2. `gapTotal = gap * max(0, tracks.length - 1)`
3. `remaining = contentAxisSize - fixedTotal - gapTotal`

Rules:

- If `remaining < 0` -> `GridOverflow`.
- If fill tracks exist:
  - `totalWeight = sum(fill.weight ?? 1)`
  - each fill size = `remaining * weight / totalWeight`
- If no fill tracks exist:
  - leave trailing `remaining` unused at axis end.
- No track justification in v1.
- No auto gap expansion.

Output per track:

- resolved `size`,
- `start` offset from content-axis origin.

### 7.2 Child cell rectangle resolution

For child `CellFrame` with defaults applied (`rowSpan = 1`, `colSpan = 1`):

1. Validate cell coordinates/spans.
2. Resolve position:
   - `x = content.x + columnStarts[col]`
   - `y = content.y + rowStarts[row]`
3. Resolve span size:
   - `width = sum(columnSizes[col..col+colSpan-1]) + columnGap * (colSpan - 1)`
   - `height = sum(rowSizes[row..row+rowSpan-1]) + rowGap * (rowSpan - 1)`
4. Apply node `offset` post-placement using existing semantics:
   - x offset resolves against **parent rect width**,
   - y offset resolves against **parent rect height**,
   - offset does not affect sibling placement or track resolution.

## 8. Interaction with existing features

### 8.1 StackArrange

- Stack remains the ordered 1D arranger.
- Grid is an additional arranger type, not a stack replacement.
- No behavior change to existing stack resolution.

### 8.2 Responsive variants

- `variants` may override `arrange` to `GridArrange`.
- `variants` may override child `frame` to `CellFrame`.
- No special-case variant resolver logic is required.
- Existing constraint remains: variants cannot add/remove rows.

### 8.3 Offset

- Offset stays node-local post-placement.
- Grid placement determines base rect, then existing offset machinery is applied.

### 8.4 z

- Existing sibling-local bounded `z` behavior is unchanged.
- Grid impacts geometry only.

### 8.5 Resolved tree

- `ResolvedLayoutDocument` preserves selected `arrange`/`frame` metadata, including `grid` and `cell`.
- `toResolvedTree` and flattening require no grid-specific branching beyond type acceptance.

### 8.6 lerp

- `lerpResolvedLayouts` already interpolates resolved rectangles.
- No special interpolation path required for grid metadata.

### 8.7 React adapter

- No adapter changes are required.
- Adapter consumes resolved rectangles; grid is core resolver-only behavior.

## 9. Examples

### 9.1 Simple 2x2 grid

```ts
{
  id: "grid",
  parent: "root",
  frame: { kind: "anchor", left: 0, right: 0, top: 0, bottom: 0 },
  arrange: {
    kind: "grid",
    columns: [{ kind: "fill" }, { kind: "fill" }],
    rows: [{ kind: "fixed", size: 100 }, { kind: "fill" }],
    columnGap: 8,
    rowGap: 8,
    padding: 16,
  },
}

{
  id: "a",
  parent: "grid",
  frame: { kind: "cell", row: 0, col: 0 },
  view: "Card",
}
```

### 9.2 Dashboard cards with fill columns

```ts
{
  id: "dashboard",
  parent: "root",
  frame: { kind: "anchor", left: 0, right: 0, top: 0, bottom: 0 },
  arrange: {
    kind: "grid",
    columns: [
      { kind: "fill", weight: 2 },
      { kind: "fill", weight: 1 },
      { kind: "fill", weight: 1 },
    ],
    rows: [{ kind: "fixed", size: 160 }, { kind: "fixed", size: 220 }],
    columnGap: 12,
    rowGap: 12,
    padding: { top: 16, right: 16, bottom: 16, left: 16 },
  },
}
```

### 9.3 Spanning child

```ts
{
  id: "chart",
  parent: "grid",
  frame: { kind: "cell", row: 1, col: 0, colSpan: 2 },
  view: "Chart",
}
```

### 9.4 Responsive variant example

Base:

- parent uses a 3-column grid,
- cards A/B/C have explicit `CellFrame` positions.

Narrow variant:

- parent switches to one column,
- A/B/C each override `frame` to new `row`/`col` values.

```ts
{
  id: "grid",
  parent: "root",
  frame: { kind: "anchor", left: 0, right: 0, top: 0, bottom: 0 },
  arrange: {
    kind: "grid",
    columns: [{ kind: "fill" }, { kind: "fill" }, { kind: "fill" }],
    rows: [{ kind: "fixed", size: 120 }, { kind: "fixed", size: 120 }],
    columnGap: 8,
    rowGap: 8,
  },
  variants: [
    {
      when: { maxWidth: 700 },
      arrange: {
        kind: "grid",
        columns: [{ kind: "fill" }],
        rows: [
          { kind: "fixed", size: 120 },
          { kind: "fixed", size: 120 },
          { kind: "fixed", size: 120 },
        ],
        rowGap: 8,
      },
    },
  ],
}

{
  id: "cardA",
  parent: "grid",
  frame: { kind: "cell", row: 0, col: 0 },
  variants: [{ when: { maxWidth: 700 }, frame: { kind: "cell", row: 0, col: 0 } }],
}

{
  id: "cardB",
  parent: "grid",
  frame: { kind: "cell", row: 0, col: 1 },
  variants: [{ when: { maxWidth: 700 }, frame: { kind: "cell", row: 1, col: 0 } }],
}
```

## 10. Implementation plan for M5b

1. Add `GridTrack`, `GridArrange`, `CellFrame` types and new error codes.
2. Update `resolveFrame` to reject `CellFrame` outside grid path (`CellFrameWithoutGrid`).
3. Add an internal grid axis track resolution helper.
4. Add an internal grid child-cell rectangle helper.
5. Extend `resolveLayoutDocument` parent-arranger branch for `arrange.kind === "grid"`.
6. Preserve metadata through existing resolved document/tree/flatten/lerp paths.
7. Add resolver and integration tests.
8. Update docs/README references for new runtime support once landed.

## 11. Test plan for M5b

### 11.1 Types/API

- `GridTrack`, `GridArrange`, `CellFrame` importability/public typing coverage.

### 11.2 Track resolution

- fixed tracks,
- fill tracks,
- weighted fill,
- mixed fixed + fill,
- gap handling,
- padding handling,
- overflow -> `GridOverflow`.

### 11.3 Cell placement

- basic cell placement,
- row/column starts,
- `colSpan`,
- `rowSpan`,
- gaps included across spans,
- fractional fill size propagation.

### 11.4 Validation

- empty rows/columns,
- invalid fixed size,
- invalid fill weight,
- negative or non-finite gaps,
- padding causing negative content,
- invalid row/col values,
- invalid spans,
- out-of-range cell spans,
- non-cell direct child under grid,
- cell child under non-grid.

### 11.5 Integration

- nested grids,
- grid child with its own `StackArrange`,
- grid + offsets,
- grid + sibling-local `z`,
- responsive variant selecting grid arrange and cell frames,
- `lerpResolvedLayouts` between grid-resolved documents.

## 12. Risks and mitigations

- **Risk: scope drift toward CSS Grid.**  
  **Mitigation:** keep explicit forbidden list and grid-specific validation/errors.
- **Risk: ambiguity around child frame legality.**  
  **Mitigation:** enforce strict direct-child `CellFrame` rule with dedicated errors.
- **Risk: hidden behavior via auto-placement pressure.**  
  **Mitigation:** explicit row/col required in all cases.
- **Risk: incompatibility concerns with existing adapters.**  
  **Mitigation:** preserve resolved-rectangle boundary; no adapter coupling.
- **Risk: future extension pressure (`UiLength` tracks, alignment modes).**  
  **Mitigation:** document as deferred v2 topics and keep M5b implementation narrow.
