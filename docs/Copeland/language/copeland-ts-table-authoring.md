# Table rows and per-row calculation locals

Tables can be authored as columns or rows. Both forms produce the same immutable,
columnar table, nominal row type, keys, and declared references. Choose rows when
entering a record's fields together helps; choose columns for repeated values or
whole-column comparisons.

```ts
record table Suppliers {
    key id: int;
    name: string;
    leadDays: int;
} = [
    { id: 10, name: "North Roastery", leadDays: 3 },
    { id: 20, name: "Leaf House", leadDays: 5 },
];
```

Row authoring requires schema-only columns with explicit types. Each entry must
be an object literal containing every declared field exactly once; property order
can differ from schema order. Quoted property names are supported. Values obey
the existing static, deeply immutable table-cell rules. Duplicate keys and broken
references remain compiler errors. An explicitly typed empty initializer is valid.

Fixed repetition also works for rows:

```ts
record table Flags { enabled: boolean; weight: int; } = [
    { enabled: true, weight: 4 } ... 3,
    { enabled: false, weight: 1 },
];
```

Counted repetition follows the existing bounded initializer rules. Zero repetitions
still check shape and types. Repeating a key twice fails uniqueness validation.
A row initializer has no independent extent for a bare `...` fill to follow, so
that form receives `COPE-INIT-0005`. Row data cannot be mixed with inline column
arrays or an asset source.

`table set`, `add-row`, `delete-row`, and CSV import preserve row authoring. An edit
to a repeated row splits that segment around the selected logical row. The compiler
validates the entire candidate before the CLI writes it, including references and
dependent snapshots. Existing `with` expressions continue to replace columns.

Derived plans can declare immutable per-row calculation locals before their output
columns:

```ts
record table Priced = derive Products as product {
    const units: int = if product.onHand < product.target {
        product.target - product.onHand
    } else { 0 };
    const cost = units * product.costCents;
    productId: int = product.id;
    orderUnits: int = units;
    orderCents: int = cost;
}
```

The local scope contains source and joined aliases plus earlier locals. Explicit
types are optional; inferred types follow ordinary expression binding. Self-reference,
forward references, duplicate locals, and alias shadowing are rejected. Output
columns have their own namespace, so `orderUnits: int = orderUnits;` is valid when
`orderUnits` is a local.

Each local evaluates once per source row, in declaration order, after relationship
lookups and before output projections. Unused locals still evaluate. They use
existing arithmetic, `if`, enum/Result `match`, record construction, and static
evaluation semantics. Runtime calls, assignments, `await`, mutable declarations,
and unsupported value types are rejected, including when nested in a larger expression.
`static` retains its ordinary requirement that its inputs be available at compile time.

Dependencies through locals remain in output-column provenance. A direct copy of
a source key or reference through a local retains that identity metadata. Locals
are represented in bound definitions and MIR; they are emitted as bindings inside
the C# row loop, rather than expanded into every use.

Authored row tables execute in C# and all three JavaScript emission profiles.
Derived tables, including locals, execute through C# and the canonical CLI relation
query host. JavaScript derived tables still stop at `COPE-JS-DERIVE-0001`.
Row authoring is host language syntax; TSON table documents retain column syntax
and reject host row initializers with `COPE-TSON-TABLE-0001`.

The [café workbook](../../../samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts)
uses both authoring forms and a shared purchasing local. The
[qualification report](../reviews/tablescript-row-authoring.md) records the bounded
evidence and remaining frictions.

Use [typed summaries](copeland-ts-table-summaries.md) to return named totals and
grouped record arrays from the authored or derived row views.
