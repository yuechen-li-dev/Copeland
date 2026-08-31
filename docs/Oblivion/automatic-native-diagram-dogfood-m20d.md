# Automatic native Diagram dogfood — M20d

## Graph C and real question

Graph C is `DiagramRealizationArchitecture`, a real semantic architecture graph authored with the existing `diagram`, `diagramNode`, and `diagramEdge` intrinsics. It asks: where do Mermaid and native Diagram realization diverge, where do they reconverge, and which product responsibilities remain shared after rendering?

The graph has 14 nodes, 19 labeled edges, five multi-parent nodes, one bidirectional pair, maximum in-degree three, maximum out-degree three, 33 labels, and maximum node-label length 19 characters. It is neither a phase progression nor a star: two renderer branches cross-link and reconverge through derived cache, provenance, source correlation, content realization, and the shared Diagram Canvas. Canvas and viewport state form the two-way pair. The primary proof contains no graph-specific layout declaration.

## Constrained Fit comparison

All captures use the real Standalone host at 2560×1440, `VerticalSplit`, a 2384×636 Diagram slot, a 2318×444 Canvas, and Fit.

| Backend | World | Aspect | Fit scale | Crossing pressure | Zoom | Task result |
|---|---:|---:|---:|---:|---|---|
| Mermaid 11.16.0 dark PNG | 784×76 | 10.316 | 2.9566 | about 4 manually visible branch/reconvergence crossings | Helpful for exact edge-label reading | Core branches are visible at Fit, but upscaled raster labels and the central reconvergence are less certain. |
| automatic native dark/light | 2340×252 | 9.286 | 0.9906 | 1 deterministic adjacent-layer estimate | No | Both branches, shared obligations, three-way content reconvergence, and Canvas/viewport return edge are answerable at Fit. |

The automatic quality classification is exactly `GOOD_DEFAULT`. Native uses nearly the full slot width at approximately 1:1 scale; labels remain sharper than Mermaid's substantially upscaled raster. The native central edge labels are compact and some trunks share space, but relationships remain traceable through distinct routes, arrow direction, edge titles, and source IDs. No explicit-native Graph C variant was needed: the automatic result already answered the task, so an authored comparison would add evidence volume without changing the decision.

## Graph A/B automatic regression

| Graph | Mermaid | Automatic native | Specialized native | Fit verdict |
|---|---|---|---|---|
| A — phase flow | 784×535, Fit 0.8299; zoom required for the recovery task | 432×1580, Fit about 0.281; semantics preserved, zoom required | 2430×510, Fit 0.8706; task completes without zoom | Specialized layout remains materially necessary. |
| B — direct-call fan-out | 579×798, Fit 0.5564; zoom helpful | 574×852, Fit about 0.521; semantics preserved, zoom required | 1432×326, Fit 1.362; task completes without zoom | Specialized layout remains materially better. |
| C — reconvergent architecture | 784×76, Fit 2.9566; task broadly answerable, exact paths benefit from zoom | 2340×252, Fit 0.9906; task completes without zoom | Not produced | Automatic is the useful baseline. |

Automatic therefore proves a useful new topology without proving that specialized layout is rare. Across the three current graphs, specialized presentation remains common for phase/cycle and high-fanout cases, while automatic is sufficient for the reconvergent layered graph.

## Performance, artifacts, and product decision

One focused local run measured Graph C layout at 23.209 ms, SVG emission at 1.294 ms, cold native realization at 29.203 ms, validated cache hit at 11.573 ms, and 13,287 SVG bytes. These are observations, not micro-benchmarks; each is comfortably interactive.

The renderer/default verdict is exactly `KEEP_NATIVE_OPT_IN`. M20c's qualified native policies remain available, Graph C establishes automatic native as a good backend default when explicitly selected, and raw Mermaid remains Mermaid. Evidence from A/B prevents `NATIVE_DEFAULT_FOR_COMPILER_DERIVED` or `NATIVE_DEFAULT_FOR_ALL_DIAGRAM_IR`.

Graph C dark/light captures, Mermaid comparison, viewport sidecars, canonical SVG, provenance, and resolved layout live under `artifacts/m20d`. Every node/edge retains stable `data-node-id`/`data-edge-id`, accessibility titles, source identity, workspace/Page/Card ownership, semantic fingerprint, appearance, renderer identity, policy identity, producer, and `Derived = true`.
