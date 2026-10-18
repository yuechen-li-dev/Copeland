# TableScript snapshot scenarios

> Historical qualification paths/results. Use the maintained
> [TableScript suite](../../../tools/Copeland.Qualification.TableScript/README.md)
> for current commands. Retired raw bundles are represented by
> [summaries and recovery hashes](evidence/tablescript-retired-artifacts/provenance.json);
> old capability statements below describe their original run.

Status: **success for bounded C# snapshot-relative recalculation**.

The café workbook can now preview a changed stock snapshot without editing its
source or rebinding the global purchasing plan:

```ts
function previewPlan(stock: Stock): PurchasePlan {
    return PurchasePlan.recalculate({ Stock: stock });
}
```

`previewTotals` and `previewSupplierSummaries` summarize that fresh plan with the
existing typed summary functions. The motivating improvement is that a scenario
uses ordinary typed parameters and the original purchasing policy. Source-copy
edits are retained only as an independent qualification oracle.

## Semantic and implementation authority

Binding resolves an input literal against the declared plan's transitive authored
dependencies. Each replacement must have the dependency's exact nominal table
type. Unspecified inputs use their existing global snapshots. Written replacement
expressions evaluate once in property order, before any relation is rebuilt.
Every derived dependency is then rebuilt once in dependency order, including
shared intermediates and dependencies reached through joins.

MIR carries the input identities, expressions, and resolved relation schedule.
Its validator checks complete input coverage, nominal identities, dependency
order, single realization, and the exact source/join arguments of every step.
C# emission calls each table's existing compiled `Create` implementation with
those snapshots. There is no second formula interpreter or scenario calculation
engine. Returned tables retain their columnar representation, nominal rows, and
derived-column provenance. Authored and global derived tables remain unchanged.

Tests exposed missing backend traversal when recalculation was nested under a
column aggregate, array length, or array index. Those paths now discover required
helpers and delegates and preserve short-circuit evaluation. A skipped branch
does not evaluate its replacement expressions or build a scenario.

The [language contract](../language/copeland-ts-table-scenarios.md) describes
admission and diagnostics; the [friction list](tablescript-friction-m12.md)
records the remaining work.

## Executed evidence

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File tools/copeland-tablescript-scenarios/qualify.ps1
pwsh -NoProfile -File tools/copeland-tablescript-typed-summaries/qualify.ps1
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1
pwsh -NoProfile -File tools/copeland-tablescript-key-edits/qualify.ps1
pwsh -NoProfile -File tools/copeland-tablescript-row-authoring/qualify.ps1
```

The full solution build passed with **zero warnings and errors**. All **2,399
tests passed**, with no failures or skips. **17 focused recalculation cases**
cover transitive and shared dependencies, multiple input replacement order,
omitted inputs, inline `with`, array selection/length/indexing, captured filters,
typed summaries, generic helpers, untaken branches, invalid receivers and input
literals, nominal mismatches, corrupted MIR schedules, and retained execution
boundaries. The combined recalculation/summary selection passes **42 cases**.
An incompatible key snapshot fails the existing declared-join guard and leaves
the global workbook intact.

The MSBuild café consumer executes **22 assertions**. Scenario qualification
passes **149 checks**. It compiles the current Copeland source and executes its
preview functions, comparing all eight fields of each of six purchasing rows,
each supplier summary field, group order, and typed totals against the canonical
`csharp-relation-plan` CLI executor. The independent CLI oracle edits an isolated
source copy by stock key. Probe functions only project Copeland values across
the existing C# host boundary; the host and PowerShell do not calculate formulas
or totals.

| Scenario | Total cents | Units | Lines | Supplier cents in first-seen order |
| --- | ---: | ---: | ---: | --- |
| Global authored workbook | 31,210 | 67 | 3 | 10: 18,500; 20: 4,960; 30: 7,750 |
| Preview: stock key 100 changed from 4 to 12 | 12,710 | 47 | 2 | 20: 4,960; 30: 7,750 |
| Global workbook after preview | 31,210 | 67 | 3 | 10: 18,500; 20: 4,960; 30: 7,750 |

Repeated previews are identical. The global purchasing rows and typed summaries,
the original source hash, shuffled-stock joins, and single compiled purchasing
policy remain intact. Existing qualification passes **57 typed-summary checks**,
**66 café workflow checks**, **96 keyed-edit checks**, and **46 row-authoring
checks**.

Evidence is saved under `artifacts/tablescript-scenarios/`: full build/test logs,
consumer assertions, generated source, MIR, runtime JSON, canonical query JSON,
source-copy edits, regression qualification logs, and `summary.json`.

## Explicit remaining boundaries

- This executes through C#. All JavaScript profiles retain
  `COPE-JS-DERIVE-0001`; no browser scenario execution is claimed.
- The receiver must be a declared derived table. Authored tables, stored plan
  snapshots, unknown/derived/duplicate input names, and wrong nominal input types
  receive `COPE-RECALC-0001` through `0004`.
- Direct use in async/generator bodies stops at `COPE-RECALC-0005`. A synchronous
  helper is the supported repair.
- Existing `with` column replacements remain static, deeply immutable arrays
  with fixed row count. Dynamic cell updates and table growth are not added.
- Incompatible cross-snapshot keys fail the existing join invariant. Coordinated
  key rewrites and a typed Result for that failure remain separate work.
- Application-owned domain validation and frontend derived-table execution are
  the next unresolved café frictions. Transparent aliases do not prove currency
  units or numeric ranges. Native Excel, browser interaction, and large-table
  performance were not qualified in this pass.
