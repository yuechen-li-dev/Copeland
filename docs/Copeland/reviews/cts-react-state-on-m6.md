# CTS React state and inputs M6

The real issue-triage application now declares its state and typed input endpoint
in the language. The manual `dispatchReact` closure has been removed. Existing
`AppEvent`, `Reduce`, immutable issue rows, semantic UI records, layout templates,
and contribution streams remain the application model.

```typescript
export function Application(width: int): ReactNode {
    state: AppState = InitialState(width);

    on Apply(event: AppEvent) => Reduce(state, event);

    return presentViewport(View(state, on.Apply),
        capture { send: on.Apply } (width: int) => send(AppEvent.Resize(width)));
}
```

`Main` creates a React root and renders `Application(getViewportWidth())`.
`presentViewport` is a compiler-realized browser projection: its React effect
attaches resize/orientation inputs and removes the listeners on teardown.
It is emitted by the compiler, rather than imported as a physical host export.

## Authoring and lifetime

`state: Model` creates the existing component state declaration with a binding
named `state`. `state model: Model` remains available; taking `on.Name` selects
the new React endpoint realization for that named binding. Legacy named state
capsules without endpoint references retain their M0 realization for compatibility.
The shorthand selects the React realization even without any handlers.
Named execution FLOW states retain their current grammar.

`on Apply(event: AppEvent) => ...` declares a replacement-state transition.
`on.Apply` is an instance-bound `(event: AppEvent) => void` capability. Taking it
does not dispatch. Each call delivers input to the surviving owner and reads its
latest committed state. State initialization is retained for the owner lifetime;
changing presentation inputs does not reset state. Prop-driven state changes
must be explicit inputs.

Explicit capture preserves the distinction between data and capabilities:

```typescript
capture { state, send: on.Restore } () => send(state.count)
```

Here `state` is an immutable snapshot; `send` addresses the live owner. Implicit
`on.Name` access inside an arrow gets a capture repair. Endpoints cannot be used
outside their owner or before their handler declaration. A lexical binding named
`on` retains ordinary lexical lookup.

Unmount destroys the canonical frame and invalidates retained endpoints. A late
delivery records `COPE-COMPONENT-STATE-0103` and cannot recreate the state. React
StrictMode effect replay re-registers the retained snapshot without resetting it.

## Authority and transaction ordering

The adapter uses the existing `@copeland/browser-v1` APIs:
`registerComponentFrames`, `dispatchComponentEvent`, and `destroyComponentFrame`.
That registry owns current state and event delivery. The React adapter stores
immutable presentation snapshots, stable typed endpoints, subscriptions, and
bounded diagnostic/commit observations. It does not introduce a second reducer
store, event bus, or scheduler. React's `useSyncExternalStore` subscribes to those
snapshots; `useLayoutEffect` acknowledges actual presentation commits.

The compiler lowers initializer, presentation, and transitions to typed ordinary
MIR functions and retains their interface in `MirComponentStateModel`. MIR
validation checks referenced functions, signatures, endpoint identities, and
generated host collisions. Node and C# emission return explicit adapter diagnostics.

Before returning a replacement to the canonical dispatcher, the adapter validates
the typed argument tuple, computes the transition, and evaluates presentation.
A transition or presentation failure therefore leaves both canonical state and
the previous presentation unchanged. Only after this succeeds does the canonical
registry replace state and publish the staged presentation. Primitive integer
inputs require signed 32-bit integers; array input checks include every element
and reject holes. Immutable array inputs must also be frozen, so external mutable
arrays cannot become live state aliases. Existing nominal carrier validators guard records/enums.

The generated browser entry waits for the existing frame-artifact loader before
starting the application. This removes the observed race between loading static
frame artifacts and registering dynamic executable React transitions.

## Qualification and diagnostics

Qualification uses the existing bound effect inspector. It resolves closed helper
calls, explicit captures, and callable fields in immutable records at the actual
call site. The real M5 `Reduce` and its generic collection operations qualify.
Fresh readonly local `MutableArray` builders may be filled and frozen. Writes
through inputs or aliases are rejected. Generic specialization now preserves
local symbol identity, so allocation ownership survives specialization.

Ordinary function classification and reaction FLOW retain their conservative
indirect-call rule. Closed transition/presentation qualification does not relax
that separate contract. Nested execution match bindings are recognized during
inspection, including generated child-frame continuations. Closed helper
qualification stops after 512 distinct contexts with a diagnostic.

| Diagnostic | Repair |
| --- | --- |
| `COPE-COMPONENT-ON-0001` | Declare the handler in this owner before taking its endpoint. |
| `COPE-COMPONENT-ON-0002` | Explicitly capture an endpoint alias in the arrow. |
| `COPE-COMPONENT-ON-0003` | Use one top-level state in a closed ordinary component; avoid reserved endpoint-name conflicts. |
| `COPE-COMPONENT-ON-0004` | Initialize from inputs and qualified pure helpers. |
| `COPE-COMPONENT-ON-0005` | React endpoint effects are deferred; use a state-only handler in this slice. |
| `COPE-COMPONENT-ON-0006` | Rename a payload that shadows the state binding. |
| `COPE-COMPONENT-ON-0007` | Use current state, typed payload, and pure helpers; send render-local values as payload. |
| `COPE-COMPONENT-ON-0008` | Construct presentation without dispatch or external work; defer actions to explicit callbacks. |
| `COPE-COMPONENT-ON-0009` | Store immutable data; freeze construction storage and keep host handles outside state. |

## Explicit machines and supported boundary

Execution projection now traverses component state initializers, handler next
state, and declared effect/completion arguments. A component can write
`state: CountingFrame = Counting.start(limit)` and
`on Tick() => Counting.step(state)` directly. Rendering never ticks the machine.
Mounted specimens prove one-step Tick, yield/completion, typed child push/pop,
success/failure continuations, and persistent bounded-stack overflow faults.

Declared React effects remain rejected even though actual React commit is observed:
effect scheduling/completion and attachment settlement are not qualified here.
Dynamic keyed stateful child ownership, concurrent/Suspense/SSR behavior, and
reaction/execution profile mixing are also deferred. Pure semantic UI helpers,
immutable keyed rows, and root-owned state are the qualified application path.
The existing bounded CustomElement wire envelope has not been widened to arbitrary
records or payloads; executable React transitions use the registry's existing
generic frame-definition API.

## Evidence and reproduction

The final solution build has zero warnings/errors, and all **2,209 .NET tests**
pass. Fifty compiler expectations match, including the actual application and
negative capture/mutation/presentation cases. The mounted application passes
74 paired browser observations with clean diagnostics. Seven additional lifecycle
and FLOW scenarios pass in each of Diagnostic, Symbolic, and Production, including
malformed integer/array payloads and rollback. Four semantic-library scenarios
and the original website's three viewport regressions also pass. The known
fixed-height prose overlap remains an intentional negative specimen.

Exact commands, hashes, and test totals are recorded in
[validation.json](evidence/react-state-on-m6/validation.json), alongside the
compiler, browser, lifecycle, semantic-library, website, and source-accounting
reports. Evidence uses Microsoft Edge 153.0.4234.48 on this Windows machine;
other browsers and platforms have not been qualified. The legacy Zig suites were
skipped because neither the frozen compiler nor its build/test infrastructure changed.

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -FullValidation
```

The runner builds both applications, checks the TypeScript baseline/tooling,
compiles the corpus, runs mounted probes in all three emission profiles, and
checks the original website. Full .NET build/tests are enabled by `-FullValidation`.
Existing M5 changes and historical reports remain intact.
