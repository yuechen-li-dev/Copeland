# A68 — Visible sample window event pump/input M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSurface.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleWindowState.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleFrameInputProvider.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleSamplePresentationMechanism.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleSampleFrame.cs`
- `samples/Aurelian.VisibleTriangle/Program.cs`
- `samples/Aurelian.VisibleTriangle/README.md`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0068-a68-visible-sample-window-event-pump-input-m0.md`

## 2. Task scope

A68 is sample window/event-pump M0 only. The goal is to keep the visible triangle sample finite while pumping window events during finite multi-frame execution and stopping early when the user closes the sample window.

The implemented path remains:

```text
sample-owned prepared Vulkan setup
  -> AurelianEngine
  -> AurelianRuntimeSession
  -> AurelianRuntimeTickFrameStep
  -> AurelianFrameLoop
  -> sample-local frame input provider
  -> runtime tick
  -> frame pump
  -> runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor passthrough
  -> sample-local presentation mechanism
```

No production input system, production host, engine-owned window lifecycle, unbounded game loop, scheduler/threading system, asset loading, runtime shader compilation, render graph, VMA/VMASharp, Vortice, new packages, CodeReferences changes, or vendor changes were added.

## 3. Window event pump behavior

`AurelianVulkanSurface` already exposed `PumpEvents()` over the owned Silk.NET window. A68 keeps that narrow presentation-owner surface and adds `IsCloseRequested`, backed by Silk.NET `IWindow.IsClosing` and the `Closing` event. The raw `IWindow` remains private and is not exposed publicly.

The sample now owns `VisibleTriangleWindowState`, which records whether close was requested and how many event pumps have been performed. This state is sample-local and is passed to the input provider and presentation mechanism.

Event pumping happens in two sample-local places:

1. before each frame input/acquire in `VisibleTriangleFrameInputProvider.GetNextFrameInputAsync`; and
2. after each swapchain present in `VisibleTriangleSamplePresentationMechanism.Present`.

The short post-loop visible-window hold also pumps through the same sample window state and stops if close is requested.

## 4. Close request behavior

When the window requests close before the next acquire, `VisibleTriangleFrameInputProvider` records a diagnostic and returns `null`. This lets `AurelianFrameLoop` stop through its existing `InputProviderCompleted` path instead of throwing or adding window-specific behavior to Core.

`Program` prints:

```text
Window close requested; stopped frame loop.
```

when the shared sample window state observes a close request.

If close is requested after the final selected frame has already completed, no special frame-loop behavior is required; the sample exits normally after reporting diagnostics.

## 5. Sample frame provider/presentation changes

`VisibleTriangleFrameInputProvider` now receives the sample-local window state and optional surface. It pumps events before acquire, checks `CloseRequested`, and only acquires a swapchain image when the finite loop still needs another frame and the window has not requested close.

`VisibleTriangleSamplePresentationMechanism` now receives the same sample-local window state and optional surface. It still dequeues the exact image acquired for the completed frame, presents it, pumps window events, and records presentation diagnostics. If close is observed after present, it records an additional sample diagnostic; the following input-provider call will stop the loop cleanly unless the finite frame cap has already ended the loop.

## 6. CLI/output behavior

The CLI remains unchanged:

- `--frames N` selects a positive finite frame count;
- default frame count is `3`;
- requested values above `300` are capped;
- `--validation` enables Vulkan validation;
- `--no-hold` skips the short post-loop responsive-window hold.

Output now identifies the run as A68, prints that events are pumped before acquire and after present, reports the frame-loop status/stop reason and attempted/completed counts, reports the window event pump count and close status, and prints clear input/presentation/window diagnostics.

## 7. Build/test validation

Validation commands for this milestone:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug`
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug`

The sample itself is not run as a normal test because it requires a presentation-capable local windowing environment.

## 8. Boundary checks

Boundary commands for this milestone:

- `rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" samples/Aurelian.VisibleTriangle src/Aurelian.Graphics -g '*.cs' -g '*.csproj' || true`
- `rg -n "Aurelian.Host|ServiceLocator|Singleton|Activator|GetType\(|Type\.|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" samples/Aurelian.VisibleTriangle src tests -g '*.cs' -g '*.csproj' || true`
- `rg -n "PumpEvents|CloseRequested|IsCloseRequested|ShouldClose|frame-delay|Delay|Thread.Sleep|IWindow|Window.Create" samples/Aurelian.VisibleTriangle src/Aurelian.Graphics/Vulkan/Presentation -g '*.cs' || true`
- `rg -n "Thread.Sleep" src/Aurelian.Core src/Aurelian.Runtime src/Aurelian.Graphics -g '*.cs' || true`
- `git status --short`

Expected results: event pump and close symbols appear only in the visible sample and the minimal Vulkan presentation surface wrapper; no runtime shader compiler dependency is added to the sample or Graphics; no host/service-locator/singleton/reflection/VMA/Vortice/vendor/reference-code expansion is added; no `Thread.Sleep` appears in production Core/Runtime/Graphics.

## 9. Deferred features

Deferred work remains explicit:

- production input system;
- Escape-to-exit keyboard handling, because adding it cleanly would require input plumbing beyond this M0 surface/window close path;
- infinite/full game loop;
- `Aurelian.Host`;
- engine-owned graphics/window lifecycle;
- swapchain recreation;
- asset loading;
- runtime shader compilation;
- SDSL-V/DXC dependency in the sample or Graphics;
- world integration;
- render graph;
- scheduler/threading system;
- VMA/VMASharp;
- Vortice;
- new packages;
- CodeReferences/vendor modifications.

## 10. Next recommendation

**A69 — Asset/shader artifact bridge for sample M0**

Rationale: the sample can now run/present multiple finite frames and respond to window close. The next major usability improvement is to stop relying on sample-local static SPIR-V fixtures and move toward the real shader artifact path without adding runtime shader compilation.
