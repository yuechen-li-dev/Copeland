# A55 — Runtime Dominatus compositor policy M0

## 1. Files changed

- `src/Aurelian.Runtime/Compositor/CompositorDispatchAct.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyFacts.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyDecision.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyStatus.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyDiagnostic.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyDiagnosticSeverity.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyDiagnosticCodes.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyResult.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicyKeys.cs`
- `src/Aurelian.Runtime/Compositor/CompositorPolicySession.cs`
- `tests/Aurelian.Runtime.Tests/CompositorPolicyM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0055-a55-runtime-dominatus-compositor-policy-m0.md`

## 2. Task scope

A55 implements runtime policy only. It does not call Vulkan, instantiate graphics resources, create a surface or swapchain, implement a frame loop, implement differential rendering, or add a runtime-to-graphics bridge.

The implemented path is:

```text
CompositorFrameFacts + RequiredPlantOutputSet + PresentationTargetRef
  -> Aurelian.Runtime.Compositor policy decision
  -> Dominatus CompositorDispatchAct
  -> fake/test compositor actuator
  -> neutral CompositorDispatchResult
```

## 3. Dominatus patterns used

Current runtime smoke registers handlers with `ActuatorHost.Register(...)`, creates an `AiWorld` using that actuator host, adds an `AiAgent` with an `HfsmInstance`, emits `Act` steps, stores the `ActuationId` in a blackboard key, and waits through `AwaitActuation<T>`.

Dominatus command types implement `IActuationCommand`. Typed handlers implement `IActuationHandler<TCommand>` and return `ActuatorHost.HandlerResult`, commonly `CompletedWithPayload<T>` when the command has an immediate typed result.

Blackboard facts use `BbKey<T>` values. A55 follows that shape with runtime-local keys for policy facts, decision, dispatch actuation id, and dispatch result.

## 4. Policy facts/decision model

`CompositorPolicyFacts` groups the neutral frame facts, required outputs, target, and requested policy. `CompositorPolicySession.Decide(...)` is a pure helper so readiness behavior can be tested without Dominatus.

M0 supports only `CompositorPolicyKind.Passthrough`. Ready and reused required outputs dispatch. Pending, missing, or failed outputs wait. Full-quality, reduced-frequency, and differential policy requests are rejected as unsupported in M0.

## 5. Compositor dispatch act

`CompositorDispatchAct` is runtime-only and implements Dominatus `IActuationCommand`. Its payload is exactly the neutral `CompositorDispatchRequest`. It deliberately does not carry backend resources, graphics handles, Vulkan handles, swapchain state, or runtime-specific image wrappers.

## 6. RunOnce / actuation behavior

`CompositorPolicySession.RunOnceAsync(...)` performs one policy tick:

1. Computes the pure decision.
2. Returns waiting or rejected results without dispatch when appropriate.
3. Builds a tiny Dominatus HFSM root node for dispatch decisions.
4. Emits `CompositorDispatchAct` with `Act`.
5. Awaits `CompositorDispatchResult` with `AwaitActuation<CompositorDispatchResult>`.
6. Returns a `CompositorPolicyResult` that distinguishes dispatched, waiting, rejected, and failed policy outcomes.

The helper is intentionally one-shot and M0-scoped. It does not own a long-running frame pump.

## 7. Fake actuator tests

Runtime tests define a fake `IActuationHandler<CompositorDispatchAct>`. The fake captures emitted acts and completes with a neutral `CompositorDispatchResult`. A failure variant returns a failed neutral dispatch result so policy failure propagation is covered without graphics code.

## 8. Boundary checks

Boundary checks were run against runtime and graphics/contract dependency seams. Runtime references Dominatus and rendering contracts. Runtime does not reference `Aurelian.Graphics`; graphics does not reference runtime policy or Dominatus; rendering contracts do not reference Dominatus.

The second boundary search reports neutral `CompositorPolicyKind` references in rendering contracts as expected because policy kinds are part of the A51 neutral contract layer, not Dominatus runtime policy.

## 9. Validation results

Validation performed:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Runtime.Tests/Aurelian.Runtime.Tests.csproj -c Debug
```

Additional boundary `rg` checks were run for forbidden runtime graphics/Vulkan terms, reverse Dominatus/runtime leakage, and project references.

## 10. Deferred features

Deferred by design:

- runtime-to-graphics compositor actuator bridge;
- graphics compositor actuator implementation;
- frame loop / present loop / frame pump;
- Vulkan calls from runtime policy;
- differential compositor policy;
- reduced-frequency cadence beyond unsupported/rejected shape;
- multi-GPU external memory;
- shader/compiler integration;
- service locator or global compositor singleton.

## 11. Next recommendation

**A56 — Runtime/Graphics compositor actuator bridge M0**

Policy now emits neutral compositor acts and the graphics module already has a neutral passthrough compositor mechanism. The next narrow integration step is a bridge that lets a runtime compositor actuator invoke the existing graphics mechanism without making `Aurelian.Graphics` depend on Dominatus and without moving policy into graphics.
