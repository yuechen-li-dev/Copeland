# CTS React effects and keyed children M7

Declared React endpoint effects now execute through accepted canonical frame
transitions. Stateful child collections have explicit keyed ownership. The new
editable search-results page exercises both through compiled Copeland source;
its browser TypeScript host performs HTTP I/O and DOM focus only.

## Authoring

The application uses the existing effect grammar with a typed completion result:

```typescript
on Found(request: int, response: string ! string) => AcceptSearch(state, request, response);
on Search(query: string) => state with { request: state.request + 1, query }
    effect Search(query) => Found(state.request + 1, result);
```

An effect invokes a named typed function. It can return a synchronous value or an
`Async<T>` computation; `result` is bound to its eventual type inside completion
arguments. Void effects do not introduce that binding. A conflicting state or
payload named `result` gets `COPE-COMPONENT-EFFECT-0108`. Typed completion arity
and argument compatibility retain the existing component diagnostics.

Effect arguments are evaluated as pure captures before committing the transition.
Completion arguments use the original state/event snapshot plus the returned
result, and must also qualify as pure. External work inside either argument list
gets `COPE-COMPONENT-EFFECT-0107`. Effect launch occurs only after both transition
and presentation evaluation succeed. Completion reduces against the current
canonical state; it never directly replaces state.

React endpoint owners default to `StateCommitted`, including named state owners
that take `on.Name`. Legacy component capsules retain their previous default.

| Phase | React behavior |
| --- | --- |
| `StateCommitted` | Launch once for each accepted transition request, independently of React batching. |
| `PresentationCommitted` | Launch after the matching React presentation revision commits. A newer state revision cancels a waiting older presentation request. |
| `AttachmentsSettled` | Rejected with `COPE-COMPONENT-ON-0005`: this adapter has no declared participant acknowledgement bridge yet. |

Request identity is the owner lifetime, monotonic transition revision, and effect
declaration identity. Source order determines launch order within each phase.
An inline completion is queued until the transition's remaining phase requests
have launched or been superseded. Completion delivery cannot nest inside a
transition or phase. Async completion order is determined by external work;
application state decides whether an older request remains relevant. The demo
keeps an explicit search request number and ignores stale search results.

Effect failures after commit are diagnostic consequences, not rollback of already
committed state or external work. Declared `T ! Error` responses can instead
become explicit success/failure state through matching, as demonstrated by save
status. No new implicit FLOW stepping, coroutine continuation, or clock is added.

## Child ownership

```typescript
function PresentRow(row: Row): ReactNode {
    return ownChild("editors", row.id, Editor(row.id, row.title));
}
```

`ownChild` is compiler-realized over the existing browser frame interface. It
requires a nonempty slot and string domain key, and a direct stateful Copeland
component element. Pure helpers may construct these descriptors; construction
does not register a frame or acquire resources.

The parent owns membership. A child lifetime is identified by parent lifetime,
slot, domain key, component definition, and a fresh generation. React keys project
that identity. Reordering preserves frames and endpoints. Removal retires the
frame immediately when the parent transition commits, including pending work and
retained endpoints. Reusing the key creates a new generation and initial state.
Changing component definition also replaces the lifetime. Input changes preserve
initialization; state changes remain explicit events.

Duplicate keys within one parent slot get `COPE-COMPONENT-CHILD-0003` during
presentation evaluation. That aborts the candidate transition before state,
ownership, or effects commit. A direct stateful child without an ownership
wrapper gets `COPE-COMPONENT-CHILD-0004`. An ownership wrapper without a canonical
parent gets `COPE-COMPONENT-CHILD-0005`. Ordinary data rows need no child frame.

The adapter returns retained child definitions through the canonical registry's
existing `{ plans, frames }` projection contract. Returning an empty frame list
would mean semantic removal; it is not a neutral renderer update. Parent identity
is populated rather than left null. Root destruction retires the full subtree.

## Lifetimes and supported profile

React attachment leases are distinct from owner retirement. StrictMode cleanup
releases a lease; synchronous reconnect retains the same frame and request
ledger. An unpaired cleanup retires at the microtask boundary. Detached endpoints
reject immediately, and an already retired owner cannot be registered again.
Parent removal independently retires owned children before React teardown.

This microtask policy is qualified for the synchronous client profile used by the
fixture with React 19.2.7. Concurrent rendering, Suspense, SSR, retained children,
and cross-parent lifetime transfer are unqualified and must not be inferred from
these tests. The browser adapter accepts presentation closures at commit rather
than replacing the committed presenter during rendering.

Destruction cancels owned Copeland computations and discards their completions.
It does not promise to undo a save or abort every underlying foreign Promise.
External delivery is not globally exactly-once. Reversible viewport subscriptions
retain their existing paired acquisition/cleanup; this milestone does not add a
general subscription declaration or automatic dependency-array business effects.

## Implementation and friction fixes

Typed MIR retains effect identities, phases, execution helpers, argument captures,
and completion interfaces. Validation checks their signatures before emission,
and text MIR prints the descriptors. The effect authority still owns purity;
stateful component calls in presentation are classified as inert constructors,
with each owner's initializer and presentation qualified separately.

The module call audit now distinguishes event declarations and completion targets
from ordinary calls, so `on Reverse()` does not accidentally refer to another
module's private `Reverse` helper. Promise interop without a stable `$schema`
produces `COPE-NPM-0010` rather than the encountered compiler exception. The demo
uses the existing schema and `string ! string` transport contracts.

## Evidence and reproduction

The final full solution build has zero warnings/errors. All **2,212 .NET tests**
pass across eight suites; all **55 compiler expectations** match. Twelve new
mounted lifecycle scenarios pass in each of Diagnostic, Symbolic, and Production.
These cover StrictMode command deduplication, keyed reorder, focus supersession,
out-of-order searches, fresh reinsertion, repeated saves, typed save failure,
retired completion/endpoint rejection, rollback, phase/completion ordering, root
teardown, and the standalone page's actual HTTP service path. Cross-module child
construction is exercised by the separate negative/phase probe module.

The existing seven state/FLOW scenarios also pass in every profile, along with
74 paired webpage observations, four semantic-library assertions, and the
original website's three viewport regressions. Microsoft Edge 153.0.4234.48 on
Windows is the qualified browser. The legacy Zig suites were skipped because
neither that compiler nor its build/test infrastructure changed.

Exact totals, logs, and hashes are in
[evidence/react-effects-children-m7/validation.json](evidence/react-effects-children-m7/validation.json).
The lifecycle reports contain the expected negative diagnostics; those are
separate from clean browser errors and console output.

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -FullValidation
node samples/copeland-ts/react-friction-m0/tooling/lifecycle.ts --serve
```

The second command starts a foreground sample host and prints its temporary URL.
Ctrl+C closes it. Its service returns deterministic all/one/none result scenarios
and echoes saved drafts; it is a qualification fixture, not durable persistence.
The authored application is `inputs/lifecycle-demo.tsx`; runtime-only negative
specimens and typed probe senders are in `inputs/lifecycle-probe.tsx`.
