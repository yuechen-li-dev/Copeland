# Copeland TS async and suspension automata (CTS-ASYNC-M1)

**Status:** CTS-ASYNC-M1 implementation slice complete. The compiler-local automaton contract, shared MIR validation, executable control-plan lowering, named-function backend realization, core expression continuation lowering, and typed lexical recovery execute through the shared plan. Broader expression families are explicitly deferred to CTS-ASYNC-M2.

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

The frontend reserves lowercase `async`/`await`, parses `Async<T>` as a compiler-owned type, and binds a named async call as `Async<eventual-return>`. `await` checks that it is in an async function and consumes an `Async<T>` to `T`. Parsing deliberately groups `await operation?` as `(await operation)?`. Async values already have a dedicated equality rejection. Named functions now emit compiler-owned C# and JavaScript carriers, frames, and explicit resume switches; neither emitter uses host `async`/`await`. The bounded lowering covers statement/return states plus `if`, `while`, `for`, `break`, and `continue`. It lowers await-bearing arithmetic, calls, assignments, returns, conditional expressions, and `&&`/`||` into expression continuation states; short-circuiting keeps its authored operand order. Each suspension has separate compiler-selected carrier and resumed-value frame slots, so target emission never relies on traversal order. `MirAsyncPropagateExecutionState` represents both function-return Result propagation and lexical `except` transfer: the latter writes its typed error payload to a compiler-owned frame slot and jumps to an explicit handler entry state. The handler and protected value blocks retain their locals in frame slots, so an `await operation?` can suspend before success or recovery without reordering effects. Async arrows, callable-type identity, associated functions, table/TSON expression suspension, and match/result expression transfer across suspension remain pending.

The generated runtime also has an internal pending-computation seam. It is not addressable from Copeland source and is used by backend tests and future transport integration. A carrier has pending, resolved, cancelled, and panicked terminal states; the first terminal action wins, late actions are ignored, and a cancellation continuation prevents authored success code from resuming. This is a completion seam only: it does not introduce sidecar transport, host Task/Promise interop, or transport failure semantics.

The shared validator runs before backend emission and rejects malformed automata under the existing malformed-MIR validation channel: blank/duplicate state or slot IDs, missing/non-entry entry state, missing owner, non-Async await slot, resume mismatch, completion mismatch, invalid cancellation edge, unknown transition endpoint, nonterminal dead end, unreachable generated state, incompatible executable frame reads/writes, invalid Result propagation slot types, malformed lexical handler transfers, and configured resource limits.

Async control splitting is compiler-owned as `MirAsyncExecutionPlan`: statement, return, branch, jump, await, evaluation, and Result-propagation states use stable identities and explicit successor edges. Awaiting states carry both their carrier and resumed-value `MirFrameSlotId`; compiler-only frame-slot expressions let subsequent states read retained values. Shared validation verifies executable-state identity, entry and target existence, await-slot ownership/type, resumed-value compatibility, evaluation-slot compatibility, propagation/handler compatibility, and rejects unsplit structured statements before either backend emits its local switch labels.

Current bounded defaults are 256 states, 512 transitions, 128 suspension points, 256 frame slots, 32 structured nesting, and 8,192 worklist steps. The first backend-neutral representation remains compiler-local; it does not reference Dominatus, MachinaLayout.JS, or Oct and is not a shared package.

## Deferred CTS-ASYNC-M2 integration

CTS-ASYNC-M2 owns async arrows/callable identity and associated functions; table/TSON/enum-match/Result-match expression transfer across suspension; source-level cancellation delivery; corpus/artifact parity; and the broader fixture and diagnostic inventory. Enum and Result match specifically require a plan-owned dispatch state that evaluates the scrutinee once, copies selected payloads into retained binding slots, and jumps to an explicit lowered arm state. No host `async`/`await`, host Promise rejection, or host Task fault may become source semantics during that work.

CTS-SIDECAR-M1 remains sequenced after this explicit realization. It will extend the pending-computation completion seam with transport outcomes without mapping them to declared Result errors.

## TSON interop transport slice

The first bounded interop path uses the compiler intrinsic
`tsonCall<Response, RemoteError>(operation, request)`. It is valid only with a
unit `$schema` and nominal request/response/remote-error records whose fields
are primitive TSON values. It returns `Async<Response ! RemoteError>`: an
`ok` envelope resolves the authored `Result`, while a `remote-error` envelope
resolves its declared `err` payload. Cancellation, connection loss, malformed
envelopes, malformed TSON, and incompatible payloads never become that
authored error type.

Both targets generate the same canonical TSON request document and a private
canonical TSON `Envelope` carrying correlation, kind, operation, and payload.
The generated adapter owns the outstanding-correlation table. A correlation is
removed before terminal delivery, so duplicate, unknown, and late envelopes are
rejected without re-entering an authored continuation. Connection loss settles
every remaining computation as a distinct transport-failed carrier terminal;
cancellation and invariant panic remain separate terminals. The adapter exposes
no Copeland `Task`, `Promise`, socket, stream, or process type.
