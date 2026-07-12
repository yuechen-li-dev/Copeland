# Machina Layout Authoring Parity Closeout M17f

## Purpose

M17f is the doc-only closeout for the M17 Machina layout authoring parity arc.

It closes the stack/grid adoption arc, records the current C# authoring baseline after M17b-M17e, documents the remaining JS parity pressure, and recommends the next milestone direction without changing runtime behavior.

M17f does not change UI/product/playback behavior.

## M18a follow-up

M18a follows the M17f recommendation for a focused layout cleanup and bugfix pass. It fixes the known Oblivion inspector title clipping risk and consolidates duplicated ROALoop-style test setup/helpers without deleting coverage.

M18a does not add new product features or new layout primitives. Future test authors should prefer shared setup helpers where they make tests clearer, but keep behavior assertions readable in the test body.

## M17 arc summary

```text
M17a:
  recon and migration ladder

M17b:
  UI.Stack authoring surface

M17c:
  Oblivion card renderer stack refactor

M17d:
  UI.Grid authoring surface

M17e:
  Oblivion wide page shell grid refactor

M17f:
  closeout and next-step planning
```

The M17 Stack/Grid authoring parity arc is closed.

C# Machina now has authoring-level stack and grid surfaces over the existing layout engine.

The current baseline is:
  UI.Stack for vertical/horizontal composition.
  UI.Grid for pane/table-like composition.
  Existing low-level StackArrange / FillFrame / GridArrange / CellFrame remain the engine.
  Oblivion card internals use stack authoring.
  Oblivion wide page shell uses grid authoring.

Remaining JS parity concepts should be implemented only when concrete pressure exists.

## Original audit complaint

Before M17:
  card and page authoring required manual cursor arithmetic.
  page shell used explicit column math.
  .slot wrapper ids leaked into authoring.
  body/footer and badge measurement/render drift risks existed.
  C# authoring did not expose stack/grid vocabulary ergonomically where needed.

The external audit's highest-value complaint was therefore not "the engine cannot do stack or grid."

It was that the authoring surface still made the intended structure harder to read and harder to maintain than the JS baseline.

## Current authoring baseline

After M17:
  UI.Stack exists.
  UI.Grid exists.
  Oblivion card internals use stack composition.
  Oblivion wide page shell uses grid composition.
  explicit .slot boilerplate is reduced in migrated paths.
  body/footer overlap risk fixed.
  badge measurement/render drift fixed.
  wide page cards/inspector panes are expressed as fill/fixed grid columns.
  playback suite remains passing.

M17f does not mean layout authoring is finished.

It means the highest-value parity gap from the external audit — stack/grid authoring adoption — has landed and is now the baseline.

## Stack authoring baseline

The current stack authoring baseline is:

- `UI.Stack(...)`
- `UI.VStack(...)`
- `UI.HStack(...)`
- `UI.StackItem.Fixed(...)`
- `UI.StackItem.Fill(...)`
- `UiPadding`
- deterministic stack item wrapper ids

Those helpers lower through the existing low-level `StackArrange` and `FillFrame` engine path.

M17f adds no new stack primitive and does not change `UI.Stack`.

## Grid authoring baseline

The current grid authoring baseline is:

- `UI.Grid(...)`
- `UI.GridCell(...)`
- `UI.Track.Fixed(...)`
- `UI.Track.Fill(...)`
- explicit sparse cell authoring
- matrix/2D cell authoring
- deterministic cell wrapper ids

Those helpers lower through the existing low-level `GridArrange` and `CellFrame` engine path.

M17f adds no new grid primitive and does not change `UI.Grid`.

## Oblivion card renderer status

`OblivionCardRenderer` now uses stack-authored internal composition for the migrated compact-card path.

The M17c follow-through materially changed the authoring baseline by:

- reducing visible cursor math
- reducing explicit `.slot` wrapper authoring in migrated card paths
- separating body and footer through one explicit authored structure
- reusing the same final badge-row model for measurement and rendering

M17f does not refactor `OblivionCardRenderer` further.

## Oblivion page shell status

The wide Oblivion page shell now uses grid-authored pane composition.

The M17e follow-through materially changed the authoring baseline by:

- expressing cards as the left fill column
- expressing inspector as the right fixed column
- preserving the existing column gap
- preserving compact mode as its existing deterministic shell path
- preserving independent pane scrolling and playback routing

M17f does not refactor page layout further.

## Playback safety net

Playback xUnit remains the safety net for later layout work.

That doctrine matters because M17f is documentation and planning only:

- no runtime/layout implementation changed here
- later layout cleanup or parity work should continue to use the playback suite as the behavior guardrail
- the closeout baseline assumes the existing playback suite remains the regression net for stack/grid follow-through

## Remaining parity gaps

### Proportional/clamped `UiLength`

Current state:

- proportional/clamp intent still lives mostly in manual scalar math rather than in an authoring-facing `UiLength` surface
- inspector-width and related shell sizing pressure still rely on scalar policy rather than first-class proportional/clamped authoring

Pressure:

- concrete pressure exists now, but it is moderate rather than urgent
- this becomes more valuable if more shell/pane sizing rules need to be authored directly instead of hidden in helper math

Likely value:

- clearer width/height intent
- less scattered `Math.Min` / `Math.Max` policy
- better future fit with stack/grid authoring

Possible milestone:

- `M18a — UiLength proportional/clamp authoring`

### Row variants

Current state:

- wide/compact mode is still document-factory-level
- JS has row-level variant support

Pressure:

- concrete pressure is lower right now
- this should wait unless per-row responsive overrides become a real maintenance need

Possible milestone:

- `M18x — Layout row variants`

### `GuideFrame` / `EdgeRef`

Current state:

- cross-node anchored placement is still not part of the C# authoring baseline

Pressure:

- low for the current Oblivion stack/grid path
- useful later for overlays, tooltips, floating panels, or similar anchored affordances

Possible milestone:

- `M18x — GuideFrame cross-node anchoring`

### `DeusMachine`

Current state:

- not part of layout authoring
- belongs to state-machine/control-surface parity rather than the M17 layout arc

Pressure:

- not concrete for this closeout
- should remain a separate track until UI state-machine parity becomes active pressure

Possible milestone:

- `M18x — DeusMachine state-machine authoring parity`

### Remaining `.slot` / id boilerplate

Current state:

- reduced in stack/grid-authored paths
- still present in manual or legacy anchor paths

Pressure:

- concrete but cleanup-shaped rather than primitive-shaped
- id stability makes broad cleanup riskier than it looks

Possible milestone:

- a targeted layout cleanup pass rather than another primitive milestone

### Remaining manifest-writing boilerplate

Current state:

- still exists
- still repeats JSON/text manifest shaping in places

Pressure:

- concrete maintenance pressure exists
- not layout critical

Possible milestone:

- manifest-writer cleanup as a later maintenance slice

## Remaining cleanup items

- remaining `.slot` / id boilerplate in manual or legacy anchor paths
- remaining manifest-writing boilerplate
- remaining nearby authoring cleanup where stack/grid migration already reduced noise
- remaining page/card cleanup items that are safe to isolate without reopening the M17 arc
- inspector title clipping, which was confirmed in M17a and intentionally not fixed during the stack/grid adoption arc

## Known limitations

- M17 closes the highest-value stack/grid authoring gap, not every JS parity concept
- wide/compact adaptation is still top-level shell selection rather than row variants
- cross-node anchored layout remains deferred until overlay pressure exists
- proportional/clamped `UiLength` authoring is still deferred
- some manual id/manifest boilerplate remains for stability and scope reasons
- playback is the regression safety net, not proof that all future authoring cleanup is free of risk

## Recommended next directions

Primary recommendation:

Option E: Layout cleanup and bugfix pass.

Why:

- stack/grid parity landed
- the biggest audit complaint is already addressed
- the next highest-value work is likely tightening confirmed small bugs and obvious nearby authoring leftovers before adding more primitives

Likely targets:

- inspector title clipping
- remaining `.slot` noise in nearby migrated/manual paths if safe
- badge helper deduplication if still helpful after M17c
- playback artifact dirtiness if still present
- small authoring docs/examples

Alternative if immediate JS primitive parity is preferred:

Option A: `UiLength` proportional/clamp authoring.

Do not implement either in M17f.

## What changed

M17f adds and updates documentation, roadmap state, artifact index state, and a deterministic closeout manifest.

It records that:

- the M17 stack/grid authoring parity arc is closed
- stack/grid authoring parity is now the current C# baseline
- the original external audit complaint has materially changed from "missing ergonomic authoring vocabulary" to "remaining deferred parity/cleanup pressure"
- remaining parity gaps are documented with concrete-now versus later pressure
- playback xUnit remains the safety net for later layout work

## What did not change

M17f is doc-only.

M17f does not:

- implement proportional `UiLength`
- implement row variants
- implement `GuideFrame`
- implement `EdgeRef`
- implement `DeusMachine`
- add new layout primitives
- change `UI.Stack`
- change `UI.Grid`
- refactor `OblivionCardRenderer`
- refactor page layout
- fix inspector title clipping
- clean up manifest writers in code
- change runtime UI behavior
- change playback behavior

## Deferred work

- proportional/clamped `UiLength` authoring
- row variants
- `GuideFrame` / `EdgeRef`
- `DeusMachine` state-machine parity
- remaining `.slot` / id boilerplate cleanup
- remaining manifest-writing boilerplate cleanup
- inspector title clipping and other small layout bugfixes
- remaining page/card cleanup items that do not justify broad refactors
