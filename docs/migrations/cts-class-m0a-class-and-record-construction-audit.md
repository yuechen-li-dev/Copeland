# CTS-CLASS-M0a: class and record construction audit

**Audit revision:** `a21a7783f74faba91c6b9846eaec0030b0a7e3d1` on `main`, tracking `origin/main`; ahead 0, behind 0; initial worktree clean. This is a historical pre-M1 audit. Its selected semantic direction is implemented by [CTS-CLASS-M1](cts-class-m1-pure-classes-and-associated-functions.md).

## Evidence classification

| Area | Finding and exact evidence | Classification |
| --- | --- | --- |
| Lexer and parser | [`SyntaxKind.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs), [`Lexer.cs`](../../src/Copeland/Copeland.TS/Syntax/Lexer.cs), and [`Parser.cs`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs) have no class/member/constructor/new/this/static/visibility tokens or grammar. Current declarations are records, functions, aliases/interfaces/enums/tables; expressions include calls, resolved member access, contextual braces, and `with`. | implemented law / parser residue absent |
| Records | [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs) defines `RecordTypeId`, declaration-ordinal `RecordFieldId`, and `RecordTypeSymbol`; [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) predeclares records, binds contextual construction, field access, `with`, and containment cycles. | implemented law |
| Bound/MIR | [`BoundNodes.cs`](../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) and [`MirNodes.cs`](../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) contain record construction/access/update and ordinary function/callable nodes. [`MirLowerer.cs`](../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) lowers those directly. No class node exists. | implemented law |
| Type algebra | [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs) and [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) implement records, enums, Results, arrays, tables, callable types, aliases/interfaces, and bounded closed generic functions. Nominal types are non-equivalent by shape. | implemented law |
| Functions and lookup | [`Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) has `FunctionSymbol`; [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs) has global value/type maps, direct names, member access, function references, generic references, and explicit captures. There is no associated-member namespace or visibility/accessibility symbol. | implemented law / M0a recommendation |
| C# | [`CSharpBackend.cs`](../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) emits records as sealed complete carriers and ordinary functions. This is record proof/backend behavior, not source class law. | proof-era behavior |
| JavaScript | [`JavaScriptBackend.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) `EmitRecordRuntime` emits private symbols, `WeakSet` provenance, null-prototype frozen values, field validators, and compiler-owned construction; ordinary function/callable emission is separate. | implemented law |
| TSON/tables | [`TsonSchema.cs`](../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs), [`TsonValues.cs`](../../src/Copeland/Copeland.TS/Tson/TsonValues.cs), and Binder TSON/table eligibility code enumerate current closed families and have no class variant. | implemented law |
| CLI/corpus/parity | [`CopelandCompiler.cs`](../../src/Copeland/Copeland.TS/Compiler/CopelandCompiler.cs), [`Copeland.Cli`](../../src/Copeland/Copeland.Cli), and `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/cts-call-m1` show the current frontend/MIR/C#/Diagnostic-JS/Symbolic-JS corpus route. | implemented law |
| Validators | [`Validate-CopelandTsTopology.ps1`](../../tools/Validate-CopelandTsTopology.ps1) verifies the isolated MIR/frontend/backend/CLI graph. [`Validate-DependencyBoundaries.ps1`](../../tools/Validate-DependencyBoundaries.ps1) audits project and textual boundaries. | implemented law |
| Historical claims | [`copeland-typescript-support.md`](../Copeland/architecture/copeland-typescript-support.md) still says classes/constructors/`this` are planned and describes a strict C# subset. It predates record/callable/type closeout. | historical proposal |
| Current rejection | [`copeland-ts-language-profile.md`](../Copeland/language/copeland-ts-language-profile.md) has CL-OBJECT-003 as intended/unimplemented; `new`, dynamic object semantics, prototype behavior, and unsupported member shapes have no accepted class path. | current rejection |

## Audit conclusion

The smallest credible M1 is not a JavaScript/CLR class implementation. It is a frontend class declaration that owns an existing immutable nominal record and ordinary functions, with frontend-only construction/update/privacy authority. Existing record MIR and private backend carriers are sufficient after that authority is checked. No parser residue needs preservation, and no historical `this`/inheritance claim is a current law.

The full accepted policy, diagnostics, limits, backend direction, and M1 evidence plan are in the [pure classes design](../Copeland/language/copeland-ts-pure-classes-design-cts-class-m0a.md).
