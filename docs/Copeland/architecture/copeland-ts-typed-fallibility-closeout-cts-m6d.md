# CTS-M6d: typed fallibility closeout

**Status:** closed. CTS-M4 through CTS-M6 now form one implemented, validated Copeland TS fallibility subsystem. This record ratifies the language laws; it does not add a new fallibility feature.

## Final boundary

`T ! E` is a first-class Result value. `ok(value)` and `err(error)` construct Result values, and `match` explicitly inspects one. None of those operations transfers control.

`?` evaluates one Result operand exactly once. It yields the `ok` payload, or transfers the original `err` payload to the target selected by binding: the nearest enclosing `try` handler, otherwise the compatible enclosing fallible function return. A handler consumes only a transfer bearing its exact stable, function-local handler identity. A transfer for an outer handler bubbles unchanged. A handler's own `?` therefore selects the next outer handler or the function boundary, never itself.

`try`/`except` is typed lexical recovery, not exception handling. It catches only an `err` transfer created by `?` for that handler identity. It does not catch a bare or stored Result, explicit Result `match`, postfix unwrap panic, compiler invariant failure, or CLR/JavaScript/runtime failure. Protected and selected handler blocks execute at most once; a successful protected block does not execute a handler.

Postfix `!` evaluates its Result operand exactly once. It yields an `ok` payload and terminates with `COPE-PANIC-UNWRAP` on `err`. That terminal path bypasses every `except`.

## Realization boundary

| Source construct | Semantic role | C# realization | JavaScript realization |
| --- | --- | --- | --- |
| `ok` / `err` | Result construction | generated Result representation | private Result representation |
| `?` to function | error propagation | branch to function result | function flow transfer |
| `?` to handler | lexical recovery transfer | branch to handler label | handler-ID flow transfer |
| `match` | explicit inspection | Result tag inspection | Result tag inspection |
| postfix `!` | terminal unwrap | panic on `err` | panic on `err` |
| `try`/`except` | typed lexical recovery | locals/labels/branches | private structured flow |

Language law ends at source semantics and canonical MIR. MIR preserves `MirResultType`, explicit Result construction/match/unwrap nodes, `MirTryExpression`, `MirValueBlock`, a stable `MirHandlerId`, and discriminated function or lexical propagation targets. Shared MIR validation rejects duplicate/dangling handler identities, incompatible lexical targets, empty handler targets, and incompatible function-return propagation before either backend emits an artifact.

C# labels and branches, and JavaScript's frozen null-prototype token-branded `value`, `toHandler`, and `toFunction` flow records, are backend-private machinery. They are neither source Results nor payload enums, are not exported as user ABI, and cannot be accepted across the private token boundary. Result values retain a distinct private Result token. Ordinary Result flow uses no CLR or JavaScript `throw`/`catch`; terminal unwrap and invariant panics may use the established host terminal mechanism.

## Evidence

Curated language fixtures cover accepted construction, forwarding, matching, unwrap, successful recovery, local recovery, nested recovery, outer-handler transfer, result-valued handlers, error agreement, empty handler targets, malformed handler shape, invalid propagation, and non-Result unwrap. Backend tests add malformed MIR identity/target validation, generated branch/flow inspection, byte-identical repeated `.cope`, C#, and JavaScript emission, stable handler identities, exact JavaScript corpus hashes, repeated Node execution, and C#/Node observable parity.

The closeout matrix exercises success without recovery, local recovery, nested inner recovery, handler-to-outer transfer, handler-to-function transfer, bare Result forwarding, explicit Result matching, successful unwrap, and unwrap panic category. Host-instrumented Node coverage counts `?`, selected-handler, and successful `!` operands, proving the expected exactly-once behavior without extending the language with an effects system.

Pre-M6 JavaScript corpus artifacts remain byte-identical. Fallibility helpers remain demand-driven: a program without Result, unwrap, or handler use does not receive the corresponding private runtime helper.

## Deferred work

This closes typed fallibility only. Immutable nominal records, general exception handling, `finally`, async fallibility, effect polymorphism, Result equality, a runtime package, and JavaScript standard-library work remain outside this milestone.
