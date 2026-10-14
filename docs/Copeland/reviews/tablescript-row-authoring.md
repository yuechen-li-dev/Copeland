# TableScript row authoring and calculation locals

Date: 2026-10-13. Scope: schema-directed row initializers and per-row `const`
bindings in derived relations. Both capabilities work through the existing compiler
and the café workbook's real C# and CLI execution paths.

## Motivating change

Suppliers are now authored as individual records after a typed schema. Products
retain columns because their repeated policy cells read better by column. The
workbook's purchasing policy and cost live together in one `PurchasePlan`; a
per-row `const orderUnits` replaces the separate `Restock` relation and its carried
columns. No ordering assumption was introduced between Products and Stock.

The qualification driver compares every canonical purchase row against the preceding
column-authored, two-relation fixture. All six rows agree. Baseline remains three
orders, 67 units, 31,210 cents. Editing espresso stock by key from 4 to 12 still gives
two orders, 47 units, 12,710 cents.

## Implementation

- The parser retains schema plus row expressions. A shared source projection feeds
  each property's original expression into the existing typed column initializer
  binder. It preserves repetition metadata, static constants, nominal identity,
  key uniqueness, and declared references.
- Row shape validation diagnoses missing, duplicate, and unknown fields before
  projection. Mixed row/column/asset forms are rejected. Empty typed data and
  counted row repetition are supported.
- Locals bind in source order over source/join aliases and earlier locals, with
  ordinary inference or explicit types. Output column names are independent.
  Recursive purity validation closes the old gap where a nested call could pass
  the root-only check. Generated names avoid authored identifiers, including match
  payload bindings.
- Bound definitions and MIR retain local expressions. C# emits one binding per
  local in the relation's row loop after joins. Provenance traverses the local
  dependency graph, with visited symbols to avoid repeatedly expanding shared
  branches. Copies through locals retain key/reference metadata.
- The existing post-static visitor now visits derived locals and projections;
  otherwise a `static` local reached MIR unresolved. Static and Result-match
  calculations use their existing owners and have an execution regression test.
- CLI set/add/delete/import preserve row-shaped authoring and use the same
  candidate validation, dry run, and atomic publication path. Repeated rows split
  around individual edits; CSV round trips retain column order and numeric
  formatting. Appending and deleting rows handle trailing commas.

## Evidence

Reproduce from the repository root:

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File tools/copeland-tablescript-row-authoring/qualify.ps1
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1 -OutputDirectory artifacts/tablescript-row-authoring/cafe
pwsh -NoProfile -File tools/copeland-tablescript-key-edits/qualify.ps1 -OutputDirectory artifacts/tablescript-row-authoring/key-edits
```

The added tests cover authored C# and all three JavaScript profiles, immutable
`with`, keys/references, reordered properties, record/enum cells, empty/zero/repeated
rows, invalid shapes/types, chained locals and transitive provenance, declaration
order, inference, name hygiene, nested effects, static/Result semantics, and CLI
mutations/CSV/rejection without source changes.

Final validation on .NET SDK 10.0.401: the full solution build passed with zero
warnings/errors. All eight test projects passed: 2,357 tests, zero failures and
zero skipped tests, including 32 new compiler/CLI cases. `git diff --check` passed.

The row authoring driver records 46 checks, including full before/after purchase
row equality, generated local count, exact cost dependencies, a keyed supplier edit
reaching its three related products, row add/delete, CSV round trip, and safe
rejection of deleting a referenced supplier. The café driver records 66 checks
and 13 compiled consumer assertions. The keyed edit driver retains its 96 checks
across both Stock orderings.

Artifacts are under `artifacts/tablescript-row-authoring/`: build/test logs,
generated MIR/C#, before/after query JSON, supplier edit/CSV evidence, summaries,
and the café/key-edit subdirectories. The previous workbook is a reproducible
fixture under `tools/copeland-tablescript-row-authoring/inputs/Workbook.columns.ts`.

## Boundaries

Runtime storage remains immutable and columnar; row source does not introduce
mutable objects or `with` row updates. Every row supplies the complete schema;
defaults, partial rows, dynamic spreads, and row-template instantiation are outside
this step. Bare row fill has no extent anchor and stops at `COPE-INIT-0005`.

Locals precede output columns and use `const`; declarations interleaved with output
columns, imperative statement bodies, effects, and runtime calls are unsupported.
Unused locals evaluate in order. Changing numeric policy or recalculating a global
derived relation from a `with` snapshot is outside this step.

Authored row tables have JavaScript execution evidence. Derived tables retain the
existing JavaScript stop `COPE-JS-DERIVE-0001`; no browser calculation claim is made.
TSON documents remain column-authored and explicitly reject host row syntax with
`COPE-TSON-TABLE-0001`, rather than projecting the schema's absent inline cells as
an empty table. No native Excel run was added.

The next bounded language step is typed summary authoring; snapshot-relative
calculation and JavaScript derived-table execution remain separate work.
