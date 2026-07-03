# Oblivion Page Grid Refactor M17e

## Purpose

M17e moves the wide Oblivion page shell onto the existing `UI.Grid(...)` authoring surface.

The goal is readability, not a new runtime feature:

- left fill pane = card stack
- right fixed pane = inspector
- column gap = page gap

Readers should not have to manually simulate:

- `cardsColumnWidth = contentWidth - inspectorWidth - gap`
- `inspectorLeft = cardsColumnWidth + gap`

## Relationship to M17a-M17d

- M17a identified page-shell column math as one of the remaining parity/readability gaps.
- M17b added authoring-level stack helpers.
- M17c used that stack surface inside `OblivionCardRenderer`.
- M17d added authoring-level `UI.Grid(...)`, `UI.GridCell(...)`, `UI.Track.Fixed(...)`, and `UI.Track.Fill(...)`.
- M17e is the wide page-shell migration onto that already-existing grid surface.

## Previous page layout problem

Before M17e, the wide Oblivion shell authored two separate anchored panels and manually supplied:

- cards column width
- inspector width
- page gap
- inspector left offset

That behavior worked, but the authoring path made page structure harder to read than the equivalent JS layout intent.

## New grid-authored wide page shell

Wide mode now expresses the page shell as one grid-authored wrapper:

```csharp
UI.Grid(
    id: "...page-grid",
    columns:
    [
        UI.Track.Fill(1),
        UI.Track.Fixed(inspectorWidth),
    ],
    rows:
    [
        UI.Track.Fill(1),
    ],
    columnGap: pageGap,
    children:
    [
        UI.GridCell(row: 0, column: 0, child: cardsPane),
        UI.GridCell(row: 0, column: 1, child: inspectorPane),
    ]);
```

M17e uses existing `UI.Grid`.

M17e does not implement new grid features.

## Wide mode behavior preservation

Wide mode still preserves:

- main card stack in the left fill pane
- inspector in the right fixed pane
- independent scroll offsets
- stack scrollbar
- inspector scrollbar
- raw source scrollbar
- expanded Markdown body scrollbar
- partial viewport culling
- playback target routing

## Compact mode preservation

Compact mode is intentionally preserved rather than broadly reworked.

- compact list and compact inspector remain deterministic shell modes
- compact playback behavior remains on the existing path
- M17e does not force compact mode onto grid just for symmetry

## Independent pane model

The M15 pane doctrine is unchanged:

- selection couples stack and inspector content
- scrolling does not
- the main stack and inspector remain independent panes
- expanded Markdown remains inline in the stack
- raw Markdown source remains locally scrollable in the inspector

## Playback validation

Playback remains the safety net for this refactor.

M17e is validated against:

- starter playback scenarios
- regression playback scenarios
- targeted wide/compact page-shell tests

## Export evidence

Representative proof artifacts live under `artifacts/m17e`:

- `m17e-oblivion-wide-grid-docs-1280x720.png`
- `m17e-oblivion-wide-grid-expanded-1280x720.png`
- `m17e-oblivion-wide-grid-inspector-scrolled-1280x720.png`
- `m17e-oblivion-compact-preserved-960x540.png`
- `oblivion-page-grid-refactor-manifest.json`
- `oblivion-page-grid-refactor-manifest.txt`

These are proof artifacts, not pixel-golden baselines.

## What changed

- refactored the wide Oblivion page shell to author through `UI.Grid(...)`
- reduced explicit two-column page math in the authoring path
- preserved existing semantic pane ids by wrapping the card and inspector panes inside grid cells
- added M17e tests, docs, exports, and manifest records

## What did not change

M17e does not:

- add a new low-level layout engine
- add new `UI.Grid` primitives
- add `GuideFrame`
- add row variants
- add proportional `UiLength`
- add `DeusMachine`
- redesign the inspector
- redesign the presenter shell
- add Markdown editing
- add notebook or Roslyn execution
- perform Aurelian work
- perform `VD-MIR` work

## Deferred work

Deferred after M17e:

- any proportional/clamped page-width policy beyond the current scalar inspector-width policy
- any compact-shell migration to grid if and when it becomes materially helpful
- pane resizing
- guide-frame style layout aids
- row variants
