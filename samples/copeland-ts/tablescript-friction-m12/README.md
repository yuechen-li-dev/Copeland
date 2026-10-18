# TableScript friction M12: café restocking

This is a workbook authoring and calculation comparison using synthetic café data.
It runs through Copeland TS's MSBuild C# generation and the default compiler-owned
table query executor. No spreadsheet interpreter or handwritten calculation engine
was added.

Start with [Workbook.ts](Workbook/Workbook.ts). It contains three input tables,
declared relationships, a restocking policy, a priced purchase plan, and ordinary
typed functions over those relations. Stock rows deliberately differ from product
order. Paused products never generate orders; reaching the reorder point exactly
also generates no order.

The recent language additions appear in ordinary work:

- `Cents` and `Units` name scalar intentions. They are transparent aliases.
- `Purchasing` and `match` make the policy exhaustive.
- Suppliers use schema declarations followed by row entries, keeping each supplier's
  fields together. Products retain column authoring for repeated policy cells.
- A per-row `const orderUnits` computes the policy once in `PurchasePlan` and shares
  it between quantity and cost. The intermediate `Restock` relation is unnecessary.
- `[Purchasing.Active ... 5, Purchasing.Paused]` and `[8 ... 2, 6, 12, 8 ... 2]`
  express repeated fixed input cells. CLI edits still address individual logical
  rows, and the totals below remain unchanged.
- `PurchasePredicate` names a callback signature; its closure captures the supplier
  and per-line spending ceiling explicitly.
- `HasOrderCost` constrains a reusable generic projection over `PurchasePlan.Row`.
- `Stock with {...}` creates a snapshot without changing global derived relations.
- `PurchasePlan.recalculate({ Stock: stock })` explicitly recalculates that snapshot
  through the existing policy and joins. `previewPlan`, `previewTotals`, and
  `previewSupplierSummaries` expose the scenario without editing source or globals.
- `purchaseTotals()` returns a declared `PurchaseTotals` record with three named
  aggregates. `supplierSummaries()` returns `SupplierSummary[]` using direct group
  key selectors and the same fields as the canonical grouped CLI query.

## Run from the repository root

```powershell
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1
```

The script builds the current CLI and workbook, executes 22 generated-code consumer
assertions and 66 qualification checks, and saves canonical query, schema, edit, and diagnostic evidence under
`artifacts/local/tablescript-cafe/`. It edits an isolated source copy. The checked-in
workbook remains unchanged.

Typed summaries have a separate qualification:

```powershell
./tools/Copeland.Qualification.TableScript/run.ps1 -Suite summaries
```

It compares every typed total and supplier field, including first-seen group order,
against canonical CLI queries before and after the stock edit. Qualification-only
Copeland field projections bridge the existing internal C# record carriers; no
host code recalculates aggregates. See [typed summary semantics](../../../docs/Copeland/language/copeland-ts-table-summaries.md).

For runtime scenario qualification:

```powershell
./tools/Copeland.Qualification.TableScript/run.ps1 -Suite snapshots
```

The suite compares all six preview rows and every typed summary field with an
independent source-copy CLI calculation. The runtime preview uses only immutable
snapshots and preserves source and globals. Repeated previews are identical. See
[snapshot-relative calculation](../../../docs/Copeland/language/copeland-ts-table-scenarios.md).

Base result: three orders, 67 units, 31,210 cents. Changing espresso stock from 4 to
12 produces two orders, 47 units, 12,710 cents. Invalid integer literals, duplicate
keys, unresolved references, and derived-column writes are rejected with the source
hash preserved.

For ordinary exploration:

```powershell
$cli = 'src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.dll'
$workbook = 'samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts'
dotnet $cli table query $workbook PurchasePlan --where 'orderUnits > 0' --order-by 'orderCents desc'
dotnet $cli table query $workbook PurchasePlan --where 'orderUnits > 0' --group-by 'supplierId, supplierName' --aggregate 'sum(orderCents) as totalCents, sum(orderUnits) as units, count() as lines'
dotnet $cli table set $workbook Stock --key 100 --column onHand --value 12 --dry-run --format json
```

`--key 100` uses Stock's declared `productId` key, so espresso stays the target
when stock rows move. The qualification script now uses this selector for its
stock scenario and guarded edits. `--row` remains available as a zero-based
position in the selected input table's order. `--dry-run` previews a source
edit; it does not execute a recalculated scenario. Apply the edit to a copy, then
query the derived relation to calculate that scenario through the CLI. Copeland
functions can instead call `PurchasePlan.recalculate` with an input snapshot.

`./tools/Copeland.Qualification.TableScript/run.ps1 -Suite keyed-edits` checks the same edit
in the authored and reversed stock orders (espresso at rows 1 and 4), comparing
canonical plans and preserving all other product results. Its evidence goes to
`artifacts/local/tablescript-qualification/keyed-edits/`. See the
[key-addressed edit report](../../../docs/Copeland/reviews/tablescript-key-addressed-edits.md).

## Excel baseline

`build-excel-baseline.mjs` uses the bundled `@oai/artifact-tool` runtime to generate
an `.xlsx` with the same raw inputs, keyed `INDEX/MATCH` stock and supplier lookups,
`IF` purchasing logic, supplier summaries, and editable input validation. It does
not copy Copeland's calculated results into output cells. It checks all six rows
and supplier totals against canonical query results, changes the same stock input,
checks recalculation, then restores the original input before export.

After qualification, run the builder with the bundled Node executable and make
`@oai/artifact-tool` available through a local `node_modules` junction to the bundle
returned by `load_workspace_dependencies`. Do not install a competing sheet library
into this sample. Optional builder arguments override the evidence and output
directories. The default workbook goes under `artifacts/local/tablescript-cafe/excel/`.
The original comparison workbook and previews under `outputs/` remain historical
evidence; reruns do not overwrite them.

Calculation and rendered previews were verified with artifact-tool. Native Excel
was not operated. The baseline is bounded to six product rows; table growth and
native sorting behavior are not qualified.

See [the M12 review](../../../docs/Copeland/reviews/tablescript-friction-m12.md)
for the comparison, fixes, and remaining friction.
