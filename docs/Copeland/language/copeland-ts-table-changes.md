# Variable-cardinality table snapshots

Table declarations own nominal identity, columns, keys, references and initial
data. Runtime snapshots own their actual row count. An authored `Products` value
can contain zero, six or seven rows without changing its nominal type. Storage
remains immutable and columnar; the declaration's row count describes its initial
data, not every future value.

## Atomic changes

Use an ordinary input record to identify the application-owned snapshot set:

```ts
record WorkbookInputs {
    products: Products;
    stock: Stock;
    suppliers: Suppliers;
}

function removeProduct(inputs: WorkbookInputs, productId: int): WorkbookInputs ! TableEditError {
    return Tables.apply(inputs, {
        products: [Products.deleteRow({ id: productId })],
        stock: [Stock.deleteRow({ productId })],
    });
}
```

`Tables.apply` is a compiler-known operation with a normal call spelling. Its
first argument is a nominal record with one direct field per owned authored,
keyed table. Ordinary metadata fields are preserved. Every declared reference
target must be present exactly once. Class inputs, derived inputs, duplicate
nominal table snapshots, and missing targets receive diagnostics. The application
chooses its ownership boundary; the operation does not discover incoming
references in external application aggregates or use hidden global snapshots.

Its second argument is a literal naming input-record fields. Each field has an
explicit array of descriptions made on the corresponding table declaration:

```ts
Tables.apply(inputs, {
    products: [Products.insertRow({
        id: request.productId,
        supplierId: request.supplierId,
        name: request.name,
        costCents: request.costCents,
        reorderPoint: 8,
        targetStock: 24,
        purchasing: Purchasing.Active,
    })],
    stock: [Stock.insertRow({
        productId: request.productId,
        onHand: request.onHand,
    })],
});
```

Insert a complete literal with every declared field exactly once, or a value of
the exact nominal authored table row type. Delete with a literal containing only
the declared key. Descriptions are currently compiler-bound directly within
`Tables.apply`; storing descriptions or supplying computed change arrays is not
implemented. A normal application function can construct the batch from a typed
request. There is no mutable transaction handle or partially constructed table
value exposed to application code.

The input expression evaluates first. All description operands then evaluate
once in written field/array/property order, including operands after an operation
that will be refused. Operations apply to private candidate snapshots in that
same order. Insert requires an absent key; delete requires a present key. A
second operation on the same table/key in one batch is rejected, including
delete/reinsert. There is no implicit upsert, cascade, rekey, or reference rewrite.

After operations finish, all owned tables are checked for unique keys and every
declared reference is checked against the final candidate targets. Validation
does not depend on which relationships a calculation happens to read. This
permits coordinated parent/child insertions and deletions in either table order.
A referenced parent cannot disappear while a surviving child still refers to it.

Success returns one new input record. Failure returns a typed error and leaves
every source snapshot intact. Existing row order survives; inserted rows append
in operation order. Keys retain their existing exact equality semantics, including
case-sensitive strings. No key is generated from a row position or count.

The first implementation admits at most 65536 explicit operations in a batch and
100000 rows per final table snapshot. A row limit is an integrity/resource bound,
not a large-table performance qualification. Candidate tables and copied columns
are local allocations; no database, WAL or durability mechanism is introduced.

## Results and diagnostics

`TableEditError` is compiler-owned and automatically available:

| Case | Meaning |
| --- | --- |
| `DuplicateKey(table, key)` | An insertion targets a present key, or final keys are not unique. |
| `NotFound(table, key)` | Deletion targets an absent key. |
| `ConflictingEdit(table, key)` | The batch targets the same table/key more than once. |
| `MissingReference(table, column, key)` | A final reference value has no target key. This also implements restricted parent deletion. |
| `RowLimit(table)` | A final snapshot exceeds the row limit. |

Location payloads are invariant strings for reporting; they are not dynamic table
selectors. Pattern variables can use names such as `tableName` and `keyValue`.
Error selection is deterministic: operation failures in written order, followed
by key/limit checks in input-record declaration order, then references in
table/column/row order. Consumers should match enum cases rather than message text.

`COPE-CHANGE-0001` through `0007` identify an invalid call shape, ownership set,
description/field shape, field type, asynchronous use, or explicit operation
budget. Move async/generator edits into a synchronous application helper.

## Checked calculation

Reference integrity does not prove join completeness. For example,
`Stock.productId -> Products.id` does not require every product to have stock.
Use the checked entry point when an application admits changing input membership:

```ts
function calculate(inputs: WorkbookInputs): PurchasePlan ! TableRelationError {
    return PurchasePlan.tryRecalculate({
        Products: inputs.products,
        Stock: inputs.stock,
        Suppliers: inputs.suppliers,
    });
}
```

Every authored dependency must be explicit (`COPE-RECALC-0006` otherwise).
The existing relation schedule and implementation run once. An unresolved lookup
returns `TableRelationError.Unresolved(relation)`; a non-unique lookup returns
`NonUnique(relation)`. Only relationship guard failures are converted. Other
computation failures retain their existing behavior. Ordinary `recalculate`
retains its existing invariant-exception contract and optional global defaults.

Iteration, selection, joins, bounds, column counts, aggregates and summaries use
actual snapshot lengths on both backends. Empty sums/counts are zero and grouped
summaries are empty. Min/max/average require nonempty input. Static whole-column
`with` retains its declared literal-shape rules and checks actual column alignment;
use `withRow` for ordinary field edits and `Tables.apply` for membership changes.
Table TSON encoding still accepts declaration-owned singletons only.

## Application and frontend ownership

The generic operation checks structural integrity. Application functions still
own positive IDs, nonnegative stock, price bounds and required companion rows.
Only publish a new frontend view after domain validation, atomic changes and
checked calculation succeed. A browser preview never grants persistence
authority: a server must validate typed commands against server-owned inputs.

The [café sample](../../../samples/copeland-ts/tablescript-rows-m15/README.md)
uses optional selection, existing typed drafts, and explicit create/delete
events. It demonstrates empty tables without a fake selected row. See the
[qualification](../reviews/tablescript-rows-m15.md) for tested boundaries.
