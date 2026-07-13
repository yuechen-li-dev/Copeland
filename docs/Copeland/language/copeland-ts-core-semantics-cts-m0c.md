# Copeland TS core semantics and JavaScript-lowering design (CTS-M0c)

## Purpose and authority

This document defines the semantic decisions and bounded recommendations that must precede a Copeland TS JavaScript backend. It does not change the compiler. The canonical product thesis remains:

> Copeland TS is a TypeScript-shaped, closed-world language. JavaScript is its first target platform and host ABI, not its source-language semantics.

Generated JavaScript must preserve Copeland meaning even when that requires explicit tags, branches, checks, frozen values, or error plumbing. JavaScript convenience is not language authority. The C# backend is implementation evidence only and does not establish the laws below.

The status vocabulary in this document is:

| Status | Meaning |
| --- | --- |
| Normative | Already directed by product doctrine or explicit user decision |
| Recommended | Best evidence-backed proposal, awaiting acceptance |
| Unresolved | Insufficient evidence or a meaningful product choice remains |
| Deferred | Real decision, but not required for the first JS vertical slice |
| Rejected | Explicitly outside Copeland TS |

An unaccepted recommendation is not permission for a backend to choose opportunistically. CTS-M0d must accept, revise, and enforce the recommendations needed by CTS-M1.

## Evidence boundary

Repository history and current sources establish these facts:

- Historical Copeland doctrine specifies `function f(): T ! E` and postfix `expression?`. It does not specify source `try`/`except`, `ok`/`err` matching, or postfix unwrap.
- Adjacent Aurelian/WyrmCoil compatibility doctrine records Oct-shaped prefix `try expression` propagation, prefix `unwrap expression`, postfix `expression?`/`expression!` compatibility forms, and `ok`/`err` match arms. It defines no paired `except` behavior and is not Copeland authority; it is design evidence only.
- In current syntax, `!` is a prefix Boolean-negation operator and the separator before a fallible function's error type. Postfix `expression!` does not exist.
- Current binding treats a fallible call as having success type `T` plus an error type `E`; an unhandled call is rejected and `?` is allowed only when the enclosing fallible function has the same error-type name.
- Current Cope MIR records a function's optional error type and records fallible calls with `IsFallible`, `ErrorType`, and `IsPropagated`. It has no first-class result value, `ok`, `err`, result match, unwrap, trap, or local handler target.
- Current source cannot construct an `err` result. The C# proof backend manufactures `CopeResult`, `Ok`, and `Err`; those choices are not language law.
- Payload enums and exhaustive `match` are implemented source-level features and have direct Cope MIR nodes.
- Numeric source literals are presently non-negative decimal integers parsed through `Int32`, even though bound type `number` and the C# proof backend use `double`.

These limitations are recorded so that proposed semantics are not misreported as implemented syntax.

## Binding and mutation law

| Decision | Status | Law |
| --- | --- | --- |
| `const` | Normative | A non-reassignable, block-scoped binding. |
| `let` | Normative | A reassignable, block-scoped binding. Every assignment must preserve the declared type. |
| `var` | Rejected | Legacy JavaScript declaration syntax; it does not introduce a Copeland binding. |
| General local type inference | Deferred | The current subset requires annotations. Future inference must not alter binding or mutation meaning. |
| Deep immutability | Unresolved | `const` protects the binding, not every transitively reachable value. No deep-immutability law follows from `const`. |
| Array or object mutation | Deferred | Array indexing/mutation and object/class values are outside the first slice and need their own laws. |

Binding reassignment and value mutation are separate. `const xs: T[]` means that `xs` cannot be rebound; it does not, by itself, decide whether a future `xs[index] = value` is legal. Likewise, freezing a compiler-generated tagged value is a representation guarantee for that value family, not a claim that all `const` references are deeply immutable.

`var` is currently tokenized but falls into parser recovery instead of reaching the binder's existing profile diagnostic. CTS-M0b therefore has no `var-declaration.cl-invalid.ts`. The narrow repair is reserved for CTS-M0d: parse `var` deliberately, reject it with a stable Copeland profile diagnostic, add the fixture, and make no other declaration change.

## Numeric semantics

### Recommended initial law

Initial Copeland `number` should be exactly the IEEE-754 binary64 value domain. This aligns the existing `double` proof with ordinary JavaScript numbers without making either backend the specification.

| Topic | Status | Recommended law | Direct JavaScript consequence |
| --- | --- | --- | --- |
| Value domain | Recommended | All IEEE-754 binary64 values, including finite values, both infinities, NaN, and signed zero. | Ordinary JS `number` is suitable. |
| NaN | Recommended | NaN is a valid computed value. It is unequal to every number, including itself. | Native arithmetic and comparisons preserve this. |
| Positive/negative infinity | Recommended | Valid computed values. Overflow and nonzero finite division by signed zero may produce them. | Native arithmetic preserves this. |
| Negative zero | Recommended | Preserved by arithmetic; numerically equal to positive zero. Its sign may affect later arithmetic such as reciprocal. | Do not canonicalize zero. |
| Division by zero | Recommended | Nonzero divided by signed zero yields signed infinity; zero divided by zero yields NaN. No Copeland exception is raised. | Native `/` is safe for typed numeric operands. |
| Overflow | Recommended | Binary64 overflow produces signed infinity. There is no separate initial overflow trap. | No generated overflow check. |
| Arithmetic | Recommended | Unary `-` and binary `+`, `-`, `*`, `/`, `%` use binary64 operations. | Native operators are safe for typed numeric operands. |
| Relational comparison | Recommended | `<`, `<=`, `>`, `>=` use IEEE numeric comparison; every ordered comparison with NaN is false. | Native operators are safe. |
| Equality | Recommended | Numeric equality follows IEEE comparison: NaN is never equal; `-0` equals `+0`. | Generated strict JS equality is safe after type validation. |
| Explicit conversions | Deferred | No conversion syntax or conversion behavior enters the first profile. | Reject unsupported conversions before emission. |
| Future integer types | Deferred | Integers may later be distinct types; `number` is not retroactively an integer union. | Do not add bitwise or truncating behavior by implication. |
| Runtime checks | Recommended | No finite-value check or zero canonicalization for closed-world core arithmetic. | Frontend typing is sufficient for listed operations. |

The important alternative is a finite-only or checked numeric model. It would avoid NaN/infinity but require checks after division and potentially every arithmetic operation, and it would diverge from both current `double` evidence and the JS target. That model remains possible for future refined numeric types, but it is not recommended for initial `number`.

This law does not authorize new literal forms. Decimal, exponent, NaN, infinity, conversion, and integer syntax remain separate frontend work.

## Equality semantics

### Recommended operator set

Copeland should retain `==` and `!=` as typed value-equality operators and reserve/reject source `===` and `!==`. Because implicit coercion is already rejected, a second strict pair adds no useful primitive distinction and misleadingly suggests JavaScript semantics.

| Value family | `==` recommendation | `!=` recommendation | Identity equality |
| --- | --- | --- | --- |
| `boolean` | Same Boolean value. | Negation of `==`. | Not distinct. |
| `string` | Same sequence of Unicode code units for the initial backend-neutral law. | Negation of `==`. | Not exposed. |
| `number` | IEEE numeric equality: NaN unequal to itself; signed zeros equal. | Negation of `==`. | Not exposed. |
| Payload enum | Same nominal enum type, same case, and recursively value-equal ordered payloads. | Negation of `==`. | Not exposed. |
| Arrays | Deferred. Do not emit accidental JS reference equality or claim structural equality. | Deferred with `==`. | No source operator yet. |
| Future objects/classes | Deferred until value, reference, and identity laws are defined. | Deferred with `==`. | If needed, use a future explicit identity operation rather than overloading value equality silently. |

Payload-enum equality is structural only through value families whose equality is defined. An enum containing an array is not equality-comparable until array equality is decided. Recursive/cyclic value equality is deferred because current language law does not admit general mutable object graphs.

The important alternative is to preserve all four spellings, with `==` as value equality and `===` as identity equality. That is familiar to JavaScript authors but creates meaningless distinctions for primitive values, exposes representation identity for immutable tagged data, and commits future classes before their identity law exists. Aliasing both pairs is also rejected as redundant surface.

Until CTS-M0d accepts and enforces this recommendation, equality MIR must be excluded from CTS-M1. The current binder's acceptance of all four spellings is implementation evidence, not authority.

## Evaluation order

### Recommended universal law

Copeland evaluation should be deterministic and left-to-right:

| Construct | Recommended order |
| --- | --- |
| Function call | Evaluate the callee target, then arguments from left to right, then invoke. Named calls currently have no computed target, so arguments remain the observable sequence. |
| Binary expression | Evaluate the left operand before the right operand, then apply the operator. |
| Array literal | Evaluate elements from first to last. |
| Payload construction | Evaluate payload arguments in declaration/source order from left to right, then construct the value. |
| Match | Evaluate the scrutinee exactly once; inspect its tag; evaluate exactly one selected arm. Unselected patterns and expressions have no effects. |
| `&&` / `\|\|` | Evaluate the left Boolean first. Evaluate the right only when required by short-circuit semantics. |
| Statements | Execute in source order unless an explicit branch, return, propagation, trap, or later control construct transfers control. |
| Backend optimization | Reordering is allowed only when observationally equivalent under these rules. |

The important alternative is to leave order backend-defined. That would make the first JavaScript emitter an accidental specification and permit later backends to disagree. A fixed left-to-right law matches current tree order and JS execution while remaining a Copeland decision.

## Payload-enum semantic and JavaScript representation contract

Payload enums are nominal tagged values. Their language-visible meaning is:

- every enum declaration defines a distinct nominal type identity;
- every case has a stable case identity;
- payload positions are ordered and typed according to the declaration;
- construction evaluates payloads left-to-right;
- match dispatches by nominal type and case, evaluates the scrutinee once, and selects one arm;
- exhaustive validation makes a missing source arm impossible in valid programs;
- an invalid runtime tag is compiler-state corruption or an interop-boundary violation, not a normal match result.

### Recommended JavaScript shape

Use compiler-owned, null-prototype, frozen records with an unexported per-enum type token, a stable textual case tag, and a frozen ordered payload array. Conceptually:

```js
const __Shape = Object.freeze(Object.create(null));

const value = Object.freeze(Object.assign(Object.create(null), {
  $type: __Shape,
  $tag: "Circle",
  $payload: Object.freeze([10])
}));
```

This is a representation sketch, not emitted output or ABI.

| Question | Status | Decision or recommendation |
| --- | --- | --- |
| Nominal type identity | Normative | Values of separately declared enums never compare or match as the same type. |
| Case tag | Recommended | Use the declared case name internally. It is deterministic and does not renumber when another case is inserted. |
| Payload order | Normative | Declaration order. |
| Prototype inheritance | Rejected | Tagged values and their type tokens must not inherit ordinary JavaScript prototype behavior. |
| Frozen representation | Recommended | Freeze the tagged record and payload array to protect compiler invariants and value semantics. |
| Public observability | Recommended | The shape is private generated representation, observable only through Copeland construction, match, and accepted equality. |
| Separate construction equality | Recommended | Two separately constructed values compare structurally under Copeland `==`, not by JS reference identity. |
| Invalid tags | Recommended | Enter a deterministic compiler panic/trap path. Do not turn corruption into an `err` value. |
| Shared runtime | Recommended | No shared runtime package for the first slice. Emit small per-module constructors/dispatch and, when needed, one private panic helper. |

The important alternative is ordinary object literals with prototypes and mutable public fields. It is shorter output but leaks exactly the prototype and mutation semantics Copeland rejects. Numeric case tags are slightly smaller, but declaration edits can silently renumber them; textual internal tags are preferred until an explicit serialization ABI exists.

## Explicit fallibility

### Canonical abstract model

A fallible value has the abstract type `Result<T, E>` with exactly two alternatives:

```text
ok(value: T)
err(error: E)
```

The source spelling `T ! E` denotes this fallibility relationship. In a function signature it states that normal success has type `T` and failure has type `E`. The same spelling is recommended for first-class fallible value types when they become storable and matchable; `Result<T, E>` is explanatory notation, not recommended source syntax.

The semantic pipeline is:

```text
Source sugar (`?`, `!`, `try`/`except`)
    ↓
Canonical `ok`/`err` construction, matching, and explicit control flow
    ↓
Targeted fallible-value and branch representation in Cope MIR
    ↓
Frozen JavaScript tagged values plus generated branches
```

JavaScript exceptions are not part of this pipeline.

### Operator and construct laws

| Construct | Status | Meaning |
| --- | --- | --- |
| Fallible signature `T ! E` | Normative | The function returns either `ok(T)` or `err(E)`. Success and error types are explicit. |
| `ok(value)` | Normative | Construct the success alternative. The value must match `T`. |
| `err(error)` | Normative | Construct the failure alternative. The error must match `E`. |
| Ordinary `return value` in a fallible function | Normative | Shorthand for returning `ok(value)`, matching current behavior. |
| `?` | Normative | On `ok(v)`, produce `v`; on `err(e)`, transfer that error to the nearest lexical fallibility target. |
| `!` postfix unwrap | Normative surface; Recommended failure behavior | On `ok(v)`, produce `v`; on `err(e)`, enter a nonrecoverable Copeland panic/trap. It never branches to `except`. |
| `match` with `ok`/`err` | Normative | Explicitly branch over a first-class fallible value, binding the selected payload. |
| `try`/`except` | Normative surface; Recommended shape | Handle explicit `err` control flow using the same result model. It does not catch JavaScript exceptions. |
| Error conversion | Recommended | No implicit conversion between error types. Mismatches are compile-time diagnostics. |
| Return/error inference | Recommended | Function success and error types remain declared. `ok`/`err` may use contextual typing from a declared `T ! E`; no unconstrained result-type inference initially. |

`!` is intentionally different from `?`. Making an `err` branch to an enclosing `except` would make unwrap an alternate spelling for propagation and erase its assertion-like meaning. A panic may use a private generated JS throw as an abort mechanism, but Copeland `try`/`except` must never catch it and it must never be converted to `err` implicitly.

Candidate explicit construction, matching, and unwrap syntax is:

```ts
enum ParseError {
  Invalid,
}

function failedParse(): number ! ParseError {
  return err(ParseError.Invalid);
}

function recover(outcome: number ! ParseError): number {
  return match outcome {
    ok(value) => value,
    err(error) => 0,
  };
}

function asserted(text: string): number {
  return parseNumber(text)!;
}
```

These are proposals for the user-directed fallibility surface, not currently accepted grammar. `err` is contextually checked against the function signature; the match parameter uses the recommended first-class `T ! E` type spelling; and `asserted` traps if parsing produces `err`.

### Recommended `try`/`except` surface

Repository history contains no Copeland `try`/`except` syntax. The recommended initial form is expression-shaped:

```ts
function parseOrZero(text: string): number {
  return try {
    const parsed: number = parseNumber(text)?;
    parsed + 1
  } except (error: ParseError) {
    0
  };
}
```

This example is proposed syntax, not currently accepted source. The protected block's normal value and the handler block's normal value must have the same type. Every `?` that targets this handler must carry the handler's declared error type. The handler binding is block-scoped.

Expression shape is recommended because it gives the construct one checkable result type and composes with declarations, returns, and other expressions. The important alternative is statement-only `try`/`except`, familiar from exception languages but awkward for value recovery and likely to introduce separate definite-return rules. A statement form may later be sugar for a `void`-valued expression; it is deferred, not promised.

The adjacent Oct-shaped shader doctrine uses prefix `try expression` as another propagation spelling. Copeland already has normative postfix `?`, so importing that prefix form would overload `try` with two error-flow roles once paired `try`/`except` exists. The recommendation is to reserve `try` for the paired handler construct and keep `?` as propagation. The alternative—supporting both `try expression` and `try { ... } except`—is syntactically distinguishable but expands the surface without adding semantics.

Within a protected `try` body, `?` targets the nearest enclosing `except`. Outside a protected body, it propagates from the current fallible function. Within an `except` body, `?` targets an outer `try` or the current fallible function; the handler does not catch its own failures. Nested `try` expressions use the nearest lexical handler.

### Desugaring examples

One fallible call:

```ts
try {
  const parsed: number = parseNumber(text)?;
  parsed + 1
} except (error: ParseError) {
  0
}
```

has canonical meaning equivalent to:

```ts
match parseNumber(text) {
  ok(parsed) => parsed + 1,
  err(error) => 0,
}
```

Two nested fallible calls:

```ts
try {
  const first: number = parseNumber(left)?;
  const second: number = parseNumber(right)?;
  first + second
} except (error: ParseError) {
  0
}
```

has canonical control flow equivalent to:

```ts
match parseNumber(left) {
  ok(first) => match parseNumber(right) {
    ok(second) => first + second,
    err(error) => 0,
  },
  err(error) => 0,
}
```

An implementation must preserve single evaluation and must not duplicate handler side effects merely because the explanatory desugaring repeats the handler text.

### Nested calls, returns, and mismatches

- A nested call is evaluated once. Each `?` examines its direct fallible value before later expressions execute.
- Propagating from a function returns the original typed `err(e)` without wrapping it in another success or error layer.
- A fallible function's ordinary return creates `ok`; an explicit `err` return creates failure.
- A nonfallible function may use `try`/`except` when every error is handled and both arms produce its ordinary return type.
- A `?` with error type `E1` cannot target a function or handler expecting `E2` without a future explicit conversion.
- `!` may appear in a nonfallible function because its failure path traps rather than returning `err`; it should be used only as an explicit assertion.

### Cope MIR gap and recommendation

Current MIR is sufficient only for direct-call function propagation already represented by `MirCallExpression(IsPropagated: true)`. It is not sufficient for the complete normative model because a fallible result is not a first-class MIR value.

The recommended later MIR work is targeted, not a generalized exception framework:

- represent `T ! E` as an explicit fallible/result type at value positions;
- represent `ok` and `err` construction;
- represent result inspection with typed success/error bindings;
- represent propagation as explicit control flow with a function-return or lexical-handler target;
- represent unwrap with an explicit panic edge;
- lower `try`/`except` before or during MIR construction into result inspection and normal control-flow joins; do not add JavaScript exception regions.

No source-level `try` node needs to survive as backend magic if the lowerer can preserve lexical handler targets and single evaluation. Current generic enum `match` machinery is useful evidence, but current `MirMatchExpression` plus string-only `MirType` cannot honestly encode a first-class fallible value or local early exit by themselves.

### Recommended JavaScript representation

Use two fixed compiler-owned, null-prototype, frozen shapes. Conceptually:

```js
// Illustrative only.
{ $tag: 0, $value: value } // ok
{ $tag: 1, $error: error } // err
```

The objects are created with null prototypes and frozen. `E` is a Copeland value, not implicitly a JavaScript `Error`. Propagation emits a tag test and an explicit `return` or local handler branch. Result matching emits a tag dispatch. Unwrap emits a tag test and a private panic call. A common external runtime package is not justified initially; small private helpers may be emitted when repeated code warrants them.

Host exceptions enter this model only through a future explicit interop adapter that catches a declared host failure and converts it to a declared Copeland error value. No arbitrary `throw`, rejected promise, host callback failure, or property access is implicitly caught by `try`/`except`.

## Optionality

| Statement | Status | Law |
| --- | --- | --- |
| `null` as ordinary absence | Rejected | It is not a Copeland value. |
| `undefined` as ordinary absence | Rejected | It is not a Copeland value. |
| Ambient nullability | Rejected | A type does not silently gain absence. |
| Tagged optional values | Normative direction | Payload enums are the general mechanism for explicit alternatives. |
| Canonical standard spelling | Unresolved | No repository evidence settles `Option`, `Maybe`, case names, generic syntax, or standard-library ownership. |
| Privileged runtime representation | Recommended against | Ordinary payload-enum representation is sufficient. |

Conceptually, users should be able to define an option-like value:

```text
Option<T> = some(T) | none
```

That notation is explanatory, not accepted syntax. The recommendation is to use ordinary payload-enum semantics and representation, with no special optional MIR node, sentinel, or JavaScript `null`/`undefined`. The important alternative is a built-in `Option`; it provides a uniform name but prematurely decides generics, standard-library ownership, and case spelling. Those remain unresolved.

## JavaScript interop boundary

The first backend is a closed-world emitter, not a general JavaScript FFI.

- Generated program internals remain compiler-owned and closed-world.
- Implicit global lookup and assignment remain rejected.
- Arbitrary JavaScript values cannot enter typed Copeland code without validation/conversion at a future explicit boundary.
- Host calls require future explicit interop declarations or adapters; M0c does not design their syntax.
- Host exceptions may become Copeland `err` only through an explicit adapter with a declared conversion.
- Compiler representations for enums and fallible values are private and are not stable public ABI unless a later export contract says otherwise.
- CTS-M1 need not implement general interop. A backend test harness invoking a known generated `main` function is test plumbing, not a product ABI.

The important alternative is to expose generated functions and objects as ordinary JavaScript and trust TypeScript annotations. That would allow unchecked values, prototypes, exceptions, and mutation to cross the boundary, contradicting the closed-world thesis.

## JavaScript semantic matrix

| Law/construct | Canonical Copeland meaning | Direct JS safe? | Generated enforcement | Runtime support | MIR sufficient? | Status | Blocking first slice? |
| --- | --- | ---: | --- | --- | ---: | --- | ---: |
| `const` | Non-reassignable block binding | Yes | Emit lexical binding after validation | None | Yes | Normative | No |
| `let` | Reassignable block binding of fixed declared type | Yes | Preserve assignments and order | None | Yes | Normative | No; may be excluded |
| `var` | No binding; intentional profile rejection | N/A | Stable frontend diagnostic | None | N/A | Rejected | Yes, via CTS-M0d enforcement |
| Boolean conditions | Only `boolean` branches | Yes | Frontend type gate | None | Yes | Normative | No |
| Supported arithmetic | Binary64 operations, no coercion | Yes, after typing | Prevent mixed operands before emission | None | Yes for current operators | Recommended | Yes if arithmetic is included |
| Evaluation order | Deterministic left-to-right with short-circuiting | Usually | Emit temporaries when JS expression form would obscure the law | None | Mostly; explicit order metadata absent | Recommended | Yes |
| Equality | Typed value equality; recommended `==`/`!=` only | Primitive only | Type-directed enum comparison; reject unsupported families | Per-enum code at most | No; spellings/types need canonicalization | Recommended | No; exclude equality |
| Named calls | Statically resolved, typed direct calls | Yes | Name mangling and arity/type validation | None | Yes | Normative | No |
| Arrays | Homogeneous ordered literal values; mutation/bounds unresolved | Not fully | Representation/mutation policy still needed | None initially | Literal yes; broader law no | Deferred | No; exclude arrays |
| Payload enums | Nominal frozen tagged values | No | Type token, tag, payload construction | Private generated code | Yes for construction | Normative semantics; Recommended JS shape | No; exclude initially |
| Exhaustive match | Single scrutinee evaluation and one selected arm | No | Nominal/tag checks and invalid-tag panic | Private panic helper at most | Yes for current match | Normative semantics; Recommended JS shape | No; exclude initially |
| Fallible return | Explicit `ok(T)` or `err(E)` value | No | Tagged result construction | Private generated code | Partial | Normative | No; exclude initially |
| `?` | Unwrap success or transfer error to lexical target | No | Tag branch plus return/handler jump | None beyond result shape | Direct function propagation only | Normative | No; exclude initially |
| `!` unwrap | Unwrap success or panic on error | No | Tag branch plus private panic | Private panic helper | No | Normative surface; Recommended trap | No; exclude initially |
| `try`/`except` | Handle explicit error flow, never JS exceptions | No | Result branches and typed join | None beyond result shape | No | Normative surface; Recommended expression shape | No; exclude initially |
| Optionality | Explicit ordinary tagged value; no ambient absence | No special mapping | Use payload-enum lowering | Same as enums | Generic enums yes | Recommended; spelling unresolved | No |
| Host interop | Future explicit checked boundary | No | Conversion adapters and declared exception capture | Boundary-specific, later | No | Deferred | No; omit interop |

## First executable JavaScript subset

CTS-M1 should begin with a deliberately smaller subset than the current frontend. It should accept only nonfallible MIR that uses:

- `MirProgram` and nonfallible `MirFunction`;
- `MirParameter` and read-only `MirLocal`;
- `MirVariableDeclarationStatement` and `MirReturnStatement`;
- `MirLiteralExpression` for Boolean and currently accepted numeric literals;
- `MirVariableExpression`;
- `MirBinaryExpression` for accepted binary64 arithmetic needed by the proof;
- `MirCallExpression` for nonfallible named calls;
- `MirIfExpression` with a Boolean condition.

It should explicitly reject, with backend diagnostics, at least:

- mutable assignment and loops;
- equality until its recommendation is accepted and frontend spelling is enforced;
- arrays and array operations;
- payload enums and match, despite their designed later representation;
- every fallible function/call and `IsPropagated` call;
- unwrap, `try`/`except`, and first-class results, which are not yet in MIR;
- objects, classes, modules, closures, and host interop.

No runtime helper is needed for this first slice.

Candidate source, to be added only with CTS-M1 tests:

```ts
function add(left: number, right: number): number {
  return left + right;
}

function main(): number {
  const answer: number = add(40, 2);
  return if true {
    answer
  } else {
    0
  };
}
```

The expected observable result is `main() == 42`, asserted by backend test plumbing that directly invokes the known generated function. This is not general host interop or a stable export ABI. The example proves source-to-MIR preservation, numeric arithmetic, typed direct calls, read-only locals, Boolean branching, return flow, JavaScript emission, loading, and execution without depending on equality source operators, arrays, tagged data, fallibility, or runtime helpers.

## Bounded follow-up sequence

1. **CTS-M0d — accepted profile enforcement.** Accept or revise the M0c recommendations needed by the first slice; deliberately parse and reject `var` with a stable diagnostic and add its invalid fixture; codify binary64 and left-to-right laws in the canonical profile; reserve/reject unsupported equality spellings or families once accepted; add only focused language evidence required before emission. Do not add backend abstractions.
2. **CTS-M1 — minimal MIR-only JavaScript backend.** Emit and execute exactly the nonfallible subset above, reject every other MIR node explicitly, and prove `main()` returns 42. Use no shared runtime package and expose no general interop ABI.
3. **Later CTS family milestones.** Expand one accepted family at a time: payload enums plus exhaustive match; equality; arrays after mutation/bounds law; then first-class fallibility after the targeted MIR gaps are resolved. Add `!` and `try`/`except` only with their accepted source syntax, result matching, and trap/handler semantics.

## Decision ledger

### Normative

- `const`, `let`, and rejected `var` binding meanings.
- Boolean-only conditions, no implicit coercion, and closed-world internals.
- Nominal payload enums and exhaustive matching.
- Explicit fallibility with `ok`/`err`, `?`, postfix unwrap `!`, result matching, and a `try`/`except` surface sharing one non-exception model.
- Rejection of `null`, ordinary `undefined`, ambient nullability, JavaScript prototype semantics, and implicit host exception conversion.

### Recommended, awaiting acceptance

- IEEE-754 binary64 as the complete initial `number` law.
- `==`/`!=` typed value equality; reserve/reject `===`/`!==`.
- Deterministic left-to-right evaluation.
- Frozen null-prototype enum and result representations.
- Structural payload-enum equality.
- Expression-shaped `try`/`except`, nearest lexical handler targeting for `?`, and panic/trap behavior for `!` on `err`.
- Ordinary payload enums for optionality, without privileged runtime representation.

### Unresolved

- The canonical optional type name, case names, generic syntax, and standard-library ownership.
- Deep immutability and future object/class identity.
- Array equality, mutation, indexing, and bounds.
- Exact panic diagnostics/host observability and whether a future host can intercept program termination.
- The complete explicit interop syntax and ABI.

### Deferred

- General local type inference and new numeric literal/conversion syntax.
- Future integer types.
- Statement-shaped `try`/`except` sugar.
- Classes, modules, closures, async fallibility, and general JavaScript interop.

### Rejected

- JavaScript loose equality and implicit coercion.
- `var`, `null`, ordinary `undefined`, ambient nullability, and prototype-based tagged values.
- Using ordinary JavaScript exception unwinding as Copeland's canonical fallibility model.
- Treating generated representation details as public ABI by accident.
