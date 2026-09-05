# Aurelian engine architecture v1

Status: current architecture after AURELIAN-TINYFARM-DIALOGUE-CONSUMER-M7B2. This document is authoritative for the application/runtime architecture. Milestone reports remain evidence, not prerequisites.

## 1. Purpose

Aurelian is the reusable systems/runtime layer for explicit, deterministic, agentic interactive applications on .NET. Native graphics are one Aurelian capability, not its definition. Aurelian exists so an application author composes qualified runtime machinery with semantic content and small domain rules instead of rebuilding host timing, state transitions, inspection, persistence, replay, and presentation plumbing.

The implementation is transitional. Aurelian already owns world, actuation, runtime, renderer-neutral render contracts, raster/null/native graphics backends, assets, and shaders. TinyFarm currently carries the newest application-runtime shapes locally while they await a second compatible consumer. This document distinguishes the formal role from code that has actually earned extraction.

## 2. What Aurelian is

Operationally, an engine is reusable systems-level machinery whose implementation details are normally hidden behind stable semantic composition and runtime APIs. TinyFarm made gameplay routine only after it had deterministic state reduction, scene/content catalogs, semantic destinations, fixed-step simulation, controller-to-intent boundaries, save/replay/hash infrastructure, inspection projections, and replaceable presentation.

Therefore Aurelian's formal role is:

> Aurelian is an agentic interactive application/world runtime that qualifies reusable host, world-realization, action, persistence, inspection, and presentation machinery while leaving application truth and domain rules application-owned.

Current `Aurelian.*` code is still weighted toward world/render infrastructure. TinyFarm is not retroactively declared an Aurelian implementation, and its types are not moved merely to satisfy the definition.

The north-star claim that JTF is a high-level application runtime over .NET is supported as a direction, not yet as a finished product. JTF already assembles Copeland/TSON, Dominatus, Aurelian, Machina.UI, and applications under explicit ownership. It still lacks qualified application-facing composition APIs for several TinyFarm-proven shapes.

## 3. LLM-native engine definition

An LLM-native engine minimizes the systems-level reasoning that must be repeated for each application. It favors stable IDs, concrete typed commands, deterministic ordering, explicit state, inspectable intermediate forms, machine-readable content, replayable inputs, and bounded adapters. It avoids hidden callback order, mutable object graphs as authority, reflection-driven behavior, implicit binding state, and renderer-owned truth because those increase context and reasoning cost.

The primary authoring equation is:

```text
qualified systems/runtime kits
+ authored semantic content
+ small application-specific state, actions, reducers, and policy
-> application
```

This does not mean hiding the systems implementation from engine contributors. It means ordinary application work should not require understanding accumulator arithmetic, save-envelope migration, GPU synchronization, path-mesh construction, or platform callback order.

## 4. Layer model

```text
APPLICATION
  authored content, semantic state, concrete intents, domain validation,
  reducers, target priority, controller policy
       |
RUNTIME / KITS
  stable composition APIs, host/time, scene realization, navigation,
  persistence/replay envelopes, inspection, UI pipeline
       |
SYSTEMS
  clocks, allocators, storage/indexes, pathfinding integration,
  render command execution, windows, graphics, text and platform input
```

Current project approximation:

- `TinyFarm.Core` is application truth: semantic state, scene/value types, concrete intents/results/events, resolver, hashing, and UI model. Its only engine dependency is the query-only `Aurelian.Spatial2D` substrate used by the resolver; that package cannot mutate TinyFarm state.
- `TinyFarm.Runtime` is application integration: session, TSON loaders, persistence, Dominatus policy, DotRecast-derived navigation, simulation host, DTOs, and scenarios.
- `TinyFarm.MonoGame` is a leaf window/input/world/UI projection.
- `Aurelian.World`, `Aurelian.Actuation`, `Aurelian.Runtime`, and rendering projects are reusable systems/runtime foundations but do not yet own TinyFarm's scene or resolver contracts.
- `Aurelian.Spatial2D` owns deterministic world-unit geometry queries, continuous sweep/slide accepted-displacement facts, trigger diffs, and stable spatial diagnostics. It owns no actors, gameplay callbacks, timestep, or rigid-body state.
- `Aurelian.Simulation` owns ordered rational cadence production, accepted/discarded host-time accounting, generic scene/anchor/route queries, transition handoff, deterministic schedule selection, and navigation coordination facts. It owns no world-time meaning, actors, pathfinder, position mutation, schedule content, or inactive-scene policy.
- `Machina.*` owns renderer-neutral UI authoring, layout, semantics, hit testing, input records, interaction helpers, and presentation operations.
- Dominatus owns decision policy and flow, not application mutation.
- Copeland/TSON owns authored table/program truth and its compilation/loading path, not mutable application state.
- Ariadne owns dialogue flow and the renderer-neutral `DialoguePresentationSnapshot`;
  applications own skin metadata, input policy, simulation policy, and conversion of
  typed dialogue effects into authoritative game intents.

No dependency cycle between Aurelian and Machina is required. A thin integration adapter may depend on both.

## 5. Application model

An application owns:

1. semantic state and stable identities;
2. authored definitions and validation;
3. concrete typed intents;
4. authoritative resolution and domain failure reasons;
5. domain events and projection models;
6. controller policies expressed as intent production.

Application authors should usually define content, scenes/routes, state, concrete actions/reducers, and policy, then select runtime and presentation capabilities. They should not normally write fixed-step accumulators, event envelopes, save container versioning, input-edge tracking, renderer synchronization, navigation mesh plumbing, or window adapters.

The qualified M5 timing boundary is:

```text
host delta -> CadenceScheduler -> ordered DueWorkFact -> application resolver -> authoritative state
```

Host, render, simulation cadence, world, and agent-decision time remain distinct domains. Pause emits no cadence work and accrues no debt. Fast-forward scales cadence production without skipping semantic work. Explicit cadence order resolves simultaneous boundaries. Configuration identity participates in replay/save compatibility metadata, while application state and semantic intents remain replay authority.

> Aurelian supplies cadence, scene, and navigation mechanisms. Applications define world-time meaning, schedule content, and simulation policy.

## 6. Scene model

TinyFarm supports the law:

```text
scene composition = graph
scene contents = validated tables
scene routing = authoritative reducer
gameplay truth = semantic state
rendering = projection
```

The same model realizes Farm, Overworld, Town, General Store, Riverside, Hearth House, and Old Burrow. No mutable GameObject hierarchy is needed.

Ownership inside the scene model:

| Shape | Owns |
|---|---|
| `SceneDefinition` | stable scene ID, display name, bounds, validated collections |
| `SceneObjectDefinition` | stable object ID, semantic kind/reference, movement-blocking property |
| layout row | authored rectangle and layer for a scene object |
| anchor | stable semantic destination, scene position, kind, optional semantic referent/facing, arrival radius |
| route | source trigger, target scene/anchor, interaction label |
| dynamic state | actor positions/facing, item placement/ownership, plot/node/tree/enemy state |

Definitions never become mutable gameplay truth. Routes are reduced by the application resolver. Derived blocked-tile and navigation structures may be cached, but are rebuilt from definitions and are not persisted.

Scene is a proven kit shape and remains TinyFarm-local until a second application proves compatible laws. Aurelian should not impose `GameObject`, component callbacks, or scene-hierarchy mutation.

## 7. Simulation and time

Five time domains must remain distinct:

| Domain | Meaning | Current law |
|---|---|---|
| Host time | elapsed time accepted from the platform | clamped before accumulation |
| Render time | presentation observations | never advances gameplay |
| Locomotion time | fixed spatial steps | integer/fixed-step, independent of frame count |
| World time | authored minutes/days and coarse progression | normal or fast-forward rate |
| Agent decision time | bounded policy evaluation cadence | not once per render frame |

The renderer does not tick gameplay, frame rate does not define simulation, and agents do not think per frame. Pause/play/fast-forward and explicit semantic minute advance are host commands. Catch-up is bounded, order is deterministic, and fixed locomotion is qualified under multiple host-delta schedules.

The multi-rate host is a proven kit shape. The current `TinyFarmSimulationHost` remains local because its command vocabulary, world-minute semantics, and session coupling are application-specific. A later Aurelian host primitive should extract only accumulator/cadence law after another consumer demonstrates the same need.

## 8. Controllers

Controllers are peers only at the semantic boundary:

```text
human | Dominatus | LLM | replay
             -> typed intent envelope
             -> application resolver
```

- Human control translates keyboard/pointer edges into typed intents and retains presentation-only focus/open state.
- Dominatus observes bounded semantic state, selects NPC goals/actions, and emits intents; it does not mutate world state.
- LLM control parses structured semantic commands such as movement or approach into the same intent path; it does not emulate keys or mouse.
- Replay resubmits recorded semantic envelopes and receives the same validation.

The concrete differences do not justify a generic controller object hierarchy. The stable contract is intent production plus source metadata.

## 9. Intents and resolution

TinyFarm's concrete intent set includes movement, routing, interaction, talk, item transfer/trade, product trade, planting/watering/harvest, gathering, cooking, chopping, attack, hotbar selection/use, and wait. Each intent carries domain meaning and supports precise authorization, failure reasons, events, replay, and inspection.

`GameAction(ActionKind, ...)` would erase type-specific payloads and move validation into tag switches or nullable bags. Keep concrete typed intents. The resolver remains the only gameplay mutation authority and must validate direct, selected-use, replay, and agent requests independently before applying an atomic reduction.

Result law:

```text
intent envelope + prior state
-> accepted | rejected | no-op
+ typed reason
+ ordered domain events
+ next authoritative state
```

## 10. Closed capability lowering

The repeated pattern is named **closed capability lowering**:

```text
selected Turnip Seed + Plot -> PlantIntent
selected Axe + Tree         -> ChopIntent
selected Sword + Enemy      -> AttackIntent
```

Decision: keep the closed explicit lowering and defer a generic capability system. The three branches share control flow but not availability, target, consumption, failure, or mutation laws. A registry would relocate rather than remove complexity and would weaken type specificity. Generalize only if multiple actions become data-configurable under genuinely shared validation and result laws.

## 11. Active/inactive realization

The proven rule is:

```text
active scene   -> detailed spatial realization
inactive scene -> coarse semantic progression
```

Scheduled NPCs use fixed-step path following only when active; inactive transitions operate on semantic destinations without DotRecast. Activation/deactivation realizes or collapses position deterministically. Rest and wander participate through semantic schedule/energy policy. Old Burrow enemy state remains durable while no enemy AI runs in peaceful scenes.

This is a reusable runtime shape, but extraction is deferred. A second application must prove what lifecycle hooks, coarse state, and realization guarantees are shared. Navigation is derived and replaceable; it never owns semantic destinations or persisted truth.

## 12. Navigation

Agents target semantic places, not coordinates. Anchors and routes are stable authored identities. Runtime lowers an anchor goal to an active-scene path and fixed-step motion. DotRecast is a Runtime-only derived implementation, cached and rebuilt from scene definitions. It does not leak into Core, saves, controllers, or presentation.

Navigation is a potential kit. Aurelian may later own realization interfaces and lifecycle, but TinyFarm retains its anchor/path contract until cross-application evidence exists.

## 13. Content and TSON

The boundary is:

```text
TSON source -> Copeland/TSON load -> TinyFarm validation/canonicalization
            -> typed immutable catalogs -> runtime
```

TSON owns authored semantic programs/data and portable values. TinyFarm owns domain schemas, referential validation, stable-ID canonicalization, and typed catalogs. Raw `TsonTable`, row position, and compiler objects never become runtime authority. Mutable state is C# runtime truth and is not written back into definitions.

Current C# constants that are behavioral laws—reach, target priority, one-hit sword damage—should remain code until authorship pressure makes them content. Existing scene, schedule, forage, recipe, tree, and enemy definitions are correctly TSON-authored. No mass migration is warranted.

## 14. Persistence and replay

TinyFarm proves four separable shapes:

- application-specific state codecs and migrations;
- a shared chunk/version container protocol;
- semantic intent replay envelopes;
- canonical semantic hashing and definition identity.

The container/checkpoint mechanisms are already supplied by Dominatus/Aurelian integration where applicable, while TinyFarm owns rich state chunks and migration validation. Replay records semantic requests rather than renderer input. Hashes cover authoritative state and support repeat/save-load/replay parity.

Extraction decision: defer moving TinyFarm codecs; extract or reuse only envelope/container/hash mechanics when a second application would otherwise reproduce them. Save schemas must never include DotRecast, renderer, hover, or other derived/presentation state.

## 15. Machina.UI

Machina.UI is the renderer-neutral UI runtime, not application state. It already owns semantic controls/actions, styles, deterministic layout, UI semantics, hit testing, input records, dispatch helpers, text measurement, and a presentation IR of fill/stroke/text/clip operations. It does not own a game window or choose a world renderer.

Application state projects to a semantic UI document. Machina lowers and lays it out, produces hit-test and presentation artifacts, and routes normalized UI input. An adapter translates presentation operations and platform input. Gameplay actions return to the application's typed intent path.

TinyFarm's current `PlayerUiModel` and responsive geometry are renderer-neutral application projection, but its MonoGame drawing and hit testing are temporary duplicate realization. Migration belongs in a thin adapter/integration host, not Core or gameplay Runtime.

AURELIAN-NATIVE-MACHINA-PRESENTATION-M3 qualifies native MSDF text as an optional
first-class realization of the same Machina presentation frame. Machina owns the
already-positioned `MachinaGlyphRun`, opaque atlas identity, color, mode, and layout;
`Aurelian.Machina.Graphics` owns atlas-to-native-texture caching and glyph-quad
adaptation; `Aurelian.Graphics` remains Vulkan-only. Raster/pixel is still the default
and a deliberate retro aesthetic. MSDF adds scalable smooth text rather than replacing
that style.

## 16. Presentation backends

The composition law is:

```text
world presentation
+ Machina presentation frame
+ one host-owned input/focus router
-> one host window
```

Backend responsibilities are surface creation, primitive/text rendering, clip realization, input normalization, focus handoff, DPI/viewport transformation, z-order, and world/UI composition. They do not know inventory, hotbar, combat, scenes, or reducers.

- MonoGame: current qualified TinyFarm world/window backend; UI adapter is missing.
- Stride: plausible world/backend host, but no current Machina same-window bridge is qualified.
- Aurelian native: renderer contracts and Vulkan/raster infrastructure exist; a complete TinyFarm sprite/tile/text UI backend is not yet qualified.
- Avalonia: viable as a desktop control/library realizer or separate desktop presentation. Offscreen/embedded game composition requires a proof because dispatcher, lifecycle, GPU interop, and native-control airspace constraints are material.

## 17. Dominatus boundary

Dominatus owns agent lifecycle/flow, utility selection, decision cadence, and policy-local memory. It may observe semantic world projections and return typed intent requests. It does not own world mutation, movement legality, inventory, energy truth, combat damage, persistence schema, rendering, or target authority.

TinyFarm usage respects this boundary. Required schedule windows are structural control flow; Open windows use bounded utility selection and persistent per-actor runtime. Mutable policy state is isolated per actor. The passive Slime correctly has no Dominatus agent.

## 18. Kits

A kit is a qualified reusable semantic/runtime pattern that an application can compose without reconstructing its systems machinery. It is not necessarily an assembly.

| Candidate | Classification | Checkpoint decision |
|---|---|---|
| Scene | potential kit | keep local pending second consumer |
| Simulation Host | potential kit | keep local; host cadence is promising |
| Interaction/Targeting | potential kit | keep local; target priority is domain law |
| Inventory | potential kit | keep local; state shape is simple and domain-coupled |
| Hotbar | potential kit | keep local; closed binding/lowering is still evolving |
| Navigation | potential kit | keep derived implementation local; later realization seam |
| Persistence | proven systems shape | reuse container ideas; defer TinyFarm codec extraction |
| Replay | proven systems shape | semantic replay law is stable; defer package move |
| Agent | proven kit | already shared in Dominatus |
| UI | proven kit | already shared in Machina.UI; add adapter, not semantics |

## 19. Application authoring

A human or LLM building an Aurelian application should need to understand only:

1. define semantic state and authored content;
2. define scenes, objects, anchors, and routes where spatial worlds apply;
3. choose qualified kits;
4. define concrete typed actions and small reducers for domain behavior;
5. define controller policy as intent production;
6. project semantic inspection/UI and choose a presentation backend.

Already boring: typed UI layout/presentation, Dominatus utility/flow, renderer-neutral snapshots/commands, TSON compilation, and deterministic fixed-point locomotion mechanics inside TinyFarm.

Partly boring: host clocks, semantic targeting, navigation realization, save/replay/hash plumbing, input normalization, and same-window UI composition.

Still application-exposed: scene catalog wiring, target-priority enumeration, save-version branches, definition-loader composition, and backend-specific TinyFarm UI realization.

## 20. Engine internals

Systems/runtime contributors must understand time-domain accumulation and catch-up, reducer atomicity and ordering, derived indexes/caches, active/inactive handoff, backend coordinate transforms and clipping, save/replay envelopes and migrations, definition identity, input focus/capture, and allocation gates. These concerns must remain explicit and testable even when absent from application authoring.

## 21. Extraction rules

Generalize when repeated implementations exhibit shared law and maintenance pressure, not merely when concrete types look similar. Extract systems machinery when a second application would otherwise have to reconstruct it.

Evidence:

- Forage, Tree, and Enemy share identity/placement/target/projection mechanics but have different domain semantics: do not create `IResourceNode` or `InteractiveWorldEntity<T>`.
- Plant, Chop, and Attack share closed lowering but not failure/mutation law: do not create a capability registry.
- Seven scenes reuse the same authored graph/table/route law: name it as a potential kit, but await a second application before moving it.
- Host/time and UI adapter plumbing are systems work a second application should not reconstruct; qualify their smallest contracts next.

## 22. Current limitations and next qualified infrastructure work

The current architecture is coherent (Outcome A), but formal role exceeds extracted implementation in several places. The next infrastructure milestone is:

> AURELIAN-MACHINA-ADAPTER-M0 — qualify one renderer-neutral Machina presentation/input adapter contract in the existing MonoGame TinyFarm window, preserving TinyFarm state/resolver authority and replacing only temporary UI drawing/hit testing.

It should prove surface/viewport translation, fill/stroke/text/clip rendering, ordered input normalization, focus suppression, world/UI z-order, and semantic action return. It must not introduce gameplay, MVVM state, a generic game framework, or new assemblies beyond a demonstrated dependency need.

Other limitations remain deferred: cross-application scene/host/persistence extraction, Aurelian-native TinyFarm rendering, Stride bridge, Avalonia compositor proof, and generalized hostile behavior.

## 23. AURELIAN-COMPOSITOR-M0 update

The former next step is now qualified. `Aurelian.Composition` owns renderer-neutral runtime layers, explicit ordering, surface/viewport/scale, lifecycle, focus/capture, top-down input routing, and typed DTO transport. It is dependency-free and distinct from the existing GPU plant-output compositor. TinyFarm qualifies the direct-host path with MonoGame world at z 0 and a Machina UI overlay at z 100. Application simulation remains independent of presentation update, and Core/Runtime remain compositor-free. See `docs/Aurelian/aurelian-renderer-neutral-layer-compositor.md`.

## 24. AURELIAN-COMPOSITOR-M1 update

The compositor architecture remains unchanged. Stable TinyFarm/Machina topology is now cached in the UI adapter, not in `Aurelian.Composition`: identical projections reuse the full prepared frame, value changes patch existing renderer-neutral slots, surface changes rebuild layout/hit geometry, and ordered inventory identity changes rebuild topology. Diagnostics count topology, layout, presentation, hit-test, and dynamic-update work without becoming gameplay state.

The result is Outcome A: the exact M0 repeated-snapshot workload falls from 194,387 B and 294.57 microseconds to 0 B and 0.06 microseconds after the cold frame; a real value-changing workload measures 14,712 B and 18.13 microseconds. TinyFarm Core/Runtime dependencies and the generic compositor contract remain unchanged. The next qualified infrastructure question is a separate `AURELIAN-NATIVE-VULKAN-BACKEND-M0` audit/vertical slice covering native sprite/quad, glyph, surface, viewport, resource, batching, composition adapter, SDSL-V upload, and synchronization ownership.

## 25. SDSL-V cross-repository ownership update

The AURELIAN-SDSLV-AUDIT-M0 decision supersedes the assumption that the
independent parser under `Aurelian.Shaders.Language` may evolve as Aurelian's
own SDSL-V dialect. SDSL-V is one semantic language defined by the canonical Oct
specification and conformance corpus. Copeland should reuse its TypeScript-shaped
parser and add a GPU semantic profile plus a small first-class metadata syntax;
`.v.ts` is a useful convention, not semantic authority.

Ownership is explicit: Copeland owns parsing, GPU closure/type checking and
frontend-neutral SDSL semantic IR production; `Aurelian.Shaders` owns HLSL,
DXC, SPIR-V validation/reflection and artifact metadata; `Aurelian.Graphics`
owns Vulkan module and pipeline realization. Shader semantic metadata generates
host binding/layout inputs, so handwritten shader/C# binding descriptions do
not become parallel authority. See `docs/Aurelian/sdsl-v-cross-repo-audit.md`
and `docs/Copeland/sdsl-v-gpu-profile.md`.

## 26. AURELIAN-SDSLV-PORT-M1 compute update

The first production compute slice now follows the ownership above. Copeland's
ordinary parser owns `@compute`, `@numthreads`, `@binding`, and `@builtin`
syntax nodes and exact spans. Its explicit `Gpu` profile binds the closed M1
types, resources, access law, builtin, functions, locals, indexing, arithmetic,
comparison, `if`, return, and reachable helper closure into
`vdmir.semantic.v1`. The `.v.ts` suffix remains only a convention.

`Aurelian.Shaders.Compute` consumes that VD-MIR directly and is the sole M1
route to deterministic HLSL, DXC `cs_6_0`/Vulkan 1.3 SPIR-V, `spirv-val`, and
structural metadata. The historical `Aurelian.Shaders.Language` parser,
validator, stage extraction, legacy emitter, and graphics smoke VD-MIR remain
retained for historical tests but are not callable semantic authorities for the
Copeland compute route. Aurelian.Graphics is unchanged.

## 27. AURELIAN-SDSLV-PORT-M2 graphics stream update

The same ownership now covers an untextured linked graphics program. Copeland's
ordinary parser distinguishes GPU shader streams from layout streams with
separate nodes. Its graphics binder owns role inference, locations, position,
targets, interpolation, helper closure, and linkage. `vdmir.semantic.v1`
remains the contract with feature level `graphics.m2` and an explicit linked
`GraphicsProgram`.

`Aurelian.Shaders.Graphics` generates HLSL structs and semantics, compiles
`vs_6_0` and `ps_6_0`, validates both Vulkan 1.3 SPIR-V modules, and records
host-facing interface facts. Aurelian.Graphics remains unchanged: no runtime
pipeline or renderer API was added. Texture, sampler, Sample, semantic spaces,
and the canonical material uniform are the exact next language slice.

## 28. AURELIAN-SDSLV-PORT-M3 forward-textured update

The bounded graphics language surface is complete. `vdmir.semantic.v1` feature
level `graphics.m3` carries nominal semantic spaces, builtin/resource streams,
typed texture/sampler/Sample semantics, explicit set-zero bindings, resource
visibility, and the canonical immutable 32-byte tint/roughness material layout.
`Aurelian.Shaders.Graphics` generates resource declarations and validates both
Vulkan 1.3 SPIR-V stages. `Aurelian.Graphics` remains untouched.

The next renderer milestone must consume vertex/pixel SPIR-V and renderer
metadata as one `CompiledGraphicsProgram`; it must not reparse shader source or
HLSL or treat SPIR-V reflection as semantic authority. Host upload code may be
generated from material metadata, but no parallel handwritten material layout
may become authoritative.

## 29. AURELIAN-NATIVE-FORWARD-TEXTURED-M0 update

The compiler/renderer boundary is now qualified. `Aurelian.Rendering.Contracts`
owns `CompiledGraphicsProgram`, combining compiled SPIR-V stages with
renderer-neutral vertex, target, resource, visibility, and material-layout
metadata. `Aurelian.Shaders` alone projects graphics.m3 VD-MIR and DXC output
into that contract. `Aurelian.Graphics` consumes the typed contract and does
not reference Copeland, VD-MIR, HLSL, or DXC.

The existing Vulkan plant, allocator, texture/view, staging uploader, barriers,
render pass, framebuffer, shader-module/pipeline, command pool, timeline fence,
submit, and draw owners remain authoritative. A bounded descriptor/sampler and
readback seam now realizes one set-zero texture/sampler/uniform interface and a
64x64 offscreen target. Compiler metadata constructs the interface; SPIR-V
decorations only verify binding and field-offset agreement. The canonical quad
produces deterministic textured/tinted RGBA bytes with validation enabled.

This proves renderer plumbing, not a new application-facing renderer. The next
bounded step is a minimal reusable native 2D quad submission primitive; it does
not authorize TinyFarm integration, text, material/asset frameworks, generalized
batching, cameras, transforms, or render graphs. See
`docs/Aurelian/aurelian-native-forward-textured-m0-report.md`.

## 30. AURELIAN-NATIVE-2D-QUAD-M1 update

The first reusable native 2D primitive is now qualified. `VulkanOrderedQuadRenderer`
reuses the existing Vulkan plant and owns one compiler-derived pipeline/layout,
render pass/target/framebuffer, nearest-clamp sampler, descriptor pool, mapped
vertex buffer, and command/fence/upload infrastructure. Application-facing texture
identity is an opaque handle over persistent raw RGBA8 resources; Vulkan handles do
not escape.

Each pass records immutable axis-aligned pixel-space quad values containing a
destination rect, ordered UV rect, texture handle, and tint. Submission sequence is
the only order law. Adjacent identical texture/tint pairs may share a draw, but the
renderer never sorts. The unchanged M3 shader keeps `sample * tint`; compiler
metadata remains construction authority for vertex, descriptor, and material ABI,
with SPIR-V reflection only cross-checking it.

The 256x256 canonical proof, 100-quad stress, and 100 persistent-pass stress pass
with Khronos validation and stable same-machine hashes. This remains an offscreen,
opaque, synchronous renderer mechanism with optional proof readback—not a sprite
engine, retained scene, compositor, or swapchain path. See
`docs/Aurelian/aurelian-native-2d-quad-m1-report.md`.

## 31. Qualified Machina text handoff

Machina now exposes a renderer-neutral `MachinaGlyphRun` containing line baselines,
token anchors, glyph origins, advances, and plane bounds. DirectOutline and CPU MSDF
consume the same semantic placement; atlas storage cannot decide layout. Avalonia is
an isolated test/tooling oracle and is not an Aurelian or Machina production
dependency. A future Aurelian text adapter may translate each non-whitespace glyph
placement into ordered native quad submissions, but it must not recompute shaping or
advance from atlas dimensions. `MACHINA-MSDF-REALIZATION-M1` now qualifies the
direct-vector MSDF field, deterministic atlas metadata, and CPU median-RGB
reconstruction. The next native step is an Aurelian adapter from the qualified
`MachinaGlyphRun` plus atlas metadata to ordered glyph quads and the existing native
shader path; no layout or atlas-owned placement belongs in Aurelian.
## Native MSDF text realization (M2)

Qualified text crosses into native rendering only through `AurelianGlyphRunAdapter`
in the renderer-specific `Aurelian.Machina.Graphics` integration leaf. The established
`Aurelian.Machina` bridge remains renderer-neutral. Machina remains authoritative for `MachinaGlyphRun`, field
planes, pixel range, atlas storage, and UVs. Aurelian owns persistent opaque textures,
the fixed MSDF pipeline/sampler/blend state, ordered glyph quads, offscreen execution,
and readback. `CompiledGraphicsProgram` remains the sole descriptor/material authority.
No font parsing, shaping, baseline derivation, or atlas packing belongs in Graphics.

## Native 2D primitive families (M4)

The native ordered-quad path has three deliberately closed realization families:

- Solid/Textured uses a sampled image and tint for raster content.
- MSDF uses precompiled multi-channel distance fields for arbitrary static contours,
  including scalable text.
- AnalyticSDF uses a textureless compiler-owned GPU program for simple parametric
  RoundedRect, Circle, and Pill geometry.

Aurelian treats simple 2D vector geometry as analytic GPU programs, not pre-rasterized
assets. MSDF is used for arbitrary precompiled contours; analytic SDF is used for
simple parametric contours.

Machina owns shape kind, rectangle, uniform corner radius, fill, border, clipping,
ordering, and hit-test meaning. `Aurelian.Machina.Graphics` adapts that presentation
intent to one native quad. `Aurelian.Graphics` owns only compiler-described pipeline,
material-buffer, vertex, blend, Vulkan, and lifetime realization. No Vulkan type enters
Machina and no Machina type enters low-level Graphics. A shape-exact input geometry,
generic SDF expression tree, arbitrary path, ellipse, per-corner radius, gradient,
shadow, or blur is not part of M4.

## Compiled vector icon realization (M5)

The final native 2D content taxonomy is:

- Raster/Texture is sampled image content.
- AnalyticSDF is simple parametric vector geometry: RoundedRect, Circle, and Pill.
- MSDF is arbitrary compiled vector contours, including glyphs and monochrome icons.

Machina owns the typed `MachinaVectorIconId`, destination rectangle, tint, ordinary
layout, clipping, painter order, and rectangular hit-test meaning. The vector asset
compiler owns the bounded source parser, canonical contours, non-zero winding,
content identity, field generation, and deterministic atlas. `Aurelian.Machina.Graphics`
maps that renderer-neutral primitive to one `NativeMsdfQuadSubmission`; low-level
Graphics receives only field texture, rectangle, UVs, tint, and MSDF parameters.

SVG is an authoring/import format, not a runtime rendering model. Aurelian renders
compiled vector fields, not SVG DOMs. Runtime drawing never parses source, tessellates
contours, generates fields, or packs atlases. The intended production flow is
`assets/icons/*.svg -> build/compiler -> compiled MSDF asset bundle -> app/runtime`.

## Native ordered layer composition (M0)

Native layers compose in semantic compositor order. `Aurelian.Composition` remains
renderer-neutral and dependency-free; it continues to own layer identity, the
`(ZOrder, LayerId ordinal, registration sequence)` law, visibility, viewport,
lifecycle and input focus/capture. `Aurelian.NativeComposition` consumes that exact
ordered presentation result and owns the Vulkan frame boundary.

Direct same-target composition is preferred when compatible. The compositor-owned
`VulkanNativeFrameTarget` fixes extent, `R8G8B8A8_UNORM`, sample count one, clear and
final readback/presentation. The first direct pass clears; later direct passes load
and preserve the target. A layer-bound renderer cannot end its own shared pass or
replace the target. Textured world, analytic Machina shapes and MSDF text therefore
coexist as sequential compatible passes with zero intermediate color surfaces and
zero composition copies.

Offscreen surfaces are explicit tools for isolation/effects, not mandatory layer
representation. An explicit `OffscreenSurface` request currently fails closed until
a bounded effect/isolation implementation exists. No render graph, scene graph or
world/UI drawable unification is implied. World painter order remains inside the
world presenter; Machina presentation order remains inside its presenter; only
top-level semantic layers are compositor-sorted.

The same boundary can later target a swapchain and order `World`, `Particles`,
`Machina`, `Debug` or editor/inspection layers without changing their semantics.
See `docs/Aurelian/aurelian-native-layer-compositor-m0-report.md`.

## Deterministic 2D spatial facts (M3)

`Aurelian.Spatial2D` is the engine-owned, pure-managed query and character-movement
substrate. AABB/circle shapes and tile-authored obstacle adapters are expressed only
in explicit world units. Static worlds are immutable and traverse colliders by stable
typed identity. Overlaps order by distance then ID; sweeps order by normalized time of
impact then ID. Normals point from obstacle toward moving shape. Equal-time contacts
are ID-ordered before bounded slide removes blocked normal components.

The authority flow is:

```text
game movement request
  -> Aurelian.Spatial2D query/sweep
  -> contact facts and accepted displacement
  -> game resolver
  -> authoritative game state
```

Triggers return overlap and entered/stayed/exited facts but never block or call game
code. The game owns trigger history, actor policy, combat, inventory, saves, and all
state changes. DotRecast proposes navigation paths; spatial sweep validates actual
motion. A future full dynamics engine may coexist through a separate integration and
must not turn this query substrate into an engine-owned physics scene. See
`docs/Aurelian/aurelian-spatial-2d-m3-report.md`.

## Native game audio (M4)

`Aurelian.Audio` owns engine-neutral realtime playback policy: typed assets/events/voices/buses, scoped resident PCM resources, bounded allocation, deterministic stealing, gain/mute, explicit-clock fades and crossfades, simple 2D pan/attenuation, event dedupe, completion queues, diagnostics, and null/offline mixing. `Aurelian.Audio.NAudio` is the Windows device leaf; backend types do not enter game code. `AurelianGameHost` owns update, focus, and disposal.

Dominatus remains the optional generation/provider owner. `Dominatus.Audio.Aurelian` adapts a generated `AudioArtifact` into the same PCM-WAV resource path used by authored files. TinyFarm projects accepted semantic results into cues; Core does not reference audio. Mixer state and voices are never save/replay/gameplay authority. Streamed music, compressed formats, and a Linux device leaf remain explicit backend/resource seams. See `docs/Aurelian/aurelian-game-audio-m4-report.md`.

## Renderer-neutral dialogue presentation (M7b2)

The VN demo and TinyFarm directly consume one immutable
`Ariadne.OptFlow.Presentation.DialoguePresentationSnapshot`. It carries stable
dialogue/operation identity, operation kind, speaker/content, ordered visible choices,
selection, advance/choice lifecycle facts, completion/cancellation, and pending
actuation identity. It carries no renderer, portrait, expression, background,
auto/skip, save-button, camera, focus object, or panel-layout state.

The authority flow is:

```text
Ariadne/OptFlow semantic runtime
  -> DialoguePresentationSnapshot
  -> application skin and input adapter
  -> typed effect at application boundary
  -> application resolver
  -> authoritative application state
```

TinyFarm proves the non-VN style: the native world remains visible, a compact Machina
lower-third overlays it, InputMan dialogue actions suppress movement/tool input, and
an accepted `GiveIntent` owns the Wild Mint transfer. TinyFarm chooses a full semantic
pause and speaking-NPC stability; neither is generic dialogue law. Deliverance stores
Dominatus dialogue chunks and intentional selection state, then reconstructs the UI
without dispatch replay. See
`docs/Aurelian/aurelian-tinyfarm-dialogue-consumer-m7b2-report.md`.
