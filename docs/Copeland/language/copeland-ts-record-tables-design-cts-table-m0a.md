# Copeland TS immutable record tables design (CTS-TABLE-M0a)

**Status:** accepted design, ratified through CTS-TABLE-M3. CTS-TABLE-M0a was documentation only; M0b–M2 implemented the source/MIR/C#/JavaScript core and M3 closed adversarial parity. See [the M3 closeout](../architecture/copeland-ts-record-tables-closeout-cts-table-m3.md).

> **TSON-table authority:** [CTS-TSON-TABLE-M0a](copeland-ts-tson-tables-design-cts-tson-table-m0a.md) supersedes every future direct JSON construction, codec, `TableJsonError`, and CTS-TABLE-JSON recommendation in this historical design. The accepted route is `record table -> dedicated nominal TSON table -> policy-directed columnar JSON`. This document remains authoritative for the implemented table language and private runtime semantics, not for interchange.

## Decision

`record table` declares one named immutable authored dataset with closed, declaration-ordered, equal-length columns and one table-owned nominal row type.

```ts
record table SampleTable {
    x: [1, 2, 3];
    y: [4, 5, 6];
}
```

The declaration is not a mutable table class and is not sugar for an array of objects. It introduces both a nominal immutable table schema/type and one authored singleton value named `SampleTable` in expression position. The compiler assigns that type and value a stable table identity and assigns the implicit row shape a different stable row-type identity. Source code names the table type as `SampleTable` in type context and the row type through the table-specific spelling `SampleTable.Row`; this is the only qualified type form introduced by the table ladder and is not a general nested-type system.

```ts
function first(): SampleTable.Row ! TableBoundsError {
    return SampleTable[0];
}
```

The authored singleton is the only source-constructed value in the core table slice. This document historically anticipated a schema-directed JSON deserializer constructing additional immutable values. [CTS-TSON-M0a](copeland-ts-tson-design-cts-tson-m0a.md) supersedes that direct routing: any future external construction must validate through an explicitly approved TSON table extension before compatibility decoding can publish a table. Consequently `SampleTable` is permitted in type annotations and table values may compose through functions, locals, Results, payload enums, and records. This does not create a general constructor or mutable builder.

| Construct         | Shape                       | Identity              | Mutability         | Purpose                  |
| ----------------- | --------------------------- | --------------------- | ------------------ | ------------------------ |
| `record`          | One closed product          | Nominal               | Immutable          | One typed value          |
| `record table`    | Closed equal-length columns | Nominal table and row | Immutable          | Authored tabular dataset |
| Payload enum      | Closed alternatives         | Nominal               | Immutable          | Sum value                |
| Result            | `ok` or `err`               | Structural `T ! E`    | Immutable          | Fallibility              |
| JS object/array   | Dynamic runtime storage     | Reference identity    | Mutable by default | Backend primitive        |
| JSON object/array | Serialized tree             | None                  | Format-dependent   | Interchange syntax       |

## Product boundary

The source looks columnar because the construct is a table. It provides authored immutable program data with validated shape, stable row order, typed cells, and backend-independent access. It does not reinterpret recursive documents, ASTs, presentation trees, or ownership hierarchies as tables.

The core access feature contains no runtime builder, query system, mutation model, key system, database contract, or host ABI. This design originally accepted direct schema-directed columnar JSON. That JSON shape remains historical design evidence, not an implementation authorization: CTS-TSON-TABLE-M0a now selects a dedicated nominal table root, admits array-valued cells, excludes Result-valued columns initially, and permits JSON only as a later compatibility lowering. Private backend storage remains outside every interchange contract.

## Current repository audit

### Evidence and classification

| Area | Finding | Classification | Consequence for tables |
| --- | --- | --- | --- |
| tokens/parser | `record`, brackets, colon, equals, dot, and literals exist. | implemented by M0b | M0b reserves `table`, adds one table-declaration grammar, and adds postfix index syntax. |
| declarations | Top-level functions, enums, records, and global statements parse. Records and enums are predeclared before bodies. Nested records are deliberately rejected. | implemented reusable mechanism | Tables should be top-level and predeclared with stable IDs; nested tables are rejected initially. |
| namespaces | One global symbol scope rejects cross-kind duplicate names. Separate dictionaries resolve enum and record type annotations, while readonly value-like symbols enable `Enum.Case`. | implemented mechanism with a conflated declaration namespace | A table name participates in the existing top-level collision rule. `Table.Row` is resolved only in type context; expression members resolve columns. This does not authorize general namespace separation. |
| qualified names | Expression `Enum.Case` and record value fields resolve specially. Type grammar accepts only identifiers, arrays, parentheses, and Result; it has no dotted type names, namespaces, imports, or general static members. | implemented narrow value mechanism; unresolved qualified-type dependency | M0b adds only `TableName.Row` in type grammar. It must not add arbitrary nested/static types. |
| contextual typing | Expected types flow into variables, returns, call arguments, array elements, branches, Result constructors, enum payload constructors, and contextual record literals. | implemented reusable mechanism | Explicit column types can contextually type record, enum, and Result constants. |
| inferred locals | Ordinary `const` and `let` require annotations. Match payloads and `except` bindings are narrow inferred bindings. | current language law | Row locals use `const row: Table.Row = ...`; table work does not generalize local inference. |
| literals | Boolean, string, and integer-spelled number literals bind. Unary minus is an expression. Decimal, exponent, `NaN`, and infinity source spellings are not implemented even though binary64 is the numeric law. | implemented subset | Table constants use only source forms accepted by the ordinary language; table design does not expand numeric syntax. |
| arrays | Homogeneous `T[]` types, contextual/nonempty literals, values, MIR, and both backend emissions exist. Empty arrays require context; heterogeneous arrays are rejected; element order is preserved. | implemented reusable mechanism | Column literal validation can reuse expression typing principles, but a column is not an array value and must not expose an array ABI. |
| array indexing | There is no index syntax node, bound node, MIR node, member such as `length`, or bounds law. Mutation, holes, spread, iteration, and out-of-range behavior are unresolved. | unresolved dependency | Table indexing must define its own Result-valued law. It does not settle general array indexing. |
| member access | Enum cases and immutable record fields are the only successful forms. Other receivers report a record-access diagnostic. | implemented reusable syntax, incompatible generic diagnostic | Table/column members need dedicated resolved nodes and `COPE-TABLE-*` diagnostics, not textual runtime lookup. |
| records | Stable record/field IDs, complete contextual construction, field reads, immutable `with`, containment validation, dedicated MIR, and private C#/JS representations are closed through CTS-REC-M3. | current language law and reusable nominal-product mechanism | Table rows share product-value laws and field contracts, but remain a distinct table-owned type with no public constructor or `with`. |
| payload enums | Nominal declarations, qualified cases, payload construction, exhaustive `match`, MIR, and both backends exist. | implemented reusable mechanism | Deep constants and row-containing payloads can use existing typed composition after table MIR exists. |
| Result | Structural `T ! E`, `ok`/`err`, match, `?`, `!`, and typed `try`/`except` exist through both backends. | current language law and reusable failure mechanism | Bounds access returns an ordinary Result and composes without a new failure channel. |
| top-level values | Global variable statements bind into `BoundProgram.GlobalStatements`, but `MirLowerer.LowerProgram` omits them. MIR and backends contain functions and type definitions only. | proof-era/incomplete mechanism; incompatible assumption | There is no usable global/module initialization model to reuse. Table data must be canonical static definitions, not arbitrary top-level execution. |
| constants | Local `const` means non-reassignable binding, not compile-time constant. | implemented table-local boundary | M0b has a closed table-local constant validator and parallel bound/MIR trees, not a general evaluator. |
| modules | Imports, exports, namespaces, module initialization, and cross-file name resolution remain unresolved. The CLI compiles one source file to MIR, C#, or JavaScript. | unresolved dependency | Initial table references are single-compilation-unit only; cross-module initialization is not designed here. |
| MIR | Cope MIR owns Copeland TS enums, records, functions, arrays, Results, and control flow. Stable record IDs and shared validation are established. | implemented reusable semantic boundary | Dedicated table definitions/types/access nodes belong in `Copeland.TS.Mir`; backend containers do not. |
| C# backend | Arrays are emitted as mutable CLR arrays; records are sealed get-only classes; Result/enum helpers and MIR validation are established. | implemented proof backend with reusable nominal enforcement | CLR arrays may be private storage only. No array or mutable container may escape as a column. |
| JavaScript backend | Arrays are emitted as ordinary arrays; records use private tokens/symbol slots and frozen null-prototype values; Results/enums use compiler-owned representations. | implemented backend with reusable privacy patterns | Any dense array storage must be private and frozen; public objects with user-named arrays are forbidden. |
| fixtures/corpus | Curated language law lives under `Language/Valid` and `Language/Invalid`; stage/backend snapshots live under their own corpus roots. Language fixtures contain no generated artifacts. | current repository law | M0b adds table fixtures atomically with syntax, semantics, validation, and MIR. M0a adds none. |
| CLI/topology | The CLI composes frontend, Cope MIR, C#, and JavaScript explicitly. Cope MIR is BCL-only and separate from DocumentMir and VD-MIR; validators reject universal compiler abstractions. | current architecture law | Table support stays within the TS lane. No Machina/Aurelian/shared data IR is created. |

### Historical findings

The reachable repository and Git history contain no authoritative Oct table contract, dataframe language, relational Copeland API, columnar Copeland MIR, or prior `record table` implementation. The only direct Copeland table statements before this milestone defer `record table` to a separate CTS-TABLE ladder. CTS-REC-M3 explicitly identifies M0a as design-only.

Machina has UI/layout rows and dispatch tables; Aurelian has TOML tables, shader artifact JSON, world/runtime structures, and separate VD-MIR doctrine. Copeland Markdown can serialize its own AST/MIR diagnostics as JSON. These are consumer-specific documents, host data structures, or artifact formats. They are historical/contextual evidence that rows, trees, JSON, and IRs have different owners, not reusable Copeland TS table law.

The absence of Oct artifacts is itself a bounded audit result: no Oct semantics can be promoted from this repository. If external Oct material is supplied later, it may inform a separately reviewed change but cannot silently override this design.

| Historical material | Classification |
| --- | --- |
| CTS-REC documents deferring `record table` | current boundary evidence |
| Machina UI/layout `UiRow` and table-shaped authoring | unrelated product model; incompatible as language law |
| Machina dispatch table | unrelated runtime lookup mechanism |
| Aurelian TOML/JSON artifact handling | serialization/host experiment, not language semantics |
| DocumentMir/VD-MIR | independent IRs protected by topology law |
| Oct/dataframe/columnar language material | not found in reachable repository/history; unresolved external evidence |

## Source grammar and typing

### Declaration grammar

M0b reserves `table` and adds exactly these column forms:

```text
table-declaration := "record" "table" Identifier "{" table-column+ "}"
table-column      := Identifier ":" "[" cell-list? "]" ";"
                   | Identifier ":" Type "=" "[" cell-list? "]" ";"
```

Canonical examples are:

```ts
record table Samples {
    x: [1, 2, 3];
    label: string = ["a", "b", "c"];
}

record table EmptyPoints {
    x: number = [];
    y: number = [];
}
```

After the colon, `[` selects inferred form; otherwise a type followed by `=` selects explicit form. The `record table` header keeps this unambiguous with ordinary record fields. Explicit types are permitted for empty and nonempty columns. There is no `name = [...]`, `name: T[]`, inferred `name = [...]`, row-object form, or alternative table literal spelling.

The column list requires at least one entry. Each column name is unique and case-sensitive. A column named `length`, `count`, `rows`, `columns`, `at`, or `Row` is valid. `Row` in type context still denotes the table-owned row type; `Table.Row` in expression context denotes a column named `Row` when one exists.

### Column inference and exactness

An inferred nonempty column takes the exact type of its first cell after ordinary binding. Every later cell must have the same type under existing `TypeFacts.AreEquivalent`; no union, numeric widening, coercion, or structural-record inference occurs. An untyped empty column is rejected.

An explicit column uses its declared element type as contextual type for every cell. It may be empty. Every cell must be assignable to exactly that type under current Copeland rules. The explicit type denotes the cell type, not `T[]` or `column T`.

Sparse holes, spread, computed names, shorthand, implicit missing cells, and dynamic column expressions are rejected. A parser recovery accident is not an accepted sparse table. Missing cells are never filled with `null`, `undefined`, a default, or an absence case.

### Table laws

| Concern        | Law                                        |
| -------------- | ------------------------------------------ |
| Column names   | Closed and unique                          |
| Column types   | Homogeneous and exact                      |
| Column lengths | Equal                                      |
| Row order      | Stable and language-visible                |
| Column order   | Declaration order                          |
| Empty table    | Allowed only with explicitly typed columns |
| Zero columns   | Rejected                                   |
| Missing cells  | Rejected; never implicit null/undefined    |
| Row identity   | Nominal row type, no hidden primary key    |
| Mutation       | Not supported                              |
| Equality       | Deferred                                   |

The first authored column establishes the authored singleton's row count. Every other column must have exactly that count. An authored zero-row declaration is valid only when every column is explicitly typed. A future deserialized zero-row value is valid for any expected table schema because all element types are already resolved by that declaration. Every table value's row count becomes fixed when that value is constructed. Column order is source declaration order; each column's element order determines row association. Neither order implies a key, uniqueness, or sorting contract.

## Authored constant policy

Table bodies are canonical authored data, not initialization code. M0b validates a closed table-local constant grammar and lowers accepted cells directly into typed MIR constants. It does not execute user functions or introduce a general constant evaluator.

Accepted constant cells are recursive combinations of:

- boolean, string, and currently accepted number literals;
- unary `-` applied directly to an accepted number literal;
- zero-payload enum values;
- payload-enum constructors whose payloads are accepted table constants;
- contextual record literals whose every field is an accepted table constant;
- contextual `ok(...)` or `err(...)` values whose payload is an accepted table constant.

Aggregate cells requiring context use an explicit column type. A named enum constructor may provide its own nominal type, but inference still rejects ambiguity. Eligible aggregate types must be deeply immutable under current Copeland law. Array-valued cells, or records/enums/Results containing arrays or any future mutable type, are rejected in the first slice.

The following are not constants: arithmetic or Boolean folding, ordinary function calls, variables or named `const` references, references to another table, table access, column access, `with`, `match`, `if`, `try`/`except`, propagation, unwrap, assignments, or arbitrary expressions. Unary minus is syntax around one literal, not general folding. No cross-table references means no cross-table initialization cycles. Aggregate containment cycles continue to follow existing record/type rejection and receive a table diagnostic when encountered in authored data.

Backends emit the authored singleton as deterministic static data from MIR definitions. They do not run a hidden module initializer. This design historically named direct JSON deserialization as a future construction path; that claim is superseded by CTS-TSON-TABLE-M0a's declaration-owned table asset direction. Named constants, source constructors, builders, and arbitrary runtime table construction require separate designs.

## Identity, row types, and composition

For each declaration the compiler creates this semantic model:

```text
TableId
NominalTableType
ImplicitRowTypeId
OrderedColumnIds
Exact column element types
Fixed row count
Ordered typed authored constants per column
```

Two table declarations always have different table and row identities, even when names, types, values, and lengths match.

```ts
record table ScreenPoints {
    x: [1, 2];
    y: [3, 4];
}

record table WorldPoints {
    x: [1, 2];
    y: [3, 4];
}
```

`ScreenPoints.Row` and `WorldPoints.Row` are not assignable. The source spelling is resolved to a stable row-type ID, never compared textually after binding.

Table rows are immutable record-like products: closed fields, declaration order, exact field types, stable IDs, and resolved field reads. They are not independently constructible records. Brace construction and `with` are rejected for a table-owned row type so source code cannot manufacture a value that appears retrieved from the dataset. A retrieved row has its table's nominal row type but no persistent row identity, hidden primary key, or source-visible membership token. Equal cell values at two positions are permitted.

The table name itself is also a nominal type. Values of `SampleTable` have the same closed columns, row type, and fixed rectangularity law as the authored singleton. The core implementation constructs only the authored singleton; any future external value must follow the separately approved TSON-table ownership law. Tables with identical schemas remain nominally incompatible.

The dedicated `TableName.Row` spelling permits rows in:

- explicitly typed locals and function parameters/returns;
- arrays where existing array rules permit them;
- Result success or error positions;
- payload-enum fields;
- ordinary record fields;
- `match` arms and `try`/`except` value flow.

This is normal type composition after binding. It does not make row construction public. `match` does not gain row patterns, and `try`/`except` does not catch bounds errors as host exceptions; it handles the ordinary Result selected by `?`.

## Row access and bounds

Bracket access on any value of the nominal table type is Result-valued:

```text
SampleTable[index] : SampleTable.Row ! TableBoundsError
```

The table expression and then the index expression are each evaluated exactly once, left-to-right. The index must have type `number`. A valid index is a finite integral binary64 number `i` such that `0 <= i < rowCount`; `-0` denotes row zero. NaN, either infinity, and fractional values produce `err(TableBoundsError.InvalidIndex(index))`. Negative integral values and integral values greater than or equal to row count produce `err(TableBoundsError.OutOfBounds(index, rowCount))`.

`TableBoundsError` is one compiler-owned nominal payload enum available when table indexing is used:

```ts
enum TableBoundsError {
    InvalidIndex(index: number),
    OutOfBounds(index: number, rowCount: number),
}
```

Its semantic identity is compiler-owned; a user declaration with that reserved name is rejected. This exact two-case value contract is language-visible, while backend helper/type names and layout are private. A statically obvious invalid or out-of-range numeric index remains a well-typed Result value rather than a diagnostic. Constant and dynamic indexing therefore have one type and one failure law.

```ts
const row: SampleTable.Row = SampleTable[index]?;
const x: number = row.x;
```

The `?`, `!`, Result `match`, and typed `try`/`except` laws remain unchanged. Row retrieval may materialize a product or return a backend-private view. Allocation, storage identity, and caching are unobservable.

## Column access

`SampleTable.x` resolves by stable table and column ID and has source type `column number`. The receiver may be any expression of that nominal table type. `column T` is a dedicated immutable type constructor, not a generic library type and not `T[]`. The type grammar permits it wherever ordinary types are permitted, including function parameters, returns, locals, Results, payload enums, and record fields.

Columns with equivalent element types are compatible regardless of declaring table. Column identity selects the authored data but is not part of `column T` type equivalence. The view is ordered, immutable, fixed-length, and never exposes a JS or CLR array.

Column indexing is also Result-valued:

```text
columnValue[index] : T ! TableBoundsError
```

It uses exactly the row-index validity, error cases, and exactly-once index evaluation above. The column receiver is evaluated once. Direct `SampleTable.x[index]` is merely the composition of resolved column access and column indexing; a backend may fuse it without changing behavior.

Iteration and metadata access are deferred. In particular, M0b does not reserve `length`, `count`, `rows`, `columns`, or `at`, does not add methods, and does not add a free `count` intrinsic. A later metadata milestone may add a structurally separate or free intrinsic after name-resolution policy is approved. All common words remain legal column names.

## Immutability, membership, and equality

The table definition, column sequence, row count, cell values, row values, and column views are immutable. Assigning to a table, a column member, a column element, or a row field is rejected with table-specific diagnostics. A `let` binding may be rebound to a different row or column value of the same type; rebinding does not mutate either value.

Row position is an access coordinate, not an entity identity. Duplicate rows are legal. An `id` column is ordinary data. No primary key, uniqueness, foreign key, join, or hidden row ID exists. Sorting, if later designed, creates a distinct value/view rather than mutating authored order.

Table, row, and column `==`/`!=` are unsupported. No structural equality, reference identity, hashing, ordering, or deduplication leaks from either backend. A future equality design must explicitly address table and row nominal identity, row/column order, binary64 NaN and signed zero, nested records, enums, Results, future absence values, and dataset size.

## Historical default schema-directed JSON proposal

> **Superseded routing:** this section records the table ladder's original unimplemented JSON proposal. CTS-TSON-M0a requires `table value -> TSON extension -> JSON compatibility lowering` and `JSON -> validated untyped JSON -> schema-directed TSON extension -> table value`. [CTS-TSON-ARRAY-M0a](copeland-ts-tson-arrays-design-cts-tson-array-m0a.md) establishes typed homogeneous arrays only as a prerequisite for a future columnar table document; it does not establish the table or Result TSON law. The exact table and Result TSON laws, JSON enum tag shape, error family, and codec API must be re-approved in that later work; this section does not authorize direct implementation.

The table design accepted the following JSON shape before the native TSON layer was designed. It was never implemented in M0a or the core M0b/M1/M2 access slices. Any future serialization still operates on logical schema and cells, never on a C# container, JavaScript object layout, private token, brand, or frozen storage object.

For a table whose declared columns are `x`, then `y`, the canonical JSON shape is:

```json
{
  "x": [1, 2, 3],
  "y": [4, 5, 6]
}
```

The outer value is one plain JSON object. Its properties are emitted in table declaration order. Each property value is one dense JSON array in row order. There is no schema envelope, table name, row type, element-type tag, row count, backend metadata, or nominal token in the document. Empty column element types and all nominal identity come from the expected table declaration.

Canonical cell encoding is recursively schema-directed:

| Copeland cell type | JSON encoding |
| --- | --- |
| `boolean` | JSON Boolean |
| `string` | JSON string |
| `number` | finite JSON number; negative zero is emitted as `-0` |
| ordinary `record` | plain object with exact fields in record declaration order |
| payload enum | `{ "case": "CaseName", "payload": { ... } }`, with exact payload fields in declaration order |
| Result | `{ "case": "ok", "value": ... }` or `{ "case": "err", "value": ... }` |

The enum `payload` object is present and empty for a zero-payload case. These encodings are the table JSON cell codec; they do not independently authorize general-purpose record, enum, or Result serialization APIs. `null` is not valid for any current cell type. NaN and infinities are outside the JSON domain. Arrays, table values, column views, and table-owned rows are not eligible cell types in the initial table constant contract.

Serialization is semantically schema-driven and fallible, with a future operation type equivalent to `Table -> string ! TableJsonError`. A valid table containing only JSON-domain values serializes deterministically. A non-finite number introduced by a future construction mechanism returns `InvalidNumber(column, row)` rather than becoming `null`, a string, or backend-specific output.

Deserialization is semantically equivalent to `string + expected nominal Table type -> Table ! TableJsonError`. The API spelling and library/host entry point remain deferred, but its validation law is fixed:

- parse JSON while retaining enough information to reject duplicate object properties;
- require exactly one outer object;
- require exactly the declared column-name set, with no missing, extra, or duplicate properties;
- require every column property to be a dense JSON array;
- require all arrays to have equal lengths;
- validate every cell recursively against the declared column type and JSON-domain mapping;
- reject missing/extra/duplicate aggregate fields, unknown enum cases, wrong payload fields, invalid Result cases, `null`, non-finite/non-number host values, and all coercions;
- reconstruct column order, element types, row type, and table nominal identity from the expected declaration;
- publish the immutable table only after the entire document validates.

Input object property order is not identity: a deserializer may accept declared columns in another textual order, then canonicalize them to schema declaration order. Array order remains row order. An empty JSON column is valid because its type comes from the expected schema. Deserialization never infers a structural or anonymous table type and never chooses a declaration from the JSON property set.

`TableJsonError` is a future compiler/library-owned nominal error family with at least `InvalidSyntax`, `ExpectedObject`, `ColumnMismatch`, `ExpectedColumnArray(column)`, `RaggedColumn(column, expected, actual)`, `InvalidCell(column, row)`, and `InvalidNumber(column, row)`. Exact source API spelling is deferred with the serialization implementation; implementations must not substitute host exceptions for this typed failure contract.

Alternate formats, schema envelopes, row-oriented JSON, arbitrary JSON-to-table inference, public host APIs, JSON DOM exposure, streaming, versioning, Arrow, binary formats, and database transport remain deferred.

## Bound and Cope MIR architecture

M0b adds dedicated source, symbol, bound, and MIR concepts. It reuses generic nominal-product validation helpers only where ownership remains clear.

```text
MirTableDefinition
  MirTableId
  Name
  MirTableRowTypeId
  Ordered MirTableColumnDefinition values
  RowCount

MirTableColumnDefinition
  MirTableColumnId
  Name
  ElementType
  Ordered MirTableCellConstant values

MirTableRowType
  MirTableRowTypeId
  DisplayName

MirTableType
  MirTableId
  DisplayName

MirColumnType
  ElementType

MirTableRowAccessExpression
  TableExpression
  IndexExpression
  ResultType

MirTableColumnAccessExpression
  TableExpression
  ColumnId
  ColumnType

MirColumnElementAccessExpression
  ColumnExpression
  IndexExpression
  ResultType
```

Names should follow existing repository conventions if implementation reveals a clearer prefix, but the responsibilities and distinct stable IDs are required. The table definition owns its row definition. `MirTableRowType` must remain distinct from `MirRecordType`: both are nominal products, but record construction/`with` are valid only for independently declared records. Row field access may use a generalized resolved product-field contract or a row-specific access node; it must preserve the distinction and must not fall back to source text.

`MirTableType` is nominal by table ID. `MirColumnType` is structural in its element type. Table row and column access take a table expression, validate its nominal table ID against the resolved access, and evaluate that receiver once. A first-class column element access likewise takes a column expression so parameters/locals work. The authored singleton lowers as a reference to its definition; future external construction is governed by the TSON-table design rather than a direct JSON decoder.

`MirTableCellConstant` is a closed table-owned constant tree with literal, record, enum, and Result variants. Aggregate variants carry stable record/field or enum/case identities plus declaration-ordered typed children. It is not a `MirExpression`, performs no calls, and is not a compiler-wide data IR. Shared MIR validation checks:

- unique nonempty stable table, row, and column IDs and names;
- one or more columns, exact element types, equal stored counts, and declared row count;
- constant-tree/type agreement and deep immutability eligibility;
- complete referenced record/enum definitions and prohibited cycles;
- valid table/column references and exact result/column/row types;
- `number` indexes and ordinary Result error identity;
- table-owned row nominality through every nested type position;
- absence of construction, update, mutation, or equality nodes for table-owned values.

Core table MIR contains logical data and operations only. It contains no JS arrays, CLR arrays, JSON documents, DOM values, SQL, database handles, or physical storage promises. A future JSON milestone may add dedicated schema-codec operations, but never a backend storage object or universal JSON/data IR. Cope MIR remains independent of Machina, Aurelian, DocumentMir, and VD-MIR.

## Backend recommendations

### Backend boundary

| Language law      | MIR responsibility    | C# recommendation           | JavaScript recommendation             |
| ----------------- | --------------------- | --------------------------- | ------------------------------------- |
| Nominal table     | Stable table ID       | Generated table definition  | Private table token                   |
| Nominal row       | Stable row type ID    | Generated row type/view     | Private row token                     |
| Closed columns    | Stable column IDs     | Fixed private columns       | Fixed private columns                 |
| Rectangularity    | Validated row count   | Equal fixed storage lengths | Equal frozen storage lengths          |
| Row access        | Bounds-aware node     | Result-valued access        | Result-valued access                  |
| Column access     | Resolved column ID    | Private readonly view       | Private frozen view                   |
| Immutability      | No mutation nodes     | No mutable exposure         | Frozen private storage                |
| Equality deferred | Unsupported operation | No source-visible equality  | No source-visible identity comparison |

### C#

Generate one deterministic nominal table value carrier with private readonly per-column storage and a fixed per-value row count, plus one private static readonly authored singleton. Generate a distinct nominal row class/readonly view contract with get-only field access and a private immutable column-view wrapper. Access validates the binary64 index once and constructs the existing ordinary Result representation with the compiler-owned bounds enum. Any future external initializer must publish the same private carrier only after complete TSON schema and table-shape validation.

The storage may use ordinary arrays internally, but it is never returned or typed as the source column. Do not promise `ImmutableArray<T>`, `ReadOnlySpan<T>`, a particular array kind, `DataTable`, LINQ, reflection, `dynamic`, dictionaries, row allocation, or CLR layout. Use ordinary BCL/AOT-compatible code and demand-driven helpers. Invalid table MIR produces no artifact.

### JavaScript

Use one private table-type token, one private row-type token, and private per-column tokens. Private dense arrays may store cells if they are frozen before publication. Publish the authored singleton and any future decoded value only through compiler-owned frozen/null-prototype table, row, and column-view representations with exact own slots and brand validation. User column names select stable private tokens and never become a public object ABI.

Evaluate the index once, validate finite-integral/range rules explicitly, and construct the same Result/bounds enum value as C#. Do not rely on JavaScript array out-of-range `undefined`, prototype methods, coercion, mutable array exposure, or public property names. Emit helpers only when used; invalid MIR produces no artifact.

Both backends must agree on table/row/column nominal checks, row and column order, count, cells, bounds cases/payloads, immutability, and exactly-once evaluation. They may differ in row materialization, storage containers, memory layout, and optimization.

## Diagnostics plan

M0b reserves this bounded family. Parser recovery may continue to use parser diagnostics, but semantically recognizable alternative/conflicting table forms receive the table family rather than succeeding accidentally.

| Code | Contract |
| --- | --- |
| `COPE-TABLE-0001` | invalid declaration, placement, column syntax, sparse/missing element, or conflicting alternative spelling |
| `COPE-TABLE-0002` | duplicate table or collision with an existing top-level declaration |
| `COPE-TABLE-0003` | zero-column table |
| `COPE-TABLE-0004` | duplicate column |
| `COPE-TABLE-0005` | untyped empty column |
| `COPE-TABLE-0006` | heterogeneous inferred column or unresolved element inference |
| `COPE-TABLE-0007` | explicit cell type mismatch |
| `COPE-TABLE-0008` | ragged column length |
| `COPE-TABLE-0009` | unsupported cell expression or mutable cell type |
| `COPE-TABLE-0010` | recursive/cyclic authored data |
| `COPE-TABLE-0011` | invalid or bare table use, including unsupported table annotation/construction |
| `COPE-TABLE-0012` | invalid or unknown column |
| `COPE-TABLE-0013` | non-number index type |
| `COPE-TABLE-0014` | attempted table mutation |
| `COPE-TABLE-0015` | attempted column mutation |
| `COPE-TABLE-0016` | attempted row mutation or construction/update |
| `COPE-TABLE-0017` | unsupported table, row, or column equality/inequality |
| `COPE-TABLE-0018` | nominal table-row mismatch |
| `COPE-TABLE-0019` | unsupported or unresolved row/column source annotation |

Diagnostics name source tables/columns/types where useful and anchor the responsible declaration, cell, access, index, assignment, or operator. They never expose generated storage or helper names.

## Fixture and proof plan

M0a adds no fixtures. M0b adds ordinary filesystem evidence under:

```text
tests/Copeland/Copeland.TS.Tests/Language/
  Valid/tables/*.cl-valid.ts
  Invalid/tables/*.cl-invalid.ts
```

Valid fixtures cover inferred nonempty columns; explicitly typed nonempty columns; typed zero-row tables; different element types across columns; row and column Result access; propagation/match handling; row fields; two same-shaped independent tables; `Table.Row` and `column T` in function/local/Result/enum/record positions; legal metadata-like column names; and ordering.

Invalid fixtures cover duplicate/zero columns; ragged lengths; heterogeneous cells; untyped empty columns; explicit mismatch; unsupported expressions and mutable/array cells; sparse/spread/missing cells; invalid index types; unknown columns; bare/general table values; table/column/row mutation; row construction/`with`; nominal row mismatch; equality; reserved `TableBoundsError` collision; invalid qualified types; and every alternative/conflicting declaration spelling.

M0b also adds focused syntax, bound, MIR text/validator, exactly-once, and deliberate backend-boundary evidence in existing stage-specific corpus/test locations. It must not mark a `.cl-valid.ts` table accepted until the complete source-to-validated-MIR contract exists. CTS-TABLE-M1 adds C# backend/runtime corpus and constant, boundary, nominal, immutability, and deterministic emission proofs; CTS-TABLE-M2 adds the corresponding JavaScript realization and Node evidence. CTS-TABLE-M3 ratifies parity and closeout; serialization remains deferred.

## Deferred boundaries

The following are explicitly outside the first core table ladder unless a later milestone is separately approved: source constructors and general runtime builders; mutable tables; insertion/deletion; shape change; computed/derived columns; filtering; projection; joins; grouping; aggregation; sorting; indexes; keys; foreign keys; uniqueness; transactions; lazy/reactive query plans; databases; dataframe APIs; LINQ; SQL; iteration; metadata/count APIs; row destructuring/patterns; table patterns; equality/hashing/ordering; general generics; general nested/static types; recursive types; named compile-time constants; module initialization; cross-table references; alternate/schema-envelope/row-oriented JSON; arbitrary JSON inference; Arrow/binary serialization; schema versioning; streaming; DOM binding; public JS/.NET host ABI; and compiler-wide/shared data IR.

The DOM remains a presentation tree derived from application data. The historical direct columnar JSON proposal is superseded; any future JSON is a compatibility projection from a validated nominal TSON table and does not determine table storage or mutation law.

## Refined CTS-TABLE ladder

| Milestone | Scope and convergence condition |
| --- | --- |
| CTS-TABLE-M0a | Documentation-only audit and implementation-ready language/MIR/backend design. |
| CTS-TABLE-M0b | Atomically implement declaration grammar, constant validation, rectangularity, stable table/row/column identity, `Table.Row`, `column T`, Result-valued row/column indexing, `TableBoundsError`, diagnostics, filesystem fixtures, canonical MIR/text/validation, CLI integration, and deliberate no-artifact rejection from both backends. |
| CTS-TABLE-M1 | Implement C# private columnar realization plus runtime, bounds, order, nominality, immutability, and deterministic artifact proofs. |
| CTS-TABLE-M2 | Implement JavaScript private columnar realization, Result-valued access, closed constants, and Node/runtime evidence. |
| CTS-TABLE-M2 | Implement JavaScript private frozen columnar realization plus Node brand/privacy/bounds/order proofs. |
| CTS-TABLE-M3 | Cross-backend parity, exactly-once and binary64-index stress, representation privacy, doctrine ratification, artifact stability, and closeout. |

The bounded follow-up is the separately approved CTS-TSON-TABLE ladder. Only after it closes may a JSON compatibility milestone choose policy, errors, and backend realization. It must consume logical TSON table definitions/values and must not reopen the private storage boundary.

No smaller M0b split is recommended. Declaration without row/column access would create unusable data; access without stable types and validated MIR would create temporary semantics; parser-only fixtures would falsely claim support. The dedicated type forms, bounds enum, constants, and access nodes are one atomic frontend contract. Backend realization remains separable because M0b can reject validated table MIR explicitly and artifact-free. JSON implementation remains separable because the accepted schema codec consumes stable logical table values after core backend parity.

## M0a completion boundary

CTS-TABLE-M0a is complete when this document, the [canonical language profile](copeland-ts-language-profile.md), and the [migration audit](../../migrations/cts-table-m0a-record-table-audit.md) agree on the nominal table-schema plus authored-value model, nominal nameable rows, typed immutable columns, deeply constant rectangular data, Result-valued access, backend-private columnar representation, dedicated MIR, diagnostics/fixtures, and deferred query/builder/interchange boundaries. The later [CTS-TSON-TABLE-M0a design](copeland-ts-tson-tables-design-cts-tson-table-m0a.md) is authoritative for interchange.

No table syntax, compiler behavior, MIR node, runtime helper, fixture, backend lowering, project, solution, or tool change follows from this document alone.
