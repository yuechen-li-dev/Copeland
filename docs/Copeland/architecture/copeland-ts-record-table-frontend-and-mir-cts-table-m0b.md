# Copeland TS record-table frontend and MIR (CTS-TABLE-M0b)

CTS-TABLE-M0b introduces immutable authored `record table` definitions to the Copeland TS frontend and canonical Cope MIR. A table is both a nominal type and its one authored singleton value.

```ts
record table Samples {
    x: [1, 2, 3];
    label: string = ["a", "b", "c"];
}
```

Tables use source-order identities `tN`; their owned row types use `tN.row`; columns use `tN.cM`; and row fields derive from their column identity. `Samples.Row` is nominal, while `column T` is structural in `T`.

Table bodies are static definitions, not global executable statements. Their columns are closed, ordered, equal-length, and deeply immutable. Empty columns need explicit element types. The frontend accepts only literals and recursively immutable record, enum, and Result aggregate values; calls, variables, arrays, table access, and executable expressions are rejected.

`table[index]` has type `Table.Row ! TableBoundsError`; `column[index]` has type `T ! TableBoundsError`. `TableBoundsError` is compiler owned and has `InvalidIndex(index: number)` and `OutOfBounds(index: number, rowCount: number)` cases. The future runtime law distinguishes invalid finite-integral indexes from bounds failures; M0b represents, but does not execute, that contract.

Canonical MIR has table definitions, table/row/column types, resolved table references and access operations, and deterministic text output. Table cells use a closed constant hierarchy (literal, record, enum, and Result); a table definition cannot store an executable MIR expression. Shared validation runs before every backend. Valid table MIR is deliberately rejected without artifacts by C# (`COPE-CS-TABLE-0001`) and JavaScript (`COPE-JS-TABLE-0001`) until CTS-TABLE-M1/M2.

Tables, rows, and columns have no equality, construction (except the authored definition), update, mutation, methods, iteration, or host-container semantics.

> CTS-TABLE-JSON must explicitly define the canonical encoding of `Result<void, E>` cells or exclude them from the JSON cell domain. M0b does not decide or implement that codec detail.
