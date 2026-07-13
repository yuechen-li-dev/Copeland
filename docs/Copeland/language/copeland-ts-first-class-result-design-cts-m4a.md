# CTS-M4a: First-class Result and fallibility MIR design

**Status:** accepted language laws and architecture record. CTS-M4b implements the bounded frontend, dedicated Cope MIR, and C# proof-backend slice; CTS-M4c implements JavaScript Result emission; CTS-M5 implements postfix unwrap; and [CTS-M6a](copeland-ts-try-except-design-cts-m6a.md) accepts the lexical-handler design. `try`/`except` remains unimplemented.

## Decision summary

Copeland's fallibility model is a structural value type:

```text
Result<T, E> = ok(T) | err(E)
```

`Result<T, E>` is explanatory notation. The Copeland spelling is `T ! E`. A bare fallible call has type `T ! E`, not `T` plus hidden error metadata. `?` and future postfix `!` consume a Result value; explicit `match` inspects one; `ok` and `err` construct one. Result is not a synthesized declaration of a user enum.

The smallest honest MIR direction is dedicated Result types and nodes. It replaces call-local `IsFallible`, `ErrorType`, and `IsPropagated` with a Result-valued call and an explicit propagation expression. It deliberately does not introduce a general sum-type, effect, SSA, CFG, exception, or pattern-compilation framework.

## 1. Current-state graph

```text
function f(): T ! E                 call f()
        |                                |
FunctionSymbol(ReturnType=T, ErrorType=E) BoundCallExpression(Type=T, ErrorType=E)
        |                                |
MirFunction(ReturnType=T, ErrorType=E)  MirCallExpression(Type=T, IsFallible, ErrorType, IsPropagated)
        \______________________________/ 
                   C# proof backend manufactures CopeResult<T,E>
```

The current lexer has `BangToken` and `QuestionToken`. The parser recognizes `! E` only after a function return type and recognizes postfix `?`; `!` is otherwise prefix Boolean negation. `TypeSyntax` has predefined, identifier, and array forms but no Result or parenthesized type form. `FunctionDeclarationSyntax` separately stores `ReturnType`, `ErrorTypeBangToken`, and `ErrorType`. `PropagateExpressionSyntax` exists, but no unwrap, Result constructor, Result type, `try`, or `except` syntax node exists.

Binding mirrors that split: `FunctionSymbol` has `ReturnType` and optional `ErrorType`; `BoundCallExpression.Type` is the success type and overrides `ErrorType`; `BoundPropagateExpression.Type` is the success type. The binder rejects an unpropagated `BoundCallExpression`, so values cannot currently be stored, passed, matched, or deliberately returned. `BindPropagate` accepts only a fallible operand and only the enclosing function's same-name error type.

Lowering only recognizes `BoundPropagateExpression` whose operand is a call. It turns that pair into a propagated `MirCallExpression`; any other propagated expression loses propagation when lowered. `MirType` is a string wrapper. The textual writer prints `func ... -> T ! E` and `call? ... propagate E`.

The C# proof backend emits its own `CopeResult<TValue,TError>` and wraps every return from a fallible `MirFunction` as `Ok`; it handles propagated calls by emitting a temporary, test, and `Err` return. Its physical type is evidence, not authority. The JavaScript backend rejects fallible functions, fallible calls, and propagated calls with `COPE-JS-0001`, without an artifact.

Payload enums establish useful lessons: `EnumTypeSymbol` is nominal; `BoundMatchExpression` evaluates a single enum scrutinee and binds arm-local payload variables; `MirEnumValueExpression` and `MirMatchExpression` preserve ordered payloads and branch selection. Those lessons apply to Result operations, but their nominal declaration identity and enum catalog do not.

Historical Copeland doctrine already specifies `function f(): T ! E` and postfix `?`. The adjacent Oct/WyrmCoil material records prefix `try expression`, prefix unwrap, postfix compatibility spellings, and `ok`/`err` arms, but no paired lexical `except`; it is evidence only, not Copeland authority.

## 2. Target semantic graph

```text
function operation(): T ! E    operation() : T ! E
             |                         |
             +-------------------------+
                       MirResultType(T, E)
              /            |             \
       MirOkExpression MirErrExpression MirResultMatchExpression
                                            |
                                MirPropagateExpression(target)
                                MirUnwrapExpression (later)
```

Every value position retains both component types. Calls produce their declared return value. `operation()?` produces `T`; `operation()!` will produce `T` or panic. A `match` consumes `T ! E` and yields the unified arm type.

## 3. Source type model and typing table

### Type grammar and availability

The eventual grammar is right-associative at the lowest type precedence:

```text
ResultType       := PostfixType ("!" ResultType)?
PostfixType      := PrimaryType ("[]")*
PrimaryType      := predefined | identifier | "(" ResultType ")"
```

Thus `number[] ! ParseError` means `Result<number[], ParseError>`, and `number ! ParseError[]` means `Result<number, ParseError[]>`. An unparenthesized repeated `!` is rejected with a diagnostic asking for parentheses. `(T ! E1) ! E2` is legal: it is an outer Result whose success value is an inner Result. It is useful for explicitly staged recovery, but no flattening occurs. Parentheses are required so it is never confused with a malformed signature.

`T ! E` is valid in parameters, locals, `const` and `let` declarations, function return positions, enum payloads, and any future generic value position. It is a normal structural value: it may be stored and passed without handling. A bare Result expression statement is rejected because it silently discards an unhandled outcome. Existing non-Result expression-statement rules are unaffected.

Success and error types must be explicit wherever the source declares a Result type. Result equality/assignment compatibility requires normal Copeland type identity for both `T` and `E`; no error alias conversion, subtyping, or implicit wrapping/conversion exists. `const` freezes the Result binding and `let` permits rebinding only to the same complete Result type; neither changes the payload or error rules.

| Source form | Type | Rule |
| --- | --- | --- |
| `operation()` for `function operation(): T ! E` | `T ! E` | A call returns its declared value type. |
| `const r: T ! E = operation();` | `T ! E` | Store without handling is valid. |
| `consume(operation())` where parameter is `T ! E` | `T ! E` argument | Passing is valid. |
| `operation()?` | `T` | Success unwrap; failure transfers to the target. |
| `operation()!` | `T` | Future success unwrap; failure panics. |
| `match r { ok(v) => a, err(e) => b }` | common type of `a`, `b` | Explicit inspection. |
| `operation();` | error | An unconsumed Result may not be discarded. |

### Constructors and contextual typing

`ok` and `err` are Result intrinsics only in a contextual Result position, not ordinary resolved functions. The initial contexts are an annotated local initializer, argument position, compatible return position, an enum payload with declared Result type, and a Result constructor/arm position whose expected Result type is already known. They each have exactly one payload: `ok(value)` checks `value : T`; `err(error)` checks `error : E`. `ok()` and `err()` are not ordinary zero-argument constructors.

An unconstrained `ok(value)` or `err(error)` is rejected rather than inferring an unconstrained error or success type. A function named `ok` or `err` does not shadow the intrinsic where a Result context selects it; outside such a context, the spelling is an ordinary name/call and follows ordinary lookup. M4b should reserve a diagnostic path rather than silently choosing an interpretation. `void ! E` is meaningful as `Result<void,E>`; `return;` constructs its success. Explicit void constructor spelling is deferred rather than inventing a fake source value.

## 4. Calls and returns

The old model makes a call simultaneously look like `T` and carry `E` out-of-band. M4b must remove that contradiction: `BoundCallExpression.Type` becomes `ResultTypeSymbol(T,E)` when the function is fallible. `BoundPropagateExpression` extracts `T` from any Result expression, not merely a call.

For a declared `function f(): T ! E`, a return is judged against the complete declared Result type:

| Return source | Bound result / action | Validity |
| --- | --- | --- |
| `return value;` where `value : T` | construct `ok(value)` | valid convenience shorthand |
| `return ok(value);` where `value : T` | explicit Result success | valid with Result return context |
| `return err(error);` where `error : E` | explicit Result failure | valid with Result return context |
| `return result;` where `result : T ! E` | forward exactly that Result | valid; no unwrap/re-wrap |
| `return result?;` | propagate `err`; on `ok(v)`, return `ok(v)` | valid when target is compatible; intentionally not forwarding |
| `return ok(result);` where `result : T ! E` | would produce `(T ! E) ! E` | reject unless that nested type was explicitly declared |
| `return result;` with different success or error type | no conversion | reject |
| `return err(e);` from `function f(): T` | no Result return context | reject |

A function with a declared `T ! E` is fallible even if its body happens only to return success. A function with plain `T` cannot return an intrinsic Result constructor. Direct forwarding is intentionally allowed because it preserves identity and evaluation without a second Result layer. `return;` remains valid for `void ! E` and means success of the language's void value; C#'s current `CopeUnit` is only one backend mechanism.

## 5. Construction and Result matching

Result matching uses the existing `match` expression shape but a dedicated Result path:

```ts
function inspect(outcome: number ! ParseError): string {
  return match outcome {
    ok(value) => "value",
    err(error) => "recovered",
  };
}
```

The scrutinee evaluates once. Exactly the selected arm evaluates. `ok(value)` and `err(error)` patterns each take one binding, whose type is respectively the Result success/error type, and the binding is arm-local. Nested Result matches are ordinary nested expressions and retain independent lexical scopes. A Result can occur as an ordinary enum payload and be matched after its payload binding is selected; an enum remains nominal and a Result remains structural.

Initially, both `ok` and `err` arms are required; wildcard Result arms are deferred. Duplicate alternatives and missing alternatives are errors. Arm result types must be compatible under normal type identity. Existing enum machinery may be reused for parsing a call-like arm pattern, arm scope creation, single-scrutinee evaluation, arm type unification, and diagnostics structure. It must not reuse `EnumTypeSymbol`, `MirEnum`, synthetic cases, or nominal enum catalogs.

## 6. MIR alternatives

| Alternative | Benefits | Rejection or cost | Decision |
| --- | --- | --- | --- |
| A. Synthesized payload enum | Reuses existing enum constructors/match lowering. | Result is structural, but enums are nominal declarations; invisible declaration identity, enum diagnostics, `.cope` output, and backend enum catalog leak a false representation. | Reject. |
| B. Dedicated targeted Result MIR | States Result type, alternatives, payloads, and control transfer directly; works with current tree-shaped MIR; leaves backend layout private. | Requires a small typed MIR migration and dedicated backend cases. | Adopt. |
| C. General sum/effect/control framework | Could abstract future unrelated forms. | No current second consumer justifies universal sums, effects, CFG/SSA, or general handlers; it expands authority and hides Result semantics. | Reject for CTS-M4a. |

## 7. Recommended MIR schema

Replace the string-only `MirType` model with a small structural type hierarchy or equivalent tagged representation. The exact C# class names are implementation detail, but the semantic shape must distinguish named, array, and Result types without parsing display strings:

```text
MirType
  MirPrimitiveType / MirNamedType / MirArrayType
  MirResultType(SuccessType: MirType, ErrorType: MirType)

MirFunction(Name, Parameters, ReturnType: MirType, Locals, Body)
MirCallExpression(FunctionName, Arguments, Type)       // Type may be MirResultType
MirOkExpression(Payload, Type: MirResultType)
MirErrExpression(Payload, Type: MirResultType)
MirResultMatchExpression(Scrutinee, OkBinding, OkExpression,
                         ErrBinding, ErrExpression, Type)
MirPropagateExpression(Operand, Target, Type: SuccessType)
MirUnwrapExpression(Operand, Type: SuccessType)        // later
```

`MirFunction.ReturnType` is the one canonical complete return type. A fallible function has a `MirResultType`; it no longer owns separate `ErrorType` or `IsFallible` state. Derived predicates are acceptable only when they inspect the canonical return type and do not restore a second representation. `MirCallExpression` has no `IsFallible`, `ErrorType`, or `IsPropagated`; its `Type` is authoritative.

The textual writer should display `T ! E` through type formatting and dedicated operations, for example `ok value`, `err error`, `result-match`, and `propagate <expression> to function-return`. `.cope` remains a projection, not a parser/serialization contract. Structural type equality uses the two component types, so both backends and diagnostics can reason about a Result without string splitting. Existing array and named-type consumers must migrate from `.Name` assumptions; this is a bounded MIR type-model change, not a repository-wide type system rewrite.

## 8. Propagation-target decision

The semantics of `expression?` are:

```text
evaluate expression once
if it is ok(value), yield value
if it is err(error), transfer the original error to the selected target
```

M4a selects **targeted propagation that survives into MIR**. It uses a discriminated target:

```text
MirPropagationTarget = FunctionReturn | LexicalExcept(HandlerId)
```

M4b implements only `FunctionReturn`, whose compatibility requires the current function return type to be `T2 ! E` with the same error type. [CTS-M6a](copeland-ts-try-except-design-cts-m6a.md#lexical-propagation-targeting) now specifies the reserved lexical direction: binding allocates a stable `HandlerId`; a `?` in its protected body targets the nearest handler, while a `?` in the handler body targets the next outer target. The handler construct and its region/join representation must be introduced together; an orphan handler id is not valid MIR.

This choice is preferable to lowering handlers away before MIR: that would require continuation rewriting in a tree IR, risks duplicated expressions and lost evaluation order, and obscures diagnostics. A fully general exception-handler IR is also not justified. A later dedicated `MirTryResultExpression`/handler region may own `HandlerId` and its one error binding; it is structured Result control flow, never JavaScript or .NET exception machinery.

## 9. Future unwrap and panic

Future postfix `expression!` has its own `MirUnwrapExpression`. It evaluates its operand once, yields the `ok` payload, and takes an explicit nonrecoverable Copeland panic edge on `err`. MIR retains the Result component types and makes the error payload available to the backend panic diagnostic. It has no propagation target, cannot be caught by `except`, and never converts failure to an `err`. Panic ABI, message formatting, and physical abort mechanism are deferred. `!` is outside M4a and M4b.

## 10. Backend responsibility boundary

MIR guarantees the Result success and error types, selected constructor alternative, typed payload, single evaluation, match selection, propagation target, and later panic edge. It does not prescribe field names, frozen objects, null prototypes, C# records/structs/classes, exception classes, allocation, memory layout, or ABI.

```text
MirResultType(T,E) and Result operations
             /                         \
JavaScript private tagged value      .NET private generated representation
```

The JavaScript tagged object approach described in historical design, including freezing and null prototypes, is a candidate private representation. The existing C# `CopeResult<TValue,TError>` is a proof representation. Neither becomes Cope MIR semantics or a public language ABI. `try`/`except` never catches host exceptions; only a future explicit adapter can turn a declared host failure into `err`.

## 11. Diagnostic matrix

Exact codes follow the repository's allocation policy at implementation time. Existing `COPE-TYPE-0013` through `0016` may be retained only where their meaning remains exact; no speculative renumbering is assigned here.

| Situation | Required diagnostic meaning |
| --- | --- |
| `ok` without Result context | Result success constructor needs an expected `T ! E`. |
| `err` without Result context | Result error constructor needs an expected `T ! E`. |
| wrong `ok` payload | Expected success type `T`, got actual type. |
| wrong `err` payload | Expected error type `E`, got actual type. |
| Result match missing arm | Both `ok` and `err` are required initially. |
| duplicate Result arm | An alternative may occur once. |
| incompatible arm results | Both arms must produce one compatible type. |
| `?` on non-Result | Propagation requires `T ! E`. |
| `?` incompatible error | Target's error type differs; no implicit conversion. |
| `?` has no target | No enclosing fallible function or lexical `except`. |
| discarded Result | A Result expression statement must be consumed, stored, returned, matched, propagated, or unwrapped. |
| incompatible forwarding | Return Result's success or error component does not match declared return Result. |
| explicit `ok` double wrapping | `ok(result)` needs an explicitly declared nested Result success type. |
| `err` in nonfallible return | No Result return context exists. |
| `!` on non-Result | Unwrap requires `T ! E`. |
| `try` handler error mismatch | The handler declaration does not accept the protected target error type. |
| nested handler ambiguity | Report only malformed/ambiguous handler syntax; otherwise nearest lexical handler wins. |

## 12. Evaluation-order laws

| Source shape | Required semantic expansion |
| --- | --- |
| `makeResult()?` | `r = makeResult()` once; `ok(v) => v`; `err(e) => transfer e`. |
| `ok(makeValue())` / `err(makeError())` | Evaluate the one payload once, then construct the selected alternative. |
| `match makeResult() { ... }` | Evaluate `makeResult()` once; select exactly one arm; evaluate no other arm. |
| `combine(first()?, later())` | Evaluate `first()` once; on `err`, transfer before `later()` starts; on `ok`, evaluate `later()` in normal left-to-right order. |
| `return existing;` where `existing : T ! E` | Return the exact Result value; no inspection or construction. |
| nested match | Outer binding exists only in its selected arm; an inner `ok`/`err` binding shadows only in the inner arm scope. |
| future `try { a()? } except (e) { recover(e) }` | Evaluate `a()` once; execute either normal continuation or one handler once; do not duplicate the protected expression or handler. |

## 13. Compatibility and atomic migration

The migration is atomic at the frontend-to-MIR boundary. A temporary adapter that keeps a call typed as `T` while also creating a first-class `T ! E` would preserve the contradiction and permit incorrect assignment/return behavior. Existing source `function f(): T ! E` and `f()?` retain their spelling and behavior, but their binding and MIR types change together.

1. Add source `ResultTypeSyntax` and parenthesized type parsing, then reinterpret a function's existing `: T ! E` as one result return type rather than a separate error field. Add `ResultTypeSymbol`, result constructors/match nodes, and context-aware binding.
2. Change `FunctionSymbol`, `BoundFunctionDeclaration`, `BoundCallExpression`, `BoundPropagateExpression`, return binding, assignability, and type dumping to use a complete Result type. Generalize propagation to every Result expression. Reject discarded Results.
3. Replace `MirType` strings with structured types; migrate `MirFunction`, `MirCallExpression`, `MirTextWriter`, lowerer, `.cope` corpus, and all direct MIR construction. Add dedicated Result expressions and delete `IsPropagated` in the same change. The retirement point is the first M4b merge: no producer or consumer may inspect it afterward.
4. Migrate C# emission and generated `.g.cs` artifacts from signature metadata/wrapping behavior to the dedicated Result vocabulary. Preserve semantics, not the current `CopeResult` source shape. Keep JavaScript's result rejection explicit until it supports the complete selected Result MIR slice; CLI continues to report backend diagnostics and emit no partial artifact.
5. Migrate frontend language fixtures, parse/bind/lowering/MIR corpora, `MirEvaluationOrderTests`, focused binder/parser/lexer tests, C# corpus/runtime tests, JavaScript backend tests/corpus/CLI integration tests, and any `.cope`/`.g.cs` expected artifacts. Update public proof-era APIs only if repository consumers require it.

Repository search finds no consumer outside the Copeland source/projects/tests for `MirFunction`, `MirCallExpression`, `IsFallible`, `ErrorType`, or `IsPropagated`; the direct references are in `Copeland.TS`, `Copeland.TS.Mir`, the two backends, CLI composition, and their tests. Therefore a speculative public compatibility facade is not justified. The C# generated `CopeResult` shape is test/proof evidence, not a stable external API.

## 14. Bounded CTS-M4b implementation slice

M4b should be one atomic vertical slice:

- first-class `T ! E` types in signatures, parameters, locals, and enum payloads;
- contextual one-payload `ok`/`err` construction;
- explicit two-arm Result match;
- structured `MirResultType`, constructors, match, and function-return propagation;
- migration of all fallible calls and existing `?` programs to Result-valued calls plus `MirPropagateExpression`;
- C# proof backend parity, corpus artifacts, and runtime coverage for construction, forwarding, match, and both `?` paths;
- JavaScript validation updated to reject the new Result MIR precisely and without an artifact, unless its implementation in the same atomic change covers construction, matching, Result-returning calls, and function propagation together.

Migrating fallible call typing necessarily migrates existing `?` in the same change: otherwise valid programs such as `const x: number = parse(text)?;` would no longer type-check. M4b should not implement postfix `!` or `try`/`except`; neither is needed for coherent first-class values, explicit construction/match, forwarding, or function propagation.

## 15. Deferred work

Postfix unwrap is now implemented by CTS-M5. Expression-shaped `try { ... } except (error) { ... }` is specified by [CTS-M6a](copeland-ts-try-except-design-cts-m6a.md) and still needs its bounded source, binding, MIR, backend, and evaluation-order implementation. Wildcard Result arms, generic Result notation, error conversions/aliases, host interop adapters, async fallibility, Option, generalized sum types, universal effects, CFG/SSA, and universal pattern compilation remain outside this design.
