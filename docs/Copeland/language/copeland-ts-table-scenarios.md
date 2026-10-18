# Snapshot-relative table calculation

Call a declared derived table's `recalculate` operation to run its existing plan
against explicit immutable input snapshots:

```ts
function previewPlan(stock: Stock): PurchasePlan {
    return PurchasePlan.recalculate({ Stock: stock });
}

function previewTotals(stock: Stock): PurchaseTotals {
    const plan: PurchasePlan = previewPlan(stock);
    return plan.rows()
        .where(line => line.orderUnits > 0)
        .summarize<PurchaseTotals>(line => ({
            totalCents: sum(line.orderCents),
            units: sum(line.orderUnits),
            lines: count()
        }));
}

const stock: Stock = Stock with { onHand: [5, 12, 0, 2, 8, 10] };
const revised: PurchasePlan = PurchasePlan.recalculate({ Stock: stock });
```

The receiver is a derived table declaration. Its nominal type supplies the result
type and owns the source, joins, formulas, locals, and column provenance. There
is no new formula body or independently interpreted query. The existing compiled
relation implementation creates the result.

The argument is one nonempty object literal. Property names identify authored
tables in the plan's transitive dependency graph. Values must have those exact
nominal table types; same-shaped tables are not interchangeable. Omitted authored
inputs use their existing global snapshots. Replacing derived intermediates,
unrelated tables, unknown inputs, or duplicate inputs is rejected. A stored table
value is not a plan receiver; call the declaration explicitly.

Written replacement expressions evaluate once, in object property order, before
any derived step. Every derived dependency is then materialized once in compiler
dependency order. Shared intermediate relations use the same freshly calculated
snapshot throughout the call. This includes both source and joined dependencies.
The operation returns a new columnar table; it never overwrites the source inputs
or global derived tables. Branches and short-circuit expressions control whether
recalculation runs. Result rows retain their existing nominal type and work with
`rows`, filters, selections, typed summaries, and column aggregates.

Bound facts and MIR retain the complete authored input set and ordered relation
steps. MIR validation checks input identities, graph coverage, dependency order,
single realization of each node, and exact source/join arguments. Backends receive
the resolved schedule rather than deriving an alternative relationship graph.

C# and JavaScript execute this operation, including all JavaScript emission
profiles. See the [application and frontend qualification](../reviews/tablescript-application-m13.md).
Direct use in async or
generator bodies stops at `COPE-RECALC-0005`; a synchronous helper is the repair.
`COPE-RECALC-0001` through `0004` diagnose an invalid receiver, argument shape,
input name, or nominal input type.

Existing `with` rules still apply: column replacements are static, deeply
immutable authored arrays that preserve the declared row count. Recalculation
accepts those snapshots through ordinary function parameters. The separate
[`withRow` operation](copeland-ts-table-application.md) adds runtime edits of
ordinary fields by declared key; neither operation changes table cardinality. Keys and references keep their
existing checks. If selected snapshots are incompatible at a declared join, the
existing compiled join guard fails with its relation name instead of substituting zero
or dropping rows. A failed calculation also preserves the global workbook.
Coordinated key rewrites remain separate. The later
[variable-row pass](copeland-ts-table-changes.md) adds runtime-sized input
snapshots and `tryRecalculate` with typed join failures and explicit dependencies.

The [café workbook](../../../samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts)
has preview functions for the plan, totals, and supplier summaries. The
[qualification report](../reviews/tablescript-snapshot-scenarios.md) records the
comparison with the canonical CLI executor. Source-copy edits are an independent
test oracle; runtime previews do not require source edits.
