# Copeland TS foundational control-flow (CTS-CF-M0)

**Status:** closed.

CTS-CF-M0 completes the current foundational statement-control-flow surface through syntax, binding, Cope MIR validation, and the C# and JavaScript backends.

## Supported surface

`if`/`else`, expression-valued `if`, `while`, C-style `for`, `break`, `continue`, and `return` are supported. Conditions in every conditional form and every nonempty loop condition are `boolean`; Copeland never inherits JavaScript truthiness. An omitted `for` condition means `true`.

`for` creates a lexical scope for its initializer binding, condition, increment, and body. That binding cannot escape the loop. `break` and `continue` apply only to the nearest lexical loop. A `continue` in a C-style `for` reaches its increment before the next condition check.

## MIR and realization

The frontend owns `MirIfStatement`, `MirWhileStatement`, `MirForStatement`, `MirBreakStatement`, and `MirContinueStatement`. Shared MIR validation checks boolean loop conditions and lexical loop depth before either backend runs; malformed control-flow MIR yields no C# or JavaScript artifact. Backends emit structured control flow from validated MIR, retaining existing Result propagation, typed `try`/`except`, unwrap, and return behavior.

Statementful condition or increment lowering is staged in authored order inside the generated loop. Backends do not receive source text or implement source-language fallbacks.

## Exclusions

This closeout excludes `do`/`while`, `for...of`, `for...in`, iterators, generators, labels, multi-level transfers, `switch`, ternaries, async control flow, and host exceptions for ordinary Result flow. [CTS-AUTOMATA-M0a](copeland-ts-suspension-automata-design-cts-automata-m0a.md) now routes async/sidecar and future iterator work through shared suspension lowering rather than a backend-only extension of these structured statements. It does not add any CTS-TYPE surface.

The foundation is honestly closed for this bounded surface. Further control-flow forms require a separate language decision.
