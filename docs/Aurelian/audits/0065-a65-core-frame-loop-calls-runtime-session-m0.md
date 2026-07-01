# A65 — Core frame loop calls runtime session M0

## 1. Files changed

- `src/Aurelian.Core/Engine/Runtime/IAurelianRuntimeTicker.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeSessionTickerAdapter.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStep.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStepResult.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStepStatus.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStepDiagnostic.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStepDiagnosticSeverity.cs`
- `src/Aurelian.Core/Engine/Runtime/AurelianRuntimeTickFrameStepDiagnosticCodes.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameLoop.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameLoopIterationResult.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameLoopOptions.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameLoopDiagnosticCodes.cs`
- `tests/Aurelian.Core.Tests/AurelianRuntimeFrameStepM0Tests.cs`
- `tests/Aurelian.Core.Tests/AurelianFrameLoopRuntimeTickM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/world-model-doctrine.md`

## 2. Task scope

A65 connects the A63 Core frame loop to the A64 Dominatus-backed runtime session through a small Core-owned runtime tick seam. The intended M0 path is now:

```text
AurelianFrameLoop
  -> AurelianRuntimeTickFrameStep.RunAsync(...)
  -> IAurelianRuntimeTicker.TickAsync(...)
  -> AurelianRuntimeSession.TickAsync(...)
  -> AurelianFramePump.RunOneFrameAsync(...)
```

The scope is orchestration only. Core does not reimplement runtime behavior, world simulation, render extraction, processor systems, Vulkan/window/swapchain setup, or parallel runtime runner integration.

## 3. Runtime ticker seam

`IAurelianRuntimeTicker` lives in `Aurelian.Core.Engine.Runtime` because Core owns frame orchestration and needs only a minimal async tick contract over `AurelianRuntimeTickInput` and `AurelianRuntimeTickResult`.

`AurelianRuntimeSessionTickerAdapter` wraps an `AurelianRuntimeSession` without making `Aurelian.Runtime` reference `Aurelian.Core`. This avoids a project dependency cycle while still proving the real session path.

`AurelianRuntimeTickFrameStep` validates its ticker and frame delta, creates `AurelianRuntimeTickInput` with `TickIndex = frameId.Value`, calls the ticker, and maps runtime tick statuses to Core frame-step statuses and diagnostics.

## 4. Frame loop integration behavior

`AurelianFrameLoop` accepts an optional `AurelianRuntimeTickFrameStep`. When absent, existing frame-loop behavior remains unchanged and iteration results contain a null runtime tick result.

When present, the frame loop:

1. requests frame input from the provider;
2. counts the frame as attempted;
3. runs the runtime tick step using the frame id and loop default delta time;
4. stops before the frame pump if runtime ticking is rejected, failed, or cancelled;
5. runs the existing frame pump only after a successful runtime tick;
6. records the successful runtime tick step result on the frame-loop iteration.

Loop-level diagnostics map runtime-step failures to `ACFL1008 RuntimeTickFailed`, `ACFL1009 RuntimeTickRejected`, and `ACFL1010 RuntimeTickCancelled`.

## 5. Dominatus-backed runtime test

`AurelianFrameLoop_RunAsync_WithRealRuntimeSession_TicksDominatusRuntime` starts a real `AurelianRuntimeSession`, wraps it in `AurelianRuntimeSessionTickerAdapter`, runs a one-frame Core frame loop with a fake compositor mechanism, and asserts both the runtime tick and frame pump complete successfully.

This test uses no graphics/Vulkan resources. It proves the real path reaches the Dominatus-backed runtime session before the existing frame pump.

## 6. Boundary checks

A65 keeps the boundaries from A63/A64:

- `Aurelian.Runtime` remains graphics-free.
- `Aurelian.Graphics` remains runtime/Dominatus-free.
- `Aurelian.Rendering.Contracts` remains neutral.
- `Aurelian.Core.Engine.Runtime` contains only orchestration seam code and no window/swapchain/Vulkan resource creation.
- No packages were added.
- No vendor or CodeReferences files were modified.
- `ParallelAiWorldRunner` remains deferred behind the runtime runner seam.

## 7. Validation results

Validation performed for this audit:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test Aurelian.slnx -c Debug`
- `dotnet test tests/Aurelian.Core.Tests/Aurelian.Core.Tests.csproj -c Debug`
- `dotnet test tests/Aurelian.Runtime.Tests/Aurelian.Runtime.Tests.csproj -c Debug`
- `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug`
- required dependency and forbidden-boundary `rg` checks from the A65 task brief

## 8. Deferred features

A65 deliberately defers:

- visible-triangle sample conversion to the full frame-loop/runtime-session path;
- persistent runtime session ownership in engine options;
- real world tick facts beyond the neutral M0 runtime tick act;
- render extraction from runtime/world state;
- processor systems;
- parallel runtime runner integration;
- any window, surface, swapchain, or Vulkan creation from the frame loop;
- any graphics dependency in Runtime.

## 9. Next recommendation

Recommended next milestone:

```text
A66 — Visible triangle sample uses frame loop + runtime session M0
```

Rationale: A62 proved the visible triangle through a manual one-frame pump, A63 added the Core frame loop, A64 added the Dominatus-backed runtime session, and A65 connects the frame loop to the runtime session. The next visible demonstration should use the full Core frame loop plus runtime session path without adding new runtime behavior or graphics ownership changes.
