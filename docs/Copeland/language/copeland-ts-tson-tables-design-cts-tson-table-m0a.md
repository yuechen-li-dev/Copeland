# Copeland TS TSON tables design (CTS-TSON-TABLE-M0a)

**Status:** accepted design and architecture-audit authority, implemented through compiler-host semantic/canonical M0b and declaration-owned compile-time asset ingestion M1. Runtime table encoding remains deferred to M2.

## Decision

Copeland record tables require a dedicated nominal `TsonTable` semantic variant. A table is not represented as a generic `TsonObject`, a nominal record, or a record whose fields happen to contain `TsonArray` values.

The selected semantic shape is:

```text
TsonTableDefinition
    stable table identity
    declaration-ordered TsonTableColumnDefinition values

TsonTable
    exact table-definition identity
    one validated row count
    declaration-ordered TsonTableColumn values

TsonTableColumn
    exact stable column identity
    explicit element TsonTypeReference
    immutable ordered TsonValue cells
```

`TsonTableColumn` is a table-owned semantic node, not a `TsonArray`. It participates in the table's nominal identity, declaration order, and rectangularity invariant. An array-valued cell is a `TsonArray`, including when nested inside another array-valued cell.

This preserves all of the language meaning that a structural record of arrays would erase:

- table-ness survives authoring-to-canonical and canonical-to-semantic round trips;
- same-shaped tables retain distinct nominal identities;
- column identity and declaration order remain explicit;
- empty columns retain their element schemas without ambient project state;
- one row count is validated against every authoritative column;
- rows remain derived table-and-index projections rather than serialized values; and
- JSON can later be a policy-directed projection without becoming the table model.

The rejected representation would make a table indistinguishable from an ordinary record with array fields after projection. It would also incorrectly identify the language's structural immutable `column T` view with the ordinary array family. Similar future JSON shapes do not justify collapsing those language distinctions.

## Evidence inherited from the closed ladders

This decision composes two already closed contracts:

1. CTS-TABLE-M0a through M3 establish a nominal immutable table schema plus authored singleton, canonical columnar storage, nominal table-owned rows, structural non-array column views, closed rectangular data, and private C#/JavaScript carriers.
2. CTS-TSON-M0a through M2c and CTS-TSON-ARRAY-M0a through M1 establish stable schema-scoped exchange identity, one production parser, immutable compiler-host values, canonical self-described text, homogeneous schema-evidenced arrays, compile-time assets, demand-created encoding plans, and two-generation C#/Node fixed points.

M0a does not merge those models. It defines the narrow semantic bridge between them.

## Four identities that must not be confused

| Concern | Meaning | Canonical TSON status |
| --- | --- | --- |
| `t0`, `t0.row`, `t0.c0` | Deterministic compiler-local identities assigned by source declaration order | Never emitted |
| `copeland://example/data#Samples` | Stable exchange identity of the table schema and value | Emitted through `$schema` plus declared name |
| `copeland://example/data#Samples.score` | Stable exchange identity of one declared column | Reconstructed from `$schema`, table name, and column name |
| Backend table, row, column, array, Symbol, class, closure, or token | Private generated carrier identity | Never emitted |

The immutable semantic table value is the declaration-ordered column data plus its validated nominal schema identity. It is not the authored source node, a compiler table ID, a generated singleton field, or a private storage object.

A row view is derived from a table value and a checked index. It has no independent serialized data in the first slice. A column view is the language operation surface `column T`; it is not the `TsonTableColumn` compiler-host node and is not a runtime or semantic `TsonArray`.

The table declaration is the authored schema and singleton definition. The `TsonTable` semantic value is the immutable finite data obtained after that declaration has been projected and validated.

## Stable exchange identity

The compilation-unit schema identity remains the root of every stable TSON identity. It uses the implemented `$schema` law: an absolute, whitespace-free `copeland://...` string without `#`.

For a table named `Samples`:

```text
table       = schema#Samples
column      = schema#Samples.columnName
```

For nominal record or payload-enum cells, the existing laws remain unchanged:

```text
record      = schema#Record
field       = schema#Record.field
enum        = schema#Enum
case        = schema#Enum.Case
payload     = schema#Enum.Case.payload
```

The derived row type does not require a TSON exchange identity in the first slice. No row value or row schema declaration is serialized, and the row shape is reconstructed from the table's ordered column definitions. If a later feature serializes rows independently, it must define a collision-free identity law in a separate milestone; `schema#Table.Row` is not reserved here because `Row` remains a legal column spelling.

Compiler-local `TableTypeId`, `TableColumnId`, `MirTableId`, `MirTableColumnId`, row IDs, generated helper names, and source traversal ordinals never enter canonical TSON.

## First bounded cell algebra

The first table TSON cell algebra is:

```text
Boolean
Number
String
Record
Payload Enum
Array<Boolean | Number | String | Record | Payload Enum | Array<...>>
```

The following decisions are deliberate:

- Boolean, Number, String, Record, and payload Enum reuse the existing TSON semantic, identity, binary64, Unicode, declaration-order, and canonical-text laws.
- Arrays reuse `TsonArray`, `TsonArraySchema`, structural `TsonTypeReference.Array`, homogeneous validation, empty-array schema evidence, nested arrays, and the 100,000-element bound.
- A table column itself remains `TsonTableColumn`; only a cell whose declared Copeland element type is `T[]` is a `TsonArray`.
- Result-valued columns are excluded. Existing tables accept non-void closed Result constants, but TSON has no Result variant. M0a does not silently design or implement one.
- Tables, rows, and columns are not cell values. The first slice is table-root-only.
- Structural objects, optionality, aliases, interfaces, `null`, and `undefined` remain excluded.

The current `BoundTableConstant` and `MirTableConstant` hierarchies contain literal, record, enum, and Result variants but no array variant. A future M1 that accepts array-valued cells must deliberately add one closed array constant variant to each hierarchy and their shared validation/lowering paths. It must not store executable `BoundExpression` or `MirExpression` nodes in a table definition.

Every nominal schema cycle remains invalid, including cycles reached through an array element schema. Table nesting is excluded, so no table-to-table cycle can occur in the first slice. Future schema, semantic-value, and plan traversals should use explicit pending/visiting/completed work stacks where practical. Bounded source depth remains a defense, not a reason to write clever unbounded recursive walkers.

## Canonical table document

Canonical table TSON reuses ordinary Copeland parser syntax. It adds no lexer, token family, parser, or parallel data grammar. `TsonDocumentReader` will extend only its restricted semantic projection to admit an existing `TableDeclarationSyntax` and an exact table-root reference.

The canonical envelope remains:

```text
const $schema
all and only reachable declarations in ordinal name order
const $value
```

The table declaration visibly contains declaration-ordered, explicitly typed, columnar data. Canonical columns always use `name: Type = [cells];`, even when the authoring profile inferred a nonempty column. The table name at `$value` identifies the declaration's authored singleton.

Canonical formatting retains the established laws: four-space indentation, LF newlines, no comments, uppercase 16-digit binary64 bit spellings, canonical string escaping, multiline nonempty arrays with trailing commas, `[]` for empty arrays, and exactly one final LF. The canonical printer sorts reachable record, enum, and table declarations together by ordinal nominal name while preserving member declaration order.

### Populated primitive table

Canonical `.tson`:

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
    label: string = [
        "first",
        "second",
    ];
}

const $value = Samples;
```

Equivalent authoring `.obj.ts`:

```ts
const $schema: string = "copeland://example/telemetry";

// Types may be inferred for nonempty authored columns.
record table Samples {
    active: [true, false];
    score: [1, -0];
    label: ["first", "second"];
}

const $value = Samples;
```

### Empty table

Canonical `.tson`:

```ts
const $schema: string = "copeland://example/empty";

record table Empty {
    id: number = [];
    label: string = [];
}

const $value = Empty;
```

Equivalent authoring `.obj.ts` uses the same explicit types because current table syntax already requires them for empty columns:

```ts
const $schema: string = "copeland://example/empty";

record table Empty {
    id: number = [];
    label: string = [];
}

const $value = Empty;
```

### Nominal record and payload-enum cells

Canonical `.tson`:

```ts
const $schema: string = "copeland://example/observations";

record table Observations {
    point: Point = [
        $record.Point({
            "x": $number("3FF0000000000000"),
            "y": $number("4000000000000000"),
        }),
    ];
    state: State = [
        State.Named(
            "ready"
        ),
    ];
}

record Point {
    x: number;
    y: number;
}

enum State {
    Missing,
    Named(label: string),
}

const $value = Observations;
```

Equivalent authoring `.obj.ts`:

```ts
const $schema: string = "copeland://example/observations";

record Point {
    x: number;
    y: number;
}

enum State {
    Missing,
    Named(label: string),
}

record table Observations {
    point: Point = [{ x: 1, y: 2 }];
    state: State = [State.Named("ready")];
}

const $value = Observations;
```

### Array-valued cells

Canonical `.tson`:

```ts
const $schema: string = "copeland://example/batches";

record table Batches {
    values: number[] = [
        [
            $number("3FF0000000000000"),
            $number("4000000000000000"),
        ],
        [],
    ];
}

const $value = Batches;
```

Equivalent authoring `.obj.ts`:

```ts
const $schema: string = "copeland://example/batches";

record table Batches {
    values: number[] = [[1, 2], []];
}

const $value = Batches;
```

This authoring text parses today but current table constant binding rejects array-valued cells. Acceptance belongs to a later implementation milestone.

## Root policy

The first slice is intentionally table-root-only:

| Position | First-slice policy |
| --- | --- |
| Canonical TSON document root | Allowed: exactly one nominal `TsonTable` root |
| `.obj.ts` authoring document root | Allowed under the same self-described table contract |
| `.obj.ts` or `.tson` compile-time asset root | Allowed as a table document; compiler integration follows the singleton rule below |
| Nested in a record, enum, array, or table cell | Rejected |
| Direct operand of `tsonEncode` | Allowed only for the authored singleton of a statically known same-unit table |
| Row or column view operand of `tsonEncode` | Rejected |

This policy adds no general first-class table constructor and no root arrays. It permits the language's one authored singleton to cross the TSON boundary without inventing nested table-value semantics.

## Compile-time asset ingestion and singleton ownership

The existing expression intrinsic cannot coherently import a second table value:

```ts
const loaded: Samples = tsonAsset("./samples.tson");
```

That spelling would imply a second independently constructed `Samples` value, while the implemented table language defines `Samples` as both its type and one authored singleton. Treating the asset as merely equal to the inline singleton would be useless and would leave two sources of truth.

The smallest coherent future distinction is therefore declaration-owned initialization:

- a table schema is its name plus ordered column names and element types;
- an inline table declaration supplies that schema and its authored singleton data together, as today;
- an asset-backed table declaration supplies the same schema in source but obtains that declaration's one singleton data set from one compile-time asset;
- the ordinary expression-valued `tsonAsset` remains limited to record and payload-enum values.

CTS-TSON-TABLE-M1 ratifies this exact source spelling in the production parser:

```ts
record table Samples from tsonAsset("./samples.tson") {
    active: boolean;
    score: number;
}
```

This form makes ownership unambiguous: the declaration creates exactly one table type and one singleton, while the source column list is the expected schema. M1 implements it through the existing `Parser`, `TableDeclarationSyntax` family, and table bound/MIR model without a second parser or general table constructor.

For an asset-backed declaration, future ingestion must:

1. resolve the same compilation-unit `$schema` identity required by existing asset ingestion;
2. require the root `TsonTable` identity to equal `schema#TableName`;
3. require exactly the source-declared columns in declaration order;
4. require every column identity to equal `schema#TableName.columnName`;
5. require exact element-schema agreement, including nested array schemas;
6. validate every cell, homogeneous columns, equal lengths, row and cell limits, binary64, and Unicode before publication;
7. lower the complete data into `BoundTableDefinition` and `MirTableDefinition`, extending the closed constant families only for accepted arrays;
8. reject Result-valued or otherwise deferred columns with a table-TSON unsupported-family diagnostic;
9. retain no path, comments, formatting, `TsonValue`, catalog, or reader object in bound/MIR/backend/runtime data; and
10. keep the existing normalized root-relative dependency path and lowercase SHA-256 source-content hash law.

The dependency hash is a deterministic build dependency hash, not table identity and not canonical semantic equality. Authoring comments may change that build hash even though they do not survive semantic projection or runtime encoding.

## Runtime canonical encoding

A future runtime slice may accept only the authored singleton of a statically known table. Binding should create table plan material only when `tsonEncode(TableName)` is demanded.

`MirTsonTablePlan` is justified because the existing `MirTsonArrayPlan` cannot represent nominal table identity, stable column identities, rectangularity, or backend table-carrier access. It belongs inside the existing `MirTsonEncodingPlan` ownership and should contain only immutable validated information:

- compiler-local `MirTableId` used solely to select the correct private carrier;
- stable table identity;
- expected fixed row count for the authored singleton;
- ordered column plan entries containing compiler-local `MirTableColumnId`, name, stable identity, element `MirTsonValuePlan`, and expected length;
- reachable nominal record and enum plans using existing identities;
- canonical static declaration/envelope material; and
- the existing fixed string, array, and output limits plus table bounds.

Compiler-local IDs in the plan never print as exchange data.

The generated writer must:

1. evaluate and validate the table operand exactly once;
2. capture each authoritative column carrier directly in declaration order;
3. capture every column length once and validate all lengths before publishing output;
4. index each cell once in ascending order;
5. serialize cells through the existing Boolean, Number, String, record, enum, and array writers;
6. never construct or enumerate row views;
7. never treat a column view as an array or enumerate its properties;
8. use no reflection, `dynamic`, property enumeration, shape inference, JSON, host serializer, runtime parser, or filesystem access; and
9. return through the existing `string ! TsonEncodeError` flow.

For legitimate generated tables, a rectangularity or carrier mismatch is an impossible backend invariant and must terminate through the existing invariant path without publishing partial canonical output. It is not a new ordinary `TsonEncodeError`. `InvalidUnicode` and `OutputLimitExceeded` remain the only ordinary runtime encoding failures in this slice.

C# should read the table's generated private per-column storage through generated table-specific access owned by the same backend. JavaScript should read the frozen arrays already captured by the table's private creation closures. Neither backend should expose storage or serialize the public meaning of a row/column carrier. Any backend-private mutable array is an implementation detail: source code cannot mutate it, and canonical serialization observes only the logical table data at the one staged call.

## Bounds and mutation are not serialized

TSON serialization concerns immutable table data, not table indexing operations.

`TableBoundsError`, `table[index]`, `column[index]`, their Result values, index classification, and row-view construction do not appear in canonical table TSON. They are language operations over a table after realization. They would appear only if a future TSON Result cell algebra explicitly admitted an actual Result-valued column, which this first slice excludes.

Private C# arrays and private frozen JavaScript arrays do not acquire exchange identity. Mutation history, alias identity, closure identity, class identity, Symbols, provenance registries, and storage allocation are absent from TSON. The immutable Copeland contract and validated logical cells are authoritative.

## JSON compatibility direction

The only approved direction is:

```text
Copeland record table
    -> nominal TSON table
    -> policy-directed columnar JSON
```

JSON is a compatibility lowering, not the schema or value authority. A plain columnar JSON object necessarily loses at least:

- nominal table identity;
- column element-schema evidence for empty columns;
- non-finite binary64 values unless a policy rejects or transforms them; and
- nominal record and enum identity unless a tagging policy preserves it.

M0a does not choose a JSON enum tag, non-finite-number policy, decoder, error API, or host codec. The JSON-first proposals in CTS-TABLE-M0a are historical and superseded by this design.

## Resource bounds

The first implementation should reuse existing TSON limits unless table structure needs one explicit interpretation:

| Resource | Recommended default | Rule |
| --- | ---: | --- |
| Columns per table | 256 | Reuse `MaximumFieldsPerAggregate` |
| Rows per table | 100,000 | Reuse `MaximumArrayLength` |
| Total table cells | 100,000 | Count before nested cell nodes; fail before materialization |
| Nested value/schema depth | 64 | Reuse `MaximumNestingDepth` |
| Total value nodes | 100,000 | Table, columns, cells, and nested values count deterministically |
| String length | 262,144 UTF-16 code units | Reuse `MaximumStringLength` and runtime string limit |
| Canonical source/output | 1,048,576 | UTF-16 source preflight for reading; UTF-8 bytes for runtime output |

The row and column bounds do not permit their Cartesian maximum: total cells is an independent earlier bound. Empty columns still count as column/schema nodes but contribute zero cells. Array-valued cells additionally obey the per-array 100,000-element bound. Implementations must use checked arithmetic when accumulating cells, nodes, and output bytes.

## Diagnostic families

Future table support should add one bounded semantic family rather than dozens of speculative leaf codes:

| Code | Family |
| --- | --- |
| `COPE-TSON-TABLE-0001` | table declaration, root policy, or unsupported nesting |
| `COPE-TSON-TABLE-0002` | table/column schema or stable identity mismatch |
| `COPE-TSON-TABLE-0003` | missing, extra, duplicate, reordered, heterogeneous, or ragged columns |
| `COPE-TSON-TABLE-0004` | invalid cell, unsupported cell family, binary64/Unicode, or canonical table value |
| `COPE-TSON-TABLE-0005` | table column, row, cell, node, depth, string, source, or output limit |

Existing `COPE-LEX-*`, `COPE-PARSE-*`, `COPE-TSON-0001` through `0005`, `COPE-TSON-ASSET-*`, and `COPE-TSON-ENCODE-*` retain ownership of their current layers. A table asset context error stays an asset diagnostic; malformed canonical table content uses the table semantic family; malformed MIR remains a shared backend-entry validation failure. Diagnostics must have deterministic nonempty source spans where source exists and must not expose host exception text.

## Ownership and dependency boundaries

Future implementation ownership is explicit:

- `TsonTable`, table definitions, columns, validation, and canonical reader/printer behavior belong to the existing compiler-host `Copeland.TS.Tson` namespace.
- `BoundTableDefinition` and the closed table constants remain frontend semantic data owned by `Copeland.TS`.
- `MirTableDefinition`, table constants, table access, and table identities remain compiler MIR owned by `Copeland.TS.Mir`.
- `MirTsonTablePlan` belongs to `Copeland.TS.Mir` as demand-created backend-neutral encoding metadata; it does not make TSON semantic objects part of MIR.
- private table/row/column/storage/writer carriers remain owned independently by the C# and JavaScript backends.

No layer may add a dependency on Machina, Aurelian, `DocumentMir`, VD-MIR, reflection, a host serializer, or a universal data/IR abstraction. Table MIR and TSON both model immutable information for different purposes; that similarity is not a reason to merge them.

## Bounded implementation ladder

| Milestone | Scope |
| --- | --- |
| CTS-TSON-TABLE-M0b | Add compiler-host `TsonTable`/schema/column values, existing-parser restricted projection, canonical reader/printer, table-root fixtures, bounds, canonical fixed points, and explicit exclusions. No compiler asset or backend integration. |
| CTS-TSON-TABLE-M1 | Ratify and implement declaration-owned asset-backed table initialization; validate exact schema/identities/shape; lower through table bound/MIR definitions; add the closed array table-constant variant if array cells remain in scope. No runtime encoding. |
| CTS-TSON-TABLE-M2 | Add demand-created `MirTsonTablePlan`, direct authoritative-column C#/JavaScript writers, exact-once observation, limits, and parity for direct authored-singleton `tsonEncode`. |
| CTS-TSON-TABLE-M3 | Close cross-backend and two-generation fixed points, malformed table semantic/asset/plan matrices, artifact hashes, CLI stale-output behavior, topology doctrine, and historical JSON routing. |

M0b should prove the semantic decision before compiler integration. M1 must not overload expression-valued `tsonAsset` into a general table constructor. M2 must not infer a schema from runtime carriers. M3 must not expand into JSON, Result TSON, runtime decoding, nested tables, or general table construction.

## M0a completion boundary

CTS-TSON-TABLE-M0a is complete when this design, the repository audit, the canonical profile, and routing documents agree on the dedicated nominal table variant, stable identity, table-root-only first slice, bounded cell algebra, canonical columnar syntax, declaration-owned asset direction, demand-created runtime plan, limits, diagnostics, ownership, and M0b–M3 ladder.

It changes documentation only. No claim in this document is current implementation authorization beyond the already closed table, TSON core, and TSON array behavior cited as evidence.
