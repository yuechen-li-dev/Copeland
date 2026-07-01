# A25 — Timeline Fences and Resource Pool M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Sync/` timeline fence types:
  - `VulkanTimelineFence.cs`
  - `VulkanFenceBundle.cs`
  - `VulkanFenceStatus.cs`
  - `VulkanFenceDiagnostic.cs`
  - `VulkanFenceDiagnosticCodes.cs`
  - `VulkanFenceOperationResult.cs`
- Added `src/Aurelian.Graphics/Vulkan/Resources/` managed pool types:
  - `FenceTaggedResourcePool.cs`
  - `FenceTaggedResource.cs`
  - `ResourcePoolStatus.cs`
  - `ResourcePoolDiagnostic.cs`
  - `ResourcePoolDiagnosticCodes.cs`
  - `ResourcePoolTelemetry.cs`
- Added tests:
  - `tests/Aurelian.Graphics.Tests/VulkanTimelineFenceM0Tests.cs`
  - `tests/Aurelian.Graphics.Tests/FenceTaggedResourcePoolM0Tests.cs`
- Updated docs:
  - `README.md`
  - `docs/architecture/mvp-roadmap.md`
  - `docs/architecture/dependency-policy.md`
  - this report.

`CodeReferences/*` and `vendor/Dominatus/*` were not modified.

## 2. Task scope

A25 implements synchronization and pool foundations only. It adds optional per-plant Vulkan timeline semaphore wrappers and a pure managed fence-tagged resource pool model.

A25 deliberately does not create:

- windows;
- surfaces;
- swapchains;
- command buffers;
- buffers;
- textures;
- Vulkan resources beyond timeline semaphores;
- rendering paths;
- VMA/VMASharp or Vortice integration;
- service locators or global graphics devices.

The A24 Vulkan plant initialization contract remains stable. Fence bundles are created explicitly through `VulkanFenceBundle.Create(plant)` rather than automatically during device initialization.

## 3. Reference material read

Read before implementation:

- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `docs/audits/0024-a24-vulkan-instance-device-init-m0.md`
- `docs/audits/0024b-a24b-dominatus-vendor-expansion.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Direct3D12/GraphicsDevice.Direct3D12.Pools.cs`
- `CodeReferences/Prometheus/reactor_vulkan_sgemm.c`

Searches were run for fence, timeline semaphore, resource-pool, command-buffer-pool, heap-pool, arena, selector, generation, telemetry, and budget terms.

## 4. Timeline fence model

`VulkanTimelineFence` owns one Vulkan timeline semaphore for one plant. It records:

- `PlantId`;
- debug-readable `Name`;
- native `Semaphore` handle;
- `NextValue` for future signal-value allocation;
- `LastKnownCompletedValue`.

Operations:

- `Create(plant, name)` creates a timeline semaphore with initial value `0`.
- `AllocateSignalValue()` increments local state and returns a future signal value. It does not submit GPU work.
- `QueryCompletedValue()` calls `vkGetSemaphoreCounterValue` and updates the last-known completed value.
- `WaitForValue(value, timeoutNanoseconds)` calls `vkWaitSemaphores` for a single requested value.
- `Dispose()` safely destroys the semaphore and tolerates repeated dispose calls.

Mutable state is protected by a private lock. This intentionally avoids Stride's unsynchronized `LastCompletedFence` pattern. A25 does not implement a global wait queue; waits are direct per-value Vulkan waits.

## 5. Fence bundle behavior

`VulkanFenceBundle` is an explicit disposable bundle containing:

- `FrameFence`;
- `CommandListFence`;
- `CopyFence`.

The bundle factory names fences with the plant id and disposes any earlier-created fences if a later fence creation fails. Bundle disposal destroys copy, command-list, then frame fence. Device initialization does not create the bundle automatically, so A24 plant creation remains unchanged and tests can create/dispose bundles only when Vulkan is available.

## 6. Managed resource pool model

`FenceTaggedResourcePool<T>` is pure managed and does not depend on Vulkan handles.

Behavior:

- `Retire(resource, retireFenceValue)` enqueues a resource with the fence value that must complete before reuse.
- `Rent(completedFenceValue)` checks only the FIFO head. If the head's retire fence is complete, the pool dequeues, resets, and returns that resource. Otherwise it creates a new resource.
- The pool does not hold its queue lock while invoking `create`.
- Reuse is deterministic FIFO.
- Telemetry records generation, created, rented, reused, retired, current queued, and high-water queued counts.

This model can later back command buffer pools, descriptor heap pools, upload arenas, and buffer/texture lifetime staging without importing those concepts in A25.

## 7. Prometheus/Stride lessons applied

Stride intent retained:

- timeline semaphore fences model GPU progress as monotonically increasing fence values;
- per-plant frame, command-list, and copy fence concepts are the right first split;
- fence-tagged resources should be reused only after their retirement fence completes;
- FIFO reuse is simple and deterministic.

Stride debt avoided:

- no unsynchronized completed-fence field;
- no hardcoded queue family index;
- no command buffer pool in A25;
- no descriptor heap pool hot-path LINQ or descriptor sizing policy;
- no create-under-lock behavior in the managed pool;
- no service-locator or global graphics device.

Prometheus patterns applied lightly:

- telemetry includes high-water and generation-style counters;
- resource reuse state is explicit and observable;
- adaptive selector/cache/budget policy is deferred rather than overbuilt in the first pool.

## 8. Tests added

Timeline fence tests:

- `VulkanTimelineFence_CreateBundle_WhenVulkanUnavailable_SkipsCleanly`
- `VulkanTimelineFence_CreateBundle_WhenPlantCreated_CreatesThreeFences`
- `VulkanTimelineFence_AllocateSignalValue_IncrementsMonotonically`
- `VulkanTimelineFence_QueryCompletedValue_DoesNotThrowWhenCreated`
- `VulkanTimelineFence_Dispose_IsIdempotent`

Resource pool tests:

- `FenceTaggedResourcePool_Rent_CreatesWhenNoRetiredResourceIsReady`
- `FenceTaggedResourcePool_Rent_ReusesReadyResourceInFifoOrder`
- `FenceTaggedResourcePool_Rent_DoesNotReuseFutureFenceResource`
- `FenceTaggedResourcePool_Retire_UpdatesTelemetry`
- `FenceTaggedResourcePool_Rent_DoesNotHoldLockDuringCreate`

The Vulkan tests use the A24 plant initializer and return after asserting diagnostics when Vulkan is unavailable.

## 9. Boundary checks

Boundary grep was run against `src/Aurelian.Graphics` and `tests/Aurelian.Graphics.Tests` for forbidden surface/swapchain/window/resource/command-buffer/VMA/Vortice/vendor/service-locator patterns.

Benign expected matches include:

- `CommandListFence` names because A25 explicitly adds the command-list fence concept, not command buffers.
- test method names and documentation-style names containing `CreateBundle` or fence words.
- `Silk.NET.Windowing` in the A22 project file package reference.
- `Buffer`/`Texture` words where boundary command patterns match documentation or negative assertions, not resource creation APIs.
- `ProjectReference` lines for the allowed `Aurelian.Rendering.Contracts` and graphics-test project references.

No CodeReferences or vendor files were changed.

## 10. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet format src/Aurelian.Graphics/Aurelian.Graphics.csproj --verify-no-changes --no-restore && dotnet format tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj --verify-no-changes --no-restore
rg -n "SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|CommandBuffer|vkAllocateCommandBuffers|Buffer|Texture|VMASharp|Vma|Vortice|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "LastCompletedFence|WaitForFenceCPUInternal|queueFamilyIndex\s*=\s*0|QueueFamilyIndex\s*=\s*0" src/Aurelian.Graphics -g '*.cs' || true
rg -n "ProjectReference" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.csproj'
git status --short
```

Build and tests passed in Debug in the current environment. Formatting verification for the changed graphics project and tests project passed. Full-solution `dotnet format Aurelian.slnx --verify-no-changes --no-restore` was also attempted and reported pre-existing whitespace issues in unrelated shader/assets/vendor files, so it was not used as the A25 formatting gate.

## 11. Deferred features

Deferred to later milestones:

- queue submit integration;
- command buffer creation and pooling;
- buffer resource creation;
- texture/image creation;
- descriptor pools/heaps;
- upload arenas;
- swapchain, surface, and windowing;
- rendering;
- Vulkan debug-object names through `VK_EXT_debug_utils`;
- cross-plant compositor fence;
- Prometheus-style selector/cache and budget controllers;
- Dominatus blackboard/actuator integration;
- VMA/VMASharp;
- Vortice.

## 12. Next recommendation

A26 should be:

```text
A26 — Buffer resource M0
```

A25 reveals that timeline values and a pure managed retirement pool are sufficient foundation to introduce the smallest explicit buffer ownership model next, while command-buffer pooling can still wait until submit/recording lifecycles are in scope.
