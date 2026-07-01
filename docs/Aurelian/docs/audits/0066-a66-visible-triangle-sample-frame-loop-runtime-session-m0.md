# A66 — Visible triangle sample frame loop + runtime session M0

## 1. Files changed

* Updated `samples/Aurelian.VisibleTriangle/Program.cs` to replace the manual top-level one-frame pump call with a finite `AurelianFrameLoop` that uses a Dominatus-backed runtime tick frame step and prepared presentation mechanism.
* Added `samples/Aurelian.VisibleTriangle/VisibleTriangleFrameInputProvider.cs` as the sample-local `IAurelianFrameInputProvider` for the prepared frame input.
* Updated `samples/Aurelian.VisibleTriangle/README.md` to describe the A66 frame-loop/runtime-session sample path and one-frame M0 limitation.
* Updated `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md` with the A66 status and dependency boundary notes.
* Added this audit report.

## 2. Task scope

A66 is a sample conversion milestone. It changes the human-facing visible triangle executable to demonstrate the current engine spine while preserving sample-owned Vulkan/window/swapchain/resource setup.

A66 does not add an infinite game loop, `Aurelian.Host`, engine-owned graphics lifecycle, asset loading, runtime shader compilation, world integration, render graph, input system, scheduler/threading system, new package dependency, VMA/VMASharp, Vortice, CodeReferences modification, or vendor modification.

## 3. Sample flow before/after

Before A66, the sample followed the A62 flow:

```text
sample-owned prepared Vulkan setup
  -> AurelianEngine
  -> AurelianFramePump.RunOneFrameAsync(...)
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor mechanism
  -> manual present
```

After A66, the executable follows:

```text
sample-owned prepared Vulkan setup
  -> AurelianEngine
  -> AurelianRuntimeSession
  -> AurelianRuntimeSessionTickerAdapter
  -> AurelianRuntimeTickFrameStep
  -> AurelianFrameLoop
  -> runtime tick each frame
  -> frame pump
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor mechanism
  -> present through IPresentationMechanism
```

The sample still prepares the offscreen triangle and one acquired swapchain target externally before constructing the frame-loop path.

## 4. Frame loop usage

`Program.cs` now constructs `AurelianFrameLoop` with the existing sample `AurelianFramePump`, a sample-local `VisibleTriangleFrameInputProvider`, the prepared `VisibleTriangleSamplePresentationMechanism`, finite `AurelianFrameLoopOptions`, and an `AurelianRuntimeTickFrameStep`.

The sample runs the loop with `RunAsync(sample.Input.FrameId)` and reports attempted/completed frames, loop status, stop reason, loop diagnostics, frame status, compositor dispatch status, presentation state, and per-iteration runtime tick status.

## 5. Runtime session usage

The sample creates and starts `AurelianRuntimeSession` before entering the frame loop. It adapts the session through `AurelianRuntimeSessionTickerAdapter`, then wraps that adapter in `AurelianRuntimeTickFrameStep` so the Core frame loop ticks Runtime before the frame pump.

The runtime session is stopped from `finally` when it was successfully started. Runtime remains graphics-free; the sample owns the composition of Core/Runtime/Graphics seams explicitly.

## 6. Presentation behavior

A66 keeps the A62 prepared presentation mechanism and uses `AurelianFrameLoopOptions.PresentAfterCompletedFrame = true` so presentation happens after a successful frame iteration through `IPresentationMechanism`.

The visible sample currently supports one frame by default because the setup acquires one swapchain image and bakes that image index into the prepared `PresentationTargetRef` and sample presentation mechanism. The `--frames` option is parsed, but values other than `1` are capped with a diagnostic. Safe per-frame acquire/presentation target refresh is intentionally deferred.

## 7. Build/test validation

Validation commands for A66:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
```

## 8. Boundary checks

Boundary checks for A66:

```bash
rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" samples/Aurelian.VisibleTriangle src/Aurelian.Graphics -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Host|ServiceLocator|Singleton|Activator|GetType\\(|Type\\.|Vortice|VMASharp|Vma|CodeReferences|Stride\\.|Machina\\.|WyrmCoil|Copeland" samples/Aurelian.VisibleTriangle src tests -g '*.cs' -g '*.csproj' || true
rg -n "AurelianFrameLoop|AurelianRuntimeSession|AurelianRuntimeTickFrameStep|AurelianRuntimeSessionTickerAdapter|RunOneFrameAsync" samples/Aurelian.VisibleTriangle -g '*.cs' || true
rg -n "ProjectReference" samples/Aurelian.VisibleTriangle src/Aurelian.Core src/Aurelian.Runtime src/Aurelian.Graphics src/Aurelian.Rendering.Contracts -g '*.csproj'
```

Expected source scan notes: the visible sample has no shader compiler project/package reference and no new host/service-locator/VMA/Vortice/reference-code dependency. Broad scans may still report pre-existing allowed mentions in documentation, tests, or backend code unrelated to the sample conversion.

## 9. Deferred features

Deferred intentionally:

* multi-frame swapchain acquire/presentation target refresh;
* infinite or continuous game loop;
* `Aurelian.Host`;
* moving Vulkan/window/swapchain ownership into Core frame loop;
* engine-owned graphics lifecycle;
* asset loading;
* runtime shader compilation;
* world integration and render extraction;
* render graph;
* input system;
* scheduler/threading system;
* VMA/VMASharp and Vortice.

## 10. Next recommendation

A67 — Multi-frame visible sample acquire/present M0.

Rationale: A66 now uses the real Core frame-loop and Dominatus-backed runtime-session path, but remains one-frame because the sample still has one prepared acquired swapchain target. The next usability step is a safe per-frame acquire/present flow that refreshes `AurelianFrameInput`, `PresentationTargetRef`, and presentation image index for each finite visible sample frame.
