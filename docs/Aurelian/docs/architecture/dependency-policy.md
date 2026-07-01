# Aurelian Dependency and Library Adoption Doctrine

## A72 checkpoint boundary status

A72 adds the checkpoint document `docs/architecture/aurelian-checkpoint-a72.md` and audit `docs/audits/0072-a72-checkpoint-visible-sample-shader-asset-bridge.md`. The checkpoint confirms that the main dependency boundaries still hold after the visible sample and shader asset bridge: Runtime remains free of Graphics/Vulkan/windowing dependencies, Graphics remains free of Runtime/Dominatus policy dependencies, Rendering.Contracts remains neutral, and the sample reaches shader artifacts through `Aurelian.Assets` rather than runtime compiler/tool ownership. Implementation should pause for human review before selecting A73.


## 1. Purpose

Aurelian should not reimplement every low-level or correctness-heavy subsystem from scratch. Physics solvers, navmesh builders, native graphics bindings, parsers, compression libraries, image codecs, and shader validation tools are all areas where subtle mistakes can consume large amounts of engineering time without improving the engine's architectural identity.

Aurelian should also not let external libraries define its architecture. A library may be the right way to solve bounded implementation plumbing, but it must not become the owner of the world model, runtime spine, actuator model, render architecture, asset contracts, shader language, or locality doctrine.

This policy defines when Aurelian should adopt libraries and how those dependencies must be kept behind clean Aurelian-owned boundaries before the engine begins adding physics, navigation, rendering, windowing, assets, or backend integrations.

## 2. Core rule

```text
Aurelian owns the architecture spine. Libraries provide bounded implementation plumbing.
```

The spine includes the world model, runtime, Dominatus orchestration, actuators, render snapshots/command plans, asset manifests, SDSL-V compiler artifacts, and locality doctrine. These are Aurelian-owned contracts and concepts.

Libraries may implement backend details behind those contracts. They can provide native API bindings, physics math and simulation internals, pathfinding queries, TOML syntax parsing, deterministic JSON support, image/codec infrastructure, or optional shader validation, but they must not replace Aurelian's architectural boundaries with their own object model.

## Vision 1 relationship

`docs/architecture/visions-1.md` is aspirational north-star doctrine. This dependency policy remains authoritative for current project edges and package boundaries. Future external renderer, UI, material, reference-renderer, raymarch, multi-plant, or LLM/DM integrations must preserve policy/mechanism separation and keep external libraries behind Aurelian-owned contracts rather than letting them define the engine spine.

## 3. When to use a library

| Good reason | Explanation |
| --- | --- |
| Correctness-heavy | Physics, navmesh, graphics bindings, parsers, compression, image codecs, etc. are easy to get subtly wrong. |
| Low strategic differentiation | Reimplementing it does not make Aurelian meaningfully better. |
| High implementation cost | Building it would delay the engine spine. |
| Clear boundary | Library can sit behind an Aurelian-owned interface/contract. |
| Replaceable later | The dependency can be swapped without rewriting the engine model. |
| NativeAOT-compatible or isolatable | Prefer libraries that work with NativeAOT; otherwise isolate them in non-core packages. |

## 4. When not to use a library

| Bad reason | Explanation |
| --- | --- |
| It is convenient but takes over architecture | Avoid importing another engine’s object model. |
| It requires service locator/global state patterns | Conflicts with locality and explicit contracts. |
| It forces reflection into core runtime | Conflicts with NativeAOT and locality. |
| It defines world/entity/component ownership | Aurelian owns its world model. |
| It couples frontend/editor/tooling to runtime core | Tools must remain layered. |
| It prevents deterministic tests | Core behavior should be testable headlessly. |

## 5. Specific library categories

### Silk.NET / Vortice

- Acceptable for native graphics/window/platform bindings.
- Must sit behind Aurelian rendering/HAL contracts.
- Must not define world, render snapshot, asset, or material architecture.
- Windowing should live outside core runtime.
- A22 introduces `Aurelian.Graphics` with `Silk.NET.Vulkan` and `Silk.NET.Windowing` references only as scaffold/package-smoke dependencies; A23 adds native-free plant contracts under `Aurelian.Graphics.Plants`; A24 adds optional/unavailable-safe Vulkan instance/device initialization for plant 0 only. A24 may create a Vulkan instance, physical-device selection, logical device, and selected queue when available, but it still creates no window, surface, swapchain, command buffers, resources, or renderer. A27 documents the allocator strategy before resource implementation: VMA/VMASharp may be used as backend plumbing, but Aurelian-owned allocator contracts and policy own the architecture seam. A28 implements those contracts plus a raw Vulkan allocator M0 backend; the raw backend is fallback plumbing only, and future buffers/textures must use `IVulkanMemoryAllocator` rather than direct Vulkan allocation calls.
- Vortice remains deferred until there is a concrete backend need that Silk.NET does not satisfy.
- Stride.Graphics remains reference-only and must not be ported into Aurelian-owned graphics code.

### BEPUphysics

- Acceptable for physics simulation.
- Aurelian should own physics component/store/actuator/event contracts.
- BEPU should not define entity identity or world ownership.
- Physics results should cross boundaries through typed events/snapshots.

### DotRecast

- Acceptable for navmesh/pathfinding.
- Aurelian should own navigation requests/results and world integration.
- DotRecast should not define AI behavior/policy.
- Dominatus/logic chooses goals; navigation library computes paths.

### Tomlyn

- Acceptable for TOML parsing.
- Tomlyn parses syntax; Aurelian owns asset manifest schema, validation, diagnostics, and artifact model.

### System.Text.Json

- Acceptable for deterministic JSON artifacts/manifests.
- Serialization shape should be explicit DTOs, not reflection-first runtime object graphs.

### DXC

- Acceptable as optional external shader validation/compiler tool.
- SDSL-V remains source language.
- DXC must not be required for normal tests.
- DXC output is artifact/compiler validation, not engine architecture.

### Avalonia / Machina

- Useful for tools/UI later.
- Must remain in tooling/editor packages.
- Must not become Aurelian core runtime dependency.

## 6. Reflection and NativeAOT policy

```text
Aurelian core is reflection-free by default.
```

Rules:

- no runtime reflection for core behavior;
- no assembly scanning for core systems;
- no `Activator.CreateInstance` dependency for core runtime;
- no reflective property paths;
- no reflection-defined serialization in runtime core;
- prefer source generation, manifests, typed registries, and explicit request/result contracts.

Reflection may be allowed:

- in tooling;
- in tests;
- in source generators;
- in build-time asset tools;
- only if the result becomes explicit generated/static metadata or artifacts.

```text
If the compiler cannot see the dependency, Aurelian should distrust it.
```

## 7. Dependency layering rules

- `Aurelian.Core` should stay minimal and dependency-light.
- `Aurelian.World` must not depend on rendering, assets, shaders, Dominatus, physics, navigation, or UI. World-owned renderable data is allowed only as symbolic world refs such as mesh/material strings, not rendering-contract refs or asset/shader handles.
- `Aurelian.Rendering.Contracts` contains renderer-independent DTOs only: render snapshots, items, cameras, resource refs, command plans, draw items, pass plans, symbolic pipeline/shader/target refs, statuses, reasons, diagnostics, and contract-local snapshot-to-plan assembly. It must not depend on world, assets, shaders, graphics/windowing packages, GPU handles, or backend object models.
- `Aurelian.Rendering.Null` is the first backend implementation boundary. It may depend on `Aurelian.Rendering.Contracts`, consumes command plans, and returns deterministic headless traces/results, but it must not introduce GPU/windowing packages, world, assets, shaders, backend-native handles, images, or windows.
- `Aurelian.Graphics` is the first graphics HAL boundary. It may reference `Aurelian.Rendering.Contracts` and tightly scoped graphics/windowing packages, starting with Silk.NET Vulkan/windowing, but it must not reference world, assets, shaders, the null renderer, Dominatus, `CodeReferences`, or vendor source. A23 adds PlantContext + PlantRegistry M0 as native-free plain data: `PlantId.Zero` is the one-plant M0 identity, the registry is deterministic and diagnostic-driven, presentation ownership is explicit, and no global graphics singleton is allowed. A24 adds a per-plant Vulkan instance/device owner: initialization is optional/unavailable-safe, created devices require timeline semaphores, the graphics+compute+transfer queue family is selected rather than hardcoded, and native handles do not leak into the plant model. A25 adds optional per-plant timeline semaphore fence wrappers and an explicit frame/command-list/copy fence bundle, plus a pure managed fence-tagged resource pool with telemetry for later command-buffer/descriptor/resource reuse; the pool is inspired by Stride and Prometheus patterns while avoiding literal engine-policy adoption. A26 adds a per-plant Vulkan command pool and primary command buffer lease lifecycle over that pool: command buffers reset/begin/end, retire with fence values, and reuse through the managed fence-tagged pool. A27 adds a docs-only Vulkan memory allocator strategy: Aurelian owns allocation contracts, budget facts, telemetry, fence retirement, grow/shrink policy, and future Dominatus hooks; VMA/VMASharp is a backend candidate that must stay hidden below `Aurelian.Graphics.Vulkan.Resources`. A26/A27 do not add buffers, textures, swapchains, windows, surfaces, render passes, draw/copy/update commands, or renderer execution. A28 adds allocator contracts and an isolated raw allocator backend. A29 adds the first real GPU resource type, Vulkan buffers, while preserving the allocator boundary: buffer creation may create/bind `VkBuffer` objects and request memory through `IVulkanMemoryAllocator`, but raw `vkAllocateMemory`/`vkFreeMemory` remain allocator-backend-only. A29 still does not add textures, mapped memory API, upload rings, staging/copy commands, descriptors, swapchains, windows, surfaces, render passes, pipelines, draws, VMA, Vortice, or dependencies on world/assets/shaders/null rendering/vendor/reference code.
- World-to-render extraction lives in `Aurelian.Runtime.Rendering` because runtime composition may reference both `Aurelian.World` and `Aurelian.Rendering.Contracts`; no separate extraction project is needed until the boundary becomes heavier.
- `Aurelian.Actuation` may depend on world contracts for world mutation.
- `Aurelian.Runtime` integrates Dominatus, dispatch, and world-to-render snapshot extraction. It may reference `Aurelian.Rendering.Contracts`, but production runtime must not reference `Aurelian.Rendering.Null`.
- Backend packages may depend on external libraries.
- Tools/editor packages may depend on UI/windowing libraries.
- No dependency should create reverse references into core layers.


## A40 DXC subprocess toolchain policy

A40 establishes `Microsoft.Direct3D.DXC` as a shader compiler tooling dependency only. The package reference belongs in `Aurelian.Shaders`, where SDSL-V parsing, validation, HLSL emission, artifact contracts, and external compiler checks already live. `Aurelian.Graphics`, `Aurelian.Runtime`, `Aurelian.World`, `Aurelian.Rendering.Contracts`, and `Aurelian.Rendering.Null` must remain DXC-free.

The intended shader toolchain remains:

```text
SDSL-V -> HLSL/Slang -> DXC subprocess -> SPIR-V artifact -> Vulkan pipeline creation
```

Aurelian does not plan direct SDSL-V -> SPIR-V generation. Runtime graphics consumes SPIR-V bytes plus stage, entry-point, hash, and future reflection/binding metadata; it must not invoke DXC or reference DXC packages. `Vortice.Dxc` remains deferred as a future fallback only if subprocess DXC is insufficient.

## 8. Wrapper rule

For every external library, Aurelian should ask:

```text
Can this dependency be hidden behind an Aurelian-owned contract?
```

If no, do not adopt yet.

Examples:

- `IPhysicsWorld` / `PhysicsStepRequest` / `PhysicsStepResult` for BEPU.
- `INavigationMeshQuery` / `PathRequest` / `PathResult` for DotRecast.
- `IRenderBackend` / `RenderCommandPlan` for graphics.
- `AssetManifestParser` and Aurelian diagnostics for Tomlyn.
- `DxcValidator` for DXC.

## 9. Anti-goals

- no NIH rewrite of commodity infrastructure;
- no dependency capture;
- no reflection-first core;
- no editor/tool dependency in runtime;
- no adopting a full engine architecture just for rendering;
- no hidden global state from dependencies crossing into Aurelian’s core model.

## 10. First practical implications

- World typed data stores should stay library-free for now.
- Render snapshot and command-plan contracts are DTO-only in `Aurelian.Rendering.Contracts`.
- The null renderer is implemented in `Aurelian.Rendering.Null` as a headless backend over command plans. World-to-render extraction now exists in `Aurelian.Runtime.Rendering`, so `WorldDataDocument -> RenderSnapshot -> RenderCommandPlan -> NullRenderer` is testable headlessly.
- `Aurelian.Graphics` now owns the first graphics HAL scaffold, native-free plant registry, Vulkan instance/device initialization M0 for plant 0, optional per-plant timeline fence bundles, a pure managed fence-tagged resource pool, a per-plant Vulkan command pool / primary command buffer lease M0, Vulkan allocator contracts with an isolated raw backend, Vulkan Buffer resource M0 over `IVulkanMemoryAllocator`, A30 mapped CPU writes for host-visible buffers, A31 one-shot staging-to-device-local buffer uploads, A33 Texture2D resources, A34 barrier command emission, and A35 synchronous whole Texture2D upload through allocator/buffer/command/barrier/fence seams. `PlantId.Zero` represents the single-GPU plant in M0; successful Vulkan creation is per-plant, requires timeline semaphores, records plain facts/diagnostics, and, through A48, can optionally create Silk.NET.Windowing surfaces plus Vulkan swapchains/images/views when `EnablePresentation` is requested and the environment supports presentation. Headless/unavailable presentation paths return typed diagnostics; render-to-swapchain and acquire/present loops remain deferred. VMA remains backend plumbing, not architecture.
- Visual render backends can later use Silk.NET first behind `Aurelian.Graphics`; Vortice remains deferred.
- Physics can later use BEPU behind `Aurelian.Physics.*`.
- Navigation can later use DotRecast behind `Aurelian.Navigation.*`.
- Assets can keep Tomlyn because TOML syntax is not strategic architecture.
- Shader DXC validation remains optional and external.

## A30 graphics mapped-memory note

A30 keeps CPU upload support inside existing dependency boundaries. The implementation uses Silk.NET Vulkan only at the backend edge and introduces no VMA/VMASharp, Vortice, global allocator, service locator, staging copy subsystem, textures, swapchain/window/surface, render pass, pipeline, or draw dependencies.

Mapped memory is represented through Aurelian-owned allocation and buffer contracts. Only allocator backends may call `vkMapMemory`/`vkUnmapMemory`; buffer code receives writability facts and performs bounds-checked writes through `AurelianVulkanBuffer.Write(...)` rather than owning raw memory allocation or mapping policy.


## A31 graphics upload dependency note

A31 keeps device-local buffer upload support inside the existing Aurelian-owned dependency boundaries. The upload helper may record `vkCmdCopyBuffer` and submit a command buffer, but it does not allocate, free, map, or unmap raw memory directly; staging memory is created through `VulkanBufferFactory` and `IVulkanMemoryAllocator`, and CPU writes go through `AurelianVulkanBuffer.Write(...)`. Command recording uses `VulkanCommandBufferPool`, and synchronization uses `VulkanFenceBundle.CommandListFence`.

The M0 upload path is intentionally synchronous: it waits for the submitted timeline fence value before disposing temporary staging resources. Upload rings, batching, persistent staging pools, async fence-retired staging, texture uploads, barriers/layout tracking, and renderer-facing draw infrastructure remain future work rather than new dependency policy.

## A32 barrier/layout dependency note

A32 keeps barrier state as an Aurelian-owned model even though the mapper exposes Silk.NET Vulkan facts at the backend boundary. Resource layouts, access masks, stages, transition plans, batches, diagnostics, and subresource tracking are defined in `Aurelian.Graphics`; callers do not depend on Stride, Vortice, VMA/VMASharp, or vendor/reference code.

The mapper is intentionally deterministic and test-covered, while command emission is deferred so the pure state model can be validated without requiring a Vulkan runtime. Cross-plant transfer layouts are represented as explicit stubs with diagnostics instead of hiding future queue-family ownership transfer policy in globals or hardcoded queue-family indices.

## A33 texture allocation boundary

A33 keeps Vulkan texture memory behind the same Aurelian-owned allocator boundary as buffers. Texture code may create and destroy images and image views, query image memory requirements, and bind returned allocations, but it must not call raw memory allocation/free APIs. `vkAllocateMemory`, `vkFreeMemory`, `vkMapMemory`, and `vkUnmapMemory` remain allocator-backend details only.

Texture creation continues the reference-only policy for Stride and other engines: their image/view/lifetime intent may inform audits, but production code owns its create plans, diagnostics, layout tracker integration, and allocator calls directly. No VMA/VMASharp, Vortice, swapchain/window, render-pass, descriptor, sampler, or upload dependency is introduced by this milestone.

## A34 barrier emission dependency note

A34 keeps synchronization emission inside the existing `Aurelian.Graphics` Vulkan backend boundary. The new barrier emitter uses Silk.NET Vulkan to record `vkCmdPipelineBarrier` against a per-plant `VulkanCommandBufferLease`, but the public planning model remains Aurelian-owned and handle-free. Native images and buffers are paired with plans only by small emission request records at the backend edge.

This milestone does not introduce VMA/VMASharp, Vortice, global services, reflection, texture upload, render passes, pipelines, descriptor systems, swapchains/windows/surfaces, or cross-module dependencies on world/assets/shaders/null rendering/vendor/reference code. Raw memory allocation and mapping APIs remain allocator-backend details only.


## A35 texture upload dependency note

A35 keeps texture upload inside the existing `Aurelian.Graphics` Vulkan backend boundary. `VulkanTextureUploader` creates temporary staging through `VulkanBufferFactory` and `IVulkanMemoryAllocator`, writes through `AurelianVulkanBuffer.Write(...)`, emits image barriers through `VulkanBarrierCommandEmitter`, records `vkCmdCopyBufferToImage`, submits through the plant queue, and synchronously waits `VulkanFenceBundle.CommandListFence` before staging disposal.

The upload helper does not allocate/free/map/unmap raw memory directly and does not introduce VMA/VMASharp, Vortice, global services, reflection, descriptor systems, samplers, render passes, pipelines, draw commands, swapchains/windows/surfaces, upload rings, or cross-module dependencies on world/assets/shaders/null rendering/vendor/reference code. Whole Texture2D mip0/layer0 four-byte color uploads are the only supported M0 capability.


## A36 render pass descriptor dependency note

A36 keeps render pass ownership inside `Aurelian.Graphics` and behind Aurelian-owned records/results. The public descriptor is plain data and does not expose Silk.NET structs; Silk.NET Vulkan is used only at the native compilation/disposal edge that creates and destroys `VkRenderPass`. This preserves the policy that external/native libraries are plumbing dependencies, not architecture-defining contracts.

The M0 render pass path does not add VMA/VMASharp, Vortice, global service locators, reflection, framebuffer/pipeline/draw abstractions, or swapchain/window/surface dependencies. Future pipeline compatibility keys can derive from the descriptor data/hash rather than from hidden backend state.

## A37 framebuffer dependency note

A37 keeps framebuffer creation behind Aurelian-owned descriptor/result/owner contracts in `Aurelian.Graphics.Vulkan.Pipelines.Framebuffers`. Silk.NET Vulkan remains plumbing at the native edge for `VkFramebuffer` creation/destruction only; raw Vulkan handles are not public API, and compatibility is expressed in Aurelian terms (`PlantId`, texture usage/format/size, render pass descriptor data).

The M0 path supports one color attachment and no framebuffer cache. It does not add VMA/VMASharp, Vortice, global services, reflection, descriptor sets, render pass begin/end command recording, pipeline creation, draw calls, swapchain/window/surface dependencies, or cross-module references to world/assets/shaders/null rendering/vendor/reference code.

## A38 render pass command dependency note

A38 keeps the Vulkan command path on the existing `Silk.NET.Vulkan` dependency. Render pass begin/end emission is implemented behind Aurelian-owned request/result/diagnostic types under `Aurelian.Graphics.Vulkan.Commanding.RenderPasses`; it does not introduce VMA/VMASharp, Vortice, service locators, reflection-based construction, swapchain/window/surface dependencies, or vendor code changes.


## A39 graphics pipeline dependency note

A39 keeps graphics pipeline creation inside the existing `Aurelian.Graphics` Vulkan backend boundary. Public pipeline inputs are Aurelian-owned records: raw SPIR-V word arrays per shader stage, stage kind and entry point metadata, vertex buffer layouts, vertex attributes, and narrow fixed-state toggles. Silk.NET Vulkan is used only at the native edge to create/destroy `VkShaderModule`, `VkPipelineLayout`, and `VkPipeline`; raw handles remain internal owner details.

The shader boundary is intentionally explicit and temporary: A39 consumes SPIR-V artifacts but does not produce them. SDSL-V remains Aurelian's source language, while the planned compiler-facing path is `SDSL-V -> HLSL or Slang -> DXC -> SPIR-V artifact -> Vulkan pipeline creation`. A39 adds no direct SDSL-V-to-SPIR-V path and no DXC, Vortice.Dxc, Vortice.Vulkan, assets/shaders project dependency, descriptor sets, push constants, uniform buffers, draw commands, pipeline bind commands, swapchain/window/surface, VMA/VMASharp, service locator, reflection, vendor/reference-code dependency, or global singleton.


## A41 — HLSL -> SPIR-V shader artifact M0

Status: implemented.

A41 turns the A40 DXC subprocess spike into a shader artifact layer for HLSL stage sources. `Aurelian.Shaders` now accepts typed HLSL vertex, fragment, and compute stage inputs with entry point, profile, and source name metadata; validates stage/profile alignment; invokes DXC only through the existing subprocess wrapper; captures SPIR-V bytes; computes lowercase SHA-256 hashes for UTF-8 HLSL source text and raw SPIR-V bytes; and writes deterministic JSON manifests with ordered fields, diagnostics, DXC arguments, hashes, and base64 SPIR-V payloads.

A41 deliberately remains a tooling/artifact milestone. It does not integrate SDSL-V emission, `Aurelian.Graphics`, `Aurelian.Assets`, Vulkan pipeline creation, Vortice.Dxc, Vortice.Vulkan, runtime DXC invocation, or direct SDSL-V -> SPIR-V generation. If DXC is unavailable, artifact tests assert unavailable diagnostics instead of failing normal test runs. The recommended next milestone is A42 — SDSL-V -> HLSL -> SPIR-V artifact M0.

## A42 shader compiler boundary

A42 keeps the SDSL-V -> HLSL -> SPIR-V artifact path entirely inside `Aurelian.Shaders`. DXC remains a tool/subprocess dependency used by shader artifact emission and tests, not a runtime graphics dependency. `Aurelian.Graphics` must continue to consume only SPIR-V bytes and metadata in later work; it must not reference SDSL-V parsers, HLSL emitters, `Microsoft.Direct3D.DXC`, or any `Vortice.Dxc` wrapper.

The A42 stage extraction is intentionally convention-based M0 metadata: generated HLSL must contain `VSMain` and `PSMain`, and the smoke fixture uses emitter conventions for `POSITION`, `SV_Position`, `COLOR0`, and `SV_Target0`. Real semantic annotations/reflection are deferred.

## A43 compiled shader contract bridge

A43 establishes the neutral handoff for compiled shader data. `Aurelian.Rendering.Contracts` may define `Aurelian.Rendering.Contracts.Shaders` DTOs because they are plain contract records/enums and contain no compiler, Vulkan, Silk, DXC, graphics backend, asset, or runtime handles. `Aurelian.Shaders` may reference `Aurelian.Rendering.Contracts` to export SPIR-V shader artifacts into compiled shader contracts. `Aurelian.Graphics` may reference `Aurelian.Rendering.Contracts` to map compiled shader contracts into Vulkan pipeline stage descriptors.

The forbidden edges remain forbidden: `Aurelian.Graphics` must not reference `Aurelian.Shaders`, `Aurelian.Shaders` must not reference `Aurelian.Graphics`, and graphics must not depend on DXC, SDSL-V parsers, HLSL emitters, or runtime shader compilation. The compiled shader contract bridge is an artifact-consumption seam, not an asset pipeline or shader compiler integration inside the graphics backend.

## A44 graphics compiled shader consumption boundary

A44 keeps shader compilation and graphics pipeline creation separated by the neutral `Aurelian.Rendering.Contracts.Shaders` layer. `Aurelian.Graphics` may consume `CompiledShaderProgram` values, map their SPIR-V bytes into Vulkan shader stage descriptors, and build a `VulkanGraphicsPipelineDescriptor` or native Vulkan pipeline through existing graphics factories. It must not reference `Aurelian.Shaders`, call DXC, inspect SDSL-V artifacts, or depend on shader compiler package types.

The allowed dependency direction remains:

```text
Aurelian.Shaders -> Aurelian.Rendering.Contracts <- Aurelian.Graphics
```

The disallowed directions remain:

```text
Aurelian.Graphics -> Aurelian.Shaders
Aurelian.Shaders -> Aurelian.Graphics
Aurelian.Graphics -> DXC/SDSL-V compiler packages
```

A44 does not authorize asset/TOML integration, draw/bind commands, descriptor sets, uniforms/push constants, surfaces, swapchains, windows, Vortice, or VMA adoption.

## A45 draw command dependency note

A45 keeps draw recording inside the existing `Aurelian.Graphics` Vulkan backend boundary. Render pass scope tokens, draw requests, diagnostics, and results are Aurelian-owned contracts; Silk.NET Vulkan appears only at the native command emission edge for `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdBindPipeline`, `vkCmdBindVertexBuffers`, and `vkCmdDraw`.

The draw encoder consumes existing graphics pipeline and buffer objects only. It does not add any dependency from `Aurelian.Graphics` to `Aurelian.Shaders`, DXC, SDSL-V compiler code, assets, world, null rendering, Dominatus, reference folders, Vortice, VMA/VMASharp, swapchains/windows/surfaces, descriptor systems, uniforms, push constants, index buffers, or reflection/service-locator construction.

## A46 graphics SPIR-V fixture policy

A46 allows tiny static SPIR-V byte fixtures in `tests/Aurelian.Graphics.Tests` for backend recording proofs. These fixtures are test data, not a production shader compiler integration: they are checked in as source arrays, include generation/validation notes, and are consumed through `Aurelian.Rendering.Contracts.Shaders` DTOs.

This does not relax the graphics dependency boundary. `Aurelian.Graphics` must not reference `Aurelian.Shaders`, DXC packages, SDSL-V/HLSL compiler internals, Vortice, VMA/VMASharp, assets, world, Dominatus, vendor code, windows, surfaces, or swapchains as part of the A46 offscreen recording proof. Presentation and visual output remain separate future milestones.

## A47 command submit dependency note

A47 keeps queue submission inside the existing `Aurelian.Graphics` Vulkan backend boundary. The new submitter exposes Aurelian-owned request/result/status/diagnostic types and uses Silk.NET Vulkan only at the native queue-submit edge for one executable command buffer and one timeline semaphore signal.

Command submission remains per plant and uses the existing `VulkanFenceBundle.CommandListFence` and `VulkanCommandBufferPool` rather than a global scheduler, service locator, renderer facade, or resource cleanup system. A47 does not add swapchains/windows/surfaces, present/acquire, descriptor systems, uniforms, index buffers, shader compiler references, VMA/VMASharp, Vortice, or vendor/reference-code dependencies.

## A49 swapchain acquire/present dependency note

A49 keeps swapchain acquire/present work inside the existing `Aurelian.Graphics.Vulkan.Presentation` boundary. It uses the existing Silk.NET Vulkan KHR surface/swapchain extension plumbing to create binary semaphores, acquire swapchain images, and present image indices while returning Aurelian-owned typed results and diagnostics.

The milestone does not add any dependency from `Aurelian.Graphics` to shader compiler projects, runtime DXC, assets, world, null rendering, Dominatus, Stride/reference code, VMA/VMASharp, Vortice, service locators, reflection, or global graphics singletons. Present M0 deliberately does not wait on the render-finished semaphore yet because no render-to-swapchain or compositor submission exists in A49.

## A50 compositor policy/mechanism split dependency note

A50 keeps compositor design split across existing dependency seams. Neutral compositor facts, requests, results, diagnostics, plant-output refs, and presentation-target refs should be plain DTOs in `Aurelian.Rendering.Contracts/Compositor`; they must not contain Vulkan handles, Silk.NET structs, Dominatus types, graphics resource owners, or world objects.

Runtime compositor policy belongs in `Aurelian.Runtime/Compositor` because runtime already composes rendering contracts and Dominatus. Graphics compositor mechanism belongs in `Aurelian.Graphics/Vulkan/Compositor` because it will own Vulkan image wrappers, barriers, copy/blit/compute command recording, submit, and semaphore handoff. The forbidden edges remain forbidden: `Aurelian.Graphics` must not reference Dominatus or runtime policy, and runtime policy contracts must not depend on `Aurelian.Graphics`. A51 should implement neutral contracts first, with no Vulkan, no Dominatus, and no graphics implementation.


## A51 compositor contracts dependency note

A51 places compositor M0 DTOs in `Aurelian.Rendering.Contracts.Compositor`, preserving `Aurelian.Rendering.Contracts` as the neutral rendering contract assembly. The contracts may be consumed by future runtime policy and graphics mechanism code, but they do not reference `Aurelian.Graphics`, `Aurelian.Runtime`, `Aurelian.World`, Dominatus, Silk.NET, Vulkan handles, shader projects, or backend resource owners.

The intended next step, **A52 — Swapchain image wrappers M0**, belongs in `Aurelian.Graphics`: it should wrap acquired swapchain images as backend mechanism targets, initialize presentation-image layout tracking, and keep ownership/handle details out of neutral contracts.

## A52 swapchain image wrapper dependency note

A52 keeps swapchain-image addressability inside `Aurelian.Graphics.Vulkan.Compositor`. The graphics mechanism now exposes non-owning presentation target wrappers for swapchain images and image views through `AurelianVulkanSwapchain.CreatePresentationTargetImageSet()`, while neutral `PresentationTargetRef` values from `Aurelian.Rendering.Contracts.Compositor` remain symbolic DTOs with no Vulkan handles.

The wrappers are explicitly not ordinary allocated textures: they do not own image memory, do not call allocator APIs, do not destroy swapchain images, and do not destroy swapchain image views. Each wrapper carries plant ID, swapchain image index, format, extent, internal native handles, and a one-mip/one-layer `VulkanLayoutTracker` initialized to `Present`. Copy/blit, barrier emission, command submission, presentation, Dominatus policy, VMA/VMASharp, Vortice, CodeReferences changes, and shader/compiler dependencies remain deferred.


## A53 compositor passthrough dependency note

A53 keeps the compositor copy mechanism inside `Aurelian.Graphics.Vulkan.Compositor` and continues to consume only neutral contracts from `Aurelian.Rendering.Contracts.Compositor`. `Aurelian.Graphics` still does not reference `Aurelian.Runtime`, Dominatus, `Aurelian.World`, shader compiler projects, Vortice, VMA/VMASharp, CodeReferences, or service-locator/reflection paths.

The new plant-output wrappers are non-owning views over existing `AurelianVulkanTexture` resources, and presentation targets remain non-owning swapchain image wrappers. Barrier emission is extended only enough to handle presentation target images without pretending that swapchain images are allocated textures. Policy selection, differential/reduced-frequency behavior, multi-GPU transfers, compute compositor pipelines, and present semaphore handoff remain deferred.

## A54 visible triangle dependency note

A54 keeps the first visible triangle proof inside `Aurelian.Graphics` tests and existing Vulkan mechanism seams. The integration path uses checked-in SPIR-V fixture bytes from graphics tests, existing offscreen draw helpers, the A53 passthrough compositor, and the A49 swapchain acquire/present wrapper; it does not add a graphics reference to `Aurelian.Shaders`, DXC, `Aurelian.Runtime`, Dominatus, `Aurelian.World`, Vortice, VMA/VMASharp, CodeReferences, vendor changes, service locators, singletons, or reflection.

The proof is intentionally not a renderer facade and not a frame loop. Runtime/Dominatus policy, differential composition, present-loop/frame-pump ownership, descriptor/uniform/index-buffer work, multi-GPU transfer, asset/TOML integration, and shader compiler integration remain deferred behind the existing contracts and graphics mechanism boundaries.

## A55 compositor policy dependency note

The A55 runtime compositor policy keeps the dependency direction explicit: `Aurelian.Runtime` may reference Dominatus and `Aurelian.Rendering.Contracts` to decide and emit compositor acts, while the neutral contracts do not reference Dominatus and `Aurelian.Graphics` does not reference runtime policy. `CompositorDispatchAct` is runtime-local and wraps a neutral request; tests complete it with a fake actuator rather than a graphics or Vulkan bridge.

The bridge from runtime acts to graphics compositor mechanism is intentionally deferred to A56, so A55 adds no new package dependency, no graphics reference in runtime, no Vulkan handles in policy state, no service locator, and no global compositor singleton.

## A56 compositor bridge dependency note

A56 uses an integration test project as the only place where runtime compositor policy and graphics compositor mechanism are composed. This is an allowed test-only dependency boundary: `tests/Aurelian.Integration.Tests` references runtime, graphics, and rendering contracts to prove the seam without adding a production bridge assembly.

Production dependency policy remains unchanged. `Aurelian.Runtime` may use Dominatus and neutral rendering contracts but must not reference `Aurelian.Graphics`; `Aurelian.Graphics` may use neutral rendering contracts and Silk.NET Vulkan plumbing but must not reference runtime policy or Dominatus; `Aurelian.Rendering.Contracts` stays neutral. A production host/frame pump is explicitly deferred rather than hidden behind service locators, global singletons, reflection, or new package dependencies.

## A57 Core integration spine dependency rule

A57 establishes `Aurelian.Core` as the engine integration spine M0 rather than adding an `Aurelian.Host` project. For this milestone, Core may reference `Aurelian.Runtime` and `Aurelian.Rendering.Contracts` so it can expose engine-level lifecycle identity and bridge Runtime compositor acts to neutral compositor mechanism contracts.

Core remains graphics-free in A57. It must not reference `Aurelian.Graphics`, Silk.NET, Vulkan, swapchain/window/surface APIs, shader tooling, service locators, global singletons, reflection-based construction, vendor samples, or CodeReferences. Runtime remains free of Graphics, Graphics remains free of Runtime/Dominatus, and Rendering.Contracts remains a neutral DTO boundary.

The production frame pump and concrete Vulkan compositor adapter are deferred. The next dependency decision should keep the Vulkan mechanism behind an Aurelian-owned adapter seam rather than letting Runtime or neutral contracts depend on backend details.

## A58 dependency note — Core-to-Graphics adapter

A58 intentionally allows `Aurelian.Core -> Aurelian.Graphics` because Core is now the high-level engine integration spine. This exception does not relax lower-level dependency boundaries: `Aurelian.Runtime` must remain graphics-free, `Aurelian.Graphics` must remain Runtime/Dominatus-free, and `Aurelian.Rendering.Contracts` must remain neutral.

The A58 Vulkan compositor adapter depends on prebuilt graphics mechanism objects supplied by the caller. It does not act as a service locator, create global singletons, instantiate windows or swapchains, add packages, or hide frame-loop ownership inside Core.

## A59 frame pump dependency note

A59 keeps high-level frame orchestration in `Aurelian.Core` and keeps mechanism work behind Aurelian-owned contracts. The new frame pump references the existing Runtime compositor policy shell and Dominatus actuator host only to execute one local policy tick; compositor actuation exits Core through `CompositorActuationBridge` and the neutral `ICompositorMechanism` interface.

The frame pump does not introduce new packages, global services, reflection-based construction, singletons, a host project, or direct Vulkan/window/swapchain setup. `Aurelian.Runtime` remains graphics-free, `Aurelian.Graphics` remains runtime/Dominatus-free, and `Aurelian.Rendering.Contracts` remains neutral.

## A60 visible frame pump integration dependency note

A60 validates the allowed Core-to-Graphics adapter exception in the real visible compositor path, but only through the integration test project. The production frame pump continues to depend on prepared `AurelianFrameInput`, `CompositorActuationBridge`, and `ICompositorMechanism`; it does not allocate or create Vulkan plants, windows, surfaces, swapchains, textures, command pools, graphics resources, or compositor mechanisms.

No production dependency boundaries are relaxed: `Aurelian.Runtime` remains graphics-free, `Aurelian.Graphics` remains Runtime/Dominatus-free, `Aurelian.Rendering.Contracts` remains neutral, no `Aurelian.Host` project is added, and no new package, service locator, singleton, reflection construction path, VMA/VMASharp, Vortice, CodeReferences, or vendor modification is introduced.


## A61 engine graphics subsystem options dependency note

A61 keeps Core graphics lifecycle policy explicit while preserving the A58 Core-to-Graphics adapter exception. The new `Aurelian.Core.Engine.Graphics` namespace contains generic options, ownership, prepared subsystem, presentation mechanism, result, and diagnostic vocabulary; it contains no Vulkan, Silk.NET, surface, swapchain, or native handle types.

M0 supports only externally owned prepared graphics. Core validates that `PreparedVisible` bundles provide prepared compositor and presentation mechanisms, but it does not create or dispose Vulkan plants, devices, windows, swapchains, command pools, graphics resources, or presentation objects. No `Aurelian.Host`, package, service locator, singleton, reflection construction path, CodeReferences change, or vendor modification is introduced.

## A62 minimal visible triangle sample dependency note

A62 adds a sample executable under `samples/Aurelian.VisibleTriangle` and does not relax production dependency boundaries. The sample references Core, Graphics, Runtime, and Rendering.Contracts to compose the already-existing prepared-visible path manually; it does not reference test projects, `Aurelian.Shaders`, `Aurelian.Assets`, `Aurelian.AssetTool`, or `Aurelian.World` directly.

The only production graphics changes are a hidden-by-default `VulkanSwapchainCreateOptions.Visible` flag and a minimal `AurelianVulkanSurface.PumpEvents()` method so the sample can show and briefly service its window. Defaults remain test/headless-safe. No new packages, service locator, singleton, reflection construction path, `Aurelian.Host`, VMA/VMASharp, Vortice, CodeReferences change, or vendor modification is introduced.

## A63 minimal production frame loop dependency note

A63 keeps high-level frame-loop orchestration in `Aurelian.Core.Engine.Frames` while preserving prepared ownership boundaries. The new `AurelianFrameLoop` consumes an existing `AurelianFramePump`, obtains prepared frame inputs through `IAurelianFrameInputProvider`, and optionally presents through `IPresentationMechanism`; it does not allocate or create Vulkan plants, windows, surfaces, swapchains, graphics resources, compositor mechanisms, or presentation mechanisms.

No dependency boundary is relaxed: `Aurelian.Runtime` remains graphics-free, `Aurelian.Graphics` remains Runtime/Dominatus-free, and `Aurelian.Rendering.Contracts` remains neutral. A63 adds no packages, `Aurelian.Host`, service locator, singleton, reflection construction path, VMA/VMASharp, Vortice, CodeReferences change, or vendor modification. The visible triangle sample conversion to the frame loop is intentionally deferred.

## A64 runtime session boundary

A64 makes the preferred `Aurelian.Runtime` tick path explicitly Dominatus-backed through `Aurelian.Runtime.Sessions.AurelianRuntimeSession`. Runtime may own Dominatus session orchestration, typed runtime tick acts, typed diagnostics, and world-runner seams. Runtime must not grow a hand-rolled `TickControl -> DispatchActs -> TickSimulation -> ExtractRenderSnapshot -> SubmitRender` processor loop.

`Aurelian.Core` remains the engine integration spine and may call a runtime session from a future frame-loop bridge. `Aurelian.Graphics` remains mechanism-only and is not referenced by Runtime. World/render extraction and graphics submission remain deferred seams outside the A64 runtime tick.

The inspected `ParallelAiWorldRunner` currently belongs to vendored `Dominatus.Core` as a staged parallel agent runner for one `AiWorld`. A64 leaves it there and exposes `IAurelianAiWorldRunner` so Runtime can adopt a deliberate adapter later without moving vendor code or creating dependency cycles.

## A65 Core/runtime seam note

A65 adds a narrow Core-owned runtime ticker seam for frame orchestration. Core may depend on Runtime session DTOs/results because Core owns high-level engine/frame orchestration, but Runtime must not depend back on Core. The real `AurelianRuntimeSession` path is connected by a Core-side adapter, preserving the dependency direction and avoiding a project cycle.

The seam is intentionally not a service locator, singleton, reflection hook, graphics bridge, or renderer abstraction. It only lets the frame loop tick a prepared runtime session before the existing frame pump. Runtime remains graphics-free; Graphics remains runtime/Dominatus-free; Rendering.Contracts remains neutral.

## A66 visible triangle frame-loop/runtime-session sample note

A66 is a sample conversion only. `samples/Aurelian.VisibleTriangle` keeps its explicit references to Core, Graphics, Runtime, and Rendering.Contracts so the sample can compose already-prepared visible Vulkan resources with `AurelianEngine`, `AurelianRuntimeSession`, the Core runtime ticker adapter, `AurelianFrameLoop`, the runtime compositor policy, the Core compositor bridge, the Vulkan compositor mechanism, and the prepared presentation mechanism.

No dependency boundary is relaxed: Runtime remains graphics-free, Graphics remains runtime/Dominatus-free, Core remains the high-level integration spine, and the sample does not reference `Aurelian.Shaders`, `Aurelian.Assets`, `Aurelian.AssetTool`, or `Aurelian.World` directly. A66 adds no packages, service locator, singleton, reflection construction path, `Aurelian.Host`, VMA/VMASharp, Vortice, CodeReferences change, or vendor modification. The finite sample is one-frame M0 until a later milestone adds safe per-frame swapchain acquire/present.


## A67 visible sample multi-frame dependency note

A67 is a sample-local lifecycle update only. The visible triangle sample may hold a finite queue of acquired swapchain image indices and finite per-frame `PlantOutputRef` wrappers because it owns the prepared Vulkan setup, but this does not move Vulkan/window/swapchain creation into Core or Runtime.

The dependency boundaries remain unchanged: Runtime stays graphics-free, Graphics stays runtime/Dominatus-free, Core remains the high-level frame/engine spine, and the sample adds no package references, runtime shader compiler dependency, asset dependency, service locator, singleton, reflection construction path, VMA/VMASharp, Vortice, CodeReferences change, or vendor modification.

## A68 visible-sample event pump boundary

A68 uses the existing Silk.NET Windowing dependency only inside the Vulkan presentation owner and the visible sample. `AurelianVulkanSurface` exposes a narrow close-status property for its owned window in addition to its existing event pump; it does not expose raw `IWindow`, keyboard devices, or a general input abstraction. The sample keeps close handling in `samples/Aurelian.VisibleTriangle` through `VisibleTriangleWindowState`, and Core/Runtime continue to receive only frame input/provider and presentation contracts.

The dependency boundary remains unchanged: no new package references, no production host/window lifecycle, no service locator or singleton, no runtime shader compiler dependency, and no graphics dependency on `Aurelian.Shaders` or asset tooling.

## A69 shader artifact file bridge policy

A69 establishes the primary runtime shader artifact shape as TOML metadata plus external SPIR-V byte files and optional generated/debug HLSL. Generated build artifacts may use raw `.spv`; checked-in sample artifacts use A69b text-safe `.spv.hex` transport files:

```text
shader.toml
VSMain.spv.hex
PSMain.spv.hex
generated.hlsl
```

Responsibilities remain layered:

- `Aurelian.Shaders` owns shader compiler/tooling emission and may write artifact files from successful compiler artifacts.
- `Aurelian.Assets` may parse TOML shader artifact manifests and load hash-checked SPIR-V bytes into neutral `CompiledShaderProgram` contracts. `spirv_encoding = "binary"` reads raw bytes, and `spirv_encoding = "hex"` decodes text hex before hashing.
- `Aurelian.Rendering.Contracts` owns the neutral compiled shader DTOs.
- `Aurelian.Graphics` consumes `CompiledShaderProgram` only and must not reference `Aurelian.Shaders`, `Aurelian.Assets`, DXC, SDSL-V compiler code, or shader artifact writer/loader code.
- Samples may load checked-in artifact files through `Aurelian.Assets`, but must not compile SDSL-V/HLSL at runtime or depend on `Aurelian.Shaders` for the runtime path.

C# SPIR-V byte arrays are fixture/bootstrap-only and are not the primary runtime artifact format. `spirv_sha256` is always computed over decoded/raw SPIR-V bytes, not over hex transport text.

## A70 asset manifest shader reference policy

A70 allows `Aurelian.Assets` asset manifests to reference existing shader artifact TOML files through repeated `[[shaders]]` entries. The asset layer owns the manifest schema, validation, path safety rules, manifest-relative path resolution, and mapping of shader artifact load failures into asset diagnostics. The shader artifact loader still returns neutral `CompiledShaderProgram` contracts from `Aurelian.Rendering.Contracts`.

This does not relax the graphics boundary. `Aurelian.Assets` must not reference `Aurelian.Graphics`, Vulkan/Silk graphics APIs, swapchain/surface/window concepts, runtime DXC, VMA/VMASharp, Vortice, service locators, global singletons, reflection factories, CodeReferences, or vendor source. A70 also does not introduce material, mesh, texture, cache, hot-reload, or graphics-resource ownership semantics.


## A71 visible sample manifest consumption policy

A71 allows the visible triangle sample executable to reference `Aurelian.Assets` and call `ShaderAssetManifestLoader` directly as sample-local composition code. The sample resolves `Assets/assets.toml`, selects shader id `smoke_triangle`, and gives the resulting neutral `CompiledShaderProgram` to the existing `Aurelian.Graphics` pipeline setup.

This is not a new engine-wide asset manager boundary. The policy remains that `Aurelian.Assets` owns manifest parsing/validation and artifact loading while staying free of `Aurelian.Graphics`, Vulkan/Silk graphics APIs, swapchain/surface/window concepts, and graphics resources. `Aurelian.Graphics` continues to consume neutral shader contracts only and must not reference `Aurelian.Assets`, `Aurelian.Shaders`, DXC, SDSL parsing/emission, asset manifests, service locators, global singletons, reflection factories, CodeReferences, or vendor source. A71 adds no material, mesh, texture, cache, hot-reload, or runtime shader compilation semantics.
