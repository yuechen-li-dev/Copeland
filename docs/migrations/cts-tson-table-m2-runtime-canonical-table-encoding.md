# CTS-TSON-TABLE-M2 runtime canonical table encoding

CTS-TSON-TABLE-M2 adds demand-created runtime canonical encoding for the authored singleton of a same-compilation-unit `record table`.

The implementation introduces `BoundTsonTablePlan`/`BoundTsonTableColumnPlan`, lowers them to immutable `MirTsonTablePlan`/`MirTsonTableColumnPlan`, validates them through shared MIR checks, and emits direct C# authoritative-column access plus JavaScript Symbol/WeakSet carrier access. Inline array-valued table constants are accepted consistently with declaration-owned table assets. Focused binding proves singleton-only demand creation and rejects copied table variables, row views, column views, and cell expressions.

The retained runtime corpus is `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2`. It covers an asset-backed encoded table plus a zero-row asset-backed table in the same program, with primitive, record, enum, nested-array, Unicode, and binary64 cells. The pinned UTF-8 bytes/SHA-256 values are:

| Artifact | UTF-8 bytes | SHA-256 |
| --- | ---: | --- |
| `empty.obj.ts` | 164 | `A3E967D07DF6730E703718EC84EF42CEE5360682022751AB2FF65B683220088E` |
| `expected.tson` | 1,619 | `77DB4113560183DD4F052F16E8656C0B2B1673FD39373FA6B720E58225F78666` |
| `main.cope` | 2,154 | `5CF1FC80EFAE33F77807298E7EE9F9A10C57565E09715B14515549D35AC78A4A` |
| `main.g.cs` | 34,774 | `B9E4B309991EE59B17016C7595669C1F34F068A0191C7804A6E8DD98EFC6B09C` |
| `main.g.js` | 62,425 | `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9` |
| `main.ts` | 577 | `563EA53F2241964E9E43749B008131301C2F883D6B7ABA01827B40E6ED619064` |
| `samples.obj.ts` | 1,054 | `684FE68C20A7EC25BD24A853198C3C5274CF5BDDC30B19F3468067FC154D55D0` |

Focused runtime/CLI evidence proves:

- C#/Node canonical parity for inline, asset-backed, and zero-row table roots.
- Canonical-reader acceptance and canonical-printer byte identity for the emitted table document.
- Result forwarding, `?` propagation, and typed `try`/`except` recovery for ordinary table encoding errors.
- Terminal invariant bypass for counterfeit JavaScript carriers and malformed C# private storage.
- Fresh CLI emission, repeated byte-identical MIR/C#/JavaScript artifacts, execution of the exact CLI-generated C# and JavaScript, stale-output preservation on failure, and absence of asset paths or host-TSON/runtime-parser dependencies in emitted artifacts.

No runtime parser, filesystem access, reflection, dynamic schema discovery, nested table cells, Result-valued table cells, general table construction, JSON, package changes, or JavaScript emission-profile work was introduced. M3 remains the explicit boundary for exhaustive closeout and retained corpus/hash evidence.
