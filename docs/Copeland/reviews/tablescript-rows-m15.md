# TableScript M15: variable rows

The café can now create and remove products while keeping product/stock changes
atomic. Runtime snapshots retain their nominal table types at zero, six or seven
rows. The compiler and both backends preserve columnar storage, keys, references,
source snapshots and application-owned calculation.

## OctetDB audit and reuse

The local OctetDB checkout was found at
`C:/Users/yuech/source/repos/yuechen-li-dev/OctetDB`, revision
`0c83607842590839741b6f5b46ce95ce90810be8`. Its canonical
[`Database.Mutate` / `Tx` implementation](https://github.com/yuechen-li-dev/OctetDB/blob/main/catalog.go)
and cross-dataset atomicity tests informed the explicit snapshot-set boundary,
staging, final-candidate checks, deterministic behavior and refusal without
publication. OctetDB's `Put` is replacement/upsert and its opaque JSON catalog
does not supply Copeland's declared reference checks.

Copeland reuses its own binder facts, MIR table/column/key/reference identities,
relation schedules, immutable record machinery, Result handling and both existing
backends. No Go engine was ported, and no persistence, WAL, retry ledger or
independent relationship interpreter was added. Durability and authoritative
command deduplication remain backend responsibilities.

## Implementation

[`Tables.apply`](../language/copeland-ts-table-changes.md) binds an ordinary input
record and literal arrays of typed insert/delete descriptions. It constructs
private candidate snapshots and validates all final keys/references before
returning one new record. Expected refusals are `TableEditError` cases. Missing
targets and ambiguous ownership sets receive source diagnostics. Protected
`withRow` fields remain protected; derived tables remain read-only.

Per-snapshot lengths now drive C#/JavaScript row/column bounds, counts, loops,
selection, joins, aggregate iteration and summaries. The authored declaration's
count remains initial-data metadata. Stable row order and append order are
preserved; empty sums/counts are zero and grouped summaries are empty.

`tryRecalculate` requires explicit authored inputs and runs the existing relation
schedule. It returns typed unresolved/non-unique relationship failures. Ordinary
`recalculate` retains its existing exact exception behavior. The checked path
only catches compiler-owned relationship failures, not unrelated calculations.

The motivating example uses ordinary application functions in
[`Rows.ts`](../../../samples/copeland-ts/tablescript-rows-m15/src/Rows.ts).
Creation checks ID/name/stock/price policy, inserts product and stock together,
then checks/calculates the plan. Removal explicitly deletes both rows. Supplier
removal is restricted by surviving references. The same domain source executes
through C# and JavaScript.

The original workbook now exports its `Purchasing` enum for typed row creation.
M13 exposes its existing view projection as `viewFromPlan`; M14 exposes its
existing price check as `validatePrice`. These small reuse points retain one
purchasing formula, one projection and one price policy. Existing results remain
unchanged.

The frontend keeps one accepted view and an optional selected-product draft. It
delegates existing stock/price edits to the M14 reducer. Creation uses the same
generic draft library. Deleting the selected key clears selection; deleting
another key retains the selected key and incomplete text. An empty table has an
explicit empty state. No foreign input owns numeric parsing or domain policy.

## Motivating evidence

| Scenario | Products | Cents | Units | Orders |
| --- | ---: | ---: | ---: | ---: |
| Original café | 6 | 31210 | 67 | 3 |
| Create 106, stock 2, price 5.00 | 7 | 42210 | 89 | 4 |
| Edit its stock to 3 | 7 | 41710 | 88 | 4 |
| Edit its price to 6.00 | 7 | 43810 | 88 | 4 |
| Remove 106 | 6 | 31210 | 67 | 3 |
| Remove every product | 0 | 0 | 0 | 0 |
| Create from empty, stock 2, price 5.00 | 1 | 11000 | 22 | 1 |
| Reset | 6 | 31210 | 67 | 3 |

Duplicate keys, absent suppliers, negative stock and incomplete price text are
refused with unchanged accepted rows. A public 14-value domain probe agrees
between C# and every browser profile. Canonical global totals remain 31210.

Variable cardinality also exposed the café's finite total range. Its ordinary
application `validateView` refuses a purchase total that cannot fit in signed
32-bit cents, while generic TableScript arithmetic retains its established wrap
policy. Both backends accept a 95-product plan totaling 2136031210 cents and
refuse the next high-priced row. A price edit that would exceed that limit also
preserves the prior accepted plan and price baseline. These are application
Result probes, executed by the C# harness and each browser profile.

Six browser workflows run React StrictMode in Diagnostic, Symbolic and Production
at 1280 and 390 pixels. They exercise creation/refusal, appended-row stock/price
edits, keyed removal, unrelated-draft preservation, empty selection/table states,
creation from empty and reset. There are no console warnings, page errors, failed
HTTP responses, unresolved description IDs or page-wide horizontal overflow.
Desktop and mobile screenshots were inspected.

## Validation

```powershell
./samples/copeland-ts/tablescript-rows-m15/run.ps1
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx --no-build -m:1 --nologo
```

The full solution builds with .NET SDK 10.0.401 with zero warnings and errors.
All 2463 tests pass, with zero failures or skips, including 17 new cardinality
cases. The sample's C# execution, strict TypeScript check, bundles and all six
browser workflows pass. The original M12 qualification also passes all 66 checks
through `csharp-relation-plan`: baseline 31210 cents and edited 12710 cents.
Frozen Zig suites were skipped because no legacy Zig compiler or build/test
infrastructure changed.

The new regression suite exercises malformed source and MIR, ownership closure,
strict insert/delete, deterministic operand evaluation even on failure,
immutability, reverse-join failure, nominal rows, generic inputs, case-sensitive
string keys and empty authored/runtime tables through C# and all JavaScript
profiles. The consumer also exposed a TSX privacy scan using the wrong source
kind and an unchecked parser cast for a generic constructor missing its argument
list; both now produce normal parsing behavior rather than a compiler exception.

Evidence: [compiler/source hashes](evidence/tablescript-rows-m15/compiler-report.json),
[browser observations](evidence/tablescript-rows-m15/browser-report.json), and
[validation summary](evidence/tablescript-rows-m15/validation-report.json).
Full build/test logs are in `artifacts/validation/tablescript-rows-m15-*`.

Pinned table emission artifacts and their byte lengths/hashes were refreshed
through the real CLI compiler for the new count carriers and relationship guard
support. Their exact comparisons and executable TSON fixed-point checks remain
enabled. Existing serialized TSON and MIR input fixtures were not changed.

## Bounded initial contract

Change descriptions are direct, compiler-bound batch expressions. Stored or
computed change arrays, async/generator lowering of direct changes, key/reference
rewrites and cascades are not implemented. Apply from a synchronous application
helper. Row/operation bounds are explicit; large-table performance is unqualified.

Integrity is checked within the explicit application-owned input record. The
application must include the data it owns; the compiler cannot prove the absence
of incoming references in an external database. Browser-produced snapshots do not
confer persistence authority. Remote commits, concurrency/idempotency protocols,
dynamic table TSON encoding and Native AOT performance remain separate work.
