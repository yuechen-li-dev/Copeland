# CTS-TYPE-M0a user-authored type-system audit

## Result

CTS-TYPE-M0a establishes a documentation-only architecture for transparent aliases, erased field-requirement interfaces, and bounded generic functions. The canonical design is [Copeland TS user-authored type-system design](../Copeland/language/copeland-ts-type-system-design-cts-type-m0a.md).

No alias, interface, generic, static-evaluation, MIR, backend, TSON, CLR interop, class, object, package, fixture, corpus, project, compiler, or runtime behavior is implemented by this milestone.

## Starting state

| Item | Observed state |
| --- | --- |
| Revision | `529359672be531630ab77555a048b4e8d27bb3fc` |
| Branch | `main` |
| Upstream | `origin/main` |
| Divergence | 0 ahead, 0 behind |
| Worktree | Clean before CTS-TYPE-M0a documentation edits |

The initial revision already contained the CTS-CF-M0 documentation and validator portability closeout. No checkout, reset, restore, commit, push, publish, package change, or generated-artifact rewrite was performed. Because the observed worktree was clean, there were no uncommitted CTS-CF-M0 or validator-portability edits to preserve separately; their existing repository content was left untouched.

## Exact audited implementation surface

| Area | Paths and types inspected | Finding |
| --- | --- | --- |
| Tokens/lexer | `src/Copeland/Copeland.TS/Syntax/Lexer.cs`; `SyntaxKind.cs`; `SyntaxFacts.cs` | `<`/`>` comparison tokens exist. `type`, `interface`, `extends`, and `implements` are ordinary identifiers, not reserved/contextual language tokens. |
| Parser/types | `Syntax/Parser.cs`; `Syntax/SyntaxNodes.cs` `TypeSyntax` and all seven derived nodes | Current types are predefined, identifier, array, parenthesized, Result, `Table.Row`, and `column T`. No generic/type-authoring grammar exists. |
| Semantic types | `Semantics/Types.cs` `TypeSymbol`, primitive/array/Result/error-nominal/enum/record/table/row/column symbols, IDs, and `TypeFacts` | Records/tables/rows are nominal; arrays/Results/columns are structural; primitives are singleton values; recovery `error` is not authorable. |
| Signatures/members | `Semantics/Symbols.cs` | Named function signatures are not first-class types. Record fields, enum payload fields, table columns, and table-row fields carry ordered types. |
| Binding | `Semantics/Binder.cs` `Scope`, predeclaration passes, `BindType`, expected-type binding, calls, returns, equality, records, tables, TSON | One lexical symbol dictionary is used. Compatibility is exact equivalence plus recovery. Named declarations support forward references. No subtype, inference, interface, or generic scope exists. |
| Bound model | `Semantics/Bound/BoundNodes.cs` | Dedicated bound definitions/operations exist for current runtime families and TSON plans; no alias/interface/type-parameter/generic nodes exist. |
| Lowering/MIR | `Lowering/MirLowerer.cs`; `Copeland.TS.Mir/MirNodes.cs`; `MirTextWriter.cs` | MIR types are named, record, table, row, column, array, and Result. Functions carry typed signatures but are not type values. Lowering produces canonical concrete types. |
| MIR validation | `Copeland.TS.Mir/MirValidator.cs` | Shared validation covers arrays, records, tables, TSON plans, flow, and Result propagation before either backend. No open type-authoring form is accepted. |
| C# | `Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs` | `MapType` and emitted private carriers realize current canonical MIR. C# syntax/identity is proof-backend policy, not language law. |
| JavaScript | `Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs`; emission model/writers | Source types erase, while nominal/Result/table carriers and validators preserve required runtime law. Unsupported MIR types are rejected. |
| TSON | `Copeland.TS/Tson/TsonSchema.cs`, `TsonValues.cs`, `TsonDocument.cs`, reader/printer; TSON bound/MIR plans | TSON has primitive/object/record/enum/array/table schema/value families. It has no alias/interface/open-generic variant, and canonical runtime plans use concrete schemas. |
| Fixtures/corpus | `tests/Copeland/Copeland.TS.Tests/Language`; frontend corpus/focused tests; both backend corpora/runtime suites; shared malformed-MIR cases | 28 valid and 67 invalid language fixtures exist, with no alias/interface/generic acceptance. Generated artifacts remain outside `Language`. |
| History/doctrine | historical M1 profile, support matrix, CTS-REC, CTS-TSON, topology and MIR doctrine records | Direct C# interfaces/generics and class-oriented plans are historical proposals. Current record/TSON/backend-neutral boundaries are compatible authority. |
| Topology | `tools/Validate-CopelandTsTopology.ps1`; `tools/Validate-DependencyBoundaries.ps1`; compiler topology record | MIR is BCL-only and backend-neutral; frontend/backends depend on MIR, not one another; compiler-host TSON/assets cannot leak into backends. |

## Accepted recommendations

- `type Name = T;` is a module-scoped, non-generic, transitively transparent compile-time alias in M0b. Same-unit forward references are allowed; duplicates and expansion cycles are rejected; only canonical expanded types reach MIR.
- `interface` initially means an erased set of exact readable-field requirements. Nominal records and table rows may satisfy it implicitly with extra fields allowed. It is constraint-only and creates no runtime value/carrier, mutation permission, declaration merging, or inheritance graph.
- Generic named functions are first. M2b requires explicit closed type arguments; M2c adds bounded direct-argument inference. Generic records/aliases, defaults, variance, higher-kinded types, and expansive generic recursion are outside the first slice.
- Generic bodies are checked against requirements. Reachable closed instantiations are the recommended first canonical-MIR strategy; C#, JavaScript, and NativeAOT may privately share, erase, preserve, or specialize equivalent code.
- TSON continues to require concrete nominal schema identity. Aliases are transparent; interfaces and open generics are never canonical TSON nodes.
- `static if`, `static match`, and `static for` belong only to a separately approved CTS-STATIC ladder after type-system closeout.

## Rejected, replaced, and deferred TypeScript families

Rejected by initial doctrine: `any`, unchecked assertions, bivariant parameters, declaration/namespace merging, conditional types, `infer`, mapped types, arbitrary type-level execution, runtime interface imitation, and higher-kinded parameters.

Replaced in the initial model: arbitrary unions by payload enums/Result/Option-style enums; intersections by explicit requirement lists; mutable anonymous DTO shapes by nominal records; untyped transport objects by TSON; many parametric `unknown` uses by generics.

Deferred pending evidence or another ladder: `keyof`, indexed access, type-query `typeof`, template-literal types, ambient declarations, default type arguments, generic records/aliases, interface methods/composition/storage values, `implements`, classes, CLR imports/metadata, and bounded static execution.

## Recommended ladder

1. CTS-TYPE-M0b: transparent non-generic aliases.
2. CTS-TYPE-M1a: confirm interface/requirement grammar and member evidence.
3. CTS-TYPE-M1b: field-only structural requirements and satisfaction.
4. CTS-TYPE-M2a: generic-function MIR/backend/limit design.
5. CTS-TYPE-M2b: explicit-argument bounded generic functions.
6. CTS-TYPE-M2c: predictable inference and cross-backend closeout.
7. CTS-TYPE-M3: doctrine, adversarial parity, diagnostics, and exclusions closeout.
8. Separate future CTS-STATIC-M0a.

## Owner approvals still required

- Alias namespace/scope and later interface-alias positions.
- Multiple-requirement spelling and table-row satisfaction.
- Continued deferral of `implements`, interface composition/methods/storage values.
- Closed-instantiation MIR baseline, stable identity recipe, and measured resource limits.
- Same-instantiation generic runtime recursion and the exact M2c inference boundary.
- CLR import identity/constraint translation and any nominal CLR-interface bridge.
- Separate authorization for CTS-STATIC.

## Validation contract

The final CTS-TYPE-M0a diff must remain limited to Markdown under `docs/`. Required checks are:

```powershell
tools/Validate-CopelandTsTopology.ps1
tools/Validate-DependencyBoundaries.ps1
git diff --check
```

Markdown link targets, headings, tables, fences, UTF-8 encoding, BOMs, and trailing whitespace are checked separately. Full builds/tests are intentionally not required because M0a changes no production, test, fixture, corpus, project, package, or tooling file.
