# CTS-TABLE-DOGFOOD-M0 — Practical table/workbook dogfood

## Result

CTS-TABLE-DOGFOOD-M0 is complete. The current record-table system is useful for
a small, Git-managed typed workbook today: author inline columnar source,
inspect and make guarded source edits with `tscl table`, exchange a bounded CSV,
compile ordinary typed queries, and consume the resulting DLL from C#.

This is a bounded positive result. It does not claim that record tables replace
a spreadsheet application, dataframe, database, SQL engine, or a dynamic query
tool. The table section can close pending real-world use; future work should be
driven by observed workflows rather than a hypothetical roadmap.

## Fixture

The authored fixture is
`samples/copeland-ts/table-dogfood-m0/`. Its authoritative workbook is
`Workbook/Workbook.ts`; it is not generated from CSV.

```text
Catalog workbook module
├─ Categories  (id, name)                         3 rows
├─ Products    (id, categoryId, name, state)       5 rows
├─ Prices      (productId, retail, cost)            5 rows
└─ Inventory   (productId, onHand, reorderPoint)    5 rows
```

`StockState` is a nominal zero-payload enum with `Active`, `LowStock`, and
`Discontinued`. `Products.categoryId` relates Products to Categories; both
Prices and Inventory relate to Products through `productId`. Relationships use
the existing typed `int` fields and normal compiled lookup code; they are not
runtime foreign-key constraints.

The module contains:

- `revisedPrices`, an immutable full-column `with` snapshot;
- `activeProducts`, a typed `rows().where(...).select(...)` query;
- `retailSum`, `retailCount`, `retailAverage`, `retailMinimum`, and
  `retailMaximum`;
- `categoryNameFor`, a cross-table ID lookup;
- `inventoryValue`, a second cross-table calculation; and
- `stateLabel`, an exhaustive `match` over `StockState`.

The final CSV edit changes only Filter Beans retail price from `16.00` to
`16.25`. The immutable revision remains `16.50`, proving that it is a distinct
snapshot.

## Workflow performed

The fixture was authored from scratch, then built with a normal first build
(which performs the expected initial NuGet restore):

```console
cd samples/copeland-ts/table-dogfood-m0
dotnet build TableDogfoodM0.slnx
dotnet run --project Consumer/Consumer.csproj --no-build
```

The compiled consumer output was:

```text
sheets=Categories,Products,Prices,Inventory
product-rows=5
active-products=3
retail=sum:115.00,count:5,average:23.00,min:8.75,max:42.00
lookup=Coffee
inventory-value=969.25
state=low-stock
original-retail=101:16.25
revised-retail=101:16.50
```

`dotnet build ... --no-restore` was intentionally not used for that first
standalone build: a fresh project has no `obj/project.assets.json` yet. It did
work after the ordinary first build.

The current CLI was invoked as:

```console
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table list Workbook/Workbook.ts
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table schema Workbook/Workbook.ts Products
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table rows Workbook/Workbook.ts Products --offset 1 --limit 3 --columns id,name,state

dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table add-row Workbook/Workbook.ts Categories --json '{"id":40,"name":"Accessories"}' --format json
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table set Workbook/Workbook.ts Categories --row 3 --column name --value "Accessories and Filters" --format json
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table delete-row Workbook/Workbook.ts Categories --row 3 --format json

dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table add-row Workbook/Workbook.ts Products --json '{"id":105,"categoryId":10,"name":"Seasonal Blend","state":"Active"}' --format json
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table delete-row Workbook/Workbook.ts Products --row 5 --format json

dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table export Workbook/Workbook.ts Prices --format csv --output Prices.csv --result-format json
# Change 101,16,7.5 to 101,16.25,7.5 in Prices.csv.
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table import Workbook/Workbook.ts Prices --format csv --input Prices.csv --replace --result-format json

dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table validate Workbook/Workbook.ts --format json
dotnet src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll table set Workbook/Workbook.ts Prices --row 1 --column retail --value not-a-number --format json
```

The category and product rows were added, edited, and deleted again to exercise
the three mutation intents without leaving incidental data. The second add
used the natural enum spelling `"Active"`; the structured result returned the
typed value as `"Active"`. Row numbering is zero-based consistently in commands
and result envelopes.

The invalid numeric edit exited `1`, returned `COPE-TABLE-TOOL-0010` in JSON,
and the SHA-256 of `Workbook.ts` was unchanged before and after the command.
Candidate validation and atomic publish therefore protected the authored
source.

## Agent JSON workflow

The following sequence required no source-column reconstruction, prose
scraping, Excel, pandas, or SQL:

```text
table schema Workbook.ts Prices --format json
  → columns productId:int, retail:number, cost:number; rowCount 5

table rows Workbook.ts Prices --offset 1 --limit 1 --format json
  → row 1: { productId: 101, retail: 16.25, cost: 7.5 }

table set Workbook.ts Prices --row 1 --column retail --value 16.25 --dry-run --format json
  → { success: true, command: "table.set", dryRun: true, ... }

table validate Workbook.ts --format json
  → { success: true, tableCount: 4, totalRows: 18, diagnostics: [] }
```

A fresh isolated sibling copy of the fixture was restored and built, then ran
that JSON-only sequence and the C# consumer successfully. The only source
reading needed was locating the single workbook module and its exact table
names; `table list` then supplies the names and locations.

## Git review quality

An isolated pre-edit snapshot and `git diff --no-index` produced this focused
review shape after the CSV round trip:

```diff
 export record table Prices {
     productId: int = [100, 101, 102, 103, 104];
-    retail: number = [18.50, 16.00, 8.75, 42.00, 29.50];
+    retail: number = [18.50, 16.25, 8.75, 42.00, 29.50];
     cost: number = [9.25, 7.50, 3.10, 21.00, 15.25];
 }
```

The CLI changed every column only for the temporary added/deleted rows. The
CSV import is a whole-table replacement operation by design, but its localized
writer preserved the existing decimal presentation of unaffected numeric cells.

## Friction inventory

| Classification | Observation | Outcome |
| --- | --- | --- |
| B — poor diagnostic / machine result | JSON mutation failure said `command: "set"`, while success said `command: "table.set"`. | Fixed. All table failure envelopes now use the same `table.<subcommand>` command identity. |
| C — awkward but usable UX | A CSV export writes invariant numeric text (`18.5`), and re-import previously rewrote source `18.50` as `18.5`, creating unrelated diff noise. | Fixed because the local, broadly useful change was small. Plain decimal `number`/`float` columns retain their established precision when input fits it. |
| C — language ergonomics | Enum equality in a `where` predicate is not the current supported route and lexical enum capture is explicit. An exhaustive helper `match` (`isActive`) is required. | Not changed. It is visible compiler guidance and the ordinary helper is short; broad enum-comparison/capture changes were not justified. |
| C — discoverability | Before `table list`, a user must know the workbook source path. | Not changed. The tool intentionally uses explicit source files; adding project-wide discovery would expand scope. |
| C — terminal width | Wide row projections can become unwieldy. `--columns`, `--offset`, and `--limit` kept the real inspection compact. | Not changed. Existing options directly address the observed case. |
| C — relationship enforcement | IDs are typed scalar fields and lookup works, but uniqueness and foreign-key validity are not enforced. | Not changed. A checked relationship/index system is not required for this small compiled workbook and would be new table semantics. |
| E — speculative feature | A query CLI, exported-function invocation ABI, SQL, dataframe API, dynamic joins, or spreadsheet UI could make the example look more familiar. | Deliberately not added. Normal compiled Copeland functions and the C# consumer already complete the workflow. |

No actual table correctness defect was observed. The two changes above are
diagnostic/UX corrections rather than new data, query, or runtime architecture.

## Friction fixed

### Consistent JSON command identity

- Observed failure: `table set ... --format json` returned `command: "set"` on
  failure and `command: "table.set"` on success.
- Root cause: the exception handler used the raw subcommand instead of the
  command identity used by successful handlers.
- Fix: failure and file-I/O envelopes now prefix `table.`.
- Broad justification: JSON clients can use one stable operation identifier for
  both success and failure.
- Tests: `TableTools_ProjectBoundColumnsAsRowsAndApplyAtomicCompilerValidatedEdits`
  now parses the failed set envelope and asserts `table.set`.

### CSV decimal review preservation

- Observed annoyance: importing a one-cell price change rewrote every existing
  decimal literal in the same arrays.
- Root cause: import used the CSV numeric spelling directly when replacing the
  complete column arrays.
- Fix: for plain decimal `number` and `float` columns, import detects the
  existing maximum fractional precision and formats an incoming value to that
  precision only when it fits. Inputs requiring more precision, exponent
  notation, or non-plain source literals retain their validated input spelling.
- Broad justification: CSV import remains an explicit whole-table edit while
  preserving meaningful localized Git diffs for common currency/measurement
  columns without silently rounding incoming data.
- Tests: the CLI integration test now performs a real export/import and proves
  `[95.0, 84.0, 91.0]` remains decimal-formatted; it also preserves the existing
  CSV round-trip check.

`docs/Copeland/table-tools.md` now documents the precision behavior.

## Current practical limits

Recommended use cases:

- small to modest, source-owned catalogs, rules, reference data, prices, and
  configuration tables reviewed in Git;
- data that benefits from a compiler-checked schema, ordinary typed code, and a
  normal .NET artifact;
- controlled CSV handoffs where full-table replacement is reviewed explicitly.

Inappropriate use cases:

- interactive spreadsheet authoring, ad-hoc filtering, dashboarding, or large
  data manipulation;
- workloads requiring database storage, indexes, foreign keys, concurrent
  editing, SQL, dynamic joins/grouping, or query invocation from the CLI;
- schemas where the current scalar ID relationship must be a compiler-enforced
  key/foreign-key constraint.

## Validation

Focused regression validation passed:

```text
dotnet build tests/Copeland/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj --no-restore
dotnet test tests/Copeland/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj --no-build --filter FullyQualifiedName~TableTools_ProjectBoundColumnsAsRowsAndApplyAtomicCompilerValidatedEdits
```

Complete repository validation passed after this report was added:

```text
dotnet build Copeland.slnx --no-restore  PASS
dotnet test Copeland.slnx --no-build      PASS (1,461 tests across 8 assemblies)
git diff --check                          PASS
```
