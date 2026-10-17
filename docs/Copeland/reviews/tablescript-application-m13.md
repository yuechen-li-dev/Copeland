# TableScript application validation and frontend execution — M13

The café now supports immutable runtime stock edits, application-owned domain
errors, and actual React recalculation through the JavaScript backend. The same
application functions execute through C#. Frontend code holds drafts and presents
returned data; it does not own purchasing formulas, relation scheduling, or stock
policy.

## Ownership and implementation

The [canonical workbook](../../../samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts)
remains the only authored purchasing policy. This pass exports its existing total
and supplier-summary records/functions so the application can reuse them through
normal local imports.

The new [application module](../../../samples/copeland-ts/tablescript-application-m13/src/Application.ts)
contains:

- `WorkbookInputs` and `WorkbookView`, ordinary immutable records.
- `setOnHand`, which rejects a negative count with
  `StockError.NegativeCount(productId, units)` and maps a missing table key to
  `StockError.UnknownProduct(productId)`.
- `calculate`, which explicitly supplies Products, Stock, and Suppliers to the
  existing derived plan and returns projected product rows and typed summaries.
- `applyStockEdit`, which composes those operations as a typed Result.

The [frontend reducer](../../../samples/copeland-ts/tablescript-application-m13/src/Editor.ts)
only edits drafts, invokes `applyStockEdit`, and either publishes one returned view
or retains the accepted view alongside its error. The [React presentation](../../../samples/copeland-ts/tablescript-application-m13/src/Cafe.tsx)
uses Copeland `View`, `stream`, `state`, and `on`. Markup is confined to
presentation. The foreign numeric control owns transient DOM text and checks the representation
of an `int`;
it passes negative integers to the application unchanged. Native form validity
prevents malformed text from submitting an older draft.

The compiler adds `table.withRow({ keyName: value }, { ordinaryField: value })`.
The selector and patch resolve to nominal table/column identities. Operands
execute once, in source order, including missing-key calls. Successful updates
preserve the original snapshot, row count, row order, keys, and references.
Missing keys return `TableKeyError.NotFound`. Key/reference patches, derived or
unkeyed receivers, malformed selectors, and wrong/duplicate fields have directed
compile diagnostics. Whole-column `with` retains its static initializer contract.
See the [language contract](../language/copeland-ts-table-application.md).

JavaScript now emits derived factories from the existing relation MIR, executes
its validated recalculation schedule, and materializes typed sum/count summaries.
There is no frontend formula parser or alternate dependency engine. Tests compare
C# and every JavaScript emission profile for shared dependency graphs, shuffled
joins, grouping, filters/captures, empty summaries, branching, evaluation order,
immutable dynamic edits, and 32-bit integer summary overflow.

The real module boundary exposed missing table export/dependency metadata and
per-file table/Result runtime identities. Those are fixed in the compiler/project
runtime, rather than merging the application into the UI. Module-private names
remain inaccessible. Immutable tables are admitted in component state; mutable
storage and host handles retain their existing rejection.

## Motivating workflow

| Scenario | Cents | Units | Orders | Result |
| --- | ---: | ---: | ---: | --- |
| Initial workbook | 31,210 | 67 | 3 | Six products; stock joins by identity despite its shuffled order. |
| Product 100: 4 → 12 on hand | 12,710 | 47 | 2 | Espresso order disappears; suppliers 20 and 30 remain in first-seen order. |
| Product 100: negative count | 12,710 | 47 | 2 | Typed field-specific error; accepted rows remain unchanged. |
| Product 999: unknown key | 12,710 | 47 | 2 | Typed unknown-product error; accepted rows remain unchanged. |
| Then product 103: 5 → 20 | 4,960 | 16 | 1 | Uses the latest accepted snapshot and retains product 100's edit. |
| Reset | 31,210 | 67 | 3 | Restores the original view. Global workbook totals were never modified. |

The C# application probe and the browser's public application export return the
same 13 values, including typed error payload checks and unchanged global totals.
The browser exercises the mounted page under React StrictMode at 1280 and 390
pixels in Diagnostic, Symbolic, and Production profiles. All six workflows pass
with no console warnings, page errors, or failed HTTP responses. The small-screen
table has its own horizontal scroll region and keyboard focus; the page does not
overflow. Error text and supporting prose remain readable. Desktop and mobile
screenshots were inspected.

## Validation and reproduction

```powershell
./samples/copeland-ts/tablescript-application-m13/run.ps1
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
```

- Full solution build: zero warnings/errors.
- Full solution tests: 2,442 passed; zero failed or skipped.
- New edit/application regression cases: 22, including malformed MIR, generic
  patch specialization, module imports, generated-name collisions, and invalid
  integer callbacks. Existing recalculation/summary tests now execute JavaScript
  parity rather than asserting the old backend stop.
- Sample tooling: strict `tsc --noEmit`, all three ESM/layout builds, C# application
  probe, and six real-browser workflows pass.
- Original café qualification: 66 checks, existing C# consumer assertions,
  canonical `csharp-relation-plan` executor, unchanged baseline/scenario totals.
- `git diff --check`: clean. Frozen legacy Zig lanes were not run because this
  pass changes the managed Copeland TS compiler/backends.

Review evidence: [compiler/source hashes](evidence/tablescript-application-m13/compiler-report.json),
[browser observations](evidence/tablescript-application-m13/browser-report.json),
and [validation summary](evidence/tablescript-application-m13/validation-report.json).
Verbose build/test logs are under `artifacts/validation/tablescript-application-m13-*`;
the original café regression outputs are under
`artifacts/local/tablescript-application-m13/cafe-regression/`. Generated browser
bundles, C#, and screenshots stay in the sample's ignored artifact directories.

## Remaining boundaries

This is a local scenario preview. There is no server persistence or remote commit
protocol. A future authoritative endpoint must revalidate a typed edit request
against server-owned inputs; a browser-produced view is not inventory authority.
Using the same language does not erase that boundary.

Coordinated key/reference rewrites, variable row count, typed cross-snapshot join
failures, average/min/max summary policy, and broader workbook domain rules remain
separate work. Incompatible declared joins retain their invariant failure rather
than dropping rows or substituting data. Transparent aliases are still not money
or range proofs. This pass does not introduce a validation DSL or promote test
`Assert.*` into a portable runtime intrinsic. Native Excel interaction and
large-table performance are not qualified here.
