# COPELAND-PROFILE-TSX-M0 report

> M1 update: Profile booleans/features are ordinary compile-time functions.
> Templates specialize typed Profile values; they do not emit syntax. See
> `profile-template-functions-m1-report.md` for the completed template seam.

## Outcome

**Outcome B — the semantic model and real contour/MSDF path are good, but the
Copeland `template`-to-Profile-operation expansion seam remains deliberately
unwired.**

M0 proves the useful core:

```text
Profile TSX
  -> compiler-recognized geometry expressions
  -> immutable ProfileDefinition / ProfileOperation SSA
  -> existing canonical VectorShape contours
  -> SVG inspection backend
  -> unchanged M5 MSDF compiler, atlas, Machina icon, and Aurelian renderer
```

It does not add a CAD system, runtime JSX tree, React dependency, SVG DOM, or
general polygon Boolean kernel. The next milestone is bounded to expanding an
existing Copeland `template` into typed `ProfileOperation` values before this
same resolver.

## Aetheris and Firmament audit

The audit was read-only and concentrated on the sheet-metal work identified as
the mature profile-construction path:

- `Aetheris.Kernel.Firmament/FirmamentV2/SemanticProfileIr.cs`
- `SemanticEdgeProfileIr.cs`, `SemanticCornerProfileIr.cs`, and
  `SemanticProfileDeltaParser.cs`
- `Materializer/ResolvedProfile2D.cs`
- `Aetheris.SheetMetal` and the M10-M12 sheet-metal tests
- the profile, edge-profile, corner-profile, profile-delta, section-stack,
  boss/pocket, and hole tests

Current Firmament places semantic authority above exact curve segmentation.
Named semantic members have stable IDs, source spans, and one-to-many exact
curve descendants. `EdgeProfile` inserts generated carrier spans around sorted
relative fragments. `ProfileDelta` is a bounded baseline-returning program of
named levels and transitions. It rejects duplicate IDs, overlaps, invalid
anchors, invalid runs, and a terminal level that does not return to the carrier.
Sheet-metal bend terminations lower to those profile deltas while retaining
their own stable semantic identity, and conflict diagnostics name both owners.

This is the most important lesson borrowed: **semantic feature identity must
survive lowering even when exact segment identity changes.**

### Required comparison

| Concern | Current Firmament approach | Proposed Copeland Profile | Decision | Reason |
| --- | --- | --- | --- | --- |
| Base profile | named Concept Path/Profile or explicit loops; physical frame | typed analytic `Shape` used as immutable `Base` state | Adapt | vector assets need a smaller entry point |
| Additive features | Tab, boss, add region, section-stack Add | `Tab` plus bounded generic `Add` | Borrow | semantic name improves editing; generic escape remains |
| Subtractive features | Notch, Hole, pocket, remove region | `Notch`, `Hole`, and contained `Subtract` | Borrow | the semantic operation retains author intent |
| Named intermediates | stable member paths and resolved descendants | named SSA states through `as` and explicit `Yield` | Adapt | exposes whole-profile progression rather than only member paths |
| Selectors | path, face, edge, local frame, start/end/center anchors | `Top/Right/Bottom/Left` plus normalized edge position | Adapt | topology/CAD selectors are unnecessary for M0 icons |
| Identity/provenance | stable concept IDs; generated carrier/curve IDs | feature ID, input/output state, source span, applied-feature chain | Borrow | diagnostics and LLM edits need stable semantic correlation |
| Templates/reuse | ordinary Firmament templates name generic profile deltas | existing Copeland templates must produce typed operations | Adapt, deferred seam | no second geometry macro system |
| Booleans | exact planar/profile and 3D constructive paths | non-zero contour composition plus contained subtraction | Reject broad port | a compiler must not depend on the CAD application/kernel |
| Offsets | physical manufacturing/profile offsets | deferred | Reject for M0 | robust offset topology is a separate bounded milestone |
| Rounds/chamfers | semantic edge/corner members and exact line/arc lowering | rounded rectangle primitive; general Round/Chamfer deferred | Adapt later | do not smuggle in an incomplete selector system |
| Final extraction | `ResolvedProfile2D` -> `PlanarContour2` -> BRep | final named state -> canonical `VectorShape` | Borrow | one explicit semantic-to-exact boundary |

### Borrowed, adapted, and rejected concepts

Borrowed:

- stable feature IDs and source spans;
- semantic features above generated curve segmentation;
- relative edge placement;
- explicit validation and fail-closed lowering;
- named, inspectable intermediate results;
- deterministic hashes at semantic and exact boundaries.

Adapted:

- Firmament's carrier-relative `FromStart`, `FromEnd`, and `CenteredAt` become a
  normalized `position` in `(0, 1)`, with `0.5` as the explicit center default;
- physical `Length` becomes abstract logical vector units;
- profile member progression becomes whole-profile SSA;
- exact line/arc output becomes the existing line/quadratic/cubic M5 contour
  representation.

Rejected as CAD/manufacturing-specific:

- millimetres, thickness, bend allowance, neutral-axis policy, bend relief,
  formed/flat mappings, section-stack extrusion, BRep ownership, face IDs,
  physical datum systems, DFM policy, and STEP topology;
- a Copeland-to-Aetheris dependency;
- the full Firmament Boolean/materialization kernel.

### Potential Firmament improvements discovered

- Whole-profile named SSA states make before/after inspection easier than a
  single resolved profile with only member descendants.
- A backend-neutral semantic hash distinct from the exact contour hash makes it
  clear whether intent or only realization changed.
- An optional familiar expression shell can make templates read like ordinary
  value construction while retaining semantic IDs.
- A compact state summary (`name`, producing feature, bounds, contours, hash)
  is useful evidence without requiring a sketch editor.
- Firmament could profit from an explicit terminal/yielded profile value in
  profile-composition tools, especially when branches or alternatives appear.

No Aetheris files were modified.

## Semantic law and IR

`Shape` is a parametric analytic value. `Profile` is a resolved closed contour
set produced from a base shape and zero or more operations. A `ProfileDefinition`
owns its name, base state/shape, ordered operations, final yield state, and span.
Every `ProfileOperation` owns a stable feature ID, one prior input state, one new
output state, a typed kind/parameters, and a source span.

The validator requires:

- positive finite dimensions and valid radii;
- each operation to consume an already-created state;
- unique feature and output-state IDs;
- immutable prior states;
- `RepeatRadial` count 3-256;
- valid edge and normalized position;
- a fully contained M0 subtraction/hole;
- one explicit final state.

There is no hidden mutable geometry object. For Gear:

```text
0 Base       = Circle(radius: 32)
1 WithTeeth  = RepeatRadial(Base, count: 12, toothDepth: 8)
2 Hollow     = Hole(WithTeeth, radius: 12)
yield Hollow
```

State summaries expose bounds, contour count, exact contour hash, producing
feature, and the applied-feature chain. The definition maps every feature ID to
its source span and input/output state. M0 does not promise stable generated edge
IDs; that would require a richer contour-provenance table and is deferred.

## Coordinates, units, closure, winding, and normalization

- Origin: the analytic shape centre unless coordinates say otherwise.
- `+X`: right.
- `+Y`: up, matching the existing MSDF `YAxisOrientation.Upward` path.
- Units: abstract finite logical vector units, not pixels or physical units.
- Output: closed contours only. Polygon closing is compiler generated.
- Fill: non-zero.
- Canonical winding: clockwise outer contours; nesting depth alternates holes to
  counter-clockwise, using the existing `VectorShape` normalization.
- Hashing: round-trip numeric text, normalized contour winding, typed operation
  order, and explicit state identity. Equivalent repeated compilation is stable.
- Start-point and commutative-operation reordering are not canonicalized in M0.

## Primitive and operation scope

Implemented shapes: `Rectangle`, `RoundedRectangle`, `Circle`, `Ellipse`,
`RegularPolygon`, and `Polygon`.

Implemented operations: `Add`, contained `Subtract`, semantic `Hole`, semantic
edge-relative `Tab` and `Notch`, gear-oriented `RepeatRadial`, `Translate`,
`Rotate`, `Scale`, and `Mirror`.

`Hole`, `Tab`, and `Notch` remain first-class semantic operations. They do not
immediately disappear into anonymous Boolean nodes. Generic `Add` and
`Subtract` are escape hatches. The M0 generic Boolean law is intentionally
narrow: addition accepts disjoint closed regions, while subtraction must be
fully contained. Overlapping generic `Add` fails closed because the M5 MSDF
path does not treat intersecting contours as a robust union; attached geometry
uses semantic `Tab`. Crossing arbitrary polygon difference, `Offset`, general
`Round`, `Chamfer`, and arbitrary `RepeatLinear` are deferred instead of backed
by a brittle home-grown kernel.

There is no raw path authoring form in M0. `Polygon(points)` is the low-level
closed-contour escape hatch. A future `Path` may admit explicit closed line,
quadratic, and cubic segments, but it must not become the normal API.

## Syntax comparison

Scores are 1 (poor) to 5 (strong); lower verbosity and compiler complexity are
scored as better.

| Candidate | TS familiarity | LLM guessability | semantic clarity | low verbosity | low compiler complexity | diff quality | SSA visibility |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Fully nested `<Base><Circle/></Base><Add>...` | 3 | 3 | 4 | 2 | 3 | 3 | 2 |
| TSX shell plus ordinary operation expressions | 5 | 4 | 5 | 4 | 4 | 5 | 5 |
| Custom state-like `{Add Tab() => WithTab}` arms | 2 | 2 | 4 | 4 | 1 | 4 | 5 |
| Ordinary TS block embedded in TSX | 4 | 3 | 4 | 2 | 2 | 5 | 5 |

The selected form is a TSX document shell with ordinary geometry expressions:

```tsx
export default (
    <Profile name="Gear" base={Circle({ radius: 32 })}>
        {RepeatRadial({
            as: "WithTeeth",
            id: "GearTeeth",
            count: 12,
            toothDepth: 8,
            toothFraction: 0.52
        })}
        {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
        {Yield(Hollow)}
    </Profile>
);
```

TSX earns its limited place by marking one static asset document and separating
its ordered semantic operations from surrounding module syntax. It does not
turn every geometry value into a component. `Circle`, `Hole`, and
`RepeatRadial` are compile-time function forms. Their object literals are named
options bags, not mutable geometry objects or operation identity. There is no
`new`, component invocation, or runtime tree.

The native/non-TSX decision is **ordinary typed compile-time API, no new
`profile` keyword in M0**. The same `ProfileDefinition` can be constructed
directly. A keyword has not yet earned its parser cost. The `.profile.tsx`
suffix is a convention; the explicit Profile compiler entry point is authority.

## TSX lowering and Flow boundary

`ProfileTsxCompiler` reuses Copeland's neutral `.tsx` parser and its
`TsXmlElementExpressionSyntax`, object literals, calls, names, arrays, and source
tokens. It recognizes exactly one default-exported `<Profile>` root, lowers its
base and brace children directly to typed Profile IR, then calls
`ProfileCompiler`. It does not bind a JSX value.

Profile does not reuse Flow MIR or runtime event semantics. The only reused Flow
lesson is architectural: profile-specific TSX recognizes a static semantic form
and lowers it before runtime emission. Geometry remains in the neutral
`Copeland.Profile` package; Flow remains `(State, Event) -> State'`.

## Template and repetition decision

### Function, generic, and object-form evaluation

Firmament can profitably expose dedicated semantic objects because the entire
language is a compile-time DSL. Copeland TS is a general language, so importing
that object model into source would make ordinary-looking values obey surprising
compiler-only rules. The short evaluation therefore selects functions:

| Form | Result | Decision |
| --- | --- | --- |
| `new Hole(...)` / mutable operation object | suggests runtime identity and mutation | reject |
| `<Hole ... />` | suggests a JSX component and tree reconciliation | reject |
| `Hole({ radius: 12 })` | ordinary call shape; named arguments; statically lowerable | select |
| positional `Hole(12, 0, 0)` | compact but opaque and fragile as options grow | reject |
| author-defined generic runtime function | useful only when it has ordinary runtime meaning | not a Profile expansion mechanism |
| Copeland `template` returning typed operations | existing compile-time parameterization and constraints | select for reusable feature families |

The intended type-level contract is that built-ins are pure compile-time
functions from an immutable options record to a typed profile operation. The
record supplies TypeScript-style named arguments; it is not the semantic node
itself. `Hole`, `Tab`, and `Notch` do not need type parameters merely to look
generic. Type parameters are warranted only when a reusable operation genuinely
preserves or constrains an input profile capability. Static geometry values such
as tooth count belong to template value parameters, not type generics.

Consequently M0 keeps `Tab({...})`, `Hole({...})`, and the other built-ins as
function-shaped intrinsics and reserves `template<...>` for author-defined,
compile-time expansion. This matches ordinary Copeland TS expectations while
still lowering immediately to the same immutable `ProfileOperation` IR.

`RepeatRadial` is a typed operation because it is a common compact profile
pattern and provides the canonical gear proof. It is deterministic and keeps
count, tooth depth, tooth fraction, and rotation semantic.

Custom `GearTeeth<Count>` must use the existing Copeland `template` facility and
produce this same operation IR. M0 intentionally does not invent a geometry
macro or pretend an expansion template is a runtime function. That adapter is the single Outcome-B seam: the
current template compiler produces artifacts, but it does not yet expose a typed
`ProfileOperation` expansion target. Wiring that target without creating a
second authoring IR is the exact next milestone.

`with` remains ordinary immutable value update and has no Boolean meaning.

## Backend proofs

`VectorGeometry` moved unchanged in behavior into the neutral
`Copeland.Profile` assembly. This is the canonical contour universe already
used by M5. `Machina.Fonts` references it; no adapter copies contours into a
second representation.

The SVG exporter walks those contours after profile resolution and flips Y only
for SVG presentation. SVG source is never reparsed on the Profile-to-MSDF path.
`ProfileVectorIconCompiler` gives the final canonical shape directly to the
existing `VectorIconMsdfCompiler.Compile` method.

The canonical M5 `Settings` fixture now compiles from `ProfileFixtures.Gear()`.
The unchanged atlas packer, `MachinaVectorIconId`, `UI.Icon`, presentation
primitive, Aurelian adapter, native MSDF quad renderer, and shader consume it.
The old hand-authored SVG remains only as a parser/compatibility oracle.

Evidence lives in `artifacts/copeland-profile-tsx-m0`:

- `Gear.svg` and `TabbedBadge.svg`, both also compiled to deterministic MSDF;
- `Shield.svg`, whose circular cutout supplies cubic contour coverage;
- `MultiHole.svg`, with one outer and two hole contours;
- `manifest.json`, with state tables, bounds, semantic hashes, contour hashes,
  M5 icon identities, and field hashes.

## LLM editability and fresh-model ergonomics

Changing Gear from 8 to 12 teeth and hole radius 8 to 12 changes exactly two
obvious parameter lines. Both the semantic IR and contour hashes change; no
control points or SVG commands are edited.

A fresh-context model was given only the syntax law and four requests. Its first
pass incorrectly invented `Shape({ shape: ... })` around the base because the
brief said “base Shape” ambiguously. After the law was corrected to say that
`base` takes a shape function directly, it authored all four requested documents
with the intended state chain and no SVG fallback. Gear and badge used no
coordinates; shield used polygon points and an offset hole; D-pad used centered
`Add`. The experiment therefore changed the documentation: direct-base syntax,
origin defaults and the edge-position law are now explicit. Its D-pad attempt
used overlapping centered `Add`; real MSDF qualification exposed poor parity,
so M0 now rejects that construction instead of silently emitting bad geometry.
The final corrected D-pad uses centered `Tab` operations on the left and right
edges of a vertical rounded rectangle. It compiles through MSDF with no raw
coordinates and keeps attached geometry semantic, without pretending M0 has a
general union kernel.

Remaining fallback pressure is concentrated in bespoke/organic silhouettes.
That supports keeping `Polygon` as an escape hatch and deferring a raw path form.

## Diagnostics and runtime audit

Diagnostics carry source path/start/length and cover malformed Profile roots,
missing/unknown/duplicate attributes and options, invalid shapes, non-finite or
non-positive dimensions, invalid radius/count/edge/position, duplicate states
and features, unknown prior/yield state, unsupported operation, non-contained
subtraction/hole, and missing final yield.

Audit results:

- no React dependency;
- no JSX runtime, `createElement`, or component call;
- no runtime profile interpreter;
- no SVG DOM/runtime;
- no dependency from Copeland to Aetheris;
- no Aetheris modification;
- compile/build-time immutable values only.

## Validation and tests

New focused coverage exercises Profile TSX parsing, typed lowering, Gear,
TabbedBadge, semantic edits, named immutable SSA, deterministic hashes,
diagnostics, transforms, generic contained subtraction, curves, multiple holes,
SVG output, direct Profile-to-M5 MSDF compilation, rejection of overlapping
generic addition, and a connected D-pad authored with semantic tabs.

Final local validation:

- `dotnet test Copeland.TS.slnx -m:1 -v:minimal`: 1,633 passed;
- `dotnet test Machina.UI.slnx -m:1 -v:minimal`: 738 passed;
- `dotnet test Aurelian.slnx -m:1 -v:minimal`: 650 passed;
- `dotnet test JointTaskForce.slnx -m:1 -v:minimal`: 3,338 passed;
- native M5 qualification: 8 icons, 18 semantic uses, zero validation errors;
- generated Profile evidence: four SVG fixtures plus deterministic manifest;
- `git diff --check`: clean apart from existing line-ending conversion warnings.

## Exact next milestone

**COPELAND-PROFILE-TEMPLATE-M1 — add one typed template expansion target that
can produce `ProfileOperation[]`, and prove `GearTeeth<Count>` lowers to the
existing `RepeatRadialProfileOperation` without a second macro system, geometry
IR, parser, or runtime.**

Do not add offsets, general polygon clipping, open paths, a sketch editor, or
runtime procedural profiles in that milestone.
