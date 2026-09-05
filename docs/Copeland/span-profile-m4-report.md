# COPELAND-SPAN-PROFILE-M4

Outcome: **B — the language-level span and connected Profile replacement seam
are complete; radial repetition of an authored `GearTooth` span is the isolated
remaining geometry seam.**

## 1–2. Existing audits

| Concern | Existing support | M4 need | Change |
| --- | --- | --- | --- |
| generic parsing | `GenericTypeSyntax` already parses `Name<T>` | recognize `Span<T>` | binder-only intrinsic-family case; no parser change |
| generic functions | open body plus deterministic closed specialization | infer/substitute nested spans | span participates in inference, substitution, and closed identity |
| nominal/structural types | records, payload enums, erased interfaces | typed `ProfileSegment` values | ordinary payload enum and records in the Profile module |
| arrays/static evaluation | immutable ordered arrays, indexing, length | compile-time span carrier | array-to-span view conversion; static values retain ordered elements |
| templates | `TemplateTypedValue` with deterministic hashing | span result | ordinary typed template result; no template special case |
| runtime/backends | closed MIR, C# and JS array lowering | representation-neutral current carrier | span lowers through immutable array MIR; no unsafe memory ABI |
| Profile segments | canonical line/quadratic/cubic contours | connected heterogeneous sequence | immutable replacement list lowered to the same canonical segments |
| identity/provenance | positional M3 IDs and feature provenance | preserve unchanged IDs and stable generated children | retained IDs outside target; `feature:<id>/segment:<index>` inside replacement |
| validation | closure, finite shape inputs, flattened intersection test | whole-span validation | exact endpoints/adjacency plus replacement/profile intersection checks |

M3 selected one outer-boundary segment by index and let the compiler own its
endpoints. `ReplaceSegment` already preserved C0 closure and ran the canonical
intersection validator. M4 keeps that model and adds only an owner state, start
index, count, and ordered immutable replacement segments. It does not introduce
topological naming, a path builder, or a second curve system.

## 3–18. Span and replacement law

`Span<T>` means **a contiguous ordered region of `T`**. It is a compiler-known
generic family because Copeland does not have generic nominal declarations and
future memory lowering needs semantic authority beyond a library record. The
ordinary current carrier is an immutable array; Profile decoding uses a distinct
owner-bound selection or replacement representation.

- General spans may be empty. `ReplaceSpan` requires both target and replacement
  to be non-empty (`COPE-PROFILE-0044`).
- `Span<ProfileSegment>` targets contain ordered consecutive `Selected` values
  from one state. Disjoint/mixed-owner selections fail with
  `COPE-PROFILE-0042`.
- `SelectSegment(owner, index)` plus `SpanOf([...])` is the deliberately small
  M4 selection API. It reuses M3 segment indexes and adds no selector language.
- The owner must equal the operation's current SSA input. Old-state and
  cross-state use fails with `COPE-PROFILE-0047`.
- `ReplaceSpan({ id, as, target, replacement })` is one Profile delta regardless
  of child count. Sequential operations make ordering explicit; no operation
  batch can contain ambiguous simultaneous overlapping spans.
- Replacement first/last endpoints must exactly equal target traversal
  endpoints. Reversed input is rejected explicitly with `COPE-PROFILE-0045`;
  there is no auto-reversal or warp.
- Every generated segment end must exactly equal the next start. A gap fails
  with `COPE-PROFILE-0046`.
- Lines and the existing Arc, Bulge, and Spline curves may be mixed. Exact-zero
  segment sanitization remains owned by `VectorContour`; no epsilon cleanup was
  added.
- Whole-boundary replacement is rejected in M4 (`COPE-PROFILE-0048`). A future
  version needs an explicit closed-span law rather than treating the boundary's
  coincident endpoint as an ordinary open replacement.
- Outer endpoint equality and internal adjacency splice into the untouched
  closed contour. The existing whole-boundary intersection and winding path then
  validates the result, so authors never append `Close()`.
- `ReplaceSegment` remains source-compatible and now calls the same
  `ProfileGeometry.ReplaceSpan(..., count: 1, ...)` core.

Target selection identity is replaced, not retained. A target selection has a
deterministic semantic hash over owner/start/count. Each generated child has a
stable ID derived from feature identity and generated index. Unchanged segments
retain geometry, identity, and provenance even when insertion shifts their
physical index. One multi-child feature retains one feature identity and each
child records its generated index; template origin continues through the
existing `ProfileTemplateProvenance` on the single operation.

## 19–36. Qualification results

The focused suite proves a real three-segment `DovetailTab` (flank, top, flank)
and a real two-segment `VNotch` (edge, inward tip, edge) on `TabbedBadge`. They
compile as two SSA states after `Base`, remain closed, and use no raw SVG path.
The standard Profile module also exposes ordinary immutable `DovetailTab`,
`VNotch`, and three-piece `GearTooth` span helpers. `GearTooth` span construction
is typed and deterministic, but applying it around a boundary still requires a
future repetition operation; existing `RepeatRadial` continues to own the
qualified Gear contour and was not misleadingly wrapped.

Ordinary functions return `Span<ProfileSegment>`, an imported `ProfileTools.ts`
helper returns a two-child Beak replacement, and a template returns
`Span<ProfileSegment>` through `TemplateTypedValue`. Concept points naturally supply
replacement endpoints and the existing Bulge proof lowers to a canonical
quadratic. Arc and Spline use the same `SegmentCurve` path.

Negative evidence distinguishes wrong element type, empty/invalid target,
stale owner, disconnected replacement, reversed endpoints, and self/profile
intersection. Non-finite values retain M3's validation and `VectorPoint`
construction law. Small/deep valid tab/notch variants use one semantic parameter
edit; invalid extremes fail rather than repair.

SVG output remains the existing canonical line/quadratic/cubic path. No M5
MSDF, Aurelian, Vulkan, shader, renderer, mutation builder, CAD solver, or raw
path implementation changed. The pelican benchmark was not gratuitously
rewritten: its existing single-segment semantic curves already use the narrower
operation. The new imported Beak proof demonstrates the reusable multi-span
authoring seam without changing that benchmark's established artifact.

The non-Profile proof binds, specializes, indexes, and reads length from
`Span<int>`, while `Span<int>` versus `Span<string>` fails normal invariant type
checking. Memory spans may later lower to a non-owning contiguous memory region;
M4 adds no pointers or unsafe runtime. `Span<CurveSegment>` is reserved naturally
for future open line art and strokes without implementing that runtime now.

## 37–47. Validation, feedback, and next pressure

Potential Firmament VNext Feedback: owner-bound `Span<ProfileSegment>`, exact
contiguous selectors, and one-delta multi-child `ReplaceSpan` would make
Firmament profile construction more reusable while preserving feature identity.
The useful boundary is the same: construction guides remain erased, and span
identity must not be conflated with incidental topology numbering. Aetheris was
not modified.

Focused M4 validation contains nine tests covering generic binding and
invariance, ordinary function return, imported helper return, template typed
value return, multi-segment target, semantic curve replacement, deterministic
children/provenance, SSA sequencing, closure, and stale/disconnected/reversed/
crossing failures. The combined Profile/template lane contains 109 passing
tests, including M0–M3 compatibility. Required solution validation passed:
Copeland.TS 1,699; Machina.UI 738; Aurelian 650; JointTaskForce 3,404
(6,491 test executions, with expected overlap between solutions).

The exact next pressure point is **radial application of an authored connected
span**. Solving it cleanly requires an explicit transform/repetition law for
owner-bound target spans; extending current `RepeatRadial` silently would create
a second implicit geometry engine and obscure SSA ownership. This is why M4 is
classified Outcome B rather than claiming the GearTooth repetition gate is
closed.

Machine-readable milestone facts are in
`artifacts/copeland-span-profile-m4/manifest.json`.
