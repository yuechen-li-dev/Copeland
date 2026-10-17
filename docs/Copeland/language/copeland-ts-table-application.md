# Runtime keyed table edits and application boundaries

`withRow` creates an immutable snapshot by declared identity:

```ts
function edit(stock: Stock, productId: int, units: int): Stock ! TableKeyError {
    return stock.withRow({ productId }, { onHand: units });
}
```

The selector must be a literal containing exactly the declared key name. Its value
has the exact key type. The patch is a nonempty literal of existing ordinary
fields, with no duplicates and normally assignable field values. The binder fixes
table, key, and column identities before MIR lowering. There is no dynamic field
lookup or unchecked row setter in the emitted API.

The source, selector value, and patch values evaluate once, in written order,
including when the key is absent. A present key produces `ok(newSnapshot)`;
an absent key produces `err(TableKeyError.NotFound)`. Row count, order, key,
references, and the original snapshot are preserved. Backends use private
columnar storage. Updating key or reference fields is deliberately unsupported;
coordinated identity changes need a separate contract. Derived tables are read
only: edit authored inputs and explicitly recalculate the plan.

`withRow` is available on authored table values and declared authored singletons,
including resolved imports. The existing whole-column `with` still requires
static authored column literals. The new operation does not relax those rules or
introduce variable table cardinality. Direct use in async/generator bodies has a
named stop; call a synchronous helper instead.

| Diagnostic | Repair |
| --- | --- |
| `COPE-ROW-0001` | Use an authored table with a declared key. |
| `COPE-ROW-0002` | Supply the exact key selector and a nonempty patch literal. |
| `COPE-ROW-0003` | Use the declared key/field type. |
| `COPE-ROW-0004` | Remove unknown or duplicate patch fields. |
| `COPE-ROW-0005` | Keep key/reference identity unchanged. |
| `COPE-ROW-0006` | Move the edit to a synchronous helper. |

## Application validation

A table's structural legality does not impose application policy. For example,
the generic operation accepts a negative integer in an ordinary integer field.
The café application declares its policy using ordinary functions and Results:

```ts
enum StockError {
    NegativeCount(productId: int, units: int),
    UnknownProduct(productId: int),
}

function setOnHand(stock: Stock, productId: int, units: int): Stock ! StockError {
    if (units < 0) {
        return err(StockError.NegativeCount(productId, units));
    }
    return match stock.withRow({ productId }, { onHand: units }) {
        ok(updated) => ok(updated),
        err(error) => err(StockError.UnknownProduct(productId)),
    };
}
```

`type` aliases and interface constraints remain compile-time declarations; aliases
do not become runtime range proofs. Expected invalid input is a typed application
error. This pass does not promote test `Assert.*` into a new portable runtime API.

The application owns the complete `WorkbookInputs` snapshot and explicitly passes
all authored leaves to `PurchasePlan.recalculate`. It produces a `WorkbookView`
containing the plan, projected rows, typed totals, and supplier summaries. The
frontend owns drafts, invokes that application function, matches success/failure,
and renders the accepted view. It does not rewrite formulas or mutate table cells.

C# and JavaScript execute the same bound relation expressions and MIR dependency
schedule. JavaScript supports authored and derived tables, snapshot recalculation,
and typed sum/count summaries in all emission profiles. Group order is first
seen after filtering; string keys are case sensitive. Integer summary sums wrap
at 32 bits, including under checked C# compilation. Enum payload grouping follows
the existing C# equality contract; nested immutable record/array payloads retain
identity rather than receiving a new deep-equality rule.

Local module imports preserve nominal table metadata and dependency plans.
JavaScript project modules share table slots, type tokens, and Result identities
through the existing module runtime conventions. Importing a table does not make
its module-private names available to source lookup. Immutable tables may be
stored in component state; mutable storage and host handles remain disallowed.

The [React café sample](../../../samples/copeland-ts/tablescript-application-m13/README.md)
qualifies this boundary through a local preview. Browser execution does not confer
server authority: a future persistence endpoint must validate commands against
server-owned inputs. Network commit, concurrency resolution, variable row count,
coordinated key rewrites, and typed cross-snapshot join failures remain separate
work. A declared join that cannot resolve its input snapshot still raises the
existing invariant failure; neither backend silently drops rows or substitutes
zeroes.
