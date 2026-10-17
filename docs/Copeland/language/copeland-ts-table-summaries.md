# Typed table summaries

`summarize<Record>` materializes a table row view into declared immutable records.
The result has named fields and ordinary record types, so functions can return
purchase totals or supplier summaries without passing query strings or indexing
anonymous cells. C# and JavaScript execute the same typed summary MIR.

```ts
record PurchaseTotals {
    totalCents: int;
    units: int;
    lines: int;
}

function purchaseTotals(): PurchaseTotals {
    return PurchasePlan.rows()
        .where(line => line.orderUnits > 0)
        .summarize<PurchaseTotals>(line => ({
            totalCents: sum(line.orderCents),
            units: sum(line.orderUnits),
            lines: count()
        }));
}

record SupplierSummary {
    supplierId: int;
    supplierName: string;
    totalCents: int;
    units: int;
    lines: int;
}

function supplierSummaries(): SupplierSummary[] {
    return PurchasePlan.rows()
        .where(line => line.orderUnits > 0)
        .groupBy(line => line.supplierId, line => line.supplierName)
        .summarize<SupplierSummary>(line => ({
            supplierId: line.supplierId,
            supplierName: line.supplierName,
            totalCents: sum(line.orderCents),
            units: sum(line.orderUnits),
            lines: count()
        }));
}
```

Without `groupBy`, the result is one record. With `groupBy`, the result is a record
array, in first-seen group order after filtering. Empty input produces zero sums
and counts for a total, and an empty array for grouped summaries. Group equality
follows the canonical C# query host's scalar equality; strings are case sensitive.

The record type argument can be omitted when a return or variable annotation
provides the record type (or grouped record array type). Explicit type arguments
always name the element record, including for grouped summaries. Every declared
field must appear exactly once; descriptor field order can differ from record
order. Transparent numeric aliases follow ordinary type equivalence.

Summary descriptors are declarative expression arrows with one source row
parameter. They accept direct group-key copies, `sum(row.column)` for an `int`,
`float`, or `number` column, and `count()` returning `int`. These are descriptor
operators, resolved by summary binding rather than runtime function lookup.
Each summary needs at least one aggregate. Copied fields must be declared group
keys; group keys do not have to appear in the output record. One to seven direct
scalar column selectors are supported, including composite keys, boolean keys,
and enum keys.

Compute expressions in `derive` first. A summary does not admit arbitrary calls,
captured values, arithmetic, block bodies, or effects in its descriptor. An
optional row parameter annotation must match the source nominal row type. Arrow
return annotations are rejected; the declared summary record owns the result
shape. `where` retains its ordinary predicate and explicit capture semantics:
the source and each predicate capture are evaluated once before iteration.
Summary expressions respect branches and short-circuit evaluation.

Binding reuses record field/type authority, canonical aggregate admission, and
derived-column provenance. MIR carries source identity, group columns, aggregate
operators, record field identities, and transitive input/relationship dependencies.
The C# emitter shares count/sum accumulator statements with canonical query
materialization. The CLI query path continues to own ad-hoc query execution.

The source must be a direct `table.rows()` view, optionally followed by `where`.
Table parameters and immutable `with` snapshots work as sources. A stored row
view or standalone `groupBy` stops at `COPE-SUMMARY-0010`; grouping is terminal
and must immediately precede `summarize`. Direct materialization in async or
generator bodies stops at `COPE-SUMMARY-0011`: put it in a synchronous helper and
call the helper instead. That avoids emitting an unsupported suspension state.

`average`, `min`, and `max` are deferred at `COPE-SUMMARY-0007` until their empty
input and Option behavior is designed. All JavaScript emission profiles execute
typed sum/count summaries. Summaries do not rebind a global derived
relation when a different input snapshot is passed elsewhere. Ordinary generated
record carriers retain their existing internal C# field names; typed fields are
usable directly inside Copeland TS, while external host projections remain an
interop concern.

Use [explicit recalculation](copeland-ts-table-scenarios.md) to create a fresh
derived snapshot, then summarize its rows. The global relation remains unchanged.

See the [café workbook](../../../samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts)
and [qualification report](../reviews/tablescript-typed-summaries.md).
