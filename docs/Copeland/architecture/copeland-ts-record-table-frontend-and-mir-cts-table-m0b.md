# Copeland TS record-table frontend and MIR (CTS-TABLE-M0b)

CTS-TABLE-M0b introduces immutable authored `record table` definitions to the Copeland TS frontend and canonical Cope MIR. A table is both a nominal type and its one authored singleton value.

```ts
record table Samples {
    x: [1, 2, 3];
    label: string = ["a", "b", "c"];
}
```

Tables use source-order identities `tN`; their owned row types use `tN.row`; columns use `tN.cM`; and row fields derive from their column identity. `Samples.Row` is nominal, while `column T` is structural in `T`.

Table bodies are static definitions, not global executable statements. Their columns are closed, ordered, equal-length, and deeply immutable. Empty columns need explicit element types. The frontend accepts only boolean/string/number literals (including signed zero) and recursively immutable record, enum, and Result aggregate values; calls, variables, arrays, table access, and executable expressions are rejected. The bound hierarchy is `BoundTableConstant` with literal, record, enum, and Result variants; lowering produces the parallel `MirTableConstant` hierarchy. Neither table-storage path contains `MirExpression`.

`table[index]` has type `Table.Row ! TableBoundsError`; `column[index]` has type `T ! TableBoundsError`. `TableBoundsError` is compiler owned and has `InvalidIndex(index: number)` and `OutOfBounds(index: number, rowCount: number)` cases. The future runtime law distinguishes invalid finite-integral indexes from bounds failures; M0b represents, but does not execute, that contract.

Canonical MIR has table definitions, table/row/column types, resolved table references and access operations, and deterministic text output. Table cells use a closed constant hierarchy (literal, record, enum, and Result); a table definition cannot store an executable MIR expression. Shared validation runs before every backend. CTS-TABLE-M1 realizes valid table MIR through C#; JavaScript continues to reject it without an artifact using `COPE-JS-TABLE-0001` until CTS-TABLE-M2.

Tables, rows, and columns have no equality, construction (except the authored definition), update, mutation, methods, iteration, or host-container semantics.

`Result<void, E>` is deliberately excluded from the M0b table-cell domain: a table Result cell must have a closed success or error payload, and `void` has no table constant representation. Source declarations involving that mutable/non-cell payload domain report `COPE-TABLE-0009`; malformed MIR is rejected by shared validation. This is not parser recovery.

CTS-TABLE-M0b is closed at the source-to-validated-MIR boundary. `TableDiagnosticInventoryTests` covers `COPE-TABLE-0001` through `COPE-TABLE-0019`; shared malformed-constant cases are independently rejected by both backends before their valid-table rejection boundary. The curated fixture inventory is 3 valid and 13 invalid table programs. The pinned representative MIR is 1661 UTF-8 bytes with SHA-256 `62897D4142128179A9036545CBA4A0BDB4E3EB74ACF9D722E71E90A0EF93234F`; established non-table corpus snapshots remain stable. M1 C# realization is next.

> CTS-TABLE-JSON must explicitly define the canonical JSON encoding of `Result<void, E>` cells or exclude them from the JSON cell domain.
