# A67 — Visible sample multi-frame acquire/present M0 audit

## 1. Files changed

- `samples/Aurelian.VisibleTriangle/Program.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleFrameInputProvider.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleFrameState.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleSampleFrame.cs`
- `samples/Aurelian.VisibleTriangle/VisibleTriangleSamplePresentationMechanism.cs`
- `samples/Aurelian.VisibleTriangle/README.md`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0067-a67-visible-sample-multiframe-acquire-present-m0.md`

## 2. Task scope

A67 is limited to finite multi-frame acquire/present in the visible triangle sample. It keeps the existing prepared Vulkan setup, `AurelianEngine`, Dominatus-backed `AurelianRuntimeSession`, `AurelianRuntimeTickFrameStep`, `AurelianFrameLoop`, Core compositor bridge, and Vulkan compositor passthrough path.

The milestone does not introduce an infinite loop, production host, engine-owned graphics lifecycle, asset loading, runtime shader compilation, world integration, render graph, input system, scheduler/threading system, VMA/VMASharp, Vortice, new packages, CodeReferences changes, or vendor modifications.

## 3. Multi-frame sample flow

The sample now defaults to three frames and accepts `--frames N` for positive finite values capped at 300. Setup prepares the Vulkan plant, window/surface/swapchain, offscreen color target, command helpers, compositor adapter, engine, and frame pump once. Runtime is started by the sample, then `AurelianFrameLoop` runs from the sample's starting frame ID for the selected finite frame count.

Each frame follows this path:

```text
AurelianFrameLoop
  -> VisibleTriangleFrameInputProvider.GetNextFrameInputAsync
  -> swapchain.AcquireNextImage
  -> AurelianRuntimeTickFrameStep
  -> AurelianFramePump
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor passthrough
  -> VisibleTriangleSamplePresentationMechanism.PresentAsync
  -> swapchain.Present(acquired image index)
```

## 4. Per-frame acquire behavior

`VisibleTriangleFrameInputProvider` owns the sample-local acquire step. For each requested frame it calls `AurelianVulkanSwapchain.AcquireNextImage()`, accepts `Acquired` and `Suboptimal` results that carry an image index, records the frame state, enqueues the acquired image index for presentation, and returns `AurelianFrameInput`.

If acquire returns unavailable, out-of-date, rejected, failed, or no image index, the provider records a diagnostic and returns `null`. That lets `AurelianFrameLoop` stop through its existing input-provider completion path without moving Vulkan-specific error handling into Core.

## 5. Per-frame presentation behavior

`VisibleTriangleSamplePresentationMechanism` is sample-local and owns a FIFO queue of acquired image indices. The input provider enqueues after successful acquire; `PresentAsync` dequeues when the frame loop presents a completed frame. This is an M0 sequential contract: the queue maps completed frames to acquired images because the frame loop runs one frame at a time.

`Presented` and `Suboptimal` are treated as success. Other present statuses are converted to an exception so the frame loop reports a presentation failure with clear diagnostics. The mechanism also pumps window events after each present when the sample has a surface.

## 6. Plant output ref strategy

The offscreen triangle remains static for A67. Setup draws the triangle once into a single offscreen Vulkan texture, then creates a finite `VulkanPlantOutputImageSet` with one `VulkanPlantOutputImage` wrapper for each planned frame ID. Each wrapper has a distinct `PlantOutputRef` matching that frame ID and the same `triangle.offscreen` image ID, and each wrapper points to the same offscreen texture.

This avoids changing production compositor resolvers or introducing a dynamic output resolver. A future unbounded or animated path can replace the finite wrapper set with an explicit provider/resolver design.

## 7. Runtime/frame loop behavior

The sample continues to use `AurelianRuntimeSession`, `AurelianRuntimeSessionTickerAdapter`, `AurelianRuntimeTickFrameStep`, and `AurelianFrameLoop`. Runtime tick runs before the frame pump on each acquired frame. Core remains responsible for orchestration only; it receives prepared frame inputs and a prepared presentation mechanism but does not create or own Vulkan/window/swapchain resources.

## 8. Build/test validation

Validation commands for this milestone:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug`

The sample itself is not run as a normal test because it requires a presentation-capable local windowing environment.

## 9. Boundary checks

Boundary commands for this milestone:

- `rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" samples/Aurelian.VisibleTriangle src/Aurelian.Graphics -g '*.cs' -g '*.csproj' || true`
- `rg -n "Aurelian.Host|ServiceLocator|Singleton|Activator|GetType\(|Type\.|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" samples/Aurelian.VisibleTriangle src tests -g '*.cs' -g '*.csproj' || true`
- `rg -n "AurelianFrameLoop|AurelianRuntimeSession|AurelianRuntimeTickFrameStep|AurelianRuntimeSessionTickerAdapter|MaxFrames|AcquireNextImage|PresentAsync" samples/Aurelian.VisibleTriangle -g '*.cs' || true`
- `rg -n "ProjectReference" samples/Aurelian.VisibleTriangle src/Aurelian.Core src/Aurelian.Runtime src/Aurelian.Graphics src/Aurelian.Rendering.Contracts -g '*.csproj'`
- `git status --short`

Expected results: no new shader compiler dependency in the sample or Graphics, no host/service-locator/singleton/reflection/VMA/Vortice/vendor/reference-code expansion, continued use of the Core frame loop/runtime session path, visible sample per-frame acquire/present symbols, and no new package edges.

## 10. Deferred features

Deferred work remains explicit:

- infinite/full game loop;
- `Aurelian.Host`;
- engine-owned graphics lifecycle;
- swapchain recreation;
- asset loading;
- runtime shader compilation;
- SDSL-V/DXC dependency in the sample or Graphics;
- world integration;
- render graph;
- input system;
- scheduler/threading system;
- VMA/VMASharp;
- Vortice;
- new packages;
- CodeReferences/vendor modifications.

## 11. Next recommendation

**A68 — Sample window event pump/input M0**

Rationale: A67 can run finite visible frames and now acquires/presents per frame, but the sample still has only a minimal event pump after present/hold and lacks deliberate close handling or input reporting. The next convergence step should stay sample-local and add a clearer window event/input pump without turning it into an infinite production host.
