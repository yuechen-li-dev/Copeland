# A64 — Dominatus-backed Aurelian runtime tick M0

## 1. Files changed

- `src/Aurelian.Runtime/Sessions/AurelianRuntimeSession.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeSessionOptions.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeTickInput.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeTickResult.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeTickStatus.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeDiagnostic.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeDiagnosticSeverity.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeDiagnosticCodes.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeResult.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeSessionFacts.cs`
- `src/Aurelian.Runtime/Sessions/AurelianRuntimeTickAct.cs`
- `src/Aurelian.Runtime/Sessions/IAurelianAiWorldRunner.cs`
- `src/Aurelian.Runtime/Sessions/SequentialAurelianAiWorldRunner.cs`
- `tests/Aurelian.Runtime.Tests/AurelianRuntimeSessionM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/world-model-doctrine.md`

## 2. Task scope

A64 adds the first runtime tick/session shape under `Aurelian.Runtime.Sessions`. The scope is intentionally M0: a started/stopped runtime session, typed tick input/result/status/diagnostics, a Dominatus `AiWorld` and `ActuatorHost`, a root HFSM runtime policy node, a neutral `AurelianRuntimeTickAct`, and a runner seam used to drive one Dominatus world tick.

The scope excludes a full simulation loop, render extraction, graphics submission, a Core frame-loop bridge, windowing, swapchain creation, asset loading, and any new package or vendor changes.

## 3. Runtime tick ownership decision

`Aurelian.Runtime` owns `AurelianRuntimeSession` because runtime ticking is behavior orchestration rather than graphics mechanism or Core integration plumbing. Core remains the engine integration spine that may call the session later from `AurelianFrameLoop`; A64 deliberately does not wire that path yet.

The runtime session owns or receives an `AiWorld`, an `ActuatorHost`, and an `IAurelianAiWorldRunner`. This keeps runtime behavior Dominatus-shaped while allowing Core or a future host to provide externally prepared integration primitives without creating project dependency cycles.

## 4. Dominatus session/tick model

`AurelianRuntimeSession.Start()` builds the M0 Dominatus policy world by registering a default neutral tick-act handler, creating a one-state HFSM graph, adding one runtime policy agent, and marking the session started.

`TickAsync(...)` validates lifecycle and delta input, writes typed `AurelianRuntimeSessionFacts` to the runtime agent blackboard, and delegates the actual world advance to `IAurelianAiWorldRunner`. The sequential M0 runner calls `AiWorld.Tick(float dt)`, which advances the Dominatus clock, ticks the actuator host, and ticks agents through Dominatus HFSM/node execution.

`Stop()` only transitions lifecycle state. It does not tear down graphics or mutate world/render state.

## 5. Actuation proof

The runtime policy node emits `AurelianRuntimeTickAct` through Dominatus `Act`, stores the `ActuationId`, awaits completion through Dominatus `AwaitActuation`, and returns success only when the corresponding `ActuationCompleted` event reports `Ok = true`.

Tests register a fake `IActuationHandler<AurelianRuntimeTickAct>` and assert the handler sees the tick act for the requested tick index. A failure handler test proves failed Dominatus actuation returns a typed `ART1006` diagnostic rather than being hidden behind an imperative clock increment.

## 6. ParallelAiWorldRunner inspection/integration decision

Inspection found `ParallelAiWorldRunner` in `vendor/Dominatus/src/Dominatus.Core/Runtime/ParallelAiWorldRunner.cs`, not in an Aurelian Core project. It is a Dominatus.Core primitive with direct dependencies on `AiWorld`, `AiAgent`, staged blackboard/mailbox/actuator surfaces, `Parallel.ForEach`, cancellation tokens, aggregate agent faults, deterministic write conflict policies, and a `ParallelTickResult`.

It runs multiple agents within a single `AiWorld`; it does not coordinate multiple `AiWorld` instances. It handles cancellation before and during parallel work, clears per-agent context factories on cancellation, aggregates agent tick exceptions, and can fail on staged world blackboard write conflicts. Its API is generic enough to become a future implementation behind `IAurelianAiWorldRunner`, but its staged-effect semantics are materially different from the simple sequential M0 session proof.

A64 therefore does not move or wrap it directly. Moving would modify vendor code or imply ownership transfer that is not needed. Wrapping it immediately would make A64 answer parallel staged merge policy before the runtime has real world tick facts or multi-agent behavior needs. The integration decision is: keep it in Dominatus.Core/vendor as future Dominatus-level parallel execution support, expose `IAurelianAiWorldRunner` in Runtime now, ship `SequentialAurelianAiWorldRunner` for M0, and defer a `ParallelAiWorldRunner` adapter until a multi-agent/world tick M1 milestone can choose staged-write conflict policy deliberately.

## 7. Existing AurelianRuntime compatibility decision

The existing `AurelianRuntime` compatibility shell remains in place and continues to expose the legacy `Tick()`/`WorldClock` behavior expected by existing tests. It is not expanded. `AurelianRuntimeSession` is the preferred Dominatus-backed runtime tick path for new integration work.

## 8. Tests added

`tests/Aurelian.Runtime.Tests/AurelianRuntimeSessionM0Tests.cs` covers:

- session start initializes a Dominatus world/actuator/agent;
- duplicate start diagnostics;
- tick-before-start diagnostics;
- invalid delta diagnostics;
- successful started tick dispatches a Dominatus runtime tick act;
- actuation failure propagation;
- stop transition;
- tick-after-stop diagnostics;
- cancellation diagnostics;
- sequential runner Dominatus world tick proof;
- documented `ParallelAiWorldRunner` decision;
- runtime source remains free of graphics/mechanism terms.

## 9. Boundary checks

A64 boundary checks verify that Runtime and Runtime tests do not reference graphics/window/backend implementation types, service-locator patterns, reflection type discovery, shader toolchain implementation, or CodeReferences material.

Project-reference checks verify that Runtime still references World, Rendering.Contracts, and Dominatus.Core only; Runtime does not reference Graphics, and Graphics is not changed to reference Runtime or Dominatus.

## 10. Validation results

Validation was performed with solution build/test commands, targeted Runtime/Core tests, and ripgrep boundary checks. The final handoff message records the exact command outcomes.

## 11. Deferred features

Deferred features:

- Core frame loop invoking `AurelianRuntimeSession`;
- full world simulation;
- world mutation acts beyond the neutral M0 runtime tick act;
- render snapshot extraction during runtime ticks;
- graphics submission;
- parallel staged Dominatus runner adapter;
- multi-agent/multi-world tick policy;
- host project;
- package additions;
- vendor or CodeReferences changes.

## 12. Next recommendation

A65 — Core frame loop calls runtime session M0.

Rationale: A63 built the Core frame loop and A64 builds the Dominatus-backed Runtime tick/session. The next convergent step is to connect Core’s frame loop to the Runtime session without recreating behavior logic in Core.
