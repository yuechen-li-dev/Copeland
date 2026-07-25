# Copeland TS async and suspension automata (CTS-ASYNC-M1)

**Status:** implementation in progress. The compiler-local automaton contract, shared MIR validation, executable control-plan lowering, and bounded named-function backend realization are implemented. General suspension lowering and full backend execution remain pending.

## Ratified implementation direction

```text
async function
= eager typed computation
+ explicit suspension automaton
+ retained live frame
+ resolved success/Result/cancellation outcome
```

```text
await
= suspend current automaton if incomplete
+ resume exactly once
+ produce the awaited Copeland value
```

`Async<T>` is a compiler-owned structural type. It is neither a JavaScript `Promise` alias nor a CLR `Task` alias. An async function's written return annotation remains its eventual Copeland result; invoking it creates `Async<eventual-result>`. Nested `Async` is never flattened implicitly.

## Implemented compiler-local substrate

`Copeland.TS.Mir` now owns `MirSuspensionAutomaton`, stable state and frame-slot identities, explicit suspension/completion/cancellation/invariant states, typed transitions, and `MirAsyncType`/`MirAsyncCallableType`. A synchronous `MirFunction` cannot reference an automaton; an async function must own one.

The frontend reserves lowercase `async`/`await`, parses `Async<T>` as a compiler-owned type, and binds a named async call as `Async<eventual-return>`. `await` checks that it is in an async function and consumes an `Async<T>` to `T`. Parsing deliberately groups `await operation?` as `(await operation)?`. Async values already have a dedicated equality rejection. Named functions now emit compiler-owned C# and JavaScript carriers, frames, and explicit resume switches; neither emitter uses host `async`/`await`. The bounded lowering covers statement/return states plus `if`, `while`, `for`, `break`, and `continue`; each supported direct await stores its compiler-selected frame slot in the shared plan, so target emission never relies on traversal order. `await operation?` propagates a completed typed Result success or error through the supported declaration shape. Async arrows, callable-type identity, associated functions, TSON/table exclusions, nested expression suspension, and lexical `try`/`except` transfer remain pending.

The generated runtime also has an internal pending-computation seam. It is not addressable from Copeland source and is used by backend tests and future transport integration. A carrier has pending, resolved, cancelled, and panicked terminal states; the first terminal action wins, late actions are ignored, and a cancellation continuation prevents authored success code from resuming. This is a completion seam only: it does not introduce sidecar transport, host Task/Promise interop, or transport failure semantics.

The shared validator runs before backend emission and rejects malformed automata under the existing malformed-MIR validation channel: blank/duplicate state or slot IDs, missing/non-entry entry state, missing owner, non-Async await slot, resume mismatch, completion mismatch, invalid cancellation edge, unknown transition endpoint, nonterminal dead end, unreachable generated state, and configured resource limits.

Async control splitting is compiler-owned as `MirAsyncExecutionPlan`: statement, return, branch, and jump states use stable identities and explicit successor edges. Awaiting statement and return states carry their exact `MirFrameSlotId`. Shared validation verifies executable-state identity, entry and target existence, await-slot ownership/type, and rejects unsplit structured statements before either backend emits its local switch labels.

Current bounded defaults are 256 states, 512 transitions, 128 suspension points, 256 frame slots, 32 structured nesting, and 8,192 worklist steps. The first backend-neutral representation remains compiler-local; it does not reference Dominatus, MachinaLayout.JS, or Oct and is not a shared package.

## Remaining M1 integration

The following must still be completed before CTS-ASYNC-M1 can be closed: async arrows/callable identity and associated functions; general structured-MIR liveness and nested-expression suspension; lexical `?`/`try` transfer beyond the bounded declaration path; source-level cancellation delivery; corpus/artifact parity; and the full fixture and diagnostic inventory. No host `async`/`await`, host Promise rejection, or host Task fault may become source semantics during that work.

CTS-SIDECAR-M1 remains sequenced after this explicit realization. It will extend the pending-computation completion seam with transport outcomes without mapping them to declared Result errors.
