# Copeland TS callables and explicit capture design (CTS-CALL-M0a)

**Status:** documentation-only design milestone. No production syntax, binding, MIR, backend, evaluator, fixture, package, or runtime behavior is implemented or changed by M0a.

## Executive decision

Copeland TS should support first-class functions, but it should not inherit JavaScript's implicit lexical-closure law.

The bounded semantic model is:

- Named functions remain ordinary declarations and direct named calls remain the fast path.
- First-class callable values are future runtime values with semantic shape `(code identity, immutable environment)`.
- A callable may read outer lexical runtime state only through an explicit capture declaration at the construction site.
- A noncapturing function expression never silently becomes a closure.
- Open generic function values are excluded. A generic function must be closed before becoming a runtime callable value.

M0a recommends a staged implementation:

1. `CTS-CALL-M0b`: function types, named nongeneric function values, closed generic specialization values, noncapturing function expressions, callable MIR, and C#/JavaScript first-class invocation.
2. `CTS-CALL-M1a`: ratify explicit-capture surface syntax if owners want a final syntax checkpoint before implementation.
3. `CTS-CALL-M1b`: immutable explicit capture through frontend, MIR, C#, and JavaScript.
4. `CTS-CALL-M2`: parity hardening, diagnostics/limits closeout, and TSON/table exclusion hardening.
5. `CTS-EVAL-M0a`: evaluator design against the same closed callable model.

If owners prefer the smallest safer step, M0b may exclude explicit capture entirely and ship only noncapturing callable values plus named/closed-generic references.

## Current implementation inventory

The current compiler implements only named function declarations, direct named calls, and closed generic specialization as additional named functions.

Implemented evidence:

- Parser:
  - `FunctionDeclarationSyntax` in [`src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs) and `ParseFunctionDeclaration` in [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs).
  - `CallExpressionSyntax` and `GenericCallExpressionSyntax` in [`SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs).
  - `ParseTypeSyntax` and `ParsePostfixTypeSyntax` in [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs) parse named/array/result/column/parenthesized types only; there is no function-type production.
  - `ArrowToken` exists only for `match` arms, not callable syntax: [`SyntaxKind.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs), `MatchArmSyntax` in [`SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), and `ParseMatchArm` in [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs).
- Semantics:
  - `FunctionSymbol` exists in [`src/Copeland/Copeland.TS/Semantics/Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs).
  - There is no `FunctionTypeSymbol` in [`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs).
  - `BindName` resolves variables and parameters only; named functions are not value expressions today: [`src/Copeland/Copeland.TS/Semantics/Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs).
  - `BindCall` and `BindGenericCall` only bind direct named calls: [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs).
  - Closed generic calls reuse cached specialized `BoundFunctionDeclaration` instances: `GetOrCreateClosedInstantiation` in [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs).
  - Nested records and nested tables are parsed but rejected; nested functions are not parsed: [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs).
- Bound tree:
  - `BoundFunctionDeclaration` and `BoundCallExpression` exist in [`src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs).
  - No bound node represents function values, lambda literals, capture environments, or first-class invocation.
- MIR:
  - `MirFunction` and `MirCallExpression` exist in [`src/Copeland/Copeland.TS.Mir/MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs).
  - `MirLowerer` lowers `BoundCallExpression` directly to `MirCallExpression(functionName, arguments, type)` in [`src/Copeland/Copeland.TS/Lowering/MirLowerer.cs`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs).
  - No MIR function-value, callable-construction, invoke-by-value, environment, or capture-slot node exists.
- C# backend:
  - `EmitFunction` emits every MIR function as `public static` members on `CopelandModule` in [`src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs).
  - `MirCallExpression` emits as a direct static call in the same file.
- JavaScript backend:
  - `EmitFunction` emits top-level `function name(...)` declarations and `EmitCall` emits direct named calls in [`src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs).
  - `JavaScriptEmissionModel` tracks backend-private lexical scopes for emitted bindings only; it is not a source callable/capture model: [`src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptEmissionModel.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptEmissionModel.cs).

Representative tests:

- Parser coverage: [`tests/Copeland/Copeland.TS.Tests/ParserTests.cs`](../../../tests/Copeland/Copeland.TS.Tests/ParserTests.cs).
- Function-call validity/invalidity fixtures: [`tests/Copeland/Copeland.TS.Tests/Language/Valid/functions`](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/functions) and [`tests/Copeland/Copeland.TS.Tests/Language/Invalid/functions`](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/functions).
- Generic specialization identity/reuse: [`tests/Copeland/Copeland.TS.Tests/BinderTests.cs`](../../../tests/Copeland/Copeland.TS.Tests/BinderTests.cs).
- MIR corpus ownership: [`tests/Copeland/Copeland.TS.Tests/MirCorpusTests.cs`](../../../tests/Copeland/Copeland.TS.Tests/MirCorpusTests.cs).
- C# backend/runtime parity: [`tests/Copeland/Copeland.TS.Backend.CSharp.Tests`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests).
- JavaScript backend/runtime parity: [`tests/Copeland/Copeland.TS.Backend.JavaScript.Tests`](../../../tests/Copeland/Copeland.TS.Backend.JavaScript.Tests).

## Callable algebra

M0a recommends the following semantic categories.

| Category | Initial status | Notes |
| --- | --- | --- |
| Direct named function call | implemented law | Current `FunctionSymbol` plus `BoundCallExpression` plus `MirCallExpression`. |
| Reference to nongeneric named function as a value | recommended for M0b | Smallest first-class case. No environment. |
| Reference to explicitly closed generic specialization as a value | recommended for M0b | Must point at the existing closed specialization cache entry. |
| Noncapturing function expression | recommended for M0b | May lower to a lifted function with empty immutable environment. |
| Explicitly capturing function expression | recommended for M1b | Requires final capture syntax and environment model. |
| Runtime callable value | recommended for M0b/M1b | Canonical runtime value is `(code identity, immutable environment)`. |
| Compiler-only/static callable | future boundary only | Must not be conflated with runtime callables. |

Open generic function values are excluded initially.

## Familiar source syntax

### Recommended noncapturing syntax

Prefer ordinary TypeScript-shaped arrows for the noncapturing case:

```ts
const double: (value: number) => number =
    (value: number) => value * 2;
```

Rationale:

- familiar to TypeScript users;
- compact and readable;
- clearly expression-shaped;
- does not itself promise capture;
- works naturally with multiline formatting.

### Named functions as values

Named functions should be usable as values without wrapper syntax:

```ts
const parseNumber: (text: string) => number = parseNumberImpl;
```

### Closed generic specialization as a value

Use the existing explicit specialization spelling:

```ts
const parser = parse<number>;
```

This is a new value-form only. It must reuse the same semantic identity and specialization cache already used by explicit and inferred calls.

### Callable parameter and return types

Prefer ordinary TypeScript-shaped arrow types:

```ts
const mapOne: (value: number, f: (value: number) => number) => number =
    (value: number, f: (value: number) => number) => f(value);
```

Initial compatibility should stay exact; see "Function-type compatibility."

### Explicit capture syntax

M0a recommends this primary form:

```ts
const addBase =
    capture { base } (value: number) => base + value;
```

Why this form is preferred:

- `capture` visibly distinguishes the construct from an ordinary block.
- The capture set is explicit at the construction site.
- The form stays compact but still formats well across lines.
- It does not resemble JavaScript's implicit lexical closure.
- It leaves room for later aliases or expressions inside the braces without changing the outer shape.

Secondary fallback form if owners reject prefix-brace capture:

```ts
const addBase =
    capture(base)((value: number) => base + value);
```

This is parseable but less readable, less multiline-friendly, and visually more wrapper-like than source-law-like.

Owner decision if deferred to M0b:

- ratify `capture { ... } arrow` as the product spelling, or
- explicitly defer capture syntax to `CTS-CALL-M1a` while still allowing `CTS-CALL-M0b` to ship noncapturing callables only.

## No implicit lexical closure

M0a ratifies this intended law:

- A noncapturing function may use its own parameters and locals.
- It may use closed type parameters already erased before runtime.
- It may name ordinary named functions and closed generic specializations.
- It may use permitted compiler-owned compile-time constants where those already participate as ordinary value expressions.
- Referencing an outer lexical runtime binding without explicit capture is a compile-time error.
- The diagnostic must name the referenced outer binding and recommend `capture`.
- The compiler must never silently rewrite a noncapturing function into a closure.

Initial capture classification:

| Referenced name family | Capture required? | Recommended law |
| --- | --- | --- |
| Outer `const` local | yes | runtime lexical binding |
| Outer mutable `let` local | yes | runtime lexical binding |
| Outer function parameter | yes | runtime lexical binding |
| Loop variable | yes | runtime lexical binding |
| Pattern-bound variable | yes | runtime lexical binding |
| Module/compilation-unit constant local | yes if it is an ordinary runtime binding | not a special closure exemption |
| Named function | no | function identity, not lexical state |
| Closed generic specialization | no | already-closed function identity |
| Record/table declaration singleton | no for the declaration identity itself | same rule as other named declarations |
| Compiler-owned constants such as `$schema` | not a callable-capture feature | remains governed by the existing intrinsic/metadata rules |
| Type/interface/alias names | no runtime capture concept | type-scope lookup only |

The important split is runtime value scope versus type-only scope. Type lookup is not capture.

## Explicit-capture semantics

The recommended initial capture law is immutable binding snapshots.

- Capture expressions are evaluated exactly once.
- Evaluation order is authored order.
- Evaluation occurs when the callable value is constructed.
- The published environment is immutable.
- The callable observes the captured value, not later rebinding of the source variable.
- Capturing a reference-like runtime value captures that reference/value; it does not imply deep copy.
- Capturing a mutable array or record-like host carrier does not make that referenced value immutable.
- Shared mutable capture cells and by-reference rebinding are excluded.

Initial capture-list surface:

- allow identifiers only in the first implemented slice;
- reject duplicates;
- reject unknown names;
- reject alias forms and arbitrary expressions until separately approved;
- reject computed/destructured/spread/default capture forms;
- allow the same source value to be captured again only if a future alias feature explicitly names the second slot.

This is intentionally smaller than JavaScript closure semantics, but it covers ordinary callback/component-style authored state once capture syntax exists.

## Mutation and rebinding law

- Rebinding the source local after callable construction does not affect the callable's environment snapshot.
- Mutating an object/array referenced by a captured value affects what later reads observe, because the captured value may itself be a reference-like runtime object.
- Mutating the capture environment itself is illegal in the initial language.
- Returning a callable whose environment outlives the declaring frame is legal once explicit capture exists, because the environment is an independent immutable runtime value.

Copeland should not model JavaScript's shared mutable lexical cells unless a separate future feature explicitly introduces them.

## First-class storage and flow

Recommended eventual runtime contexts for closed callable values:

- locals;
- parameters;
- return values;
- `if`/`match` branches when the branch types are exactly the same callable type;
- record fields;
- payload-enum payloads;
- `Result` success or error payloads;
- arrays.

Recommended exclusions:

- TSON values and schemas;
- record-table declaration cells;
- callable equality and hashing;
- truthiness/falsiness;
- method/`this` surfaces;
- host callable leakage.

Interfaces may admit callable-typed fields only if the existing field-requirement algebra can carry them as exact field types without introducing methods, overloads, or variance.

## Function-type compatibility

M0a recommends exact structural compatibility.

- exact parameter count;
- exact parameter types;
- exact return type;
- exact fallibility type;
- no optional or rest parameters initially;
- no bivariance or host variance;
- no implicit parameter dropping;
- no overload sets;
- no structural callable object types;
- no `any`;
- no implicit async or host-delegate conversions.

Transparent aliases remain transparent:

- `type Mapper = (value: number) => number;`
- `Mapper` and `(value: number) => number` are the same canonical function type.

Nominal function-signature identity is not recommended unless later implementation evidence shows canonical structural comparison is insufficient.

## Generic integration

M0a adopts the completed CTS-TYPE doctrine:

- A generic function becomes a callable value only after closed type arguments are known.
- Explicit and inferred closed instantiations share one semantic identity and specialization cache.
- A callable value references the existing closed specialization; it does not create a second runtime generic mechanism.
- Generic inference does not infer from an uninvoked open generic function value in the initial slice.
- Existing generic-to-generic and generic-recursion restrictions remain unchanged.
- Interfaces remain erased requirement sets, not runtime callable constraints.

Noncapturing generic function expressions should be deferred until named-function callable values and ordinary function-type storage are proven.

## Recursion and cycles

Current and recommended status:

| Topic | M0a status |
| --- | --- |
| Named direct nongeneric recursion | already permitted by the named-function model; keep permitted |
| Mutual named nongeneric recursion | keep permitted if existing declaration binding already allows it |
| Generic direct recursion | remain rejected under existing `COPE-GENERIC-0014` |
| Generic-to-generic calls | remain rejected under existing `COPE-GENERIC-0006` |
| Callable capturing itself | reject initially |
| Mutually capturing callable values | reject initially |
| Recursive callable-containing records/enums | defer until callable storage exists and cycle policy is proven |
| Recursive specialization/callable identity graphs | keep bounded and iterative |

Initial callable environments should reject capture-environment cycles explicitly. Compiler analysis must use bounded iterative worklists, not unbounded recursive graph traversal.

## Bound and MIR recommendation

The canonical semantic form should be:

```text
callable value = {
    code identity,
    immutable environment value
}
```

Recommended additions after M0a:

- canonical callable type node;
- stable callable definition identity;
- lifted callable definition/body;
- explicit environment definition with declaration-ordered slots;
- callable-construction expression;
- callable-invocation expression;
- capture-slot access expression.

Capturing functions lower conceptually to:

```text
lifted function(environment, ordinary parameters) -> result
```

Direct named calls should remain the existing optimized MIR form. Only first-class invocation needs a new callable-invoke MIR family.

No authored lexical-scope graph should survive canonical MIR.

## C# backend direction

Recommended direction:

- keep direct named calls as direct static calls;
- emit lifted callable bodies as static methods;
- emit sealed environment carriers with explicit readonly fields;
- emit callable carriers containing code identity and environment;
- optionally use private delegates internally if they are not the source law.

Avoid:

- reflection;
- `dynamic`;
- expression trees;
- runtime code generation;
- reliance on Roslyn/C# closure lowering as authoritative Copeland semantics.

This remains compatible with the current `public static` function-emission posture in [`CSharpBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs).

## JavaScript backend direction

Recommended direction for both Diagnostic and Symbolic profiles:

- direct named calls remain direct generated calls;
- callable values carry explicit immutable environment objects or arrays;
- environment slots use backend-private stable names or indexes;
- exactly-once capture evaluation is staged explicitly in emitted code;
- first-class invocation reads the callable carrier and calls the lifted implementation without exposing JavaScript `this`;
- private helpers, type tokens, and provenance machinery remain backend-local.

Generated host closures may be used privately only if the explicit Copeland environment remains authoritative and testable.

This does not reopen `CTS-JS-EMIT`; callable helpers should follow the existing helper-name allocation and profile discipline.

## Future Cope MIR interpreter contract

`CTS-EVAL` should target this execution contract:

```text
(code identity, immutable environment, arguments)
-> call frame
-> result or typed propagation
```

Required evaluator rules:

- direct named calls and first-class invocation are distinct entry operations but converge on the same callable body contract;
- the evaluator receives already-lowered capture slots and never reconstructs frontend lexical scopes;
- capture-slot access reads the immutable environment value;
- Result propagation and typed `try`/`except` reuse the existing typed-fallibility contract;
- terminal unwrap/invariant behavior remains explicit evaluator behavior, not host exception policy;
- recursion/call-depth limits must be explicit and deterministic;
- evaluation order remains left-to-right.

## Future static-execution boundary

Explicit capture helps future static execution because it makes dependencies visible and enumerable.

- `static if`, `static match`, `static for`, and compile-time initialization can later reject or admit callables based on explicit capture contents.
- Runtime callables do not automatically become compile-time executable.
- Static execution will need a stricter capability/value policy than ordinary runtime execution.

M0a does not design the complete static language.

## TS-XML and UI callback implications

- `.tsx` is intended for Copeland TS-XML, not unrestricted JSX.
- UI callbacks require first-class callable values.
- React compatibility does not justify importing JavaScript implicit closure semantics.
- Event callbacks with state must use explicit capture or some separate explicitly stateful runtime/component facility.
- Machina/component lifecycle is a separate ownership problem and is not solved by callable capture alone.

## Diagnostics

Recommended future `COPE-CALL-*` family:

- `COPE-CALL-0001`: outer lexical runtime name used without explicit capture;
- `COPE-CALL-0002`: unknown capture name;
- `COPE-CALL-0003`: duplicate capture;
- `COPE-CALL-0004`: invalid capture form;
- `COPE-CALL-0005`: capture of unsupported binding kind;
- `COPE-CALL-0006`: callable signature mismatch;
- `COPE-CALL-0007`: open generic used as runtime callable;
- `COPE-CALL-0008`: invocation of non-callable value;
- `COPE-CALL-0009`: callable arity mismatch;
- `COPE-CALL-0010`: unsupported callable storage/context;
- `COPE-CALL-0011`: capture cycle;
- `COPE-CALL-0012`: callable resource limit exceeded.

Reuse existing `COPE-TYPE-*`, `COPE-BIND-*`, or generic diagnostics where they already express the law precisely.

## Resource recommendations

Base new callable limits on existing bounded generic/type policies rather than arbitrary large values.

Recommended initial caps:

- parameters per callable: 32;
- captures per callable: 16;
- nested callable-expression depth: 16;
- callable definitions per compilation: 1024;
- function-type nesting depth: 16;
- evaluator call-depth default cap: 256;
- capture-cycle diagnostic path length: 16;
- callable identity rendered-length budget: align with current bounded specialization-identity discipline.

Rationale:

- current closed-type depth is 16;
- current inference depth is 16;
- current per-generic closed-instantiation limit is 16;
- current total closed-instantiation cap is 128;
- current requirement/type-parameter caps are deliberately modest.

Callable limits should follow that same "small, deterministic, explainable" posture.

## Fixture and parity plan

Valid future cases:

- named function as value;
- closed generic specialization as value;
- noncapturing function expression;
- explicit immutable capture;
- escaping returned callable;
- callable parameter and return;
- approved storage in record/enum/Result/array;
- conditional or match selection between same-typed callables;
- exactly-once capture evaluation;
- authored capture order;
- rebinding after capture;
- C#, Diagnostic JS, Symbolic JS, and future evaluator parity.

Invalid future cases:

- implicit outer-local capture;
- implicit parameter or loop-variable capture;
- duplicate or unknown capture;
- shared mutable/by-reference capture requests;
- open generic callable;
- signature mismatch;
- invalid arity;
- callable equality;
- TSON or table serialization of callables;
- capture cycle;
- resource-limit overflow;
- unsupported `this`, methods, async, generators, optional/rest parameters, and host-callable leakage.

## Exclusions

M0a does not implement or authorize:

- production callable syntax;
- implicit closures;
- methods or `this`;
- overload sets;
- decorators;
- reflection or dynamic invocation;
- host-function leakage;
- callable equality/hashing;
- TSON/table callable serialization;
- TS-XML/TSX/React integration work;
- evaluator implementation;
- static-execution implementation.

## Owner decisions

M0a leaves only narrow owner choices:

1. Ratify `capture { ... } arrow` now, or defer exact capture syntax to `CTS-CALL-M1a`.
2. Decide whether `CTS-CALL-M0b` ships only noncapturing callable values or also capture syntax if owners want one larger atomic slice.
3. Confirm whether callable-typed interface fields are allowed in the first callable-storage slice or deferred.

## Recommended next milestone

Recommended next milestone: **`CTS-CALL-M0b` noncapturing callable values and callable types**.

Scope:

- callable type syntax;
- callable type symbol;
- named nongeneric function values;
- closed generic specialization values;
- noncapturing function expressions;
- callable construction and invoke MIR;
- C# and JavaScript backend support;
- diagnostics for open-generic values, non-callable invocation, signature mismatch, and unsupported contexts;
- no explicit capture yet unless owners choose the larger atomic slice.
