# CTS-TSON-TABLE-M0a table integration audit

## Result

CTS-TSON-TABLE-M0a is the accepted documentation-only architecture authority. Its compiler-host semantic and canonical slice is now implemented by [CTS-TSON-TABLE-M0b](../Copeland/architecture/copeland-ts-tson-table-semantic-model-cts-tson-table-m0b.md); compiler asset and runtime integration remain deferred.

The selected representation is a dedicated nominal `TsonTable` with declaration-ordered, schema-evidenced `TsonTableColumn` nodes. A column is not a `TsonArray`; array-valued cells use the existing `TsonArray`. The first slice is table-root-only and excludes Result-valued columns.

No production code, test, fixture, corpus artifact, project, package, parser, binder, MIR, backend, CLI, or runtime behavior changes in this milestone.

## Starting state

| Item | Observed state |
| --- | --- |
| Revision | `bfe2ec6c743d7d2dd3aa9d4b631d63c30bd7327e` |
| Branch | `main` |
| Upstream | `origin/main` at the same revision; zero reported divergence |
| Worktree | Clean before M0a documentation edits |

The audit preserved the initial state and performed no checkout, reset, restore, commit, push, publish, package change, or generated-artifact rewrite.

## Authoritative records read

The audit reconciled the complete CTS-TABLE-M0a–M3, CTS-TSON-M0a–M2c, and CTS-TSON-ARRAY-M0a–M1 design, architecture, and migration records with the canonical [Copeland TS language profile](../Copeland/language/copeland-ts-language-profile.md). Historical claims were treated as evidence only when current source and tests still support them.

The direct JSON-first portions of CTS-TABLE-M0a are superseded. The authoritative route is now table value to nominal TSON table to policy-directed JSON compatibility lowering.

## Current syntax and semantic ownership

| Area | Current production evidence | Audit finding |
| --- | --- | --- |
| Syntax | `src/Copeland/Copeland.TS/Syntax/Parser.cs` `ParseTableDeclaration`; `SyntaxNodes.cs` `TableDeclarationSyntax` and `TableColumnSyntax` | The production parser accepts `record table Name { inferred: [cells]; typed: T = [cells]; }`. There is no second parser and no asset-backed table declaration form. |
| Table identities | `src/Copeland/Copeland.TS/Semantics/Types.cs` `TableTypeId`, `TableColumnId`, `TableRowFieldId`, `TableTypeSymbol`, `TableRowTypeSymbol` | Source order assigns compiler-local `tN`, `tN.cM`, and derived row/field identities. `TableTypeSymbol.AddColumn` derives the nominal row fields from columns. |
| Columns | `src/Copeland/Copeland.TS/Semantics/Symbols.cs` `TableColumnSymbol`; `Types.cs` `ColumnTypeSymbol` | `column T` is structural in its element type. It is not an array type. |
| Binding | `src/Copeland/Copeland.TS/Semantics/Binder.cs` `PredeclareTables`, `BindTableBodies`, `BindTableConstant`, `IsEligibleTableCellType` | A declaration owns both its nominal type and global singleton. Binding requires at least one column, explicit types for empty columns, homogeneous cells, equal lengths, deeply immutable eligible types, and no recursive/cyclic table cell schema. |
| Bounds type | `Binder.cs` `PredeclareTableBoundsError` | Compiler-owned `TableBoundsError` has `InvalidIndex(index: number)` and `OutOfBounds(index: number, rowCount: number)`. |

`Parser.ParseTableDeclaration` already uses ordinary `ArrayLiteralExpressionSyntax` for column data. That reusable syntax fact does not make a column an ordinary runtime or TSON array.

## Bound and MIR table model

`src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs` owns:

- `BoundTableDefinition` with `TableTypeSymbol`, ordered columns, and one row count;
- `BoundTableColumnDefinition` with a `TableColumnSymbol` and closed cells;
- closed `BoundTableConstant` variants for literal, record, enum, and Result data; and
- table reference, column access, row access, column element access, and row-field access expressions.

`src/Copeland/Copeland.TS.Mir/MirNodes.cs` owns the parallel backend-neutral model:

- `MirTableDefinition`, `MirTableColumnDefinition`, `MirTableId`, and `MirTableColumnId`;
- `MirTableType`, nominal `MirTableRowType`, and structural `MirColumnType`;
- closed `MirTableConstant` literal, record, enum, and Result variants; and
- resolved table/row/column access expressions.

`src/Copeland/Copeland.TS/Lowering/MirLowerer.cs` lowers bound definitions and constants directly. Neither constant hierarchy contains an array variant. `Binder.BindTableConstant` rejects `BoundArrayExpression`, and `IsEligibleTableCellType` excludes `ArrayTypeSymbol`. Current focused tests explicitly reject direct, record-nested, enum-nested, and Result-nested array table cells as `COPE-TABLE-0009`.

The future TSON cell algebra may admit arrays because CTS-TSON-ARRAY-M1 is closed, but compiler integration must add explicit closed table-array constant nodes. Storing ordinary executable expressions in a table definition would violate the closed table ladder.

## Validation audit

`src/Copeland/Copeland.TS.Mir/MirValidator.cs` validates table definitions before either backend emits. The current checks cover:

- unique and exact table, row, column, and row-field compiler-local identities;
- positive column inventory and nonnegative row count;
- exact column constant counts and rectangularity;
- primitive constant kind/type agreement;
- record identity, exact fields, field order, duplicates, and completeness;
- enum identity, case, payload arity, payload order, and payload types;
- Result branch, payload type, and the excluded `Result<void, E>` form;
- table/row/column type ownership and access result types; and
- the exact compiler-owned `TableBoundsError` definition.

`Binder.IsEligibleTableCellType` and `ContainsCyclicTableCellType` reject recursive record, enum, and Result cell schemas. Current implementation uses recursive helpers with visiting sets. A future table-TSON implementation should retain bounded depth and prefer explicit work stacks for new schema/value/plan traversal.

Malformed table constant cases are shared by `tests/Copeland/TestSupport/TableMirValidationCases.cs` and exercised through both backend entry points by `tests/Copeland/Copeland.TS.Tests/MalformedTableConstantValidationTests.cs` and backend suites. Malformed MIR yields the existing shared no-artifact diagnostics rather than backend-specific table recovery.

## C# realization

`src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs` `EmitTable` creates, per `MirTableDefinition`:

- one sealed table class;
- one sealed table-owned row class;
- one sealed column class per column, derived from generated `CopeColumn<T>`;
- one private typed CLR array per authoritative column;
- an internal column view that retains its private array;
- a private table constructor and deterministic `Create` factory; and
- one private static authored singleton in the generated module.

Rows retain the owning table and checked integer index, then project fields through table-specific direct reads. There is no row-oriented authoritative storage. Table and column indexing check NaN, infinities, and fractions before bounds and return ordinary `CopeResult<..., TableBoundsError>` values.

The CLR arrays are mutable implementation objects, but no generated Copeland surface exposes them or a setter. Their identity and mutability are not language or interchange semantics.

## JavaScript realization and provenance

`src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs` `EmitTableRuntime` creates:

- private table, row, and column Symbols;
- one frozen dense private array per authoritative column;
- frozen null-prototype column carriers with private brand/read closures;
- a frozen null-prototype table carrier with fixed non-enumerable symbol slots;
- row-read closures returning ordinary frozen Result carriers; and
- frozen null-prototype row views containing the table, checked index, and table-specific row token.

Rows project through the owning table's authoritative column closure and contain no copied record. Columns are deliberately not JavaScript arrays. `Array.isArray(columnView)` is false and storage arrays are not table, row, or column properties.

CTS-TSON-M2c also hardened generated nominal record and enum carriers with private `WeakSet` provenance. That registry protects record/enum cells and ordinary access, but it is backend-private and never an exchange identity.

## Bounds, staging, fixtures, and parity

Current language fixtures are owned by:

- `tests/Copeland/Copeland.TS.Tests/Language/Valid/tables` with three valid programs;
- `tests/Copeland/Copeland.TS.Tests/Language/Invalid/tables` with thirteen invalid programs;
- `TableFeatureTests` and `TableDiagnosticInventoryTests` for source, bound, MIR, and `COPE-TABLE-0001` through `0019` evidence;
- `MalformedTableConstantValidationTests` and shared malformed cases;
- C# corpus `tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m1-table-csharp-valid`;
- JavaScript corpora `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/m2-table-basic.*` and `m2-table-nested.*`;
- `TableCloseoutParityTests` for repeated C#/Node parity and staging; and
- `CliIntegrationTests` for MIR/C#/JavaScript output and execution.

The closed parity law classifies `-0` as index zero; NaN, infinities, and fractions as `InvalidIndex`; and negative or too-large finite integrals as `OutOfBounds`. Receiver and index evaluation are staged once. None of those operations belongs in canonical table data.

## Current TSON semantic model

`src/Copeland/Copeland.TS/Tson` currently owns:

| File | Production types and behavior |
| --- | --- |
| `TsonValues.cs` | closed `TsonValue`; `TsonBoolean`, normalized-bit `TsonNumber`, Unicode-valid `TsonString`, schema-evidenced `TsonArray`, structural `TsonObject`, nominal `TsonRecord`, and nominal `TsonEnum` |
| `TsonSchema.cs` | `TsonTypeReference`, `TsonArraySchema`, field/record/enum definitions, and `TsonCatalog` with one schema identity |
| `TsonDocument.cs` | Object TypeScript/canonical profiles, bounded `TsonLimits`, diagnostics, and `TsonDocument` |
| `TsonDocumentReader.cs` | `SyntaxTree.Parse(source)` entry, restriction/projection, schema/identity/type/value validation, cycles, bounds, and canonical-byte verification |
| `TsonCanonicalPrinter.cs` | exact self-described declaration/value text, binary64 bits, Unicode escapes, four-space/LF/final-newline law |

Current stable identities are `schema#Type`, `schema#Type.field`, `schema#Enum.Case`, and `schema#Enum.Case.payload`. `TsonArray` has structural element schema and no nominal identity. Root arrays are explicitly rejected. Tables are rejected by the TSON restriction pass and `TsonTable` does not exist.

Default reader limits are source length 1,048,576 UTF-16 code units; depth 64; declarations 256; fields 256; enum cases 256; payloads 64; value nodes 100,000; strings 262,144 UTF-16 code units; and arrays 100,000 elements.

## Compile-time asset ingestion

`src/Copeland/Copeland.TS/Semantics/Binder.cs` currently accepts `tsonAsset` only as the initializer of an explicitly typed local `const` whose expected type is a same-unit nominal record or payload enum. It requires compilation-unit `$schema`, resolves `.obj.ts` or `.tson`, uses `TsonDocumentReader.ReadSelfDescribed`, validates exact reachable schema and identity, then lowers to ordinary bound record/enum/array construction.

`src/Copeland/Copeland.TS/Compiler/CopelandAssets.cs` resolves root-confined relative paths and records a normalized root-relative path plus lowercase SHA-256 source-content hash. Paths, comments, source layout, reader/catalog/value objects, and the intrinsic are absent from MIR and generated artifacts.

The existing expression form cannot import a table without contradicting authored singleton semantics. The design therefore assigns future table assets to declaration-owned initialization: source declares the expected ordered schema, and exactly one asset supplies that declaration's singleton data.

## Runtime encoding and fixed points

`src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs` owns `BoundTsonEncodingPlan` and `BoundTsonEncodeExpression`. `src/Copeland/Copeland.TS.Mir/MirNodes.cs` owns immutable `MirTsonEncodingPlan`, primitive/record/enum `MirTsonValuePlan` nodes, structural `MirTsonArrayPlan`, limits, and `MirTsonEncodeExpression`.

`src/Copeland/Copeland.TS.Mir/MirTsonCanonicalText.cs` builds shared static canonical text. `MirValidator` checks plan identity, reachability, ordering, type agreement, cycles, limits, and static text before either backend. Both generated writers directly traverse statically known carriers, preserve binary64 and Unicode laws, enforce output limits, and return `string ! TsonEncodeError` without reflection, property enumeration, host serialization, parsing, or filesystem access.

The record core fixed point in `TsonEncodeRuntimeTests.Runtime_canonical_output_recompiles_as_a_canonical_asset_without_byte_changes` and the array corpus fixed point in `Array_corpus_has_two_generation_csharp_node_fixed_point_and_pinned_artifacts` prove:

```text
generation 1 authoring asset
    -> compiler value
    -> C# and Node exact canonical output
    -> canonical reader/printer byte fixed point
    -> generation 2 canonical asset
    -> C# and Node identical bytes
```

Table TSON should extend this proof shape, not create a parallel encoder or a JSON oracle.

## Topology and dependency enforcement

`tools/Validate-CopelandTsTopology.ps1` currently enforces:

- no TSON lexer/parser/token hierarchy;
- `TsonDocumentReader` enters through `SyntaxTree.Parse`;
- no backend, CLI, Machina, Aurelian, Dominatus, Roslyn, reflection, or host-serializer dependency in compiler-host TSON;
- no `TsonResult`, `TsonTable`, or `TsonJson` before a separately approved milestone;
- no compiler-host TSON semantic types in Cope MIR;
- `MirTsonEncodingPlan`, `MirTsonEncodeExpression`, and `MirTsonArrayPlan` remain MIR-owned;
- no compiler-host TSON semantic dependencies in backends; and
- no JSON, reflection, `dynamic`, or runtime filesystem dependency in generated encoding.

`tools/Validate-DependencyBoundaries.ps1` independently checks repository project and subsystem boundaries. M0b replaced the deliberate `TsonTable` prohibition with assertions for shared-parser table projection, compiler-host-only ownership, and the continued absence of `MirTsonTablePlan`.

## Architecture decisions

| Question | Decision |
| --- | --- |
| Semantic representation | Dedicated nominal `TsonTable`, not a record/object of arrays |
| Column representation | Distinct table column node with stable identity, element schema, and ordered cells |
| Row representation | Derived from table schema plus index; never serialized in the first slice |
| Stable identities | `$schema#Table` and `$schema#Table.column`; no compiler-local IDs |
| Cells | Boolean, Number, String, Record, payload Enum, and nested Array |
| Results | Excluded until a separate TSON Result design exists |
| Root | One table root; no nesting; direct authored-singleton encoding only |
| Assets | Table document root plus future declaration-owned singleton initialization |
| Runtime plan | Demand-created `MirTsonTablePlan` inside existing plan ownership |
| Bounds errors | Indexing behavior only; absent from canonical table data |
| JSON | Later policy lowering from nominal TSON table, never authority |

## Limits and diagnostics

The design reuses 256 columns, 100,000 rows, 100,000 total cells, depth 64, total value nodes 100,000, string length 262,144 UTF-16 code units, and canonical runtime output 1,048,576 UTF-8 bytes. Checked aggregate accounting must reject the cell/node limit before allocating a Cartesian maximum.

The proposed bounded family is `COPE-TSON-TABLE-0001` through `0005` for root/policy, identity/schema, columns/shape, cells/canonicality, and limits. Existing parser, core TSON, asset, encode, and malformed-MIR families retain their established ownership.

## Proposed ladder

1. **CTS-TSON-TABLE-M0b:** compiler-host semantic table/schema/column nodes, existing-parser projection, canonical reader/printer, fixtures, limits, and fixed points; no compiler/backend integration.
2. **CTS-TSON-TABLE-M1:** declaration-owned table asset ingestion, exact schema/identity/shape validation, table bound/MIR materialization, and explicit array constant support if retained.
3. **CTS-TSON-TABLE-M2:** demand-created table encoding plans and direct authoritative-column C#/JavaScript parity.
4. **CTS-TSON-TABLE-M3:** two-generation, malformed semantic/asset/plan, CLI, artifact, topology, and doctrine closeout.

## Files changed

- `docs/Copeland/README.md`
- `docs/Copeland/architecture/copeland-ts-record-tables-closeout-cts-table-m3.md`
- `docs/Copeland/architecture/copeland-ts-runtime-tson-array-encoding-cts-tson-array-m1.md`
- `docs/Copeland/architecture/copeland-ts-tson-core-closeout-cts-tson-m2c.md`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/Copeland/language/copeland-ts-record-tables-design-cts-table-m0a.md`
- `docs/Copeland/language/copeland-ts-tson-tables-design-cts-tson-table-m0a.md`
- `docs/migrations/cts-tson-table-m0a-table-integration-audit.md`

## Validation

Validation completed on 2026-07-14:

| Check | Result |
| --- | --- |
| Exact changed-path and extension inspection | Passed: exactly the eight Markdown files listed above; no production, test, fixture, corpus, project, solution, package, CLI, or tool path changed. |
| Local Markdown links and literal repository paths | Passed for all changed documents; 31 literal `src/`, `tests/`, `tools/`, or `docs/` paths were resolved. |
| Heading anchors | Passed: no duplicate normalized heading anchor in any changed document. |
| Markdown tables and code fences | Passed: contiguous table rows have consistent unescaped column counts and every fenced block is balanced. |
| Terminology and historical status | Passed: `TsonTable` is consistently described as selected but unimplemented; direct JSON-first table proposals are explicitly historical and superseded. |
| Whitespace and encoding | Passed: no trailing whitespace, UTF-8 BOM, invalid UTF-8, or disallowed control character. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 27 production projects with no exceptions. |
| `git diff --check` | Passed; only Git working-copy LF-to-CRLF advisories were emitted. |

Full solution tests were intentionally not run because final scope inspection found documentation changes only.
