# A49 — Swapchain acquire/present M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSwapchain.cs`: replaced A48 deferred acquire/present skeletons with `vkAcquireNextImageKHR` and `vkQueuePresentKHR` wrappers that return typed acquire/present results.
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanPresentationSemaphoreSet.cs`: added an idempotent binary semaphore owner for swapchain presentation synchronization.
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanSwapchainAcquireResult.cs`: added the dedicated acquire status enum and result record.
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanSwapchainPresentResult.cs`: added the dedicated present status enum and result record.
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanPresentationDiagnosticCodes.cs`: added A49 diagnostics for semaphore creation, invalid image indices, out-of-date/suboptimal swapchains, surface loss, and the deferred-present removal marker.
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanSwapchainFactory.cs`: creates the presentation semaphore set as part of swapchain-owned resources and reports semaphore creation failure as a typed presentation diagnostic.
- `tests/Aurelian.Graphics.Tests/VulkanSwapchainAcquirePresentM0Tests.cs`: added headless-safe acquire/present M0 tests.
- `tests/Aurelian.Graphics.Tests/VulkanSwapchainM0Tests.cs`: removed the A48 deferred acquire/present skeleton expectation.
- `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md`: documented A49 status, boundaries, and dependency policy.

## 2. Task scope

A49 is limited to proving that an available swapchain image can be acquired and that a caller-supplied swapchain image index can be submitted to Vulkan presentation. It does not render into swapchain images, does not submit render work, does not integrate the offscreen draw path with presentation, does not add a compositor, and does not add a frame/present loop.

The intended convergence state is **Success**: A48's deferred acquire/present skeleton now has real acquire/present Vulkan calls, typed outcomes, and headless-safe tests.

## 3. Reference material read

Commands run before coding included:

```bash
rg -n "AcquireNextImage|vkAcquireNextImage|QueuePresent|vkQueuePresent|binary semaphore|submitSemaphore|acquireSemaphore|present|out of date|suboptimal|surface lost|Swapchain|semaphore ring|Present" docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '1,900p' docs/claude/aurelian-vulkan-intent-port-audit.md
rg -n "AcquireNextImage|vkAcquireNextImage|QueuePresent|vkQueuePresent|Present\(|submitSemaphore|acquireSemaphore|Semaphore|currentFrameIndex|currentBufferIndex|OutOfDate|Suboptimal|SurfaceLost" CodeReferences/Stride/Stride.Graphics -g '*.cs'
sed -n '1,900p' CodeReferences/Stride/Stride.Graphics/Vulkan/SwapChainGraphicsPresenter.Vulkan.cs
```

Stride acquire/present intent borrowed:

- swapchain presentation needs binary semaphores separate from timeline command-list fences;
- acquire and present are swapchain-bound concerns;
- out-of-date and suboptimal results are expected presentation states, not exceptional application crashes;
- present waits should be explicit policy rather than hidden in a global renderer.

Stride pitfalls avoided:

- no monolithic presenter loop was ported;
- no queue-wide waits were added;
- no hidden frame index or current buffer index state was introduced;
- no render submission and present policy were mixed;
- no overbroad automatic swapchain recreation logic was added.

Aurelian improvements:

- acquire and present return explicit typed results and diagnostics;
- semaphore ownership is a tiny swapchain-owned binary semaphore set;
- render integration remains deferred and therefore visible rather than hidden;
- headless/unavailable behavior stays test-safe.

## 4. Presentation semaphore model

A49 adds one pair of binary semaphores per swapchain:

- `ImageAvailableSemaphore` is passed to `vkAcquireNextImageKHR`.
- `RenderFinishedSemaphore` is created for the presentation synchronization seam but is not waited by `Present(...)` in A49.

There is no semaphore ring yet and no multiple-frames-in-flight policy. The semaphore owner is idempotent on disposal and is owned by `AurelianVulkanSwapchain`, so it is disposed with other swapchain presentation resources.

## 5. Acquire behavior

`AurelianVulkanSwapchain.AcquireNextImage(ulong timeoutNanoseconds = ulong.MaxValue)` now:

- rejects disposed swapchains with `VulkanSwapchainAcquireStatus.Rejected` and `AGPR1012`;
- calls `vkAcquireNextImageKHR` through `KhrSwapchain`;
- uses the image-available binary semaphore and no fence;
- returns `Acquired` with an image index on `Result.Success`;
- returns `Suboptimal` with an image index and `AGPR1018` on `Result.SuboptimalKhr`;
- returns `OutOfDate` and `AGPR1017` on `Result.ErrorOutOfDateKhr`;
- returns `Unavailable` and `AGPR1019` on `Result.ErrorSurfaceLostKhr`;
- returns `Failed` and `AGPR1010` for other Vulkan results.

No automatic swapchain recreation is attempted.

## 6. Present behavior

`AurelianVulkanSwapchain.Present(uint imageIndex)` now:

- rejects disposed swapchains with `VulkanSwapchainPresentStatus.Rejected` and `AGPR1012`;
- rejects image indices outside the swapchain image range with `AGPR1016`;
- calls `vkQueuePresentKHR` with one swapchain and one image index;
- returns `Presented` on `Result.Success`;
- returns `Suboptimal` with `AGPR1018` on `Result.SuboptimalKhr`;
- returns `OutOfDate` with `AGPR1017` on `Result.ErrorOutOfDateKhr`;
- returns `Unavailable` with `AGPR1019` on `Result.ErrorSurfaceLostKhr`;
- returns `Failed` with `AGPR1011` for other Vulkan results.

A49 present intentionally uses no wait semaphores. The render-finished semaphore is created now to establish the future seam, but no A49 operation records or submits render work that could signal it. Waiting on an unsignaled render-finished semaphore would be incorrect for this milestone.

## 7. Headless/unavailable behavior

The presentation plant and swapchain creation paths retain A48's headless-safe behavior. Tests return cleanly when Vulkan, windowing, surface support, or swapchain creation is unavailable. Acquire/present tests only require the real acquire/present path when a swapchain is actually created.

## 8. Tests added

`tests/Aurelian.Graphics.Tests/VulkanSwapchainAcquirePresentM0Tests.cs` adds coverage for:

- headless/unavailable acquire/present flow;
- acquire returning an image index or typed non-success result;
- invalid present image index rejection;
- present after acquired image returning presented, suboptimal, or out-of-date status;
- idempotent presentation semaphore disposal;
- acquire after dispose diagnostics;
- present after dispose diagnostics.

The existing A48 swapchain tests still cover hidden-window/surface/swapchain creation, deterministic selection, and idempotent surface/swapchain disposal.

## 9. Boundary checks

Boundary commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Aurelian.World|Aurelian.Rendering.Null|Aurelian.Shaders|Dxc|DXC|SDSL|Sdslv|Hlsl|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkAcquireNextImage|AcquireNextImage|vkQueuePresent|QueuePresent|CreateSemaphore|vkCreateSemaphore|DestroySemaphore|vkDestroySemaphore" src/Aurelian.Graphics/Vulkan/Presentation tests/Aurelian.Graphics.Tests -g '*.cs' || true
rg -n "vkCmdDraw|CmdDraw|vkCmdBeginRenderPass|CmdBeginRenderPass|vkQueueSubmit|QueueSubmit" src/Aurelian.Graphics/Vulkan/Presentation tests/Aurelian.Graphics.Tests -g '*.cs' || true
```

The first boundary search reports pre-existing graphics code and tests that mention `SType`, `Type2D`, `GetType()`, shader-reference guard tests, and the existing allocator enum value `Vma`; A49 did not add forbidden project dependencies or runtime shader compiler dependencies. The acquire/present/semaphore search reports the new A49 presentation calls isolated under `Vulkan/Presentation` plus tests. The draw/render/queue-submit search has no hits in `Vulkan/Presentation` or the A49 tests.

## 10. Validation results

Validation results observed:

- `dotnet build Aurelian.slnx -c Debug`: passed.
- `dotnet test Aurelian.slnx -c Debug`: passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug`: passed; 204 graphics tests passed.

## 11. Deferred features

A49 deliberately defers:

- rendering to swapchain images;
- compositor passthrough;
- render pass/pipeline/draw work for presentation;
- present/frame loop;
- semaphore rings and multiple frames in flight;
- automatic swapchain recreation policy;
- offscreen draw path integration;
- descriptor sets, uniforms, index buffers, VMA/VMASharp, Vortice, and shader compiler dependencies in graphics.

## 12. Next recommendation

A50 — Compositor passthrough M0.

Rationale:

- A21 called for a compositor seam early.
- A49 makes swapchain images acquirable and presentable.
- A50 should introduce the passthrough compositor seam before direct render-to-swapchain work hardens into the wrong architecture.
