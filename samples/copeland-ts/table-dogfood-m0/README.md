# CTS-TABLE-DOGFOOD-M0 fixture

`Workbook/Workbook.ts` is a compact Git-managed product catalog. It contains
four related typed record tables, a read-only single-source derived table,
ordinary typed queries, an immutable price revision, and a normal C# consumer.
Derived tables remain columnar and retain exact schema/provenance; joins,
grouping, and persistence are intentionally deferred.

Build and run from this directory:

```console
dotnet build TableDogfoodM0.slnx --no-restore
dotnet run --project Consumer/Consumer.csproj --no-build
```

The detailed workflow and findings are in
`docs/Copeland/reviews/cts-table-dogfood-m0.md`.
