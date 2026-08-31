# Native layout qualification — M20c

## Graph A: phase lanes

Graph A is the unchanged `DiagramCardRealizationFlow` source from M20a/M20b: 16 nodes, 31 edges, 24 guarded transitions, semantic fingerprint `a7c7e007bdc4faedb589cd23f5b3f6269cdee589ee635af3c426b4338c68b5d1`. Policy `phase-lanes-v1` retains the experiment's primary phase spine, diagnostics lane, source-repair lane, renderer-recovery lane, explicit semantic order, and alignment. Those declarations live only in `Oblivion.App` layout policy.

Resolved Graph A is 2430×510. In the 2318×444 Diagram Canvas it fits at 0.8706. The constrained labels required by the reading task are explicit callouts; every complete transition/guard label remains attached to its exact edge in the SVG accessibility title and metadata. Stable transition order controls path layering.

## Graph B: direct call ownership

Graph B is `NativeDiagramRealizationCalls`, a bounded real semantic source using existing `reflect callsOf<RealizeNativeDiagramCard>()` and `callGraphDiagram`. It has 9 nodes and 8 aggregated direct-call edges; the two cache-validation calls remain visible as `×2`. It answers: which operations are directly coordinated by native Diagram Card realization, and which concerns remain outside that boundary? Grep can find the names separately, but it does not present the bounded ownership set or repeated call count as one stable artifact.

Its topology is a branching fan-out rather than ordered phase progression with recovery loops. Policy `branching-calls-v1` uses two node rows and upper/lower routing rails. It does not reuse Graph A placements or lanes. Resolved Graph B is 1432×326 and fits at 1.362 in the same half-height slot; no zoom or pan is required.

## Policy and determinism

Policy identities are `phase-lanes-v1`, `branching-calls-v1`, and `automatic-layered-v1`. The automatic path is deterministic, bounded, non-crashing for small graphs, and selected only when neither qualified family applies. Unsupported policy names produce a diagnostic rather than nonsense geometry.

Nodes use stable Diagram identity order. State edges retain semantic transition order; flowchart/call edges retain the existing canonical Diagram order. Coordinates use invariant numeric formatting. Repeated resolution and emission for both graphs are structurally and byte identical. Metadata records node rectangles, edge routes, label anchors, world bounds, policy identity, renderer identity, semantic ownership, and appearance.

## Copeland TS layout reuse

M20c reuses Copeland's semantic Diagram normalization, stable node/edge order, existing template evaluation, `callsOf`, `callGraphDiagram`, and Mermaid emission. It does not force the project/table layout materializer into Diagram geometry: that machinery resolves project trees and authored tables, while native Diagram routing needs edge paths and label anchors. Forcing it would create an adapter larger than the bounded renderer. The exact mismatch is recorded here; no second semantic table system and no Diagram IR layout API were added.

## Generalization result

`order` generalized through stable semantic ordering. `alignment` generalized as a resolved-layout concern. `spine` and `lane` remain useful only inside the Graph A policy; Graph B instead established bounded branching rails. None should become permanent Diagram IR in M20d without another topology proving a common contract.
