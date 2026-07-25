# Copeland TS async and suspension automata (CTS-ASYNC-M1)

**Status:** implementation in progress. The compiler-local automaton contract, shared MIR validation, and initial named-function source parsing/binding for `async`, `await`, and `Async<T>` are implemented; suspension lowering and backend execution are not yet implemented.

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

The frontend reserves lowercase `async`/`await`, parses `Async<T>` as a compiler-owned type, and binds a named async call as `Async<eventual-return>`. `await` checks that it is in an async function and consumes an `Async<T>` to `T`. Parsing deliberately groups `await operation?` as `(await operation)?`. Async values already have a dedicated equality rejection. Async arrows, callable-type identity, associated functions, TSON/table exclusions, and backend consumption remain pending.

The shared validator runs before backend emission and rejects malformed automata under the existing malformed-MIR validation channel: blank/duplicate state or slot IDs, missing/non-entry entry state, missing owner, non-Async await slot, resume mismatch, completion mismatch, invalid cancellation edge, unknown transition endpoint, nonterminal dead end, unreachable generated state, and configured resource limits.

Current bounded defaults are 256 states, 512 transitions, 128 suspension points, 256 frame slots, 32 structured nesting, and 8,192 worklist steps. The first backend-neutral representation remains compiler-local; it does not reference Dominatus, MachinaLayout.JS, or Oct and is not a shared package.

## Remaining M1 integration

The following must still be completed before CTS-ASYNC-M1 can be closed: async arrows/callable identity and associated functions; structured MIR discovery/control splitting/liveness; explicit C# and JavaScript frame carriers and resume switches; source-level Result/cancellation semantics; the sidecar pending-computation seam; corpus/artifact parity; and the full fixture and diagnostic inventory. No host `async`/`await`, host Promise rejection, or host Task fault may become source semantics during that work.

CTS-SIDECAR-M1 remains sequenced after this explicit realization. It will extend the pending-computation completion seam with transport outcomes without mapping them to declared Result errors.
