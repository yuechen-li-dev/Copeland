# COPELAND-PROFILE-SEMANTIC-GEOMETRY-M3

Outcome: **B — the compile-time concept, primitive, and single-segment curve seam is complete; reusable multi-segment boundary features remain the isolated pressure point.**

## 1. Firmament concept audit

| Firmament concept | Copeland/Profile adaptation | Decision | Reason |
| --- | --- | --- | --- |
| `Concept Struct` non-materialized values | ordinary `ConceptPoint` / `ConceptPath` records | adapt | Copeland already statically evaluates ordinary records and functions; a second concept language is unnecessary |
| Concept Path ordered line/arc scaffold | two-endpoint guide consumed by `Tube` | adapt narrowly | M3 needs attachment and centerline materialization, not a general path command language |
| `ErasedBeforeFeatureAir` | erased before Profile IR/canonical contours | reuse law | construction values must not become drawable contours or runtime state |
| selector-shaped authored references | stable `contour:n/segment:n` evidence and integer selector payload | adapt narrowly | deterministic boundary order is sufficient for the admitted single-contour edit lane |
| generated topology names | no public topology-number naming | reject | Firmament's own audit warns that incidental topology IDs are not stable authored contracts |
| feature/source provenance | replacement feature ID plus source span on diagnostics and state evidence | reuse | failures and changed segments remain attributable |
| full CAD placement/constraint machinery | typed arithmetic helpers only | reject | Profile construction guides are not a DOF solver |
| BRep/STEP realization | existing canonical `VectorShape` contours | reject | Profile remains a closed 2D authoring facility |

Aetheris was not changed. Its useful feedback is architectural: preserve semantic source identity, erase non-materialized construction values, fail closed, and do not promise topology names that are only incidental backend order.

## 2–4. Concept model, representation choice, and erasure

`ConceptPoint` and `ConceptPath` are ordinary Copeland records. `Point`, `PathBetween`, `Midpoint`, `Along`, and `OffsetPoint` are ordinary pure functions. This uses the existing type checker, static evaluator, function calls, records, and imports; there is no parser form or runtime interpreter.

Concept values can feed a materializer such as `Tube({ from, to, width })`. Only the resulting `CapsuleProfileShape` crosses the Profile host boundary. The evidence fixture contains 30 concept-type uses and the final SVG contains no concept node, label, or contour contribution. The manifest records `finalContourContribution: 0`.

## 5–9. Closed base primitives

The qualified base family is now Rectangle, RoundedRectangle, Circle, Ellipse, Slot, Capsule, RegularPolygon, and Polygon.

- Slot is a centered capsule with total `length`, `width`, optional angle, and optional center. M3 requires `length >= width`.
- Capsule is the closed thickening of a finite `from`–`to` segment with round ends. A coincident endpoint pair deterministically becomes a circle. `Tube` is an ordinary standard-library alias over Capsule, not a compiler primitive and not SVG stroke state.
- RegularPolygon uses value parameter `sides` (3–1024), radius, and rotation. No numeric type generic was introduced.
- Polygon accepts at least three finite vertices, closes the final edge automatically, and relies on the canonical contour owner to normalize winding. Callers do not repeat the first point.

## 10–17. Segment identity and semantic replacement

Every state exposes deterministic segment summaries with semantic ID `contour:n/segment:n`, geometry hash, and provenance feature ID. M3 deliberately does not claim a general topological naming system. Replacement selects the admitted outer-boundary segment by stable index.

`ReplaceSegment` consumes one existing segment and one `SegmentCurve`, then creates the output SSA state. Both replacement endpoints are taken from the selected segment; author-supplied endpoints do not exist in the semantic Arc/Bulge API. C0 continuity and closure are therefore preserved by construction. C1 is deferred because it needs adjacent-tangent policy rather than a hidden heuristic.

The admitted semantic set is Arc and Bulge. Both express a signed normal midpoint displacement and lower to an exact canonical quadratic segment. Arc is the intent-oriented spelling; Bulge is the mathematical spelling. Spline is the explicit cubic-control escape hatch. The authoring gradient is:

```text
Arc/Bulge semantic intent
→ signed mathematical displacement
→ explicit Spline controls
```

SVG retains quadratic `Q` and cubic `C` commands. The canonical contour remains line/quadratic/cubic data and therefore continues down the existing M5 MSDF path without a raster intermediate.

The replacement validator flattens only for validation and rejects a replacement that crosses a non-adjacent boundary segment. The failure is compile-time, carries the source span, target segment, and feature ID, and returns no shape. Non-finite curve parameters and negative segment selectors also reject. Arbitrary repair is never attempted.

Unchanged segments retain the same ID, geometry hash, and provenance. The replaced segment retains its positional ID, receives a new geometry hash, and changes provenance to the replacement feature.

## 18–21. Custom feature/tool law and proofs

Ordinary functions and templates can return `ProfileOperation` or `ProfileOperation[]`; M3 adds no macro category. The focused suite proves ordinary `GearTooth`, `DovetailTab`, and `VNotch` helpers bind through that existing contract, and proves a template-produced Bulge replacement carries template instantiation provenance.

The honest boundary is that the current `DovetailTab` and `VNotch` proofs wrap the existing bounded edge operations; they do not yet synthesize arbitrary multi-segment replacement geometry. A real dovetail or V-shaped boundary needs the next pressure point described below. `GearTooth` continues to compose through ordinary `RepeatRadial` semantics.

## 22–23. Attachment and benchmark refactor

The pelican/bicycle source now names hub, seat, handlebar, crank, body, head, wing, knee, foot, fork, top-tube, leg, and beak-axis construction values. `PathBetween`, `OffsetPoint`, and ordinary typed layout records replace the local Manhattan-normal polygon Tube implementation.

The front-frame top tube, fork, seat, handlebar, crank arm, upper/lower legs, and foot materialize through Capsule/Tube. The body applies a semantic Bulge before its wing cut; the beak applies an Arc before its seam and placement. Existing clean wheel, tail, head, and painter-layer code was retained.

## 24–25. Source comparison and editability

Compared with the retained M0 benchmark source used as the before snapshot:

| Metric | Before | M3 |
| --- | ---: | ---: |
| source lines | 285 | 219 |
| Polygon calls | 8 | 5 |
| raw curve control points | 0 | 0 |
| named concept type uses | 0 | 30 |
| semantic curve operations | 0 | 2 |

Four requested variants compile deterministically and each changes one source line: stronger body curve, upward beak bend, thicker top tube, and changed upper-leg width. Each produces a distinct composition, contour, and SVG hash while all component profiles remain closed.

## 26–28. Visualization, SVG, and MSDF

No separate debug SVG was added; concept state is currently visible through typed source and the manifest rather than a second drawing mode. The production SVG is [pelican-bicycle.svg](../../artifacts/copeland-profile-semantic-geometry-m3/pelican-bicycle.svg). It uses only ordinary canonical Profile paths and retains curve commands. The M5 integration regression consumes the same `VectorShape` line/quadratic/cubic representation; no renderer or shader changed.

## 29. Firmament feedback

The useful Firmament lesson is to keep non-materialized concept values explicitly erased and keep authored semantic provenance separate from backend topology numbering. Copeland applies that lesson at a much smaller 2D boundary: typed guide records, deterministic ordered segment evidence, and fail-closed replacement validation.

## 30. Exact next pressure point

The next bounded pressure is a `SegmentReplacement` that can return a connected sequence of line/quadratic/cubic spans between the compiler-owned endpoints. That one neutral extension would make genuine DovetailTab, VNotch, taper, and S-curve helpers practical through ordinary functions/templates. It should be added before richer selectors or C1 policy. A constraint solver, BRep naming layer, or general stroke renderer is not required.

## Evidence and validation

- Focused semantic geometry: 14 tests, including concept erasure/materialization, Slot/Capsule/polygons, Arc/Bulge/Spline, closure, identity, provenance, self-intersection and non-finite rejection, custom functions, and template replacement.
- Evidence generator: `dotnet run --project tools/Copeland.Profile.SemanticGeometryM3Evidence/Copeland.Profile.SemanticGeometryM3Evidence.csproj`.
- Machine-readable manifest: [manifest.json](../../artifacts/copeland-profile-semantic-geometry-m3/manifest.json).
- Full validation results are recorded in the final task report; generated artifacts are deterministic across two compilations per baseline and edit variant.
