# TableScript typed summaries

> Historical qualification paths/results. Use the maintained
> [TableScript suite](../../../tools/Copeland.Qualification.TableScript/README.md)
> for current commands. Retired raw bundles are represented by
> [summaries and recovery hashes](evidence/tablescript-retired-artifacts/provenance.json);
> old capability statements below describe their original run.

Status: **success for the bounded C# sum/count milestone**.

The café workbook now declares `PurchaseTotals` and `SupplierSummary`, returns
one typed record from `purchaseTotals()`, and returns a typed record array from
`supplierSummaries()`. The existing scalar total functions delegate to the named
total record. Both functions filter actionable purchase rows; supplier grouping
uses direct selectors for supplier identity and name.

The motivating improvement is source-level authoring: the same supplier summary
that previously required an ad-hoc CLI aggregate string is now a checked function
with a declared result shape. A missing, duplicated, or mistyped result field is a
source diagnostic. Calculated inputs remain in the existing `derive` plan, which
still has one per-row policy local and no intermediate restocking relation.

## Semantic and implementation authority

This adds terminal expressions to existing table row views, without new parser
keywords or first-class grouped-view storage. Binding owns record types, direct
source columns, supported aggregates, descriptor shape, and group keys. The
canonical query binder and authored summaries share numeric/scalar admission
and derived-column provenance. MIR retains field identities, group metadata,
aggregate operators, and transitive input/relationship dependencies. C# summary
emission shares count/sum accumulator statements with query materialization.

An ungrouped summary returns one record, including zero sums/counts for empty
input. A grouped summary returns first-seen groups after filtering, or an empty
array for empty input. Descriptors may reorder result fields; the binder normalizes
them into declared record order. Existing explicit filter captures evaluate once
before iteration. Branch and short-circuit evaluation preserve when a summary runs.
Summaries over a table parameter consume that snapshot without mutating the
singleton. Ordinary callable members named `summarize` retain their usual meaning.

## Executed evidence

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File tools/copeland-tablescript-typed-summaries/qualify.ps1
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1
pwsh -NoProfile -File tools/copeland-tablescript-key-edits/qualify.ps1
pwsh -NoProfile -File tools/copeland-tablescript-row-authoring/qualify.ps1
```

The full solution build passed with **zero warnings and errors**. All **2,382
tests passed**, with no failures or skips, including **25 focused summary cases**.
Focused runtime coverage includes composite string/boolean/enum keys, numeric
sums, omitted output keys, contextual record inference, declared field order,
filtered empty input, captured predicates, immutable snapshots, capture creation
order, untaken branches, ordinary member-name compatibility, and retained derived
provenance. Invalid descriptors and corrupted MIR receive directed diagnostics.
All three JavaScript profiles receive the named execution boundary.

The MSBuild café consumer executes **15 assertions**. Typed summary qualification
passes **57 checks**, comparing every total and supplier field and group order
against the canonical `csharp-relation-plan` query executor. It emits and compiles
the current Copeland source into an executable C# probe before and after an edit
to an isolated source copy. Its Copeland-only probe functions copy record fields
across the existing host boundary; neither the C# runner nor PowerShell
recalculates aggregates.

Existing qualification also passed: **66 café workflow checks**, **96 keyed-edit
checks**, and **46 row-authoring checks**. Their purchasing rows, source edit
guards, and per-row policy/provenance assertions remain intact.

| Scenario | Total cents | Units | Lines | Supplier cents in first-seen order |
| --- | ---: | ---: | ---: | --- |
| Authored stock | 31,210 | 67 | 3 | 10: 18,500; 20: 4,960; 30: 7,750 |
| Stock key 100 changed from 4 to 12 | 12,710 | 47 | 2 | 20: 4,960; 30: 7,750 |

Evidence is saved under `artifacts/tablescript-typed-summaries/`: full build/test
logs, MSBuild consumer output, typed runtime JSON, canonical query JSON, stock edit
JSON, generated source, summary MIR, and `summary.json`. The original workbook's
hash is preserved. Existing workflow qualification logs are retained alongside
the new evidence; their detailed artifacts stay in their existing directories.

## Explicit remaining boundaries

- `sum(column)` and `count()` are implemented. `average`, `min`, and `max` stop at
  `COPE-SUMMARY-0007` until their empty-input/Option semantics are designed.
- Source views are direct `rows()` chains with optional `where`; stored row views
  and standalone `groupBy` stop at `COPE-SUMMARY-0010`.
- Direct materialization in async/generator bodies stops at
  `COPE-SUMMARY-0011`. A synchronous summary helper is the bounded repair.
- JavaScript materialization stops at `COPE-JS-SUMMARY-0001`; derived tables also
  retain their existing `COPE-JS-DERIVE-0001` boundary. No browser summary
  execution is claimed.
- Passing a changed input snapshot elsewhere does not rebind global derived
  relations. Snapshot-relative derivation remains a separate capability.

The later [snapshot scenario pass](tablescript-snapshot-scenarios.md) adds explicit
`PurchasePlan.recalculate({ Stock: revised })` and qualifies summaries over that
fresh plan. The validation totals above describe this original summary milestone.
- Ordinary C# record carriers still expose internal generated field names.
  Copeland functions use named typed fields directly; qualification uses explicit
  field projections at the host boundary. Exported tables' named C# API does not
  imply an equivalent exported record API.
- No native Excel operation or new Excel artifact qualification was performed.

See [the language contract](../language/copeland-ts-table-summaries.md) and
[the workbook](../../../samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts).
