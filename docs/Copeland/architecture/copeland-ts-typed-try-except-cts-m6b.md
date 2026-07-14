# CTS-M6b: Typed `try`/`except` implementation

**Status:** historical CTS-M6b implementation record. It implemented frontend, Cope MIR, C# proof backend, CLI, language fixtures, and focused tests. JavaScript handler lowering is implemented by CTS-M6c; the complete contract is closed by CTS-M6d.

`try { ... value } except (error) { ... value }` is a dedicated expression, not a CLR or JavaScript exception construct. Its bounded value blocks accept `const`/`let` declarations, ordinary expression statements, and one final expression without a semicolon. `return`, statement control flow, and nested ordinary blocks are rejected with `COPE-TRY-0005`.

The protected and handler tails must have structurally identical type `V`; the entire expression has type `V`. The first protected `?` selecting its handler fixes exact error type `E`. Further targeted propagations require the same `E` (`COPE-TRY-0003`), and a protected region with no targeted propagation is rejected (`COPE-TRY-0004`). The handler binding is one inferred, read-only `E` variable scoped only to the handler.

Binding maintains a lexical target stack. A protected region adds `LexicalExcept(hN)`; its handler body removes that target while preserving outer targets. Therefore inner protected propagation selects the inner handler, an inner handler can propagate to the next outer handler, and an outer handler falls back to a compatible function Result return. Postfix unwrap remains `MirUnwrapExpression`, is not a propagation target, and remains terminal.

Cope MIR preserves this with `MirTryExpression`, `MirValueBlock`, `MirTryBinding`, function-local `MirHandlerId`, and discriminated `MirPropagationTarget`. `MirValidator` rejects dangling or self-handler targets, incompatible error payloads, duplicate handler identities in one function, empty handler regions, and malformed value blocks. The textual projection records `try-result hN`, handled error type, value blocks, and lexical target.

The C# backend lowers handlers to typed result/error temporaries, labels, and `goto` branches. Normal success goes to a join label; an `err` assigned to a lexical target transfers to that target's error temporary and handler label. It uses no C# `try`, `catch`, or ordinary Result exception. The existing unwrap panic still throws only on unwrap failure and is not intercepted.

At this checkpoint the JavaScript backend rejected `MirTryExpression`; CTS-M6c later replaced that historical boundary with private structured-flow lowering. Existing Result construction, function-return propagation, and unwrap JavaScript emission remain supported.

Relevant source contracts are under `tests/Copeland/Copeland.TS.Tests/Language/*/fallibility/try-except-*.cl-*.ts`; C# branch/runtime and JavaScript rejection proofs are backend-owned focused tests.
