# A31 — Staging buffer / device-local upload copy M0 audit

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Resources/Uploads/` with upload request, result, status, diagnostics, diagnostic codes, and the M0 one-shot `VulkanBufferUploader`.
- Added `tests/Aurelian.Graphics.Tests/VulkanBufferUploadM0Tests.cs`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md`.

## 2. Task scope

A31 implements the first real CPU-to-GPU device-local buffer upload path:

```text
CPU bytes
  -> mapped CpuToGpu staging buffer
  -> vkCmdCopyBuffer
  -> device-local destination buffer
  -> command-list timeline fence signal
```

The implementation is intentionally M0 and one-shot. It does not add an upload ring, persistent staging allocator, batching, async staging retirement, texture upload, render passes, pipelines, draw calls, descriptor binding, swapchain/window/surface support, VMA/VMASharp, Vortice, service locators, or global state.

## 3. Reference material read

The inspection and reference-reading commands requested for A31 were run, including:

```bash
git status --short
find src/Aurelian.Graphics -type f | sort
find tests/Aurelian.Graphics.Tests -type f | sort
sed -n '1,520p' docs/audits/0030-a30-buffer-mapped-memory-upload-m0.md || true
sed -n '1,520p' docs/audits/0029-a29-vulkan-buffer-resource-m0.md || true
sed -n '1,520p' docs/audits/0026-a26-vulkan-command-buffer-pool-m0.md || true
sed -n '1,520p' docs/audits/0025-a25-timeline-fences-resource-pool-m0.md || true
```

Relevant source areas inspected:

```bash
find src/Aurelian.Graphics/Vulkan/Resources/Buffers -type f | sort
find src/Aurelian.Graphics/Vulkan/Resources/Allocation -type f | sort
find src/Aurelian.Graphics/Vulkan/Commanding -type f | sort
find src/Aurelian.Graphics/Vulkan/Sync -type f | sort
find src/Aurelian.Graphics/Vulkan/Device -type f | sort
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Resources/Buffers/AurelianVulkanBuffer.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Resources/Buffers/VulkanBufferFactory.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Resources/Allocation/IVulkanMemoryAllocator.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Commanding/VulkanCommandBufferPool.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Commanding/VulkanCommandBufferLease.cs
sed -n '1,520p' src/Aurelian.Graphics/Vulkan/Sync/VulkanFenceBundle.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Sync/VulkanTimelineFence.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Device/AurelianVulkanPlant.cs
```

Claude/Stride/Prometheus references were read with the requested searches and excerpts. The key extracted lessons were:

- Stride upload intent borrowed: CPU data is staged in host-visible memory and copied into GPU-local resources with a recorded Vulkan copy command, then submitted and synchronized by GPU progress fences.
- Stride upload-ring issues avoided: A31 does not add an unbounded upload ring, hidden persistent upload allocator, or command-list-owned upload lifetime. The staging allocation is temporary and explicitly synchronized before disposal.
- Prometheus submit/fence/retire lessons applied: fence values represent resource safety boundaries, submitted work must have observable signal/completion facts, and resource reuse/lifetime should be generation/fence-driven rather than guessed. For M0, this becomes a synchronous wait before staging disposal.
- Deferred: upload arenas/rings, grow/shrink budget policy, async staging retirement, image layout transitions, transfer queue policy, and Dominatus-visible upload budgeting.

## 4. Upload request/result model

`VulkanBufferUploadRequest` carries the destination `AurelianVulkanBuffer`, source bytes, destination offset, and debug name. `VulkanBufferUploadResult` returns `Submitted`, `Rejected`, or `Failed`, an optional signal fence value, and upload diagnostics. `Success` is true only when the upload was submitted and a signal value is available.

Validation rejects:

- disposed uploader;
- missing destination;
- empty data;
- plant mismatch;
- destination missing `TransferDestination` usage;
- out-of-bounds upload ranges.

## 5. Staging buffer behavior

The uploader creates a temporary staging buffer through `VulkanBufferFactory` using:

- `SizeBytes = request.Data.Length`;
- `Usage = VulkanBufferUsage.TransferSource`;
- `MemoryUsage = VulkanMemoryUsage.CpuToGpu`;
- `MapOnCreate = true`;
- uploader plant id.

Bytes are written through `AurelianVulkanBuffer.Write(...)`. The upload code does not call raw allocation, free, map, or unmap APIs.

## 6. Command buffer copy/submit behavior

The uploader rents a command buffer from `VulkanCommandBufferPool`, begins it as a one-time command buffer through the existing lease API, records exactly one `vkCmdCopyBuffer`, and ends the lease.

The copy region is:

- `srcOffset = 0`;
- `dstOffset = request.DestinationOffset`;
- `size = request.Data.Length`.

The command buffer is submitted to `plant.GraphicsQueue` using `vkQueueSubmit` with a `TimelineSemaphoreSubmitInfo` pNext chain that signals `VulkanFenceBundle.CommandListFence` to the allocated value.

## 7. Fence/wait behavior

Before renting, the uploader queries `CommandListFence.QueryCompletedValue()` and uses that value to rent/reuse command buffers safely. After recording, it allocates a command-list fence signal value, submits the command buffer, waits up to five seconds for that value, and only then retires the command buffer to the pool.

If the wait fails after submission, the uploader reports failure with diagnostics and attempts queue-idle cleanup before disposing temporary staging resources. The expected normal path is the timeline wait.

## 8. Lifetime/disposal safety

The uploader does not own the plant, allocator, command buffer pool, or fence bundle. Its disposal is idempotent and prevents later uploads with an explicit diagnostic.

Temporary staging buffers are disposed only after the submitted copy has completed in the success path. This is synchronous but safe for M0 and avoids returning a pending object or inventing an upload ring/staging pool prematurely.

## 9. Tests added

Added `VulkanBufferUploadM0Tests` covering:

- Vulkan unavailable skip-cleanly behavior;
- empty upload rejection;
- missing transfer-destination usage rejection;
- out-of-bounds rejection;
- device-local upload submit/fence signal path when Vulkan is available;
- idempotent uploader dispose;
- upload-after-dispose diagnostics;
- boundary check that upload code does not call raw allocation/map APIs.

Tests remain normal-test safe when Vulkan is unavailable: plant initialization diagnostics are asserted and tests return.

## 10. Boundary checks

Boundary checks requested for A31 were run after implementation:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "VMASharp|Vma|Vortice|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|RenderPass|Framebuffer|Pipeline|Draw|vkCmdDraw|vkCreateImage|Texture|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkAllocateMemory|vkFreeMemory|AllocateMemory|FreeMemory|vkMapMemory|vkUnmapMemory|MapMemory|UnmapMemory" src/Aurelian.Graphics -g '*.cs'
rg -n "vkCmdCopyBuffer|CmdCopyBuffer|vkQueueSubmit|QueueSubmit|Submit" src/Aurelian.Graphics/Vulkan/Resources/Uploads src/Aurelian.Graphics/Vulkan/Commanding tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

Expected benign matches include package references to Silk.NET.Windowing, existing raw allocator backend calls, existing project references, and documentation/test names containing searched words.

## 11. Validation results

Validation passed in the current environment:

- `dotnet build Aurelian.slnx -c Debug` passed.
- `dotnet test Aurelian.slnx -c Debug` passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed.
- Boundary grep confirmed upload code uses copy/submit only in the upload seam and raw allocation/map/unmap calls remain isolated to allocator code, apart from test strings used to assert the boundary.

## 12. Deferred features

Deferred beyond A31:

- upload ring;
- persistent staging allocator;
- batching;
- async/fence-retired staging resources;
- texture/image creation and upload;
- image layout transitions and general barrier tracking;
- non-coherent flush/invalidate path;
- transfer queue split/handoff policy;
- descriptor binding;
- swapchain/window/surface;
- render pass/framebuffer/pipeline/draw;
- VMA/VMASharp and Vortice;
- Dominatus upload budget policy.

## 13. Next recommendation

Recommended next milestone:

```text
A32 — Barrier/layout tracker M0
```

Reason: texture/resource upload and later render-resource work will need explicit barriers and image layout/resource-state tracking before texture uploads, draw paths, or pipeline work can converge safely.
