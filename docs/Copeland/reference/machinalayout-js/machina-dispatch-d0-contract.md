> D1 status (2026-05-12): Implemented in `src/dispatch` with subpath export `machinalayout/dispatch`.

# MachinaDispatch D0 Contract

## 1. Executive summary

MachinaDispatch D0 defines a tiny, pure, framework-independent **event dispatch table** module for table-driven UI transitions.

It is intentionally narrow:

- Input: `state + event + dispatch tables`.
- Output: `next state`.
- Canonical operation: `dispatchEvent(state, event, tables)`.

It is **not** a router, store, middleware layer, effect engine, or state framework. It only maps events to one-field state updates through compact **columnar dispatch tables**.

## 2. Actual problem

Many UI transitions are repetitive and local, such as:

- set a route-like field,
- toggle a boolean,
- increment/decrement a number,
- derive a field from an event suffix.

In practice, these flows often begin as repeated event `if`/`switch` chains. The Oct Storefront M1→M7 evolution showed that these patterns become clearer and safer when represented as data tables and pure reducers.

D0 captures that lesson: repeated UI event logic should become data, while remaining a tiny pure dispatcher.

## 3. Non-goals

D1 must explicitly exclude:

- React hook,
- Vue composable,
- React Native hook/composable,
- global store,
- subscriptions,
- middleware,
- async actions/loaders,
- browser history ownership,
- URL parser,
- nested routes,
- guards,
- effects,
- undo/redo,
- persistence,
- devtools,
- reducer composition framework,
- Dominatus/Octomata-style state machine system,
- router cathedral behavior.

## 4. Design principles

1. Pure functions only.
2. No framework dependency.
3. No React/Vue/RN hooks in D1.
4. No subscriptions.
5. No global store.
6. No browser history.
7. No async loaders/actions.
8. No middleware.
9. No guards.
10. No nested routes.
11. No effects.
12. No state machine.
13. No Dominatus port.
14. No router cathedral.

Operationally:

- If no rule matches, return the original state object by reference.
- If one rule matches and produces a changed value, return a shallow copy with one field updated.
- Never mutate input state or tables.

## 5. Columnar table model

Canonical authoring model is **columnar operation-specific tables**, not repeated row objects.

Rationale:

- Reduces repeated keys (`event`, `field`, `op`) in small tables.
- Keeps intent close to spreadsheet-like authoring.
- Improves scanability for simple UI transition rules.

Canonical shape:

```ts
const dispatch = defineDispatchTables<AppState>({
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
    events: ["cart.add", "cart.remove"],
    fields: ["cartCount", "cartCount"],
    by: [1, -1],
  },
  setSuffix: {
    prefixes: ["product.inspect."],
    fields: ["selectedProduct"],
    allowedSuffixes: [PRODUCT_IDS],
  },
});
```

Table “views”:

- `set`: `event | field | value`
- `toggle`: `event | field`
- `increment`: `event | field | by`
- `setSuffix`: `prefix | field | allowedSuffixes`
- `incrementSuffix`: `prefix | field | by | allowedSuffixes`

Naming decision:

- Use plural column names (`events`, `fields`, `values`, `prefixes`) because each property is a column vector.

## 6. Type proposal

D1 should start from this model (with small refinements):

```ts
export type SetDispatchTable<TState> = {
  events: readonly string[];
  fields: readonly (keyof TState)[];
  values: readonly unknown[];
};

export type ToggleDispatchTable<TState> = {
  events: readonly string[];
  fields: readonly (keyof TState)[];
};

export type IncrementDispatchTable<TState> = {
  events: readonly string[];
  fields: readonly (keyof TState)[];
  by?: readonly number[];
};

export type PrefixSetDispatchTable<TState> = {
  prefixes: readonly string[];
  fields: readonly (keyof TState)[];
  allowedSuffixes?: readonly (readonly string[] | undefined)[];
};

export type PrefixIncrementDispatchTable<TState> = {
  prefixes: readonly string[];
  fields: readonly (keyof TState)[];
  by?: readonly number[];
  allowedSuffixes?: readonly (readonly string[] | undefined)[];
};

export type MachinaDispatchTables<TState> = {
  set?: SetDispatchTable<TState>;
  toggle?: ToggleDispatchTable<TState>;
  increment?: IncrementDispatchTable<TState>;
  setSuffix?: PrefixSetDispatchTable<TState>;
  incrementSuffix?: PrefixIncrementDispatchTable<TState>;
};

export function defineDispatchTables<TState>(
  tables: MachinaDispatchTables<TState>,
): MachinaDispatchTables<TState>;
```

D0 decisions:

- `by` defaults to `1` when omitted per row.
- `allowedSuffixes` is per-row (`index` aligned with `prefixes`/`fields`).
- Suffix mismatch against an allowed list is a **non-match**, not an error.
- `incrementSuffix` is included in D1 (simple and useful for catalog-driven count updates).
- `defineDispatchTables` should preserve type inference and perform optional lightweight runtime table-shape validation in development; `dispatchEvent` must still validate defensively.

Type limits acknowledged:

- `keyof TState` does not guarantee runtime value type at a field.
- `toggle` and `increment` require runtime value checks.
- `values` remain `unknown[]` in D1 for simplicity.
- Advanced field-aware type gymnastics are deferred.

## 7. Dispatch semantics

Primary function for D1:

```ts
dispatchEvent<TState extends Record<string, unknown>>(
  state: TState,
  event: string,
  tables: MachinaDispatchTables<TState>,
): TState
```

Matching order is fixed:

1. `set`
2. `toggle`
3. `increment`
4. `setSuffix`
5. `incrementSuffix`

Rules:

- Within a group, scan arrays in index order.
- First matching row in a group wins.
- First matching group by fixed order wins.
- No multi-action event fan-out.
- No global explicit priority system in D1.

Result behavior:

- If no match: return original `state` reference.
- If match and computed value is `Object.is(currentValue, nextValue)`: return original `state` reference.
- Else: return shallow copied state with only the target field updated.

Helper decision for D1:

- Include internal/public helpers if they reduce duplication and clarify behavior:
  - `resolveEventValue<TValue>(event, { events, values })`
  - `matchEventPrefix(event, prefix, allowedSuffixes?)`
- These helpers are acceptable D1 exports from `machinalayout/dispatch` if kept narrow and pure.

## 8. Operation semantics

### `set`

- Match: exact event string equality.
- Update: `next[field] = value`.

### `toggle`

- Match: exact event equality.
- Runtime requirement: current field value is boolean.
- Update: `next[field] = !state[field]`.
- Invalid runtime value => throw dispatch error.

### `increment`

- Match: exact event equality.
- Runtime requirement: current field value is number.
- Delta `by`:
  - default `1` if `by` omitted,
  - else use row delta,
  - must be finite.
- Update: `next[field] = state[field] + by`.
- Invalid field value or invalid `by` => throw dispatch error.

### `setSuffix`

- Match: event starts with row `prefix`.
- `suffix = event.slice(prefix.length)`.
- If row allowed list exists: suffix must be present, else non-match.
- Update: `next[field] = suffix`.

### `incrementSuffix`

- Match: event starts with row `prefix`.
- Suffix handling same as `setSuffix`.
- Runtime requirement: target field is number.
- Delta `by` default is `1`, row override when provided and finite.
- Update: numeric increment.
- Included in D1.

## 9. Error model

D1 should define a dedicated error type, separate from layout errors:

```ts
export type MachinaDispatchErrorCode =
  | "InvalidDispatchTable"
  | "InvalidDispatchField"
  | "InvalidDispatchValue"
  | "InvalidDispatchEvent";

export class MachinaDispatchError extends Error {
  readonly code: MachinaDispatchErrorCode;
}
```

Throw conditions:

- `InvalidDispatchTable`
  - column length mismatch within a table group,
  - structurally invalid table columns at runtime.
- `InvalidDispatchField`
  - referenced field key is missing in runtime state object.
- `InvalidDispatchValue`
  - toggle target is not boolean,
  - increment target is not number,
  - increment delta is non-finite.
- `InvalidDispatchEvent`
  - non-string event input,
  - non-string prefix/event entries in table misuse.

Suffix not in `allowedSuffixes` is non-match (not an error).

No diagnostics subsystem is required in D1.

## 10. Immutability semantics

D1 contract:

- Never mutate `state`.
- Never mutate `tables`.
- No match => return exact same state reference.
- Match with identity-equal value via `Object.is` => return same state reference.
- Match with changed value => return new shallow object with one updated field.

This minimizes unnecessary UI rerenders in userland frameworks.

## 11. Relationship to routing

Routing-like behavior is represented as state assignment:

```ts
type AppState = { route: "library" | "settings" };

const tables = defineDispatchTables<AppState>({
  set: {
    events: ["nav.library", "nav.settings"],
    fields: ["route", "route"],
    values: ["library", "settings"],
  },
});

const next = dispatchEvent(state, "nav.settings", tables);
```

No browser history, nested route tree, loader/action graph, or guard engine is owned by MachinaDispatch.

## 12. Relationship to MachinaLayout

Composition without coupling:

```ts
const nextState = dispatchEvent(state, event, dispatch);
const routeRows = rowsByRoute[nextState.route];
const layout = resolveLayoutRows(routeRows, rootRect);
```

Boundaries:

- MachinaDispatch does not call layout resolvers.
- MachinaLayout does not call dispatch functions.
- Userland composes both.

## 13. Package/subpath plan

Recommended packaging target for D1:

- public subpath: `machinalayout/dispatch`
- build outputs:
  - `dist/dispatch/index.js`
  - `dist/dispatch/index.d.ts`

Policy:

- no framework peer dependencies,
- no adapter dependencies,
- subpath-only initially to avoid root API bloat.

## 14. Examples

### A) Routing as state assignment

```ts
const tables = defineDispatchTables<{ route: "home" | "settings" }>({
  set: {
    events: ["nav.home", "nav.settings"],
    fields: ["route", "route"],
    values: ["home", "settings"],
  },
});
```

### B) Filter toggle

```ts
const tables = defineDispatchTables<{ newOnly: boolean }>({
  toggle: {
    events: ["filter.new"],
    fields: ["newOnly"],
  },
});
```

### C) Cart increment/decrement

```ts
const tables = defineDispatchTables<{ cartCount: number }>({
  increment: {
    events: ["cart.add", "cart.remove"],
    fields: ["cartCount", "cartCount"],
    by: [1, -1],
  },
});
```

### D) Product inspect via prefix/suffix

```ts
const PRODUCT_IDS = ["p1", "p2", "p3"] as const;

const tables = defineDispatchTables<{ selectedProduct: string | null }>({
  setSuffix: {
    prefixes: ["product.inspect."],
    fields: ["selectedProduct"],
    allowedSuffixes: [PRODUCT_IDS],
  },
});
```

### E) React userland usage (no custom hook)

```ts
const [state, setState] = useState(initialState);

function onEvent(event: string) {
  setState((s) => dispatchEvent(s, event, tables));
}
```

### F) Vue userland usage (no Pinia required)

```ts
const state = ref(initialState);

function onEvent(event: string) {
  state.value = dispatchEvent(state.value, event, tables);
}
```

### G) Integrating route state with Machina layout rows

```ts
const next = dispatchEvent(state, event, tables);
const rows = rowsByRoute[next.route];
const layout = resolveLayoutRows(rows, rootRect);
```

## 15. D1 implementation plan

1. Add `src/dispatch/types.ts`.
2. Add `src/dispatch/errors.ts` with `MachinaDispatchError` and codes.
3. Add `src/dispatch/dispatchEvent.ts`.
4. Add `src/dispatch/helpers.ts` (for `resolveEventValue`, `matchEventPrefix`) if included publicly.
5. Add `src/dispatch/index.ts`.
6. Add package subpath export `./dispatch` and build entry `dispatch/index`.
7. Add tests for dispatch semantics and error behavior.
8. Add docs/README section linking `machinalayout/dispatch`.
9. Run build/test/pack smoke checks.

## 16. D1 test plan

Planned coverage:

- set dispatch,
- toggle dispatch,
- increment dispatch,
- increment default `by`,
- increment row-specific `by`,
- setSuffix with allowed suffix,
- setSuffix with disallowed suffix returns same object,
- incrementSuffix behavior,
- fixed group order first-match behavior,
- first row wins within group,
- no match returns same object reference,
- matched identical value returns same object reference,
- matched changed value returns shallow copy,
- tables not mutated,
- table length mismatch error,
- invalid toggle field type error,
- invalid increment field type error,
- non-finite `by` error,
- missing runtime field error,
- route assignment scenario,
- React-style usage as plain function test,
- Vue-style usage as plain function test.

## 17. Risks and mitigations

- Risk: scope creep into state framework.
  - Mitigation: enforce non-goals and pure dispatch-only API.
- Risk: confusion about routing ownership.
  - Mitigation: explicitly document “routing is state assignment”; no history ownership.
- Risk: runtime type mismatch despite TS keys.
  - Mitigation: strict runtime checks + explicit dispatch error codes.
- Risk: table authoring mistakes (length mismatch).
  - Mitigation: deterministic validation and early `InvalidDispatchTable` errors.
- Risk: API bloat at package root.
  - Mitigation: ship as `machinalayout/dispatch` subpath first.
