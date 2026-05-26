# Dominatus Authoring Footguns

## Purpose

Dominatus nodes are ordinary C# iterators, which makes them approachable, debuggable, and testable with standard tooling. That same flexibility also means semantic authoring footguns are possible when node lifetime and event-consumption behavior are not modeled explicitly.

This note captures operational patterns found during real Copeland/Machina integration work, including the `CounterUiRuntime` event re-consumption issue and established root-node patterns from Ariadne samples. The goal is disciplined authoring plus targeted tests, not a Roslyn-enforced DSL.

## Quick Rules

- Root nodes that hand off should usually park with `Ai.Steady`.
- `Succeed`/`Fail` complete/pop; they do not park.
- Persistent event listeners must avoid re-consuming historical events.
- `Ai.Event<T>()` now defaults to `FutureOnly` cursor start in refreshed Dominatus.
- Use `EventCursorStart.IncludeExisting` when a listener must consume events published before wait installation.
- Keep monotonic sequence IDs or correlation IDs when duplicate protection is still required by your flow.
- `TickUntilIdle`-style helpers must be bounded or predicate-driven.
- Iterator locals are not persistence state.
- Blackboard keys should be stable and explicit.

## Footgun 1: Root nodes that hand off but do not park

Root or overlay nodes are often used to establish long-lived scope for an app/runtime shell. A common failure mode is: root hands off to a leaf state, then the root naturally completes (or keeps advancing) instead of remaining alive-but-idle.

When that happens, the root can re-enter, re-trigger transitions, or otherwise fight leaf behavior. In Dominatus semantics, completion (`Succeed`, `Fail`, or natural iterator end) is not equivalent to “stay alive but idle.”

## Pattern: Root handoff + Ai.Steady

The Ariadne sample demonstrates the expected shape for this scenario (`vendor/Dominatus/src/Ariadne.Console/Scripts/AriadneThreadOfNight.cs`): perform one-time handoff, then explicitly park the root.

```csharp
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    yield return Ai.Goto(States.Intro);
    yield return Ai.Steady("Root parked after handoff");
}
```

Conceptually:

- Root performs initial orchestration/handoff once.
- `Ai.Steady` parks that root node.
- Leaf nodes do active state work.

This is usually the right shape for app roots, adventure roots, overlays, and runtime shells.

## Footgun 2: Persistent event listeners can re-consume historical events

A real bug pattern appeared in `CounterUiRuntime`: a persistent listener loop repeatedly installed `Ai.Event<UiActionEvent>()`.

Event waits are cursor-based. Reinstalling a wait can begin from a fresh cursor position. If historical events remain retained in the event bus and still match the filter, the same old event can be consumed again.

Observed symptoms in this class of bug included stalled tests and runaway processing/OOM-like behavior.

Relevant repo context:

- `src/Machina.Dominatus/Runtime/CounterUiRuntime.cs`
- `src/Machina.Dominatus/Runtime/UiActionEvent.cs`
- `vendor/Dominatus/src/Dominatus.Core/Nodes/NodeRunner.cs`
- `vendor/Dominatus/src/Dominatus.Core/Runtime/AiEventBus.cs`

Conceptual bad shape (illustrative, not claiming exact source parity):

```csharp
while (true)
{
    yield return Ai.Event<UiActionEvent>();

    // Handle event...
    // Loop reinstalls Ai.Event<T>() with fresh cursor.
}
```

## Pattern: Cursor start + optional sequence/correlation guard

Refreshed Dominatus now defaults `Ai.Event<T>()` to `EventCursorStart.FutureOnly`, which starts a newly installed wait from the current event-bus tail.

That default prevents historical replay, but it also means events published before wait installation are intentionally skipped.

For ingress flows that publish before the first wait is installed, set `cursorStart: EventCursorStart.IncludeExisting`.

Sequence/correlation guards remain useful when you need stronger exactly-once guarantees across reinstalls, replay, or multi-step coordination.

Local mitigation pattern:

- Add monotonic `Sequence` on incoming event payload.
- Publisher increments sequence for each sent action.
- Consumer tracks `lastProcessedSequence`.
- Event filter excludes events with `Sequence <= lastProcessedSequence`.

This prevents re-processing retained historical events even if a wait is reinstalled.

Conceptual example (API shape may differ by site):

```csharp
private long _nextSequence;
private long _lastProcessedSequence;

public void SendAction(UiAction action)
{
    var evt = new UiActionEvent(action.Name, ++_nextSequence);
    // publish/send event
}

private static IEnumerator<AiStep> CounterNode(AiCtx ctx)
{
    var lastProcessed = 0L;

    while (true)
    {
        yield return Ai.Event<UiActionEvent>(
            filter: e => e.Sequence > lastProcessed,
            onConsumed: (_, e) =>
            {
                lastProcessed = e.Sequence;
                // handle event
            });
    }
}
```

Other viable guards:

- Correlation IDs per request/interaction.
- Persisted blackboard cursor discipline.
- Event-bus semantics/API that wait only for events appended after install.
- One-shot action nodes where appropriate instead of perpetual listener loops.

## Pattern: Bounded TickUntil helpers

Test/runtime helpers should not spin until “idle” unless idle is precisely and safely defined.

Prefer bounded or predicate-driven forms:

```csharp
runtime.TickUntil(
    predicate: () => runtime.Count == expected,
    maxTicks: 32);
```

or:

```csharp
runtime.TickUntilIdle(maxTicks: 32);
```

Guideline:

- No unbounded `while` ticking in tests.
- If bounds are exceeded, throw a diagnostic exception that includes useful state.

## Related but distinct: Ai.Steady vs event cursor discipline

`Ai.Steady` solves root parking. It does not solve repeated event-wait reinstallation.

If a node goes steady, it stops advancing and therefore stops processing future events. That is correct for parked roots after a handoff.

Persistent event consumers, by design, continue advancing over time. Those nodes therefore require sequence/correlation/cursor discipline to avoid historical event re-consumption.

## Machina/Copeland guidance

Keep Dominatus out of pure model/layout/data packages:

- `Machina.Layout`
- `Machina.Core`
- `Machina.Standard`
- Pure raster packages

Use Dominatus in runtime/control-plane layers, including:

- Renderer actuation adapters
- App runtime scopes
- Browser host lifecycle
- Async effects
- Capability dispatch

For simple deterministic state transitions, prefer `Machina.Runtime.Dispatch`.

Use Dominatus deliberately for long-lived orchestration and runtime control.

Three-tier rule:

```text
Imperative local state:
  fine for tiny one-off code.

Dispatch table:
  best for simple deterministic field transitions.

Dominatus:
  use for orchestration, side effects, scopes, async, persistence/replay, runtime control.
```

## Checklist for new Dominatus nodes

- Is this node a root/scope node? If it hands off, should it `Ai.Steady`?
- Is this node persistent? What makes it park between ticks?
- Does every `while (true)` path yield a real wait/actuation/steady/control step?
- Can this event wait re-consume old events?
- Does it need sequence/correlation/cursor discipline?
- Does it rely on iterator locals that must survive checkpoint/replay?
- Are blackboard keys stable and static?
- Are `TickUntil` helpers bounded in tests?
- Is this simple field transition better handled by `DispatchTable`?

## References in this repo

- `docs/dominatus-audit-m0a.md`
- `docs/dominatus-authoring.md`
- `docs/dominatus-integration.md`
- `docs/machina-dominatus-runtime.md`
- `src/Machina.Dominatus/Runtime/CounterUiRuntime.cs`
- `src/Machina.Dominatus/Runtime/UiActionEvent.cs`
- `vendor/Dominatus/src/Ariadne.Console/Scripts/AriadneThreadOfNight.cs`
