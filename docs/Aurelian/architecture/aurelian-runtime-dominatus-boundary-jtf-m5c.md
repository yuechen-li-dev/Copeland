# Aurelian Runtime Dominatus public boundary — JTF-M5c

## Status

JTF-M5c closes the Aurelian side of the Dominatus ownership review. `Aurelian.Runtime` remains the intentional engine/runtime owner of `Dominatus.Core`; ordinary Aurelian lifecycle and compositor consumers no longer need to name a Dominatus runtime type.

No project was added, no package version changed, and no transition, UI lifecycle, rendering, input, screen, frame-pump, or close-handling design changed.

## Pre-change inventory and classification

The compiled pre-M5c `Aurelian.Runtime` public surface exposed the following Dominatus type graph.

| Pre-change symbol | Dominatus exposure | Classification | M5c decision |
| --- | --- | --- | --- |
| `AurelianRuntimeSessionOptions.ActuatorHost`, `World`, `Runner`, `ConfigureActuatorHost` | `ActuatorHost`, `AiWorld`, `IAurelianAiWorldRunner`, `Action<ActuatorHost>` | A — ordinary constructor options blended advanced world composition into the default session API | Replaced by explicit `Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusOptions`. |
| `IAurelianAiWorldRunner.RunTickAsync` and `SequentialAurelianAiWorldRunner.RunTickAsync` | `AiWorld` | B/C — useful only to an author deliberately orchestrating a Dominatus world; proof-era naming made it look ordinary | Retained as explicitly named `IAurelianDominatusWorldRunner` and `SequentialAurelianDominatusWorldRunner` in the advanced namespace. |
| `AurelianRuntimeSession.World` and `ActuatorHost` | `AiWorld`, `ActuatorHost` | A — an ordinary lifecycle object exposed its implementation objects as default status | Replaced with explicit `GetDominatusAccess()`. |
| `AurelianRuntimeTickAct : IActuationCommand` | `IActuationCommand` | C — runtime implementation command, used only by the internal policy node | Made internal. |
| `CompositorPolicySession.RunOnceAsync(facts, ActuatorHost, ...)` | `ActuatorHost` | B — direct host composition is meaningful only for advanced Dominatus hosts | Retained as `Aurelian.Runtime.Dominatus.CompositorPolicyDominatus.RunOnceAsync`. |
| `CompositorPolicySession.RunOnceAsync(facts, Func<CompositorDispatchAct, ...>)` | nested `CompositorDispatchAct : IActuationCommand` | A — normal compositor callers should exchange the Aurelian-owned dispatch request | Changed to `Func<CompositorDispatchRequest, CancellationToken, Task<CompositorDispatchResult>>`. |
| `CompositorDispatchAct : IActuationCommand` | `IActuationCommand` via public base interface | C — bridge implementation detail | Made internal. |
| `Aurelian.Core.CompositorActuationBridge.HandleAsync` and `AsHandler` | public `CompositorDispatchAct` whose type graph contained `IActuationCommand` | A — Core's public frame-pump seam leaked Runtime's Dominatus command | Changed to the neutral `CompositorDispatchRequest` delegate. |
| `AurelianRuntimeSessionKeys`, `CompositorPolicyKeys`, policy graph/agent/blackboard/event cursor/trace implementation | `BbKey<T>`, `StateId`, `HfsmGraph`, `HfsmInstance`, `AiAgent`, `AiCtx`, `ActuationId`, mailbox/event types | C | Already internal/private; retained internally. |
| `DominatusSmokeRuntime` and `DominatusSmokeResult` | only scalar/string result values | D — retained smoke proof without a concrete Dominatus signature | Unchanged. |

The repository had no public Aurelian signatures involving Dominatus persistence or trace types. Aurelian samples and `Aurelian.Machina` use the Aurelian frame and compositor contracts, not a Dominatus type. Tests that used direct actuators were converted to the request/delegate path unless they exercise the named advanced surface.

## Ordinary consumer path

The normal session requires only Aurelian contracts:

```csharp
var session = new AurelianRuntimeSession();
session.Start();

AurelianRuntimeTickResult tick = await session.TickAsync(
    new AurelianRuntimeTickInput(7, TimeSpan.FromMilliseconds(16)));

session.Stop();
```

`Start`, `Stop`, `TickAsync`, lifecycle state, results, and diagnostics retain their existing semantics. A repeated `Stop` remains rejected with the existing `RuntimeAlreadyStopped` diagnostic. The Core frame pump still accepts close before another frame and still composes policy through an Aurelian-owned `CompositorDispatchRequest` delegate.

## Explicit advanced access

An advanced runtime author may opt in deliberately:

```csharp
var session = new AurelianRuntimeSession(new AurelianRuntimeDominatusOptions
{
    ActuatorHost = customHost,
    WorldRunner = customRunner,
});

AurelianRuntimeDominatusAccess access = session.GetDominatusAccess();
```

The advanced namespace is for authors who intentionally own Dominatus world, actuator, blackboard, HFSM, trace, or persistence orchestration. It is not for normal game/session, frame-loop, or integration-host code. The session owns the returned world and actuator for its entire lifetime. Configure or inspect them before `Start`; callers must not mutate world topology, handler registrations, blackboards, trace, or persistence state while a session tick or compositor-policy tick is active. Compatibility of this opt-in surface follows the referenced `Dominatus.Core` package.

`CompositorPolicyDominatus.RunOnceAsync` is the equivalent deliberate host-composition entry point. Normal Core and renderer composition must use `CompositorPolicySession.RunOnceAsync` with the Aurelian-owned request delegate.

## Compiled enforcement

`AurelianRuntimePublicBoundaryM5cTests` reflects exported Runtime and Core types, constructors, methods, properties, fields, events, interfaces/base types, arrays/by-ref wrappers, nested generic arguments, and generic constraints. It also proves the inspector catches a test-defined `IReadOnlyList<Dictionary<string, AiWorld>>` property.

The exact Runtime allowlist is intentionally small:

1. `AurelianRuntimeDominatusAccess.ActuatorHost`
2. `AurelianRuntimeDominatusAccess.World`
3. `AurelianRuntimeDominatusOptions.ActuatorHost`
4. `AurelianRuntimeDominatusOptions.ConfigureActuatorHost`
5. `AurelianRuntimeDominatusOptions.World`
6. `CompositorPolicyDominatus.RunOnceAsync`
7. `IAurelianDominatusWorldRunner.RunTickAsync`
8. `SequentialAurelianDominatusWorldRunner.RunTickAsync`

No namespace- or assembly-wide exemption exists. `Aurelian.Core` is asserted source- and public-surface-neutral; it has no Dominatus source token or public Dominatus type. It retains a Runtime project reference because the frame loop executes the intentional Runtime implementation; that is not a Core source or public API ownership transfer.

`Aurelian.Machina` is likewise asserted source- and project-neutral. `Machina.Dominatus` remains the separate optional integration owner for future coarse UI behavioral scopes and has no back-edge into Machina core.

## Compatibility and follow-up

The former public `AurelianRuntimeSessionOptions`, session `World`/`ActuatorHost` properties, old runner names, public runtime act, public compositor act, and direct compositor overload were proof-era API and had no production consumer outside repository tests. They were intentionally restricted rather than deprecated: retaining their old default locations would preserve accidental coupling. The additive ordinary request/delegate path already existed for compositor policy and now becomes the only ordinary path; the explicit advanced namespace provides the compatibility path where direct Dominatus ownership is real.

The separately stabilized deterministic-transition API remains deferred. M5c neither updates Dominatus packages nor consumes local packages/source, changes transition tables, introduces a runtime adapter, or migrates UI local state. Machina Push/Pop component lifecycle work also remains deferred.

With the M5a authoring audit, M5b ownership consolidation, and this M5c public-boundary proof, JTF-M5 is complete.
