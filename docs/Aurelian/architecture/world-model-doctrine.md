# Aurelian World Model Doctrine

## 1. Purpose

Aurelian needs a world model before world implementation begins. `Aurelian.World` should not grow by accident from smoke scaffolding, renderer needs, asset shortcuts, or copied ECS habits; it needs an explicit doctrine that states what a world is, where its boundaries are, and how it relates to Dominatus, actuators, rendering, assets, and shaders.

This doctrine preserves lessons from Stri-V, WyrmCoil, Machina, and Dominatus:

- Stri-V showed how hidden global state, nullable lifecycle semantics, callback-order behavior, and policy-heavy managers make change propagate through unrelated systems.
- WyrmCoil showed the value of explicit intermediate forms, command plans, structured failure, and deterministic compilation-style pipelines.
- Machina remains useful as reference material for runtime ideas, but Aurelian must not inherit an editor-first or service-locator runtime shape.
- Dominatus gives Aurelian a policy/orchestration spine: decisions and acts are explicit, while data ownership remains outside the policy engine.

This document defines the design law and architectural boundaries for `Aurelian.World`. It is a docs-only milestone and does not implement the world model.

## 2. Primary design law: locality of change

```text
Locality of change is the primary design law.
```

Good code lets meaningful changes stay local. When a townie behavior changes, the change should not require edits to renderer submission, asset loading, global processors, unrelated NPCs, and editor lifecycle hooks. When a shader manifest changes, the change should stay in shader and asset boundaries rather than forcing world/runtime changes.

Spaghetti code is unsafe because changes propagate. The danger is not merely ugliness; it is that a small change becomes impossible to reason about because state, policy, and side effects are scattered through distant objects.

Stri-V proved that hidden global state and nullable lifecycle semantics destroy locality. If an object being `null` means "not created yet," "destroyed," "not loaded," "disabled," or "waiting for a callback," then every consumer must learn and duplicate lifecycle policy. That is propagation disguised as convenience.

Rust borrow-check pain around long ownership chains is another manifestation of poor locality. When a small mutation requires threading ownership through a deep parent chain, the model is telling us that the boundary is wrong. Aurelian should learn from that pain without simply copying Rust mechanics into C#.

Aurelian should optimize first for locality of change, then performance inside those boundaries. Runtime representation may be flattened, indexed, cached, pooled, or snapshot for speed, but those performance forms must not become the authoring model or erase the boundaries that make changes local.

## 3. Universal decomposition: data, composition, logic

Every meaningful software object can be decomposed into three aspects:

| Aspect | Question | Examples |
| --- | --- | --- |
| Data | What is it? | position, profile, needs, mesh ref, text, style |
| Composition | What is it made of? | child units, traits, slots, contained UI nodes |
| Logic | What does it do? | handlers, decisions, dispatch, utility, local rules |

This applies uniformly to:

- world;
- scene;
- room;
- NPC;
- player;
- document;
- webpage;
- UI button/card;
- shader module;
- asset manifest.

A world is not only a bag of entities. It has data, composition, and logic boundaries. A webpage is not only DOM nodes. It has data, nested composition, and local behavior. A shader module is not only text. It has declarations, imports/composition, and logic. An asset manifest is not only a file list. It has intent data, composition of sources/artifacts, and validation/build behavior.

Aurelian should make this decomposition explicit so that implementations can vary without losing conceptual locality.

## 4. Component definition

```text
A component is a reusable local composition unit.
```

An Aurelian component should contain or declare:

- data shape;
- child composition;
- local logic hooks/surface;
- declared inputs;
- declared outputs.

Aurelian explicitly rejects:

```text
component = passive mutable data bag processed globally
```

Old ECS correctly wanted reusable composition. Reusing `Transform`, `Renderable`, `Inventory`, or `Needs` is a good instinct. The common failure is moving meaning out of the component and into global processors that own query shape, policy, mutation, and side effects. Once that happens, the component becomes a passive bag and the real object behavior is scattered across many systems.

Aurelian components preserve locality by keeping data, composition, and local logic meaning together. A component may still be compiled into storage columns, indexes, and snapshots at runtime, but its source-level definition should remain a readable local unit of meaning.

## 5. Unit / locality boundary

The conceptual primitive for the world model is:

```text
WorldUnit
```

`WorldUnit` is now represented at M0 by descriptors and resolved units under `Aurelian.World.Units`. M0 keeps the boundary small: identity, kind, immediate composition, opaque logic references, resolver output, and deterministic hierarchy queries.

A `WorldUnit` is a locality boundary:

- a parent composes a child but should not inspect or mutate child internals;
- a child affects the outside world only through declared outputs;
- a parent affects a child only through declared inputs;
- everything crossing the boundary should be typed.

Webpage analogy:

- A page composes cards.
- A card composes buttons.
- The page arranges the card but should not reach into the button's internal hover/click state.
- The button owns its internal behavior and emits typed outputs such as `Clicked`, `Submitted`, or `RequestedNavigation`.

Game analogy:

- A town composes households.
- A household composes people.
- A person may contain needs, schedule, social memory, and local behavior surfaces.
- The town should not directly mutate a person's social memory internals. It should send typed inputs, observe typed outputs, or request an actuator to perform a named mutation.

This boundary is not about preventing all communication. It is about making communication explicit, typed, and reviewable.

## 6. Authoring composition vs runtime flattening

Authoring can use reusable conceptual composition:

```text
Marge = Human + Woman + Elder + Retired + Neighbor
```

Runtime should resolve that composition to shallow/local units and typed stores/indexes. Aurelian should avoid deep inheritance chains such as:

```text
Animal -> Mammal -> Human -> Woman -> Elder -> Marge
```

as runtime architecture. Deep inheritance makes source-level reuse look convenient while pushing change cost into ancestors, descendants, override order, and fragile lifecycle assumptions.

Deep conceptual reuse should compile into local resolved units. The authored definition may say `Human + Elder + Neighbor`, but the runtime should be able to materialize the resolved data shape, child composition, logic references, input channels, output channels, and query indexes without forcing every tick to traverse a conceptual hierarchy.

Important principle:

```text
Authoring may be compositional.
Runtime should be flattened where useful.
Source-level changes should remain local.
```

## 7. Replacing ECS processors with orchestration ladder

Aurelian should use the Dominatus/TinyTown-style pattern where state is explicit, decisions are bounded, and side effects are applied through named execution boundaries. If a TinyTown sample is present in vendored/reference material, it should be treated as inspiration for data as records, decision logic as utility/dispatch, side effects as explicit execution, and optional LLM calls as bounded tools rather than orchestrators.

The logic complexity ladder is:

```text
direct statement
→ simple branch
→ dispatch table / switch
→ utility decision
→ optional LLM-assisted decision
→ validated actuator request
```

Use the smallest rung that fits:

- A conditionless action can just be a statement or function.
- One condition can be an `if` or `switch`.
- Many conditions and many choices should use utility scoring or another explicit decision function.
- Ambiguous human-ish decisions may use LLM calls, but results must be validated, clamped, typed, and treated as suggestions or bounded tools.
- Mutation happens through actuators.

Aurelian explicitly rejects:

```text
global processor owns query + policy + side effects
```

A processor that queries all matching objects, decides what should happen, mutates world state, triggers assets, and emits render work is a policy knot. It makes change non-local because data ownership, behavior policy, and side effects become inseparable.

## 8. World responsibilities

`Aurelian.World` should own:

- entity/unit identity;
- data stores;
- composition documents;
- resolved world documents;
- local world snapshots;
- transform/hierarchy state;
- typed world queries;
- serialized world chunks eventually.

`Aurelian.World` should not own:

- behavior policy;
- renderer backend;
- shader compiler;
- asset build pipeline;
- UI/tool host;
- global service locator.

The world owns data and structure. It should provide deterministic ways to resolve, snapshot, and query that data. It should not become the place where every system hides policy because it is convenient to reach all state from there.

## 9. Dominatus relationship

Dominatus owns policy/orchestration. It observes world state through boards/observations, chooses or coordinates acts, and emits act requests.

Aurelian actuators apply acts to world, asset, or render state. Dominatus should not become the data store. World should not become the behavior policy engine.

Suggested loop:

```text
input/messages
→ mailbox
world observations
→ board
Dominatus tick
→ act requests
actuator dispatch
→ world mutations
simulation passes
→ snapshots
```

This keeps responsibilities separate:

- world owns state;
- Dominatus owns orchestration and policy flow;
- actuators own mutation boundaries;
- snapshots expose deterministic outputs for downstream systems.

## 10. Actuator relationship

Actuators are mutation boundaries. An actuator accepts a typed request and returns a typed result/outcome. It names side effects rather than allowing arbitrary mutation across boundaries. A14 makes the first such boundary concrete under `Aurelian.Actuation.World`: spawn, destroy, attach, detach, and replace-descriptor requests operate over `WorldDocument` values and return `WorldActuationResult` statuses and diagnostics. A16 extends that boundary to `WorldDataDocument` through typed requests for setting/removing unit names and 2D transforms; data-store mutation still returns structured results rather than mutating original store state in place.

Side effects should be named, including:

- spawn unit;
- destroy unit;
- set transform;
- attach parent;
- register renderable;
- set active camera;
- load asset;
- compile shader.

Actuators preserve locality by preventing random mutation across boundaries. A parent unit, behavior policy, renderer, or asset tool should not casually mutate arbitrary world internals. Instead, code requests a named effect and receives a structured outcome that can be tested, logged, rejected, or replayed. For world unit M0, rejected and no-op results keep the original document; applied results return a new `WorldDocument` that has been validated by `WorldUnitResolver`.

## 11. Render extraction relationship

The world does not talk to the GPU. The world extracts render snapshots; render snapshots become command plans; render backends execute plans.

Aurelian rendering path:

```text
World data/stores
→ RenderSnapshotExtractor
→ RenderSnapshot
→ RenderCommandPlan
→ Null/GPU backend
```

This follows WyrmCoil command-plan lessons: separate source data from planned execution, make intermediate products inspectable, and keep backend-specific work behind a later boundary.

Failures should be structured data, not hidden exceptions. Snapshot extraction and command-plan generation should be able to report missing meshes, invalid materials, unsupported features, or backend limitations as typed diagnostics/outcomes.

## 12. Asset/shader relationship

Asset manifests describe source-of-intent. The asset pipeline validates and builds deterministic artifacts. Shaders follow:

```text
source -> parse -> validate -> emit -> artifact -> optional external validation
```

World references runtime asset handles, not Stride content objects. A world unit may reference a mesh, material, shader artifact, texture, or document through an Aurelian-owned handle/identifier. It should not depend on Stride content objects, editor object graphs, shader compiler internals, or asset build tasks.

This keeps asset authoring, shader compilation, runtime world state, and render extraction independently changeable.

## 13. File/module doctrine

Recommended source file shape for substantial world units:

```text
Data
Composition
Logic
Inputs/Outputs
```

Example conceptual file:

```text
Townie.cs
  TownieData
  TownieComposition
  TownieLogic
  TownieInputs
  TownieOutputs
```

or:

```text
Door.cs
  DoorData
  DoorComposition
  DoorLogic
  DoorInputs
  DoorOutputs
```

Aurelian should not require every tiny file to have all sections. Small identifiers, values, and trivial helpers can remain small. But every substantial unit should make the separation explicit so reviewers can see what the unit is, what it is made of, what it does, what it accepts, and what it emits.

## 14. M0 implementation implications

The first world implementation milestone is now:

```text
A13 — World Unit M0
```

A13 M0 includes:

- `UnitId`;
- `UnitKindId`;
- `WorldUnitDescriptor`;
- `UnitChild`;
- `UnitComposition`;
- `UnitLogicRef` as opaque identifier only;
- `WorldDocument`;
- `ResolvedWorld`;
- simple resolver for parent/child relationships;
- deterministic snapshot/query of units and children;
- tests for locality boundaries and shallow composition.

A13 M0 intentionally does not include:

- full ECS;
- processors;
- entity manager;
- renderer;
- assets;
- physics;
- complex behavior;
- LLM calls;
- deep inheritance system.

A13 makes the smallest possible world model real: IDs, descriptors, immediate-only composition, a resolver, resolved-world query/snapshot shape, diagnostics, and tests. `ResolvedWorld` is the M0 snapshot/query object; it exposes deterministic pre-order traversal, immediate child queries, transitive descendant queries, parent lookup, and unit lookup. Behavior runtime, Dominatus integration, actuators, renderer integration, asset integration, physics, editor workflows, and blackboards remain future work. Blackboards remain logic/runtime state, not durable world data.

A14 adds the first actuator contracts over this model:

```text
A14 — World actuator request/result contracts M0
```

A14 lives in `Aurelian.Actuation.World`, not `Aurelian.World`. It adds pure request records for spawning units, destroying leaf units, attaching immediate children, detaching immediate children, and replacing descriptors; it adds status/diagnostic/result contracts; and it applies valid mutations by creating new world documents rather than mutating the input document in place. Applied mutations are resolved through `WorldUnitResolver` before they are returned. Invalid mutations return deterministic diagnostics such as duplicate unit, missing parent/child, duplicate child/slot, root destruction, child-bearing destruction, or resolver-invalid mutation. Dominatus bridges, typed data stores, behavior runtime, blackboards, renderer, assets, shaders, physics, editor workflows, and ECS managers/processors remain outside A14.

A16 adds the first typed world data stores under `Aurelian.World.Stores`. These stores represent data, not logic: `UnitNameStore` maps `UnitId` to a name/label value, and `Transform2Store` maps `UnitId` to a library-free 2D transform value. `WorldDataDocument` groups those stores with the existing `WorldDocument` without replacing the composition model, while `WorldDataSnapshot` resolves a valid document into queryable unit snapshots with optional names, identity-default transforms, and immediate children. These stores are explicit and typed; they are not a generic component framework, do not define `IComponent`, and do not introduce an entity manager, processors, behavior runtime, blackboards, renderer integration, assets, shaders, physics, or Dominatus integration. Mutation is routed through `WorldDataActuator` request/result contracts in `Aurelian.Actuation.World`; invalid names, missing units, and non-finite transforms are rejected with diagnostics, remove-missing operations are no-ops with info diagnostics, and applied changes return new `WorldDataDocument` values. Future stores may include 3D transform, renderable references, camera references, physics state, or navigation state, but they should remain Aurelian-owned contracts and `Aurelian.World` should stay library-free unless a later policy milestone explicitly changes that boundary.

## 15. Anti-goals

Aurelian world architecture explicitly rejects:

- no Stride ECS recreation;
- no `EntityManager`;
- no processors as core runtime;
- no global service locator;
- no null lifecycle state;
- no child mutating unrelated state;
- no parent inspecting child internals;
- no renderer dependency in world;
- no editor-first design.

## Addendum A — Immediate composition rule

```text
A unit should declare only its immediate composition.

If OldWoman composes Woman and Elder, it does not also manually declare Human and Animal. Those are responsibilities of Woman and Human respectively.

Transitive composition belongs to the resolver, not the authoring file.
```

## Addendum B — Logic as state machines

```text
Logic means states and transitions.

If there is no possible transition, the concept is data or invariant, not logic state.

if/else, switch, dispatch tables, utility decisions, HFSMs, and LLM-assisted choices are different representations of state transition logic at different complexity levels.
```

## Addendum C — Agentic locality

```text
Aurelian uses agentic in the older/general sense: local units have capacity for autonomous state transition through declared inputs, outputs, and actuators.

LLMs may assist ambiguous decisions, but agentic behavior does not require LLMs.

## A20 renderable world data and extraction

A20 adds a minimal renderable store to the world model without changing the doctrine boundary: renderable data is world data, but rendering contracts and backend dependencies are not world dependencies.

Rules:

- `Aurelian.World.Stores.Renderable2DStore` is an explicit typed store, not an ECS component manager.
- `Renderable2DData` carries symbolic `WorldMeshRef` and `WorldMaterialRef` values only.
- These symbolic refs are strings owned by the world layer for now; they are not asset handles, shader handles, backend handles, or `Aurelian.Rendering.Contracts` refs.
- `WorldDataSnapshot` may include renderable data for units, including invisible renderables, because extraction decides what becomes a render item.
- `Aurelian.Runtime.Rendering.WorldRenderSnapshotExtractor` is the composition-layer bridge that maps resolved world data into `RenderSnapshot` DTOs.
- The M0 extractor creates a default symbolic camera and maps visible renderable units to `RenderItem2D` records; real camera stores and scene systems remain deferred.

The validated headless path is now:

```text
WorldDataDocument -> RenderSnapshot -> RenderCommandPlan -> NullRenderer
```

This path proves the data flow while preserving the no-GPU, no-window, no-assets, and no-shaders boundaries.

## A64 runtime tick doctrine note

A64 adds `AurelianRuntimeSession` as the preferred runtime tick path. The session is intentionally Dominatus-shaped: tick facts are written to a runtime agent blackboard, a Dominatus HFSM node emits a neutral `AurelianRuntimeTickAct`, actuation completion is observed through Dominatus events, and typed runtime diagnostics report lifecycle, validation, cancellation, runner, and actuation failures.

This reinforces the world doctrine boundary: world data remains world-owned, Dominatus owns behavior orchestration, actuators remain the seam for side effects, and Runtime does not become a global world/render/graphics processor. Render extraction, graphics submission, and real world mutation acts are deferred until their local boundaries are explicit.

## A65 frame-loop/runtime connection note

A65 connects frame orchestration to runtime orchestration without moving behavior into Core. The frame loop may call the runtime tick step once per frame, but runtime tick behavior remains in `Aurelian.Runtime.Sessions.AurelianRuntimeSession` and is still Dominatus-backed.

This preserves the doctrine split: Core coordinates the high-level frame path, Runtime/Dominatus owns behavior policy flow, world/render extraction is still deferred, Runtime has no graphics/Vulkan responsibility, and `ParallelAiWorldRunner` remains a future implementation detail behind the runner seam.
