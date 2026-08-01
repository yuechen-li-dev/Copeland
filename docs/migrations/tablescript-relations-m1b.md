# TABLESCRIPT-RELATIONS-M1B

M1B adds declared-reference joins to the existing `derive` declaration:

```ts
record table ProductCatalog = derive Products as product
    join Categories as category through product.categoryId
    join Prices as price through price.productId {
    productName: string = product.name;
    categoryName: string = category.name;
    retail: number = price.retail;
}
```

Keys identify rows; references classify relationships; joins use those declared
relationships. The `through` member must be a declared reference and the binder
resolves its direction before lowering. M1B allows only inner many-to-one and
one-to-one joins, so each source row produces at most one projected output row.
Source-row order is retained exactly.

Each alias has its exact generated row type and is scoped to the derivation.
The bound and MIR plans retain the ordered relation joins, reference/key IDs,
cardinality, aliases, output schema, and deterministic plan identity. C# builds
typed local immutable key lookups, iterates the source relation in order, and
constructs new immutable typed column arrays. An unresolved derived-input lookup
throws a clear runtime error; it is never silently omitted.

Copied source-alias key/reference columns preserve identity metadata. Computed,
renamed, and ambiguous identity projections do not manufacture identities.
Provenance records the contributing columns and the joins that make joined values
available. Joined tables stay read-only, while later derivations, aggregates, and
normal columnar queries remain available. JavaScript retains the existing explicit
derived-materializer unavailable diagnostic.

Deferred: one-to-many or many-to-many expansion, arbitrary predicates, outer
joins, grouping, windows, SQL, databases, optimizers, persistence, mutable
derived tables, dynamic CLI queries, and row-level lineage. M1B is not an ORM or
SQL engine.
