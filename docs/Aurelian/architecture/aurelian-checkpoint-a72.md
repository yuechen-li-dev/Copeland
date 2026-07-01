# Aurelian checkpoint A72 — visible sample + shader asset bridge

## 1. Summary

A72 is a regrouping checkpoint after the A0-A71 bring-up run. It does not add production code, projects, packages, features, or architecture beyond status recommendations. Its purpose is to make the current engine state reviewable before selecting A73.

Current thesis:

> Aurelian is not an engine with AI bolted on. Aurelian is an engine whose core orchestration model is Dominatus-shaped.

Professional translation of the Stride comparison:

> Stride's engine core was effectively an implicit, reflection-heavy, less-local version of what Aurelian is making explicit through Dominatus, typed contracts, and actuator boundaries.

The current implementation has reached a visible, finite, human-runnable proof path: checked-in text-safe shader artifacts are described by an asset manifest, loaded into neutral shader contracts, consumed by Vulkan pipeline setup, routed through Core frame-loop/runtime integration, coordinated by a Dominatus-backed runtime session and compositor policy, copied through the Vulkan compositor mechanism, and presented through a sample-owned window/swapchain path.

The engine is still pre-MVP. The visible triangle is a valuable proof of real seams, not proof that the engine has a production host, full render system, asset manager, world-to-render integration, gameplay loop, resize handling, descriptor/uniform path, or material/mesh/texture asset pipeline.

Implementation should pause for human review before A73.

## A73 Vision 1 integration note

A73 links `docs/architecture/visions-1.md` as Aurelian Vision 1, the post-checkpoint north star for explicit Dominatus-native orchestration, dumb renderer strategy, materials/UI/reference-renderer/raymarch/multi-plant/LLM-DM direction, and the rule that Aurelian owns world truth while mechanisms render, validate, or execute bounded requests. Vision 1 is aspirational where it discusses external renderers, Machina UI, Margaret, ray marching, multi-plant/multi-GPU, and LLM/DM gameplay; this A72 checkpoint remains the source of current implemented repo reality until a later checkpoint supersedes it.

## 2. Current executable proof

The current demo path is the visible triangle sample:

```bash
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
```

Useful optional flags:

```bash
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug -- --validation
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug -- --no-hold
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug -- --frames 10
```

The sample proves the following real path:

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

What is demoable:

- a small desktop window when Vulkan presentation and a supported windowing environment are available;
- a static visible triangle rendered through the current compositor path;
- finite multi-frame acquire/present, defaulting to three frames and capped at 300;
- sample-local window event pumping before acquire and after present;
- clean frame-loop stop when the window close request is observed;
- asset-manifest shader resolution by id `smoke_triangle`, with no direct shader artifact path in the sample's main runtime path.

Known environment limitations:

- the sample should be built in CI but not run automatically in headless environments;
- it needs Vulkan loader/device support and a windowing/presentation environment;
- validation requires available Vulkan validation layers;
- swapchain recreation/resize robustness is not yet implemented.

## 3. Current project roles

- `Aurelian.Rendering.Contracts`: backend-neutral render, shader, command-plan, and compositor DTOs. It is the lowest shared rendering contract surface.
- `Aurelian.World`: immutable-ish world documents, unit IDs/descriptors, typed data stores, snapshots, and resolver/query models.
- `Aurelian.Actuation`: typed world mutation requests/results and actuator-owned world document transitions.
- `Aurelian.Rendering.Null`: backend-independent null renderer proof over rendering contracts.
- `Aurelian.Shaders`: shader authoring/build-time pipeline pieces: SDSL-V parser/validator, HLSL emission, optional DXC subprocess/tool dependency, SPIR-V artifact writing, and shader artifact metadata.
- `Aurelian.Assets`: runtime asset loading bridge for TOML manifests and shader artifacts into neutral rendering contracts.
- `Aurelian.Graphics`: Vulkan backend mechanism code: plant/device, memory/resources, uploads/barriers, render pass/framebuffer/pipeline/draw/submit, presentation/swapchain, and compositor mechanism.
- `Aurelian.Runtime`: Dominatus-backed policy/runtime layer for sessions, runtime ticks, and compositor policy. It references world and rendering contracts, not graphics.
- `Aurelian.Core`: engine integration spine. It joins runtime, rendering contracts, and graphics mechanisms through explicit adapters, frame pump, frame loop, runtime tick frame step, and compositor bridge.
- `Aurelian.AssetTool`: CLI/tooling surface for asset/shader pipeline work.
- `samples/Aurelian.VisibleTriangle`: human-facing integration sample that owns window/Vulkan preparation and exercises the current real path.

## 4. Current dependency shape

Current project-reference summary from the A72 inspection:

- Core references `Aurelian.Runtime`, `Aurelian.Rendering.Contracts`, and `Aurelian.Graphics`.
- Runtime references `Aurelian.World`, `Aurelian.Rendering.Contracts`, and vendored `Dominatus.Core`.
- Graphics references only `Aurelian.Rendering.Contracts` among Aurelian projects and uses Silk.NET Vulkan/windowing packages.
- Rendering.Contracts references no Aurelian projects.
- Assets references `Aurelian.Rendering.Contracts` and `Aurelian.Shaders`, plus Tomlyn. This is acceptable for the current shader artifact bridge but should stay loader/tooling-shaped rather than become graphics-owned resource management.
- Shaders references `Aurelian.Rendering.Contracts` and has the DXC tool package. Graphics does not reference Shaders.
- The visible sample references Assets, Core, Graphics, Runtime, and Rendering.Contracts because it is currently the composition executable.
- Integration tests reference Core, Runtime, Graphics, and Rendering.Contracts, and link a graphics-test SPIR-V fixture.

Risky or surprising dependencies:

- Core currently references Graphics directly. This is intentional for the current Core Vulkan compositor adapter, but should be watched so Core stays an integration spine rather than an expanding graphics implementation layer.
- Assets references Shaders. This is acceptable while Assets decodes the A69/A69b shader artifact model, but runtime sample code must not drift into runtime compilation or compiler/tool ownership.
- The sample references Graphics directly because there is no production host/window lifecycle yet. This is acceptable sample composition, not the desired final application-host shape.

## 5. What is real implementation

Real implementation now includes:

- typed rendering contracts, shader stage/program contracts, render snapshots, command plans, and compositor DTOs;
- world unit/document and typed data-store M0 surfaces;
- typed world actuation request/result surfaces;
- Dominatus-backed runtime session start/tick/stop path;
- Core frame loop, frame pump, frame-input provider contract, presentation mechanism contract, runtime tick frame step, and frame-loop stop reasons;
- compositor policy/mechanism split with runtime policy and graphics mechanism kept apart by neutral contracts;
- Vulkan plant/device initialization and diagnostics;
- timeline fence, command-buffer pool, command-submit helper, allocator contracts/raw allocator, buffers, textures, upload helpers, layout/barrier tracking, render pass/framebuffer, pipeline creation from neutral compiled shader contracts, vertex draw command recording, offscreen draw submission proof, surface/swapchain, acquire/present, swapchain image wrappers, and compositor passthrough copy;
- shader artifact TOML plus raw `.spv` or text-safe `.spv.hex` transport with decoded-byte hash validation;
- asset manifest shader references and runtime manifest loading to neutral `CompiledShaderProgram`;
- visible sample that executes the current real path when the environment supports it.

## 6. What is scaffold/proof-only

The following are scaffold, proof-only, or M0-constrained:

- the visible sample owns window, Vulkan setup, swapchain preparation, and sample diagnostics;
- the triangle is static and reuses an offscreen output for planned frame IDs;
- sample frame count is finite by design, not a full game loop;
- the current compositor is passthrough/copy-oriented, not a differential, multi-input, quality-aware compositor;
- render command plans and null renderer exist, but there is no full world-to-Vulkan render-command execution path;
- world data exists, but the visible sample does not render from a world scene;
- shader artifacts are checked in for the sample; tool-built artifact generation is not yet a production content pipeline;
- `.cs` shader byte arrays remain tests/bootstrap fixtures only;
- graphics integration tests are headless-safe and often skip/diagnose when Vulkan is unavailable, which is correct for CI but not a visual correctness oracle.

## 7. What is intentionally deferred

Deferred deliberately:

- production host/application lifecycle;
- engine-owned window lifecycle;
- production input system;
- infinite/main game loop policy;
- material, mesh, texture, and scene asset systems;
- general asset manager/cache/hot reload;
- runtime SDSL-V/HLSL/DXC compilation;
- descriptor sets, uniform buffers, camera matrices, indexed meshes, texture sampling, depth buffer, resize/swapchain recreation, and robust present semaphore lifecycle;
- world-to-render visible gameplay sample;
- render graph/scheduler/threading system;
- differential compositor and multi-output quality policy;
- VMA/VMASharp or Vortice adoption;
- external memory, device groups, and explicit multi-GPU execution;
- LLM/DM/emergent gameplay behavior.

## 8. Boundary health

A72 boundary scans classify the current state as follows:

- `Aurelian.Runtime` for graphics/Vulkan/window terms: clean. Runtime does not reference `Aurelian.Graphics`, Silk.NET, Vulkan, swapchain, or surface terms in source/project files.
- `Aurelian.Graphics` for runtime/Dominatus/policy terms: clean. Graphics does not reference Runtime, Dominatus, HFSM/blackboard, runtime session, or compositor policy session types.
- `Aurelian.Rendering.Contracts` for runtime/graphics/Vulkan/Dominatus terms: clean.
- visible sample and Graphics for shader compiler terms (`Aurelian.Shaders`, DXC, SDSL/HLSL artifact types): clean in `.cs`/`.csproj` scan. The sample reaches shaders through `Aurelian.Assets`, not direct compiler/tool code.
- `Aurelian.Assets` and its tests for graphics/Vulkan terms: expected false positive in a test assertion that verifies `ShaderAssetManifestLoader` does not reference `Aurelian.Graphics`; no production assets graphics dependency was found.
- broad risky terms: acceptable intentional exceptions include Vulkan `StructureType`/`ImageType`/`ViewType` usage inside Graphics, raw allocator enum value `Vma` as a future allocation backend option, test assertions about Vortice absence, test reflection used for boundary/property assertions, and diagnostic `ex.GetType().Name` strings. No service locator/singleton/application-host pattern or CodeReferences/vendor modification was introduced.

Needs follow-up, but not A72 code changes:

- keep monitoring Core's direct Graphics dependency as the host/application layer evolves;
- keep Assets -> Shaders constrained to artifact loading/tool-adjacent responsibilities, not runtime shader compilation;
- replace sample-owned setup with a production host only when the next milestone intentionally targets host/application lifecycle.

## 9. Known technical debt

Known debts after A71:

- no production host or engine-owned window lifecycle;
- sample-local Vulkan/window setup is not yet a reusable application model;
- no resize/swapchain recreation path;
- no robust input subsystem;
- no material/mesh/texture asset system;
- no general asset manager/cache/hot reload;
- no render-command-plan-to-Vulkan execution path for world scenes;
- no descriptors/uniform buffers/camera matrices/depth path;
- no production shader build tooling workflow from SDSL-V source to checked-in artifacts;
- no runtime world/gameplay integration in the visible sample;
- no visual regression/readback validation for the visible result;
- headless-safe graphics tests prove command paths and diagnostics, not end-user visual success;
- the Core/Graphics coupling is acceptable but should not become a hidden monolith.

## 10. Dominatus integration status

Dominatus is now real in Aurelian's runtime path, but still bounded:

- Runtime owns the Dominatus-backed session and policy behavior.
- Core calls the runtime session through ticker/frame-step adapters.
- The visible sample starts the runtime session and the frame loop ticks it.
- Runtime compositor policy decides dispatch readiness through neutral compositor facts/contracts.
- Graphics remains mechanism-only and does not reference Dominatus.

Where Dominatus should enter next:

- world/runtime policy decisions after explicit world facts and actuation contracts are strong enough;
- compositor policy evolution, such as quality/cadence/readiness choices, while continuing to emit neutral dispatch requests;
- bounded gameplay/world policy acts that go through validators and actuators.

Where it should not enter yet:

- Vulkan resource ownership;
- shader compilation/loading mechanisms;
- direct world mutation bypassing actuators;
- sample-local window/event mechanics.

## 11. Graphics/Vulkan status

Graphics/Vulkan is the most implementation-heavy area. The current Vulkan path is not just scaffold: it includes real device/resource/pipeline/submit/presentation pieces and a visible sample path. However, it remains M0 quality.

Current status:

- plant-shaped device initialization exists;
- raw Vulkan allocation path exists;
- buffers/textures/uploads/barriers exist;
- render pass/framebuffer/pipeline/draw exist;
- command submit helper exists;
- surface/swapchain/acquire/present exist;
- compositor passthrough mechanism exists;
- visible sample can present through the compositor path when environment support is present.

Major missing graphics capabilities:

- swapchain resize/recreation;
- depth buffer;
- descriptor sets/uniform buffers;
- camera matrices;
- indexed mesh data;
- texture sampling/material binding;
- render-command-plan execution from a world scene;
- broader synchronization/present-semaphore hardening;
- visual regression validation.

## 12. Shader/assets status

Shader/assets state is now a real bridge from checked-in artifacts to runtime graphics contracts:

- `Aurelian.Shaders` can produce/describe shader artifacts at build/tool time;
- A69/A69b established TOML metadata plus raw `.spv` or text-safe `.spv.hex` SPIR-V transport;
- loader hash validation is over decoded/raw bytes, so `.spv.hex` remains text-safe while preserving byte integrity;
- A70 introduced asset manifest shader references;
- A71 made the visible sample load `Assets/assets.toml`, resolve `smoke_triangle`, and pass a neutral `CompiledShaderProgram` into graphics setup;
- there is no runtime shader compilation and no direct compiler dependency in Graphics or sample code.

Still missing:

- production asset build step;
- material/mesh/texture asset models;
- asset cache/lifetime/hot reload;
- asset-to-GPU resource ownership policy;
- generalized asset lookup beyond the sample-local helper.

## 13. World/runtime status

World/runtime has strong contracts but little visible gameplay integration:

- world units, descriptors, hierarchy, and typed stores exist;
- world actuation requests/results exist and preserve actuator-owned mutation boundaries;
- render snapshot/command contracts exist;
- runtime session and Dominatus-backed ticking exist;
- compositor policy is runtime-owned and Dominatus-shaped.

Still missing:

- visible sample driven by world facts;
- gameplay tick facts/actions;
- scene/NPC/dialogue/quest data;
- world-to-render extraction into a visible Vulkan path;
- validators for higher-level policy-driven world changes;
- persistence/loading story beyond M0 documents and assets.

## 14. Multi-plant/multi-GPU future positioning

Aurelian is plant-shaped from day one, multi-GPU later.

Explicit multi-GPU is a future goal, not an immediate implementation priority. The current seams intentionally preserve the possibility without committing A72/A73 to external-memory/device-group work:

- `PlantId` identifies graphics plants;
- `PlantOutputRef` names produced plant outputs independently of backend handles;
- `RequiredPlantOutputSet` represents policy-selected output requirements;
- compositor policy/mechanism split separates readiness/quality/cadence choices from graphics execution;
- Dominatus policy can later reason about which plant outputs are needed;
- graphics mechanism boundaries keep backend resources out of Runtime/Dominatus state.

Real multi-GPU, external memory, cross-device synchronization, device groups, multi-adapter scheduling, and heterogeneous plant orchestration should wait until basic engine usability is stronger. The immediate value is preserving seams, not implementing multi-GPU plumbing prematurely.

## 15. LLM/DM/emergent gameplay future positioning

LLM DM/emergent gameplay is a future high-level capability, not the next implementation step.

When introduced, it should be bounded Dominatus/actuator-driven policy, not direct world mutation. Required prerequisites:

- explicit world facts;
- validated world actuation;
- scene/dialogue/quest proposal contracts;
- runtime policy integration;
- safety/consistency validators;
- clear audit diagnostics for accepted/rejected proposals.

Do not implement LLM/DM/emergent gameplay before core engine usability. Without a stable world/render/runtime spine, LLM behavior would either be fake, brittle, or too unconstrained to validate.

## 16. Recommended next milestone clusters

A72 should not choose final direction too aggressively. Viable A73+ clusters are:

### Option A — Basic engine usability

- material/mesh assets;
- render command plan -> Vulkan execution;
- simple world -> visible render path;
- make the sample less sample-local without building a full host too early.

### Option B — Runtime/world gameplay spine

- world tick facts;
- Dominatus world policies;
- simple NPC/scene acts;
- validators and actuator diagnostics for policy-driven mutation.

### Option C — Graphics completeness

- depth buffer;
- resize/swapchain recreation;
- descriptor sets/uniform buffers;
- camera matrices;
- indexed draws and texture sampling.

### Option D — Authoring/tooling

- SDSL-V semantic annotations;
- `.sdslvtest` or shader-contract tests;
- asset tool builds shader artifacts;
- content pipeline documentation for generated artifacts.

### Option E — Sample/productization

- improve visible sample diagnostics/onboarding;
- package sample assets more cleanly;
- document known platform requirements;
- add a local-run checklist and optional visual smoke guidance.

## 17. “Do not do yet” list

Do not pursue yet:

- broad host/editor architecture;
- runtime shader compilation;
- material/mesh/texture asset systems plus hot reload all at once;
- multi-GPU/device-group/external-memory implementation;
- LLM DM/emergent gameplay;
- differential compositor complexity;
- vendor rewrites or CodeReferences changes;
- service locator/reflection-heavy object graph construction;
- new packages unless a specific reviewed milestone justifies them;
- large architecture pivots before human review.

## Next checkpoint recommendation

Review with human before selecting A73.
