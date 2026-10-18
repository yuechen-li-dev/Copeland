# CTS-TABLE-DOGFOOD-M0 fixture

`Workbook/Workbook.ts` is a compact Git-managed product catalog. It contains
four related typed record tables, read-only single- and multi-source derived
tables, ordinary typed queries, an immutable price revision, and a normal C#
consumer. `ProductCatalog` proves many-to-one and one-to-one declared-reference
joins; `InventoryCatalog` proves a chained join and aggregate. Derived tables
remain columnar with exact schema/provenance. Grouping and persistence remain
deferred.

Build and run from this directory:

```console
dotnet build TableDogfoodM0.slnx -m:1
dotnet run --project Consumer/Consumer.csproj --no-build
```

The detailed workflow and findings are in
`docs/Copeland/reviews/cts-table-dogfood-m0.md`.

The consumer asserts query counts, summaries, declared-reference/chained joins,
enum projection, and preservation of the original price snapshot. A failed result
exits with an error. The earlier unkeyed/manual-lookup variant is preserved in
[`ManualCatalogTests`](../../../tests/Copeland/Copeland.TS.Tests/ManualCatalogTests.cs),
including C# and all three JavaScript profiles; it no longer needs a copied sample
project. See the [current example index](../README.md) for the full café editor.
