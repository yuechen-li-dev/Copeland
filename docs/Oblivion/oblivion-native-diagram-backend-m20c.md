# Oblivion production native Diagram backend — M20c

## Verdict

M20c qualifies `native-svg-v1@1.0.3` as a production-capable, opt-in renderer for compiler-derived semantic Diagram Cards. The renderer verdict is `NATIVE_QUALIFIED_OPT_IN`. Mermaid remains the default and the automatic fallback; raw Mermaid authoring is unchanged.

## Production architecture

The existing `Copeland.TS.Templates.Diagram` remains semantic truth. `OblivionNativeLayoutPolicy` is presentation intent, `OblivionResolvedDiagram` owns concrete node rectangles, edge routes, and label anchors, and `OblivionNativeDiagramSvgEmitter` produces the derived SVG. None of these types were added to Diagram IR. `OblivionNativeSvgRenderer` owns cache/provenance orchestration in `Oblivion.App`; `Oblivion.Avalonia` projects the same resolved geometry as vector controls into `AvaloniaOblivionDiagramCanvas`. The Canvas owns Fit, Zoom, Pan, and Reset and never calls the renderer during camera movement.

The semantic source boundary now admits the already-existing bounded Copeland template-to-Diagram projection. `copeland-flow/state` behavior is unchanged; `copeland-template/diagram` evaluates an ordinary bound template such as `callsOf<F>()` plus `callGraphDiagram`. No Diagram IR type or reflection query changed.

## SVG and security

Native output has an explicit `viewBox`, width and height, stable node and edge groups, deterministic paths and text, an overall `<title>` and `<desc>`, and per-node/per-edge `<title>` elements. `data-node-id`, `data-edge-id`, and `data-source-identity` preserve correlation without embedding absolute source paths. Source labels are XML-escaped. Output is bounded to 2 MiB and contains no JavaScript, external resources, links, remote images, `foreignObject`, HTML, WebView, Chromium, Cef, or browser renderer.

Node size derives from deterministic conservative label metrics, padding, and minimum/maximum width. The typography stack is `Segoe UI, sans-serif`; no font is shipped. Exact glyph rasterization can vary across platforms, but resolved geometry and SVG bytes do not.

## Appearance

The renderer accepts the existing `OblivionResolvedAppearance` contract. Light uses a white surface, slate text/edges, and blue node borders. Dark uses the established slate surface with light text, muted edges, and cyan node borders. Appearance is part of both the cache key and provenance. The light and dark Graph A captures prove there is no white-canvas island in dark mode.

## Cache and provenance

The SHA-256 native key includes semantic fingerprint, `native-svg-v1@1.0.3`, layout-policy identity, resolved appearance, `svg`, and fixed canonical/security/font/output options. Metadata format 1 stores the key, full derived provenance, and resolved geometry. Validation rejects missing halves, unreadable metadata, provenance/owner mismatch, appearance mismatch, policy mismatch, malformed SVG, unsafe SVG, and oversized SVG. A corrupt entry is diagnosed and rebuilt. Native and Mermaid renderer identities, extensions, directories, and keys are distinct.

Provenance records semantic hash/reference, renderer identity/version, render operation, output format, resolved appearance, layout identity through render options, producer, workspace/Page/Card/content ownership, and `Derived = true`.

## Fallback and inspection

`OblivionFallbackDiagramRenderer` reports the native diagnostic and then invokes Mermaid. The returned artifact and provenance remain Mermaid-owned; fallback never reports false native success. If both renderers fail, semantic source and diagnostics remain inspectable. `card show` now reports preferred backend, available cached backends, active artifact backend, layout-policy identity, renderer provenance, and diagnostics. M20c does not add a persistent GUI selector; Standalone's `--diagram-backend native` and `--appearance` are bounded qualification controls.

## Bounds and failures

Native layout accepts 1–256 nodes, at most 512 edges, labels up to 4096 UTF-8 bytes, and SVG output up to 2 MiB. Unsupported strategies/topologies fail with a visible diagnostic and preserve Mermaid fallback. Tests cover missing/invalid semantic source through the existing Card projection suite, deterministic layout/SVG, corrupt cache, policy/appearance/backend key separation, fallback truthfulness, source correlation, label preservation, and Canvas camera behavior.

## Non-goals

M20c adds no coordinates or `spine`/`lane`/`alignment` API to Diagram IR, no new reflection query, arbitrary graph solver, force-directed layout, Graphviz, browser dependency, authored SVG source, Diagram editor, drag persistence, sequence/chart/timeline projection, nested viewport, or Page/Card/vault redesign.
