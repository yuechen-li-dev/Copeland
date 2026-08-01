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

## M1B declared-reference joins

Keys identify rows. References classify relationships. A derived table can use
those already-declared facts to enrich one source row without exposing arrays or
introducing a query language:

```ts
export record table ProductCatalog = derive Products as product
    join Categories as category through product.categoryId
    join Prices as price through price.productId {
    productName: string = product.name;
    categoryName: string = category.name;
    retail: number = price.retail;
}
```

`through` names a declared reference field. The compiler resolves whether the
reference is on the already-available alias or on the joined alias, rejects
unknown/ambiguous relationships, and binds each alias to its exact generated row
type. M1B supports inner many-to-one and one-to-one joins only. It preserves the
original source relation row order; every source row produces at most one output
row. C# builds typed immutable lookup indexes for joined keys and materializes
fresh exact column arrays. An unresolved lookup from a derived input fails loudly
at runtime rather than silently dropping the row.

Copied source keys/references from the source alias retain identity metadata;
computed or ambiguous projections do not. Provenance records input columns plus
the relationships used to expose joined values. Joined derived tables remain
read-only, but can themselves be derived, queried, and aggregated. JavaScript
continues to report its explicit unavailable derived-materializer boundary.

Deferred: one-to-many or many-to-many expansion, arbitrary predicates, outer
joins, grouping and grouped aggregates, window functions, recursive queries,
SQL/databases, optimizers, persistence, dynamic query CLI execution, row-level
lineage, and mutable derived tables. This is not an ORM or SQL engine.
