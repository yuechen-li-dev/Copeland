# Native versus Mermaid dogfood — M20c

## Setup

Both backends used the same semantic Diagram for each graph, a 2560×1440 Standalone window, `VerticalSplit`, a 2384×636 slot (2318×444 Canvas), and Fit mode. Light and dark use the same resolved appearance contract. Machine-readable viewport and native geometry sidecars live beside the captures in `artifacts/m20c`.

## Graph A task

Task: identify compiler/projection/host failures leading to Diagnostics, renderer/emission/artifact failures leading to RendererRecovery, review rejection leading to SourceRepair, and `CacheStale → Emitting`.

Mermaid Fit rendered a 784×535 world at 0.8299. The graph occupied a narrow central region with crossing recovery arcs and labels too small for reliable completion; zoom was required and did not remove path ambiguity. Native Fit rendered 2430×510 at 0.8706. The phase spine and recovery lanes filled the slot, all four requested relationships were directly readable, and neither zoom nor pan was required. Native remains materially better for this motivating case.

## Graph B task

Task: identify the direct operations owned by `RealizeNativeDiagramCard` and confirm repeated cache validation.

Mermaid Fit rendered 579×798 at 0.5564. The ownership set was present, but the tall fan-out used a small fraction of the available width and required zoom for comfortable reading. Native Fit rendered 1432×326 at 1.362. The two-row fan-out filled the half-height slot, all eight direct callees were readable, and `ValidateDerivedCache ×2` answered the repeated-call question without zoom or pan. Native was better for this bounded branching topology.

## Labels, crossings, and pressure

Graph A native materially reduces crossing pressure by separating semantic-repair and renderer-recovery routes; its remaining long return routes are explicit. Dense complete guard text would overwhelm a half-height Canvas, so the qualified task relationships are visible callouts while every exact guard remains edge-associated in SVG `<title>` and geometry/provenance metadata. Graph B uses shared upper/lower rails; overlapping trunk segments represent common direct fan-out without crossing nodes. The only call-count label remains visible at its destination.

## Appearance and artifacts

Graph A native SVG is 19,036 bytes in both appearances; the comparable Mermaid PNG is 53,866 bytes light and 60,403 bytes dark. Graph B native SVG is 7,913 bytes; Mermaid PNG is 37,722 bytes. These are different formats and size is operational evidence, not a quality score. Light/dark keys and artifacts are distinct.

One local qualification run measured Graph A layout at 0.043 ms and SVG emission at 0.171 ms, Graph B layout at 0.042 ms and SVG emission at 0.063 ms, and Graph B full native realization at 29.549 ms cold versus 13.337 ms on a validated cache hit. These are observations, not performance assertions; cache hits perform metadata and inert-SVG validation without layout emission or external process launch.

## Verdict and M20d

Outcome A: the backend qualifies across both real graphs. Production renderer verdict is `NATIVE_QUALIFIED_OPT_IN`, not default, because only phase-state and direct-call topologies have strong comparative evidence and the generic automatic layout is merely safe. Mermaid remains default, fallback, and the renderer for raw Mermaid Cards.

M20d should qualify `automatic-layered-v1` on one third, non-phase, non-star graph with bidirectional or multi-parent edges. Only if that evidence repeats a common layout-intent contract should M20d promote any typed layout declaration. It should not add public `spine`, `lane`, or coordinate semantics now.
