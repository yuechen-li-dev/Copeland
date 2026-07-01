# A59 — Minimal Core frame pump M0

## 1. Files changed

- `src/Aurelian.Core/Engine/Frames/AurelianFrameId.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameInput.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameResult.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameStatus.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameDiagnostic.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameDiagnosticCodes.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFramePumpOptions.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFrameCompositorInputs.cs`
- `src/Aurelian.Core/Engine/Frames/AurelianFramePump.cs`
- `tests/Aurelian.Core.Tests/AurelianFramePumpM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0059-a59-minimal-frame-pump-m0.md`

## 2. Task scope

A59 implements Minimal Core frame pump M0. The scope is one-frame orchestration only: Core accepts explicit frame input and compositor policy facts, invokes Runtime compositor policy once, bridges dispatch actuation through the existing Core compositor bridge, and returns a typed frame result.

A59 does not create a continuous frame loop, window loop, swapchain, Vulkan resources, render graph, scheduler/threading system, asset loading path, world update integration, differential compositor, `Aurelian.Host`, new packages, CodeReferences changes, or vendor changes.

## 3. Frame pump model

`AurelianFramePump` is constructed with an `AurelianEngine`, a `CompositorActuationBridge`, and optional `AurelianFramePumpOptions`. M0 defaults to requiring the engine to be started before a frame can run.

`RunOneFrameAsync(...)` runs exactly one logical frame. It creates a local per-frame Dominatus `ActuatorHost`, registers a narrow compositor dispatch handler, and calls `CompositorPolicySession.RunOnceAsync(...)` with the supplied compositor facts. The pump does not own a persistent world, actuator host, frame loop, or graphics setup.

## 4. Frame input/result/diagnostics

Frame identity is represented by `AurelianFrameId`, including invariant `ToString()` formatting and `Next()` increment behavior.

Frame input is explicit and boring: `AurelianFrameInput` carries the frame id and already-built `CompositorPolicyFacts`. M0 therefore avoids world extraction, render extraction, asset loading, render graph construction, or backend resource ownership.

Frame output is `AurelianFrameResult`, with `Completed`, `Waiting`, `Rejected`, and `Failed` statuses plus typed diagnostics:

- `ACF1001` — engine not started;
- `ACF1002` — missing frame input;
- `ACF1003` — missing compositor facts;
- `ACF1004` — compositor waiting;
- `ACF1005` — compositor rejected;
- `ACF1006` — compositor failed;
- `ACF1007` — frame cancelled.

## 5. Compositor policy bridge usage

A59 uses the existing policy/mechanism bridge:

```text
AurelianFrameInput
  -> CompositorPolicyFacts
  -> CompositorPolicySession.RunOnceAsync(...)
  -> CompositorDispatchAct
  -> CompositorActuationBridge.HandleAsync(...)
  -> ICompositorMechanism.DispatchAsync(...)
  -> CompositorPolicyResult
  -> AurelianFrameResult
```

This keeps the frame pump mechanism-agnostic. The pump knows only the neutral Core bridge and `ICompositorMechanism`; it does not call `Aurelian.Graphics` Vulkan APIs directly.

## 6. Tests added

`tests/Aurelian.Core.Tests/AurelianFramePumpM0Tests.cs` covers:

- invariant frame id formatting;
- frame id increment;
- rejection when the engine is not started;
- completed frame when compositor dispatch succeeds;
- waiting frame when required compositor outputs are pending;
- rejection for unsupported compositor policy;
- failed frame when compositor dispatch fails;
- cancellation mapping;
- frame pump source boundary against Vulkan/window/swapchain creation terms.

The tests use a fake `ICompositorMechanism`, not Vulkan objects.

## 7. Boundary checks

Boundary checks were run with ripgrep for forbidden frame pump and dependency terms. The frame pump source under `src/Aurelian.Core/Engine/Frames` contains no Vulkan/window/swapchain setup. Existing Core Vulkan adapter and existing Vulkan-named tests remain expected A58 artifacts outside the frame pump implementation.

Project reference checks preserve the intended A58 shape: Core may reference Graphics, Runtime, and Rendering.Contracts; Runtime remains graphics-free; Graphics remains runtime/Dominatus-free; Rendering.Contracts remains neutral.

## 8. Validation results

Validation included solution build/test commands, targeted Core/Runtime/Integration tests, and dependency-boundary ripgrep checks. Results are recorded in the final A59 handoff.

## 9. Deferred features

Deferred features:

- no continuous frame loop;
- no window loop;
- no swapchain creation;
- no Vulkan setup inside the frame pump;
- no visible sample executable;
- no render graph;
- no world update/extraction integration;
- no asset loading;
- no scheduler/threading system;
- no persistent engine runtime/actuator host;
- no differential compositor;
- no `Aurelian.Host` project.

## 10. Next recommendation

A60 — Vulkan-backed visible frame pump integration M0.

A58 and earlier graphics milestones prove the graphics pieces, and A59 now proves the abstract one-frame Core pump. The next convergent step is to connect the one-frame pump to an externally prepared Vulkan visible path without moving window/swapchain/Vulkan setup into the frame pump itself.
