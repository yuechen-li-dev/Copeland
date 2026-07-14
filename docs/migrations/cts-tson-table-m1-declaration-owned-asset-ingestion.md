# CTS-TSON-TABLE-M1 declaration-owned asset ingestion migration

## Baseline and result

Work began from clean revision `599c40f600cda9aab3bf3864e90fa097e4ce408a` on `main`, tracking `origin/main` with zero divergence. M1 implements declaration-owned compile-time table asset initialization through the existing production parser, compiler asset resolver, TSON table reader, closed table pipeline, shared validator, and both backends.

The ratified syntax is:

```ts
record table Samples from tsonAsset("./samples.tson") {
    active: boolean;
    score: number;
}
```

`from` is contextual. No grammar conflict required another spelling.

## Delivered changes

- `TableAssetClauseSyntax` and asset-aware `TableDeclarationSyntax`/`TableColumnSyntax` preserve declaration, clause, path, column, and type spans.
- Binder resolves one literal asset only for its owning table declaration, requires `$schema`, validates exact stable table/column/reachable nominal schemas, and projects `TsonTable` cells into ordinary closed bound table definitions.
- `BoundTableArrayConstant` and `MirTableArrayConstant` carry defensively copied closed nested-array values with explicit element types.
- Lowering and deterministic `.cope` text erase every TSON/asset detail.
- Shared MIR validation handles closed arrays and malformed length/depth/node/type/alias cases before either backend.
- C# emits typed arrays; JavaScript emits frozen ordinary array carriers.
- JavaScript Result payload validation now recognizes `MirArrayType`, a general production defect exposed by array-valued table indexing.
- Filesystem fixtures, focused parser/binder/MIR/backend/CLI tests, shared malformed-MIR cases, topology enforcement, and a pinned representative corpus were added.

## Behavioral evidence

Object TypeScript and canonical TSON inputs produce identical MIR. Comment-only `.obj.ts` changes produce a different dependency hash and the same MIR. C# and Node agree on nested arrays, nominal values, negative-zero bits, Unicode, bounds, and singleton identity. CLI MIR/C#/JavaScript output is repeated byte-identically; invalid input creates no fresh output and preserves stale output bytes.

The complete corpus hashes are recorded in the [architecture authority](../Copeland/architecture/copeland-ts-tson-table-assets-cts-tson-table-m1.md).

## Exclusions

M1 adds no runtime table encoder, `MirTsonTablePlan`, runtime parser/filesystem dependency, general table constructor, expression-valued table asset, JSON, Result-valued TSON cell, nested table, table mutation, package/version change, commit, push, or publication.

## Validation record

Validation completed on 2026-07-14:

| Lane | Result |
| --- | --- |
| Focused table-asset frontend | 8 tests passed, about 0.2 s |
| Focused C#/Node runtime parity | 1 test passed, about 1 s |
| `Copeland.TS.slnx` | build passed with 0 warnings; 478 frontend, 158 C#, and 83 JavaScript tests passed (719 total) |
| `Copeland.slnx` | build passed with 0 warnings; 832 tests passed including 31 CLI and 82 Markdown tests |
| `JointTaskForce.slnx` | build passed with 0 warnings; 2,085 tests passed in about 23 s wall-clock |
| Topology and dependencies | Copeland TS topology and 27-project dependency-boundary validation passed; topology includes project-cycle validation |
| Static and artifact checks | duplicate-parser, MIR/backend table-TSON leakage, generated runtime path/parser/filesystem leakage, corpus byte/hash stability, documentation, and `git diff --check` passed |

No separate Machina slow lane or NativeAOT publish lane was run; no shared Machina infrastructure or runtime dependency changed.
