# A63 — Minimal production frame loop M0

## 1. Files changed

* Added Core frame-loop source files under `src/Aurelian.Core/Engine/Frames/`:
  * `AurelianFrameLoop.cs`
  * `AurelianFrameLoopOptions.cs`
  * `AurelianFrameLoopResult.cs`
  * `AurelianFrameLoopStatus.cs`
  * `AurelianFrameLoopDiagnostic.cs`
  * `AurelianFrameLoopDiagnosticCodes.cs`
  * `AurelianFrameLoopStopReason.cs`
  * `IAurelianFrameInputProvider.cs`
  * `AurelianFrameLoopIterationResult.cs`
  * `DelegateFrameInputProvider.cs`
* Added `tests/Aurelian.Core.Tests/AurelianFrameLoopM0Tests.cs`.
* Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/compositor-policy-mechanism-split.md`, and `docs/architecture/dependency-policy.md`.

## 2. Task scope

A63 implements a minimal production frame loop abstraction in `Aurelian.Core` only. It is an orchestration layer over the existing one-frame `AurelianFramePump`; it is not an engine host, graphics lifecycle owner, Vulkan bootstrapper, window/event loop, scheduler, renderer facade, asset path, runtime shader compiler, or continuous unbounded game loop.

## 3. Frame loop model

`AurelianFrameLoop` is constructed with an existing `AurelianFramePump`, an `IAurelianFrameInputProvider`, an optional `IPresentationMechanism`, and `AurelianFrameLoopOptions`. It runs prepared frames by repeatedly asking the provider for the next `AurelianFrameInput`, calling `AurelianFramePump.RunOneFrameAsync(...)`, recording an `AurelianFrameLoopIterationResult`, and stopping on max frames, provider completion, frame failure, rejection, or cancellation.

M0 defaults to `MaxFrames = 1`, so the default path is finite. `MaxFrames = null` is allowed for provider/cancellation-driven execution, but tests use finite loops except where provider completion is specifically under test.

## 4. Input provider model

`IAurelianFrameInputProvider` exposes:

```csharp
ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
    AurelianFrameId frameId,
    CancellationToken cancellationToken = default);
```

The provider returns prepared frame input for the requested frame id or `null` to request a clean loop stop. The interface contains no Vulkan, window, surface, swapchain, or graphics-resource types. `DelegateFrameInputProvider` provides a small delegate-backed implementation for simple callers/tests.

## 5. Presentation mechanism use

Presentation remains optional and abstract. If `PresentAfterCompletedFrame` is true and an `IPresentationMechanism` is provided, the loop calls `PresentAsync(...)` only after a successful frame result. If no presentation mechanism is supplied, the frame can still complete and the iteration records `Presented = false`.

A presentation exception is converted to a failed loop result with `ACFL1006 PresentationFailed`. The loop does not create presentation mechanisms, swapchains, windows, or graphics resources.

## 6. Stop/cancellation behavior

The loop returns typed status and stop reason values:

* `Completed` / `MaxFramesReached` when the finite frame budget is reached.
* `Completed` / `InputProviderCompleted` when the provider returns `null`.
* `Failed` / `FrameFailed` when a frame fails and `StopOnFrameFailure` is true.
* `Cancelled` / `Cancelled` when the provided cancellation token is canceled.
* `Rejected` / `Rejected` for missing frame pump, missing input provider, or invalid `MaxFrames`.

The result also reports frames attempted, frames completed, per-iteration results, and diagnostics.

## 7. Tests added

`AurelianFrameLoopM0Tests` covers:

* missing frame pump rejection;
* missing input provider rejection;
* invalid max-frame rejection;
* one-frame completion;
* multi-frame completion until max frames;
* provider-completed stop;
* presentation after completed frame;
* no-presentation-mechanism completion;
* stop-on-frame-failure behavior;
* continue-after-frame-failure behavior;
* cancellation returning a cancelled loop result;
* source boundary check that frame-loop code does not create Vulkan/window resources.

Tests use fake frame input providers, fake compositor mechanisms, and a fake presentation mechanism. No Vulkan, window, surface, or swapchain resources are created by the tests.

## 8. Boundary checks

The A63 validation commands include full solution build/test, Core test build/test, visible triangle sample build, and source scans for disallowed ownership/dependency terms. The frame loop source remains in `Aurelian.Core.Engine.Frames` and contains no Vulkan/window/swapchain setup or package changes.

## 9. Validation results

Validation completed successfully with:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Core.Tests/Aurelian.Core.Tests.csproj -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
```

The requested source-boundary scans were also run. The frame-loop source remains free of Vulkan/window/swapchain creation. Some broad repository scans report pre-existing allowed graphics/backend terminology in graphics tests and backend source, so those scans are diagnostic rather than hard failures.

## 10. Deferred features

Deferred intentionally:

* converting `samples/Aurelian.VisibleTriangle` to use `AurelianFrameLoop`;
* continuous unbounded runtime loop;
* window event pumping/input abstraction;
* engine-owned graphics lifecycle;
* creation of Vulkan/window/swapchain/surface resources;
* compositor mechanism creation;
* render graph/world/assets integration;
* runtime shader compilation;
* scheduler/threading/framerate limiter;
* `Aurelian.Host`.

## 11. Next recommendation

A64 — Visible triangle sample uses frame loop M0.
