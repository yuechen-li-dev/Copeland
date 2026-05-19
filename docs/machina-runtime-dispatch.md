# Machina.Runtime.Dispatch (M0a)

## Purpose

Machina.Runtime.Dispatch is a tiny, deterministic state transition layer for simple UI event updates.

Its model is:

- `state + event + dispatch table -> next state`

This is the C# port of the original MachinaDispatch intent from Oct research and TypeScript work in MachinaLayout.JS, but **not** a field-name-string port.

## C# design

M0a uses typed getter/setter lambdas instead of string field keys.

```csharp
private sealed record CounterState(int Count);

private static readonly DispatchTable<CounterState> CounterDispatch =
    DispatchTable.For<CounterState>()
        .Increment(
            eventName: "counter.increment",
            get: state => state.Count,
            set: (state, value) => state with { Count = value });

var next = CounterDispatch.Dispatch(new CounterState(0), "counter.increment");
// Count = 1
```

This keeps transitions explicit, compile-time typed, and immutable-friendly.

## Supported transitions in M0a

- `Set`
- `Toggle`
- `Increment` (int)

Prefix/suffix transitions are deferred to M0b.

## Ordered semantics

Dispatch tables are ordered.

- transitions are evaluated in insertion order
- first matching transition wins
- unknown event returns the original state

This differs from the TypeScript fixed group-order approach and is intentional for C# ergonomics.

## No-match and no-op behavior

- no matching transition: original state returned
- `Set` when current equals target value: original state returned
- `Increment` when `by == 0`: original state returned
- `Toggle`: always writes `!current`

For reference-type state (for example record classes), identity checks can avoid unnecessary rendering:

```csharp
var next = table.Dispatch(state, evt);
if (!ReferenceEquals(next, state))
{
    RenderCurrentState();
}
```

For value-type state, use explicit equality rather than reference identity.

## Error model

M0a throws `MachinaDispatchError` with stable codes:

- `InvalidDispatchEvent`
- `InvalidDispatchTransition`
- `InvalidDispatchValue`

Examples:

- null/empty/whitespace event names
- null getter/setter delegates
- null dispatch state
- checked `Increment` overflow

## When to use dispatch tables

Use dispatch tables for simple, deterministic field transitions driven by named events.

Do not use dispatch tables as a full runtime orchestration system.

- async effects
- timers
- guards
- modal/screen scopes
- IO
- orchestration

Use Dominatus or application-specific runtime logic for those concerns.

## Three-tier rule

```text
Imperative local state:
  fine for tiny one-off code.

Dispatch table:
  best default for simple deterministic field transitions.

Dominatus:
  use for orchestration, side effects, scopes, async, persistence/replay, runtime control.
```

## Dependency boundary

Machina.Runtime.Dispatch M0a is pure state transition logic.

- no Dominatus dependency
- no presenter/window dependency
- no renderer dependency

## Presenter sample default (M0d)

As of M0d, `samples/Machina.Presenter.Sample` uses `DispatchTable<CounterState>` for the live counter state loop.

This demonstrates the intended default for simple deterministic field transitions in real UI interaction paths.

