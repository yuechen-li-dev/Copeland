# A30 — Buffer mapped memory / CPU upload M0

## 1. Files changed

- Extended `src/Aurelian.Graphics/Vulkan/Resources/Allocation/` allocation contracts, allocation wrapper state, raw allocator mapping behavior, diagnostics, and telemetry.
- Extended `src/Aurelian.Graphics/Vulkan/Resources/Buffers/` buffer create plans and added safe mapped-buffer write result/diagnostic contracts.
- Added `tests/Aurelian.Graphics.Tests/VulkanBufferMappedMemoryM0Tests.cs` for unavailable-safe mapped allocation and write coverage.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md`.

## 2. Task scope

A30 implements safe CPU writes to host-visible Vulkan buffer allocations only. It covers persistent map-on-create intent, map/unmap ownership in the allocator backend, mapped allocation telemetry, and bounds-checked byte writes into mapped buffers.

A30 intentionally does not implement staging-to-device-local copy, copy command recording, queue submission, upload rings, non-coherent flush/invalidate support, textures, descriptors, render passes, pipelines, draw commands, swapchain/window/surface work, VMA/VMASharp, Vortice, or global allocation policy.

## 3. Reference material read

Read before coding:

- `docs/audits/0029-a29-vulkan-buffer-resource-m0.md`
- `docs/audits/0028-a28-vulkan-allocator-contracts-raw-m0.md`
- `docs/architecture/graphics-memory-allocation.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/Buffer.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/GraphicsDevice.Vulkan.cs`
- Stride search output captured in `/tmp/a30-stride-buffer-upload-search.txt`

Reference takeaways:

- Persistent mapping is the right direction for dynamic/staging-style host-visible buffers: map once at creation and unmap at destruction rather than churning map/unmap calls per update.
- Stride's direct map/unmap path in command-list update code is useful reference material, but Aurelian should avoid spreading raw `vkMapMemory`/`vkUnmapMemory` above allocator backends.
- Upload ring behavior is explicitly deferred. Future upload rings need fence-aware reuse and budget policy rather than unbounded transient upload allocation.

## 4. Mapping allocation model

`VulkanAllocationRequest` now carries `MapOnCreate`. `RawVulkanMemoryAllocator` validates that mapping is requested only for `CpuToGpu` or `GpuToCpu` allocations, rejects `GpuOnly + MapOnCreate`, allocates memory, maps it when requested, and stores the mapped pointer in `VulkanMemoryAllocation`.

`VulkanMemoryAllocation` exposes `IsMapped` and `CanWrite` facts while keeping the raw pointer internal. Disposal is idempotent. If an allocation is mapped, the raw allocator unmaps it before freeing memory.

M0 host-visible memory still requires `HOST_VISIBLE | HOST_COHERENT` for `CpuToGpu` and `GpuToCpu`, so no flush/invalidate operations are required or implemented in A30.

## 5. Buffer write API

`AurelianVulkanBuffer.Write(ReadOnlySpan<byte> data, ulong destinationOffset = 0)` is the M0 CPU write path. It:

- rejects disposed buffers;
- treats empty writes as successful no-ops;
- rejects unmapped or non-writable allocations;
- rejects writes where `destinationOffset + data.Length` exceeds the logical buffer size;
- copies bytes into the mapped allocation pointer for valid writes.

The API writes byte data only. It does not record copy commands, submit work, flush non-coherent memory, or expose a readback API.

## 6. Diagnostics

Allocator diagnostics added:

- `AGM1008 MappingNotSupportedForUsage`
- `AGM1009 MapMemoryFailed`

Buffer write diagnostics added:

- `AGB2001 BufferNotMapped`
- `AGB2002 WriteOutOfBounds`
- `AGB2003 BufferDisposed`
- `AGB2004 EmptyWrite` reserved for future explicit no-op reporting; empty writes currently return success with no diagnostic.

## 7. Tests added

`VulkanBufferMappedMemoryM0Tests` covers:

- mapped `CpuToGpu` buffer creation succeeds or returns clean diagnostics when Vulkan is unavailable/unsuitable;
- mapped buffer writes within bounds return success;
- writes to unmapped buffers are rejected;
- out-of-bounds writes are rejected;
- writes after dispose return the disposed diagnostic;
- `MapOnCreate` rejects `GpuOnly` usage;
- mapped allocation telemetry updates when mapping succeeds;
- allocation disposal unmaps before freeing and remains idempotent.

## 8. Boundary checks

Boundary checks performed for A30:

- solution build;
- solution tests;
- graphics test project tests;
- dependency/scope forbidden search;
- raw allocate/free/map/unmap search;
- command-buffer copy/submit search in buffer/allocation scope;
- git status review.

## 9. Validation results

Validated locally:

- `dotnet build Aurelian.slnx -c Debug` passed.
- `dotnet test Aurelian.slnx -c Debug` passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed.

The graphics tests remain unavailable-safe: if Vulkan plant creation cannot succeed, tests assert clean diagnostics and return without failing due to missing Vulkan runtime/device support.

## 10. Deferred features

Deferred intentionally:

- staging-to-device-local upload copies;
- `vkCmdCopyBuffer` recording;
- queue submission/upload execution;
- upload ring allocator and fence-aware reuse;
- non-coherent memory flush/invalidate;
- textures/images and layout transitions;
- descriptors;
- render passes, framebuffers, pipelines, and draw commands;
- swapchain/window/surface creation;
- VMA/VMASharp and Vortice backend work;
- global allocator or service locator patterns.

## 11. Next recommendation

Recommended next milestone:

```text
A31 — Staging buffer / device-local upload copy M0
```

Reason: A30 proves safe direct CPU writes for host-visible buffers. The next missing resource capability is populating device-local buffers through an explicit staging/copy path without weakening the allocator boundary or hiding queue submission inside buffer objects.
