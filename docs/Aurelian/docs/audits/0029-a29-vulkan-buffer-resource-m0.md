# A29 — Vulkan Buffer resource M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Resources/Buffers/` with Aurelian-owned buffer usage flags, create plans, diagnostics, create results, native buffer ownership, and the buffer factory.
- Added `tests/Aurelian.Graphics.Tests/VulkanBufferM0Tests.cs` for validation, unavailable-safe creation, disposal, and allocator-boundary checks.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` to record the A29 resource boundary and A30 recommendation.

## 2. Task scope

A29 implements only the first real Vulkan GPU resource type: buffers. The milestone covers creating a Vulkan buffer, querying memory requirements, allocating through `IVulkanMemoryAllocator`, binding memory, exposing plant/resource state, and safe disposal.

A29 intentionally does not implement textures, upload rings, mapped memory APIs, staging/copy commands, descriptors, render passes, pipelines, draw commands, swapchains, windows, surfaces, VMA/VMASharp, Vortice, global allocators, service locators, or reflection-based construction.

## 3. Reference material read

- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- `docs/audits/0028-a28-vulkan-allocator-contracts-raw-m0.md`
- `docs/architecture/graphics-memory-allocation.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResource.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsResourceBase.Vulkan.cs`
- `CodeReferences/Prometheus/reactor_vulkan_sgemm.c` search results for buffer/arena/fence/telemetry language.

Borrowed buffer intent:

- buffers are plant-owned resources;
- usage is expressed through Aurelian-owned flags and mapped to Vulkan flags at the backend edge;
- buffer resource state carries `PlantId`;
- allocation and resource lifetime are separate seams;
- telemetry and explicit ownership are preferred over hidden global state.

Stride pitfalls avoided:

- no per-buffer `vkAllocateMemory` spread in buffer code;
- no per-resource physical-device memory-property query;
- no typed buffer views in M0;
- no implicit `vkCmdFillBuffer` zeroing;
- no dynamic-buffer map/unmap path in this milestone;
- no descriptor or command-list coupling in the buffer object.

Prometheus lessons kept at the architecture level:

- ownership, capacity, and validity should be explicit facts;
- staging/upload and in-flight retirement need their own milestone rather than being hidden inside buffer creation;
- telemetry should remain plain data consumable by future policy layers.

## 4. Buffer create plan

`VulkanBufferCreatePlan` contains:

- `PlantId` — the plant/device that owns the buffer;
- `SizeBytes` — logical buffer size requested by the caller;
- `VulkanBufferUsage` — Aurelian-owned usage flags;
- `VulkanMemoryUsage` — allocation intent consumed by the allocator;
- `DebugName` — optional diagnostic/debug label text.

Validation rejects:

- zero-size buffers (`AGB1001`);
- no usage flags (`AGB1002`);
- `VulkanMemoryUsage.Unknown` (`AGB1003`);
- mismatched plan/plant/allocator plant ids (`AGB1004`).

## 5. Usage mapping

A29 maps Aurelian-owned usage flags to Vulkan flags as follows:

| Aurelian usage | Vulkan usage |
| --- | --- |
| `Vertex` | `BufferUsageFlags.VertexBufferBit` |
| `Index` | `BufferUsageFlags.IndexBufferBit` |
| `Uniform` | `BufferUsageFlags.UniformBufferBit` |
| `Storage` | `BufferUsageFlags.StorageBufferBit` |
| `TransferSource` | `BufferUsageFlags.TransferSrcBit` |
| `TransferDestination` | `BufferUsageFlags.TransferDstBit` |

M0 exposes no typed buffer views and performs no bind-as-vertex/index/descriptors integration.

## 6. Allocator boundary

The buffer factory creates the `VkBuffer`, queries `VkMemoryRequirements`, and sends `requirements.Size` plus `requirements.MemoryTypeBits` to `IVulkanMemoryAllocator.Allocate` through `VulkanAllocationRequest`.

The boundary remains strict:

- buffer code does not call `vkAllocateMemory`;
- buffer code does not call `vkFreeMemory`;
- `RawVulkanMemoryAllocator` remains the only raw allocator backend in M0;
- the resource disposes the returned `VulkanMemoryAllocation` but does not know how that allocation was created;
- `GpuResourceState` records logical plan size and allocation backend, while `VulkanMemoryAllocation` records backend allocation size/offset/memory.

## 7. Native buffer lifetime/disposal

`AurelianVulkanBuffer` owns:

- the native `VkBuffer` handle;
- the `VulkanMemoryAllocation` returned by the allocator;
- plant id, logical size, usage, memory usage, and `GpuResourceState`.

Disposal is idempotent. On first dispose it destroys the native buffer and disposes the allocation. Later dispose calls return without additional Vulkan calls or allocator frees.

## 8. Tests added

`VulkanBufferM0Tests` covers:

- unavailable-safe Vulkan plant creation path;
- zero-size rejection;
- no-usage rejection;
- unknown-memory-usage rejection;
- plant mismatch rejection;
- small CPU-visible vertex buffer creation when Vulkan is available, with clean diagnostic return when creation cannot succeed;
- idempotent buffer disposal and allocator telemetry free count;
- source-level allocator boundary check for buffer files.

## 9. Boundary checks

Required checks include:

- solution build;
- solution tests;
- graphics tests;
- forbidden graphics dependency/scope search;
- raw memory allocation/free search;
- buffer create/requirements/bind search;
- git status review.

## 10. Validation results

Validated locally for A29:

- `dotnet build Aurelian.slnx -c Debug` passes.
- `dotnet test Aurelian.slnx -c Debug` passes.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passes.
- Forbidden dependency/scope search returns only expected package/project history or allowed package references, with no production dependency on VMA/Vortice, textures, swapchains, surfaces, windows, render passes, pipelines, draws, world/assets/shaders/null renderer, vendor, or CodeReferences.
- Raw `vkAllocateMemory`/`vkFreeMemory` calls remain in `RawVulkanMemoryAllocator`; buffer code uses `IVulkanMemoryAllocator`.
- `vkCreateBuffer`, `vkGetBufferMemoryRequirements`, and `vkBindBufferMemory` are contained in the new buffer factory path.

## 11. Deferred features

Deferred intentionally:

- mapped memory API;
- CPU writes/reads;
- upload rings;
- staging buffers;
- copy commands;
- barriers/layout tracking;
- descriptor binding;
- typed buffer views;
- textures;
- render passes/pipelines/draw;
- swapchain/window/surface;
- VMA/VMASharp backend;
- Dominatus memory policy integration.

## 12. Next recommendation

A30 should be **Buffer mapped memory / upload M0**.

Reason: A29 proves buffer ownership and allocator binding, but buffers still cannot receive useful data through an Aurelian-owned API. A mapped/upload M0 should add the smallest explicit CPU-visible or staging path over the existing allocator/resource boundary before texture resources or vertex-buffer rendering paths depend on populated buffers.
