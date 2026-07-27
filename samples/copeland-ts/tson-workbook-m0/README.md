# CTS-TSON-WORKBOOK-M0 fixture

This fixture is a source-authored compiled workbook. `Workbook/Workbook.ts` is
the authoritative data and query source. Its exported `Scores` and `Employees`
record tables compile into `Copeland.TsonWorkbookM0.dll`; the C# consumer uses
the generated typed table API directly.

Build and run from this directory:

```console
dotnet build
dotnet run --project Consumer/Consumer.csproj --no-build
```

The stable proof values are:

```text
sheets=Scores,Employees
rows=3,3
direct=81.5
view=2
average=89.17
engineering-average=93.00
original-bob=81.5
revised-bob=84.0
```

The trailing timing and allocation lines are measurements, not golden output.

The authored update law is a full-column replacement:

```ts
function revisedScores(): Scores {
    return Scores with {
        score: [95.0, 84.0, 91.0]
    };
}
```

Replacement arrays must be authored, closed, deeply immutable constants of the
declared column element type and exactly the original row count. The operation
returns another value of the same nominal table type. It does not change the
authored singleton.

`export record table` opts a table into the generated CLR surface. The generated
module exposes the named table, `RowCount`, named column accessors, `At` on
tables and columns, and named row fields. Non-exported tables retain the
compiler-private surface.

For M0, one `.ts` file is deliberately used: table declarations own their
inline authoritative values, and adjacent ordinary functions provide views and
aggregates. Existing `.obj.ts`/`.tson` table assets remain appropriate when a
logic-free data-only file is more important than keeping this very small
workbook together, but they are not required by this proof.

`bob-score.diff` captures the review shape of a direct source-data cell change.
No Excel, LibreOffice, Python, pandas, Node runtime, database server, data
directory, or previous database CLI is used by the .NET build and consumer.
