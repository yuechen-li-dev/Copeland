# Aurelian Vision 1 — Engine as Explicit Orchestration

## 1. Purpose

This document records the first major vision checkpoint for the Aurelian Engine.

It is not a milestone plan, not an implementation spec, and not a promise that every idea here should be built immediately. It is a north-star document for future contributors, future LLM collaborators, and future versions of ourselves.

## Status note

This document is a vision document, not a claim that every described subsystem exists today. Current implemented reality is tracked in `aurelian-checkpoint-a72.md`, milestone audits, and the MVP roadmap. Sections about external renderers, Machina UI, Margaret, ray marching, multi-plant/multi-GPU, and LLM/DM gameplay are directional goals unless explicitly marked otherwise.

Aurelian has now crossed an important threshold: it can show a visible triangle through a deliberately over-architected but honest path involving asset manifests, shader artifacts, Core frame-loop orchestration, a Dominatus-backed runtime session, compositor policy, graphics mechanism, swapchain presentation, and a sample-owned window lifecycle.

That triangle is not the product. It is proof that the seams are real.

The engine is still pre-MVP. But the thesis is now clear.

```text
Aurelian is not an engine with AI bolted on.

Aurelian is an engine whose core orchestration model is Dominatus-shaped.
```

## 2. The Stri-V / Stride lesson

Aurelian began partly from the attempt to understand and salvage ideas from Stri-V and Stride.

The conclusion was not that Stride lacked ambition. Stride is full of ambitious subsystems. The problem is that its engine core expresses behavior through implicit, reflection-heavy, globally entangled patterns where state propagation is hard to inspect and hard to localize.

Professional summary:

```text
Stride's engine core was effectively an implicit, reflection-heavy, less-local version of what Aurelian is making explicit through Dominatus, typed contracts, and actuator boundaries.
```

The core lesson is not “Stride bad, Aurelian good.” The useful lesson is:

```text
A game engine is fundamentally a behavior orchestration system.

If that orchestration is hidden inside reflection, processors, service-like lookups, implicit component scans, and backend/frontend entanglement, the engine becomes difficult to reason about.

Aurelian should make orchestration explicit.
```

This means:

* world state should be explicit;
* facts should be inspectable;
* policy should be separable from mechanism;
* mutation should go through typed actuators;
* rendering should consume snapshots/plans, not own game truth;
* runtime behavior should be Dominatus-shaped, not a hand-written mini-Dominatus;
* subsystem integration should happen in Core, not through accidental project coupling.

## 3. Core thesis

Aurelian is an orchestration-native engine.

Every major subsystem should fit into this pattern:

```text
facts -> policy -> acts -> validated mechanism -> results -> facts
```

In Aurelian terms:

```text
Data:
  explicit state, documents, stores, assets, facts

Composition:
  local, typed relationships between units, renderables, assets, policies, and mechanisms

Logic:
  states, transitions, Dominatus policies, utility decisions, actuation, validation

Mechanism:
  graphics, physics, UI renderer, asset loading, shader compilation, external renderers, reference renderers

Actuation:
  typed requests/results that cross from policy into mechanism or mutation
```

Aurelian should avoid hidden state wherever possible. If something mutates the world, submits GPU commands, runs a compositor, creates a scene, spawns an NPC, or asks an LLM to propose a quest, that action should be visible as a typed act or request with a typed result.

The goal is not “simple code” in the aesthetic sense. The goal is locality of change.

```text
Good engine code is code where changes do not propagate unpredictably.
```

## 4. Current engine identity

Aurelian.Core is the engine integration spine.

It is not merely a utilities project. It is the crown: the place where prepared subsystems are wired together through explicit contracts.

Current intended project roles:

```text
Aurelian.Core:
  engine integration spine;
  engine lifecycle shell;
  frame pump/frame loop orchestration;
  subsystem wiring;
  Core-side compositor mechanism abstraction;
  Core-to-Graphics adapter where appropriate.

Aurelian.Runtime:
  Dominatus-backed runtime policy/session layer;
  runtime tick;
  compositor policy;
  future world/NPC/scene/DM policies.

Aurelian.Rendering.Contracts:
  neutral DTOs for rendering, shader programs, command plans, compositor requests/results, and future renderer-independent data.

Aurelian.Graphics:
  Vulkan backend mechanism;
  graphics plants, resources, barriers, pipelines, draw, swapchain, compositor passthrough;
  no Dominatus;
  no Runtime dependency.

Aurelian.World:
  world documents, unit descriptors, composition, typed stores, snapshots, resolver/query surfaces.

Aurelian.Actuation:
  typed world mutation and actuation boundaries.

Aurelian.Shaders:
  SDSL-V/HLSL/DXC/SPIR-V build-time shader pipeline.

Aurelian.Assets:
  TOML asset manifests and runtime loading of artifact data into neutral contracts.

Aurelian.Rendering.Null:
  backend-independent command-plan proof.

samples/Aurelian.VisibleTriangle:
  human-facing integration sample;
  sample-owned window/Vulkan setup;
  current executable proof.
```

The dependency doctrine should remain:

```text
Runtime must not reference Graphics.
Graphics must not reference Runtime or Dominatus.
Rendering.Contracts must remain neutral.
Core may integrate subsystems because Core is the engine spine.
Samples/integration tests may compose everything.
```

## 5. Current executable proof

The current visible sample proves the following path:

```text
sample-owned prepared Vulkan setup
  -> Aurelian.Assets manifest shader load
  -> checked-in shader.toml + .spv.hex artifact decode/hash validation
  -> neutral CompiledShaderProgram
  -> AurelianEngine
  -> AurelianRuntimeSession
  -> AurelianFrameLoop
  -> runtime tick step
  -> frame pump
  -> sample-local event pump before acquire
  -> per-frame swapchain acquire
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor mechanism
  -> per-frame present
  -> sample-local event pump after present/close detection
```

This proves that the architecture can compose.

It does not prove that Aurelian has a production host, full renderer, material system, world-to-render scene, input system, asset manager, editor, or gameplay loop.

The visible triangle is a valuable proof, not an engine MVP.

## 6. External engines as dumb renderers

Aurelian should not try to beat Unreal at being Unreal.

Near-term usability may come from making existing engines act as dumb renderers:

```text
Unreal
Unity
O3DE
Stride
```

These engines have mature renderers, import pipelines, animation systems, and editor viewports. They also carry behavior models that Aurelian should not inherit.

The strategic idea:

```text
External engines may render pixels.

Aurelian owns truth.
```

Aurelian can own:

* world state;
* runtime policy;
* behavior orchestration;
* NPC and scene logic;
* asset interpretation;
* compositor decisions;
* LLM/DM actuation in the future.

External renderers can receive:

* render snapshots;
* scene update commands;
* material/mesh/light/camera data;
* UI surfaces;
* debug overlays.

This makes existing engines render plants, not world authorities.

Example future shape:

```text
Aurelian.Renderer.Unreal:
  consumes Aurelian render snapshots/command plans;
  maps them to Unreal scene primitives;
  uses Nanite/Lumen/Unreal renderer;
  never owns behavior truth.

Aurelian.Renderer.Unity:
  same concept for Unity.

Aurelian.Renderer.Stride:
  potentially ironic but useful;
  Stride as pixels, not policy.
```

This could make Aurelian usable long before its native Vulkan renderer becomes competitive with mature commercial engines.

## 7. Materials: Wyrmcoil TOML and flattened MaterialX-shaped graphs

Aurelian should keep the Wyrmcoil material direction:

```text
TOML material assets
  -> flattened MaterialX-ish graph semantics
  -> validated/lowered material artifacts
  -> runtime parameter/binding data
```

The principle:

```text
TOML for readable material intent.
MaterialX compatibility for interchange.
Flattened artifacts for runtime.
```

A material authoring shape can be graph-like without forcing the runtime to interpret an arbitrary dynamic graph every frame.

A future material artifact should likely include:

* material id;
* material model;
* shader artifact references;
* parameter block layout;
* texture references;
* flattened node list;
* validation diagnostics;
* binding metadata;
* future renderer compatibility hints.

Do not build a material megasystem too early. The first useful step is a small TOML material asset that can point to shader artifacts and plain parameters.

## 8. UI: Machina, Avalonia, and strict 2D/3D separation

Machina UI is a separate project, but it fits Aurelian’s direction.

Near-term strategy:

```text
Use Avalonia for practical UI controls/rendering.
Carry over Machina.Layout and only the parts needed for explicit layout/surface composition.
Keep 2D UI and backend 3D renderer strictly separated.
```

Aurelian should not weld UI into the 3D renderer.

Instead:

```text
UI renderer:
  layout
  controls
  text
  input focus
  panels
  UI surfaces

3D renderer:
  cameras
  meshes
  materials
  lighting
  world surfaces

Compositor:
  combines UI surfaces and 3D outputs
```

This allows both:

```text
standard overlay UI:
  UI surface composited over final frame

in-world UI:
  UI surface rendered to texture
  texture placed on a mesh/object in the world
  input ray maps world hit to UI coordinates
```

Near-term audit target:

```text
Machina UI carryover audit:
  inspect Copeland CodeReferences;
  identify reusable layout primitives;
  identify hidden assumptions;
  determine what minimal Machina.Layout + Avalonia seam belongs in Aurelian.
```

Do not import the whole UI project blindly. Carry over useful organs, not the haunted skeleton.

## 9. Margaret as reference renderer plant

Margaret should remain part of the long-term vision as a production-grade unbiased/reference renderer.

It should not be the normal real-time renderer.

It should be a truth/reference plant:

```text
Margaret:
  unbiased/reference rendering
  lighting validation
  material validation
  camera/lens correctness
  cinematic or still-frame rendering
  ground truth for real-time approximation
  calibration data for differential rendering
```

Future plant model:

```text
Plant 0:
  real-time Vulkan or external renderer

Plant 1:
  Margaret reference renderer

Dominatus policy:
  decides when to invoke reference rendering;
  compares agreement/confidence;
  updates diagnostics/calibration;
  adjusts compositor/runtime policy.
```

This fits Aurelian’s multi-plant architecture without making slow reference rendering part of every frame.

## 10. Meshes, ray marching, and future renderable taxonomy

Meshes are necessary, but Aurelian should not be mesh-only.

Meshes are dominant because tools, hardware, animation systems, collision, and import pipelines are mesh-centered. They are also an approximation. For some objects, accuracy and performance fight each other directly: more geometric accuracy means more triangles and more cost.

Aurelian should support tessellated mesh rasterization, but also treat ray marching / SDF / implicit fields as first-class future renderables.

Future renderable taxonomy:

```text
MeshRenderable
RayMarchRenderable
SdfRenderable
UiSurfaceRenderable
ParticleFieldRenderable
ProceduralSurfaceRenderable
ReferenceOnlyRenderable
```

A future ray marching asset may need:

* bounds;
* distance-function shader/artifact;
* material;
* quality/step policy;
* collision/proxy metadata;
* renderer support flags;
* fallback representation.

This should not derail basic mesh rendering. But Aurelian should avoid hardcoding the assumption that every object is ultimately a mesh.

## 11. Multi-plant and explicit multi-GPU future

Aurelian is plant-shaped from day one, multi-GPU later.

Explicit multi-GPU would be powerful, but it is not an immediate priority. It is a future optimization and architecture payoff, not the next usability milestone.

Current seams intentionally preserve the possibility:

```text
PlantId
PlantContext
PlantOutputRef
RequiredPlantOutputSet
CompositorPolicyKind
CompositorDiagnostics
Dominatus policy
Graphics mechanism boundaries
```

The future goal is not to resurrect SLI.

The better goal:

```text
multi-plant orchestration:
  CPU
  GPU
  second GPU
  reference renderer
  remote renderer
  test fake
  future accelerator
```

Dominatus can eventually decide:

* which plants run this frame;
* which outputs are required;
* which older outputs may be reused;
* when confidence has dropped;
* when full-quality recalculation is needed;
* when transfer cost outweighs computation benefit;
* when a plant is overloaded.

Do not implement real multi-GPU yet. Defer:

* device groups;
* external memory;
* cross-device image transfer;
* multi-adapter synchronization;
* heterogeneous scheduling.

The correct near-term stance:

```text
Preserve seams now.
Implement multi-GPU later.
```

## 12. LLM DM and emergent gameplay

Aurelian’s long-term gameplay vision is not “NPC chatbot in a box.”

The compelling idea is an LLM-assisted DM/world dramaturge:

```text
explicit world facts
  -> Dominatus policy decides whether a high-level LLM act is warranted
  -> LLM proposes scene/dialogue/quest/world changes
  -> validators check consistency and constraints
  -> typed actuators commit or reject changes
```

The LLM should not directly mutate the world.

It should operate through bounded acts:

```text
ProposeSceneAct
ProposeDialogueAct
ProposeQuestBeatAct
ProposeNpcSpawnAct
ProposeFactionReactionAct
SummarizeNpcMemoryAct
GenerateRumorAct
```

Every proposal should be validated against:

* world facts;
* lore constraints;
* location constraints;
* gameplay constraints;
* duplication rules;
* reward/economy rules;
* continuity;
* safety/consistency checks.

The vision:

```text
Aurelian does not merely generate content.

Aurelian stages consequences.
```

Example future flow:

```text
Player steals a relic.
Town food supply falls.
Militia and priest faction tension rises.
A storm blocks the north road.

Runtime policy asks:
  Should a social consequence surface tonight?

LLM DM proposes:
  tavern confrontation scene involving priest, militia captain, and grieving villager.

Validators approve/reject.
Actuators spawn/schedule/update facts.
NPC policies act locally.
```

Do not implement this yet. It needs:

* stronger world facts;
* world actuation validators;
* scene/dialogue/quest contracts;
* persistence;
* diagnostics;
* gameplay loop.

But this is one of Aurelian’s long-term reasons to exist.

## 13. What to prioritize next

After the A72 checkpoint, likely useful next clusters:

### Cluster A — Basic engine usability

Goal: move from triangle proof to simple scene proof.

Possible milestones:

* material TOML M0;
* mesh asset M0;
* texture asset M0;
* simple world-to-render visible scene;
* render command plan -> Vulkan execution;
* camera transform/matrix path.

### Cluster B — External dumb renderer adapters

Goal: make Aurelian usable before native Vulkan matures.

Possible milestones:

* external renderer adapter contract audit;
* Unreal/Unity/O3DE/Stride adapter feasibility;
* render snapshot export format;
* bridge sample that drives an external renderer without owning behavior.

### Cluster C — UI and presentation

Goal: UI surfaces without coupling UI to 3D renderer.

Possible milestones:

* Machina UI carryover audit;
* Machina.Layout minimal import;
* Avalonia surface composition design;
* UI surface renderable contracts;
* UI overlay sample.

### Cluster D — Runtime/world gameplay spine

Goal: start using Dominatus for actual world behavior.

Possible milestones:

* world tick facts M0;
* simple agent/NPC policy;
* world actuation validators;
* scene proposal contracts;
* simple interactive simulation without LLM.

### Cluster E — Authoring/tooling

Goal: make assets/shaders less hand-built.

Possible milestones:

* asset tool builds shader artifacts;
* SDSL-V semantic annotations;
* `.sdslvtest`;
* material asset tool path;
* sample asset build docs.

### Cluster F — Graphics completeness

Goal: make Vulkan renderer less toy-like.

Possible milestones:

* depth buffer;
* descriptor sets/uniform buffers;
* texture sampling;
* indexed meshes;
* camera matrices;
* resize/swapchain recreation.

## 14. What not to do yet

Do not pursue yet:

* production editor;
* broad host/application lifecycle;
* full ECS-like system;
* service locator;
* reflection-heavy object graph construction;
* runtime shader compilation;
* material + mesh + texture + hot reload all at once;
* multi-GPU/device-group/external-memory implementation;
* LLM DM/emergent gameplay implementation;
* differential compositor;
* large renderer rewrite;
* importing all of Machina UI blindly;
* adopting VMA/Vortice without a specific reviewed milestone;
* integrating external engines before defining clean adapter contracts.

The engine is now strong enough to choose carefully. Do not reward that by sprinting into the prettiest swamp.

## 15. Immediate recommendation

Before selecting A73 implementation, review and decide which cluster matters most.

My current recommendation:

```text
A73 — Vision document lands in repo
A74 — Dumb renderer adapter audit
A75 — Machina UI carryover audit
A76 — Material TOML / flattened MaterialX design audit
```

Reason:

* Aurelian’s native Vulkan renderer is real but still M0.
* Usability may improve faster by driving existing renderers as dumb presentation plants.
* UI and materials are central to actual game-making.
* Runtime/world/LLM ideas need stronger world/gameplay facts first.
* Multi-GPU and differential compositor should remain future-seam goals, not immediate implementation.

The most important doctrine:

```text
External engines may render pixels.
Avalonia may render buttons.
Margaret may render truth.
Aurelian owns the world.
Dominatus decides what happens.
```
