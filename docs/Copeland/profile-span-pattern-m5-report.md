# COPELAND-PROFILE-SPAN-PATTERN-M5

Outcome: **A — reusable connected span patterns close the radial custom-feature seam.**

The qualified lowering is:

```text
ownerless ProfileSpanPattern
→ resolve a target on the current Profile SSA state
→ instantiate target-relative concrete ProfileReplacementSegments
→ ProfileGeometry.ReplaceSpan
→ next Profile SSA state
```

No `Span<T>` law, renderer, runtime transform system, mutable builder, or second
curve representation changed.

## 1. M4 ownership and RepeatRadial audit

| Concern | Current law | M5 need | Change |
| --- | --- | --- | --- |
| `SpanTypeSymbol` | invariant compiler-known `Span<T>` family | remain memory/Profile neutral | none |
| Profile span carrier | immutable static array of typed `ProfileSegment` values | distinguish selection from recipe | new nominal record outside `Span<T>` |
| owner identity | selected segments carry one state name | keep concrete targets state-bound | unchanged |
| segment identity | generated IDs derive from feature and child index | add repetition level | `feature:<parent>/instance:<i>/segment:<j>` |
| stale detection | target owner must equal operation input | reselect on every repeat | unchanged `COPE-PROFILE-0047`; repeat creates current owner per step |
| `ReplaceSpan` | exact endpoints, adjacency, closure, intersection | remain geometry authority | shared core, unchanged validator |
| `ReplaceSegment` | delegates to `ReplaceSpan` count 1 | regression only | unchanged |
| provenance | feature plus optional template origin | repetition index | summary carries repetition index and lowered steps |
| templates | `TemplateTypedValue` preserves ordinary typed values | carry a pattern through specialization | ordinary record value; no macro kind |
| feature identity | one operation feature ID | one radial parent, stable children | parent retained; children use deterministic instance paths |

Legacy `RepeatRadialProfileOperation` repeats a primitive gear description:
`count`, `toothDepth`, `toothFraction`, and `rotation`. It accepts no semantic
operation or authored segment sequence, selects no concrete target, and calls
`ProfileGeometry.Gear` once. It therefore remains separate and source-compatible.

M5 adds `RepeatRadialPatternProfileOperation`. It retains one high-level repeat
node, but its evidence exposes one lowered replacement per instance. Circle
target selection first makes a geometry-preserving subdivision of the canonical
cubic boundary into alternating target and untouched gap spans. It does not
turn the circle into a polygon. Each target's concrete endpoints are read from
the current shape immediately before instantiation.

## 2. Reusable pattern semantics

The chosen name is `ProfileSpanPattern`. `SpanPattern<T>` was rejected because
M5 has no proven non-Profile instantiation law; `SpanTemplate` conflicts with
Copeland templates; `ReplacementPattern` hides connected ordered span meaning.

`ProfileSpanPattern` is an immutable nominal record containing connected local
`ProfileSegment` geometry. It contains no Profile state, selected segment,
owner, or concrete segment identity. `GearTooth`, `DovetailTab`, and `VNotch`
are ordinary functions returning this type. `SpanPattern(...)` is the thin
constructor used by other ordinary helpers.

The hard definition law is:

- first outer endpoint exactly `(0, 0)`;
- last outer endpoint exactly `(1, 0)`;
- every segment end exactly equals the next start;
- selected/owner-bearing `ProfileSegment.Selected` values are forbidden;
- all points and curve parameters are finite;
- line and the existing Arc/Bulge/Spline curve cases are accepted and lower to
  the existing line/quadratic/cubic contour representation.

Disconnected or selected patterns fail with `COPE-PROFILE-0050`; reversed or
non-normalized endpoints fail with `COPE-PROFILE-0052`. Reversal is never
inferred.

## 3. Instantiation and transform law

`ReplaceSpanWithPattern({ id, as, target, pattern })` is the one-instance API.
It resolves the concrete target span, maps the pattern, then calls the same
`ReplaceSpan` core used by M4. `RepeatRadialPattern` uses that same mapping and
core for every instance.

Local coordinates are:

```text
u = distance fraction along target traversal
v = signed distance along the target's outward unit normal
```

The target-relative affine map supplies the minimum Translate + Rotate + Scale
semantics needed by M5. `(0,0)` snaps to the target start and `(1,0)` snaps to
the target end exactly. `u` receives endpoint-fit longitudinal scaling. `v`
remains in authored Profile/world units and is not silently scaled by target
width. Positive `v` is outward for the canonical clockwise outer contour;
negative `v` therefore authors notches. This single `FitEndpoints` law is the
only M5 fit mode. There is no optional scene transform or fit-mode zoo.

The ordinary `with` proof replaces a pattern record's immutable `segments`
field. A payload enum `match` selects Sharp versus Soft teeth without a
geometry-specific branch in the compiler.

## 4. Sequential radial SSA and identity

For repetition `i`, M5 uses a stable radial target descriptor but does not cache
a concrete span. It adjusts the descriptor for prior topology deltas, reads the
current contour endpoints, instantiates the ownerless pattern, and calls
`ReplaceSpan`. Evidence records:

```text
WithTeeth#instance:i-1
→ target start/count on current shape
→ generated segment count
→ WithTeeth#instance:i
```

The last internal output is the authored `as` state. `ProfileStateSummary` keeps
the high-level `RepeatRadialPattern` state plus ordered `LoweredReplacements`,
so repeat intent and sequential SSA proof coexist.

One repeat has one parent feature (`GearTeeth`). Generated identities are
`feature:GearTeeth/instance:i/segment:j`; summaries expose both generated
segment index and repetition index. Segment provenance remains the parent
feature. Existing `ProfileTemplateProvenance` preserves template name,
specialization arguments, instantiation source span, and generated operation
index on the high-level repeat.

## 5. Canonical authored proofs

The standard `GearTooth` is the real three-line connected M4 shape, now returned
as a `ProfileSpanPattern`. The canonical profile is Circle → authored radial
GearTooth → center Hole. Results are deterministic and distinct:

| Teeth | Lowered replacements | Final outer segments | Profile IR hash | Contour hash |
| ---: | ---: | ---: | --- | --- |
| 8 | 8 | 32 | `41e9620a4c5b1fbd765017b8f0044f9b288c20e1a0279f61c7bced6711f34903` | `50c6a7f8990d430d210099290347a06d0ae1bac71634c120ab4e4c560e015ca4` |
| 12 | 12 | 48 | `66f5929952cc733a4c07501eb9d7c94db43a752df9ade7ec2eafe3ee1987b3ff` | `0750728ee6fe52d89850660a8a11f8a960d9feaf8ce80209ac9567dea872ed47` |
| 16 | 16 | 64 | `def6a07e883aa3e9fb3141348e2186eac45c55c043ad7265cf6d93d4bf63971d` | `2594d75903557c69b6c9ca64247b3e0c6a7253710aff87e501c6c90fd664c165` |

The legacy and authored Gear are intentionally not contour-equivalent. Legacy
Gear is an all-line four-point-per-tooth polygon whose depth and fraction are
radial/angular primitive parameters (12-tooth contour hash
`663176df3e459fba57e430ebad9ef3a56773d985305354c4ddbbf595d2f4e91e`).
The authored Gear uses a three-segment local tooth, preserves the untouched
cubic circle gaps, and keeps normal depth in world units. Treating those as hash
equivalent would silently change one of the laws. Both use the existing SVG
path; the authored 12-tooth output also compiles through the unchanged
Profile-to-vector-icon MSDF path with matching contour hash and finite field
pixels.

Dovetail and VNotch patterns instantiate sequentially on a rectangle. The
Dovetail reaches `MaxY=26`; the inward VNotch does not extend the `MinY=-20`
boundary. This numerically qualifies positive-outward and negative-inward
normal semantics. The final contour is exactly closed.

An ordinary imported `ProfileTools.ts` exports a `ToothByStyle` function. A
template calls it, uses `match` to choose the pattern, applies ordinary `with`,
and returns `RepeatRadialPattern`. This covers ordinary function, imported
helper, template-produced pattern, template-produced operation, `with`, and
`match` without parser registration.

## 6. Rejection and regression evidence

M5 rejects stale targets with M4's `COPE-PROFILE-0047`. Pattern self-crossing,
neighbor overlap, or crossing an unrelated boundary reaches the authoritative
`ReplaceSpan` intersection validator and fails with `COPE-PROFILE-0043`.
Counts outside `3..256`, including 500, fail with `COPE-PROFILE-0051`; no tooth
is clipped. Exact endpoint/adjacency splicing preserves closedness. The existing
whole-boundary and canonical winding laws remain in force.

Focused M4+M5 Profile validation passed 21/21. The new MSDF qualification passed
1/1. Required solution validation passed:

- `dotnet test Copeland.TS.slnx -m:1`: 1,711 passed;
- `dotnet test Machina.UI.slnx -m:1`: 739 passed;
- `dotnet test Aurelian.slnx -m:1`: 650 passed;
- `dotnet test JointTaskForce.slnx -m:1`: 3,419 passed.

That is 6,519 successful test executions across the requested solutions, with
expected overlap. It covers Profile TSX M0, template functions M1, function
authoring M2, layer composition M1, semantic geometry M3, span/Profile M4, and
Aurelian native vector-icon MSDF M5. Generic `Span<int>` and invariant span type
tests remain green. No renderer source changed.

## 7. Potential Firmament VNext Feedback

Ownerless replacement patterns would let Firmament define tabs, notches, teeth,
and other custom boundary features once without retaining a stale Profile
state. Target-relative `(u,v)` coordinates keep feature depth semantic while
allowing endpoint fit at different orientations. Sequential current-state
reselection gives repeated features deterministic SSA and provenance instead
of relying on incidental pre-mutation indexes. Firmament would benefit from
these four laws directly; it does not need a mutable sketch builder or scene
graph. Aetheris was not modified.

## 8. Deferred pressure

The exact next authoring pressure is a stable selector abstraction for
non-circular `RepeatLinear`/`RepeatAlongPath`, especially when an earlier
replacement changes later target topology. `ProfileSpanPattern` could naturally
generalize to ownerless `CurveSegment` motifs once an open-line-art consumer
exists, but M5 adds no speculative generic pattern hierarchy.

Machine-readable facts are in
`artifacts/copeland-profile-span-pattern-m5/manifest.json` and
`artifacts/copeland-profile-span-pattern-m5/proof.json`.
