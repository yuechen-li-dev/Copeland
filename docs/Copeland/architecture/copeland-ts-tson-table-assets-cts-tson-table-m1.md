# Copeland TS declaration-owned TSON table assets (CTS-TSON-TABLE-M1)

**Status:** implemented and closed for compile-time ingestion. Runtime table encoding remains CTS-TSON-TABLE-M2.

## Accepted source contract

The production parser accepts exactly the declaration-owned form:

```ts
const $schema: string = "copeland://example/data";

record table Samples from tsonAsset("./samples.tson") {
    active: boolean;
    score: number;
}
```

`from` is contextual after `record table Name`; it remains an ordinary identifier everywhere else. `TableAssetClauseSyntax` retains the `from` token and the complete call, including the literal path span. Each `TableColumnSyntax` retains its name, explicit type, terminator, and whether inline data was authored.

The declaration creates the same one nominal table type, derived nominal row type, column views, and authored singleton as an inline declaration. The asset initializes that singleton. Expression-valued `tsonAsset` remains limited to the earlier record/enum contract and cannot construct a second table value.

Asset-backed columns require explicit types and no value arrays. Inline declarations retain their prior inferred/explicit column syntax and behavior. Zero-column declarations, inline data plus an asset clause, nonliteral or extra arguments, a different call target, and nested declaration placement are rejected through existing parser, table, and `COPE-TSON-ASSET` ownership.

## Compiler-host path

```text
TableDeclarationSyntax plus TableAssetClauseSyntax
    -> Binder declaration ownership
    -> CopelandAssetResolver / ICopelandAssetSource
    -> TsonDocumentReader (ObjectTypeScript or CanonicalTson)
    -> validated TsonTable
    -> exact source-schema and stable-identity validation
    -> BoundTableDefinition with closed constants
    -> MirTableDefinition with closed constants
    -> existing C# and JavaScript table realization
```

Resolution retains CTS-TSON-M1b policy: paths are relative to the primary source, normalized and root-confined, limited to `.obj.ts` and `.tson`, cached by normalized full path, and recorded as root-relative `/` paths plus lowercase SHA-256 of authored UTF-8 content. Comments therefore affect dependency evidence while disappearing from semantic projection, MIR, and generated code.

The CLI supplies the filesystem implementation only. Binder/lowering do not access the filesystem directly, and neither backend references TSON or asset abstractions.

## Source schema authority and identity

The compilation-unit `$schema` declaration is required and remains compiler metadata. For table `Samples`, the asset must have identity `schema#Samples`; column `score` must have identity `schema#Samples.score`. Columns match exactly by count, declaration order, name, identity, and recursively structural element schema.

Every reachable record or payload enum is resolved through existing same-unit compiler symbols. Existing schema validation compares record identity and ordered fields, enum identity and ordered cases/payloads, and every child type. The compiler synthesizes no source types from the asset catalog. Stable TSON identities validate the boundary only; normal source order still assigns `tN`, `tN.row`, and `tN.cM`.

The M0b reader owns table-root, rectangularity, cell-family, binary64, Unicode, canonicality, and resource validation. Its `COPE-TSON-TABLE-0001` through `0005` diagnostics retain asset-local normalized provenance. Compiler boundary mismatches use the established `COPE-TSON-ASSET-0003` and source declaration spans, without host exception text or absolute paths.

## Closed array-valued table constants

M1 adds only:

- `BoundTableArrayConstant(ArrayTypeSymbol, IReadOnlyList<BoundTableConstant>)`;
- `MirTableArrayConstant(MirArrayType, IReadOnlyList<MirTableConstant>)`.

Both defensively copy their element sequence. Elements are recursively closed table constants; no executable bound/MIR expression is retained. Empty arrays keep their explicit element type, nested arrays preserve order, and backend realization creates a typed CLR array or a frozen JavaScript array value. This family is distinct from compiler-host `TsonArray` and runtime-encoding `MirTsonArrayPlan`.

Shared MIR validation checks the array type, homogeneous child types, closed child families, maximum length 100,000, nesting depth 64, node count 100,000 per constant tree, and repeated-reference alias/cycle attempts before either backend emits. Existing table identity, rectangularity, row count, and access validation remains shared.

## Erasure and backend reuse

MIR contains only ordinary compiler-local table definitions and closed constants. It contains no asset path, comments, `$schema`, `tsonAsset`, TSON value/catalog/schema node, source profile, or filesystem detail. Equivalent `.obj.ts` and `.tson` assets produce identical MIR; comment-only authoring changes alter dependency SHA-256 but not MIR or runtime value.

C# uses the established private typed per-column arrays and one private static table singleton. JavaScript uses the established private frozen column arrays, carriers, tokens, and one singleton. Array-valued cells require only closed-constant emission. A general JavaScript Result payload-validation omission for `MirArrayType`, exposed by `Result<T[], TableBoundsError>`, was corrected in the existing shared backend type-condition path.

Runtime proofs cover row/column/cell access, bounds Results, nested and empty arrays, nominal record fields, enum payloads, negative-zero bits, Unicode strings, and singleton identity. Publication still occurs only after complete table construction. No runtime parser, filesystem, reflection, `dynamic`, property discovery, or shape inference is emitted.

## Fixtures and representative corpus

Filesystem source/asset fixtures are under `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/{Valid,Invalid}`. Existing `Tson/Valid/tables` and `Tson/Invalid/tables` remain the exhaustive table-document/schema/shape/cell/limit corpus; M1 tests compose their established reader behavior with declaration-owned resolution and projection.

The representative M1 corpus is `TsonTableAssets/Corpus/representative` and covers primitive, zero-row, record, payload-enum, nested-array, binary64, Unicode, order, `.obj.ts`, and canonical `.tson` evidence.

| Artifact | UTF-8 bytes | SHA-256 |
| --- | ---: | --- |
| `empty.tson` | 130 | `83290D5672AA58BF14F8F23E8B6F54BB2883C8B47C16418D39971A881D6D173B` |
| `main.cope` | 1,681 | `B5E206D80383A49D821EBFD3DEB3EF8E2DD12FFD7D3BE3F6579C5EA0AACCA90A` |
| `main.g.cs` | 14,916 | `E44594FF253DF2210366616E808F34135D84AE7091FC120A9F3735403F2C1B9F` |
| `main.g.js` | 38,279 | `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A` |
| `main.ts` | 971 | `FF124D4067C5BE4A2F8C7242902A04EDDB243F0419E1ABAEA100228EBE8E4CEF` |
| `samples.obj.ts` | 660 | `0D42F52BABBAC35E584B5D8ECD7B60B9B8DD69ECA58C82D10E065905AAA28761` |

Repeated compiler and CLI emission is byte-identical. Generated artifacts contain none of the asset paths, extensions, intrinsic, schema identity, TSON reader, or filesystem helpers.

## Explicit M2 boundary

CTS-TSON-TABLE-M1 does not add expression-valued table construction, multiple values of one nominal table type, table mutation, runtime parsing/decoding, JSON, runtime table encoding, `tsonEncode` table support, or `MirTsonTablePlan`. CTS-TSON-TABLE-M2 remains the demand-created runtime table encoding milestone. The documentation-only [CTS-JS-EMIT-M0a audit](../language/copeland-ts-javascript-emission-profiles-design-cts-js-emit-m0a.md) occurs between M1 and M2 without changing this ladder; JavaScript emission implementation and artifact regeneration wait until TABLE-M2/M3 close.
