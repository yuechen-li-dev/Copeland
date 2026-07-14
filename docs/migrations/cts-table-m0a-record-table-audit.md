# CTS-TABLE-M0a record-table audit

## Result

CTS-TABLE-M0a establishes the documentation-only design for immutable authored columnar record tables after the closed CTS-REC immutable-record ladder. The implementation-ready authority is [Copeland TS immutable record tables design](../Copeland/language/copeland-ts-record-tables-design-cts-table-m0a.md), and the [canonical language profile](../Copeland/language/copeland-ts-language-profile.md) records the accepted direction without claiming implementation.

No parser, compiler, Cope MIR, backend, runtime, fixture, test, project, solution, CLI, or tooling behavior changes in this milestone.

## Starting state and method

- **Starting revision:** `d2bbe303969ae371146356f68db8c100b95a6f82` (`Ratify immutable record closeout across Copeland docs`).
- **Starting branch:** `main`.
- **Starting worktree:** clean (`git status --short` produced no paths).
- **Method:** current source/document inspection plus `rg`, repository file inventories, and bounded Git history searches. No checkout, reset, restore, build output, package installation, or worktree rewrite was used.

The audit inspected the canonical profile and completed record doctrine; lexer/tokens/parser/syntax; declaration and scope handling; type symbols and equivalence; binder/context propagation; bound nodes; lowering; Cope MIR/validator/writer; C# and JavaScript emitters; CLI composition; language fixtures/corpus; and topology/dependency validators. Historical searches covered Copeland, Machina, Aurelian, migrations, and reachable Git history for tables, rows, columns, JSON, dataframe, relational, columnar, and Oct material.

## Current findings

| Area | Finding | Classification |
| --- | --- | --- |
| arrays | Homogeneous `T[]` literals/values and exact element typing exist; empty literals need context and mixed elements are rejected. | implemented reusable mechanism |
| indexing | No postfix bracket parser, bound/MIR access node, bounds behavior, length/count, or array indexing/mutation law exists. | unresolved dependency |
| globals | Top-level statements bind, but their `BoundProgram.GlobalStatements` are omitted by MIR lowering and both backends. | proof-era/incomplete mechanism; incompatible with runtime table initialization |
| constants | `const` prevents rebinding only. There is no compile-time expression classifier/evaluator, named constant graph, folding, or cycle model. | unresolved dependency |
| qualified names | Enum cases and record fields use narrow dotted expression resolution; dotted types, namespaces, modules, general static members, and nested types do not exist. | reusable syntax plus unresolved type-name dependency |
| namespaces | Top-level declaration kinds share collision rules, while type resolution also uses enum/record maps. | current implementation constraint |
| local inference | Ordinary local declarations require annotations; only pattern/handler bindings infer narrow types. | current language law |
| records | Immutable nominal closed products, stable IDs, field access, `with`, MIR validation, and private C#/JS representations are closed. | current language law and reusable product mechanism |
| enums/Result | Nominal payload enums, exhaustive match, structural Result, propagation, unwrap, and typed handling exist through both backends. | implemented reusable composition/failure mechanism |
| MIR/topology | Cope MIR is BCL-only and TS-lane-specific; DocumentMir and VD-MIR are deliberately independent. | current architecture law |
| fixtures/CLI | Language fixtures require frontend-to-MIR truth; the CLI explicitly selects MIR/C#/JS artifacts. | current repository law |

The numeric runtime law is binary64, but current source number literals are integer-spelled only. Negative values are unary expressions. The table constant contract therefore permits literal syntax already accepted by the language and unary minus on a literal, without expanding numeric syntax or adding arithmetic folding.

## Historical findings

No authoritative Oct table semantics, Copeland dataframe/relational API, columnar Cope MIR, `record table` implementation, or JSON-to-table language mapping was found in the reachable repository or Git history. Existing CTS-REC documents only defer record tables to this separate ladder.

Machina UI/layout rows and dispatch tables, Aurelian TOML/JSON artifact structures, Copeland Markdown JSON dumps, DocumentMir, and VD-MIR are product- or lane-specific mechanisms. They are useful evidence that trees, layout rows, serialized artifacts, and compiler IRs have distinct ownership, but they are incompatible sources of Copeland TS table law. No shared table infrastructure is recommended.

## Accepted language decisions

- `record table` declares both one nominal immutable table schema/type and its authored singleton value, not a mutable table class.
- The declaration has a stable table ID and a table-owned stable nominal row-type ID.
- The authored value is referenced as `SampleTable` in expressions; `SampleTable` is also its nominal table type and the row type is `SampleTable.Row` in type context.
- `TableName.Row` is a dedicated qualified-type rule, not a general nested-type system.
- Table values may appear in annotations and ordinary type composition. The core slice source-constructs only the authored singleton; future schema-directed JSON deserialization may construct another immutable value of the same nominal type.
- Same-shaped tables have incompatible row types. Rows compose in locals, functions, arrays, records, payload enums, Results, `match`, and `try`/`except` through the explicit row spelling.
- Rows are not publicly constructible and do not support `with`; retrieval gives the row type but no hidden persistent row identity or primary key.
- Canonical columns use `name: [values];` or `name: Type = [values];`. Explicit types are allowed for empty and nonempty columns; no alternative spellings are accepted.
- Inferred columns are nonempty, homogeneous, and exact. Explicit columns contextually type every cell.
- One or more columns are required. Authored zero-row declarations are valid only when every column is explicitly typed. A deserialized zero-row value is valid for any expected schema because its element types are already resolved. Ragged data and implicit missing/null/undefined cells are rejected.
- Table bodies accept only a closed deeply immutable constant tree: literals, unary-negative literals, enum values, contextual record values, and contextual Result values. Calls, references, folding, `with`, arrays/mutable aggregates, cross-table references, and initialization effects are rejected.
- Row access is `Table[index] : Table.Row ! TableBoundsError`.
- Column access is `Table.column : column T`; `column T` is a nameable intrinsic immutable type, not an array or general generic.
- Column indexing is `columnValue[index] : T ! TableBoundsError`.
- Indexes are binary64 numbers. Finite integral values in `[0, count)` succeed; `-0` is zero; NaN/infinities/fractions return `InvalidIndex`; negative or too-large integers return `OutOfBounds`.
- `TableBoundsError` is a compiler-owned nominal payload enum with `InvalidIndex(index)` and `OutOfBounds(index, rowCount)`. Statically obvious bounds failures remain Result values.
- Iteration and metadata/count access are deferred. Common column names are not reserved.
- Table/row/column mutation and equality are rejected. Position is not identity; no key, uniqueness, sorting, or relational behavior is inferred.
- Default serialization is schema-directed columnar JSON: one plain object whose exact properties are declaration-ordered dense column arrays in row order.
- Serialization reads logical cells, never private C#/JavaScript storage. Nominal identity, row shape, column order, and empty-column types are not encoded in JSON.
- Deserialization requires an expected nominal table type, rejects duplicate/missing/extra columns, non-array columns, ragged lengths, invalid/null/non-JSON cells, and coercion, then restores identity and types from the expected declaration before publishing an immutable value.
- Primitive, record, payload-enum, and Result cells use canonical recursive schema mappings. Non-finite numbers are rejected; signed zero serializes as `-0`.
- Alternate formats, schema envelopes, row-oriented JSON, inference from JSON shape, Arrow, streaming/versioning, DOM exposure, and public host APIs remain deferred.

## Architecture recommendations

Cope MIR should add a nominal table type plus table, row, and column stable IDs; ordered definitions; fixed row count; a closed typed table-cell constant tree; a distinct table-row type; structural `column T`; receiver-based row access, column access, and column-element access nodes; and shared validation. Table-owned rows may reuse generic nominal-product validation helpers, but they must remain distinct from independently constructible records.

The C# backend should emit one nominal table definition, private static readonly column storage, a private immutable column view, nominal row values/views, and Result-valued bounds handling. Internal arrays are permitted but never exposed. The JavaScript backend should use private table/row/column tokens, frozen private dense storage, frozen/null-prototype branded views, and explicit bounds checks. Neither backend promises physical container, row allocation, public names, or ABI.

Core MIR contains no CLR/JS storage objects, JSON DOM, SQL, database schemas, or shared universal data representation. A future JSON milestone may add dedicated logical codec operations, never backend layouts.

## Diagnostics and fixtures

The design allocates `COPE-TABLE-0001` through `COPE-TABLE-0019` for declaration/syntax, duplicate names/columns, zero columns, empty inference, heterogeneity/mismatch, raggedness, unsupported/cyclic constants, invalid table/column/index access, table/column/row mutation, equality, nominal mismatch, and unsupported annotations.

M0b adds filesystem-backed `Language/Valid/tables/*.cl-valid.ts` and `Language/Invalid/tables/*.cl-invalid.ts` coverage plus focused syntax/bound/MIR/validator evidence. M1 and M2 add C# and JavaScript backend realization evidence. M0a adds no fixtures or tests.

## Refined ladder

1. **CTS-TABLE-M0a:** documentation-only design and audit.
2. **CTS-TABLE-M0b:** atomic source-to-validated-MIR implementation, including constants, rectangularity, nominal rows, nameable columns, Result bounds behavior, diagnostics, fixtures, and CLI path.
3. **CTS-TABLE-M1:** private columnar C# realization and runtime proofs.
4. **CTS-TABLE-M2:** private frozen columnar JavaScript realization and Node proofs.
5. **CTS-TABLE-M3:** cross-backend parity, exactly-once/bounds stress, privacy, doctrine, deterministic artifacts, and closeout.

After core closeout, a separately approved CTS-TABLE-JSON ladder should implement the accepted schema-directed default codec and typed `TableJsonError` behavior across both backends. Query operations, general builders, mutation, metadata/iteration, equality, keys, alternate formats, database/dataframe APIs, module initialization, general nested types/generics, and shared IR remain deferred.

## Files changed

- `docs/Copeland/language/copeland-ts-record-tables-design-cts-table-m0a.md`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/migrations/cts-table-m0a-record-table-audit.md`

## Validation

Validation completed on 2026-07-13:

| Check | Result |
| --- | --- |
| Exact changed-path and extension check | Passed: exactly the three Markdown files listed above; no production, test, fixture, project, solution, CLI, or tooling path changed. |
| Changed-document relative links, Markdown tables, fenced code blocks, referenced paths, and terminology | Passed. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 27 production projects; no exceptions. |
| `git diff --check` | Passed; Git emitted only its working-copy LF-to-CRLF advisory for the modified canonical profile. |

Full builds/tests are intentionally excluded unless final diff inspection finds a non-document change.
