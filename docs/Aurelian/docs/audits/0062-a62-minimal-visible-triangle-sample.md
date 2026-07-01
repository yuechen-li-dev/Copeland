# A62 — Minimal visible triangle sample executable

## 1. Files changed

* Added `samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj` for the standalone sample executable.
* Added sample-local setup, presentation, top-level program, static SPIR-V fixture, and README files under `samples/Aurelian.VisibleTriangle/`.
* Added hidden-by-default visible-window selection to `VulkanSwapchainCreateOptions` and `VulkanSwapchainFactory`.
* Added a minimal `AurelianVulkanSurface.PumpEvents()` helper for sample window event servicing.
* Added the sample project to `Aurelian.slnx`.
* Updated README and architecture docs for the A62 sample boundary.

## 2. Task scope

A62 is a sample executable milestone. It demonstrates the current prepared-visible path with one frame, one offscreen triangle, compositor passthrough, and swapchain present. It does not add a production frame loop, editor, asset loading, runtime shader compilation, world integration, render graph, differential compositor, gameplay logic, `Aurelian.Host`, VMA/VMASharp, Vortice, new package dependencies, CodeReferences changes, or vendor changes.

## 3. Sample project shape

The sample lives at `samples/Aurelian.VisibleTriangle/` with namespace `Aurelian.VisibleTriangle`. It references:

* `src/Aurelian.Core/Aurelian.Core.csproj`
* `src/Aurelian.Graphics/Aurelian.Graphics.csproj`
* `src/Aurelian.Runtime/Aurelian.Runtime.csproj`
* `src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj`

It does not reference test projects, shader tooling, assets, asset tooling, or world projects directly.

## 4. Prepared visible ownership usage

The sample creates all Vulkan/window/swapchain/command/texture/pipeline/compositor resources externally, then validates an `AurelianPreparedGraphicsSubsystem` using `AurelianEngineGraphicsOptions.PreparedVisible`, the `VulkanCompositorMechanismAdapter`, and a sample-local presentation mechanism wrapper. The `AurelianEngine` is constructed with `PreparedVisible` graphics options, but Core still does not allocate or own Vulkan resources.

## 5. Frame pump/compositor path

The executable follows the motivating path:

```text
Core frame pump
  -> Runtime Dominatus compositor policy
  -> Core compositor bridge
  -> Graphics Vulkan compositor mechanism
  -> offscreen triangle
  -> compositor passthrough
  -> swapchain present
```

The triangle is rendered to an offscreen color target first. `AurelianFramePump.RunOneFrameAsync(...)` receives compositor policy facts declaring that offscreen output ready and targets the acquired swapchain image. The Vulkan compositor passthrough then copies the offscreen output into the presentation target before the sample presents.

## 6. SPIR-V fixture policy

The sample duplicates the tiny static SPIR-V byte arrays locally from the existing graphics-test fixture. This avoids a test-project dependency and avoids runtime shader compilation. The sample does not reference `Aurelian.Shaders`, shader compiler packages, asset manifests, or shader artifact tooling.

## 7. Window/swapchain behavior

`VulkanSwapchainCreateOptions.Visible` defaults to `false` so tests remain hidden/headless-safe. The A62 sample passes `Visible: true`, creates a 640x480 window titled `Aurelian Visible Triangle`, presents once, and pumps window events briefly unless `--no-hold` is passed. If Vulkan presentation or windowing is unavailable, the sample prints diagnostics and exits nonzero.

## 8. Build/test validation

Validation commands run for A62:

* `dotnet build Aurelian.slnx -c Debug`
* `dotnet test Aurelian.slnx -c Debug`
* `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug`

The sample itself is not intended as a CI test because it may open a visible window and requires Vulkan presentation.

## 9. Boundary checks

Boundary checks confirm the sample does not reference shader tooling/DXC packages or forbidden host/VMA/Vortice patterns, and project references remain constrained to the intended sample dependencies. The broader repository still contains historical documentation/test references to deferred or explicitly forbidden names; A62 did not add production dependencies for them.

## 10. Deferred features

Deferred features remain:

* production continuous engine frame loop;
* engine-owned graphics lifecycle;
* input/window event abstractions;
* asset/shader artifact bridge;
* runtime shader compilation;
* render graph;
* world integration;
* differential compositor;
* presentation semaphore integration/recreation policies.

## 11. Next recommendation

A63 — Minimal production frame loop M0.
