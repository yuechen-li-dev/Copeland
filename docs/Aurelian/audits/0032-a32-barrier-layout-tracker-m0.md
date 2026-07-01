# A32 — Barrier/layout tracker M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Resources/Barriers/` for Aurelian-owned barrier/layout vocabulary, Vulkan mapping facts, pure barrier plans/batches, per-subresource layout tracking, diagnostics, statuses, and buffer transition planning.
- Added pure unit tests in `tests/Aurelian.Graphics.Tests/` for mapping behavior, tracker behavior, batching/no-op behavior, invalid subresource diagnostics, and buffer access/stage planning.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with the A32 boundary and deferrals.

## 2. Task scope

A32 is intentionally a planning milestone. It adds the vocabulary and deterministic state transitions needed before textures, render passes, pipelines, and draw paths, but it does not record Vulkan barriers into command buffers.

Included:

- Aurelian-level resource layouts.
- Aurelian-level access and stage flags.
- Silk.NET Vulkan image layout/access/stage mapping facts.
- Pure image barrier plan and batch DTOs.
- A per-mip/per-array-layer layout tracker.
- Pure buffer access transition plans.
- Tests that require no Vulkan runtime.

Excluded:

- textures/images and image creation;
- render passes, framebuffers, pipelines, descriptors, and draw commands;
- swapchains, windows, surfaces, presentation integration;
- VMA/VMASharp, Vortice, or CodeReferences/vendor coupling;
- `vkCmdPipelineBarrier` command emission.

## 3. Reference material read

Read before implementation:

- `docs/audits/0031-a31-staging-buffer-device-local-upload-m0.md` for the current upload/copy boundary.
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md` for the Vulkan intent-port architecture and do-not-carry-over list.
- `docs/claude/aurelian-vulkan-intent-port-audit.md` barrier/resource sections.
- Stride reference search results in `/tmp/a32-stride-barrier-search.txt` plus `BarrierMapping.Vulkan.cs`, `GraphicsResourceBase.Vulkan.cs`, and `CommandList.Vulkan.cs` as reference-only material.
- Prometheus search results in `/tmp/a32-prometheus-barrier-search.txt` for cache/generation/validity lessons.

### Stride mapping intent

Stride's mapping intent is useful: convert an engine-facing barrier layout enum to the three Vulkan facts required for image barriers (`VkImageLayout`, access mask, and pipeline stage mask), and let command-list code consume those facts rather than duplicating layout knowledge at every transition call site.

### Stride pitfalls to avoid

A32 avoids copying these Stride pitfalls:

- No shared mutable `NativeLayout` equivalent as the authoritative layout for all command buffers.
- No ignored subresource parameter; tracking is per `(mip, arrayLayer)` from M0.
- No shader-resource mapping that misses vertex shader usage.
- No defaulting everything to `General`.
- No one-barrier-at-a-time command emission in this milestone; batch DTOs exist before emission.

### Prometheus generation/cache lessons

Prometheus reinforces that cached state must carry explicit validity and invalidation information. A32 applies the lesson by keeping state transitions explicit, diagnosable, and deterministic: accepted transitions mutate the tracker, rejected transitions do not, and no-op transitions are represented as no-ops rather than silent mutations. Future command emission can add generation counters if command-buffer-local caches need stronger submitted-vs-recording separation.

### What is deferred

Command-buffer-local barrier accumulation, actual Vulkan `ImageMemoryBarrier`/`BufferMemoryBarrier` emission, queue-family ownership transfer details, texture creation, render-pass/pipeline integration, descriptor binding, swapchain/presentation transitions, and cross-plant copy/compositor policy are deferred.

## 4. Barrier layout/access/stage model

A32 defines `VulkanResourceLayout` as the Aurelian-owned vocabulary for resource layouts:

- `Undefined`, `General`;
- `TransferSource`, `TransferDestination`;
- `ShaderResourceVertex`, `ShaderResourceFragment`, `ShaderResourceCompute`, `ShaderResourceAll`;
- `StorageReadWrite`;
- `ColorAttachment`, `DepthStencilAttachment`;
- `Present`;
- `CrossPlantTransferSource`, `CrossPlantTransferDestination`.

`VulkanResourceAccess` and `VulkanBarrierStage` are Aurelian-owned flags that preserve intent before lowering to Silk.NET Vulkan flags. They deliberately separate vertex/fragment/compute shader resource stages so future resource binding does not inherit Stride's too-narrow shader-resource mapping bug.

## 5. Vulkan mapping behavior

`VulkanBarrierMappings.Map` returns `VulkanBarrierPlanResult` with a `VulkanBarrierMapping` and optional diagnostics.

M0 mappings:

- `Undefined` -> `ImageLayout.Undefined`, no access, `TopOfPipeBit` as deterministic old-state stage.
- `General` -> `ImageLayout.General`, shader read/write, `AllCommandsBit`. This is intentionally broad and documented as not a default layout.
- Transfer layouts -> transfer optimal image layouts, transfer access, transfer stage.
- Stage-specific shader resources -> `ShaderReadOnlyOptimal`, shader read, and the requested shader stage.
- `ShaderResourceAll` -> `ShaderReadOnlyOptimal`, shader read, `AllGraphicsBit | ComputeShaderBit`.
- `StorageReadWrite` -> `General`, shader read/write, compute + fragment M0 stages.
- `ColorAttachment` -> color attachment layout/access and color attachment output stage.
- `DepthStencilAttachment` -> depth/stencil layout/access and early+late fragment tests, with no color output stage.
- `Present` -> `PresentSrcKhr`, present read intent, bottom-of-pipe M0 stage.
- Cross-plant transfer layouts -> transfer source/destination facts plus `AGBARR1003` info diagnostics because queue-family ownership transfer is deferred.

## 6. Layout tracker behavior

`VulkanLayoutTracker` stores one layout per subresource in a flat array:

```text
index = mip * arrayLayers + arrayLayer
```

Behavior:

- constructor rejects zero mip-level or array-layer counts;
- `Get` returns a tracked subresource layout and throws for invalid caller indices;
- `Transition` rejects invalid subresources with `AGBARR1002` diagnostics;
- same-layout transitions return `NoOp` and no plan;
- accepted transitions produce a `VulkanBarrierPlan` and update only the target subresource;
- rejected transitions do not mutate state;
- `TransitionAll` walks every subresource and batches only subresources that actually changed.

## 7. Buffer transition planning

Buffers do not use image layouts, so A32 adds pure access/stage transition plans only:

- `HostWriteToTransferRead` supports staging buffers after CPU writes and before copy reads.
- `TransferWriteToVertexRead` supports uploaded vertex buffers before future bind/draw use.
- `TransferWriteToShaderRead` supports uploaded buffers before future shader reads.

No Vulkan `BufferMemoryBarrier` objects are created yet.

## 8. Tests added

Added:

- `VulkanBarrierMappingM0Tests`
- `VulkanLayoutTrackerM0Tests`
- `VulkanBufferTransitionPlannerM0Tests`

The tests are pure managed tests and require no Vulkan runtime availability.

## 9. Boundary checks

Boundary checks were run against `src/Aurelian.Graphics` and `tests/Aurelian.Graphics.Tests` for banned dependencies/features and command emission in the new barrier namespace.

## 10. Validation results

Validated with:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug`
- boundary `rg` checks for prohibited dependencies/features and barrier command emission.

## 11. Deferred features

Deferred:

- Vulkan command emission (`vkCmdPipelineBarrier`, image/buffer memory barrier structs, queue-family ownership transfer indices);
- textures/images and image views;
- render passes, framebuffers, pipelines, descriptors, and draw commands;
- swapchain/window/surface/presentation integration;
- upload-ring batching and async staging retirement;
- cross-plant compositor transfer implementation;
- VMA/VMASharp and Vortice.

## 12. Next recommendation

Recommended next milestone: **A33 — Texture resource M0**.

Rationale: A32 now provides the per-subresource layout tracker and Aurelian-owned layout vocabulary that texture creation needs. Texture resource M0 can attach a tracker to each texture/image without inventing synchronization semantics inside the texture milestone. Barrier command emission can follow once images/textures exist and provide real motivating cases for image range lowering.
