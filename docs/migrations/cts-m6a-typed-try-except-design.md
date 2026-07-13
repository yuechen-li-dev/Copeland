# CTS-M6a: Typed `try`/`except` design audit

**Status:** documentation-only architecture and semantic-design milestone. No implementation is claimed.

## Outcome

CTS-M6a accepts expression-shaped `try`/`except` as a narrow ergonomic layer over typed first-class `Result<T, E>` propagation. The canonical decision record is [CTS-M6a typed Result `try`/`except` design](../Copeland/language/copeland-ts-try-except-design-cts-m6a.md).

```text
?             transfers an err to a selected lexical handler or function Result return
try/except    handles only that selected typed transfer and yields a value
!             terminally panics; it is not a transfer and bypasses every handler
```

The audit confirms that the current implementation already has the Result foundation: structural `ResultTypeSymbol`/`MirResultType`, `BoundPropagateExpression`/`MirPropagateExpression`, Result matches, both explicit-Result backends, and `MirUnwrapExpression`. It also confirms the deliberately incomplete part: both propagation-target enums contain only `FunctionReturn`, and there is no `try`, `except`, or expression value-block syntax.

## Selected design

- `try { prefix-statements; final-expression } except (error) { prefix-statements; final-expression }` is a primary expression. Its dedicated value blocks are not general block expressions.
- The protected and handler values have exactly one structural type `V`; the whole expression has type `V`.
- The first protected `?` targeted to a handler infers `E`; all other such sites require exact structural `E`. Handler `error` is a read-only inferred `E` binding.
- A nested protected body selects the nearest handler. A `?` in a handler selects the next outer handler or the enclosing compatible fallible function, never that same handler.
- A `try` with no `?` targeted to itself is rejected. Constructing/forwarding/matching an `err` does not activate a handler.
- `MirTryExpression`, `MirValueBlock`, stable `HandlerId`, and discriminated `LexicalExcept(HandlerId)` propagation targets are required. Pre-MIR desugaring is rejected because it obscures target identity and exactly-once continuation semantics.
- JavaScript will use private structured flow records/IIFEs, never `throw`/`catch` for ordinary `err`. C# will use generated locals, labels, and branches, never CLR exceptions for ordinary `err`.
- `MirUnwrapExpression` and its `COPE-PANIC-UNWRAP` path remain terminal. Neither backend may make it observable to `except`.

## Diagnostic reservation

Existing `COPE-TYPE-0014`, `0015`, and `0016` retain their propagation meanings. CTS-M6b reserves `COPE-TRY-0001` through `0006` for malformed syntax/binding, result mismatch, incompatible handler errors, no handler-targeted propagation, unsupported bounded value-block control flow, and handler-binding misuse. Existing `COPE-RESULT-0001` through `0009` are not renumbered.

## Planned implementation sequence

1. **CTS-M6b:** source, binder, dedicated MIR, diagnostics, language/MIR fixtures, and C# lowering only.
2. **CTS-M6c:** JavaScript structured-flow lowering plus backend corpora and C#/Node parity.
3. **CTS-M6d:** diagnostic/nesting/panic stress coverage, doctrine ratification, and closeout.

No general exception model, exception catching, async Results, error conversions/unions, Result equality, general block expressions, static expansion, generics redesign, or runtime-host work is included.

## Validation required for this documentation milestone

1. Validate changed Markdown links, table column counts, and balanced fences.
2. Run `tools/Validate-CopelandTsTopology.ps1`.
3. Run `tools/Validate-DependencyBoundaries.ps1`, as repository doctrine requires for boundary validation.
4. Run `git diff --check`.
5. Confirm the final diff contains only the requested documentation/profile cross-reference updates and no production, project, fixture, test, solution, package, or tooling change.
