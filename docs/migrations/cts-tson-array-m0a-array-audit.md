# CTS-TSON-ARRAY-M0a Copeland array integration audit

**Status:** documentation-only architecture milestone. The repository was inspected at `debd5f7f78f5e3f32929b1c2727af4000b1b5001` on `main`; `origin/main` was the same revision and the starting worktree was clean.

## Outcome

The audited safe direction is one immutable compiler-host `TsonArray` plus one structural array schema, reusing ordinary Copeland `T[]` and `[...]`. The full decision is in [the ARRAY-M0a design](../Copeland/language/copeland-ts-tson-arrays-design-cts-tson-array-m0a.md). No source behavior changed.

## Audited paths and conclusions

| Concern | Exact path/type | Conclusion |
| --- | --- | --- |
| Syntax and parser | `src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs`: `ArrayTypeSyntax`, `ArrayLiteralExpressionSyntax`; `Syntax/Parser.cs`: `ParsePostfixTypeSyntax`, `ParseArrayLiteralExpression` | Existing grammar owns `T[]` and `[...]`, including trailing commas. |
| Binder and types | `Semantics/Types.cs`: `ArrayTypeSymbol`, `TypeFacts.AreEquivalent`; `Semantics/Binder.cs`: `BindArray` | Homogeneous inference; expected element context; `COPE-TYPE-0010` rejects empty arrays without evidence. |
| Array operations | `Syntax/IndexExpressionSyntax`; `Binder.BindIndex`, `Binder.BindAssignment` | Indexing is table/column-only; no array index, bounds, mutation, or `length` semantics are implemented. |
| Bound/MIR | `Semantics/Bound/BoundNodes.cs`: `BoundArrayExpression`; `Lowering/MirLowerer.cs`; `Copeland.TS.Mir/MirNodes.cs`: `MirArrayExpression`, `MirArrayType`; `MirValidator.cs`; `MirTextWriter.cs` | Existing typed array construction and validation are reusable for assets. |
| C# | `Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs` | Emits ordinary mutable `T[]`; nested type mapping is recursive. |
| JavaScript | `Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs` | Explicitly rejects `MirArrayExpression` and `MirArrayType`; no current JavaScript array realization exists. |
| Runtime evidence | `tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/M0hRuntimeTests.cs`: `Executes_Array_Literal_Return`, `Executes_Enum_Array_Return` | C# primitive and enum arrays execute. |
| Corpus/fixtures | `tests/Copeland/Copeland.TS.Tests/Language/Valid/arrays/homogeneous-array.cl-valid.ts`; `TestData/Corpus/m0-csharp-valid/array_literal.*`; `m1-enum-match-*/enum_array_type.*` | Existing evidence is not a TSON array contract. |
| TSON model | `Copeland.TS/Tson/TsonValues.cs`, `TsonSchema.cs`, `TsonDocumentReader.cs`, `TsonCanonicalPrinter.cs`, `TsonDocument.cs` | Core has six variants and already owns defensive copying, parser restriction, cycles, canonical LF text, and limits. |
| Asset route | `Semantics/Binder.cs`: `BindTsonAsset`, `ValidateTsonSchemaType`, `TsonTypeMatches`, `TryLowerTsonValue` | Current root/value validators exclude arrays; recursive expected-array validation/lowering is the narrow extension. |
| Encoding route | `Copeland.TS.Mir/MirNodes.cs`: `MirTsonEncodingPlan`; `MirValidator.cs`; `MirTsonCanonicalText.cs`; C#/JS backend TSON writers | Existing plan is nominal-root primitive/record/enum only; add an array value-plan in ARRAY-M1, after assets. |
| Dependency enforcement | `tools/Validate-CopelandTsTopology.ps1`, `tools/Validate-DependencyBoundaries.ps1` | Existing namespace/project placement is appropriate. |

## Implemented versus proposed

Implemented: parser array syntax, structural homogeneous typing, contextual empty arrays, ordinary bound/MIR arrays, `.cope` rendering, C# array emission, and C# primitive/enum runtime coverage. The C# carrier is mutable. JavaScript ordinary arrays, source array indexing/mutation/bounds, TSON arrays, TSON array schemas, asset-array lowering, and encoding-array plans are not implemented.

Historical TSON documents correctly exclude arrays through M2c. This milestone proposes no change to those historical core claims. The genuine integration conflict is only JavaScript: an asset slice that promises both backends must include a minimal ordinary JavaScript array emission path; it must not conceal that gap with a TSON-private carrier.

## Recommended progression

Implement exactly ARRAY-M0b as specified in [the design's bounded recommendation](../Copeland/language/copeland-ts-tson-arrays-design-cts-tson-array-m0a.md#exact-cts-tson-array-m0b-recommendation): semantic value/schema, reader/printer, fixtures, nominal-root nested asset lowering, and the minimum ordinary JavaScript array realization for asset parity. Keep runtime array encoding, JSON, tables, decoding, Results-as-data, aliases/interfaces, and root arrays out of that slice.
