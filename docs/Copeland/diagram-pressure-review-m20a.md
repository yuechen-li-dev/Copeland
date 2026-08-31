# Diagram pressure review — M20a

## Decision

Outcome B: the dense semantic graph is useful, but one concrete layout/fitting problem is now material. Mermaid remains the production backend in M20a; no renderer rewrite or Diagram IR change landed.

Renderer verdict: `MERMAID_LAYOUT_LIMIT_NOW_MATERIAL`.

Native SVG decision: `NATIVE_SVG_RECON_JUSTIFIED`.

## Diagram IR pressure

| Desired semantic capability | Pressure | Evidence |
| --- | --- | --- |
| semantic phase/recovery grouping | REPEATED | compiler phases, source repair, and derived recovery are meaningful categories but cannot be expressed as owned groups |
| explicit phase-spine ordering | REPEATED | source transition order exists, but the desired main lifecycle spine is not a first-class node-order relationship |
| edge categories | REPEATED | success, semantic failure, renderer failure, retry, and cancel relationships are semantically distinct |
| recovery lanes | REPEATED | `Diagnostics` and `RendererRecovery` are meaningful convergence lanes rather than cosmetic boxes |
| hierarchical identity | MINOR | current stable flow/state/transition identities remain sufficient for source correlation |
| importance/weight | MINOR | the happy path should dominate visually, but the graph remains representable without weight |
| edge annotations | NONE | existing event and guard labels preserve the required semantic facts |

None is blocking. Adding generic boxes, coordinates, or backend hints in M20a would conflate semantic ownership with layout. Diagram IR remains unchanged.

## Mermaid backend pressure

| Backend/layout limitation | Classification | Evidence |
| --- | --- | --- |
| large graph fitting | BLOCKING | the complete graph fits, but detailed labels are not comfortably readable in the expanded 2560×1440 Card without zoom |
| long recovery-edge crossings | ANNOYING | routes into `Diagnostics`, `RendererRecovery`, and terminal states cross or pass through the central reading area |
| poor rank/order control | ANNOYING | TD scatters recovery relationships around the phase chain; source transition order cannot impose the desired spine |
| label placement | ANNOYING | several guarded labels sit close to unrelated long paths, slowing target-edge association |
| excess whitespace | ANNOYING | the 784×535 TD aspect fitted into the wide Card leaves most horizontal space unused |
| group rendering | ANNOYING | Mermaid cannot render the desired semantic lanes because the current IR intentionally has no groups |
| layout instability | TOLERABLE | repeated rendering was deterministic; the problem is the stable layout itself |
| theme mismatch | TOLERABLE | light and dark are both coherent and preserve identical geometry |

## Ordering pressure

The semantic model already has meaningful transition declaration order, and the lifecycle has a meaningful phase spine from `WorkspaceIntake` through `HumanReview`. TD communicates that sequence better than LR. LR produced a 1568×308 strip with worse route tracing, so no direction change is recommended.

The smallest justified future ordering form would describe semantic phase succession or a primary path, not coordinates. It is not implemented because the graph remains honestly representable and M20b should first determine whether a backend can use existing order and identities.

## Grouping pressure

Desired groups are semantic: compiler phases, semantic/source repair, derived renderer/cache recovery, and terminal review outcomes. They are not generic visual clusters. Grouping pressure is repeated but not blocking; M20a does not add groups.

## Label pressure

The longest label is 40 characters, and the labels accurately combine concise event names with compact boolean guards. The semantic names are not excessively verbose, and the projection formatter is not adding redundant detail. The failure is Mermaid placement and fit-to-Card scaling. Labels were not shortened to hide that backend pressure.

## Edge aggregation and external/dynamic calls

This is a state graph, so call-site `×N` aggregation and Direct/External/Dynamic presentation do not apply. External node count and dynamic edge count are both zero.

## Source correlation

All 16 states and 31 transitions retain the maintained source path and nonzero compiler coordinates. The focused M20a test compiles the real vault source, projects the semantic view twice, verifies deterministic Mermaid/fingerprint output, and asserts correlation for every state and transition. No click-through UI was added.

## Native SVG decision and M20b

`NATIVE_SVG_RECON_JUSTIFIED` does not authorize a production renderer. The exact failure to investigate is whether the same unchanged Diagram IR can be arranged as one readable primary phase spine with two recovery lanes, without crossing the spine with every failure path and without shrinking 40-character labels below comfortable Card reading size.

Exact M20b scope:

1. Consume this exact checked-in M20a Diagram IR/semantic record; add no reflection query and no new Card kind.
2. Build one bounded, non-production SVG layout recon with a primary phase spine, semantic/source-repair lane, and derived-renderer-recovery lane.
3. Render the recon in the existing expanded Card at 2560×1440 in light and dark.
4. Compare label size, route crossings, used Card area, and human/agent task completion against the M20a Mermaid proofs.
5. Stop after the comparison. Do not replace Mermaid unless the recon materially improves the exact failed task with unchanged semantics.

The desired next semantic projection remains sequence/interaction ordering for temporal phase handoffs. It is recorded only; M20a and the recommended M20b do not implement it.
