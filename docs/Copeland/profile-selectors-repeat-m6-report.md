# COPELAND-PROFILE-SELECTORS-REPEAT-M6

Outcome: **A — stable selectors close the SVG/Profile authoring arc.**

The qualified path is:

```text
owner-independent ProfileSelector
→ resolve on current immutable Profile SSA state
→ owner-bound ProfileSpanSelection
→ deterministic arc-length station and tangent
→ instantiate ownerless ProfileSpanPattern
→ authoritative ReplaceSpan
→ next Profile SSA state
```

No runtime query system, parser form, renderer change, CAD constraint solver, or
second curve universe was added.

## 1. Existing selector audit

| Selector concept | Current behavior before M6 | Stable across SSA? | M6 action |
| --- | --- | ---: | --- |
| `SelectSegment(owner, index)` | Produces a concrete selected `ProfileSegment`; `SpanOf` makes an owner-bound contiguous target | No; deliberately stale after owner changes | Retained as the low-level systems/debug escape hatch |
| `Span<ProfileSegment>` | Concrete ordered, adjacent boundary view owned by one state | No | Unchanged; it remains the resolved carrier and stale-span authority |
| `ReplaceSegment(index)` | Replaces one raw segment | No | Unchanged compatibility escape hatch |
| `ProfileEdge.Top/Bottom/Left/Right` | Axis-relative input to rectangle Tab/Notch construction | Only while the closed-base edge builder remains authoritative | Not promoted into selectors; directional tie-breaking would add unneeded ambiguity law |
| feature identity/provenance | Operations and generated segments retain feature IDs; M5 repeat IDs are deterministic | Partly | Extended with inherited semantic tags used by `FeatureSpan` |
| concept guides | `ConceptPath` is erased construction geometry, previously line-only | Independent of Profile ownership | Reused directly by `RepeatAlongPath`; optional existing `SegmentCurve` makes a curved guide |
| radial target selection | M5 resolves stable radial descriptors against each current state | Yes, within Circle-specific law | Kept separate; M6 copies its sequential SSA/evidence law, not its Circle-specific target machinery |
| `Between` / offset selection | No Profile implementation | N/A | Evaluated and deferred: `FeatureSpan`, `NamedSpan`, and `Along` cover the demonstrated pressure without anchor/query expansion |

Firmament's useful lesson is its preference for authored feature/port references
and explicit unknown/empty failure over emitted topology IDs. M6 adopts that
bounded semantic-reference law. It does not import Firmament's broad face/edge
selector surface or its parser syntax.

## 2. Final selector model and ownership

The chosen name is `ProfileSelector`. `BoundarySelector` was too broad and
`ProfileSpanSelector` confused the query with its concrete result.

M6 has exactly three composable cases:

- `FeatureSpan(featureId)` resolves inherited semantic feature identity;
- `NamedSpan(name)` resolves an intentionally named region;
- `Along(selector, start, end)`, surfaced as the ordinary helper
  `AlongSpan`, restricts another selector by geometric arc-length fraction.

The selector contains no Profile owner or segment index. Its semantic hash is
derived from case, semantic identity, and recursive parameters. Every operation
resolves it anew. Resolution returns one concrete `ProfileSpanSelection` whose
owner is the current internal SSA state; normal `COPE-PROFILE-0047` stale-span
checks remain unchanged for author-supplied concrete spans.

`NameSpan({ id, as, name, target })` is a geometry-preserving Profile operation.
Its concrete target is intentionally the one-time low-level naming seam. It
attaches `feature:<id>` and `name:<name>` semantic tags to that region. Duplicate
names fail with `COPE-PROFILE-0059`. Replacement descendants inherit the
target's tags, so preserved semantic identity follows topology evolution;
destroying the identity by an operation that rebuilds the entire shape causes a
later selector to fail explicitly.

Empty resolution is `COPE-PROFILE-0056`. A match split into more than one run is
`COPE-PROFILE-0058`; M6 never returns disjoint sets or silently chooses a first
run. Invalid `Along` ranges fail with `COPE-PROFILE-0060` and require
`0 <= start < end <= 1`.

## 3. Arc-length and geometry law

Fractions and stations are measured over curve geometry, never segment count.
Lines, quadratics, and cubics share deterministic 96-chord arc-length sampling;
fraction boundaries use 48-step deterministic bisection and exact de Casteljau
subcurves. Geometry-preserving subdivisions duplicate semantic tags and
provenance, and adjacent split endpoints are snapped exactly to preserve the
closed-boundary invariant.

This means a line may become several curves without changing the selector's
fraction definition. `NameSpan` alone leaves the canonical contour hash
unchanged even though the high-level Profile IR records the naming operation.

## 4. RepeatLinear

The ordinary typed API is:

```ts
RepeatLinear({
    id, as,
    target: ProfileSelector,
    pattern: ProfileSpanPattern,
    count: int,
    spacing: number,
    footprint: number,
    offset?: number
})
```

`spacing`, `footprint`, and `offset` are explicit world units. `spacing` is the
distance between instance starts; `footprint` is the boundary interval replaced
by one instance. `count + spacing` means exactly that many stations, without
squeezing. No fit mode was needed. A sequence that does not fit fails with
`COPE-PROFILE-0062`; overlapping footprints fail with `COPE-PROFILE-0065`.
Count must be `1..256`, spacing/footprint positive, and offset non-negative
(`COPE-PROFILE-0061`).

Each instance re-resolves its selector against the current state, refines only
its current arc interval, instantiates the ownerless pattern, and calls the M4
`ReplaceSpan` authority. Local positive `v` uses the unchanged canonical
outward normal. Pattern depth remains authored world-unit depth; it is never
silently distorted to follow excessive curvature.

## 5. RepeatAlongPath

`RepeatAlongPath` has the same explicit count/spacing/footprint law and adds a
`ConceptPath`:

```ts
RepeatAlongPath({ id, as, target, path, pattern, count, spacing, footprint, offset })
```

The guide must share the semantic target's traversal endpoints. It may be a
line or one existing `Arc`/`Bulge`/`Spline` curve—there is no separate path
representation. Stations use guide arc length. Each instance samples the guide
tangent at its footprint midpoint; the canonical left perpendicular defines
positive `v` for the clockwise Profile boundary. Exact target endpoints still
snap into `ReplaceSpan`. Zero-length paths and sampled zero tangents/cusps fail
with `COPE-PROFILE-0063`; endpoint mismatch fails with
`COPE-PROFILE-0064`.

The guide is compile-time-only and erased. Final SVG contains the scallops, not
the guide.

## 6. Sequential SSA, identity, and provenance

Both repeat operations remain one high-level `ProfileOperation`. Their state
summary also contains one ordered `ProfileLoweredReplacementSummary` per
instance:

```text
Profile0 → resolve → instance 0 → ReplaceSpan → Profile1
Profile1 → resolve → instance 1 → ReplaceSpan → Profile2
...
```

Generated IDs retain M5's law:
`feature:<parent>/instance:<i>/segment:<j>`. Segment provenance names the repeat
feature and semantic tags retain both the new feature and inherited target
identity. Template provenance remains on the high-level repeat.

## 7. Qualification proofs

The mechanical TabbedBadge proof names its top span, selects the middle 80% by
`Along(NamedSpan("TopEdge"), 0.1, 0.9)`, and places four V notches with non-zero
world-unit spacing. It produces four ordered lowered replacements and a closed,
deterministic SVG.

The stable-topology fixture names raw right-edge index 1, performs an unrelated
earlier top-edge replacement that inserts two net segments, then resolves the
right edge at a later raw index and repeats successfully. Target feature/name
identity and final closed geometry remain intact. A topology-changing
replacement of the target preserves identity on descendants; a whole-shape
rebuild intentionally destroys it and subsequent selection fails empty.

The curved proof uses the existing rounded-rectangle cubic as a matching
`ConceptPath` and places four scallops by arc length with local tangent normals.
The two generated segments per instance have deterministic parent/child IDs.

An imported `ProfileSelectorTools.ts` proof returns `ProfileSelector`,
`ProfileSpanPattern`, and `ProfileOperation[]` from ordinary templates. A
payload-enum `match` chooses Feature versus Named targeting and ordinary `with`
copies the immutable pattern configuration. There is no parser whitelist or
Profile-specific module behavior.

The pelican/bicycle benchmark is intentionally unchanged. Forcing repeated
feathers into the current cut-paper illustration made the semantic benchmark
worse; its existing concept-guide, layer, and SVG route remains green. The
mechanical badge and curved scallop fixtures are the clearer repetition proofs.

SVG export is unchanged. The generated TabbedBadge and curved-path artifacts
flow through the existing exporter. The existing Aurelian native vector-icon
MSDF test remains the backend qualification: no MSDF, Machina, Aurelian,
Vulkan, or renderer source was modified.

## 8. Diagnostics and tests

Focused M6 tests cover owner independence, owner-bound resolved spans,
deterministic selector hashes, FeatureSpan, NamedSpan, Along, geometry-hash
neutral naming, four-instance linear repeat, four-instance curved path repeat,
ConceptPath erasure, imported templates, `match`, `with`, topology index shift,
empty resolution, disconnected resolution, invalid fractions, duplicate names,
zero/negative/excessive count, negative spacing, overlapping footprints,
zero-length path, cusp, and closedness. M4/M5 retain stale-span,
self-intersection, and authoritative boundary-overlap coverage.

Validation passed:

- focused Profile M0-M6/SVG regression: 94/94;
- focused native vector-icon/MSDF integration: 3/3;
- `dotnet test Copeland.TS.slnx -m:1`: 1,724 passed;
- `dotnet test Machina.UI.slnx -m:1`: 739 passed;
- `dotnet test Aurelian.slnx -m:1`: 650 passed;
- `dotnet test JointTaskForce.slnx -m:1`: 3,432 passed;
- `git diff --check`: clean.

That is 6,545 successful executions across the four requested solution gates,
with expected overlap. Machine-readable evidence is in
`artifacts/copeland-profile-selectors-repeat-m6/manifest.json` and `proof.json`;
the directory also contains the two canonical SVGs.

## 9. Deferred features and park decision

Deferred, not implemented: full stroke semantics, gradients, boolean path
groups, open line-art scenes, responsive vector layout, masks/clips, runtime
procedural profiles, GPU MSDF generation, general `Between`/offset/directional
queries, a general topology query language, auto-fit, and CAD constraints.

The Profile/SVG substrate now covers semantic closed profiles, ordinary
functions/templates, erased concept guides, layers, segment/span replacement,
ownerless patterns, radial/linear/path repetition, stable selectors, SVG, and
the existing MSDF backend. It is good enough to park.

**Recommendation: park Profile/SVG after M6 and return to vector UI.** The next
known pressure is product use of the qualified semantic vector-icon and native
Machina presentation path—not another speculative Profile milestone. Revisit
Profile only when a concrete UI benchmark exposes a missing authoring law.
