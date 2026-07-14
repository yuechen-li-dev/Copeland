# CTS-TABLE-M0b record-table language and MIR migration

CTS-TABLE-M0b advances the design recorded by CTS-TABLE-M0a into source syntax, binding, canonical MIR, deterministic `.cope` text, fixture coverage, and deliberate backend rejection.

The implemented syntax is `record table Name { column: [cells]; typed: T = [cells]; }`, `Name.Row`, `column T`, and postfix indexing. Table identities are deterministic by authored declaration order (`tN`, `tN.row`, `tN.cM`). The table name is both its nominal type and singleton value. Bound and MIR definitions own closed literal/record/enum/Result constant trees rather than executable expression nodes.

The bounded `COPE-TABLE` diagnostics reserve declaration and rectangularity errors (`0001`–`0008`), constant eligibility (`0009`–`0010`), table/access rules (`0011`–`0016`), equality and nominal row mismatch (`0017`–`0018`), and unresolved row/column annotations (`0019`). Source fixtures under `Language/Valid/tables` and `Language/Invalid/tables` establish the initial filesystem contracts.

Backends validate first. Canonical valid table MIR produces no C# or JavaScript artifact and receives the respective table-unsupported diagnostic. Malformed table MIR must fail shared validation instead.

CTS-TABLE-M1 and CTS-TABLE-M2 remain responsible for executable C# and JavaScript realization. JSON remains deferred.

## Closeout evidence

`Result<void, E>` table cells are excluded by `COPE-TABLE-0009`; M0b has no zero-payload table Result variant. The future JSON decision remains explicit: CTS-TABLE-JSON must define its canonical encoding or exclude the cell domain.

The shared MIR validator checks primitive payload kind, record/field identities and completeness, duplicate fields, enum/case/payload shape, Result payload type and the void-success exclusion, column element agreement, and rectangular row counts. Invalid table MIR returns shared `COPE-CS-0002`/`COPE-JS-0002` diagnostics with no artifact; only valid canonical table MIR reaches `COPE-CS-TABLE-0001` or `COPE-JS-TABLE-0001`.

The bounded diagnostic inventory is: `0001` declaration/placement, `0002` collisions/compiler-owned bounds name, `0003` zero columns, `0004` duplicate column, `0005` untyped empty column, `0006` inferred heterogeneity, `0007` explicit mismatch, `0008` raggedness, `0009` non-constant or mutable cell, `0010` recursive authored data, `0011` invalid table use, `0012` invalid column, `0013` non-number index, `0014` table mutation, `0015` column mutation, `0016` row construction/update/mutation, `0017` table/row/column equality, `0018` nominal-row mismatch, and `0019` unresolved row/column annotation. `TableDiagnosticInventoryTests` gives every code an exact focused source case with a non-empty source span. Source diagnostics remain distinct from malformed-MIR diagnostics.

The fixture inventory is 3 valid and 13 invalid files under `Language/*/tables`. The valid contracts cover inferred and explicit columns, typed zero-row tables, record/enum/Result constants, singleton/table/row/column composition, Result-valued indexing, `TableBoundsError`, independent same-shaped tables, legal member-looking column names, and a static negative index. The invalid contracts cover declaration/shape, constant eligibility, annotations/access, indexing, mutation, equality, nominal rows, and recursive authored data.

The representative `constants-and-access.cl-valid.ts` MIR is emitted twice byte-identically. Its UTF-8 length is 1661 bytes and SHA-256 is `62897D4142128179A9036545CBA4A0BDB4E3EB74ACF9D722E71E90A0EF93234F`. It is not the externally supplied `308076dfb8f8fb7cd5604fc9d996f4439f0af07f8133e9d051a6cf6dea4c4200`: no checked-in source-to-hash mapping exists for that earlier value, while this closeout hash is pinned by a focused regression test over the representative closed-constant/access fixture. Existing non-table `.cope`, `.g.cs`, and `.g.js` corpus snapshot tests remain byte-stable.

The CLI writes only after successful compilation. A failed backend compilation preserves an existing output; success is determined by the command result, not the existence of an older file. Focused CLI tests prove both fresh and SHA-256-checked stale paths for C# and JavaScript. M1 C# table realization is next; M0b does not implement C#/JavaScript table execution, JSON, queries, builders, count/iteration, or array indexing.
