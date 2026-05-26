# GridArrange runtime (M5b)

`GridArrange` adds explicit deterministic 2D placement to MachinaLayout. It places direct children into declared rows/columns and resolves normal rectangles for downstream renderers.

## Types

- `GridTrack`: `{ kind: "fixed", size }` or `{ kind: "fill", weight? }`
- `GridArrange`: `{ kind: "grid", columns, rows, columnGap?, rowGap?, padding? }`
- `CellFrame`: `{ kind: "cell", row, col, rowSpan?, colSpan? }`

## Behavior

- Only direct children of a grid parent may use `CellFrame`.
- Grid computes a content rect from parent rect minus padding.
- Fixed tracks consume fixed space; fill tracks split remaining space by weight.
- Gaps are explicit; no justify, no auto-gap expansion.
- Cell spans include internal gaps.
- Offset is applied after cell placement using existing `applyOffset` semantics.

## Validation / error codes

- `InvalidGridTrack`: invalid columns/rows/track values/gaps.
- `InvalidGridCell`: invalid row/col/spans or out-of-range.
- `GridChildMustBeCell`: grid parent has non-cell direct child.
- `CellFrameWithoutGrid`: cell resolved outside grid arranger path.
- `GridContentNegative`: padding makes content width/height negative.
- `GridOverflow`: fixed tracks + gaps exceed content size.

## Example

```ts
{
  id: "root",
  frame: { kind: "root" },
  arrange: {
    kind: "grid",
    columns: [{ kind: "fixed", size: 100 }, { kind: "fill" }],
    rows: [{ kind: "fixed", size: 40 }, { kind: "fill" }],
    columnGap: 8,
    rowGap: 8,
    padding: 8,
  },
}
```

## Limitations

- Not CSS Grid compatible.
- No auto-placement.
- No implicit tracks.
- No template areas/named lines/minmax/subgrid.
