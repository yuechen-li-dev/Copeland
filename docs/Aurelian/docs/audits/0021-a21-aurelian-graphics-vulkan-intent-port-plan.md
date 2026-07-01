# A21 — Aurelian.Graphics Vulkan Intent-Port and Bring-Up Plan

## 1. Files changed

- Created this report: `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`.
- No source files were changed.
- No project files were changed.
- No packages were added.
- `CodeReferences/*` and `vendor/Dominatus/*` were not modified.

## 2. Task scope

A21 is a docs/design milestone. Its purpose is to choose the first real graphics direction after the A20 headless chain:

```text
WorldDataDocument
  -> RenderSnapshot
  -> RenderCommandPlan
  -> NullRenderer trace
```

A21 does not implement Vulkan, create a graphics project, add package references, open a window, or draw a triangle. It defines the practical architecture and milestone sequence for a future `Aurelian.Graphics` bring-up.

The non-negotiable constraints for this plan are:

- preserve Dominatus orchestration and the plant/controller model;
- preserve render snapshots, command plans, and the null/headless path;
- keep `Aurelian.Rendering.Contracts` pure and renderer-independent;
- use explicit contracts and plain data boundaries;
- avoid reflection, service locators, and global graphics singletons;
- do not import Stride's effect/material system;
- use `Silk.NET.Vulkan` and `Silk.NET.Windowing` for the first Vulkan backend path;
- keep Vortice only as a possible future alternative if Silk.NET becomes painful.

## 3. Executive recommendation

Create `Aurelian.Graphics` as the first graphics HAL project in A22, using Vulkan internally for M0 and adding Silk.NET Vulkan/windowing packages only in that milestone.

The recommended initial shape is:

```text
src/Aurelian.Graphics/
  Plants/
  Vulkan/
    Device/
    Sync/
    Resources/
    Commanding/
    Pipelines/
    Presentation/
    Compositor/
    Diagnostics/

tests/Aurelian.Graphics.Tests/
```

Use `Aurelian.Graphics`, not `Aurelian.Graphics.Vulkan`, as the first project name because the real dependency boundary is currently “graphics HAL vs pure rendering contracts”, not “multiple backend implementations”. Vulkan can remain an internal implementation detail for M0. Split to a backend-specific project later only if a second backend appears or dependency isolation becomes necessary.

Recommended next milestone:

```text
A22 — Aurelian.Graphics project scaffold and Silk.NET package references
```

A22 should create the project/folders and add `Silk.NET.Vulkan` plus `Silk.NET.Windowing`, but should not implement device creation beyond package/structure smoke.

## 4. Reference material inspected

### Current Aurelian state

The repo currently contains architecture docs, A0–A20 audit reports, the Claude Vulkan audit, Stride/Machina/Copeland/WyrmCoil reference material, Prometheus reference material, and source projects for core, actuation, assets, runtime, shaders, world, rendering contracts, and null rendering.

Key current-state facts:

- A20 documents the full headless path from world data through render snapshots and command plans to the null renderer.
- The dependency policy explicitly allows Silk.NET/Vortice for bounded native graphics/window/platform binding plumbing, but not as owners of Aurelian's world, render snapshot, asset, or material architecture.
- `Aurelian.Rendering.Contracts` is already the pure renderer-independent layer and must remain free of GPU/windowing packages.
- `Aurelian.Rendering.Null` proves render command plans can be consumed headlessly without GPU/window dependencies.

### Claude Vulkan intent-port audit

Read `docs/claude/aurelian-vulkan-intent-port-audit.md` and extracted its subsystem intent, “Do Not Carry Over” items, multi-GPU plant/controller mandate, and bring-up order.

The strongest architectural takeaway is that every `VkDevice` should be treated as a plant from day one. Even single-GPU M0 should use plant 0 through the same registry/routing path intended for future multi-GPU operation.

### Stride.Graphics reference corpus

Inspected the Stride.Graphics file list and Vulkan search results. The reference corpus contains 397 files under `CodeReferences/Stride/Stride.Graphics`, including Vulkan implementations for device, command list, pipeline state, swapchain, texture, buffer, descriptor set layout, barrier mapping, and graphics resource base.

Representative files inspected include:

- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/PipelineState.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/SwapChainGraphicsPresenter.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Texture.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/DescriptorSetLayout.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/BarrierMapping.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResource.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResourceBase.Vulkan.cs`

The code inspection confirms the Claude audit direction: Stride is useful as a reference corpus for Vulkan intent and pitfalls, but should not be used as a foundation or literal port.

### Prometheus SGEMM reactor reference

Read `CodeReferences/Prometheus/reactor_vulkan_sgemm.c` and searched for reactor, arena, selector/cache, generation, policy, budget, telemetry, fence, command, descriptor, pool, Vulkan, submit, staging, and controller concepts.

The Prometheus file is compute-specific, but it provides useful patterns for arena lifecycle, generation-tagged caches, controller policy feedback, resource ownership checks, and telemetry/control loops.

## 5. Silk.NET vs Vortice decision

### Chosen first path: Silk.NET

Use:

- `Silk.NET.Vulkan`
- `Silk.NET.Windowing`
- possibly `Silk.NET.Core` if required by package shape

Reasons:

- Silk.NET is bindings-first and less likely to capture Aurelian architecture.
- Silk.NET Vulkan pairs naturally with Silk.NET Windowing for cross-platform surface creation.
- It fits the dependency policy: native graphics/windowing plumbing behind Aurelian-owned contracts.
- It supports a Vulkan-first plan without importing another engine's object model.
- It aligns with an explicit, low-level HAL where `PlantContext`, resources, command lists, and presentation are Aurelian-owned.

### Deferred alternative: Vortice.Vulkan

Do not use Vortice.Vulkan for the first backend. Keep it as a future option if Silk.NET becomes painful because of package/API friction, NativeAOT constraints, unsupported surface behavior, or binding ergonomics.

Stride's Vulkan reference uses Vortice, but that is not a reason to choose Vortice for Aurelian. Aurelian should read Stride's intent and pitfalls, not inherit its binding choice or architecture.

### Packages not added in A21

A21 documents the package plan only. No packages were added.

Potential implementation-milestone package candidates:

| Candidate | Intended role | A21 decision |
| --- | --- | --- |
| `Silk.NET.Vulkan` | Vulkan bindings | First backend path; add in A22. |
| `Silk.NET.Windowing` | Window and cross-platform Vulkan surface creation | First presentation path; add in A22. |
| `Silk.NET.Core` | Shared Silk.NET abstractions if required transitively or explicitly | Evaluate during A22 package smoke. |
| VMASharp / VMA binding | Vulkan memory allocator binding | Evaluate before resource implementation; do not add until allocator milestone. |

## 6. Stride.Graphics intent-port summary

Aurelian should borrow intent from Stride but discard the engine model, lifecycle assumptions, and known bugs.

### Device init intent

Borrow:

- query physical device limits and features during device init;
- cap descriptor counts against device limits;
- enable only supported extensions;
- require timeline semaphore support for Aurelian's sync model;
- create per-device fences/semaphores and pools;
- seed upload/staging infrastructure;
- create sentinel/null resources only if Aurelian exposes descriptors that need non-null fallback bindings.

Design differently:

- select the best graphics/compute/transfer-capable queue family explicitly;
- cache physical-device memory properties once during plant init;
- initialize each `VkDevice` as an isolated plant, not a global graphics device;
- make debug utilities per plant/device, not static/global;
- use Silk.NET bindings, not Vortice, in the first backend;
- evaluate VMA/VMASharp before implementing resources rather than doing per-resource `vkAllocateMemory`.

### Fence/sync intent

Borrow:

- timeline semaphore style for frame, command-list, and copy progress;
- distinct frame/command/copy fence roles;
- binary semaphores for swapchain acquire/present where Vulkan requires them.

Design differently:

- make fences per plant;
- add a future compositor fence that represents all assigned plant outputs being ready;
- avoid unsynchronized shared completed-fence state;
- avoid starvation-prone CPU wait patterns;
- publish fence facts/telemetry for Dominatus to observe later.

### Resource pool intent

Borrow:

- fence-tagged recycler pattern: retire objects with a fence value and reuse only after completion;
- separate pools for command buffers and descriptor pools;
- reset pooled Vulkan objects before reuse.

Design differently:

- pools are per plant;
- avoid holding locks during slow Vulkan allocation;
- avoid LINQ/hot-path allocation when creating descriptor pools;
- size descriptor pools from observed demand and high-water marks;
- add generation-tagged selector/cache hints later.

### Command list intent

Borrow:

- explicit lifecycle: reset, record, close/submit, recycle;
- command buffers per command list;
- transition resources before draw/copy use;
- end active render passes before barriers;
- integrate command-buffer and descriptor-pool recycling with fences.

Design differently:

- command lists are per plant and receive routed render work by `PlantId`;
- use a first-class layout tracker instead of shared mutable `NativeLayout`;
- honor per-subresource barriers;
- batch barriers before draw/copy use;
- do not expose public API methods that throw `NotImplementedException`;
- fix the Stride `DrawInstanced` first-instance argument bug by defining Aurelian draw contracts explicitly;
- avoid allocating descriptor sets per draw forever; start correct, then cache by descriptor content/hash.

### Pipeline state intent

Borrow:

- compile plain pipeline descriptions into `VkPipeline`, `VkPipelineLayout`, descriptor set layouts, shader modules, and render-pass compatibility data;
- model vertex input, assembly, rasterizer, depth/stencil, blend, multisample, viewport/scissor dynamic state;
- support compute pipelines later if needed by compositor.

Design differently:

- one shader module per stage;
- use SDSL-V artifact hashes and per-stage artifacts as cache inputs;
- maintain a per-plant `VkPipelineCache` and persist/invalidate it deliberately;
- derive depth bias and attachment load ops from Aurelian pipeline/render-pass descriptors;
- validate unsupported multisample requests rather than ignoring them;
- make explicit render pass descriptors instead of implicit Stride-style render pass creation.

### Swapchain/presentation intent

Borrow:

- manage `VkSurfaceKHR`, `VkSwapchainKHR`, image views, acquire semaphores, present semaphores, format selection, present mode selection, out-of-date/suboptimal handling, and pre-transform choice;
- recognize acquire/present as their own sync boundary.

Design differently:

- surface creation uses `Silk.NET.Windowing`;
- swapchain belongs to the presentation/compositor boundary, not a global graphics device;
- plant 0 is both render plant and presentation plant in M0 only by assignment, not by architecture;
- avoid queue-wide idle waits when fence-scoped waits are sufficient;
- do not expose unimplemented fullscreen setters or platform branches;
- keep presentation policy facts available for Dominatus later.

### Compositor intent

Stride has no equivalent subsystem. Aurelian should introduce the compositor seam early.

M0 compositor:

- passthrough path;
- one plant output;
- presentation plant is plant 0;
- writes/blits to the swapchain backbuffer;
- emits placeholder diagnostics.

Future compositor:

- accepts multiple `PlantOutput` images;
- runs on presentation plant;
- supports differential, SFR, AFR, or software fallback policies;
- feeds agreement rate, plant utilization, and divergence telemetry to Dominatus;
- uses external memory/cross-plant transfer paths rather than CPU readback where possible.

### Texture/buffer resource intent

Borrow:

- create buffers/images/views from validated descriptions;
- map usage flags to Vulkan usage/access/layout state;
- use staging/upload paths for device-local resources;
- maintain views for shader/resource-target usages;
- defer destruction until relevant fence values complete.

Design differently:

- every resource handle/state carries `PlantId`;
- use VMA/VMASharp if viable, avoiding per-resource Vulkan memory allocation;
- persistently map dynamic/staging buffers where possible;
- use per-subresource layout tracking;
- do not over-broaden depth/stencil stages;
- avoid unnecessary zero-fill GPU work;
- use last-submitted fence values for deferred destruction.

### Descriptor set layout intent

Borrow:

- descriptor layout entries compile to `VkDescriptorSetLayoutBinding[]`;
- descriptor type counts feed descriptor-pool sizing/exhaustion logic;
- immutable samplers can be embedded in layouts.

Design differently:

- cache layouts per `(PlantId, LayoutHash)`;
- derive stage visibility from SDSL-V artifact/reflection metadata;
- implement or explicitly disallow immutable sampler arrays before public exposure;
- avoid magic descriptor-type counts without validation/static assertion.

### Barrier abstraction intent

Borrow:

- keep an Aurelian-level layout/access/sync abstraction that maps to Vulkan layout, access flags, and pipeline stages;
- centralize mappings instead of scattering Vulkan barriers through resource code.

Design differently:

- add precise shader-resource variants such as vertex/all-shader visibility;
- avoid pessimistic `General` layout except where required;
- model cross-plant transfer source/destination variants for future ownership transfers;
- batch barrier emission;
- use generation-tagged cache decisions where stable layouts repeat.

### Graphics resource base intent

Borrow:

- common resource state for memory, native access/stage/layout state, pending fence values, debug names, and deferred destruction.

Design differently:

- extract state into a value-like `GpuResourceState` that carries `PlantId`;
- do not store hard references from resources back to updating command lists;
- wire debug names with `VK_EXT_debug_utils` rather than deprecated/stubbed marker code;
- cache memory properties and use an allocator rather than per-resource allocation.

## 7. Prometheus reactor lessons

The Prometheus SGEMM reactor is not a graphics renderer and should not be copied literally. Its SGEMM-specific shader selection, matrix layout, slot batching, and compute dispatch details remain reference-only. However, several patterns should inform Aurelian.Graphics.

### Arena lifecycle ideas

Prometheus tracks typed arenas by role, required bytes, capacity bytes, live committed bytes, generation, ownership, validity, in-flight state, failure reasons, low-usage epochs, cooldowns, and grow/shrink/rebuild counters.

Aurelian application:

- create per-plant arenas/pools by resource role: upload, staging, vertex, index, uniform, texture, descriptor, command buffer;
- record `required_bytes`, `capacity_bytes`, high-water marks, and in-flight retire fences;
- grow when pressure is sustained;
- shrink only after low-usage epochs and cooldowns;
- make budget decisions visible to Dominatus rather than hiding them in ad hoc allocator code.

### Selector/cache ideas

Prometheus uses generation-tagged selector/cache patterns to avoid recomputing decisions when inputs have not changed.

Aurelian application:

- cache descriptor set layout selection by content hash and generation;
- cache pipeline compilation results by `(PlantId, PipelineHash)`;
- cache barrier mapping decisions when resource generation/layout inputs are unchanged;
- cache command/descriptor pool availability hints to avoid hot-path locks when a ready object is unlikely.

### Budget policy ideas

Prometheus tracks policy thresholds, waste budgets, retreat/recovery, lookahead, outstanding depth, and safe/aggressive/recovery modes.

Aurelian application:

- expose upload budget facts per plant;
- expose descriptor pool pressure facts;
- use conservative defaults for M0 and leave policy hooks for later Dominatus decisions;
- avoid unbounded upload-buffer growth by adding grow/shrink policy before heavy resource streaming.

### Telemetry/control-loop ideas

Prometheus records submit counts, state transitions, failures, ownership violations, serialized Vulkan execution, worker resource IDs, fence IDs, and diagnostic counters.

Aurelian application:

- per-plant diagnostics should include queue family, device name, enabled features/extensions, frame fence value, command fence value, copy fence value, upload capacity/usage, descriptor pool usage, command-buffer pool usage, present mode, swapchain image count, and validation/debug status;
- compositor diagnostics should later include plant utilization, agreement/divergence, copy/transfer cost, and present latency;
- Dominatus can observe graphics facts on a blackboard and adjust assignment/policy without directly owning Vulkan handles.

### Compute-specific ideas to keep reference-only

Do not port:

- SGEMM-specific tile dimensions, matrix shape signatures, packed layout selectors, FP16/packed4 shader decisions, slot-specific compute policies, or matrix precision fallback rules;
- worker-slot execution mechanics except as inspiration for plant resource ownership validation;
- compute shader dispatch details except later for compositor/passthrough implementation.

## 8. Aurelian.Graphics project shape

### Recommended name: `Aurelian.Graphics`

Use one graphics HAL project first:

```text
src/Aurelian.Graphics/
tests/Aurelian.Graphics.Tests/
```

Initial folders:

```text
src/Aurelian.Graphics/
  Plants/
  Vulkan/
    Device/
    Sync/
    Resources/
    Commanding/
    Pipelines/
    Presentation/
    Compositor/
    Diagnostics/
```

### Name comparison

| Candidate | Assessment |
| --- | --- |
| `Aurelian.Graphics` | Recommended. Represents the real new HAL boundary. Vulkan can be internal for M0. Avoids project explosion and keeps the name open for a future backend split. |
| `Aurelian.Graphics.Vulkan` | Reasonable if dependency isolation is required immediately, but premature while only one backend exists. It also implies a separate non-Vulkan graphics abstraction project that does not exist yet. |
| `Aurelian.Rendering.Vulkan` | Less preferred because `Rendering.Contracts` already means renderer-independent render DTOs. A Vulkan backend is graphics/HAL plumbing, not the pure rendering contract layer. |
| `Aurelian.Rendering.Backend.Vulkan` | Too verbose and suggests a backend plugin architecture before Aurelian has a second backend or backend-loader need. |

### Dependency boundary

`Aurelian.Graphics` may reference:

- `Aurelian.Rendering.Contracts` for render command plans;
- `Aurelian.Shaders` later when consuming SDSL-V artifacts/SPIR-V manifests, if the dependency remains explicit and acyclic;
- Silk.NET packages after A22.

`Aurelian.Graphics` should not be referenced by:

- `Aurelian.World`;
- `Aurelian.Rendering.Contracts`;
- core world data stores or pure command-plan contracts.

The null renderer remains separate and continues to validate headless command plans.

## 9. Plant/controller graphics model

### Plant model

Planned concepts:

- `PlantId`: small value identity, `0` means plant 0 in single-GPU M0.
- `PlantContext`: immutable-ish runtime context for one Vulkan plant/device.
- `PlantRegistry`: indexed collection of plant contexts, one entry in M0.
- `AurelianVulkanDevice`: Aurelian-owned wrapper/state for one `VkDevice`, not a singleton.
- `GpuCapabilityTier`: per-plant facts derived during device init.
- `GpuResourceState`: resource state value carrying `PlantId` and layout/access/fence data.

Invariants from day one:

- no global graphics singleton;
- no resource can be bound to the wrong plant without an explicit transfer path;
- command lists are per plant;
- resource pools are per plant;
- debug/diagnostic state is per plant;
- the M0 single-GPU path is “one plant in the registry,” not a special architecture.

### Controller relationship

Dominatus eventually owns assignment and policy decisions. Aurelian.Graphics should expose facts/telemetry and accept explicit work assignments; it should not hide policy in global renderer state.

Planned relationship:

- `RenderCommandPlan` remains renderer-independent input.
- A future graphics actuator/controller maps command-plan draw work to plant-specific command lists.
- Dispatch/render acts carry `PlantId` hints.
- Dominatus reads plant facts and compositor diagnostics to adjust policies.
- M0 may use a simple fixed policy: all work goes to plant 0.

## 10. Subsystem design plan

### Device init

M0 device init should include:

- Vulkan instance creation through Silk.NET;
- optional validation layers and debug utils when available;
- physical device enumeration;
- queue family scoring/selection for graphics+compute+transfer;
- device extension validation, including swapchain extension when presentation is enabled;
- device feature validation, with timeline semaphores required;
- per-plant memory property cache;
- per-plant allocator initialization after VMA decision;
- per-plant fences/semaphores: frame, command, copy;
- per-plant command pool and descriptor pool seed;
- upload ring seed;
- per-plant diagnostics.

### Sync/fences

M0 sync should model:

- per-plant `FrameFence`;
- per-plant `CommandListFence`;
- per-plant `CopyFence`;
- timeline semaphore values as explicit facts;
- binary acquire/present semaphores at the swapchain boundary;
- future `CompositorFence` as a seam even if it is immediately satisfied for single-GPU passthrough.

Avoid unsynchronized global fence values and avoid CPU wait implementation that can starve lower fence-value waiters behind higher-value waits.

### Resources

Resources should be created from plain descriptors/plans, validated before Vulkan calls, and carry `PlantId`.

Planned resource families:

- buffers: vertex, index, uniform/constant, storage/structured, staging/upload;
- textures: 2D first, render target/depth/shader-resource usage later;
- views/descriptors: plain data descriptors resolved per plant;
- layout tracker: per resource and per subresource;
- allocator: evaluate VMASharp/VMA before implementation.

Do not use per-resource Vulkan memory allocation as the long-term model.

### Commanding

Commanding should include:

- one or more command lists per plant;
- command-buffer lifecycle integrated with per-plant pools and fence retirement;
- barrier batch accumulation;
- copy/update-subresource paths using upload/staging arenas;
- render pass begin/end discipline;
- descriptor allocation that is correct in M0 and cacheable later;
- no shared mutable `NativeLayout` equivalent.

### Pipelines

Pipelines should include:

- explicit pipeline descriptor plain data;
- explicit render pass descriptors;
- per-stage shader modules;
- SDSL-V artifact hashes as stable cache keys later;
- per-plant lazy compilation;
- per-plant `VkPipelineCache` and cache invalidation by relevant surface/render-pass format facts;
- validation for unsupported states instead of ignored settings.

### Presentation

Presentation should include:

- `Silk.NET.Windowing` window/surface path;
- `VkSurfaceKHR` and `VkSwapchainKHR` managed at presentation/compositor boundary;
- plant 0 as presentation plant in M0;
- format and present mode selection recorded as diagnostics/facts;
- acquire/present semaphore ring;
- out-of-date/suboptimal/surface-lost handling in a contained presentation subsystem.

### Compositor

The compositor seam should exist early even when it is passthrough.

M0:

- one plant output;
- one presentation plant;
- passthrough/blit/copy path to swapchain;
- simple diagnostics structure.

Deferred:

- differential compositor policy;
- cross-plant image import/export;
- external memory handle support;
- multi-GPU agreement telemetry;
- Dominatus policy feedback.

### Diagnostics

Diagnostics should be explicit plain data:

- plant id;
- device name/type/vendor;
- queue family indices;
- enabled layers/extensions/features;
- timeline fence values;
- upload/descriptor/command pool capacities and pressure;
- swapchain format/present mode/image count;
- compositor mode and per-frame metrics.

## 11. Do-not-carry-over checklist

Checklist synthesized from the Claude audit and confirmed by Stride code inspection:

- [ ] No `GraphicsDeviceStatus` dead stub that always reports normal.
- [ ] No `simulateReset` device-lost test shim in production API.
- [ ] No hardcoded queue family index 0.
- [ ] No per-resource `vkGetPhysicalDeviceMemoryProperties` query.
- [ ] No per-resource `vkAllocateMemory` as the long-term allocator model.
- [ ] No unrecycled/unbounded upload buffer growth.
- [ ] No stale `TODO D3D12` comments interpreted as Vulkan/Aurelian requirements.
- [ ] No static/global debug messenger device.
- [ ] No single global graphics device.
- [ ] No unsynchronized global fence completion state.
- [ ] No CPU fence wait path that can starve lower fence waiters.
- [ ] No lock held while slow Vulkan object allocation is performed.
- [ ] No LINQ or allocation-heavy descriptor-pool construction on hot paths.
- [ ] No shared mutable `NativeLayout` equivalent.
- [ ] No ignored subresource barrier parameter.
- [ ] No fixed 10-slot framebuffer key.
- [ ] No Stride `DrawInstanced` first-instance bug.
- [ ] No public API methods that throw `NotImplementedException` for normal renderer operations.
- [ ] No wasteful render-pass end/restart just to clear attachments when an in-pass clear is valid.
- [ ] No descriptor allocation per draw forever without a path to caching.
- [ ] No all-stages-shared bytecode constraint.
- [ ] No `VkPipelineCache.Null` as the long-term pipeline model.
- [ ] No unconditional depth bias.
- [ ] No unconditional attachment `loadOp = Load`.
- [ ] No silently ignored multisampling request.
- [ ] No commented-out fullscreen/device-lost implementation exposed as real behavior.
- [ ] No unimplemented platform surface branches for target platforms.
- [ ] No `vkQueueWaitIdle` where a scoped fence wait is sufficient.
- [ ] No debug-only `Debugger.Break()` behavior in production fallback code.
- [ ] No Android-specific workaround flags generalized into Aurelian surface contracts.
- [ ] No depth/stencil barriers that include color attachment stages.
- [ ] No deferred destruction keyed to the next fence value when the last submitted fence value is correct.
- [ ] No unimplemented depth-stencil upload exposed as supported.
- [ ] No unnecessary `vkCmdFillBuffer` zeroing.
- [ ] No structured/storage buffer access mapped as uniform read.
- [ ] No dynamic/staging map/unmap churn where persistent mapping is possible.
- [ ] No descriptor set layout stage flags broadened to all stages when artifact metadata can be precise.
- [ ] No immutable sampler array `NotImplementedException` exposed as supported API.
- [ ] No magic descriptor-type count without validation.
- [ ] No pessimistic `General` layout as a default for all common transitions.
- [ ] No resolve layout mapping copied without validating Vulkan 1.3 options.
- [ ] No resource hard reference back to an updating command list.
- [ ] No deprecated/stubbed debug marker path instead of `VK_EXT_debug_utils`.
- [ ] No Stride effect/material system.
- [ ] No service locator.
- [ ] No reflection-driven backend registration.
- [ ] No modification of `CodeReferences/*` or `vendor/Dominatus/*`.

## 12. Implementation milestone plan

Recommended milestones after A21:

| Milestone | Name | Intended scope |
| --- | --- | --- |
| A22 | `Aurelian.Graphics` project scaffold and Silk.NET package references | Create `src/Aurelian.Graphics` and `tests/Aurelian.Graphics.Tests`; add Silk.NET Vulkan/windowing packages; folder structure and package smoke only; no Vulkan implementation. |
| A23 | PlantContext + PlantRegistry M0 | Define `PlantId`, `PlantContext`, `PlantRegistry`, diagnostics DTOs, and single-plant fixed policy shape. No native Vulkan device yet unless needed for shape tests. |
| A24 | Vulkan instance/device init M0 | Create instance/device for plant 0, select physical device/queue family, require timeline semaphores, enable debug utils if available, cache memory properties, expose diagnostics. |
| A25 | Timeline fences and resource pool M0 | Per-plant frame/command/copy timeline semaphores; command buffer pool and descriptor pool recycler; compositor fence seam stub. |
| A26 | Buffer resource M0 | Plain buffer create plan; VMASharp/VMA decision; vertex/index/uniform/staging buffer creation; PlantId in state; simple upload path. |
| A27 | Texture resource M0 | Texture create plan; 2D image/view creation; layout tracker; initial upload/staging path; PlantId in state. |
| A28 | Barrier/layout tracker M0 | Aurelian barrier layouts to Vulkan mapping; per-subresource tracking; batch barrier accumulation; precise stage/access mapping. |
| A29 | Command list M0 | Per-plant command list lifecycle, recording, barriers, copies, buffer binds, simple render-pass boundaries, fence-tagged recycle. |
| A30 | Surface/swapchain M0 | Silk.NET.Windowing surface; swapchain for presentation plant 0; acquire/present semaphores; resize/out-of-date handling. |
| A31 | Compositor passthrough M0 | First-class compositor seam; single plant output to swapchain; diagnostics; no multi-GPU policy yet. |
| A32 | Pipeline/render pass M0 | Explicit render pass descriptors; per-stage shader modules; pipeline cache; descriptor set layout from plain descriptor data. |
| A33 | First triangle through plant/compositor path | Minimal render command path from Aurelian contracts to plant 0 command list to compositor passthrough to present. Temporary hardcoded shader artifact path allowed if documented. |
| A34 | Dominatus graphics policy facts M0 | Stage plant telemetry, upload budget, descriptor pressure, and compositor diagnostics as facts for Dominatus observation; still fixed plant 0 policy by default. |
| A35+ | Multi-GPU preparation/future | Cross-plant transfer contracts, external memory investigation, differential compositor experiments, policy feedback loops. |

This sequence slightly expands Claude's compact A1–A11 bring-up order into Aurelian's existing milestone numbering after A21 and keeps project/package scaffold separate from native Vulkan implementation.

## 13. Risks and open decisions

| Risk / open decision | Why it matters | Recommended handling |
| --- | --- | --- |
| VMASharp choice not verified | Vulkan memory allocation is correctness-heavy; wrong allocator choice can cause leaks, fragmentation, NativeAOT friction, or package churn. | Evaluate VMASharp/VMA binding before A26. If unsuitable, isolate allocator behind Aurelian-owned allocator contracts. |
| Silk.NET version/package shape | Package APIs and transitive dependencies may differ by version. | In A22, add packages and run package/build smoke only. Record exact versions and API surface notes. |
| Vulkan validation layer availability | Developer machines/CI may not have validation layers installed. | Make validation optional but diagnosable. Tests should not require validation layer presence. |
| NativeAOT compatibility | Aurelian policy prefers reflection-free/NativeAOT-compatible core behavior. | Keep Silk.NET isolated in `Aurelian.Graphics`; avoid reflection-based discovery; document any NativeAOT limitations. |
| Cross-platform surface creation | Window/surface APIs vary across Windows, X11, Wayland, macOS/MoltenVK. | Prefer `Silk.NET.Windowing`; keep raw platform branches out of M0 unless required. |
| Single project vs backend-specific project | A single project is simpler, but backend dependencies may become too broad later. | Start with `Aurelian.Graphics`; split to `Aurelian.Graphics.Vulkan` only when a second backend or dependency isolation requires it. |
| Compositor seam complexity | Early compositor seam can slow first triangle if overbuilt. | Implement passthrough seam with minimal diagnostics; defer differential/multi-GPU behavior. |
| Multi-GPU design scope creep | Full multi-GPU can consume many milestones before first visual output. | Carry `PlantId` and seams from day one, but implement only plant 0 until first triangle is working. |
| SDSL-V artifact to SPIR-V path not complete | Real pipeline creation depends on SPIR-V artifacts and shader reflection/stage metadata. | Allow A33 to use a temporary hardcoded shader artifact path if documented; align A32+ with SDSL-V artifact milestones. |
| First triangle shader path | A visual smoke may need shaders before full asset/shader bridge exists. | Use the smallest explicit temporary path; do not create a Stride-like effect system. |
| Swapchain format changes vs pipeline cache | Pipelines/render passes depend on attachment formats. | Include swapchain/render-pass format in pipeline cache keys and invalidation facts. |
| Timeline semaphore support | Aurelian sync model depends on timelines. | Fail device init clearly if unsupported; document fallback only if future hardware target requires it. |
| MoltenVK portability subset | macOS/MoltenVK requires portability subset handling. | During A24, enable portability extension if present and document capability tier. |
| Descriptor cache policy | Descriptor allocation per draw is correct but expensive. | Start simple in M0; add content-hash cache after correctness and diagnostics. |
| External memory for multi-GPU | Cross-device image sharing is platform-specific and complex. | Defer until after first triangle and compositor passthrough; keep cross-plant transfer concepts in contracts. |
| Dominatus integration timing | Integrating policy too early can block graphics bring-up. | M0 uses fixed plant 0 assignment; expose facts later in A34. |

## 14. Validation / command log

Commands run for A21 inspection and validation:

```bash
git status --short
find docs -maxdepth 3 -type f | sort
find CodeReferences -maxdepth 4 -type f | sort | head -n 300
find CodeReferences/Stride -maxdepth 5 -type f | sort | head -n 300
find CodeReferences/Prometheus -maxdepth 3 -type f | sort || true
find src -maxdepth 3 -type d | sort
find src -maxdepth 4 \( -name '*.csproj' -o -name '*.cs' \) | sort
sed -n '1,420p' docs/architecture/dependency-policy.md
sed -n '1,420p' docs/architecture/mvp-roadmap.md
sed -n '1,420p' docs/audits/0020-a20-world-to-render-snapshot-extraction-m0.md || true
sed -n '1,900p' docs/claude/aurelian-vulkan-intent-port-audit.md
rg -n "^##|^###|Do Not Carry Over|^- \*\*|Bring-Up" docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '79,260p' docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '115,245p' docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '154,210p' docs/claude/aurelian-vulkan-intent-port-audit.md
find CodeReferences/Stride/Stride.Graphics -type f | sort > /tmp/a21-stride-graphics-files.txt
wc -l /tmp/a21-stride-graphics-files.txt
head -n 400 /tmp/a21-stride-graphics-files.txt
rg -n "GraphicsDevice|CommandList|FenceHelper|ResourcePool|CommandBufferPool|HeapPool|PipelineState|SwapChain|Texture|Buffer|DescriptorSetLayout|BarrierLayout|GraphicsResource|Vulkan|NativeLayout|queueFamilyIndex|vkAllocateMemory|vkGetPhysicalDeviceMemoryProperties|DrawInstanced|NotImplementedException|FrameFence|CommandListFence|CopyFence|DescriptorPool|FramebufferKey|RenderPass|PipelineCache|Swapchain|Surface" CodeReferences/Stride/Stride.Graphics -g '*.cs' > /tmp/a21-stride-graphics-vulkan-search.txt || true
wc -l /tmp/a21-stride-graphics-vulkan-search.txt
head -n 1600 /tmp/a21-stride-graphics-vulkan-search.txt
find CodeReferences/Stride/Stride.Graphics -type f | grep -Ei 'GraphicsDevice.*Vulkan|CommandList.*Vulkan|PipelineState.*Vulkan|SwapChain.*Vulkan|Texture.*Vulkan|Buffer.*Vulkan|DescriptorSetLayout.*Vulkan|BarrierMapping|GraphicsResource.*Vulkan|ResourcePool|CommandBufferPool|HeapPool' | sort
sed -n '1,260p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs
sed -n '260,620p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs
sed -n '1,180p' CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs
sed -n '520,720p' CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs
sed -n '1030,1110p' CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs
sed -n '1,240p' CodeReferences/Stride/Stride.Graphics/Vulkan/PipelineState.Vulkan.cs
sed -n '1,240p' CodeReferences/Stride/Stride.Graphics/Vulkan/SwapChainGraphicsPresenter.Vulkan.cs
sed -n '1,220p' CodeReferences/Stride/Stride.Graphics/Vulkan/Texture.Vulkan.cs
sed -n '1,220p' CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs
sed -n '1,180p' CodeReferences/Stride/Stride.Graphics/Vulkan/DescriptorSetLayout.Vulkan.cs
sed -n '1,220p' CodeReferences/Stride/Stride.Graphics/Vulkan/BarrierMapping.Vulkan.cs
sed -n '1,160p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResource.Vulkan.cs
sed -n '1,180p' CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResourceBase.Vulkan.cs
rg -n "class ResourcePool|class CommandBufferPool|class HeapPool|OptionalLock|CreateObject|RecycleObject|GetObject|queueFamilyIndex|vkCreateCommandPool|DescriptorPoolSize|MaxDescriptorTypeCounts" CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs CodeReferences/Stride/Stride.Graphics -g '*.cs'
sed -n '1,900p' CodeReferences/Prometheus/reactor_vulkan_sgemm.c
rg -n "reactor|arena|selector|cache|generation|policy|budget|telemetry|fence|timeline|command|descriptor|pool|vulkan|VkDevice|VkQueue|VkFence|VkSemaphore|upload|staging|dispatch|submit|feedback|state|plant|controller" CodeReferences/Prometheus/reactor_vulkan_sgemm.c > /tmp/a21-prometheus-reactor-search.txt || true
wc -l /tmp/a21-prometheus-reactor-search.txt
head -n 1400 /tmp/a21-prometheus-reactor-search.txt
rg -n "Silk|Vortice|Vulkan|Graphics|Rendering|dependency|package|project|A21|A22|Aurelian.Rendering.Contracts|Null|WorldDataDocument|RenderSnapshot|RenderCommandPlan" docs/architecture/dependency-policy.md docs/architecture/mvp-roadmap.md docs/audits/0020-a20-world-to-render-snapshot-extraction-m0.md src -g '*.md' -g '*.csproj' -g '*.cs'
sed -n '1,130p' docs/architecture/dependency-policy.md
sed -n '1,160p' docs/architecture/mvp-roadmap.md
find src -maxdepth 3 -type d | sort
find src -maxdepth 4 \( -name '*.csproj' -o -name '*.cs' \) | sort | sed -n '1,220p'
test -f docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md
git status --short
```

Validation result:

- `test -f docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md` passed.
- `git status --short` showed only the new A21 audit report after creation.
- No build was run because A21 is docs-only and no docs tooling/build requirement was identified.

## 15. Next recommendation

```text
A22 — Aurelian.Graphics project scaffold and Silk.NET package references
```

A22 should:

- create `src/Aurelian.Graphics`;
- create `tests/Aurelian.Graphics.Tests`;
- add `Silk.NET.Vulkan` and `Silk.NET.Windowing` package references;
- optionally add `Silk.NET.Core` only if package/API shape requires it;
- add no Vulkan implementation beyond package/structure smoke;
- keep build/test green;
- keep `Aurelian.Rendering.Contracts` pure;
- leave `Aurelian.Rendering.Null` and the full headless path intact.
