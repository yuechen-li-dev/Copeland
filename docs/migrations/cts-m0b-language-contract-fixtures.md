# CTS-M0b: Copeland TS language-contract fixtures

## Result

CTS-M0b adds a curated, filesystem-backed language-law suite under:

```text
tests/Copeland/Copeland.TS.Tests/Language/
  Valid/
    conditions/
    declarations/
    functions/
    arrays/
    fallibility/
    tagged-data/
  Invalid/
    conditions/
    declarations/
    dynamic-types/
    absence/
    coercions/
    functions/
    fallibility/
    tagged-data/
```

The suite contains 8 valid `*.cl-valid.ts` fixtures and 12 invalid `*.cl-invalid.ts` fixtures. `Language` contains source contracts only: no `.cope`, `.g.cs`, `.g.js`, diagnostic snapshot, or runtime-output artifact is permitted.

`TestData/Corpus` remains the separate home for compiler regression, recovery, parser/binder/MIR snapshots, generated C# evidence, and runtime-oriented evidence. Nothing was moved: the corpus sources retain their mixed assertion and generated-artifact coverage, while the new fixtures are purpose-written, readable source laws.

## Discovery and execution

`LanguageFixtures` is local test infrastructure in `Copeland.TS.Tests`. The project copies `Language/**/*` to test output. The helper resolves its root from `AppContext.BaseDirectory`, not the process working directory; validates the complete tree; requires both valid and invalid fixtures; and enumerates normalized relative paths in ordinal order.

Ordinary xUnit `[Theory]` and `[MemberData]` pass each normalized relative path to the test. There is no test DSL, source generator, custom runner, subprocess, JavaScript backend dependency, or C# proof-backend execution.

- Valid fixtures call `CopelandCompiler.CompileToMir`, require no diagnostics, and require usable bound and Cope MIR results.
- Invalid fixtures call the same facade, require diagnostics and a bound compilation, reject lexer/parser diagnostics as evidence, and require both MIR compilation and MIR text to be absent.

This establishes normal frontend validation rejection rather than a crash, parser accident, backend failure, or snapshot mismatch.

## Fixture inventory

| Law | Acceptance fixtures | Rejection fixtures |
| --- | --- | --- |
| CL-FLOW-001 | `Valid/conditions/boolean-condition.cl-valid.ts` | `Invalid/conditions/number-truthiness.cl-invalid.ts` |
| CL-TYPE-001 | `Valid/declarations/typed-let-const.cl-valid.ts` | `Invalid/declarations/missing-annotation.cl-invalid.ts` |
| CL-TYPE-002 | `Valid/declarations/same-kind-operators.cl-valid.ts` | `Invalid/coercions/mixed-plus.cl-invalid.ts` |
| CL-TYPE-003 | — | `Invalid/dynamic-types/any-annotation.cl-invalid.ts` |
| CL-NULL-001 | — | `Invalid/absence/null-literal.cl-invalid.ts` |
| CL-NULL-002 | — | `Invalid/absence/undefined-value.cl-invalid.ts` |
| CL-ARRAY-001 | `Valid/arrays/homogeneous-array.cl-valid.ts` | — |
| CL-CALL-001 | `Valid/functions/typed-named-call.cl-valid.ts` | `Invalid/functions/wrong-arity.cl-invalid.ts`, `Invalid/functions/wrong-argument-type.cl-invalid.ts` |
| CL-FAIL-001 | `Valid/fallibility/fallible-propagation.cl-valid.ts` | `Invalid/fallibility/unhandled-fallible-call.cl-invalid.ts`, `Invalid/fallibility/wrong-error-propagation.cl-invalid.ts` |
| CL-ENUM-001 | `Valid/tagged-data/payload-enum-construction.cl-valid.ts`, `Valid/tagged-data/payload-enum-match.cl-valid.ts` | `Invalid/tagged-data/nonexhaustive-match.cl-invalid.ts`, `Invalid/tagged-data/payload-pattern-arity.cl-invalid.ts` |

## Corpus audit

The initial fixture syntax was audited against these existing corpus areas before authoring new sources:

- `m0-bind-valid`, `m0-bind-invalid`, `m0-mir-valid`, and `m0-mir-invalid` for declarations, fallibility, null, and MIR gating.
- `m0-csharp-valid` for existing proof-backend source coverage that must remain outside `Language` with its `.g.cs` evidence.
- `m1-enum-bind-valid`, `m1-enum-bind-invalid`, `m1-match-bind-valid`, `m1-enum-match-mir-valid`, and `m1-enum-match-csharp-valid` for payload enums and exhaustive matches.
- lexical and parser recovery corpus directories were deliberately retained as regression evidence and not reclassified as language-law fixtures.

No existing source was moved or deleted.

## Honest gaps and profile corrections

- `var` has no CTS-M0b fixture. Although `var` is tokenized, the current parser does not parse it as a variable declaration, so its rejection is parser recovery. The new runner correctly refuses to treat that as validation evidence. CTS-M0c should first route `var` declarations to the binder’s existing profile diagnostic, then add `Invalid/declarations/var-declaration.cl-invalid.ts`.
- `any` and `undefined` fixtures prove current rejection through unknown-type/name diagnostics. Dedicated product-profile diagnostics remain a later improvement; fixture existence does not claim that diagnostic design is settled.
- The canonical profile now separates resolved `null` exclusion (must reject; no ordinary JavaScript `null` lowering) from unresolved explicit optionality representation (no JavaScript representation is authorized).

CTS-M0c is recommended as the semantic-decision milestone for backend-blocking unresolved laws: numbers, equality, evaluation order, explicit optionality representation, and JavaScript representations for fallibility and payload enums.

## Files changed

- `tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj`
- `tests/Copeland/Copeland.TS.Tests/LanguageFixtures.cs`
- `tests/Copeland/Copeland.TS.Tests/LanguageFixtureTests.cs`
- `tests/Copeland/Copeland.TS.Tests/Language/**`
- `tools/Validate-CopelandTsTopology.ps1`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/migrations/cts-m0b-language-contract-fixtures.md`

## Validation

Focused language-fixture lane:

```powershell
dotnet test tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj --no-restore --filter FullyQualifiedName~LanguageFixtureTests
```

Result: 21 passed (8 valid, 12 invalid, 1 topology), 0.556 seconds test time.

Full validation:

```powershell
dotnet build Copeland.TS.slnx
dotnet test Copeland.TS.slnx --no-build

dotnet build Copeland.slnx
dotnet test Copeland.slnx --no-build

dotnet build JointTaskForce.slnx
dotnet test JointTaskForce.slnx --no-build

pwsh -File tools/Validate-DependencyBoundaries.ps1
pwsh -File tools/Validate-CopelandTsTopology.ps1
git diff --check
```

All commands passed. Build elapsed times were 1.30 seconds for `Copeland.TS.slnx`, 3.03 seconds for `Copeland.slnx`, and 8.08 seconds for `JointTaskForce.slnx`; the JointTaskForce test command completed in 17.5 seconds. The focused Copeland TS test lane reported 111 passing frontend tests and 43 passing C# proof-backend tests; the focused language-fixture subset was 21 passing tests in 0.556 seconds.

Also passed: solution/project-path and graph-cycle checks through the topology validator; dependency-boundary validation for 26 production projects; changed-document local link/path validation; language-fixture naming/content validation (8 valid, 12 invalid); an active-source/test stale Cope Test convention search; and `git diff --check`.

Machina UI slow and integration-specific lanes were not run because this change is confined to Copeland TS tests, Copeland documentation, and the Copeland TS topology validator; no shared production, Machina, or Aurelian implementation changed.
