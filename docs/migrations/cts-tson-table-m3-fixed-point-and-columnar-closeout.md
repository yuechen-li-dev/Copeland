# CTS-TSON-TABLE-M3 fixed-point and columnar closeout

## Outcome

CTS-TSON-TABLE-M3 is closed. It ratifies the authoritative columnar law for semantic tables, canonical TSON, declaration-owned assets, bound/MIR plans, generated C# and JavaScript carriers, and runtime `tsonEncode`. The only code changes are closeout tests: a semantic anti-row regression and a real second-generation table asset proof. No production defect was found, so no production file or retained corpus artifact changed.

The complete contract and ledger are in [the architecture closeout](../Copeland/architecture/copeland-ts-tson-table-closeout-cts-tson-table-m3.md). The C#/Node generation-one and generation-two output is byte-identical, UTF-8 without BOM, and exactly one LF terminated. Node is `v26.2.0`.

## Scope ratification

- Canonical TSON is declaration-ordered columns of arrays; row views are derived and never serialized.
- The eventual default JSON compatibility mapping is columnar object-of-arrays. JSON itself is not implemented.
- `InvalidUnicode` and `OutputLimitExceeded` remain the only ordinary encoding failures. Carrier and plan corruption remain terminal backend invariants.
- CTS-JS-EMIT production work, helper deduplication, naming changes, imported runtimes, parser/filesystem work, reflection, `dynamic`, new table syntax/values, and package changes were not added.
- The retained 62,425-byte M2 Diagnostic JavaScript artifact is unchanged and remains a CTS-JS-EMIT benchmark.

## Files changed

- Tests: `tests/Copeland/Copeland.TS.Tests/TsonTableFeatureTests.cs`; `tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/TsonEncodeRuntimeTests.cs`.
- Documentation: this migration record; the architecture closeout; the documentation index and routing/status records.
- Production, corpus, generated artifacts, tooling, packages, and project files: unchanged.

## Validation

Focused table semantic/fixed-point and C#/Node runtime tests passed: 39 tests, including the M3 generation-two proof. Full solution and topology/dependency validation are recorded by this closeout after the documentation updates. The Machina slow lane is excluded because no shared Machina infrastructure changed.
