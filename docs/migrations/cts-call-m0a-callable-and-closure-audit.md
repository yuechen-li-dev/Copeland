# CTS-CALL-M0a callable and closure audit

**Status:** completed documentation-only audit. No production code, tests, fixtures, tooling, runtime behavior, packages, commits, or publishing changed.

## Initial repository state

- Revision: `9fb2029cb31642d06b285f622c1891006ed2bc0f`
- Branch: `main`
- Upstream: `origin/main`
- Ahead/behind: `0/0`
- Initial worktree status: clean (`git status --short --branch` reported only `## main...origin/main`)

No unrelated or user-owned modifications needed preservation because the worktree was clean at entry.

## Audit summary

The current Copeland TS implementation supports:

- named function declarations;
- direct named calls;
- explicit generic calls and bounded direct-argument inference;
- closed-specialization caching that materializes specialized named functions;
- MIR function definitions and direct named `MirCallExpression`;
- C# static function emission;
- JavaScript top-level function emission.

The current implementation does **not** support:

- first-class function values;
- function-type syntax or `FunctionTypeSymbol`;
- arrow/lambda/function-expression parsing;
- nested/local function declarations;
- capture syntax;
- closure environments;
- callable MIR nodes beyond direct named call;
- C# or JavaScript first-class invocation of runtime callable values.

## Classified findings

| Topic | Classification | Evidence | Finding |
| --- | --- | --- | --- |
| Function declaration syntax | implemented law | [`Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs), [`SyntaxNodes.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), [`ParserTests.cs`](../../tests/Copeland/Copeland.TS.Tests/ParserTests.cs) | `function name(params): returnType { ... }` is the implemented function surface. |
| Direct call syntax | implemented law | [`SyntaxNodes.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), [`Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs) | Calls are postfix `target(args)` only. |
| Explicit generic-call syntax | implemented law | [`GenericCallExpressionSyntax` in `SyntaxNodes.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), [`Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs), [`ParserTests.cs`](../../tests/Copeland/Copeland.TS.Tests/ParserTests.cs) | `name<Type>(args)` is implemented for named functions. |
| Function-type syntax | current rejection | [`ParseTypeSyntax` and `ParsePostfixTypeSyntax` in `Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs), [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs) | No function-type grammar or semantic type exists. |
| `FunctionTypeSymbol` | current rejection | [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs) | No such type exists. |
| Function symbol model | implemented law | [`Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) | `FunctionSymbol` carries name, parameters, return type, fallibility, and generic metadata only. |
| Overload assumptions | implemented law | [`Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs), [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | One function name resolves to one `FunctionSymbol`; no overload-set model exists. |
| Named function lookup for value expressions | current rejection | [`BindName` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Name binding returns variables, parameters, and table references only; functions are not values. |
| Direct named calls | implemented law | [`BindCall` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`BoundCallExpression` in `BoundNodes.cs`](../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) | Calls require a named `FunctionSymbol`. |
| Calling non-functions | implemented law | [`BindCall` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Non-function call targets emit `COPE-BIND-0006`. |
| Wrong arity/type at call sites | implemented law | [`BindCall` and `BindGenericCall` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`Language/Invalid/functions`](../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/functions) | Arity and argument-type checking are exact. |
| Arrow token availability | parser residue | [`SyntaxKind.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs), [`Lexer.cs`](../../src/Copeland/Copeland.TS/Syntax/Lexer.cs), [`SyntaxFacts.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxFacts.cs) | `=>` is tokenized even though callable arrows do not exist. |
| Arrow syntax use | implemented law for `match`, parser residue for callables | [`ParseMatchArm` in `Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs), `MatchArmSyntax` in [`SyntaxNodes.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs) | `=>` is only used for `match` arms today. |
| Arrow/lambda/function-expression parsing | current rejection | [`ParsePrimaryExpression` and `ParsePostfixExpression` in `Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs) | No parser path for arrow/lambda/function expressions exists. |
| Nested/local function declarations | current rejection | [`ParseStatement` in `Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs), [`BindStatement` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Statements do not include local functions. |
| Nested records/tables | parser residue plus current rejection | [`NestedRecordDeclarationStatementSyntax` and `NestedTableDeclarationStatementSyntax` in `SyntaxNodes.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), [`BindNestedRecord`/`BindNestedTable` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Nested declarations are parsed, then rejected. |
| Local scopes | implemented law | [`Scope` and block/for/match/try handlers in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Lexical scopes exist for ordinary bindings. |
| Mutable and immutable locals | implemented law | [`VariableSymbol` in `Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs), [`BindVariable` and `BindAssignment` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | `const` is read-only, `let` is mutable, `var` is profile-rejected. |
| Closure/capture lookup | current rejection | [`BindName` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | There is no callable-expression context that could capture outer lexical values. |
| Generic functions | implemented law | [`FunctionSymbol` in `Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs), [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`BinderTests.cs`](../../tests/Copeland/Copeland.TS.Tests/BinderTests.cs) | Named generics are bound once and specialized into closed bodies. |
| Explicit specialization and inference reuse | implemented law | [`GetOrCreateClosedInstantiation` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`BinderTests.cs`](../../tests/Copeland/Copeland.TS.Tests/BinderTests.cs) | Explicit and inferred calls reuse one closed specialization identity. |
| Open generic callable values | current rejection | [`BindName` and call-only generic paths in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) | Open generic functions are callable only through immediate call syntax. |
| Direct generic recursion restriction | implemented law | [`BindInferredGenericCall` and `BindGenericCall` in `Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), [`GenericDiagnosticInventoryTests.cs`](../../tests/Copeland/Copeland.TS.Tests/GenericDiagnosticInventoryTests.cs) | `COPE-GENERIC-0014` rejects generic recursion. |
| Generic-to-generic call restriction | implemented law | same files as above | `COPE-GENERIC-0006` rejects generic-to-generic calls. |
| Nongeneric direct recursion | unresolved owner decision | implementation audit found no dedicated rule or regression case | The current model likely permits named nongeneric recursion, but no explicit doctrine/test closes it out. |
| Bound callable nodes | current rejection | [`BoundNodes.cs`](../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) | Only `BoundFunctionDeclaration` and direct `BoundCallExpression` exist. |
| MIR function definitions and direct calls | implemented law | [`MirNodes.cs`](../../src/Copeland/Copeland.TS.Mir/MirNodes.cs), [`MirLowerer.cs`](../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) | MIR contains direct named functions and calls only. |
| MIR callable values/environments | current rejection | same files | No MIR callable carrier, invoke-by-value, or environment slot exists. |
| MIR locals/function identity | implemented law | [`MirFunction`, `MirLocal`, and `MirCallExpression` in `MirNodes.cs`](../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) | Function identity is currently the emitted name string. |
| MIR validation topology | implemented law | [`MirValidator.cs`](../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) | MIR validation assumes the current direct-call/function world. |
| C# function emission | implemented law | [`CSharpBackend.cs`](../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) | Functions emit as `public static` methods on `CopelandModule`. |
| C# direct call emission | implemented law | same file | `MirCallExpression` emits direct static calls. |
| JavaScript function emission | implemented law | [`JavaScriptBackend.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) | Functions emit as top-level generated `function` declarations. |
| JavaScript helper/scope/name allocation | implemented law | [`JavaScriptBackend.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs), [`JavaScriptEmissionModel.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptEmissionModel.cs) | Backend-private scope/name logic already exists and is suitable precedent for future callable helpers. |
| JavaScript direct call emission | implemented law | [`EmitCall` in `JavaScriptBackend.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) | Calls emit direct named invocation only. |
| Record/enum/Result/array interaction with callables | current rejection | [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs), [`TsonEncodeFeatureTests.cs`](../../tests/Copeland/Copeland.TS.Tests/TsonEncodeFeatureTests.cs) | No callable type exists, so those families cannot store callables. |
| Table and TSON interaction with callables | current rejection | [`TableFeatureTests.cs`](../../tests/Copeland/Copeland.TS.Tests/TableFeatureTests.cs), [`Tson*` tests under `tests/Copeland/Copeland.TS.Tests`](../../tests/Copeland/Copeland.TS.Tests) | Table/TSON storage is closed to executable forms already. |
| Callable equality | proof-era behavior / backend guardrail | [`JavaScriptBackendTests.cs`](../../tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptBackendTests.cs) | JS equality tests explicitly reject unsupported `Closure`/function families. No source callable equality law exists. |
| CLI/corpus/parity infrastructure | implemented law | [`CopelandCompiler.cs`](../../src/Copeland/Copeland.TS/Compiler/CopelandCompiler.cs), corpus and backend test projects | Existing lanes are ready for future callable corpus expansion. |
| Historical TypeScript-support callable proposal | historical proposal | [`docs/Copeland/architecture/copeland-typescript-support.md`](../Copeland/architecture/copeland-typescript-support.md) | Mentions arrow functions and function values as deferred delegate/lambda work. |
| Current language profile closure status | implemented documentation law | [`docs/Copeland/language/copeland-ts-language-profile.md`](../Copeland/language/copeland-ts-language-profile.md) | Closures/capture are currently unresolved, not implemented. |
| CTS-TYPE callable routing | historical proposal needing routing update | [`docs/Copeland/architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md`](../Copeland/architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md) | Placeholder `CTS-CALLABLE` owner needs concrete M0a routing. |
| TSX/TSPack/TS-XML | historical proposal / current rejection | [`docs/Copeland/architecture/copeland-ts-compiler-topology-jtf-m6c.md`](../Copeland/architecture/copeland-ts-compiler-topology-jtf-m6c.md), [`docs/Copeland/architecture/compiler-source-contract-jtf-m6b.md`](../Copeland/architecture/compiler-source-contract-jtf-m6b.md) | `.tsx` and TSPack are not implemented in this compiler. |
| React/MachinaLayout.JS references | historical proposal / reference snapshot | [`docs/Copeland/reference/machinalayout-js`](../Copeland/reference/machinalayout-js) | These are reference materials only and do not establish current Copeland callable law. |

## Repository-audit conclusions

1. The real implementation already has a stable distinction between named functions and closed generic specializations, which is the correct semantic base for future callable values.
2. The implementation has no partial function-value or closure machinery to preserve, so M0a is free to define one bounded callable model instead of reconciling multiple half-implemented ones.
3. `ArrowToken` is parser residue for callable work only in the narrow sense that the token exists; it is presently consumed only by `match` arms.
4. Backend-private JavaScript lexical scope tracking must not be mistaken for product closure semantics.
5. Existing generic specialization identity and caching are strong evidence that closed generic function values should reuse existing specialization artifacts rather than invent a second runtime generic mechanism.

## M0a recommendation

Adopt:

- first-class callables with no implicit lexical capture;
- familiar TypeScript-shaped arrow syntax for noncapturing function expressions and function types;
- explicit `capture { ... }` syntax for capturing expressions;
- immutable environment snapshots with exactly-once authored-order evaluation;
- a canonical callable runtime form `(code identity, immutable environment)`;
- staged implementation beginning with noncapturing callable values and closed named-function references.

Do not adopt:

- JavaScript shared mutable lexical-cell semantics;
- open generic callable values;
- methods/`this`/prototype/decorator/host-callable leakage;
- callable equality or serialization;
- any implementation that makes backend host closure behavior the source law.
