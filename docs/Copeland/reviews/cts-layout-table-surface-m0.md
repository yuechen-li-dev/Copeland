# CTS-LAYOUT-TABLE-SURFACE-M0 review

CTS-LAYOUT-TABLE-SURFACE-M0 adds `csv overlay Name { ... }` to streams. The
feature makes the sibling-box relation visible without creating an embedded
data format or a second layout path.

## Grammar and schema

The chosen grammar is visibly CSV-shaped but intentionally uses semicolon row
termination:

```ts
csv overlay root {
    name, content, x, y, width, height, layer, z;
    page, Page(), 0px, 0px, 320px, 180px, content, 5;
}
```

One header is required. `name`, `content`, `x`, `y`, `width`, and `height` are
required exactly once; `layer` and `z` are optional and default through the
normal containing-layer / zero-z laws. Column order is unconstrained. M0
supports only the explicit sibling-box `overlay` profile.

Every non-header cell is an ordinary `ExpressionSyntax`. The parser sees the
commas of `Dialog(title, description)` as call-expression punctuation, not
table separators. The name cell binds as a normal semantic identifier; content
binds with the normal ReactNode binder; coordinates and dimensions use the
existing length/dimension binders; layer and z reuse the existing paint binder.

## One model, two surfaces

The table binder creates the existing `BoundLayoutNode` overlay and slot nodes,
then the ordinary stream binder creates ordinary `BoundLayoutBindingEntry`
instances. It neither emits/reparses source text nor stores an untyped runtime
table. Exact layout-type inference, normalization, React projection, generated
CSS, and the browser host therefore use the same graph as nested authoring.

Row names are stable slot identities. Rows retain lexical order while binding;
normalization assigns that order as the final paint tie-breaker. Reordering rows
only changes the intended equal-layer/equal-z ordering.

## Diagnostics and editor support

Stable `COPE-LAYOUT-TABLE-*` diagnostics cover malformed headers, duplicate or
unknown columns, missing required columns, row arity, duplicate/invalid names,
invalid coordinate/dimension/layer/z cells, unsupported containers, and
non-renderable content. The language server completes `csv`, indexes table
containers and row names, exposes header/container/row/cell semantic tokens,
and retains normal definition navigation for content expressions and layers.
Because the ordinary compiler compiles unsaved document overlays, table
diagnostics update through the normal LSP path.

## Intentional M0 limits

No RFC CSV, string bodies, CSV files, runtime rows, formulas, arbitrary
computed columns, grid/flow/memory profiles, keyed reconciliation, spreadsheet
UI, visual canvas, responsive rules, or intrinsic-measurement additions are
included. The next semantic question before website migration is whether an
explicit table block should gain a constrained `grid` profile, or whether the
normalized box-table inspector should come first to establish that profile from
real authoring evidence.
