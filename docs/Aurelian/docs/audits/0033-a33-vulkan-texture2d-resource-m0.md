# A33 — Vulkan Texture2D resource M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Resources/Textures/` for texture usage flags, texture format vocabulary, create plans, diagnostics, create results, native texture ownership, and texture factory creation.
- Added `tests/Aurelian.Graphics.Tests/VulkanTextureM0Tests.cs` for validation, unavailable-safe Vulkan creation, layout tracker initialization, disposal idempotence, and allocator-boundary checks.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with the A33 scope, allocator boundary, layout tracker behavior, and deferrals.

## 2. Task scope

A33 adds the first Vulkan image resource type for Aurelian.Graphics. The scope is Texture2D creation and lifetime only:

- Aurelian-owned texture create plans.
- `VkImage` creation for 2D optimal-tiled, sample-count-1 images.
- Image memory requirement queries.
- Device-memory allocation through `IVulkanMemoryAllocator` only.
- `vkBindImageMemory` binding.
- Optional default color image view creation for shader-resource/color-attachment intent.
- Per-subresource `VulkanLayoutTracker` initialization.
- Safe double-dispose of image view, image, and allocation.

Excluded from A33:

- texture upload and staging-copy-to-image;
- `vkCmdCopyBufferToImage`;
- image memory barrier command emission;
- render passes, framebuffers, pipelines, descriptors, samplers, draws;
- swapchains, windows, surfaces, presentation integration;
- depth/stencil textures, cube textures, 3D textures, and texture arrays beyond simple 2D-array view support;
- VMA/VMASharp, Vortice, global allocators, service locators, reflection, CodeReferences changes, and vendor changes.

## 3. Reference material read

Read before implementation:

- `docs/audits/0032-a32-barrier-layout-tracker-m0.md` for layout tracker behavior and current barrier deferrals.
- `docs/audits/0029-a29-vulkan-buffer-resource-m0.md` for the existing resource factory/allocation/bind/dispose pattern.
- `docs/audits/0028-a28-vulkan-allocator-contracts-raw-m0.md` for allocator boundaries and raw allocator responsibilities.
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md` for the graphics intent-port plan and do-not-carry-over boundaries.
- `docs/claude/aurelian-vulkan-intent-port-audit.md` texture/resource sections.
- Stride reference search output in `/tmp/a33-stride-texture-search.txt` plus `Texture.Vulkan.cs`, `GraphicsResource.Vulkan.cs`, and `GraphicsResourceBase.Vulkan.cs` as reference-only material.

### Texture/image creation intent borrowed

Stride's useful intent is the native lifetime sequence: create an image, get requirements, allocate memory, bind memory, create views for intended usage, track layout state, and release native image-view/image/memory resources in deterministic owner code.

### Stride pitfalls avoided

A33 avoids copying these pitfalls:

- no direct memory allocation in texture code;
- no shared mutable `NativeLayout` as the sole layout truth;
- no upload path or copy command hidden inside texture construction;
- no depth/stencil stage-mask or aspect ambiguity;
- no staging ring ownership or command-list submission hidden in the resource constructor;
- no broad texture type matrix before Aurelian has tested contracts for each case.

### What is deferred

Upload, copy-to-image, command-barrier emission, queue-family ownership transfer, descriptor/sampler integration, render-pass/pipeline/draw integration, swapchain/presentation textures, depth/stencil, cube/3D textures, and allocator backend replacement are deferred.

## 4. Texture create plan

`VulkanTextureCreatePlan` carries:

- `PlantId`;
- width and height;
- Aurelian-owned `VulkanTextureFormat`;
- Aurelian-owned `VulkanTextureUsage` flags;
- `VulkanMemoryUsage`;
- `VulkanResourceLayout InitialLayout`;
- mip levels and array layers;
- optional debug name.

Validation rejects zero dimensions, zero mip count, zero array-layer count, no usage flags, unknown memory usage, plant/allocator mismatch, and non-undefined initial layout.

## 5. Format/usage mapping

A33 keeps the format set intentionally tiny:

- `Rgba8Unorm` -> `Format.R8G8B8A8Unorm`;
- `Bgra8Unorm` -> `Format.B8G8R8A8Unorm`;
- `Rgba8Srgb` -> `Format.R8G8B8A8Srgb`;
- `Bgra8Srgb` -> `Format.B8G8R8A8Srgb`.

Usage maps to Vulkan image usage flags:

- `ShaderResource` -> `ImageUsageFlags.SampledBit`;
- `ColorAttachment` -> `ImageUsageFlags.ColorAttachmentBit`;
- `TransferSource` -> `ImageUsageFlags.TransferSrcBit`;
- `TransferDestination` -> `ImageUsageFlags.TransferDstBit`.

## 6. Allocator boundary

Texture creation does not call `vkAllocateMemory` or `vkFreeMemory`. The factory queries `vkGetImageMemoryRequirements`, passes size and memory type bits to `IVulkanMemoryAllocator.Allocate`, and binds only the returned `VulkanMemoryAllocation` with `vkBindImageMemory`.

The raw backend remains the only current code path that owns raw Vulkan memory allocation/free/map/unmap APIs.

## 7. Image view and layout tracker behavior

A default image view is created only when usage includes `ShaderResource` or `ColorAttachment`. M0 uses color aspect only, matching the M0 format set. Single-layer textures use `ImageViewType.Type2D`; multi-layer textures use `ImageViewType.Type2DArray` without adding cube semantics.

Every successful texture creates a `VulkanLayoutTracker(mipLevels, arrayLayers, InitialLayout)`. Because A33 does not emit barriers, the only supported initial layout is `VulkanResourceLayout.Undefined`.

## 8. Native texture lifetime/disposal

`AurelianVulkanTexture` owns:

1. native `ImageView` when one was created;
2. native `Image`;
3. the allocator-returned `VulkanMemoryAllocation`.

Disposal is idempotent and releases in that order: image view, image, allocation. The texture exposes public plant/resource state and keeps native handles internal.

## 9. Tests added

Added tests for:

- clean skip/report behavior when Vulkan is unavailable;
- validation rejections for dimensions, mip levels, array layers, usage, memory usage, plant mismatch, and non-undefined initial layout;
- real small RGBA texture creation when a Vulkan plant is available, or clean diagnostics otherwise;
- idempotent disposal;
- layout tracker initialization across all subresources;
- allocator-boundary source checks that reject raw memory and copy-to-image calls in texture code.

## 10. Boundary checks

Boundary checks cover build/test execution and searches for disallowed dependencies or APIs. The texture folder contains expected image/image-view creation, memory requirements, bind, and destroy calls, and no raw memory allocation/free calls.

## 11. Validation results

Validation commands were run during A33 implementation:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug`
- dependency/API boundary `rg` checks over graphics source and tests

The normal test pattern remains Vulkan-unavailable safe: when plant creation fails, Vulkan-dependent tests assert diagnostics and return rather than failing the suite.

## 12. Deferred features

Deferred after A33:

- texture upload and staging-copy-to-image;
- `vkCmdCopyBufferToImage`;
- image and buffer barrier command emission;
- render pass, framebuffer, pipeline, descriptor, sampler, and draw integration;
- depth/stencil images;
- cube, 3D, and richer array texture semantics;
- swapchain/window/surface/presentation integration;
- VMA/VMASharp allocator backend;
- Vortice or other graphics API dependencies.

## 13. Next recommendation

A34 — Barrier command emission M1.

Texture upload and rendering both need actual command-buffer barrier emission before Aurelian can truthfully transition images from `Undefined` to transfer destination, shader resource, or color attachment layouts. Implementing barrier emission next reduces the risk that upload or render milestones would need to smuggle layout transitions into unrelated systems.
