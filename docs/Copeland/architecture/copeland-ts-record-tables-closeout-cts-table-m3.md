# Copeland TS immutable record-table closeout (CTS-TABLE-M3)

**Status:** closed core table ladder. This ratifies CTS-TABLE-M0a through M2; it does not add a table feature.

## Ratified laws

`record table` declares one authored singleton and its nominal immutable table schema. Its closed, declaration-ordered columns have one validated row count. `Table.Row` is nominal per table; `column T` is an immutable structural view, not an array. Rows are table-and-index views over authoritative columns. Tables and rows with the same visible shape remain distinct. Source construction of rows, table/row/column mutation, `with`, equality, mutable cells, recursive table-cell types, and `Result<void, E>` cells remain rejected.

Table definitions contain only recursive `MirTableConstant` values (literal, record, enum, and non-void Result), never executable `MirExpression`. Table, row, column, and row-field identities are canonical and shared validation rejects malformed identities, shape, constants, accesses, and bounds Result types before either backend emits an artifact.

Access returns ordinary `Result` flow: table indexing is `Table.Row ! TableBoundsError`, column indexing is `T ! TableBoundsError`, and column selection returns `column T`. `-0` is zero; finite integral in-range indexes succeed; `NaN`, either infinity, and fractions return `InvalidIndex`; negative finite integrals and values at or above the count return `OutOfBounds`. This includes binary64 `9007199254740991`: it is classified before C# converts to `int`, so no overflow, wrap, truncation, or host exception occurs.

## Private realizations

| Concern | C# | JavaScript |
| --- | --- | --- |
| Table/row/column identity | distinct sealed carriers | distinct private Symbols and frozen null-prototype carriers |
| Storage | declaration-ordered private arrays | declaration-ordered frozen arrays captured by private closures |
| Rows | owning table plus checked integer index | owning table plus checked index Symbols |
| Public mutable storage | none | none; columns are not arrays |
| Bounds/fallibility | `CopeResult`, no CLR exception | frozen Result, no ordinary `catch`/`finally` |

Neither representation is an ABI, host interop format, or serialization contract. Counterfeit/invariant failure and postfix-unwrap panic remain terminal and bypass typed `except`.

## Cross-backend evidence

`TableCloseoutParityTests` emits one canonical program through C# and Node twice. Its pinned Node trace is:

```text
28755,true,10,20,true,1000,1000,1000,1000,2003,2003,2003,2003,2000,3000
```

The matrix covers empty/one/multi-column tables; singleton/column/row/cell access; first/middle/last values; strings and escaping, booleans, `-0`, immutable records, payload enums, Result and nested Result/enum constants; matching, propagation, unwrap, typed recovery and handler propagation; return/argument/conditional/logical/match positions; receiver/index/argument source order; unselected branches; every bounds category; and repeated deterministic execution. JavaScript Node representation tests additionally prove frozen null-prototype table/row/column carriers, fixed descriptors, no public backing array, non-array columns, counterfeit rejection, and same-shape row isolation. C# structural tests prove distinct sealed carriers, private arrays, no record equality, and row projection from columns.

The audit found two production defects. JavaScript rejected validated general unary `-` and `!`; its backend now emits them while preserving operand staging, with a focused regression. C# emitted an unparenthesized assignment when it was a unary/binary operand, changing source precedence; operand emission now parenthesizes assignments, with a non-table regression. No table representation or MIR compatibility path was added.

## Scope retained outside the ladder

JSON, `toJSON`, `fromJSON`, parsing, stringification, codecs, serialization, and host interop are **unimplemented**. In particular, `JSON.stringify` of a private JavaScript carrier is neither canonical nor supported. [CTS-TSON-M0a](../language/copeland-ts-tson-design-cts-tson-m0a.md) superseded the old direct CTS-TABLE-JSON routing, and [CTS-TSON-TABLE-M3](copeland-ts-tson-table-closeout-cts-tson-table-m3.md) now closes the dedicated nominal, table-root-only TSON ladder. Its future JSON default is explicitly columnar object-of-arrays; any row-oriented compatibility requires a separately named policy. Builders, mutation, row construction, queries, row-oriented storage, equality/hashing, keys, metadata/iteration, relational/dataframe operations, and all alternate formats remain excluded.

The frontend fixture inventory remains 3 valid and 13 invalid table programs. `COPE-TABLE-0001` through `0019` each retain focused non-empty-span coverage. Shared malformed table-MIR cases are rejected as `COPE-CS-0002`/`COPE-JS-0002` with no artifact; no production `COPE-CS-TABLE-0001` or `COPE-JS-TABLE-0001` remains.

See the [M3 migration record](../../migrations/cts-table-m3-immutable-record-tables-closeout.md) for validation and artifact evidence.
