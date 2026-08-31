# Dense compiler-derived Diagram Card dogfood — M20a

## Selected real diagram

M20a uses `DiagramCardRealizationFlow`, a maintained semantic description of the current compile-to-Diagram-Card lifecycle. It records source intake, compiler phases, Mermaid selection and emission, artifact/cache qualification, Card projection and hosting, human review, semantic source repair, derived-renderer recovery, and accepted/rejected outcomes.

This was selected over the existing direct call-graph sample and the small VehicleFlow/PantryFlow state machines. The call sample is a shallow six-node fixture, while the existing flows do not reach dense-graph pressure. The selected flow answers a current design/debugging question: which failures require semantic source repair, and which failures belong to derived rendering and cache recovery? It is not a size-only torture graph.

## Semantic source and bounded projection

The first-class Diagram Card points to `source/DiagramCardRealizationFlow.ts` inside the maintained `M20aDenseDiagram.oblivion` vault. `OblivionDiagramCardRealizer` compiles the file, obtains the existing syntax-free `StateMachineSemanticView`, projects the unchanged Diagram IR, emits Mermaid, and sends it through the appearance-qualified renderer and mature Diagram Card host.

The projection contains 16 nodes, 31 edges, 24 guarded transitions, 16 distinct node labels, 25 distinct edge labels, and 41 distinct labels overall. The longest label is 40 characters: `RendererUnavailable [available == false]`. There are no external nodes or dynamic edges. These values remain far below the existing 256-state and 1,024-transition bounds.

No reflection query was added. Diagram IR is unchanged. The compiler's rule against two transitions for one event in a state led the source to use explicit success/failure event names; FLOW semantics were not widened.

## Human-use task

Question: Which failures require source repair, and which require renderer recovery?

Result: parse, bind, lower, projection, and host failures converge on `Diagnostics`, which can retry Card projection or move to `SourceRepair`. A rejected human review moves directly to `SourceRepair`. Renderer unavailability, emission failure, and corrupt artifacts converge on `RendererRecovery`; a stale cache returns directly to `Emitting`. Renderer recovery can retry emission or fall back to diagnostics. The diagram exposed those convergence hubs and the cache-stale bypass faster than reading the 31 transition declarations serially.

The image did not replace source navigation for exact route verification. At Card scale, several guard labels require zoom and long recovery arcs make an individual return path slower to follow than a targeted source search.

## Agent-use task

Codex used the diagram to verify that semantic repair and derived recovery remain separate and to discover the direct `CacheStale -> Emitting` loop without repeatedly navigating the source. It also made the three high-fan-in areas (`Diagnostics`, `RendererRecovery`, and `Rejected`) obvious.

The agent still needed the Mermaid/source record to verify each label and source location. For exact edge inspection, the dense Card was slower than grep. The diagram helped with global structure but not detailed trace reading.

## Light and dark qualification

Both 2560×1440 captures use the exact semantic fingerprint `a7c7e007bdc4faedb589cd23f5b3f6269cdee589ee635af3c426b4338c68b5d1` and Mermaid source hash `179831cfeee1e9e8892ff5e5914c8a642684ca0726b78493a5e9f4bdf8473ccd`.

- Light cache key: `3b79ae942ab037c0cc0a0ad33bc39ec1930c55a4f0b29544324cc737e3252da7`.
- Dark cache key: `120069e7e334c698105ef3e4541e7d08ecb0ad1c8cf3dfa740e91a63ce6ddc69`.

Both standalone runs reported page extent 1440, viewport 1421, and offset 0. Light and dark preserve the same geometry. Contrast is adequate in both; dark has no white-canvas island. Theme mismatch is not the pressure.

## Readability and Card sizing

Top-down communicates the main phase sequence better than left-right. The bounded LR experiment produced an extreme 1568×308 strip with longer horizontal routes and worse traceability, so TD remains the correct direction.

The qualified TD artifact is 784×535. Fitting that aspect ratio into the wide, fixed-height expanded Card uses only a minority of the available width, leaves generous horizontal whitespace, and makes labels small. The graph fits without clipping or local scrolling, but useful detailed reading needs zoom. Pan/zoom was not added.

A 3× internal-resolution experiment produced 2352×1605 output. It improved raster sharpness but retained the same aspect ratio and logical fit, so it did not make labels larger inside the Card. The problem is layout/fitting, not source-label verbosity or insufficient PNG resolution. The 920px-class expanded body policy is adequate for small diagrams but inadequate for this dense TD graph.

## Conclusion

Outcome B. The semantic visualization is useful, and the unchanged Diagram IR represents it honestly, but Mermaid's ranking/routing plus fit-to-Card now materially limits detailed reading.

Renderer verdict: `MERMAID_LAYOUT_LIMIT_NOW_MATERIAL`.

Native SVG decision: `NATIVE_SVG_RECON_JUSTIFIED`.

M20a does not replace Mermaid or implement native SVG. M20b should be a bounded recon against this exact Diagram IR and Card: test whether a phase spine plus semantic repair and renderer-recovery lanes can keep labels readable at 2560×1440. No production backend, new reflection, new Card kind, pan/zoom, or speculative IR expansion should be included.
