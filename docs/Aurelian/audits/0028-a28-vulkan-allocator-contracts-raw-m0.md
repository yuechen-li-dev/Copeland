# A28 — Vulkan allocator contracts + raw allocator M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Resources/Allocation/` with allocator contracts, allocation handle, telemetry, diagnostics, resource state, and the raw Vulkan allocator backend.
- Added `tests/Aurelian.Graphics.Tests/VulkanMemoryAllocatorM0Tests.cs` for unavailable-safe allocator validation.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` to record the A28 boundary and A29 recommendation.

## 2. Task scope

A28 creates Aurelian-owned allocation contracts and a narrow raw Vulkan backend before any buffer or texture resource implementation. It does not add buffers, textures, upload rings, VMA/VMASharp, Vortice, surfaces, swapchains, render passes, pipelines, draw work, or command recording for resources.

## 3. Allocation contract model

The allocation model is centered on `IVulkanMemoryAllocator`. Future Vulkan resource code must ask the allocator for memory through `VulkanAllocationRequest` and consume `VulkanAllocationResult`/`VulkanMemoryAllocation`, rather than directly calling Vulkan allocation APIs or depending on backend-specific VMA types.

The contract includes:

- plant ownership through `PlantId`;
- backend identity through `VulkanAllocationBackendKind`;
- memory intent through `VulkanMemoryUsage`;
- request validation diagnostics with stable `AGM` codes;
- a small `GpuResourceState` placeholder for plant/resource retirement facts;
- pure telemetry records suitable for tests and future Dominatus observation.

## 4. Raw Vulkan allocator backend

`RawVulkanMemoryAllocator` is intentionally M0 fallback plumbing. It owns one plant/device, caches physical device memory properties in the constructor, validates allocation requests, selects a compatible memory type, calls `vkAllocateMemory`, and returns a `VulkanMemoryAllocation` wrapper whose disposal calls back into the allocator for `vkFreeMemory`.

This backend allocates one `VkDeviceMemory` object per successful request. That is acceptable only for smoke/prototype work and first buffer bring-up; it is not the long-term resource allocation model. The important A28 outcome is isolation: raw Vulkan allocation/free mechanics are centralized in the backend and do not spread into future buffer/texture layers.

## 5. Memory type selection

The raw backend maps M0 usage intent to Vulkan memory property requirements:

- `GpuOnly` -> `DeviceLocal`;
- `CpuToGpu` -> `HostVisible | HostCoherent`;
- `GpuToCpu` -> `HostVisible | HostCoherent`;
- `Unknown` -> no extra required property flags.

Selection scans cached `PhysicalDeviceMemoryProperties` and accepts the first memory type whose bit is present in the request mask and whose property flags satisfy the usage requirement. Tests can use `uint.MaxValue` as a synthetic compatibility mask because buffers/images are intentionally deferred.

## 6. Telemetry

Allocator telemetry is a pure immutable record containing allocation count, free count, live allocation count, requested bytes, live bytes, and high-water live bytes. Counters are updated under a lock in the raw backend so validation and later resource code can observe allocator behavior without inspecting Vulkan handles.

## 7. Tests added

The new test file covers:

- unavailable-safe allocator creation;
- zero-size rejection;
- zero memory-type-bit rejection;
- plant mismatch rejection;
- small CPU-visible allocation success or clean `NoSuitableMemoryType` rejection;
- idempotent allocation disposal;
- telemetry allocation/free accounting;
- allocation after allocator disposal rejection;
- public contract names avoiding VMA exposure.

Every Vulkan-dependent test first creates a plant through `VulkanPlantInitializer`. If Vulkan is unavailable or rejected on the host, the test asserts diagnostics and returns cleanly.

## 8. Boundary checks

A28 keeps allocator work inside `Aurelian.Graphics` and its tests. It does not modify `CodeReferences` or `vendor/Dominatus`. The reference inspection reinforced three design points:

- raw allocator is M0 only because per-resource Vulkan allocation is a known scalability hazard and should be replaced by suballocation/VMA/custom arenas behind the same contract;
- Stride pitfalls avoided include spreading memory allocation through buffer/texture code, per-resource memory-property decisions, upload/staging lifetime coupling, and hidden collection/fence lifetime behavior;
- Prometheus-style seams preserved include explicit telemetry, capacity/live/high-water facts, policy-controlled grow/shrink decisions, generation/retirement concepts, and keeping Dominatus as an observer/controller of Aurelian facts rather than backend objects.

## 9. Validation results

Validation commands executed for this milestone:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- allocator/reference/boundary `rg` checks listed in the A28 task prompt
- `git status --short`

## 10. Deferred features

Deferred by design:

- Vulkan buffers;
- Vulkan textures/images;
- memory binding helpers;
- upload rings/staging arenas;
- persistent mapping;
- budget extensions;
- VMA/VMASharp package/API verification and backend implementation;
- cross-plant transfers;
- layout/access/stage/subresource tracking;
- swapchain/window/surface/render-pass/pipeline work.

## 11. Next recommendation

A29 — Buffer resource M0

A29 should add a buffer create plan, create a simple Vulkan buffer, query memory requirements, allocate through `IVulkanMemoryAllocator`, bind memory, expose plant-id resource state, and avoid textures, swapchain, render pass, and draw work.
