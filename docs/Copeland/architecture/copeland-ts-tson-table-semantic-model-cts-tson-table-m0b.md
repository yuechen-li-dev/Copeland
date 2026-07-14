# Copeland TS TSON table semantic model (CTS-TSON-TABLE-M0b)

**Status:** implemented compiler-host semantic and canonical-text milestone. Declaration-owned compiler asset ingestion and ordinary backend realization are implemented by [CTS-TSON-TABLE-M1](copeland-ts-tson-table-assets-cts-tson-table-m1.md); runtime encoding, decoding, and JSON remain excluded.

## Implemented path

```text
.obj.ts or canonical .tson
    -> production SyntaxTree.Parse
    -> restricted TSON projection
    -> TsonCatalog plus TsonTableSchema
    -> immutable TsonTable
    -> bounded canonical printer
    -> exact UTF-8 canonical bytes
```

No table lexer, parser, token family, textual preprocessor, regex parser, or copied grammar exists. Both profiles enter through the same `SyntaxTree.Parse(source)` call and interpret the production `TableDeclarationSyntax`, `TableColumnSyntax`, type syntax, and expression syntax.

## Semantic model

`TsonTableSchema` is a nominal catalog definition. It owns an opaque `TsonTableIdentity` and declaration-ordered `TsonTableColumnSchema` values. Each column schema owns an opaque `TsonTableColumnIdentity` and explicit `TsonTypeReference` element schema.

`TsonTable` owns its exact schema, immutable declaration-ordered `TsonTableColumn` values, and validated row count. Each column owns its schema evidence and an immutable ordered cell sequence. Callers cannot mutate retained storage: all collection inputs are defensively copied. Rectangularity is established before publication.

Identities derive only as:

```text
table  = $schema#Table
column = $schema#Table.column
```

The opaque identity factories validate the schema authority before creating these values. Compiler-local `t0`, `t0.row`, `t0.c0`, MIR identities, and backend carrier identities never enter this model or canonical text. Same-shaped tables under different stable schema identities remain nominally distinct.

The first cell algebra is Boolean, binary64 Number, Unicode String, nominal Record, payload Enum, and homogeneous nested Array of those families. A column is not a `TsonArray`; only an array-valued cell is. Result, `void`, `null`, `undefined`, structural object cells, tables, rows, columns, functions, aliases, sparse arrays, and heterogeneous arrays are rejected.

## Root and catalog laws

A table document contains exactly one table declaration and selects it only with the exact untyped form:

```ts
const $value = TableName;
```

Reachable record and enum declarations may accompany it. A table cannot occur in a record field, enum payload, array element, table cell, nested declaration, or arbitrary expression. Root arrays remain rejected. Existing non-table structural-object root behavior is unchanged.

Table, record, enum, field, case, payload, and column names/identities remain unique in their established owners. Record and enum cell types resolve through the same catalog. Catalog cycle detection includes table-column references and nested array element schemas. Tables cannot be referenced as cell types.

Zero-row tables are represented by one or more explicitly typed empty columns. Zero-column tables are rejected deterministically because no serialized fact can establish their row count.

## Canonical form

Canonical table declarations visibly retain `record table`, declaration-ordered columns, explicit element types, and columnar values:

```ts
const $schema: string = "copeland://example/telemetry";

record table Samples {
    active: boolean = [
        true,
        false,
    ];
    score: number = [
        $number("3FF0000000000000"),
        $number("8000000000000000"),
    ];
}

const $value = Samples;
```

Nominal declarations use ordinal name order; table columns preserve declaration order. Existing binary64 bit spelling, normalized NaN, Unicode validation, escaping, record and enum constructors, nested arrays, four-space indentation, LF, no BOM, and exactly one final LF remain authoritative. `CanonicalTson` reparses and compares printer equality, so comments, alternate layout, reordered declarations, and alternate spellings fail canonicality.

## Limits and precedence

Defaults are 256 columns, 100,000 rows, 100,000 total cells, 100,000 total value nodes, depth 64, array length 100,000, string length 262,144 UTF-16 code units, source length 1,048,576 UTF-16 code units, and canonical output 1,048,576 UTF-8 bytes.

Value nodes count one table, one node per column, one node per cell value, and every nested record, enum, array, field/payload value, and array element through the existing value traversal. Cells are not double-counted as wrapper nodes. Empty columns contribute their column nodes and zero cells. Cell totals use widened multiplication before materialization; row and column maxima therefore do not permit their Cartesian product.

Validation precedence is: source/syntax bounds and parser diagnostics; envelope and schema identity; nominal catalog and cycles; column presence/order/rectangularity; row and aggregate-cell bounds; table/column node reservation; cell depth/node/type/value validation; canonical UTF-8 output; canonical byte equality. No rejected table publishes a partially constructed `TsonTable`.

The canonical writer accounts UTF-8 bytes on every append and throws `TsonCanonicalLimitException` before exceeding the configured bound. It does not first construct unbounded output.

## Diagnostics and evidence

- `COPE-TSON-TABLE-0001`: table declaration, envelope, exact root, or containment misuse.
- `COPE-TSON-TABLE-0002`: table/column schema, type evidence, or stable identity failure.
- `COPE-TSON-TABLE-0003`: column presence, uniqueness, order, rectangularity, or shape failure.
- `COPE-TSON-TABLE-0004`: unsupported, invalid, or mismatched cell value.
- `COPE-TSON-TABLE-0005`: table resource or canonicality failure.

Filesystem fixtures live under `tests/Copeland/Copeland.TS.Tests/Tson/Valid/tables` and `Tson/Invalid/tables`. The representative authoring/canonical corpus lives under `Tson/Tables/Corpus`. Its canonical artifact is 1,145 UTF-8 bytes with SHA-256 `450DF822E63C4A1F681D98796D707EA6AAB35D1B4D533CDD479B49BB2394256A`.

## Boundary and M1

`TsonTable` remains entirely in `Copeland.TS.Tson`. No compiler table asset ingestion, bound table constant, MIR table plan, backend API, generated runtime artifact, CLI mode, Machina dependency, or Aurelian dependency was added.

CTS-TSON-TABLE-M1 is closed as declaration-owned compile-time table asset initialization, exact identity/schema/shape validation, and closed table/array constant lowering. It does not turn expression-valued `tsonAsset` into a general table constructor or add runtime encoding.
