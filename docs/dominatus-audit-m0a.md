# Dominatus Integration M0a Audit

## Executive Summary

Dominatus in this vendored snapshot is a deterministic, iterator-node HFSM runtime with:

- a stack-based control model (`HfsmInstance`) over node iterators (`NodeRunner`),
- typed blackboards with revision + dirty-key gating,
- typed per-agent event buses and mailbox ingress,
- typed actuation commands with synchronous or deferred completion (`ActuatorHost`),
- checkpoint/replay infrastructure that restores blackboard + active state path and replays completion events.

For Machina/Copeland, the strongest immediate fit is **runtime control + UI ingress + render command emission as typed actuations**. The least risky near-term path is to keep current Machina packages Dominatus-free and introduce Dominatus-aware runtime/renderer adapters in new packages.

## Source/Commit Audited

- Audit target: vendored source tree under `vendor/Dominatus` in this repository.
- No upstream git SHA was embedded in this audit; the effective audited artifact is the current vendored tree.
- Primary docs read:
  - `vendor/Dominatus/docs/ARCHITECTURE.md`
  - `vendor/Dominatus/docs/AUTHORING_GUIDE.md`
  - `vendor/Dominatus/docs/DOMINATUS_SERVER_M0.md`
  - `vendor/Dominatus/docs/ACTUATORS_STANDARD_M0.md`

## Project Map

Audited projects and intent (from source):

- `src/Dominatus.Core`
  - Runtime kernel: HFSM, nodes/steps, blackboard, events/mailbox, actuation interfaces/host, persistence/checkpoint/replay.
- `src/Dominatus.OptFlow`
  - Authoring helpers (`Ai.*`) for emitting canonical steps.
- `src/Ariadne.OptFlow`
  - Dialogue-oriented step wrappers (`Diag.*`) built on Dominatus actuation/event semantics.
- `src/Dominatus.UtilityLite`
  - Utility scoring helpers (`Utility`, `When`) over core decision primitives.
- `src/Dominatus.Actuators.Standard`
  - Standard actuator handlers (file/time/http/process/calendar).
- `src/Dominatus.Server`
  - ASP.NET minimal API exposure of world/agent state.
- `samples/Dominatus.FishTank`
  - Real-time utility/HFSM sample with custom commands.
- `src/Ariadne.Console`
  - Console dialogue sample with custom actuation handlers.
- `docs/*`
  - Architecture + package milestone docs.

## Core Runtime Architecture

### Main runtime/session types

- `AiWorld` is the top-level simulation/runtime container. It owns clock, world blackboard, agent list, world view, mailbox, and actuator. `Tick(dt)` advances clock, expires world BB TTL, ticks actuator, then ticks each agent. (`vendor/Dominatus/src/Dominatus.Core/Runtime/AiWorld.cs`)
- `AiAgent` owns agent-local blackboard, event bus, brain (`HfsmInstance`), change tracker, and in-flight actuations set. Agent tick expires agent BB TTL then ticks brain. (`vendor/Dominatus/src/Dominatus.Core/Runtime/AiAgent.cs`)
- `AiCtx` is the node execution context record (world/agent/events/view/mail/act + `Bb`/`WorldBb` convenience). (`vendor/Dominatus/src/Dominatus.Core/Runtime/AiCtx.cs`)

### HFSM / stack model

- `HfsmGraph` maps `StateId -> HfsmStateDef`; each state has node + transition/interrupt lists. (`HfsmGraph.cs`, `HfsmStateDef.cs`)
- `HfsmInstance` maintains a runtime stack of active frames (`ActiveState`) each with `NodeRunner`.
- `KeepRootFrame` option changes semantics: root can remain as overlay while leaf changes.
- Tick order is:
  1. optional transition/interrupt scan (gated by cadence + BB revision/dirty keys),
  2. optional root overlay tick when `KeepRootFrame=true`,
  3. leaf tick.

### Node authoring model

- Node type: `AiNode` delegate = `IEnumerator<AiStep> (AiCtx ctx)`.
- `NodeRunner` advances iterator and interprets wait/event/actuation steps, emitting control steps upward.

### Tick lifecycle

- World tick is deterministic single-threaded loop in source.
- Node wait steps can continue in same tick when conditions already satisfied (important for replay and immediate completion behavior).

### Blackboard model

- Typed keys via `BbKey<T>(string Name)`.
- Two blackboards: world (`world.Bb`) and per-agent (`agent.Bb`), both same class.

### Mailbox/event model

- `AiEventBus`: per-agent typed buckets + cursor-based consume.
- `IAiMailbox`: world-level routing helper (`Send`, `Broadcast`) that publishes into recipient event bus.

### Actuation model

- `IActuationCommand` marker commands.
- `IAiActuator.Dispatch(AiCtx, IActuationCommand)` returns `ActuationDispatchResult` with id + accepted/completed/ok.
- `ActuatorHost` routes commands by concrete type to registered handlers; supports policies and deferred completion queue.

### Trace / replay model

- `IAiTraceSink`, `TextWriterTraceSink`: runtime tracing hooks.
- Persistence includes replay log/checkpoint and replay driver types under `Persistence/*`.

### Persistence/save-restore model

- `DominatusCheckpointBuilder`, `DominatusCheckpoint`, `AgentCheckpoint`, `ReplayDriver`, `SaveFile` etc. indicate intended full save/restore + replay.
- Stack restore is path-based (`RestoreActivePath`), not iterator serialization; nodes are re-entered and replay catches state back up.

### Diagnostic/error model

- Runtime generally treats node exceptions as failure completion (`NodeRunner.Tick` catches `MoveNext` exceptions -> `Failed`).
- Transition predicate exceptions are swallowed as false (`SafeWhen`).
- No global exception bus; behavior is intentionally fail-soft per node/transition.

## Node Authoring Model

### Are nodes iterator-based?

Yes. Node authoring is explicitly C# iterator methods returning `IEnumerator<AiStep>`.

### What does a node yield?

`AiStep` subtypes, notably:

- waits: `WaitSeconds`, `WaitUntil`, `WaitEvent<T>` / `IWaitEvent`
- control: `Goto`, `Push`, `Pop`, `Succeed`, `Fail`
- decision: `Decide`
- actuation: `Act`, `AwaitActuation`, `AwaitActuation<T>`
- parking: `Steady`

### Key step/result types

- Step base: `AiStep`.
- Runner result: `NodeTickResult` with emitted step or completed status.
- Completion status: `NodeStatus` (`Running`, `Succeeded`, `Failed`).

### Push/pop/goto/replace/complete/fail behavior

- Node emits control step.
- `HfsmInstance.ApplyEmittedStep` applies stack ops:
  - `Goto`: replace top (or push above root when keep-root mode and only root present).
  - `Push`: push new frame.
  - `Pop`: pop current frame.
  - `Succeed`/`Fail`: both pop frame in current M0 behavior.
- Natural iterator completion is converted to `Succeed("NodeCompleted")` by HFSM.

### Waiting/awaiting model

- `WaitSeconds` and `WaitUntil` are runner-local waits.
- `IWaitEvent` install stores cursor + optional timeout and polls each tick.
- Event consumption wins over timeout when both possible on same tick.
- `Act` + `Await` pattern: dispatch command, store id optionally in BB, then wait for matching `ActuationCompleted*` event.

### Node-local state

- Iterator locals are node-local **but not persisted** across checkpoint restore.
- Persisted resumability should use blackboard keys (Ariadne `DiagSteps` uses synthetic BB keys for this reason).

### Minimal real API example

```csharp
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

public static IEnumerator<AiStep> Idle(AiCtx ctx)
{
    yield return Ai.Wait(0.5f);
    yield return Ai.Succeed();
}
```

This matches source authoring conventions in `docs/AUTHORING_GUIDE.md` and actual sample node style.

## Blackboard

### Key declaration and type safety

- Keys are explicit typed values: `static readonly BbKey<T> Key = new("Name")`.
- Type safety is at callsite; storage is internal `Dictionary<string, object?>`, so wrong-type reads return default/false via `TryGet`.

### Read/write behavior

- `TryGet`, `GetOrDefault`, `Set`, `SetFor`, `SetUntil`, `Remove`, `ClearTtl`, `Expire`.
- Equality/no-op: `Set` no-ops fully when same value and no TTL mutation.
- TTL mutation can still increment revision even when value equal if TTL changed/cleared.

### Revision/dirty tracking

- `Revision` increments on successful write-like mutations including expiry/removal.
- `DirtyKeys` tracks key names changed since `ClearDirty()`.
- HFSM transition scanning uses dirty key filters (`DependsOnKeys`) and clears dirty after scan cycle.

### Persistence behavior

- Snapshot enumeration includes optional expiry metadata (`EnumerateSnapshotEntries`).
- Restore-oriented methods (`SetRaw`, `Clear`) bypass dirty/revision/journal semantics by design.

### Suitability as renderer/canvas state tracker

**Short answer: partially suitable, with caveats.**

Good fit:

- current fill color / text style token / transform token can be represented as typed keys.
- revision + dirty keys can gate expensive rerender paths.
- deterministic TTL can model short-lived transient interaction facts.

Caveats:

- blackboard is keyed flat store, not a structured render-state stack; clip/transform push/pop nesting needs explicit modeling in node stack or command stream.
- value equality relies on `EqualityComparer<T>`; mutable reference types as BB values are risky for dirty detection.
- no intrinsic batched diff domain for renderer; only per-key dirty info.

Practical recommendation:

- Use BB for **high-level render intent/state** (theme, active screen, invalidation revision, pointer focus).
- Use explicit render command stream (actuation payloads or buffer objects) for per-frame drawing ops and scope stack semantics.

## Mailbox / Event Ingress

### Representation and queue semantics

- Messages are plain typed CLR objects published to `AiEventBus` buckets keyed by exact type.
- Cursor-based consume (`EventCursor.Index`) scans append-only list for that type.
- No global staged/visible dual queue in current core; semantics are “published now, consumed by waiters as they tick.”

### Tick boundary semantics

- Publication can occur any time handler/node does so, but consumption happens when waiting step ticks.
- `NodeRunner` immediately attempts first consume when installing wait event, enabling same-tick consume if already present.

### Consume/peek

- There is consume (`TryConsume`) with optional filter.
- No distinct public peek API in bus.

### How Machina UI actions should enter Dominatus

Recommendation:

1. Define typed UI event records (e.g., `ButtonPressed`, `CheckboxChanged`, `SwitchChanged`, `TextInputChanged`, `HitTestResolved`).
2. Inject at host boundary by publishing to target agent `Events` (or mailbox send to agent id).
3. In nodes, use `Ai.Event<T>(...)` wait steps with filters and optional timeout.

Mapping examples:

- button press -> `ButtonPressed(string controlId)` event.
- checkbox changed -> `CheckboxChanged(string controlId, bool value)`.
- switch changed -> same shape as checkbox.
- text input changed later -> throttled/coalesced host-side, publish latest `TextInputChanged`.
- renderer hit-test result later -> deferred typed event with correlation id.

## Actuation

### Core command/host model

- Commands implement `IActuationCommand`.
- Handlers implement `IActuationHandler<TCmd>` and return `ActuatorHost.HandlerResult`.
- `ActuatorHost.Register<TCmd>(handler)` registers by concrete command type.
- Dispatch returns `ActuationDispatchResult` with unique `ActuationId`.

### Policy/registration

- Host supports pluggable `IActuationPolicy` deny/allow checks before handler call.

### Sync vs async completion

- Immediate: handler returns `Completed=true`; host publishes completion events immediately.
- Deferred: handler schedules `CompleteLater(...)`; host ticks pending list and publishes completion when due.
- In-flight deferred ids tracked per-agent in `InFlightActuations` for checkpoint replay.

### Await semantics

- Node waits via `AwaitActuation`/`AwaitActuation<T>` keyed by stored `ActuationId` in BB.
- Type payload can be copied to BB by `AwaitActuation<T>(..., storePayloadAs)`.

### Can draw commands be modeled as Dominatus actuations?

Yes, strongly yes for control-plane decoupling.

Feasibility per command:

- `FillRect`: straightforward typed command + immediate completion (or batched deferred acknowledgment).
- `DrawText`: same.
- `PushClip` / `PopClip`: valid as scoped commands; caller must maintain stack discipline.
- `SetTransform`: valid idempotent command.
- `BeginFrame` / `EndFrame`: valid framing commands; can enforce ordering in actuator.

### Should render commands follow `Dominatus.Actuators.Standard` patterns?

Yes on structure (typed command records + handler + registration + optional policy), but not on domain specifics. Render actuator should be a separate package (e.g., Machina runtime/renderer layer), not added to Standard package.

### Minimal null/snapshot render actuator concept

- `RenderActuatorHost` commands append into in-memory frame log.
- Immediate completion for all commands.
- Optional payload on `EndFrame` with snapshot/frame checksum for tests.

### CPU raster actuator later

- Same command surface.
- Handler mutates raster canvas state and produces image/framebuffer output on `EndFrame`.
- Could defer completion for expensive frames if needed.

## Push/Pop and Runtime Scopes

### Are push/pop suitable for UI declaration scopes?

Yes for coarse runtime scopes (screen/dialog/modal flows), not for dense per-widget tree traversal every frame.

### Suitable for canvas save/restore scopes?

Partially. Push/pop semantics map conceptually, but HFSM frame machinery is heavier and semantically stateful across ticks; render save/restore usually needs lightweight per-frame stack in renderer execution layer.

### Frame weight and implications

- Each push creates `NodeRunner` + iterator + cancellation token source lifecycle.
- Good for behavioral scopes; probably too heavy for thousands of fine render scopes.

### Persistence/tracing implications

- HFSM stack is checkpointed by active state path and replayed by node re-entry.
- Scope encoded as HFSM frames participates in save/replay/trace; this is good for modal runtime logic but noisy/overkill for low-level draw scopes.

### Mapping guidance

- modal/dialog declaration frame: **use push/pop**.
- screen/page frame: **use push/pop or root-decide target states**.
- clipped container render scope: **prefer renderer-local stack/command stream**, not HFSM frame.
- temporary interaction capture scope: **use push/pop** if it spans user interaction time.

## OptFlow and Ariadne.OptFlow

### What they provide

- `Dominatus.OptFlow` (`Ai` class): concise constructors for steps/decisions/events/actuation waits.
- `Ariadne.OptFlow` (`Diag`, `DiagSteps`, commands): dialogue primitives that dispatch commands and wait for completion, with restore-safe bookkeeping via synthetic BB keys.

### Needed for Machina renderer/runtime M0?

- `Dominatus.OptFlow`: useful and low-risk for authoring readability.
- `Ariadne.OptFlow`: not required for renderer M0; useful reference for robust long-wait UI interactions and restore semantics.

### Later usefulness

- dialog flows / UI flows: yes, Ariadne patterns are directly relevant.
- app navigation: yes, via `Ai.Decide`, `Goto`, `Push` patterns.
- Copeland runtime scripts: yes for script ergonomics.
- HMI/operator prompts: yes, especially typed command + await flow.

## UtilityLite

### What it provides

- Convenience layer over `Dominatus.Core.Decision` primitives:
  - `Utility` helpers (`Always`, `Bool`, `Score`, combinators, BB helpers).
  - `When` facade for readable decision expressions.
- Decision memory/hysteresis/min-commit are implemented in `HfsmInstance.ApplyDecision` (core), not UtilityLite itself.

### Comparison to Aetheris/Judgment-style pattern

- Similar conceptual pattern: considerations scored and composed into options with hysteresis/commit policy.
- Dominatus version is intentionally minimal and directly tied to runtime `AiWorld/AiAgent` state lambdas.

### Recommendations

- Machina runtime focus/interaction priority: **yes, plausible fit** for arbitration logic.
- Renderer usage: **generally no**; renderer should be deterministic command execution, not utility scoring.
- Copeland browser/runtime policy decisions: **yes, selective use** for non-safety-critical preference arbitration.

## Standard Actuators

### Provided capabilities

From source tree:

- File sandbox commands (`ReadTextFileCommand`, `WriteTextFileCommand`, `AppendTextFileCommand`, `FileExistsCommand`, `ListFilesCommand`).
- Time commands (`GetUtcNowCommand`, `GetLocalNowCommand`).
- HTTP allowlisted commands (`HttpGetTextCommand`, `HttpPostJsonCommand`, `HttpPostTextCommand`).
- Process command (`RunProcessCommand`) with allowlist options.
- Calendar commands (`WriteCalendarEventCommand`, `AppendCalendarEventCommand`) under `Calendar/*`.

### Handler structure

- Registration extensions create one handler instance and register command types on `ActuatorHost`.
- Handlers enforce options/allowlists/path resolution rules internally.

### Safety/usefulness for Machina/Copeland runtime

- Useful as examples and for tooling tasks.
- For browser/runtime host surfaces, file/process/http should be policy-gated tightly; default to deny unless explicitly needed.

### Immediate vs deferred use

- Immediate: time actuator + maybe constrained HTTP for integration tests.
- Deferred: process/file/calendar in end-user runtime until threat model/policy layer is in place.

## Dominatus.Server

### What it provides

- Thread-safe runtime wrapper (`DominatusServerRuntime`) with lock-based `Read`/`Write` around `AiWorld`.
- Minimal API endpoints for health/world/agents/blackboard/path/snapshots via `MapDominatusServer`.
- DTO mapping utilities for serialization.

### Relevance to Copeland runtime/browser host

- Useful as dev/debug introspection endpoint pattern.
- Not a full host/runtime framework; it is observability-centric.

### Use soon or vendored-only?

- Recommend **near-term dev tooling use only** (inspection dashboards/tests).
- Keep optional in production runtime architecture initially.

### Complexity/dependency risks

- Pulls ASP.NET abstractions and web hosting concerns into runtime boundary.
- Risk of overcoupling core runtime to server transport if adopted too early.

## Samples Reviewed

### Dominatus.FishTank

Demonstrates:

- HFSM graph registration + keep-root decision loop.
- Utility decision switching (`Ai.Decide`) plus action states emitting custom `Ai.Act` commands.
- Blackboard-driven steering loops and high-frequency waits.

Useful patterns for Machina/Copeland:

- Root arbitration + leaf behavior decomposition.
- Clear separation of command intent (`FishCommands`) vs host implementation (`FishActuatorHandlers`).

Demo-only cautions:

- Tight render/sim loop and sample-specific constants are not architecture guidance.
- Some inline randomness and physics constants are sample convenience only.

### Ariadne.Console

Demonstrates:

- Command handlers as UI bridge (`DiagLineHandler`, `DiagAskHandler`, `DiagChooseHandler`).
- Adventure graph registration and run loop.
- Dialogue scripts using `Diag.*` + `Ai.*`.

Useful patterns:

- “UI is actuator handler, script is pure node logic.”
- Restore-safe long interaction steps via BB bookkeeping (`Ariadne.OptFlow/DiagSteps`).

Demo cautions:

- Console blocking behaviors and sleep loop are demo choices; do not copy directly into browser/runtime host.

## Machina Mapping

Current principle validated:

- Keep `Machina.Layout/Core/Standard` Dominatus-free now.
- Introduce Dominatus only in runtime/renderer integration layers.

Reason:

- Dominatus is control-plane/runtime orchestration, not intrinsic layout math or core rendering primitives.
- Preserving existing package purity keeps compile graph and API surface clean.

## Copeland Browser/Runtime Mapping

Recommended control-plane mapping:

- Browser host converts input/platform events into typed Dominatus events.
- Dominatus nodes arbitrate flow/state transitions and emit typed render/runtime actuations.
- Renderer/runtime adapters execute actuations against concrete backend (null snapshot first, CPU raster later).
- Optional server/debug layer exposes world + BB + active path for diagnostics.

## Recommended Package Architecture

### 1) `Machina.Runtime` (new)

- Dependencies: `Machina.Core`, optionally `Machina.Layout`, `Dominatus.Core`, `Dominatus.OptFlow`.
- Responsibilities:
  - Define typed UI/runtime events.
  - Define runtime-facing BB keys and state contracts.
  - Author baseline HFSM runtime states.
- Non-goals:
  - no raster drawing implementation,
  - no transport/server endpoints.
- First milestone:
  - action ingress + declaration/runtime state loop only.

### 2) `Machina.Renderer` (new)

- Dependencies: `Machina.Core` (+ any drawing primitives), optionally `Machina.Layout`; **no mandatory Dominatus dependency** if command types are abstracted.
- Responsibilities:
  - Render command model and execution backend abstractions.
  - Null/snapshot backend + later CPU raster backend.
- Non-goals:
  - runtime HFSM logic.
- First milestone:
  - in-memory frame snapshot backend with deterministic command list.

### 3) `Machina.Dominatus` (adapter package; recommended)

- Dependencies: `Machina.Runtime`, `Machina.Renderer`, `Dominatus.Core`, `Dominatus.OptFlow`.
- Responsibilities:
  - Bridge Machina runtime intents to Dominatus commands/events/handlers.
  - Provide Dominatus actuator registrations for render commands.
- Non-goals:
  - own layout primitives,
  - own browser transport.
- First milestone:
  - register minimal render actuator handlers that write to snapshot buffer.

### 4) `Copeland.Runtime` (host/orchestration)

- Dependencies: `Machina.*`, `Dominatus.*` as needed, platform host libs.
- Responsibilities:
  - app lifecycle, ticking, IO/event injection, policy wiring, optional diagnostics endpoints.
- Non-goals:
  - low-level render primitive definitions.
- First milestone:
  - single-agent world loop + UI event injection + render snapshot publication.

## Recommended Next Milestone

**Recommendation: `Machina.Dominatus M0a: adapter + render snapshot actuation pipeline`.**

Why this first:

- It validates core seam (Dominatus control plane -> renderer command execution) without committing to full runtime/navigation complexity.
- It produces testable artifacts quickly (deterministic command snapshots).

### Exact scope

- Add adapter package defining typed render actuation commands:
  - `BeginFrame`, `EndFrame`, `FillRect`, `DrawText`, `PushClip`, `PopClip`, `SetTransform`.
- Implement actuator handler that records commands into per-frame snapshot buffer.
- Add minimal Dominatus node/sample in tests that emits a frame and awaits completion if needed.

### Required projects

- New: `Machina.Dominatus` (or equivalent adapter package).
- Reuse: `Dominatus.Core`, `Dominatus.OptFlow`, `Machina.Renderer` (if created simultaneously) or temporary internal command structs.

### Required Dominatus APIs

- `ActuatorHost`, `IActuationHandler<T>`, `ActuationDispatchResult`, `Ai.Act`, `Ai.Await`, `AiWorld`, `AiAgent`, `HfsmGraph`, `HfsmInstance`.

### Tests to write

- Command ordering test for a single frame.
- PushClip/PopClip balance test.
- Same-tick immediate completion + await test.
- Deterministic replay/restore smoke for pending frame command id behavior (if deferred completions introduced).

### What not to implement

- no full widget runtime,
- no browser host transport,
- no layout refactor,
- no CPU raster backend yet,
- no Dominatus dependency injection into `Machina.Layout/Core/Standard`.

## Risks / Unknowns

- Replay/save semantics for complex render pipelines are only partially validated by current samples; needs dedicated adapter tests.
- Event bus assumption “one active waiter per type per agent” may constrain advanced concurrent UI waits; design around this or extend bus policy later.
- Flat blackboard keys can become unwieldy for rich UI state unless key naming conventions are enforced early.
- `Ariadne.OptFlow` callsite-id stability caveat indicates care needed for long-lived persisted scripts/content updates.

## Files and Types Worth Reading

Priority source files before implementation:

1. Core runtime
   - `vendor/Dominatus/src/Dominatus.Core/Runtime/AiWorld.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Runtime/AiAgent.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Hfsm/HfsmInstance.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Nodes/NodeRunner.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Nodes/Steps/Steps.cs`
2. Blackboard/persistence
   - `vendor/Dominatus/src/Dominatus.Core/Blackboard/Blackboard.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Persistence/DominatusCheckpointBuilder.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Persistence/ReplayDriver.cs`
3. Actuation
   - `vendor/Dominatus/src/Dominatus.Core/Runtime/ActuatorHost.cs`
   - `vendor/Dominatus/src/Dominatus.Core/Runtime/IActuatorHandler.cs`
4. Authoring helpers
   - `vendor/Dominatus/src/Dominatus.OptFlow/Ai.cs`
   - `vendor/Dominatus/src/Dominatus.UtilityLite/UtilityLite.cs`
   - `vendor/Dominatus/src/Dominatus.UtilityLite/When.cs`
5. Dialogue/interaction patterns
   - `vendor/Dominatus/src/Ariadne.OptFlow/Diag.cs`
   - `vendor/Dominatus/src/Ariadne.OptFlow/DiagSteps.cs`
   - `vendor/Dominatus/src/Ariadne.Console/Program.cs`
6. Standard handlers
   - `vendor/Dominatus/src/Dominatus.Actuators.Standard/*`
7. Server/debug surface
   - `vendor/Dominatus/src/Dominatus.Server/*`
8. Samples
   - `vendor/Dominatus/samples/Dominatus.FishTank/*`
