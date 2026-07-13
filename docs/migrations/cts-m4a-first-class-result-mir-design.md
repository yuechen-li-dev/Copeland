# CTS-M4a first-class Result MIR design

**Status:** documentation-only design milestone. No compiler, test, fixture, project, solution, tooling, or runtime file is changed by this record.

## Outcome

CTS-M4a records the accepted first-class Result laws and the smallest backend-neutral Cope MIR direction needed to implement them. The detailed design is [CTS-M4a: First-class Result and fallibility MIR design](../Copeland/language/copeland-ts-first-class-result-design-cts-m4a.md).

The accepted language law remains `Result<T,E> = ok(T) | err(E)`, with `T ! E` as Copeland source spelling. A fallible call must become a Result value, not a success-typed call annotated with fallibility metadata. This does not claim that `T ! E` value annotations, `ok`, `err`, Result matching, postfix unwrap, or `try`/`except` parse or run today.

## Required implementation migration inventory

The eventual atomic M4b migration affects these current production shapes:

| Layer | Current production type or member | Required destination |
| --- | --- | --- |
| syntax | `SyntaxKind.BangToken`, `QuestionToken`, `PropagateExpression`; `FunctionDeclarationSyntax.ErrorTypeBangToken`/`ErrorType`; `TypeSyntax` variants | Result/parenthesized type syntax and Result constructors/match patterns, while retaining existing signature spelling and `?`. |
| binding | `FunctionSymbol(ReturnType, ErrorType)`, `BoundExpression.ErrorType`, `BoundCallExpression`, `BoundPropagateExpression` | Complete structural Result type on values; propagation target on a Result expression. |
| lowering | `MirLowerer.LowerFunction`, `LowerExpression`, special propagated-call case | Dedicated Result type/operations; no call-only propagation. |
| MIR | `MirType(string)`, `MirFunction.ReturnType/ErrorType/IsFallible`, `MirCallExpression(... IsFallible, ErrorType, IsPropagated)` | Structured Result type, unified function return type, Result constructors/match/propagate nodes. |
| textual MIR | `MirTextWriter` function and `call? ... propagate` formatting | Type formatting and dedicated Result operation formatting. |
| C# proof backend | `CSharpBackend.FunctionReturnType`, return wrapping, `EmitPropagation`, generated `CopeResult<TValue,TError>` | Consume Result MIR directly; keep any .NET physical representation private. |
| JavaScript backend | `JavaScriptBackend.ValidateProgram`, `ValidateCall`, unsupported fallible-MIR diagnostics | Continue explicit rejection until a full Result subset is emitted, then implement only dedicated Result operations. |
| CLI | `Copeland.Cli` backend composition and emit paths | Preserve diagnostic-gated backend behavior; no source-syntax compatibility mode. |

The current direct references are confined to the Copeland frontend, Cope MIR project, C# and JavaScript backends, CLI composition, and their tests. No external repository consumer was found by searching the working tree for `MirFunction`, `MirCallExpression`, `IsFallible`, `ErrorType`, and `IsPropagated`. A proof-era public-shape adapter is therefore not justified.

## Tests and artifacts that must migrate

The precise current fallibility evidence includes:

- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-mir-valid/fallible_signature.ts` and `.cope`;
- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-mir-valid/propagation.ts` and `.cope`;
- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-bind-valid/fallible_propagation.ts`;
- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-bind-invalid/question_nonfallible_function.ts` and `question_error_mismatch.ts`;
- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-mir-invalid/nested_unhandled_fallible.ts`;
- `tests/Copeland/Copeland.TS.Tests/Language/Valid/fallibility/fallible-propagation.cl-valid.ts` and `Language/Invalid/fallibility/unhandled-fallible-call.cl-invalid.ts` / `wrong-error-propagation.cl-invalid.ts`;
- `tests/Copeland/Copeland.TS.Tests/BinderTests.cs`, `MirEvaluationOrderTests.cs`, parser/lexer/corpus/facade tests;
- `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-csharp-valid/fallible_signature.g.cs`, `propagation.g.cs`, and `void_fallible.g.cs`;
- `tests/Copeland/Copeland.TS.Backend.CSharp.Tests/CSharpBackendTests.cs`, `CSharpCorpusTests.cs`, and `Runtime/M0hRuntimeTests.cs` including its Result reflection assertions;
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptBackendTests.cs`, which currently expects rejection for fallible functions/calls/propagation, plus JavaScript corpus/runtime and CLI integration tests that exercise backend diagnostics;
- all direct `MirFunction` and `MirCallExpression` constructions in frontend/backend test code and their expected `.cope`/`.g.cs` artifacts.

Payload enum evidence also constrains the migration: `BoundMatchExpression`, `MirMatchExpression`, `CSharpBackend.EmitMatchExpression`, `JavaScriptBackend.ValidateMatch`, and CTS-M3 enum/match corpus fixtures demonstrate ordered payloads and exhaustive selection. They must continue to represent only nominal user enums; Result tests must use dedicated Result nodes and no synthetic `MirEnum`.

## Atomicity and retirement

M4b must change signature typing, call typing, and existing `?` together. A fallible call changing from `T` to `T ! E` while retaining the old special propagated-call MIR would make existing valid source incorrectly typed. Compatibility is source-level—the existing signature spelling and postfix `?` remain—not a prolonged dual semantic model.

`IsPropagated` retires in the same M4b change that introduces `MirPropagateExpression`. `IsFallible` and `ErrorType` retire as independent authority at the same time; a convenience query may derive fallibility from a unified Result return type, but cannot be stored as competing state.

## Validation performed for CTS-M4a

This milestone is documentation-only, so builds/tests are not required. Validation must instead cover the documentation and repository boundary:

1. resolve changed Markdown relative links and paths;
2. check Markdown tables have a consistent column count;
3. check fenced-code delimiters balance;
4. run `tools/Validate-CopelandTsTopology.ps1` to retain project/dependency boundaries;
5. run `git diff --check`;
6. confirm the CTS-M4a diff changes only the two documents and the narrow canonical-profile link/status note, and changes no production, test, fixture, project, solution, or tooling file.

## Deferred implementation sequence

M4b is limited to first-class Result values, explicit construction/match, function-return propagation, and C# proof parity. CTS-M5 later supplies the defined postfix-unwrap panic boundary. Paired expression-shaped `try`/`except` is now designed by [CTS-M6a](../Copeland/language/copeland-ts-try-except-design-cts-m6a.md) and still requires lexical handler targets plus structured Result handler MIR. Neither should be smuggled into backend exception handling.
