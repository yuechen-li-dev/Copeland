# Copeland TS runtime TSON table encoding (CTS-TSON-TABLE-M2)

**Status:** implemented runtime milestone, closed and ratified by [CTS-TSON-TABLE-M3](copeland-ts-tson-table-closeout-cts-tson-table-m3.md). M2 remains the implementation record; M3 supplies the two-generation closeout, adversarial matrix, retained-hash doctrine, and final routing cleanup.

`tsonEncode(Samples)` is accepted only when `Samples` is the declaration-owned singleton of a statically known table in the current compilation unit. Row and column views, locals or parameters with a table type, nested tables, and Result-valued columns are not encoding roots. The unit must have a valid `$schema`; the returned type remains `string ! TsonEncodeError` with only `InvalidUnicode` and `OutputLimitExceeded` as ordinary errors.

Binding creates no table encoding material merely because a table is declared or accessed. On the first singleton encoding it creates one `BoundTsonTablePlan`, lowered to an immutable `MirTsonTablePlan`. The plan contains the compiler-local table/column IDs used only for private carrier selection, stable table/column identities, declaration order, exact row and column lengths, table bounds, and the existing record, enum, and nested-array value plans. It intentionally contains no assets, paths, syntax, compiler-host TSON values, parser services, reflection metadata, or backend symbol names. Repeated `tsonEncode` uses for the same singleton reuse that validated plan and the existing demanded writer families.

Shared MIR validation rejects malformed table IDs, identities, columns, lengths, table shapes, bounds, nested table value plans, unsupported cell plans, cycles, and encode expressions that do not refer to the proper table plan before either backend writes an artifact. Static invalidity is therefore distinct from runtime representation failure.

Both backends evaluate the operand once, require the authentic declaration singleton, capture every authoritative column once, validate all captured lengths before output, and traverse declaration-order columns and row-order cells. C# uses generated internal table storage accessors; JavaScript uses closed Symbols plus WeakSet provenance. Neither backend creates row views, discovers properties, reflects, parses, uses a filesystem, or serializes carrier identity. Carrier/provenance corruption terminates through the backend invariant path, bypassing typed `except`; Unicode and output-size failures use the existing Result flow. Empty columns emit canonical `[]` and zero-row tables therefore round-trip through the canonical reader/printer.

The emitted document uses the canonical table spelling: `$schema`, ordinal nominal declarations, `record table` column declarations and data, `$value = TableName`, four-space indentation, LF-only output, and one final LF. Cells reuse the existing binary64, string, record, enum, and nested-array writers. The compiler-host canonical reader accepts and reprints the focused runtime output byte-identically.

The retained M2 runtime corpus lives at `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2` and is executed through both backends and the real CLI. Its pinned UTF-8 bytes and SHA-256 values are:

| Artifact | UTF-8 bytes | SHA-256 |
| --- | ---: | --- |
| `empty.obj.ts` | 164 | `A3E967D07DF6730E703718EC84EF42CEE5360682022751AB2FF65B683220088E` |
| `expected.tson` | 1,619 | `77DB4113560183DD4F052F16E8656C0B2B1673FD39373FA6B720E58225F78666` |
| `main.cope` | 2,154 | `5CF1FC80EFAE33F77807298E7EE9F9A10C57565E09715B14515549D35AC78A4A` |
| `main.g.cs` | 34,774 | `B9E4B309991EE59B17016C7595669C1F34F068A0191C7804A6E8DD98EFC6B09C` |
| `main.g.js` | 62,425 | `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9` |
| `main.ts` | 577 | `563EA53F2241964E9E43749B008131301C2F883D6B7ABA01827B40E6ED619064` |
| `samples.obj.ts` | 1,054 | `684FE68C20A7EC25BD24A853198C3C5274CF5BDDC30B19F3468067FC154D55D0` |

Focused parity covers inline and asset-backed tables, primitive/record/enum/nested-array cells, binary64 edge values, Unicode, zero-row tables, repeated C#/Node canonical identity, Result forwarding and propagation, typed `try`/`except` recovery for ordinary encoding errors, and terminal counterfeit-carrier failures through direct JavaScript helper access.

The JavaScript Diagnostic emitter remains the authority. This work does not begin CTS-JS-EMIT production profiles, symbolic names, release printing, helper deduplication, or artifact migration.
