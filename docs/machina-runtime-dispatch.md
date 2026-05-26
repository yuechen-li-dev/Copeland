# Machina.Runtime.Dispatch (M3c)

## Purpose

Machina.Runtime.Dispatch is a tiny, deterministic state transition layer for simple UI event updates.

Its model is:

- `state + event + dispatch table -> next state`

This is the C# port of the original MachinaDispatch intent from Oct research and TypeScript work in MachinaLayout.JS, but **not** a field-name-string port.

## C# design

M3c uses typed getter/setter lambdas and typed `UiActionId` values instead of stringly app-code action identifiers.

```csharp
private sealed record CounterState(int Count);

private static class Actions
{
    public static readonly UiActionId Increment = new("counter.increment");
}

private static readonly DispatchTable<CounterState> CounterDispatch =
    DispatchTable.Create<CounterState>(
    [
        DispatchTransitions.Increment(
            Actions.Increment,
            get: state => state.Count,
            set: (state, value) => state with { Count = value }),
    ]);

var next = CounterDispatch.Dispatch(new CounterState(0), Actions.Increment);
// Count = 1
```

This keeps transitions explicit, compile-time typed, and immutable-friendly.

## Supported transitions in M3c

- `Set`
- `Toggle`
- `Increment` (int)

Compatibility string overloads remain available. New code should define `UiActionId` once and reuse it in views and dispatch tables.

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
