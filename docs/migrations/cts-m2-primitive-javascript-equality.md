# CTS-M2: primitive JavaScript equality

## Outcome

CTS-M2 implements primitive Copeland equality in `Copeland.TS.Backend.JavaScript`. Validated `boolean`, binary64 `number`, and `string` `==`/`!=` expressions emit JavaScript `===`/`!==`. Generated output never uses loose equality. Source `===`/`!==` is still rejected; generated strict operators do not expand the Copeland source language.

## MIR decision

The pre-change audit found sufficient existing data: `MirBinaryExpression` retains canonical operator text, typed ordered operands, and a typed result. `MirExpression.Type` carries the bound type for literals, variables, calls, and binary expressions. Lowering already populates those types, and the M0d binder rejects source strict spelling before MIR. Therefore CTS-M2 makes no MIR change and no C# backend change; accepted `.cope` and `.g.cs` artifacts remain stable.

## Changed files

- `src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs`: typed equality validation and strict-operator mapping.
- `src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptLiteralWriter.cs`: deterministic code-unit-preserving JavaScript string encoder.
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/`: primitive/unsupported equality validation, corpus determinism and hash, Node execution, and corpus fixtures.
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/primitive-equality.ts` and `.g.js`: Boolean and binary64 equality artifact.
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/string-equality.ts` and `.g.js`: string equality and quote/backslash artifact.
- `tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs`: built-CLI equality emission and Node execution.
- `docs/Copeland/language/copeland-ts-language-profile.md` and this migration/architecture record.

## Proof scope

The Node proof covers Boolean equality/inequality, ordinary numeric equality/inequality, `0 / 0` NaN equality and inequality, a binary-arithmetic negative-zero construction, and string equality/inequality. The corpus string artifact exercises quote and backslash escaping; the direct encoder test additionally covers newline, carriage return, tab, controls, line separators, and surrogate code units. Unsupported arrays, enum names, Result-family names, object/class-family names, closures/functions, and unknown future names are diagnostic-only and produce no artifact.

`primitive-equality.g.js` has SHA-256 `AD297686E173C5A30FD9D6CFA030F90DC048D604CFB7808063DED441EC74B5FC`.

## Validation record

Validation completed on 2026-07-13 using Node.js v26.2.0:

| Command or check | Result |
| --- | --- |
| Focused JavaScript backend tests | Passed: 28 tests in 0.21 s. Includes exact corpus bytes, repeated emission, stable hash, strict-token scan, diagnostics, and repeated Node execution. |
| Focused CLI tests | Passed: 13 tests in 1 s, including built-CLI JavaScript equality emission and Node execution. |
| `dotnet build Copeland.TS.slnx` | Passed in 1.02 s. |
| `dotnet test Copeland.TS.slnx --no-build` | Passed: 203 tests in 3.6 s (132 frontend, 43 C# backend, 28 JavaScript backend). Existing MIR `.cope` and C# `.g.cs` comparisons passed unchanged. |
| `dotnet build Copeland.slnx` | Passed in 1.69 s. |
| `dotnet test Copeland.slnx --no-build` | Passed: 298 tests in 3.3 s. |
| `dotnet build JointTaskForce.slnx` | Passed in 5.08 s. |
| `dotnet test JointTaskForce.slnx --no-build` | Passed: 1,551 tests in 18.4 s. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed in 6.1 s: solution/project paths, graph-cycle checks, backend isolation, fixture ownership, and topology. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed in 3.3 s for 27 production projects. |
| Emitted loose-equality scan | Passed: bounded `==`/`!=` regular expressions find no loose operator in the JavaScript corpus; the test uses the same check against emitted source. |
| `git diff --check` | Passed. |

The Machina, Aurelian, integration, and slow lanes are not separately selected because production changes stay within Copeland TS and its tests. The required `JointTaskForce.slnx` pass nevertheless exercised the changed project graph and its available cross-subsystem suite.

## Follow-up

The recommended next CTS milestone is payload-enum representation plus exhaustive-match emission, explicitly excluding payload structural equality until its nominal/recursive law is separately implemented and tested.
