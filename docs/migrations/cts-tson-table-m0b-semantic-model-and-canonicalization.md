# CTS-TSON-TABLE-M0b semantic model and canonicalization

## Outcome

CTS-TSON-TABLE-M0b implements the M0a-selected dedicated nominal table family in the compiler-host TSON subsystem. Both authoring and canonical text reuse the production parser. Immutable schema-evidenced columns, table-root projection, nominal catalog resolution, exact canonical printing, resource limits, diagnostic families, filesystem fixtures, and a pinned fixed-point corpus are present.

## Implementation inventory

Production changes are confined to `src/Copeland/Copeland.TS/Tson`: `TsonTable`, `TsonTableColumn`, `TsonTableSchema`, `TsonTableColumnSchema`, opaque table/column identities, table-aware reader/catalog validation, table canonical printing, table limits, and incremental UTF-8 output accounting.

Tests add `TsonTableFeatureTests`, valid and invalid table fixtures, and the representative corpus. The topology validator now permits only the compiler-host table semantic family, continues to prohibit a second parser and TSON dependencies in MIR/backends, and explicitly rejects `MirTsonTablePlan` in M0b.

The zero-column ambiguity is resolved by rejection: without a column, serialized data cannot distinguish zero rows from any other row count. Typed zero-row tables with one or more empty columns are canonical.

Implementation exposed one fixture-matrix mismatch in the prompt: production `record table` syntax is simultaneously the schema and its column data, so a separate textual value cannot omit, add, or reorder columns relative to that schema. Such states are unrepresentable in `.obj.ts`/`.tson`; focused semantic-constructor tests prove that missing and reordered column sequences are rejected. Duplicate names and ragged textual columns retain filesystem fixtures. This preserves the M0a declaration-owned canonical grammar instead of inventing a second value syntax solely to manufacture malformed fixtures.

## Representative corpus

| Artifact | UTF-8 bytes | SHA-256 |
| --- | ---: | --- |
| `representative.tson` | 1,145 | `450DF822E63C4A1F681D98796D707EA6AAB35D1B4D533CDD479B49BB2394256A` |

The corpus covers primitive columns, nominal record and payload-enum cells, nested array cells, positive and negative zero, a finite fraction, infinities, normalized NaN, escaping, non-ASCII Unicode, and a valid surrogate pair. A separate typed-empty filesystem fixture proves empty-column schema retention because rectangularity forbids mixing a zero-length column into a populated table.

## Explicit exclusions

No compiler table-asset ingestion, bound/MIR table lowering, `MirTsonTablePlan`, `tsonEncode` support, C# or JavaScript table realization, runtime decoder, filesystem runtime access, JSON, package/version change, CLI mode, or generated `.cope`, `.g.cs`, or `.g.js` corpus artifact is part of this milestone.

## Validation

Focused semantic, fixture, corpus, core TSON, array, record-table, solution, topology, dependency, duplication, leakage, documentation, hash, and whitespace checks are recorded in the final milestone report. M1 remains declaration-owned compile-time table asset ingestion only.
