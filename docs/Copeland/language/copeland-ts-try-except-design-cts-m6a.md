# CTS-M6a: Typed Result `try`/`except` design

**Status:** accepted language and architecture design. CTS-M6b implements the frontend, binding, Cope MIR, C# proof backend, fixtures, and focused tests described here; CTS-M6c implements JavaScript private-flow lowering and Node evidence.

## Outcome

`try`/`except` is expression-shaped, typed handling for a `Result` error actively propagated with postfix `?`. It is not JavaScript or CLR exception handling.

```ts
const value: number = try {
    const text: string = readText()?;
    parseNumber(text)?
} except (error) {
    0
};
```

It intercepts only the selected propagation edge. It neither observes a merely constructed/forwarded `err` value nor recovers a terminal postfix-unwrap panic.

```text
?             err -> nearest lexical except, else function Result return
try/except    intercepts the selected ? err and yields an ordinary expression value
!             terminal panic on err; no propagation target and no except interception
```

This record resolves the direction reserved by [CTS-M4a](copeland-ts-first-class-result-design-cts-m4a.md#8-propagation-target-decision) and is subordinate to the canonical [language profile](copeland-ts-language-profile.md).

## Accepted decisions

| Decision | CTS-M6a rule |
| --- | --- |
| Keyword | Use `try` and `except`; `catch` is not a Copeland TS keyword. |
| Scope | Handle only typed `err` propagation produced by `?`. No host exception conversion or catching exists. |
| Shape | `try` is a primary expression with two dedicated value blocks; it does not make ordinary `{ ... }` a general expression. |
| Error inference | The first `?` targeted to the handler fixes `E`; every other `?` targeted to that handler must have exactly structural error type `E`. |
| Handler binding | `except (name)` declares one inferred, read-only binding of type `E`. A type annotation is not accepted in this slice. |
| Result type | The protected value and handler value have exactly one structural type `V`; the `try` expression has type `V`. |
| Empty handler target | Reject a `try` whose protected lexical region contains no `?` targeted to its own handler. It otherwise implies recoverable exception handling without a Result propagation edge. |
| MIR | Add a dedicated lexical handler expression and a stable handler identity. Do not desugar it away before MIR. |
| Backends | JavaScript uses compiler-private structured flow records/IIFEs; C# uses compiler-generated locals, labels, and branches. Ordinary `err` uses neither `throw`/`catch` nor CLR exceptions. |

## Bounded grammar

The eventual authoring spelling is also the CTS-M6b spelling. The bounded part is the contents of its value blocks, not an alternate surface syntax.

```text
TryExceptExpression  := "try" TryValueBlock "except" ExceptBinding TryValueBlock
ExceptBinding        := "(" Identifier ")"
TryValueBlock        := "{" TryPrefixStatement* Expression "}"
TryPrefixStatement   := VariableDeclaration ";" | Expression ";"
```

The final `Expression` has no semicolon and is the block value. It may itself be `if`, `match`, a nested `try`/`except`, a call, a postfix `?`, or any other supported expression. A prefix expression statement is evaluated for effects and discards only its ordinary non-Result value under the existing expression-statement rules. `const` and `let` declarations retain their current annotation and scope rules.

```ts
const total: number = try {
    const left: number = readLeft()?;
    const right: number = readRight()?;
    left + right
} except (parseError) {
    report(parseError);
    0
};
```

CTS-M6b deliberately excludes `return`, `if`/`while`/`for` statements, nested ordinary block statements, `break`, and `continue` from `TryPrefixStatement`. This does not limit ordinary function bodies; it avoids silently expanding this Result feature into a general block-expression/control-flow design. A later proposal may widen `TryValueBlock` only with separate reachability, scope, and backend lowering rules.

The current parser already has primary expression parsing, postfix chaining, statement blocks, and expression-bodied `if`/`match`; it has no block expression. A dedicated `TryExceptExpressionSyntax` plus `TryValueBlockSyntax` is therefore smaller and clearer than changing `BlockStatementSyntax` into an expression. `try` and `except` become reserved keywords only in CTS-M6b.

## Type rules

Let the protected value block have value type `V`, and let the error type inferred for this handler be `E`.

```text
Γ; targets + H ⊢ protected : V     H receives one or more ? operands of type Sᵢ ! E
Γ, error : E; targets (without H) ⊢ handler : V
────────────────────────────────────────────────────────────────────────────
Γ; targets ⊢ try protected except (error) handler : V
```

`Sᵢ` may differ at each propagation site. The success payload of each `?` is typed at that expression site; only the error component is constrained by the handler. The selected handler receives the original error payload once, not a wrapper Result and not a copied or converted payload.

### Exact agreement

- The first `?` whose selected target is handler `H` establishes `E`.
- Every later `?` whose selected target is `H` must have an error type structurally equivalent to `E`. The first implementation has no error unions, aliases, subtyping, conversion, coercion, or effect inference.
- The protected tail and handler tail must be structurally equivalent `V`; this includes arrays and nested Results. There is no common-supertype or implicit Result wrapping rule.
- `except (error)` is contextually typed as `E`. It is read-only, visible only in the handler block, and follows ordinary duplicate-name/shadowing diagnostics. It is not visible in the protected block.
- `ok` and `err` retain their existing contextual Result rules. If the containing `try` expression is contextually expected to be `R ! F`, both tails receive that expected result type; `ok` and `err` may therefore construct `R ! F` in either tail. The handled propagation type `E` remains independent of `F`.

For example, this expression handles `ParseError` but itself produces a first-class `number ! DisplayError` value; it does not flatten either Result:

```ts
const displayed: number ! DisplayError = try {
    const text: string = readText()?; // readText: string ! ParseError
    ok(parseNumber(text)?)
} except (parseError) {
    err(showable(parseError))
};
```

Nested Results remain literal values. If `nested : (number ! InnerError) ! OuterError`, `nested?` has type `number ! InnerError`; a second postfix operation determines the next operation: `nested??` propagates `InnerError` to the selected target, while `nested?!` terminally unwraps it. `try` neither flattens nor implicitly matches Results.

`void` is an ordinary value type for this purpose. A final void-returning call supplies a `void` block value:

```ts
try {
    writeFile()?
} except (error) {
    record(error)
}
```

This has type `void`. It is also valid for a `void ! E` function return context under the established Result return rules. There is no new source unit literal.

## Lexical propagation targeting

Binding maintains an ordered target stack. A fallible function contributes `FunctionReturn(Ef)` when its declared return type is `T ! Ef`. Entering a protected `try` block allocates handler identity `H`, pushes `LexicalExcept(H, E?)`, and binds that protected block. The first propagation targeting `H` completes `E`; subsequent targetings must agree exactly.

Before binding the handler block, the binder removes `H` from the stack, keeps the outer targets, and introduces `error : E` in the handler scope. Therefore a handler cannot recursively handle its own `?`.

```text
function return target (if any)
             │
             ▼
try H_outer {                         except (outer) { ... }
    try H_inner {                     except (inner) {
        operation()?  ─────────────► H_inner
    }                                   retry(inner)? ─────► H_outer
    ...
}                                       outerRetry(outer)? ► function return
```

More formally, a `?` selects the innermost target that is lexically active at the expression site. The protected body of `H` makes `H` active. The handler body of `H` does not. Nested protected bodies shadow outer handlers; nested handler bodies resume with the next outer target.

Outside a `try`, the existing function-return behavior is unchanged. `?` still requires a Result operand and a compatible enclosing fallible function. A `?` in a handler with no outer lexical handler and no compatible fallible function is invalid.

`?` is the sole source operation that can select an `except`. These do not invoke a handler:

- `err(error)` constructed in a protected block;
- a Result value stored, passed, returned, or forwarded without postfix `?`;
- an explicit `match` that observes an `err` value;
- postfix `!` on an `err` value;
- a JavaScript exception, CLR exception, process failure, or compiler/backend invariant failure.

## Evaluation laws

All existing Copeland TS left-to-right, exactly-once evaluation laws apply. For a `?` targeted to handler `H`:

1. Evaluate the operand once.
2. If it is `ok(v)`, continue with `v` at that source position.
3. If it is `err(e)`, stop evaluation of the protected continuation immediately.
4. Bind that original `e` once to `H`'s handler parameter and evaluate the handler once.
5. Do not evaluate a later operand, statement, tail expression, or any unselected handler.

Earlier side effects remain observable. The protected body, Result operand, and handler are never reevaluated merely to transfer the error.

```ts
try {
    const left: number = first()?;
    const right: number = second()?;
    observe(left, right)
} except (error) {
    recover(error)
}
```

If `first()` is `err`, neither `second()` nor `observe` runs. If `first()` is `ok` and `second()` is `err`, `first()` is not repeated; `observe` does not run; `recover` runs once. If both are `ok`, no handler runs.

Postfix unwrap remains terminal. In the following shape, an `err` from `operation()!` produces `COPE-PANIC-UNWRAP`; it does not bind `error` or execute `recover`:

```ts
try {
    const ready: void = establish()?;
    operation()!
} except (error) {
    recover(error)
}
```

The preliminary `?` makes this a meaningful handler expression. Implementations must preserve the panic bypass even when `operation()!` occurs after earlier successful side effects.

## Result match remains first-class

`try`/`except` is concise for a sequence whose first propagated error should transfer control to one lexical recovery value. Explicit `match` remains preferable when code must inspect or transform both alternatives as ordinary values, retain or forward the original Result, select without early exit, work with a stored Result, or make both alternatives visually explicit for domain logic.

No synthetic payload enum is introduced, and this design does not reopen Result equality.

## Bound tree and MIR design

The existing `BoundPropagationTarget.FunctionReturn` and `MirPropagationTarget.FunctionReturn` are enums with one case. CTS-M4a explicitly reserved `LexicalExcept(HandlerId)`, so CTS-M6b must replace each with a discriminated representation rather than overload a Boolean or backend-only convention.

```text
BoundPropagationTarget = FunctionReturn | LexicalExcept(BoundHandlerId)
MirPropagationTarget   = FunctionReturn | LexicalExcept(MirHandlerId)

BoundTryExceptExpression(
    HandlerId,
    Protected: BoundValueBlock,
    HandlerBinding: VariableSymbol,
    HandledErrorType: TypeSymbol,
    Handler: BoundValueBlock,
    Type: TypeSymbol)

MirTryExpression(
    HandlerId,
    Protected: MirValueBlock,
    HandlerBinding: MirTryBinding,
    HandledErrorType: MirType,
    Handler: MirValueBlock,
    Type: MirType)

MirValueBlock(PrefixStatements, ValueExpression)
```

`HandlerId` is a stable, function-local identity allocated once by binding/lowering and preserved into MIR. It is not the source variable name, a backend label, or a runtime exception type. `MirTryExpression` owns exactly one identity and binding. A `MirPropagateExpression` targeting `LexicalExcept(H)` is valid only within the owning protected region or an inner region from which `H` is the next outer target. Invariant validation must reject dangling, self-handler, and out-of-scope targets.

The dedicated node is required. Existing `MirResultMatchExpression` cannot express a protected continuation exited by several nested operands without continuation rewriting, duplicated expressions, or erased target identity. Rewriting it away before MIR would obscure the selected lexical target and make exactly-once guarantees backend accidents. `MirTryExpression` is the narrow structured Result handler region; it is not a general exception region, effect system, continuation framework, CFG/SSA migration, or universal control-flow representation.

The Cope text projection should make this visible, for example:

```text
try-result h1 error ParseError -> number
  protected {
    let text: string = propagate call readText to except h1
    propagate call parseNumber(text) to except h1
  }
  except error: ParseError {
    0
  }
```

The exact formatting is non-contractual, but it must identify the handler region, binding, handled type, result type, and every non-function propagation target. `unwrap` remains its own MIR node without a propagation target.

## Backend lowering recommendations

### JavaScript: compiler-private structured flow

Select a statementful IIFE/prelude lowering with a compiler-private, token-branded flow record. It has only backend-local alternatives equivalent to `value(v)`, `to-handler(handlerId, error)`, and `to-function(error)`. It is not a source Result, not a synthetic payload enum, and never crosses the generated program API boundary.

For each `MirTryExpression`, the protected lowering returns a private flow outcome. A `MirPropagateExpression` targeted to `H` evaluates and validates its Result operand once, then returns `to-handler(H, error)` from the current private lowering scope. The owning `MirTryExpression` recognizes only its own `H`, binds the payload once, and lowers its handler exactly once. A flow for an outer handler or function return bubbles outward unchanged. The function-emission boundary converts `to-function(error)` into the existing explicit Result `err` return.

This fits the existing JavaScript backend's `EmittedExpression` prelude architecture and prevents JavaScript `throw`/`catch` from becoming ordinary Result control flow. Throwing a private sentinel is rejected because it conflates Result flow with host exception mechanics and can accidentally catch an unwrap panic. A direct full-function state machine is also rejected as broader than the feature needs.

Existing private throws remain terminal-only: malformed private Result representation/invariant failure and `COPE-PANIC-UNWRAP` keep their existing classification. Generated `try` lowering must not install a JavaScript `catch`, including a selective one.

### C#: locals, labels, and branches

Select direct statement lowering inside the generated method. For each `MirTryExpression`, declare a result temporary and a typed error temporary, lower the protected value into the result temporary, and branch to a generated handler label on `err`. At the handler label, bind the error once in a scoped local, lower the handler value into the same result temporary, then branch to a generated join label. A propagation targeting an outer handler assigns that outer handler's error temporary and branches to its label; function-return propagation remains the existing explicit `CopeResult<..., ...>.Err(...)` method return.

The generated labels and temporaries are backend-private and function-local. The lowering must maintain valid C# local scope and definite assignment; labels must not jump into a local's scope. This is AOT-compatible ordinary generated C# and requires neither reflection, dynamic dispatch, runtime code generation, nor a new runtime dependency.

Using `try`/`catch`, `Exception`, or `CopeUnwrapPanicException` for ordinary `err` is rejected. `MirUnwrapExpression` retains its existing terminal private exception path and no generated `try` handler catches it.

### Required parity

| Observation | Required parity |
| --- | --- |
| successful protected value | handler does not run; expression yields the body value. |
| handled error | selected handler runs once and its value is the expression value. |
| payload | handler receives the original typed `err` payload once. |
| ordering | left-to-right, exactly-once effects; no later protected work after `err`. |
| nesting | nearest protected handler wins; handler-originated `?` targets the next outer target. |
| outer propagation | a `?` outside/escaping handlers still returns the enclosing function's `err`. |
| terminal panic | `!` bypasses every `except` and preserves `COPE-PANIC-UNWRAP`. |
| diagnostics | equivalent source rejection and no partial backend artifact for invalid MIR/source. |

## Diagnostic plan

The current namespace reserves `COPE-RESULT-0001` through `0009` for constructors, Result match, and Result-type syntax; `COPE-TYPE-0014` through `0016` already identify no target, error mismatch, and non-Result `?`; and `COPE-TYPE-0019` identifies non-Result unwrap. CTS-M6b must retain those meanings and must not renumber them.

| ID | Meaning | Ownership |
| --- | --- | --- |
| `COPE-TYPE-0014` | `?` has no valid function-return or lexical-handler target. Update wording only if necessary; preserve the identity. | binder |
| `COPE-TYPE-0015` | `?` error type is incompatible with its selected function or lexical handler target. | binder |
| `COPE-TYPE-0016` | `?` operand is not a Result. | binder |
| `COPE-TRY-0001` | malformed `try`/`except` shape, missing `except`, or malformed one-name handler binding. | parser |
| `COPE-TRY-0002` | protected value type and handler value type are incompatible. | binder |
| `COPE-TRY-0003` | two or more `?` sites targeted to one handler have incompatible error types. | binder |
| `COPE-TRY-0004` | protected lexical region has no `?` targeted to its own handler. | binder |
| `COPE-TRY-0005` | a value block uses unsupported CTS-M6b statement/control flow. | parser/binder |
| `COPE-TRY-0006` | handler binding is missing, duplicated in its scope, or otherwise cannot form the required one read-only `E` binding. | binder |

Existing ordinary scope diagnostics may remain additional context for a duplicate name. The implementation should emit the stable `COPE-TRY-0006` at the handler binding rather than treating a malformed handler as a later unknown-name error. A source form that tries to “catch” `operation()!` but contains no targeted `?` is rejected with `COPE-TRY-0004`; a meaningful handler that also contains `!` is valid but must panic at runtime rather than run its handler.

## CTS-M6b/C fixture matrix

Source contracts belong under `tests/Copeland/Copeland.TS.Tests/Language/Valid/fallibility` and `Language/Invalid/fallibility`. Parser/bound/MIR expected artifacts remain in their existing frontend-owned corpora; generated JavaScript and C# artifacts remain in backend-owned corpora. No backend output belongs in the language-fixture tree.

| Contract | Future source fixture or evidence | Expected result |
| --- | --- | --- |
| successful handling | `try-except-success.cl-valid.ts` | body value; handler not run. |
| handled `err` | `try-except-handled-err.cl-valid.ts` | handler receives `E`, yields `V`. |
| multiple sites | `try-except-multiple-same-error.cl-valid.ts` | same `E`; earliest err wins. |
| nesting | `try-except-nested.cl-valid.ts` | inner protected `?` selects inner handler. |
| handler-to-outer | `try-except-handler-outer-target.cl-valid.ts` | `?` in inner handler selects outer handler/function return. |
| Result result | `try-except-result-value.cl-valid.ts` | `V` may itself be `R ! F`; no flattening. |
| `void ! E` | `try-except-void-result.cl-valid.ts` | final void values and Result return context work. |
| exactly once | backend-owned runtime corpus | operand/payload/handler counters prove one evaluation. |
| left-to-right | backend-owned runtime corpus | later operand/tail omitted after err; earlier effects remain. |
| panic bypass | backend-owned runtime corpus | `!` reports `COPE-PANIC-UNWRAP`; handler is not observed. |
| C#/Node parity | shared primitive-observable parity harness | same success, recovery, order, nested target, outer propagation, and terminal classification. |
| body/handler mismatch | `try-except-result-mismatch.cl-invalid.ts` | `COPE-TRY-0002`. |
| error mismatch | `try-except-error-mismatch.cl-invalid.ts` | `COPE-TRY-0003` or selected-target `COPE-TYPE-0015` as applicable. |
| malformed handler | `try-except-malformed-handler.cl-invalid.ts` | `COPE-TRY-0001`/`0006`. |
| invalid target | `try-except-invalid-target.cl-invalid.ts` | `COPE-TYPE-0014` or `0016`. |
| attempted panic handling | `try-except-panic-not-result.cl-invalid.ts` | no targeted `?`, therefore `COPE-TRY-0004`; runtime corpus separately proves bypass in a meaningful handler. |
| unsupported block control flow | `try-except-control-flow.cl-invalid.ts` | `COPE-TRY-0005`. |

The source-valid fixtures establish frontend/MIR acceptance only. Node and generated C# runtime cases must observe primitives/counters/terminal classification rather than compare backend-private Result, flow, panic, label, or exception representations.

## Milestone boundary

| Milestone | Bounded implementation |
| --- | --- |
| CTS-M6b | Add `try`/`except` source syntax, dedicated value-block parsing, target-stack binding, stable handler ids, bound/MIR nodes and text, diagnostics, source/MIR fixtures, and C# direct branch/label lowering with focused C# coverage. Do not implement JavaScript lowering. |
| CTS-M6c | Implement JavaScript private structured-flow lowering, backend-owned corpus/runtime cases, and bounded C#/Node parity for the matrix above. |
| CTS-M6d | Complete diagnostic normalization, nesting and panic-bypass stress coverage, doctrine/profile ratification, artifact review, and closeout. |

CTS-M6b must not add async Result handling, host interop adapters, exceptions, `throw`/`catch`/`finally`, filters/rethrow/stack unwinding, Result equality, conversion/union effects, general block expressions, arbitrary control-flow expansion, bounded static expansion, static execution, generics redesign, or runtime-host work.

## Unresolved work deliberately left outside CTS-M6a

- Whether a later dedicated value-block proposal can admit ordinary statement control flow without becoming general block expressions.
- Handler parameter annotations, multiple handlers, pattern handlers, filters, and error conversion; none is authorized by CTS-M6b.
- Async Result propagation and host exception-to-Result adapters.
- Any broader Result pattern, equality, generic, static-expansion, or effect-system work.

Those questions must not weaken the exact, one-handler, lexical Result semantics adopted here.
