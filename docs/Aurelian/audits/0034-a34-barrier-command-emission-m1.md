# A34 — Barrier command emission M1

## 1. Files changed

- Added `VulkanBarrierCommandEmitter` plus emission status/result/diagnostic-code records under `src/Aurelian.Graphics/Vulkan/Resources/Barriers/`.
- Added `VulkanTextureBarrierEmission` and `VulkanBufferBarrierEmission` request records so native resources are paired with pure plans only at the Vulkan backend edge.
- Added `tests/Aurelian.Graphics.Tests/VulkanBarrierCommandEmissionM1Tests.cs` for empty-list no-ops, command-buffer recording emission, combined buffer/image emission, and wrong-plant rejection.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with the A34 boundary and deferrals.

## 2. Task scope

A34 is barrier command emission only. It bridges A32's pure barrier/layout plans into actual Vulkan command-buffer recording without adding upload, draw, presentation, or renderer infrastructure.

Included:

- buffer memory barrier emission;
- color image memory barrier emission for layout transitions;
- batched `vkCmdPipelineBarrier` for combined buffer and image barrier requests;
- command-buffer recording-state validation through `VulkanCommandBufferLease`;
- plant ownership validation for resources and command buffers;
- optional-safe tests that return cleanly when Vulkan is unavailable.

Excluded:

- texture upload and `vkCmdCopyBufferToImage`;
- render passes, framebuffers, pipelines, descriptors, and draw commands;
- swapchains, windows, surfaces, or presentation;
- VMA/VMASharp, Vortice, service locators, reflection, or global graphics state;
- CodeReferences/vendor changes;
- cross-plant queue-family ownership transfer emission.

## 3. Reference material read

Read before implementation:

- `docs/audits/0032-a32-barrier-layout-tracker-m0.md`.
- `docs/audits/0033-a33-vulkan-texture2d-resource-m0.md`.
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`.
- `docs/claude/aurelian-vulkan-intent-port-audit.md` barrier, command-list, layout-tracker, and command emission sections.
- Stride reference search output in `/tmp/a34-stride-barrier-emission-search.txt`.
- `CodeReferences/Stride/Stride.Graphics/Vulkan/BarrierMapping.Vulkan.cs`.
- `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`.

### Stride intent borrowed

A34 borrows the useful Stride intent that engine-facing barrier layouts should lower to native Vulkan layout/access/stage facts immediately before command recording. Aurelian keeps that in its own mapping and emitter types instead of copying Stride command-list policy.

### Stride pitfalls avoided

- No shared mutable `NativeLayout` equivalent is introduced. A32's `VulkanLayoutTracker` remains explicit per texture and per subresource.
- The subresource range from `VulkanBarrierPlan` is honored for mip level and array layer range; A34 does not ignore subresources.
- Texture and buffer barriers are batched into one `vkCmdPipelineBarrier` when the combined API is used instead of forcing one command per transition.
- Render-pass cleanup assumptions are deferred because Aurelian has no render pass model yet.

## 4. Emission API

A34 adds these APIs:

- `EmitTextureBarriers(AurelianVulkanPlant, VulkanCommandBufferLease, IReadOnlyList<VulkanTextureBarrierEmission>)`.
- `EmitBufferBarriers(AurelianVulkanPlant, VulkanCommandBufferLease, IReadOnlyList<VulkanBufferBarrierEmission>)`.
- `Emit(AurelianVulkanPlant, VulkanCommandBufferLease, IReadOnlyList<VulkanBufferBarrierEmission>, IReadOnlyList<VulkanTextureBarrierEmission>)` for a combined batched command.
- `Emit(AurelianVulkanPlant, VulkanCommandBufferLease, VulkanBarrierBatch)` remains intentionally rejecting for non-empty pure batches because pure image plans do not and should not carry native image handles.

Result reporting uses `VulkanBarrierEmissionResult` with status, emitted image count, emitted buffer count, and `VulkanBarrierDiagnostic` records. Empty barrier lists are successful no-ops with `AGBARR2001` diagnostics.

## 5. Image barrier emission

Texture emission converts each `VulkanTextureBarrierEmission` to `ImageMemoryBarrier`:

- old/new layouts from `VulkanBarrierMapping.ImageLayout`;
- source/destination access masks from `VulkanBarrierMapping.AccessMask`;
- source/destination stages ORed across all texture and buffer plans;
- native image from `AurelianVulkanTexture`;
- subresource range from `VulkanBarrierPlan`;
- color aspect only for M1;
- queue family indices set to ignored for same-queue-family ownership.

Cross-plant transfer layouts are rejected because queue-family ownership transfer details are deferred.

## 6. Buffer barrier emission

Buffer emission converts each `VulkanBufferBarrierEmission` to `BufferMemoryBarrier`:

- old/new access values lower to Vulkan access flags;
- old/new stages lower to Vulkan pipeline stage flags;
- native buffer from `AurelianVulkanBuffer`;
- offset and size from `VulkanBufferTransitionPlan`;
- queue family indices set to ignored.

The combined API emits buffer and image barriers in a single `vkCmdPipelineBarrier` call.

## 7. Layout tracker interaction

A34 deliberately does not mutate `VulkanLayoutTracker` during emission. The A32 tracker remains responsible for moving planned state when a transition plan is accepted. The emitter consumes already-created plans and records Vulkan commands. If command emission fails after planning, rollback or submitted-layout reconciliation is deferred to a later milestone rather than patched into M1.

## 8. Tests added

`tests/Aurelian.Graphics.Tests/VulkanBarrierCommandEmissionM1Tests.cs` adds:

- empty texture barrier list returns no-op;
- empty buffer barrier list returns no-op;
- texture barrier records into a begun command buffer when Vulkan is available;
- buffer barrier records into a begun command buffer when Vulkan is available;
- combined buffer/image emission succeeds when Vulkan is available;
- wrong-plant texture barrier is rejected;
- wrong-plant buffer barrier is rejected.

The Vulkan-dependent tests follow the existing optional-safe pattern: if Vulkan initialization or resource creation is unavailable, the test asserts diagnostics and returns cleanly.

## 9. Boundary checks

Boundary checks were run against source and tests to confirm that A34 did not introduce forbidden scope:

- no VMA/VMASharp or Vortice;
- no swapchain/window/surface;
- no render pass/framebuffer/pipeline/draw;
- no `vkCmdCopyBufferToImage` texture upload;
- no dependencies on world/assets/shaders/null rendering/vendor/reference code from graphics;
- barrier command emission appears in the barrier emitter and A34 tests;
- raw memory calls remain isolated to allocator backend code.

## 10. Validation results

Validation commands run:

- `dotnet build Aurelian.slnx -c Debug` — passed.
- `dotnet test Aurelian.slnx -c Debug` — passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` — passed.
- Scope boundary `rg` checks — passed with expected pre-existing matches for generic Silk.NET `SType`/`Type` identifiers, Vulkan initializer exception type reporting, and raw allocation/mapping calls isolated to `RawVulkanMemoryAllocator`.

## 11. Deferred features

Deferred beyond A34:

- texture upload;
- `vkCmdCopyBufferToImage`;
- image aspect selection for depth/stencil;
- cross-plant queue-family ownership transfers;
- render-pass-aware cleanup or in-pass barrier policy;
- descriptor sets;
- render passes, pipelines, framebuffers, and draws;
- swapchain/window/surface integration;
- submitted-layout versus recording-layout reconciliation and rollback after failed emission;
- VMA/VMASharp backend.

## 12. Next recommendation

A35 — Texture upload M0.

Texture creation, command buffers, staging-buffer upload foundations, layout tracking, and barrier command emission now exist. The next convergent step is to add a narrow texture upload path that records layout transition(s), `vkCmdCopyBufferToImage`, and final shader-resource transition while preserving the same optional-safe Vulkan test pattern.
