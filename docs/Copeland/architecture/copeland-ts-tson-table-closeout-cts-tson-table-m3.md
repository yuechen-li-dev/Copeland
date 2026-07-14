# Copeland TS TSON table closeout (CTS-TSON-TABLE-M3)

**Status:** closed. This record ratifies the complete table-TSON ladder from CTS-TSON-TABLE-M0a through M2. M3 adds focused closeout evidence and doctrine only; it introduces no table-language, JSON, or CTS-JS-EMIT production feature.

## Columnar law

Record-table data is columnar by law. The semantic `TsonTable` owns declaration-ordered `TsonTableColumn` values; every column owns its ordered cells; all columns have the same length; and `RowCount` is derived from that shared column length. Typed zero-row columns remain typed empty columns. There is no semantic table-row value, serialized row node, or serialized row-view type.

Canonical TSON, declaration-owned `.obj.ts` projection, bound constants, `MirTsonTablePlan`, generated C# storage, generated JavaScript storage, and `tsonEncode` all preserve that authority. A row is only a derived table-and-index access view. It reads across authoritative columns on request and is never encoded.

The canonical form visibly groups each declaration-ordered column's cells:

```ts
record table Samples {
    a: number = [
        $number("3FF0000000000000"),
        $number("4000000000000000"),
    ];
    b: number = [
        $number("4010000000000000"),
        $number("4014000000000000"),
    ];
}
```

It is semantically equivalent to `{ a: [1, 2], b: [4, 5] }`, never `[{ a: 1, b: 4 }, { a: 2, b: 5 }]`. `TsonTableFeatureTests.Canonical_table_data_is_columnar_and_has_no_row_representation` inspects the semantic columns and cells, asserts no row collection/type exists, and verifies the canonical declaration order and grouped cell blocks.

Future JSON remains unimplemented. Its ratified compatibility direction is nominal TSON table to a default columnar JSON object, `{ a: [...], b: [...] }`. A row-object-array compatibility form would require a separately named policy and cannot silently become the default.

## Fixed point and runtime boundary

`TsonEncodeRuntimeTests.Table_m2_corpus_has_pinned_artifacts_and_repeated_canonical_fixed_point` is the two-generation proof. Generation 1 compiles the declaration-owned authored asset, emits both backends twice byte-identically, executes exact C# and Node output, requires C#/Node byte equality, no BOM, one final LF, canonical-reader acceptance, and canonical-printer byte equality. Generation 2 uses those exact bytes as the hermetic `generation-1.tson` asset of a fresh source-owned table declaration, lowers through ordinary bound/MIR definitions, executes both backends, and requires the original exact bytes again. The assertion also proves that the asset filename, host path, and authoring comment do not enter MIR, generated artifacts, or runtime output.

The retained corpus covers multiple declaration-ordered columns and rows; typed zero rows; Boolean, number, string, nominal record, payload enum, empty nested-array, and nested-array cells; `0`, `-0`, finite fractions, normalized NaN, both infinities, escaped strings, non-ASCII text, and supplementary Unicode. Core and ARRAY-M1 corpus lanes retain maximum finite binary64 and minimum-positive-subnormal coverage. No row-object writer exists: the canonical printer and generated writers enumerate columns directly, then enumerate each column's cells in element order.

C# captures every private column exactly once and validates all lengths before writing. JavaScript validates authentic WeakSet-provenanced table and column carriers, captures each private dense column array once, validates every length, rejects holes before each read, and writes cells in ascending order. Both writers use declaration order for columns and the existing declaration order for record fields and enum payloads. They construct no row during encoding and repeated encoding observes the same immutable table state.

Ordinary failures remain exactly `InvalidUnicode` and `OutputLimitExceeded`; they use Result forwarding, `?`, unwrap, and typed `try`/`except` without host exceptions. Counterfeit carriers, incorrect provenance/tokens, malformed private slots, wrong lengths, sparse arrays, and impossible plan/carrier shape are terminal invariants and bypass typed `except`. C# retains generated direct private access with no reflection or `dynamic`; JavaScript retains non-public Symbols and `WeakSet` provenance. The hostile test accesses the generated private closure only through the existing test harness and proves counterfeit rejection without weakening the runtime carrier.

## Shared validation, demand, and artifact policy

`MirValidator` is the sole malformed-plan boundary before either backend emits. `MalformedTsonEncodingPlanValidationTests` exercises missing, duplicate, malformed, cross-root, wrong-identity, reordered/duplicate/missing column, ragged-length, table-root, nested-table, unsupported plan, array/schema cycle, bounds, and static-text failure cases through both backend entry points. Both return their existing shared invalid-MIR boundary and no artifact.

`TsonEncodeFeatureTests` proves demand creation and plan reuse: no intrinsic means no plan or error enum, row/column/cell views are rejected, repeated singleton encoding reuses one plan, and asset-backed singletons use the same path. `TsonEncodeRuntimeTests.Writer_helpers_are_demand_emitted_and_forbidden_runtime_apis_are_absent` proves non-table encoding does not demand a table writer and searches emitted artifacts for parser, filesystem, reflection, property enumeration, host serialization, and row-object serialization routes. Existing CLI integration tests own fresh-output, repeatability, invalid-source/no-partial-output, invalid-MIR/no-artifact, stale-output SHA-256 preservation, and path-safe diagnostics.

The Diagnostic JavaScript emitter remains authoritative. CTS-JS-EMIT-M0a is recorded only as a future benchmark and sequencing constraint; M3 neither changes helper naming/deduplication nor regenerates its corpus.

## Retained artifact inventory

All retained artifacts are unchanged, UTF-8 without BOM, and LF-terminated. No M3 corpus artifact was needed; the generation-two asset is hermetic test input.

| Relative path | Bytes | SHA-256 | State / reason |
| --- | ---: | --- | --- |
| `tests/Copeland/Copeland.TS.Tests/Tson/Tables/Corpus/representative.obj.ts` | 705 | `EB9AC540BD235AA869A323936E7F328CB7320A9BAF554ACD4CEC4F9CD15AE0D9` | unchanged M0b semantic authoring corpus |
| `tests/Copeland/Copeland.TS.Tests/Tson/Tables/Corpus/representative.tson` | 1,145 | `450DF822E63C4A1F681D98796D707EA6AAB35D1B4D533CDD479B49BB2394256A` | unchanged M0b canonical corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/empty.tson` | 130 | `83290D5672AA58BF14F8F23E8B6F54BB2883C8B47C16418D39971A881D6D173B` | unchanged M1 asset corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main.cope` | 1,681 | `B5E206D80383A49D821EBFD3DEB3EF8E2DD12FFD7D3BE3F6579C5EA0AACCA90A` | unchanged M1 MIR corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main.g.cs` | 14,916 | `E44594FF253DF2210366616E808F34135D84AE7091FC120A9F3735403F2C1B9F` | unchanged M1 C# corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main.g.js` | 38,279 | `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A` | unchanged M1 Diagnostic-JS benchmark |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main.ts` | 971 | `FF124D4067C5BE4A2F8C7242902A04EDDB243F0419E1ABAEA100228EBE8E4CEF` | unchanged M1 source corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonTableAssets/Corpus/representative/samples.obj.ts` | 660 | `0D42F52BABBAC35E584B5D8ECD7B60B9B8DD69ECA58C82D10E065905AAA28761` | unchanged M1 asset corpus |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/empty.obj.ts` | 164 | `A3E967D07DF6730E703718EC84EF42CEE5360682022751AB2FF65B683220088E` | unchanged M2 zero-row asset |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/expected.tson` | 1,619 | `77DB4113560183DD4F052F16E8656C0B2B1673FD39373FA6B720E58225F78666` | unchanged M2 canonical bytes; M3 generation-two input |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main.cope` | 2,154 | `5CF1FC80EFAE33F77807298E7EE9F9A10C57565E09715B14515549D35AC78A4A` | unchanged M2 MIR |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main.g.cs` | 34,774 | `B9E4B309991EE59B17016C7595669C1F34F068A0191C7804A6E8DD98EFC6B09C` | unchanged M2 C# |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main.g.js` | 62,425 | `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9` | unchanged M2 Diagnostic-JS artifact and CTS-JS-EMIT benchmark |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main.ts` | 577 | `563EA53F2241964E9E43749B008131301C2F883D6B7ABA01827B40E6ED619064` | unchanged M2 source |
| `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/samples.obj.ts` | 1,054 | `684FE68C20A7EC25BD24A853198C3C5274CF5BDDC30B19F3468067FC154D55D0` | unchanged M2 authoring asset |

## Requirement ledger

| Requirement group | Evidence | Final status |
| --- | --- | --- |
| Default table emission is declaration-ordered columns of arrays, never arrays of row objects | `TsonTable`, `TsonTableColumn`, canonical printer, both generated writers, and the new columnar regression | satisfied |
| Ordered columns/cells, rectangularity, row-count derivation, typed zero rows, and no serialized rows | `TsonTableFeatureTests`, canonical/asset fixtures, `TsonTableValidation` | satisfied |
| Inline and declaration-owned asset path through bound/MIR/private carriers/runtime writer | `TsonTableAssetFeatureTests`, `TsonEncodeFeatureTests`, M2 corpus | satisfied |
| Two-generation C#/Node fixed point, canonical reader/printer, BOM/final-LF, and provenance erasure | M3-expanded `Table_m2_corpus_has_pinned_artifacts_and_repeated_canonical_fixed_point` | stronger evidence |
| Primitive, nominal, nested-array, numeric, Unicode, ordering, and zero-row matrix | M0b/M1/M2 table corpus plus core/array corpus edge lanes | satisfied |
| Exactly-once, Result forwarding, propagation, typed recovery, and unselected paths | `TableCloseoutParityTests` and `TsonEncodeRuntimeTests` staging/flow tests | satisfied |
| Authentic JavaScript carrier, counterfeit, wrong provenance/token, sparse-array, and terminal failure boundary | `Table_encoding_preserves_result_flow_and_terminal_invariants` and private-carrier checks | satisfied |
| Malformed semantic documents/assets | `TsonTableFeatureTests`, `TsonTableAssetFeatureTests`, `TsonFixtureTests`, invalid filesystem fixtures | satisfied |
| Malformed `MirTsonTablePlan` and related values before either backend emits | `MalformedTsonEncodingPlanValidationTests` through both emitters | satisfied |
| Demand emission, plan reuse, no-table writer for non-table roots, and diagnostic-emitter stability | `TsonEncodeFeatureTests`, writer-demand test, retained hashes | satisfied |
| CLI fresh/repeated/no-partial/stale-output and diagnostic path policy | existing `CliIntegrationTests` table/TSON asset lanes | satisfied |
| JSON and a row-oriented default | no implementation; future default direction explicitly ratified as columnar | accepted-scope exclusion |
| CTS-JS-EMIT structured writer, naming migration, helper deduplication, release work | CTS-JS-EMIT-M0a sequencing only; 62,425-byte artifact retained | accepted-scope exclusion |

The ledger has zero `missing` rows. The complete TSON table ladder is honestly closed.
