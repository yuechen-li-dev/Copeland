# A35 — Texture upload M0

## 1. Files changed

- Added `VulkanTextureUploadRequest`, `VulkanTextureUploadResult`, `VulkanTextureUploadStatus`, `VulkanTextureUploadDiagnostic`, and `VulkanTextureUploadDiagnosticCodes` in `src/Aurelian.Graphics/Vulkan/Resources/Uploads/`.
- Added `VulkanTextureUploader` in `src/Aurelian.Graphics/Vulkan/Resources/Uploads/`.
- Added `tests/Aurelian.Graphics.Tests/VulkanTextureUploadM0Tests.cs`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with the A35 synchronous Texture2D upload scope and deferrals.

## 2. Task scope

A35 implements only the first narrow CPU-to-GPU Texture2D upload path:

```text
CPU RGBA bytes
  -> mapped CpuToGpu staging buffer
  -> image barrier to TransferDestination
  -> vkCmdCopyBufferToImage
  -> image barrier to ShaderResourceFragment
  -> queue submit
  -> command-list timeline fence signal/wait
```

The implementation is whole texture only, 2D only, mip 0 only, array layer 0 only, four bytes per pixel only, and synchronous for M0.

## 3. Reference material read

Read before implementation:

- A34 barrier emission audit.
- A33 Texture2D resource audit.
- A31 staging upload audit.
- A21 Vulkan intent-port plan.
- Claude Vulkan intent-port audit texture/upload/barrier sections.
- Stride `Texture.Vulkan.cs` and `CommandList.Vulkan.cs` texture upload/barrier references.

Stride intent borrowed:

- Use a CPU upload/staging allocation as the source for texture data.
- Transition the image into a transfer destination layout before copy.
- Record `vkCmdCopyBufferToImage` with a `BufferImageCopy` region.
- Transition to a shader-readable layout after upload.
- Ensure the upload command work completes before temporary upload memory can be reclaimed.

Stride pitfalls avoided:

- No shared mutable `NativeLayout`; Aurelian keeps a per-texture, per-subresource `VulkanLayoutTracker`.
- No copy before the correct destination layout; the upload path records a transfer-destination barrier first.
- No per-resource raw allocation leak from upload code; staging is created through buffer factory + allocator contracts and disposed after fence completion.
- No upload buffer lifetime ambiguity; M0 waits synchronously before releasing staging.
- No broad texture feature import; partial uploads, mips, arrays, cubes, samplers, descriptors, render passes, pipelines, and draws remain deferred.

## 4. Texture upload request/result model

The request is a small typed record with destination texture, byte payload, and optional debug name. Validation enforces the M0 assumptions: destination must exist, must not be disposed, must belong to the uploader plant, must include `TransferDestination`, must use a supported four-byte color format, and the byte length must exactly equal `width * height * 4`.

The result reports `Submitted`, `Rejected`, or `Failed`, the optional signal fence value, and structured diagnostics. `Success` is true only when the status is `Submitted` and a signal value is available.

## 5. Staging buffer behavior

`VulkanTextureUploader` creates a temporary mapped staging buffer with:

- `VulkanBufferUsage.TransferSource`;
- `VulkanMemoryUsage.CpuToGpu`;
- `MapOnCreate: true`.

CPU bytes are copied by `AurelianVulkanBuffer.Write(...)`. The upload helper does not directly allocate/free/map/unmap raw memory.

## 6. Barrier/copy command behavior

The command recording sequence is:

1. rent a command buffer after querying the command-list fence completed value;
2. begin one-time command recording;
3. plan and emit a texture barrier to `TransferDestination`;
4. record `CmdCopyBufferToImage` with buffer offset 0, row length 0, image height 0, color aspect, mip 0, array layer 0, layer count 1, offset `(0,0,0)`, and extent `(width,height,1)`;
5. plan and emit a texture barrier to `ShaderResourceFragment`;
6. end command recording.

A no-op barrier plan is accepted if the tracker is already at the requested layout.

## 7. Fence/wait behavior

The upload allocates a command-list timeline fence signal value, submits the command buffer to the plant graphics queue with that timeline semaphore signal value, waits for the value, and then retires the command buffer to the pool with the same value.

The wait is synchronous by design for M0 so staging resources are not destroyed before GPU completion.

## 8. Layout tracker behavior

The upload path uses the existing A34 pattern: `VulkanLayoutTracker.Transition(...)` mutates the tracker when a plan is created, and barrier emission consumes the resulting plan. After successful upload, mip 0 / layer 0 is tracked as `ShaderResourceFragment`.

Known limitation: if planning succeeds but command emission later fails, the tracker may be ahead of actual GPU state. A35 accepts this because the path is synchronous, narrow, and failure is expected to be exceptional. A future barrier transaction API should provide plan/emit/commit rollback or reconciliation.

## 9. Lifetime/disposal safety

`VulkanTextureUploader` is per plant and does not own the plant, allocator, command buffer pool, or fence bundle. It supports idempotent disposal. Upload after disposal returns a rejected result with `AGTU1014`.

Temporary staging buffers are disposed in `finally`, but only after the synchronous submit/wait path has completed on success. If submission was observed and a wait failure occurs, the helper asks the queue to idle before returning failure and releasing staging.

## 10. Tests added

Added `VulkanTextureUploadM0Tests` covering:

- clean Vulkan-unavailable behavior;
- empty data rejection;
- missing `TransferDestination` usage rejection;
- byte-size mismatch rejection;
- disposed texture rejection;
- successful submit/fence signal when a plant is available;
- final layout tracker state of `ShaderResourceFragment`;
- idempotent uploader disposal;
- upload-after-dispose diagnostic;
- no raw allocation/mapping API calls in upload code.

## 11. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "VMASharp|Vma|Vortice|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|RenderPass|Framebuffer|Pipeline|Draw|vkCmdDraw|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkAllocateMemory|vkFreeMemory|AllocateMemory|FreeMemory|vkMapMemory|vkUnmapMemory|MapMemory|UnmapMemory" src/Aurelian.Graphics -g '*.cs'
rg -n "vkCmdCopyBufferToImage|CmdCopyBufferToImage|vkCmdPipelineBarrier|CmdPipelineBarrier|vkQueueSubmit|QueueSubmit|Submit" src/Aurelian.Graphics/Vulkan/Resources/Uploads src/Aurelian.Graphics/Vulkan/Resources/Barriers tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

## 12. Validation results

- Build passed with 0 warnings and 0 errors.
- Full solution tests passed.
- Graphics tests passed: 114 tests.
- Raw memory allocation/mapping calls remain isolated to `RawVulkanMemoryAllocator` and allocator diagnostics.
- Copy-to-image command use appears in the texture upload path and related tests.
- Pipeline barriers remain in the barrier emitter/upload-adjacent tests.
- Submit remains in upload paths/tests.
- The broad forbidden-token scan still reports pre-existing type names and Vulkan enum/API names such as `StructureType`, `PhysicalDeviceType`, `PipelineStageFlags`, `CmdPipelineBarrier`, and the existing `VulkanAllocationBackendKind.Vma` enum value; no A35 VMA/VMASharp/Vortice package or implementation was added.

## 13. Deferred features

Deferred beyond A35:

- mip generation;
- partial region uploads;
- texture arrays, cubes, and 3D uploads;
- upload rings and async/fence-retired staging;
- samplers;
- descriptor sets;
- render passes;
- pipelines;
- draw commands;
- swapchains/windows/surfaces;
- readback verification;
- non-coherent memory flush/invalidate policy;
- transactional barrier rollback/reconciliation;
- VMA/VMASharp backend adoption;
- Vortice.

## 14. Next recommendation

Recommended next milestone: **A36 — Render pass descriptor M0**.

Resources, uploads, layout tracking, and barrier emission now exist, so a narrow render-pass descriptor contract is the next graphics foundation before pipeline and draw work. Surface/swapchain can remain deferred until the command/resource core can describe the render targets it intends to use.
