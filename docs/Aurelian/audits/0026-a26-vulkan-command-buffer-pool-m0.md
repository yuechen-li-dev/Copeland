# A26 — Vulkan Command Buffer Pool M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Commanding/` command-buffer lifecycle types:
  - `VulkanCommandPool.cs`
  - `VulkanCommandBufferPool.cs`
  - `VulkanCommandBufferLease.cs`
  - `VulkanCommandBufferLifecycle.cs`
  - `VulkanCommandBufferStatus.cs`
  - `VulkanCommandBufferDiagnostic.cs`
  - `VulkanCommandBufferDiagnosticCodes.cs`
  - `VulkanCommandBufferOperationResult.cs`
  - `VulkanCommandBufferTelemetry.cs`
- Added tests:
  - `tests/Aurelian.Graphics.Tests/VulkanCommandBufferPoolM0Tests.cs`
- Updated docs:
  - `README.md`
  - `docs/architecture/mvp-roadmap.md`
  - `docs/architecture/dependency-policy.md`
  - this report.

`CodeReferences/*` and `vendor/Dominatus/*` were not modified.

## 2. Task scope

A26 implements Vulkan command pool / primary command buffer lifecycle only. It specializes the A25 timeline-fence and fence-tagged pool foundation into a per-plant command-buffer recycler.

Included:

- one native `VkCommandPool` per `AurelianVulkanPlant` command-buffer pool;
- primary command buffer allocation from that pool;
- command buffer lease lifecycle: ready, recording, executable, retired, disposed;
- `Reset()`, `Begin()`, and `End()` operations with diagnostics;
- fence-tagged retirement and reuse through `FenceTaggedResourcePool<T>`;
- command-buffer telemetry over A25 pool telemetry;
- tests that skip cleanly when Vulkan is unavailable.

Explicitly deferred:

- buffers;
- textures;
- render passes;
- framebuffers;
- pipelines;
- draw commands;
- copy/update commands;
- queue submit/execution;
- windows;
- surfaces;
- swapchains;
- renderer/backend integration;
- VMA/VMASharp;
- Vortice;
- Dominatus graphics policy integration.

## 3. Reference material read

Read before implementation:

- `docs/audits/0025-a25-timeline-fences-resource-pool-m0.md`
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`
- `CodeReferences/Prometheus/reactor_vulkan_sgemm.c`

Searches were run for Stride command-buffer-pool, command-list, queue-family, fence, resource-pool, and recycle terms, and for Prometheus pool/cache/generation/telemetry terms.

Stride's useful intent:

- command buffers are pooled rather than allocated every command list;
- pooled objects are associated with fence values and reused only after GPU completion;
- command buffers are reset before reuse;
- command list recording follows reset/begin/end lifecycle.

Stride debt avoided:

- no `queueFamilyIndex = 0` hardcoding;
- no command-buffer pool shared across plants;
- no broad command-list object carrying render-pass, descriptor, staging, framebuffer, pipeline, and submit behavior in this milestone;
- no Vortice adoption;
- no literal copy of Stride's optional locking/resource pool implementation.

Prometheus lessons applied:

- keep explicit counters and high-water telemetry;
- keep ownership/generation concepts visible early;
- prefer small specialized lifecycle boundaries over hidden global state;
- make reuse decisions evidence-driven and inspectable.

## 4. Command pool model

`VulkanCommandPool` owns a native Vulkan command pool for exactly one plant. It stores the plant id and queue family index selected during A24 device initialization.

The command pool is created with `ResetCommandBufferBit` so individual primary command buffers can be reset before reuse. The queue family index comes from `AurelianVulkanPlant.QueueFamilyIndex`; A26 does not hardcode family zero.

Disposal is idempotent. Destroying the command pool implicitly releases command buffers allocated from it, so A26 does not maintain separate Vulkan command-buffer frees.

## 5. Command buffer lease/lifecycle

`VulkanCommandBufferLease` wraps one primary `VkCommandBuffer` allocated from the plant command pool.

Lifecycle:

```text
Ready -> Begin() -> Recording -> End() -> Executable -> Reset() -> Ready
Ready/Executable -> Retire() -> Retired -> Reset on reuse -> Ready
Any live state -> pool Dispose() -> Disposed
```

Rules enforced in M0:

- `Begin()` succeeds only from `Ready`;
- `End()` succeeds only from `Recording`;
- `Reset()` returns a non-disposed command buffer to `Ready`;
- disposed leases report diagnostics instead of performing lifecycle work;
- no commands are recorded between begin and end.

## 6. Fence-tagged reuse behavior

`VulkanCommandBufferPool` combines one native `VulkanCommandPool` with one managed `FenceTaggedResourcePool<VulkanCommandBufferLease>`.

Behavior:

- `Rent(completedFenceValue)` asks the A25 pool for a ready lease.
- If the FIFO retired lease at the head has a retire fence value less than or equal to `completedFenceValue`, it is dequeued, reset, and returned.
- If the head is still in the future, a new primary command buffer lease is allocated.
- `Retire(lease, retireFenceValue)` marks the lease retired and enqueues it with the retire fence value.
- Plant ownership is checked on retire so a lease cannot be returned to a different plant's pool.

No queue submit exists in A26. Tests use synthetic completed fence values for reuse behavior.

## 7. Telemetry

`VulkanCommandBufferTelemetry` exposes:

- plant id;
- generation;
- created count;
- rented count;
- reused count;
- retired count;
- queued count;
- high-water queued count.

The counts are derived from A25 `ResourcePoolTelemetry`, keeping resource-pool accounting behavior consistent across future Vulkan pool types.

## 8. Tests added

Added `tests/Aurelian.Graphics.Tests/VulkanCommandBufferPoolM0Tests.cs` with coverage for:

- unavailable-safe Vulkan initialization pattern;
- pool creation for plant zero;
- ready lease rental;
- begin/end/reset lifecycle;
- future-fence retirement not being reused early;
- ready-fence FIFO reuse;
- idempotent disposal;
- selected plant queue-family use instead of hardcoded zero.

The tests create a Vulkan plant with validation disabled and return early with diagnostic assertions when Vulkan is unavailable.

## 9. Boundary checks

Boundary checks were run for forbidden concepts across `src/Aurelian.Graphics` and `tests/Aurelian.Graphics.Tests`:

- swapchain/surface/window terms;
- render-pass/framebuffer/pipeline/draw terms;
- buffer/image/texture terms;
- VMA/Vortice terms;
- forbidden project/domain references;
- service-locator/singleton/reflection terms;
- hardcoded queue-family-zero assignments;
- project references.

Observed boundary grep matches were benign:

- `StructureType.*` Vulkan struct initialization in existing and new Vulkan code;
- `GetType().Name` in existing unavailable-safe Vulkan diagnostics;
- `PhysicalDeviceType` in existing device selection;
- a test reflection assertion in existing plant-registry tests;
- project references remained limited to `Aurelian.Graphics -> Aurelian.Rendering.Contracts` and tests -> graphics.

No forbidden CodeReferences/vendor modifications were made.

## 10. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|RenderPass|Framebuffer|Pipeline|Draw|vkCmdDraw|vkCreateBuffer|vkCreateImage|Texture|VMASharp|Vma|Vortice|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "queueFamilyIndex\s*=\s*0|QueueFamilyIndex\s*=\s*0" src/Aurelian.Graphics -g '*.cs' || true
rg -n "ProjectReference" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.csproj'
```

Results:

- build passed;
- test suite passed;
- hardcoded queue-family-zero check returned no matches;
- project reference check returned only the allowed graphics/contracts and tests/graphics references;
- broad boundary grep returned only benign matches documented above.

## 11. Deferred features

Deferred beyond A26:

- command submission and timeline semaphore signaling;
- command list/backend execution abstractions;
- buffers and memory allocation;
- textures/images;
- upload/copy/update commands;
- descriptor pools/sets;
- barriers/resource state tracking;
- render pass/framebuffer/pipeline/draw support;
- swapchain/window/surface/presentation;
- VMA/VMASharp decision or integration;
- Vortice;
- Dominatus graphics policy integration;
- multi-plant cross-device transfer behavior.

## 12. Next recommendation

A27 — Buffer resource M0

A27 should:

- add buffer create plans;
- add plant-id resource state;
- create simple Vulkan buffers;
- avoid VMA initially or decide/evaluate VMA before implementation;
- avoid textures;
- avoid swapchain/window/render pass;
- continue to keep tests unavailable-safe when Vulkan is unavailable.
