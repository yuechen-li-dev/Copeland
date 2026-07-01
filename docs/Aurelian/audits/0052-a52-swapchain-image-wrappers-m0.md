# A52 — Swapchain image wrappers M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetImage.cs`: non-owning swapchain presentation image wrapper.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetImageSet.cs`: ordered wrapper set with image-index lookup and swapchain construction helper.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetResolver.cs`: neutral `PresentationTargetRef` to backend wrapper resolver.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetResolutionResult.cs`: typed resolution result.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetStatus.cs`: typed resolver status enum.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetDiagnostic.cs`: typed resolver diagnostics.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetDiagnosticCodes.cs`: A52 diagnostic codes.
- `src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSwapchain.cs`: public image-set creation method and disposed-state check.
- `src/Aurelian.Graphics/Properties/AssemblyInfo.cs`: internal visibility for focused graphics tests.
- `tests/Aurelian.Graphics.Tests/VulkanPresentationTargetImageM0Tests.cs`: headless-safe wrapper and resolver tests.
- `README.md`: A52 status.
- `docs/architecture/mvp-roadmap.md`: A52 roadmap status and A53 recommendation.
- `docs/architecture/compositor-policy-mechanism-split.md`: A52 mechanism status.
- `docs/architecture/dependency-policy.md`: A52 dependency/ownership note.
- `docs/audits/0052-a52-swapchain-image-wrappers-m0.md`: this report.

No CodeReferences files, vendor/Dominatus files, shader projects, VMA/VMASharp, Vortice, runtime policy, or world code were changed.

## 2. Task scope

A52 implements the graphics-side addressability layer for swapchain presentation images. The milestone makes swapchain images visible to future Vulkan compositor mechanism code as explicit non-owning presentation target wrappers, while neutral contracts continue to use symbolic `PresentationTargetRef` values.

A52 does not implement copy/blit, render-to-swapchain, barrier emission, queue submit, acquire/present expansion, compositor dispatch, frame-loop policy, Dominatus policy, or plant output source image wrappers.

Convergence state: **Success**. The intended wrapper/addressing capability now exists and is covered by headless-safe tests; the next blocker is isolated as backend source-image resolution plus Vulkan copy/blit dispatch.

## 3. Reference material read

Reference material inspected before coding:

- `docs/audits/0051-a51-compositor-contracts-m0.md`
- `docs/audits/0050-a50-compositor-policy-mechanism-split-audit.md`
- `docs/audits/0049-a49-swapchain-acquire-present-m0.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/SwapChainGraphicsPresenter.Vulkan.cs`
- `/tmp/a52-stride-swapchain-image-search.txt`

Extracted intent:

- Swapchain images are owned by the swapchain/window-system presentation path, not by Aurelian texture allocation code.
- Stride creates swapchain image views and then binds the current swapchain image into a backbuffer texture abstraction, coupling presenter state, global graphics-device assumptions, and ordinary texture paths.
- Aurelian should avoid hiding acquired presentation targets behind an ordinary texture owner. A52 instead uses explicit non-owning wrappers, neutral `PresentationTargetRef` mapping, and one layout tracker per swapchain image.

## 4. Wrapper ownership model

`VulkanPresentationTargetImage` is a non-owning view of swapchain state. It carries:

- plant ID;
- swapchain image index;
- width and height;
- selected swapchain format string;
- internal native `Image` handle;
- internal native `ImageView` handle;
- one `VulkanLayoutTracker`.

The wrapper does not implement `IDisposable` because it owns no native resource. It does not destroy swapchain images or image views, does not allocate memory, does not free memory, and does not model itself as an `AurelianVulkanTexture`.

`AurelianVulkanSwapchain` remains the owner responsible for destroying image views and the swapchain handle during its existing disposal path.

## 5. Presentation target image set

`VulkanPresentationTargetImageSet` stores the ordered wrapper collection for one swapchain and exposes `TryGet(uint imageIndex, out VulkanPresentationTargetImage target)`. The image set is also non-owning and does not implement disposal.

`AurelianVulkanSwapchain.CreatePresentationTargetImageSet()` creates one wrapper per swapchain image, preserves swapchain image order, copies the swapchain facts needed by the wrapper, and disallows creation after swapchain disposal with `ObjectDisposedException`.

## 6. PresentationTargetRef resolution

`VulkanPresentationTargetResolver.Resolve(...)` maps neutral `PresentationTargetRef` values from `Aurelian.Rendering.Contracts.Compositor` to backend wrappers. It validates:

- image set is present (`AGCT1001`);
- target plant ID matches the image set plant ID (`AGCT1002`);
- target swapchain image index exists in the set (`AGCT1003`).

Successful resolution returns `VulkanPresentationTargetStatus.Resolved`, the target wrapper, and no diagnostics. Rejections return typed diagnostics and no target.

`AGCT1004 SwapchainDisposed` is reserved for disposed-swapchain target creation/resolution seams; A52 currently disallows creating an image set from a disposed swapchain before the resolver is involved.

## 7. Layout tracker convention

Every presentation target image wrapper initializes a one-mip/one-array-layer `VulkanLayoutTracker` to `VulkanResourceLayout.Present`.

This is an A52 convention for swapchain presentation images: future compositor work can explicitly plan transitions from `Present` into transfer-destination or color-attachment layouts. A52 itself emits no barriers and performs no layout transitions.

## 8. Tests added

`tests/Aurelian.Graphics.Tests/VulkanPresentationTargetImageM0Tests.cs` adds headless-safe coverage for:

- clean skip when presentation plant/swapchain creation is unavailable;
- wrapping all swapchain images when a swapchain is available;
- initial `Present` layout tracking for each wrapper;
- resolving a matching `PresentationTargetRef`;
- rejecting plant mismatches;
- rejecting image indices outside the wrapper set;
- preserving swapchain ownership of images/views through normal swapchain disposal;
- disallowing wrapper-set creation after swapchain disposal.

Resolver tests use synthetic internal wrappers so they pass even in headless environments.

## 9. Boundary checks

Boundary commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Aurelian.World|Aurelian.Rendering.Null|Aurelian.Shaders|Dxc|DXC|SDSL|Sdslv|Hlsl|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCmdCopyImage|CmdCopyImage|vkCmdBlitImage|CmdBlitImage|vkCmdPipelineBarrier|CmdPipelineBarrier|vkQueueSubmit|QueueSubmit|vkQueuePresent|QueuePresent|vkAcquireNextImage|AcquireNextImage" src/Aurelian.Graphics/Vulkan/Compositor tests/Aurelian.Graphics.Tests -g '*.cs' || true
rg -n "DestroyImage|vkDestroyImage|DestroyImageView|vkDestroyImageView" src/Aurelian.Graphics/Vulkan/Compositor tests/Aurelian.Graphics.Tests -g '*.cs' || true
```

The first dependency-boundary search reports pre-existing graphics/test mentions such as shader-boundary guard tests, Vulkan enum names like `Type2D`/`SType`, existing acquire/present tests, and existing allocator enum text. A52 did not add forbidden project references or dependencies.

The command-boundary search reports no copy, blit, pipeline-barrier, queue-submit, queue-present, or acquire calls under `src/Aurelian.Graphics/Vulkan/Compositor`. Hits in tests are from pre-existing swapchain acquire/present tests and the boundary command text in this audit report is outside the searched paths.

The destroy-boundary search reports no destroy image/image-view calls under `src/Aurelian.Graphics/Vulkan/Compositor`; swapchain image-view destruction remains in the existing swapchain owner.

## 10. Validation results

Validation results observed:

- `dotnet build Aurelian.slnx -c Debug`: passed.
- `dotnet test Aurelian.slnx -c Debug`: passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug`: passed.

## 11. Deferred features

A52 deliberately defers:

- compositor passthrough dispatch;
- plant output backend source-image wrappers;
- resolving `PlantOutputRef` to a backend image;
- copy/blit/compute compositing;
- render-to-swapchain;
- layout transition planning/emission for presentation targets;
- queue submission and semaphore handoff;
- acquire/present policy expansion;
- present loop/frame loop;
- Dominatus runtime policy;
- VMA/VMASharp and Vortice;
- shader/compiler dependencies in graphics.

## 12. Next recommendation

A53 — Vulkan compositor passthrough copy M0

A53 should:

- accept neutral `CompositorDispatchRequest`;
- resolve `PresentationTargetRef` to a swapchain target wrapper;
- resolve one `PlantOutputRef` to a backend source image wrapper;
- record barriers and copy/blit source image to presentation target;
- signal render-finished semaphore or return a typed dispatch result;
- keep Dominatus policy deferred.
