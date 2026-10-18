# TableScript friction M12: café restocking

## Result

The motivating scalar workbook works through the real Copeland TS compiler,
MSBuild C# output, and default `csharp-relation-plan` query path. A formula-based
Excel workbook computes the same results from the same raw inputs. This qualifies
workbook authoring and calculation, not an interactive spreadsheet application or
a universal claim that Copeland replaces Excel.

| Case | Orders | Units | Cents |
| --- | ---: | ---: | ---: |
| Original stock | 3 | 67 | 31,210 |
| Espresso stock changed from 4 to 12 | 2 | 47 | 12,710 |

The original supplier totals are North Roastery 18,500 cents, Leaf House 4,960
cents, and Pantry Co 7,750 cents. Shuffled stock rows prove that product keys own
the lookup. Paused decaf remains excluded, and cocoa at the reorder threshold is
excluded. The authoring model uses integer cents with bounded synthetic quantities;
`Cents` is an alias for `int`, not a money type with range, overflow, or unit proofs.

## What reads better

The Copeland policy is written once:

```typescript
const orderUnits: Units = match product.purchasing {
    Active => if stock.onHand < product.reorderPoint && stock.onHand < product.targetStock {
        product.targetStock - stock.onHand
    } else { 0 },
    Paused => 0,
};
```

The Excel baseline uses the corresponding filled-down formula:

```excel
=IF(G13="Paused",0,IF(AND(D13<E13,D13<F13),F13-D13,0))
```

Copeland makes the policy, units, and relationships legible without tracing cell
coordinates. It also gives us exhaustive enum branches, typed callback aliases,
explicit capture, constrained generic row projections, compiler diagnostics, and
reviewable source changes. Supplier lookup is a declared reference followed by a
join. The Excel baseline gives readers a compact familiar grid, editable cells,
table filters, input validation, and visible totals with little setup.

The comparison workbook has two sheets: Restock for decisions and Inputs for the
three raw relations. Supplier summaries group by supplier ID using aligned product
inputs; stock and supplier names use keyed `INDEX/MATCH`. The six-row comparison
does not qualify automatic growth of those bounded formula ranges. Formula
evaluation was performed by artifact-tool, not desktop Excel.

## Fixes found by the real comparison

1. **Enum query cells lost their cases.** The initial `Products` JSON query returned
   `{}` for both Active and Paused. The CLI normalized only `System.Enum`, while
   Copeland emits nested record cases. Typed result normalization now uses the
   compiler's enum metadata and declared payload order. Zero-payload cases render
   as names; scalar and nested-enum payloads render with the same `case`/`values`
   shape as authored `table rows`. JSON and CSV regression checks cover distinct
   cases, integer payloads, multiple payloads, and nested enum payloads. Arbitrary
   record/array/result payload projection is not qualified by this fix.
2. **`match` lost formula provenance.** `Restock.orderUnits` initially reported
   empty dependencies and relationships. Both binder collectors now traverse the
   scrutinee and every arm. The schema reports `purchasing`, `reorderPoint`,
   `stock.onHand`, and `targetStock`, plus the stock-to-product relationship.
   A compiler regression checks this metadata independently of rendered output.

Neither fix moves calculation or relationship authority into the CLI renderer or
the Excel builder. The CLI only projects already-calculated typed results.

## Remaining friction

| Operation | Current experience |
| --- | --- |
| Enter related records | [Row authoring](tablescript-row-authoring.md) keeps business fields together and CLI edits preserve that form. Column authoring remains available; an interactive entry grid is still separate work. |
| Reuse a calculated column | Per-row `const` locals share calculations within one `derive`. The café policy no longer needs an intermediate restocking relation. |
| Write summaries | [Typed summaries](tablescript-typed-summaries.md) return declared records and grouped record arrays with `sum`/`count` through C#. Average/min/max remain deferred; the [application pass](tablescript-application-m13.md) qualifies JS sum/count execution. |
| Edit by identity | [Key-addressed edits](tablescript-key-addressed-edits.md) support `--key` for `set` and `delete-row`, independently of stock order. Runtime [`withRow`](../language/copeland-ts-table-application.md) adds immutable application edits. |
| Preview scenarios | [Snapshot-relative recalculation](tablescript-snapshot-scenarios.md) runs `PurchasePlan.recalculate({ Stock: revised })` through C# and JavaScript, including transitive derived dependencies. It preserves globals and source; source-copy queries serve as an independent oracle. |
| Validate domain rules | Keys, references, types, and column lengths are checked. Aliases do not impose nonnegative stock, a valid target range, or currency units. The [application pass](tablescript-application-m13.md) now owns nonnegative-stock validation with typed Results; aliases remain transparent. |
| Use the frontend | The [application pass](tablescript-application-m13.md) qualifies React execution. The [editing pass](tablescript-editing-m14.md) adds typed drafts and Apply/Cancel/Select/Reset. The [variable-row pass](tablescript-rows-m15.md) adds atomic create/delete, key/reference checks, empty states and checked recalculation. Remote persistence remains separate. |

Two broader probes are retained under the sample's `inputs/` and in qualification
JSON. Array-valued TSON query columns fail with `COPE-TABLE-QUERY-0016` because
generated C# references an unmapped `array` type. The optional legacy executor
fails with the same code because it cannot expose the generated row's source-name
property. These are concrete deferred boundaries; the default scalar executor
used here works. No legacy or arbitrary structured-cell parity claim is made.

The authoring, calculation reuse, summary, identity-edit, and scenario passes now
have qualified implementations. The [application and frontend pass](tablescript-application-m13.md) now qualifies
ordinary application-owned validation and JavaScript derived-table execution in a
real React preview. The [variable-row pass](tablescript-rows-m15.md) now qualifies
changing cardinality and typed relationship failures. Remaining work includes
broader domain policies and authoritative remote commits. Transparent aliases still do not
become money or range proofs.

The follow-up [ellipsis reconciliation](tablescript-ellipsis-reconciliation.md)
reserves `...` for bounded postfix initializer repetition, audits Concept/Oct
semantics, and records the source-editing requirements before implementation.
The [first implementation step](tablescript-ellipsis-implementation.md) now supports
counted repetition, remainder fills, both host backends, and logical-cell source
edits. The audit also fixed malformed table parser recovery. This workbook now
uses the new initializer forms with the same logical inputs and calculated totals.

The [second implementation step](tablescript-key-addressed-edits.md) adds `--key`
selection to `set` and `delete-row`. The café scenario now targets Stock's
`productId = 100`, and a separate qualification repeats that edit after reversing
stock order. Its canonical output is identical in both cases.

The [row authoring pass](tablescript-row-authoring.md),
[typed summary pass](tablescript-typed-summaries.md), and
[snapshot scenario pass](tablescript-snapshot-scenarios.md) record their later
implementation and validation. The validation below is the original M12 baseline;
the linked reports contain follow-up evidence and test totals.

## Validation

- `dotnet build Copeland.slnx -m:1 --nologo`: zero warnings/errors.
- Full `Copeland.slnx` tests: 2,279 passed, zero failed/skipped.
- Generated workbook consumer: 13 assertions, including capture, shape-constrained
  row projection, threshold policy, shuffled joins, and immutable `with` behavior.
- `qualify.ps1`: 66 checks over canonical queries, grouped totals, dependency schema, dry-run and
  real source-copy edits, rejected edits with unchanged source hashes, and named
  broader boundaries. Evidence is in `artifacts/tablescript-friction-m12/`.
- Excel: every row's identity, name, supplier, stock, quantity, and cents checked
  against Copeland before/after the stock edit; supplier totals checked; baseline
  restored; no formula errors; both worksheet previews inspected.

Native Excel interaction, live browser rendering, large-table performance, array
query execution, arbitrary structured enum payloads, and legacy execution remain
unqualified. No unrelated language grammar was changed.
