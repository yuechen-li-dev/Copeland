# Oblivion Card Renderer Stack Refactor M17c

## Purpose

M17c refactors `OblivionCardRenderer` internal card composition onto the M17b stack authoring surface.

The goal is renderer-internal readability and card-layout hardening, not a broad page-shell rewrite.

## Relationship to M17a/M17b

M17a identified the real gap as authoring-surface adoption in `OblivionCardRenderer`, plus two card-specific risks:

- body/footer coupling and overlap risk
- `ComputeLayout` versus `LimitLabels` measurement/render drift

M17b then added `UI.Stack(...)`, `UI.VStack(...)`, `UI.HStack(...)`, explicit stack items, and deterministic wrapper ids without migrating Oblivion callers yet.

M17c is the first real renderer migration onto that surface.

M17d follows separately by adding `UI.Grid(...)` as authoring infrastructure only.

The Oblivion page shell is still intentionally deferred to M17e.

## Previous card authoring problem

Before M17c, the main compact-card path in `OblivionCardRenderer` was written as repeated:

- `UI.Anchor(... top: cursorTop, height: X)`
- `cursorTop += X`
- explicit `".slot"` wrapper ids in authoring code

That shape made section ordering visible, but it made layout ownership noisy and encouraged measurement/render drift between body/footer math and final rendered badge rows.

## New stack-authored card composition

M17c now expresses compact-card internal composition as explicit stack-authored sections:

- header stack for title, optional subtitle, optional source, meta row, and optional tags row
- fill body frame for preview/expanded content
- nested body/footer stack inside collapsed preview frames

The page shell is intentionally unchanged.

No `UI.Grid(...)` or page-level grid migration is introduced here.

## Body/footer separation

Collapsed preview cards now use one explicit body-layout stack inside the preview frame:

- fill body text/summary region
- fixed footer region when action/artifact badge rows exist

That removes the previous implicit coupling where footer placement depended on separate arithmetic paths.

The practical outcome is:

- body text no longer paints under footer badge rows
- footer rows no longer paint over preview text
- collapsed Markdown and collapsed plain cards share the same separation model

Expanded Markdown cards keep their existing local scrolling path and do not add a compact footer region while expanded.

## Badge measurement/render consistency

M17c now computes the final visible compact-card footer rows once and reuses that exact row model for both:

- `ComputeLayout(...)` footer measurement
- rendered action/artifact badge rows

That means:

- overflow badges are included in reserved footer height
- measured row count matches rendered row count
- layout is no longer based on raw source counts while rendering is based on limited-plus-overflow counts

## Badge helper cleanup

M17c reduces duplication inside `OblivionCardRenderer` by sharing:

- badge-row rendering
- footer row construction
- collapsed preview frame composition

Cross-file badge helper convergence with `PresenterCard` is still deferred because it was not necessary to land the safe card-specific refactor.

## Behavior preservation

M17c keeps existing product behavior where possible:

- selection behavior is unchanged
- expansion behavior is unchanged
- expanded Markdown local scrolling is unchanged
- inspector behavior is unchanged
- page shell layout is unchanged
- playback scenario semantics are unchanged

Visible changes are limited to the documented card-layout hardening:

- explicit body/footer separation
- compact footer measurement/render consistency

## Playback validation

M17c keeps the M15 and M16 playback safety net intact.

Validation for this milestone includes:

- focused presenter sample tests for stack structure and layout hardening
- playback xUnit suite
- full presenter sample test suite

## Export evidence

Proof artifacts for M17c live under `artifacts/m17c`.

They are deterministic local evidence artifacts, not pixel-golden baselines.

## What changed

- `OblivionCardRenderer` internal compact-card composition now uses stack authoring
- manual `cursorTop` authoring is sharply reduced in the main card path
- manual `".slot"` wrapper authoring is sharply reduced in card-renderer paths
- collapsed Markdown and plain preview bodies now share one explicit body/footer stack
- footer measurement now uses the same final badge-row model as rendering
- focused M17c tests and milestone docs/manifests were added

## What did not change

- no page-shell refactor
- no `UI.Grid(...)` authoring surface
- no `CellFrame` authoring surface
- no broad `OblivionWorkbenchCatalog` page-layout rewrite
- no Markdown editing
- no notebook execution
- no Aurelian work
- no `VD-MIR` work

## Deferred work

- cross-file badge helper convergence with `PresenterCard`
- future authoring-level grid/cell surface work
- future page-shell grid migration
- any broader layout-authoring cleanup outside the card renderer
