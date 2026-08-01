# Copeland TS table identities M0

`record table` values remain immutable, typed, columnar relations. M0 adds
single-column identity and relationship facts without changing table storage:

```ts
record table Categories {
    key id: int = [10, 20];
    name: string = ["Coffee", "Tea"];
}

record table Products {
    key id: int = [100, 101];
    reference categoryId: int -> Categories.id = [10, 20];
}
```

`key` marks one `int`, `string`, or `boolean` column as the table's row
identity. Authored values must be unique. `reference` keeps scalar values in
the source column and binds metadata to a target table key; it is not an object
pointer, identity map, or lazy navigation property. A column may be both a key
and a reference, as in a one-to-one fact table.

The binder retains the key and typed target symbols; MIR retains a table key
column identity and a target table/key identity on each reference column. This
lets inspection report identity facts without parsing strings after binding.
`tscl table list`, `schema`, and `validate` use the same bound facts.

This is a compiled in-memory constraint surface, not a database, SQL engine,
ORM, dataframe API, or general query language. The existing `rows().where()`
and `select()` behavior remains unchanged. Joins, derived columnar tables,
computed relational columns, and relation provenance are not implemented by
this slice and require a dedicated relation-shape executor rather than a
parallel query syntax.
