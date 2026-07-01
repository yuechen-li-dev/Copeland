# A48 — Surface/swapchain M0

## 1. Files changed

- `Directory.Packages.props` and `src/Aurelian.Graphics/Aurelian.Graphics.csproj`: added the Silk.NET KHR Vulkan extension package used for `VK_KHR_surface` and `VK_KHR_swapchain` commands.
- `src/Aurelian.Graphics/Vulkan/Device/*`: added presentation-aware plant initialization through `VulkanPlantOptions.EnablePresentation`.
- `src/Aurelian.Graphics/Vulkan/Presentation/*`: added presentation diagnostics, facts, owners, factory, selection helpers, and acquire/present deferred skeletons.
- `tests/Aurelian.Graphics.Tests/VulkanSwapchainM0Tests.cs`: added headless-safe swapchain M0 tests.
- `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md`: documented A48 status and boundaries.

## 2. Task scope

A48 is limited to presentation resources: a Silk.NET.Windowing window/surface path, `VkSurfaceKHR`, surface support/capability/format/present-mode query, deterministic format/present-mode selection, `VkSwapchainKHR`, swapchain images, image views, and safe disposal. It does not integrate offscreen drawing with presentation and does not render into swapchain images.

## 3. Reference material read

- A47 command submit audit.
- A21 Vulkan intent-port plan.
- Claude Vulkan intent-port swapchain/presentation notes.
- Stride `SwapChainGraphicsPresenter.Vulkan.cs` and swapchain search hits.

Stride intent borrowed:

- explicit surface capability, format, and present-mode query before swapchain creation;
- FIFO for vsync and mailbox/immediate preference for no-vsync;
- using current surface transform as `preTransform`;
- swapchain images are retrieved and wrapped with image views;
- acquire/present is a distinct synchronization concern.

Stride pitfalls avoided:

- unimplemented platform branches are not copied; Silk.NET.Windowing owns platform surface creation;
- fullscreen dead code is not introduced;
- queue-wide idle waits are not used;
- debugger-break fallbacks are not used;
- swapchain ownership is not hidden in a global graphics device.

Aurelian improvements:

- Silk.NET.Windowing surface path;
- presentation boundary isolated in `Aurelian.Graphics.Vulkan.Presentation`;
- plant-zero presentation remains explicit through `EnablePresentation`;
- unavailable/headless-safe diagnostics and tests.

## 4. Presentation-enabled device init

A48 adds `VulkanPlantOptions.EnablePresentation`. When enabled, plant initialization asks Silk.NET.Windowing for platform-required Vulkan surface instance extensions, validates that those instance extensions are available, validates that the selected physical device supports `VK_KHR_swapchain`, and enables that device extension. Offscreen/non-presentation plant creation does not require swapchain support.

If Silk.NET.Windowing cannot report required surface extensions in a headless environment, plant creation returns an unavailable diagnostic instead of throwing.

## 5. Surface creation behavior

`VulkanSwapchainFactory.Create` creates a hidden Silk.NET window, initializes it, obtains its `IVkSurface`, creates `VkSurfaceKHR` for the presentation plant instance, and checks selected queue-family presentation support against the created surface.

Failures return typed presentation diagnostics such as `HeadlessEnvironment`, `SurfaceCreationFailed`, or `SurfaceSupportMissing`.

## 6. Swapchain selection behavior

Surface state is queried through `VK_KHR_surface`:

- capabilities;
- formats;
- present modes.

Format selection is deterministic: prefer `B8G8R8A8Srgb` with SRGB nonlinear colorspace, then `B8G8R8A8Unorm` with SRGB nonlinear colorspace, then the first reported format. The single `Format.Undefined` Vulkan wildcard case resolves to `B8G8R8A8Srgb`/SRGB nonlinear.

Present mode selection is deterministic: vsync chooses FIFO when available; no-vsync chooses Mailbox, then Immediate, then FIFO, then the first reported mode.

Extent clamps requested size to capabilities unless the surface provides a fixed current extent. Image count is `minImageCount + 1`, capped by `maxImageCount` when a cap exists.

## 7. Swapchain image/view ownership

`AurelianVulkanSwapchain` owns the `VkSwapchainKHR` and image views. Swapchain images are stored as handles but not destroyed, because they are owned by the swapchain. Disposal order is image views first, then swapchain. `AurelianVulkanSurface` owns the surface and the Silk.NET window and destroys the surface before disposing the window.

Both owners are idempotent on disposal.

## 8. Headless/unavailable behavior

A48 treats presentation as optional runtime capability. If CI/headless machines cannot create windows or if Vulkan presentation support is missing, creation returns unavailable/rejected diagnostics and tests return cleanly. Normal offscreen plant/device tests still do not require presentation.

## 9. Tests added

`VulkanSwapchainM0Tests` covers:

- clean unavailable/headless behavior;
- rejection when a plant was not created with presentation enabled;
- successful surface/swapchain facts when presentation is available;
- idempotent surface and swapchain disposal;
- deterministic format selection;
- FIFO selection for vsync;
- deferred acquire/present skeleton diagnostics.

## 10. Boundary checks

Boundary searches were run for forbidden dependencies and for presentation-call isolation. Presentation Vulkan calls are isolated to the presentation folder/tests, and no `Aurelian.Shaders`, DXC, VMA/VMASharp, Vortice, world/runtime/null-renderer, CodeReferences, vendor, service locator, singleton, or reflection dependency was introduced in `Aurelian.Graphics`.

## 11. Validation results

Validation commands for A48:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
```

The graphics test project passes in the observed headless container by treating presentation initialization as unavailable when Silk.NET.Windowing cannot find an applicable window platform.

## 12. Deferred features

Deferred intentionally:

- binary acquire/present semaphore ownership;
- actual `vkAcquireNextImageKHR`/`vkQueuePresentKHR` execution;
- render-to-swapchain image layout transitions;
- render pass/framebuffer/pipeline targeting swapchain images;
- resize/recreate policy;
- present loop;
- compositor passthrough.

## 13. Next recommendation

A49 — Swapchain acquire/present M0.
