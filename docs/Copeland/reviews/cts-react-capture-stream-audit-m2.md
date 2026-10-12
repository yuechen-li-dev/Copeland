# React capture and stream grammar audit (M2)

Status: audit complete; recommendations and source sketches only. No grammar,
compiler, runtime, or page implementation changes are made by this audit.

Date: 2026-10-12. Reviewed checkouts:

| Repository | Reviewed HEAD |
| --- | --- |
| Copeland | `7503e1ca6c30ba010ee9f343d85e189faee428b5` |
| Oct | `62b02c79e4519ea8066a4b7d6cecfdb3149ca1f0` |
| Concept | `68de20d19f76d336487a47e2d36d1b996ca00a07` |

All three working trees were clean when inspected. Oct and Concept were read
only. This report is the sole new repository artifact.

## Decision

Recommend **candidate A: named contributions independent of arrangement** as
the canonical UI stream meaning. Retain existing inline streams during a
prototype. Candidate B, `stream ... using Layout`, could later be convenience
syntax over that same binding model if a demonstrated use warrants it.

For callables, keep Copeland's explicit immutable environment and TS-shaped
arrow types. Bring back Oct's named capture expressions, using the proposed
TS object spelling `capture { name: expression }`. Bring back Concept's
discipline of separating environment, persistent state, and lifetime ownership;
do not import its move/ref modes without the corresponding ownership model.

This is an architectural recommendation, not a measured claim that the new
spelling is easier or that these specimens currently compile.

## What the capture audit establishes

| Property | Oct | Concept EVT1 | Current Copeland TS |
| --- | --- | --- | --- |
| Callable spelling | `fn(...) -> T with { name: expression } { ... }` | `callback(...) with (...) { ... }` | `capture { name, ... } (...) => ...` |
| Implicit outer lexical capture | Rejected | Rejected | Rejected |
| Construction | Ordered, exactly once capture expressions | Ordered, exactly once environment entries | Ordered identifier snapshots |
| Environment mutation | Captured bindings immutable | Copied/moved fields may mutate through mutable callable storage | Captured bindings immutable |
| Capture value policy | Value snapshots; arrays explicitly copied | Copy, move, mutable borrow, readonly borrow; normal provenance and Drop laws | Existing value semantics; immutable slots do not imply deep copying host/reference values |
| Public callable type | Exact signature, including fallibility; environment hidden | Concrete type includes literal/code/environment identity; explicit non-owning signature erasure | Exact structural signature; environment hidden |
| Escaping callable | Independent stored value environment | Allowed only when ownership/provenance permits; borrowed erased temporaries rejected | Independent environment may outlive creator; no Concept-style borrow-capture feature |
| Named declarations | Global lookup without local capture | Module lookup without local capture | Global declarations/static names are distinct from lexical runtime values |

### Oct: the useful source-level addition

The authoritative [function-value reference](../../../../oct/Language/reference/language/15-function-values.md)
supports renaming and expressions in captures. Each expression resolves in the
surrounding scope, is evaluated once in authored order, and installs an immutable
binding in the body. Nested callables list their dependencies independently.

The [language specimens](../../../../oct/Language/Functions/FunctionValues/valid/explicit_captures_m0.octest)
cover renamed snapshots, escaping functions, explicit nesting, and an important
array case: after capturing `[2]`, changing the source element to `10` leaves the
callback's result at `8` for input `4`.

This is implemented rather than merely specified: interpreter construction uses
`cloneValue` in [interpret.go](../../../../oct/internal/interpret/interpret.go)
at the `ast.FunctionExpr` case (line 1824), and compiled construction uses
`lowerMIRValueWithClone` in [lower_expr.go](../../../../oct/internal/build/lower_expr.go)
(line 1816). The compiled helper selects cloning for arrays, Bytes, vectors,
and matrices. Do not generalize that inspection into a proof of every possible
aggregate/backend combination. `TestExplicitCaptureLoweringRetainsTypedEnvironment`
in [compiler_test.go](../../../../oct/internal/build/compiler_test.go) checks
explicit typed MIR captures and `First`/`Second` construction call order.

The immutable-capture law does not prove callback purity. Oct's
[batch reference](../../../../oct/Language/reference/runtime/22-batch.md)
explicitly records the absence of an effect system proving arbitrary calls pure.

### Concept: ownership and storage are part of semantics

The current [specification, sections 26 and 27](../../../../Concept/docs/spec/CONCEPT_EVT1_LANGUAGE_SPEC.md)
defines copy, `move`, `ref`, `ref const`, and bounded named copy initializers.
Capture provenance includes borrowed backing storage. Owning fields drop in
reverse capture order; borrowing does not acquire ownership. Mutating a copied
environment requires mutable callable storage. This differs from both Oct and
Copeland: Concept's mutable environment can be an accumulator.

[callable_capture.go](../../../../Concept/internal/concept/callable_capture.go)
validates capture modes, copyability, provenance, body mutation/consumption,
and immutable-versus-mutable invocation. The generated representation records
ordered environment fields and ownership effects. Exact concrete types retain
code/environment identity; erased callbacks are explicit borrows of concrete
storage, with no owning closure box.

[callable_capture_test.go](../../../../Concept/internal/concept/callable_capture_test.go)
contains 20 valid and 12 invalid capture cases and checks inline environments,
allocation `None`, malformed MIR/plans, and named rejection families including
`CALLABLE_CAPTURE_LIFETIME_INVALID` and `CALLABLE_CONST_MUTATING_INVOKE`.
These tests were inspected, not executed in this audit.

Importing `ref state` into Copeland without those lifetime and storage checks
would introduce a shared-cell state mechanism with different laws. React
callbacks needing current state should instead deliver typed events to the
existing state owner, or receive current state as an explicit argument.

### Copeland: this is extension and reconciliation, not a new closure model

The implemented [CTS-CALL-M1 contract](../architecture/copeland-ts-complete-callable-semantics-cts-call-m1.md)
already defines stable code plus immutable ordered environment, escaping
callables, exact signatures, and storage in records/enums/arrays/Result.
The older M0a document is historical and must not be read as current support.

[Binder.BindCapture](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs)
(line 7587) currently accepts identifier entries only. `BindArrow` isolates the
body from uncaptured lexical state. JavaScript's `__cope_callable_capture` in
[JavaScriptBackend.cs](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs)
freezes a copied slot array with `values.slice()`, not a recursively cloned
object graph. The C# backend uses readonly environment fields.

[CallableBackendTests](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/CallableBackendTests.cs)
include escaping captures, a later `let` rebind leaving the callback at `15`,
and callable aggregate storage. The stabilization
[static-name tests](../../../tests/Copeland/Copeland.TS.Tests/UiStaticNameStabilizationTests.cs)
preserve the distinction between enum declarations and real lexical values,
including a parameter shadowing an enum name.

Proposed extension, **not accepted syntax**:

```ts
const search = capture {
    send,
    query: state.queryDraft
} () => send(AppEvent.Search(query));
```

The current equivalent declares `const query = state.queryDraft` before
`capture { send, query }`. The extension removes that auxiliary binding and
states the dependency more precisely than capturing the entire `state` record.
Each initializer should resolve in the surrounding scope, evaluate once in
source order, and create an immutable environment slot. Captures must not form
an initializer scope in which a preceding capture silently supplies a later
entry. Keep duplicate/parameter collision checks, existing limits, contextual
arrow typing, and typed fallibility; no implicit defaults/spread/destructuring.

An old callback still observes its captured query. This feature is neither a
memoization dependency list nor an automatic latest-state read. In the current
[browser dispatcher](../../../samples/copeland-ts/copeland-website-m0/runtime/browser-v1.js),
`send` reduces against the owner's `currentState` and rerenders. Capturing the
stable event endpoint is appropriate for state transitions; capturing state
is appropriate when an intentional render-time snapshot is required.

## State, FLOW, and markup boundaries

The UI can use records plus `with` for data and payload enums plus exhaustive
`match`/`switch` for loading/error/ready or dialog modes. Neither requires a
second behavior language embedded in tags. Use FLOW when explicit progression,
suspension, child execution, or completion matters; do not require a flow for
every controlled input.

The related languages agree on explicit state ownership but do not share every
control boundary. Concept bare `yield` re-enters the current state from its
beginning. Oct top-level yield continues at the next state statement, with its
documented nested-boundary restrictions. Copeland execution FLOW steps explicit
frames and has its own state-entry/child rules. Those spellings must not be
treated as interchangeable React lifecycle sugar.

Relevant current contracts are [Octomata](../../../../oct/Language/reference/runtime/21-octomata.md),
Concept specification sections 22–25, and
[Copeland execution FLOW](../language/copeland-ts-flow-execution-design.md).
Oct separates turn-local captured functions from persistent board memory and
excludes function values from boards. Concept admits explicitly stored callable
machine fields subject to concrete typing and lifetime checks. Capture and
persistence therefore need separate qualification even when a language supports
both.

Copeland [component state](cts-component-state-m0.md) already owns typed
transitions and canonical frame identities. Its documented bounded browser path
uses Custom Element presentations with restricted state/transition shapes;
the friction app still uses `dispatchReact` with an `AppState` record. The
existence of `state`/`on` or execution FLOW does not qualify a general React hook
replacement. Completing that browser delivery is a separate implementation seam.

Oct provides a useful [markup precedent](../../../../oct/Language/reference/language/21-oct-xml.md):
typed markup elaborates to ordinary calls, arrays, and literals before normal
type checking; it adds no markup runtime. Copeland should likewise keep normal
computation, matching, and events outside structural markup. Keep TS-XML where
semantic HTML, text, children, or third-party component props are clearer. Replacing
every tag with a positional `createElement` call would not demonstrate an
ergonomic improvement. Less wrapper markup is a stronger immediate target than
zero markup. Semantic headings, lists, labels, links, and keyboard interaction
must remain in the resulting document.

## Stream comparison on the actual friction case

The existing [issue pages](../../../samples/copeland-ts/react-friction-m0/src/App.tsx)
have separate `IssueWide` and `IssueStacked` functions, each binding the same
`IssueList(state, send)` and `IssueDetail(state)` content. The stored
[authoring accounting](evidence/react-ui-stabilization-m1/authoring-accounting.json)
records two binding sites for each, versus one each in the baseline. This is a
concrete reason to separate content from geometry.

| Authoring task | Candidate A: contribution stream + bind | Candidate B: stream using layout |
| --- | --- | --- |
| Initial small page | Separate arrangement costs an extra declaration | Shorter when one surface owns everything |
| Wide/stacked layouts sharing content | One authored content bundle; two explicit bindings | Two surface bodies repeat entries unless reuse is added |
| Meaning of the declaration | Record-like named contributions | Presentation factory selecting an arrangement |
| Geometry/origin ownership | Only the layout owns origin and spatial facts | The proposed stream origin duplicates/conflicts with an already concrete layout origin |
| Reuse across components | Explicit factory parameters can produce the same channel shape | Needs a separate content-reuse mechanism to become equivalent to A |
| Current migration | New contribution value and expression-bind path | Closer to current stream parser, but `using` and composition are still new |

Recommended specimen, **design sketch; does not compile today**:

```ts
function IssueBody(state: AppState, send: (event: AppEvent) => void): ReactNode {
    stream IssueContent {
        list: IssueList(state, send);
        detail: IssueDetail(state);
    }

    if (state.stacked || state.narrow) {
        return bind IssueStackedLayout using IssueContent;
    }
    return bind IssueWideLayout using IssueContent;
}
```

The separately declared layouts retain their `<0px, 0px>` origins and their
respective row/column geometry. This example uses static layout names in ordinary
control flow; it does not require first-class dynamic layout values.

The TS developer model is: functions compute content, a stream packages named
contributions like an exact props object, and a layout consumes those channels.
The compiler automates the connection, neutral hosts, and inspectable identities.
Shader metadata need not become UI decorators to express that common principle.

### Decisions required by A's semantics

1. **Use eager contribution values in the first prototype.** Evaluate content
   expressions once, in source order, when the local stream statement runs.
   Binding consumes those values and does not reevaluate the stream body. A new
   call of `IssueBody` produces current content. Exported reuse should use an
   ordinary parameterized factory; escaping factories use normal explicit
   capture. Do not silently switch between eager values and lazy factories.
2. **A content channel contract is not a layout topology contract.** Existing
   `layout type` checks exact structure. A row and a column cannot simply be
   asserted to satisfy the same exact topology. Each layout keeps its structural
   validation; binding checks its required singular slots against the content
   channels. Preserve slot semantic paths and diagnose ambiguous names. Do not
   weaken layout exactness to make content reuse work.
3. **Keep callable capture separate from local presentation parameter passing.**
   Current `BindLocalPresentation` collects dependencies into generated function
   parameters; `_localPresentationCaptureArguments` passes current values when
   its accessor is called. Existing local streams are therefore not construction-
   time escaping snapshots. A's eager bundle is an intentional new evaluation
   boundary. It cannot be implemented by merely renaming that accessor.
4. **Arrangement changes do not automatically prove state retention.** Channel
   names express content identity, but React descendants may still remount when
   host topology changes. Retention must be verified against canonical component
   instances and their runtime state owners. Do not promise it from matching
   strings alone. Binding creates its normal host structure; the bundle itself
   is neither a mount nor a singleton component instance.
5. **Begin with singular channels.** General dynamic collections, stable keys,
   collection cardinality, optional channels, and asynchronous streams require
   their own contracts. This audit does not add them or claim that A solves the
   baseline's general filtered/mapped list advantage.

## Bounded next implementation and acceptance

Implement a contribution bundle plus expression binding for one article and
the two issue arrangements through the existing bound layout/binding graph,
Machina normalizer, and React host emitter. Keep existing inline stream syntax
working. No second layout engine, state registry, renderer, or collection runtime.

Named capture initializers are a separable small callable increment; their
lowering must extend the existing ordered environment values and validation,
not introduce JS lexical cells. Either increment can be validated independently.

Acceptance should include:

- one list and one detail contribution site, shared across both arrangements;
- direct compilation and browser rendering through the normal TSPack path;
- explicit call-count/order probes distinguishing eager bundle construction,
  binding, and escaping callback construction;
- missing, extra, duplicate, ambiguous, and wrong-type channel rejection, while
  each layout retains topology/type validation;
- existing callbacks reading their original snapshot after source rebinding;
  new render callbacks reflecting new state, and dispatch reducing current state;
- resize and manual arrangement switches retaining selection, with any
  component-local lifetime/remount behavior recorded rather than inferred;
- previous long-text, font-size, keyboard, input, dialog, containment, and
  diagnostic checks preserved; source accounting compared against the baseline.

The exact new parser spelling remains a prototype question. The recommendation
settles semantic direction without claiming a usability result before authors
and real pages exercise it.

## Evidence limits

This pass inspected current specifications, compiler/runtime implementations,
positive/negative language specimens, and existing regression test bodies.
No Go, .NET, browser, native, or shader tests were rerun. The earlier M1 reports
remain historical evidence for the existing app, not qualification of any new
syntax. The proposed examples have deliberately not been placed in executable
fixtures. Only this report was added; Oct and Concept remain unchanged.
