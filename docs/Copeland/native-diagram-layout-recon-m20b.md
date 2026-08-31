# Native Diagram layout recon — M20b

## Boundary and hypothesis

This is an experimental Standalone-only backend selected explicitly with `--diagram-backend native-experiment`. Mermaid remains the production/default renderer.

The recon compiles the exact M20a `DiagramCardRealizationFlow` semantic graph through the existing `StateMachineDiagramProjection`. The result remains 16 nodes, 31 edges, and semantic fingerprint `a7c7e007bdc4faedb589cd23f5b3f6269cdee589ee635af3c426b4338c68b5d1`. `DiagramNode`, `DiagramEdge`, source correlation, and reflection behavior are unchanged.

The experiment keeps four layers separate:

```text
Diagram IR
  -> explicit layout declaration
  -> resolved node/orthogonal-route geometry
  -> light/dark SVG and raster proof
  -> shared Diagram Canvas camera
```

It reuses the ordinary Copeland/Machina explicit `UI.Layer`, `UI.At`, `UI.Rect`, and `UI.Text` layout path for raster qualification. Coordinates live only in the experimental layout declaration.

## Explicit layout

The primary phase spine orders intake, compiler, renderer, artifact, projection, host, review, and acceptance from left to right. `Diagnostics` occupies a semantic repair lane. `SourceRepair` and `RendererRecovery` occupy separate lower lanes. Eleven task-relevant failure/recovery labels are placed explicitly; remaining edge labels stay preserved in SVG `<title>` and semantic attributes.

The native world is 2460×510 (aspect 4.8235), deliberately matching the wide half-height slot. In the 2318×444 canvas Fit scale is 0.8706, giving an effective 13.9 px node-label size from the 16 px source size. The Mermaid world is 784×535 (aspect 1.4654), fits at 0.8299, and leaves most horizontal slot area unused while retaining a dense crossing/ranking pattern.

Exact crossing counts were not promoted into a graph-analysis subsystem. Visual review shows the native spine/lane routes make the four requested destinations and `CacheStale -> Emitting` label materially easier to find; some long orthogonal recovery routes remain and are acceptable recon pressure.

## Exact task replay

| Mode | Result |
|---|---|
| Mermaid Fit | Global shape visible; labels too small for the task. |
| Mermaid 1.8× + pan | Local labels readable; repeated navigation and crossing ambiguity remain. |
| Native Fit | Phase order, Diagnostics, RendererRecovery, SourceRepair, and CacheStale recovery label are visible together. |
| Native zoomed | Available through the same camera; not required to complete the task. |

Pan/zoom alone does not rescue Mermaid for the motivating half-height task. The native layout is materially better because of `spine`, `lane`, `order`, and `alignment`; these are evidence for M20c, not additions to Diagram IR in M20b.

Renderer verdict: `NATIVE_LAYOUT_MATERIALLY_BETTER`.

## M20c recommendation

Run a bounded production-backend investigation that tests only the smallest separate layout vocabulary—`spine`, `lane`, `order`, and `alignment`—against this graph and one additional real dense graph. Keep Diagram IR unchanged until both cases prove the vocabulary; do not replace Mermaid before cache/provenance, accessibility, edge-label, and appearance parity are qualified.
