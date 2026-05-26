# MachinaDispatch Runtime Guide (D1)

## Purpose

MachinaDispatch is a tiny, pure table-driven event dispatcher for single-field state transitions:

`state + event + dispatch tables -> next state`

Import from the subpath:

```ts
import { defineDispatchTables, dispatchEvent } from "machinalayout/dispatch";
```

## Thesis

MachinaDispatch is not a router, store, middleware layer, or async framework. It only maps event strings to deterministic state updates through columnar tables.

## Dispatch tables

```ts
type AppState = { route: "home" | "settings"; cartCount: number; newOnly: boolean; selectedProduct: string };

const tables = defineDispatchTables<AppState>({
  set: {
    events: ["nav.home", "nav.settings"],
    fields: ["route", "route"],
    values: ["home", "settings"],
  },
  toggle: {
    events: ["filter.new"],
    fields: ["newOnly"],
  },
  increment: {
    events: ["cart.add"],
    fields: ["cartCount"],
    by: [1],
  },
  setSuffix: {
    prefixes: ["product.inspect."],
    fields: ["selectedProduct"],
    allowedSuffixes: [["p1", "p2"]],
  },
});
```

## Operation semantics

Matching order is fixed: `set -> toggle -> increment -> setSuffix -> incrementSuffix`.

- First matching group wins.
- Within each group, first matching row wins.
- No match returns the same state object.

## Error model

Errors throw `MachinaDispatchError` with stable codes:

- `InvalidDispatchTable`
- `InvalidDispatchField`
- `InvalidDispatchValue`
- `InvalidDispatchEvent`

## Immutability

- Never mutates input state or tables.
- Identity-equal updates return the original state reference.
- Changed updates return a shallow copy with one changed field.

## Routing as state assignment

```ts
const next = dispatchEvent({ route: "home" }, "nav.settings", tables);
```

This is state assignment, not URL parsing/history/router trees.

## Composition with MachinaLayout

```ts
const nextState = dispatchEvent(state, event, tables);
const rows = rowsByRoute[nextState.route];
const layout = resolveLayoutRows(rows, rootRect);
```

Dispatch and layout remain decoupled.


## Smallest useful example

```ts
type CounterState = {
  count: number;
};

const DISPATCH = defineDispatchTables<CounterState>({
  increment: {
    events: ["counter.increment"],
    fields: ["count"],
    by: [1],
  },
});

const next = dispatchEvent({ count: 0 }, "counter.increment", DISPATCH);
// => { count: 1 }
```

React usage needs only framework state:

```ts
const [state, setState] = useState<CounterState>({ count: 0 });
const send = (event: string) => {
  setState((s) => dispatchEvent(s, event, DISPATCH));
};
```

Vue usage is equally small:

```ts
const state = ref<CounterState>({ count: 0 });
const send = (event: string) => {
  state.value = dispatchEvent(state.value, event, DISPATCH);
};
```

No MachinaDispatch hook, provider, router, or store runtime is required.

## When to use MachinaDispatch

Use MachinaDispatch when an event can be expressed as a simple field transition:

- `field = value`
- `field = !field`
- `field += n`
- `field = event suffix`

If behavior needs async effects, timers, retries, guards, hierarchical states, trace/replay, or orchestration, keep that in real app logic (or Dominatus/userland code) and keep MachinaDispatch as the tiny deterministic table layer.

If Dominatus would be overkill, you probably need a table, not a state manager.

## Non-goals

No hooks, composables, browser history, URL parsing, router trees, middleware, subscriptions, async actions/loaders, or global state runtime.
