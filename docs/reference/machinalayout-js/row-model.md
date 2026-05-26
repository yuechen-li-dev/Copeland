# Row Model

`LayoutRow[]` is the canonical authoring model.

## Why rows

Rows are:

- easy to read,
- easy to diff,
- easy to patch,
- easy to serialize,
- easy for humans and LLMs,
- naturally table/record-shaped.

> Nesting is an output shape, not an authoring strategy.

## Row fields

- `id`: stable node identifier.
- `parent`: parent node id (omit only for root).
- `order`: sibling order key.
- `frame`: geometry primitive (`absolute`, `anchor`, `fixed`).
- `arrange`: optional arrangement strategy (currently `stack`).
- `slot`: renderer view key.
- `debugLabel`: optional debug-facing label.
- `z`: optional sibling-local paint layer metadata.

## Parent-child meaning

Parent-child means only:

- resolve this child rectangle in the parent coordinate space.

It does **not** imply:

- component ownership,
- state ownership,
- event ownership,
- routing hierarchy,
- semantic DOM hierarchy,
- styling inheritance.

## Deterministic sibling order

Siblings are ordered by:

1. `order ?? 0`
2. original row index (tie-break)

This keeps compile and resolve deterministic.

## Compile validation guarantees

`compileLayoutRows` rejects invalid row graphs:

- exactly one root required,
- duplicate ids rejected,
- unknown parents rejected,
- cycles rejected,
- invalid `z` rejected.

## Compact row table example

| id               | parent  | order | frame                                | arrange | slot          | z |
|------------------|---------|-------|--------------------------------------|---------|---------------|---|
| root             | —       | 0     | absolute `{x:0,y:0,w:1024,h:640}`   | —       | —             | — |
| header           | root    | 0     | anchor `{left:0,right:0,top:0,h:64}`| —       | `header`      | 0 |
| sidebar          | root    | 1     | anchor `{left:0,top:64,bottom:0,w:240}` | —   | `sidebar`     | 0 |
| toolbar          | root    | 2     | anchor `{left:240,right:0,top:64,h:56}` | stack | —             | 0 |
| toolbar-button-1 | toolbar | 0     | fixed `{w:120,h:40}`                | —       | `toolbarButton` | 0 |

- `offset`: optional post-placement local translation (`OffsetSpec` using `UiLength`), not margin and does not affect siblings.


## Root row frame guidance (M3a)

Use `RootFrame` on the root row. Root geometry is sourced from caller `rootRect`, not from row frame numbers.

- Root `FillFrame` is invalid (`FillFrameWithoutArranger`).
- Root `FixedFrame` is invalid (`FixedFrameWithoutArranger`).
- Root `AbsoluteFrame` and `AnchorFrame` remain accepted for compatibility, but `RootFrame` is preferred.
