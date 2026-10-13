# React state and input handling audit

Copeland already has most of the authoring model: an immutable component state
value, typed `on` declarations, reaction FLOWs, and explicit execution FLOW frames.
The missing React capability is delivery: component handlers are not callable
source endpoints, and ordinary JavaScript component emission does not preserve
state across renders. A small syntax pass should expose the existing model while
the React adapter supplies instance lifetime and typed event delivery.

This is an audit and design proposal. No production compiler, grammar, renderer,
or runtime changes were made. The preceding semantic UI work remains intact.

## Keep data and control state distinct

| Construct | Responsibility | Proposed frontend use |
|---|---|---|
| Component `state model: Model = initial` | One immutable data snapshot owned by a component instance | Read fields and replace the whole snapshot through `on` |
| FLOW `state Waiting` | A named control position | Describe progress, suspension, and explicit continuations |
| FLOW `board` | Persistent data for an activation | Keep counters, progress, and other machine data |
| `on Event(...)` | Typed input handling within its owner | Compute a component snapshot or a reaction FLOW transition |
| Execution FLOW `start` and `step` | Explicit machine advancement | Store a frame in component data and advance it on an explicit input |

Do not turn component `state` into an implicit FLOW, or alias FLOW `board` to
`state`. A frame's control tag and its persistent data have different jobs.
Immutable record replacement with `with` remains the normal data update operation.

## What Concept establishes

Concept separates outer shared automata storage, per-frame machine storage, and
transient state-body locals. Explicit value, reference, and owned captures govern
what may persist. `state.field` selects the shared environment; `machine.field`
selects machine storage. Those storage and ownership rules are useful precedents,
but Copeland's immutable records do not need C-style reference capture syntax.

`Step` advances the selected machine or current top frame explicitly. Bare
`yield;` retains persistent storage and re-enters the same state from its beginning
on the next step. It does not save an instruction pointer. The bounded inline
stack has capacity eight, explicit parent continuations, and independent storage
for repeated activations of the same machine. Its frames share the outer automata
environment; that is distinct from Copeland's per-activation FLOW board.

Concept input reactions accept an enum with typed payload patterns. Their guarded
reactions are an unordered set: all eligible guards are evaluated once, exactly
one may run, and multiple successful guards produce `Ambiguous` without changing
state. Ordered `transition match` has a separate first-match rule. Reaction-only
states and internal body states may be siblings, but a single state cannot mix
the two bodies.

These rules are specified in Concept's current
[language specification](C:/Users/yuech/source/repos/Concept/docs/spec/CONCEPT_EVT1_LANGUAGE_SPEC.md:1022)
and exercised by the automata state, reaction, and machine-stack tests. The focused
run passed ten test groups, including strict C11 execution and reaction Normal and
Verify modes. Input reactions are still rejected by the native EVT2 LIR path with
`EVT2_UNSUPPORTED_AUTOMATA_INPUT`, as asserted in
[lir_machine_test.go](C:/Users/yuech/source/repos/Concept/internal/concept/lir_machine_test.go:167).
This audit does not claim equivalent reaction support across every Concept backend.

Bring back the separation of persistence, input, and control, plus explicit
advancement and lifetime. Keep component M0's one-handler-per-event rule; importing
Concept's guard arbitration is a separate language change.

## Current Copeland evidence

The saved [compiler report](evidence/react-state-on-audit/compiler-report.json)
contains fourteen specimens. Every expected acceptance or diagnostic matched.
An accepted specimen establishes binding and emission, not complete React delivery.

| Specimen | Current result | Implication |
|---|---|---|
| `state current: int = 0; on Increment() => current + 1;` | Accepted | Existing declaration syntax is sufficient for the model |
| `state state: Model = ...` followed by `state.count` | Accepted | Familiar field access already works when the binding is named `state` |
| `state: Model = ...` | `COPE-PARSE-0004` | The desired spelling needs a small shorthand |
| `on.Increment` or bare `Increment` as a callback | `COPE-BIND-0001` | A handler declaration is not currently a callable endpoint |
| Direct state assignment | `COPE-BIND-0003` | State is readonly; handlers return a replacement |
| Two component state declarations | `COPE-COMPONENT-STATE-0002` | M0 owns one aggregate snapshot |
| `on Apply(event: Event) => Reduce(current, event)` | Accepted | Existing domain enums and reducers fit typed `on` |
| Reaction FLOW `Door.start()` in Copeland source | `COPE-BIND-0001` | The generated backend facade is not a typed source session API |
| Execution FLOW `Once.start(42)` and `Once.step(...)` | Accepted; emitted result is `42` | Typed explicit execution frames already work in ordinary expressions |
| Reaction and execution states in one execution FLOW | `COPE-FLOW-EXEC-0001` | The event-to-frame adapter is not implemented |
| Direct FLOW calls in component state/handler nodes | `COPE-BIND-0001` | Execution projection omits these AST nodes |
| The same calls through ordinary typed helpers | Accepted | Frame storage is admitted; the missing traversal is local |
| A handler calling a helper that mutates a caller's array | Accepted | Handler type checking does not establish transition purity |

The owning code paths explain these results:

- [Binder.cs](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs:5585)
  admits one readonly state value inside a `ReactNode` function and checks typed
  handler parameters and whole-value results. Handler names are not declared as
  lexical functions or members of an `on` namespace.
- [MirLowerer.cs](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs:1132)
  lowers ordinary component state to a local initializer and drops handler
  declarations from the ordinary function body. The emitted counter returns zero
  on both direct calls and exports only `Counter`. The
  [emission observation](evidence/react-state-on-audit/emission-observations.json)
  uses real React 19.2.7 in Node; it is not mounted React lifecycle qualification.
- [ComponentFrameArtifactEmitter.cs](../../../src/Copeland/Copeland.Cli/ComponentFrameArtifactEmitter.cs:20)
  handles canonical attached browser frames for the bounded CustomElement path:
  string or nullary enum state, zero-payload events, and supported constant/match
  transitions. It is not a general record-state React emitter. Ordinary unattached
  function calls can bypass this path, so successful compilation alone is weak
  evidence of frontend state support.
- [ComponentStateRuntime.cs](../../../src/Copeland/Copeland.TS/Semantics/Bound/ComponentStateRuntime.cs:176)
  already owns typed dispatch, current state, presentation validation, attachment
  reconciliation, phased effects, completion delivery, and destruction. Reuse its
  semantic contract and canonical identities rather than adding a second state
  authority in React.
- [FlowExecutionProjection.cs](../../../src/Copeland/Copeland.TS/Syntax/FlowExecutionProjection.cs:370)
  rewrites ordinary declarations and expressions but does not traverse component
  state initializers or handler expressions. Its execution-profile checks also
  explicitly reject reaction mixing.

There is a purity prerequisite. The component binder accepts external mutation
through a helper. The shared
[effect classifier](../../../src/Copeland/Copeland.TS/Semantics/FunctionEffects.cs:493)
classifies mutable-array writes as `LocalMutation` without proving that the
receiver is fresh local storage. Existing static-safe facts cannot simply be
treated as proof of an isolated UI transaction. Fix or bound qualification at
that existing owner, distinguishing local collection builders from writes through
input or captured aliases. The M5 reducer uses generic collection helpers and
callbacks, so the real reducer must be qualified rather than replaced by a
simpler counter to obtain a passing demonstration.

## Recommended authoring contract

The next syntax slice should allow this **proposed**, currently unsupported code:

```typescript
function Application(width: int): ReactNode {
    state: AppState = InitialState(width);

    on Apply(event: AppEvent) => Reduce(state, event);

    return View(state, on.Apply);
}
```

`state: AppState` normalizes to the existing declaration with a binding named
`state`. Keep `state model: Model` for authors who prefer a domain name. This
shorthand belongs only in the component declaration context; named FLOW states
retain their current grammar.

`on.Apply` is an instance-bound callable with type `(event: AppEvent) => void`.
Taking the endpoint does not dispatch. Calling it delivers input to the owning
canonical frame. This fits the semantic UI library's existing typed `send`
parameter without changes to actions, choices, records, or React projections.
Use one `Apply` carrying the existing `AppEvent` enum for the first migration.
Direct typed handlers such as `on Select(id: int)` can expose corresponding
`on.Select` endpoints through the same mechanism; no inferred `onWhatever`
aliases or second event bus are needed.

The runtime contract must be explicit:

1. Initialize state once per component lifetime. Presentation recomputation reads
   the current snapshot and does not rerun initialization. Later prop changes do
   not silently reset state; prop-driven updates need an explicit input policy.
2. An endpoint retains owner identity and lifetime, not a captured render snapshot.
   Each delivery reads the latest committed state. Two component instances remain
   isolated, and an endpoint obtained from an earlier render still addresses its
   surviving owner.
3. Ordinary explicit capture of `state` remains capture of an immutable snapshot.
   Explicit capture of `on.Apply` retains an endpoint whose validity is checked on
   delivery. Do not silently convert snapshot capture into a live state reference.
4. A transition computes the replacement from current state and typed payload.
   External actions belong in the existing declared effect pipeline. The first
   React slice must qualify permitted helpers and reject unsupported mutation or
   capture with an exact repair. Existing inherited render-local scope is not yet
   a defined handler capture policy.
5. Unmount destroys the owner and invalidates retained endpoints. Late deliveries
   must use the existing destroyed-owner diagnostic policy and cannot recreate
   state. Dynamic keyed child ownership needs its own qualification before it is
   advertised; start with the application root owner.
6. React presentation acknowledgement must represent an actual commit before
   `PresentationCommitted` effects run. Calling `root.render` alone is not that
   acknowledgement. Keep effects unsupported in the first React slice if the
   adapter cannot yet establish this phase contract.

The existing component facts, frame/bridge contract, and attachment ownership
should remain the authority. React schedules and projects presentation. The
adapter still needs concrete JavaScript frame emission and lifecycle integration;
the C# runtime's existence does not complete those pieces.

## Compose explicit machines without hiding their clock

A component may hold a typed execution frame as its data snapshot and declare
`on Tick() => CounterFlow.step(frame)`. Fix the projection traversal so that
`CounterFlow.start(...)` and `CounterFlow.step(...)` work directly in those nodes.
Ordinary helper wrappers are evidence that this gap is narrow, not the intended
authoring convention.

Each Tick advances exactly one explicit step under the existing FLOW rules.
Yield, child push, completion, failure, and overflow retain their established
semantics. Rendering does not step the machine, and a handler does not implicitly
drain it until completion. Timers and asynchronous work enter through declared
inputs/effects when that adapter path is qualified.

Do not combine reaction and execution FLOW profiles in this milestone. Their
missing event-to-frame adapter is independent of making component `on` usable
with React. Also retain the current reaction FLOW duplicate-handler policy rather
than suggesting Concept's multiple guarded candidates are already supported.

## Next bounded implementation milestone

Implement the missing execution-projection traversal first, including state
initializers, handler next-state expressions, and applicable effect/completion
arguments. Add the shorthand and typed `on.Name` binding to the existing component
facts, then emit root-owned React state and input delivery through that model.
Qualify helper purity/captures at the owning semantic seam before enabling the
real reducer. Keep unsupported effects and dynamic child ownership diagnostic.

Migrate the M5 application from its manual `dispatchReact` closure to component
`state` plus `on Apply`. Preserve `AppEvent`, `Reduce`, semantic UI records, and
layout streams. This makes the application express its actual state/input model
without a new state engine or broad rewrite of the presentation library.

Acceptance must use the real mounted application: repeated search, select,
insert, remove, reverse, resize, and notice workflows; rerender without state
reset; rapid inputs and retained endpoints using latest state; two-owner isolation;
and unmount followed by a late input. A mounted FLOW specimen must demonstrate
explicit ticking and stack outcomes. Include negative alias mutation/capture
specimens and validate local immutable collection builders. A counter alone is
not acceptance for the issue-triage reducer.

## Validation and reproduction

Current audit validation passed fourteen compiler expectations, forty focused
.NET frontend tests, eight focused JavaScript backend tests, TypeScript checking
of the audit tooling, emitted React/explicit-FLOW observations, and ten Concept
Go test groups including C11 execution. Concept remained clean after the run.
The full Copeland suite was not rerun for this audit, and no mounted React state
implementation was qualified. Exact commands and source hashes are in
[validation.json](evidence/react-state-on-audit/validation.json).

From the Copeland root, reproduce the new specimens and emission observations:

```powershell
dotnet run --project samples/copeland-ts/react-friction-m0/tooling/compiler-probes/CompilerProbes.csproj -- samples/copeland-ts/react-friction-m0/inputs/state-on-audit-corpus.json docs/Copeland/reviews/evidence/react-state-on-audit/compiler-report.json
node samples/copeland-ts/react-friction-m0/baseline/node_modules/typescript/bin/tsc --project samples/copeland-ts/react-friction-m0/tooling/tsconfig.json
node samples/copeland-ts/react-friction-m0/tooling/state-on-audit.ts
```
