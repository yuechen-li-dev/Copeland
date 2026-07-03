# Machina Layout Parity Next Steps M17f

## Purpose

This document records the post-closeout decision surface after M17f.

M17f is doc-only.

The stack/grid authoring parity arc is now closed enough to stop treating stack/grid adoption itself as the next blocker.

## M18a result

Option E was selected for M18a. The milestone fixes the Oblivion inspector title clipping risk and consolidates duplicated test helper/setup patterns in `tests/Machina.Presenter.Sample.Tests`.

No coverage was intentionally removed. No new product feature or layout primitive was added. Future tests should use shared setup helpers for repeated state/render/path setup while keeping important assertions explicit and readable.

## Candidate next milestones

- Option A: `UiLength` proportional/clamp authoring
- Option B: row variants
- Option C: `GuideFrame` / `EdgeRef`
- Option D: `DeusMachine` state-machine parity
- Option E: layout cleanup and bugfix pass
- Option F: manifest writer cleanup

## Option A: UiLength proportional/clamp authoring

What it would do:

- add explicit proportional/clamped authoring vocabulary where current scalar sizing math remains manual

Why it matters:

- clearer author intent for width/height policy
- better fit with the new stack/grid authoring baseline

Current pressure:

- concrete, but not the highest urgency

Good reasons to choose it next:

- the goal is immediate continued JS parity primitive work
- upcoming layout work needs author-facing proportional/clamp semantics more than cleanup

Possible milestone name:

- `M18a — UiLength proportional/clamp authoring`

## Option B: Row variants

What it would do:

- add row-level responsive overrides rather than keeping all wide/compact switching at the document-factory level

Why it matters:

- closer JS parity
- useful if one-document responsive authoring becomes the real maintenance pressure

Current pressure:

- lower

Good reasons to choose it next:

- repeated per-row responsive exceptions start appearing
- top-level shell branching becomes harder to maintain than row-local variation

Possible milestone name:

- `M18x — Layout row variants`

## Option C: GuideFrame / EdgeRef

What it would do:

- add cross-node anchored layout references

Why it matters:

- overlays, tooltips, floating panels, and anchored affordances often want this vocabulary

Current pressure:

- low today

Good reasons to choose it next:

- real overlay/floating-panel work is next
- manual cross-node anchoring becomes active pain

Possible milestone name:

- `M18x — GuideFrame cross-node anchoring`

## Option D: DeusMachine state-machine parity

What it would do:

- follow the JS state-machine/control-surface lane rather than the layout lane

Why it matters:

- future UI state-machine authoring parity

Current pressure:

- separate-track pressure, not layout-closeout pressure

Good reasons to choose it next:

- product/runtime work needs row-first state-machine authoring more than layout refinement

Possible milestone name:

- `M18x — DeusMachine state-machine authoring parity`

## Option E: Layout cleanup and bugfix pass

What it would do:

- clean up the most obvious nearby authoring leftovers and fix confirmed small layout bugs without adding new primitives

Likely targets:

- inspector title clipping
- remaining `.slot` noise in nearby migrated/manual paths if safe
- remaining page/card cleanup items
- badge helper deduplication if worthwhile
- small authoring docs/examples
- playback artifact dirtiness if still present

Why it matters:

- the highest-value parity gap already landed
- this improves the current baseline before scope expands again

Current pressure:

- highest concrete pressure now

## Option F: Manifest writer cleanup

What it would do:

- reduce repeated JSON/TXT manifest-writing boilerplate

Why it matters:

- maintenance cleanup
- less repeated milestone plumbing

Current pressure:

- real but lower than layout cleanup
- not layout critical

## Recommended priority

Default recommendation:

1. Option E: layout cleanup and bugfix pass
2. Option A: `UiLength` proportional/clamp authoring
3. Option F: manifest writer cleanup
4. Option B: row variants
5. Option C: `GuideFrame` / `EdgeRef`
6. Option D: `DeusMachine` state-machine parity

Reason:

- M17 already solved the highest-value stack/grid authoring gap
- the current baseline should be tightened before more primitives are added
- if the goal shifts back to immediate JS primitive parity, Option A is the strongest alternative first step

## Decision criteria

Choose Option E next if:

- the goal is to strengthen the current baseline with minimal risk
- known small bugs and cleanup items are now more valuable than new primitives

Choose Option A next if:

- the team wants to continue JS layout primitive parity immediately
- proportional/clamped author intent is the next concrete authoring pain

Choose Options B/C/D only when:

- their specific pressure becomes concrete in real product or sample work

Choose Option F when:

- maintenance cost of repeated manifests becomes more annoying than current layout pressure

## Non-goals

This next-steps document does not implement anything.

It does not:

- reopen the M17 stack/grid adoption arc
- claim layout authoring is finished
- change runtime behavior
- change playback behavior
- choose a milestone irreversibly
