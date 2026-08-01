# TABLESCRIPT-DERIVED-TABLES-M1A

Copeland now distinguishes two first-class record-table definitions:

- Authored tables store static source-owned column data.
- Derived tables compute a new, read-only columnar relation from an existing table.

The M1A syntax is deliberately singular:

```ts
export record table PriceMargins = derive Prices as price {
    productId: int = price.productId;
    margin: number = price.retail - price.cost;
}
```

The alias is scoped to the projection body and uses the source table's generated
row type. Projection expressions use normal Copeland scalar expression binding.
Each output has an exact type and records copied/computed provenance in the
derived relation plan. C# materializes the plan in source order into freshly
allocated immutable column arrays; it never exposes an array result as the
relation value. JavaScript reports a precise unavailable-materializer diagnostic.

Derived tables support normal column operations after materialization and are
read-only in the table CLI. The CLI reports their kind, source, schema, and
column provenance. CSV import/export, direct edits, and rows inspection are
authored-table operations in M1A.

Deferred: joins and multiple sources, outer joins, grouping and grouped
aggregates, window functions, recursive queries, SQL/databases, optimizers,
mutable derived tables, dynamic query CLI execution, persistence, and row-level
lineage.
