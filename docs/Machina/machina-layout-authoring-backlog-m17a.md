# Machina Layout Authoring Backlog M17a

## Purpose

This backlog records the ordered implementation slices that follow the M17a recon.

It is intentionally staged.

The goal is parity with the stronger JS authoring model over time, without attempting a one-shot port or mixing recon with runtime behavior changes.

## Priority order

```text
P0  M17b  StackArrange + FillFrame authoring parity
P1  M17c  Oblivion card renderer stack refactor
P2  M17d  GridArrange + CellFrame authoring parity
P3  M17e  Oblivion page layout grid refactor
P4  M17f  UiLength proportional/clamp support
P5  M17g  Row variants
P6  M17h  GuideFrame
P7  M17i  DeusMachine parity
```

## P0 StackArrange + FillFrame

Primary goal:

- expose an authoring-first vertical/horizontal stack API over the existing low-level C# stack/fill primitives

Why first:

- it removes the largest current readability tax: vertical `cursorTop` / `currentTop` arithmetic

Target areas:

- `OblivionCardRenderer`
- inspector section shells
- compact and wide pane local composition

Required constraints:

- preserve deterministic lowering
- preserve current runtime behavior until caller migrations happen
- keep direct ids/hit-testing stable enough for later refactors

## P1 Oblivion card renderer stack refactor

Primary goal:

- move `OblivionCardRenderer` onto the new stack authoring path

Why second:

- it is the noisiest current authoring file
- it is where the card footer/body and badge-measurement issues can be simplified

Expected side effects:

- shared visible badge-row computation
- clearer title/subtitle/source/meta/tag/body/footer structure
- better groundwork for later card-specific bug fixes

## P2 GridArrange + CellFrame

Primary goal:

- expose an authoring-first grid/cell API over the existing low-level C# grid/cell primitives

Why after P1:

- page-shell column arithmetic is important, but card-level vertical composition is the larger immediate readability problem

Target areas:

- Oblivion page shell
- future presenter shell slices if the pattern proves worthwhile

## P3 Oblivion page layout grid refactor

Primary goal:

- refactor the wide Oblivion page shell to a declarative grid while preserving independent card-stack and inspector panes

Expected results:

- remove manual column math from page authoring
- preserve pane identity, scroll ownership, and hit-testing

## P4 UiLength proportional/clamp support

Primary goal:

- express proportional dimensions directly in authoring-facing APIs instead of scattered scalar math

Likely uses:

- inspector width policy
- future responsive shell geometry

Not part of P0-P3:

- this is more useful after stack/grid authoring can consume it cleanly

## P5 Row variants

Primary goal:

- add row-level responsive overrides only when a concrete need remains after stack/grid cleanup

Current note:

- M12h already solved the immediate shell-mode problem at the document-factory level

Recommendation:

- do not pull this forward unless one-document responsive maintenance becomes the real next pain

## P6 GuideFrame

Primary goal:

- add cross-node edge-reference placement for overlays, floating panels, or tooltip/popover style UI

Current note:

- current Machina/Oblivion does not urgently need it for the card/page cleanup arc

## P7 DeusMachine parity

Primary goal:

- mirror the JS row-first state-machine surface in C#

Current note:

- valuable for future UI state machines
- separate from the current layout readability arc

## Bugfix candidates

- inspector title clipping
  recommended as a quick fix before or alongside early cleanup, not blocked on full parity
- card footer/body composition and badge-measurement drift
  best fixed during the M17c card renderer stack refactor
- page-column bottom-edge mismatch
  treat as preserved independent-pane behavior unless product intent changes

## Cleanup candidates

- `.slot` id auto-derivation
  high readability payoff, medium id-stability risk
- shared badge-row helpers
  medium payoff, low risk
- generic manifest writer
  medium maintenance payoff, low layout relevance

## Non-goals

This backlog does not imply:

- a one-shot JS port
- a brand-new layout engine
- a broad renderer rewrite
- playback behavior changes
- editor or notebook work
- Aurelian or `VD-MIR` work
