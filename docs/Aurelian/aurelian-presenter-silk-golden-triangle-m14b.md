# Aurelian Presenter/Silk.NET Golden Triangle M14b

## Purpose

M14b implements the first real visible-pixel Aurelian runtime golden path in the monorepo.

The goal is narrow and explicit:

- use Presenter architecture rather than inventing an Aurelian-only host
- make Silk.NET another Presenter backend
- keep Aurelian responsible for rendering
- keep Presenter responsible for window, frame pump, input polling, and present
- let a developer run one explicit sample command and reach a real Vulkan present path

## Implemented path

The M14b sample path is:

```text
Presenter/Silk.NET backend
  -> required Vulkan instance extensions
  -> Aurelian Vulkan plant initialization
  -> Presenter-owned window-backed swapchain
  -> prepared offscreen triangle render
  -> compositor passthrough
  -> Vulkan presentation mechanism
  -> Presenter present
```

The runnable entry point remains `samples/Aurelian.VisibleTriangle`.

## Presenter backend shape

M14b adds a minimal Presenter-shaped backend inside the sample:

- `SilkNetPresenterBackend`
- `SilkNetFrameInputProvider`

This backend is intentionally narrow:

- create and initialize the Silk.NET Vulkan window
- report required Vulkan instance extensions before plant creation
- own event pumping and close-request observation
- provide the initialized window to swapchain creation

It is backend-shaped rather than renderer-shaped. The backend does not create the triangle pipeline, shaders, compositor, or engine state.

## Vulkan presentation bridge

M14b also adds a reusable Vulkan presentation mechanism in Core:

- `src/Aurelian.Core/Graphics/Vulkan/Presentation/VulkanPresentationMechanism.cs`

This is the missing `IPresentationMechanism` bridge for the visible path. It dequeues the acquired swapchain image index for the completed frame, calls `AurelianVulkanSwapchain.Present(...)`, records diagnostics, and lets the Presenter backend pump events after present.

M14b does not add a generalized scheduler or multi-frame presentation manager.

## Surface and extension fix

The direct blocker from the prior audit was real: `VulkanPlantInitializer` previously asked `Silk.NET.Windowing` for required surface extensions before a window had been initialized, which produced `AGV1011`.

M14b fixes that by changing the order:

1. create and initialize the Presenter/Silk.NET window first
2. ask the Presenter backend for required Vulkan instance extensions
3. pass those extensions into `VulkanPlantInitializer`
4. create the swapchain against the already initialized Presenter window

`VulkanSwapchainFactory` now supports an externally owned initialized `IWindow` in addition to its existing self-owned window path.

## Triangle and compositor path

The triangle render path stays intentionally small:

- checked-in shader artifacts are still loaded through `Aurelian.Assets`
- one static offscreen triangle is rendered through existing Vulkan abstractions
- `VulkanCompositorPassthrough` copies the plant output into the swapchain target
- the Presenter path presents that swapchain image

This proves the runtime path without changing compiler defaults.

## Boundaries preserved

M14b does not:

- make `VD-MIR` the default
- remove the direct AST-to-HLSL path
- extract Copeland packages
- add Slang or PTX
- split shader MIR from kernel MIR
- integrate Machina or Oblivion
- add a full host framework or engine shell
- make CI depend on a real Vulkan surface/window

The sample still loads the existing checked-in runtime shader artifacts. M14b is runtime plumbing and Presenter integration, not a compiler-default expansion milestone.

## Run command

```powershell
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -- --presenter silk --frames 120
```

Useful flags:

- `--no-hold`
- `--validation`
- `--frames N`

## Validation note

In the current workspace on July 1, 2026, the explicit Presenter/Silk.NET sample path successfully built, acquired a swapchain image, dispatched the compositor passthrough, and presented through Vulkan with `--presenter silk --frames 1 --no-hold`.

That proves the runtime golden-path slice is wired end to end. Human visual confirmation of the desktop window remains a manual check.
