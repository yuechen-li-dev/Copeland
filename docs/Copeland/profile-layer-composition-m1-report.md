# COPELAND-PROFILE-LAYER-COMPOSITION-M1

**Outcome A: typed source composition replaces numeric painter selection cleanly.**

The pelican/bicycle now compiles from five named layers in ordinary source order:

```text
Wheels
Bicycle Frame
Pelican Legs
Pelican Body
Pelican Details
```

Those layers contain the same 17 independently resolved Profiles as M0. There is
no renderer change, geometry change, React/JSX runtime, or retained vector tree.

## 1. Current numeric-layer audit

| Concern | M0 implementation | Problem | M1 reuse/change |
| --- | --- | --- | --- |
| Source selection | `const Layer: int = 0`, 17 numeric branches | Anonymous values carry identity and order | Removed; five typed named layer values |
| Painter order | C# `layerNames` array plus numeric loop | Order and names duplicated outside source | Source `ProfileLayer[]` order is authority |
| Geometry | One `<Profile>` selected per compiler run | Geometry itself was sound | Reused unchanged per item |
| Style | `BuildLayerStyle(int, ...)` | Color classification repeated numeric ranges | Existing `ProfileStyle` attached to each Profile value |
| SVG | `ExportLayers(ProfileSvgLayer[])` | Flat paths; source did not own groups | New composition overload emits semantic groups |
| Hashing | Evidence tool joined per-layer hashes | No first-class named composition hash | Composition IR owns semantic/order and geometry hashes |

## 2. Machina ordering lessons

Machina was inspected for `VStack`, `HStack`, `Anchor`, child IDs, stack items,
and resolved child ordering. The useful concepts were source-local ordered
collections, explicit nominal identity (`NodeId`), and deterministic stable
ordering. The numeric layout `Z` and row `Order` fields were not copied: Profile
painting needs only author order. Profile has no dependency on Machina.UI.

## 3. Candidate source models

| Candidate | Assessment |
| --- | --- |
| A — `<Layers><Layer>` | Readable, but requires a second nested TSX grammar and makes helper-returned values awkward |
| B — payload enum | Strong identity, but every new layer requires declaring an illustration-specific enum and still needs a collection surface |
| C — anonymous `<Compose>` | Source order is clear, but semantic identity and SVG naming become optional or external |
| D — `Layers([Layer(...)])` | Reuses ordinary arrays, functions, records, static evaluation, and typing with no parser form |

Candidate D is the final model. It is the least awkward because a layer helper is
an ordinary `ProfileLayer[]`, not syntax that must be recognized by a TSX walker.

## 4–6. Chosen syntax and layer identity

```ts
function BicycleLayers(...): ProfileLayer[] {
    const WheelsLayer: ProfileLayer = Layer("Wheels", [...]);
    const FrameLayer: ProfileLayer = Layer("Bicycle Frame", [...]);
    return [WheelsLayer, FrameLayer];
}

export default (Layers(BicycleLayers(...)));
```

`Layer` accepts ergonomic literal text and immediately constructs a typed
`ProfileLayerId` inside the statically evaluated Profile library. Names are not
parser-whitelisted. A new name needs no compiler edit. Non-static construction
cannot pass the existing bounded static evaluator, so the string is never a
renderer/runtime handle.

## 7–10. Painter order, grouping, duplicates, and empties

The `ProfileComposition.layers` array is painted first-to-last. Each
`ProfileLayer.profiles` array is also painted first-to-last. A layer can hold any
number of independently resolved Profile values. Nested groups are deliberately
unsupported; the five-layer flat model handled the benchmark without a tree.

Duplicate layer identities reject with `COPE-PROFILE-COMPOSE-0005`. Duplicate
Profile identities reject globally with `COPE-PROFILE-COMPOSE-0007`. Empty
layers are allowed in source and erased during lowering; an all-empty
composition rejects with `COPE-PROFILE-COMPOSE-0008`.

## 11. Typed composition IR

The backend-neutral build-time IR is deliberately small:

```text
ProfileComposition
  ProfileLayer(ProfileLayerId)
    ResolvedProfilePaintItem
      stable Profile ID
      canonical VectorShape
      ProfileStyle
      Profile IR hash
      canonical contour hash
```

It wraps resolved outputs and never enters `ProfileShape`, `ProfileOperation`,
geometric SSA, booleans, contours, or feature provenance.

## 12. TSX versus ordinary TS

M1 adds only the ordinary typed value surface. Existing `<Profile>` TSX remains
supported and thin, and still accepts semantic geometry expressions rather than
`<Circle>`/`<Add>` components. A second TSX composition spelling was not added
because it would duplicate Candidate D without improving the benchmark.

## 13. SVG grouping and export

`ExportComposition` emits one `<g>` per nonempty semantic layer and one `<path>`
per resolved Profile. `data-profile-layer` and `data-profile-id` retain the exact
semantic names. XML IDs are deterministic lowercase ASCII slugs; leading digits,
empty slug results, and collisions receive deterministic SHA-256-derived suffixes.
Raw SVG grouping is derived output, not authority.

## 14. Hash behavior

- `SemanticHash` includes layer identity/order, item identity/order, Profile IR,
  contour hash, and style. Rename, reorder, style, or semantic geometry changes
  therefore change it.
- `CanonicalGeometryHash` hashes the sorted multiset of canonical contour hashes.
  Layer rename, layer move, or painter reorder does not change it.
- Each item's existing Profile IR and canonical contour hashes remain unchanged
  when that item moves between layers.
- SVG bytes include group and painter order, so reorder changes the SVG hash.

## 15. Geometry identity preservation

Focused tests move `Wing` between two layers and compare its Profile IR hash,
contour hash, and the composition geometry hash. All remain equal; only the
semantic composition hash changes. No unrelated feature is regenerated.

## 16–17. Pelican migration and readability

M0 had 284 source lines, 37 case-insensitive layer-related lines, 30 numeric
selector comparisons/literals, and no named source layer declaration. M1 has
237 source lines, 15 case-insensitive layer-related lines, five named layer
declarations, and zero numeric selector comparisons/literals. The final five
names appear together in the returned array, so behind/in-front order is readable
without following branches or a C# table.

## 18. Four semantic edit regressions

| Edit | Changed source lines | Changed Profile geometry |
| --- | ---: | --- |
| Beak +20% | 1 | `Beak` |
| Wheels +15% | 1 | all 17, because wheel centers anchor the connected layout |
| Raise body | 1 | upper/lower leg, tail, neck, body/wing, beak, head/eye |
| Head upward + larger wing | 2 | body/wing, beak, head/eye |

The exact M0 localized edit quality `1 / 1 / 1 / 2` is preserved.

## 19. Three painter-order tests

Three resolved composition edits were exported: pelican body before bicycle
frame, pelican details before body, and beak after head. Each changes semantic
composition and SVG hashes. All retain geometry hash
`26fb8ad6e9e1802d6b3933660c33808660a612c1cf081d6037e9b4b935e7b48d`.

## 20. Fresh-context model result

A fresh-context model read only the final source and public declarations. It
correctly said:

1. Wing-behind-body is not a truthful layer-only edit because the wing remains
   the existing subtractive `FoldedWing` operation inside `BodyAndWing`.
2. Beak-in-front-of-head means moving `Beak` after `HeadAndEye` within `Pelican Details`.
3. Bicycle-frame-behind-legs is already satisfied because `Bicycle Frame`
   precedes `Pelican Legs`.

It explicitly declined to invent numeric z values.

## 21. Diagnostics

Composition diagnostics cover missing/default value, invalid composition value,
invalid layer values, empty/non-static identity, duplicate layers, invalid Profile
content, duplicate Profile identity, unresolved content, and empty Profile/yield
identity (`COPE-PROFILE-COMPOSE-0001` through `0009`). Existing typed binding,
static-evaluation, Profile geometry, unresolved yield, and `ProfileStyle`
diagnostics retain source paths/spans and remain authoritative.

## 22. No-runtime audit

`ProfileComposition` is materialized only by the static evaluator and consumed by
canonical SVG export. No JSX runtime, React package, VDOM, retained layer tree,
visibility, opacity inheritance, animation, events, layout, clipping, transforms,
MSDF runtime, Aurelian, Vulkan, or Machina renderer change was added.

## 23. Evidence and validation

The M1 evidence runner compiled 107 times deterministically. Baseline composition
hash is `ee6933e87a9819cc1b731cd815636a7111960231922eae78cb89628bae79a732`.
M0 SVG hash `9d0cbde4...` and M1 grouped SVG hash `2a51f242...` differ as expected,
while their ordered fill/path hash is exactly
`c5f21cd77ede49a635d821c25ba748b6b7bbc17ed48450bebcf1d5de7a6395b3`.
Independent 800×600 Chrome rasterizations are byte-identical at SHA-256
`526bd39a36364845451cac453fdbec714169f2d18bedf491006e37e4e1b51627`.

Evidence lives in `artifacts/copeland-profile-layer-composition-m1`; the sample
contains the canonical grouped SVG and machine-readable manifest. Final test
results:

- focused M0/M1/M2/Profile SVG/composition lane: 46 passed;
- `dotnet test Copeland.TS.slnx -c Release -m:1`: 1,676 passed;
- `dotnet test JointTaskForce.slnx -c Release -m:1`: 3,381 passed;
- `git diff --check`: passed.

The broad run emitted only existing third-party OpenFont `CS0649` warnings.

## 24. Exact next pressure point

The next observed authoring pressure is the existing polygon-based `Tube`
workaround. A separately bounded `Capsule`/`PolylineStroke` investigation could
reduce frame, fork, handlebar, and leg coordinate work. It is geometry work and
was intentionally not included in painter composition M1.
