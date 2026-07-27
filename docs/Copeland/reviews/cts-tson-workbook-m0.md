# CTS-TSON-WORKBOOK-M0 — Git-native compiled workbook

## Result

CTS-TSON-WORKBOOK-M0 is honestly complete for the bounded M0 question.

The proof answers yes: small Excel-like typed tabular data can live directly in
readable Copeland source, receive compiler validation, produce a normal .NET
library through `dotnet build`, be reviewed as text, and support immutable
derived snapshots and ordinary compiled queries without Excel, LibreOffice,
VBA, Python, pandas, a database server, or an external data directory.

This is not a general database result. The fixture has six rows across two
sheets. Its positive result applies to small, Git-managed workbook/asset data.
Scale remains an explicit follow-up question.

The prior CTS-TSON-DATABASE-M0 experiment tested an external immutable
column-store architecture: routed leaf directories, segment files, manifests,
generated readers, and storage-engine operations. This milestone tests a
different hypothesis: source-authored compiled workbook data. None of the
external segment-tree architecture is used by the primary proof.

## Final organization

```text
samples/copeland-ts/tson-workbook-m0/
├─ TsonWorkbookM0.slnx
├─ README.md
├─ bob-score.diff
├─ Workbook/
│  ├─ Workbook.csproj
│  └─ Workbook.ts
└─ Consumer/
   ├─ Consumer.csproj
   └─ Program.cs
```

The implementation also adds focused compiler/backend tests and the bounded
frontend, MIR, C#, and JavaScript support required by immutable table
derivation.

## Corrected hypothesis and model

The implemented model is:

```text
one typed Copeland module
├─ exported authored record table Scores
├─ exported authored record table Employees
├─ typed projection record ScoreView
└─ ordinary query/aggregate/derivation functions
        ↓ dotnet build
Copeland.TsonWorkbookM0.dll
        ↓ project reference
normal C# consumer
```

The workbook hierarchy is semantic and named, not a physical index:

```text
Workbook module
├─ Scores
└─ Employees
```

This differs from the illustrative `record Workbook { Scores: Scores; ... }`.
The existing language makes a `record table` declaration both the nominal type
and its authored singleton; it is not a freely constructible schema. The
cleanest honest M0 hierarchy is therefore the module plus exported named table
values. Inventing constructible arbitrary table values or nested table records
would have widened the experiment substantially.

## Authored syntax and record-table law

The authoritative source uses existing inline authored table syntax:

```ts
export record table Scores {
    employeeId: int = [1, 2, 3];
    name: string = ["Alice", "Bob", "Carol"];
    score: number = [95.0, 81.5, 91.0];
}

export record table Employees {
    id: int = [1, 2, 3];
    name: string = ["Alice", "Bob", "Carol"];
    department: Department = [
        Department.Engineering,
        Department.Sales,
        Department.Engineering
    ];
}
```

For M0, a record table remains:

- one nominal immutable table type;
- one authored singleton value;
- declaration-ordered, statically typed columns;
- a fixed row count shared by every column;
- direct column access and Result-valued row/element access;
- generated arrays owned by the assembly, not external segments;
- no keys, mutable rows, SQL relation, storage directory, or runtime database.

Existing binding already rejects ragged declarations (`COPE-TABLE-0008`),
incompatible column cells (`COPE-TABLE-0007` or ordinary contextual type
diagnostics), nonconstant/mutable cells (`COPE-TABLE-0009`), recursive table
data, row construction, mutation, and equality.

## TSON role and logic boundary

One ordinary `.ts` file is the cleanest current M0 experience. Table
declarations own both schema and inline authoritative cells, while adjacent
functions own executable views. This keeps the six-row proof readable and
requires no asset loading.

The existing alternatives remain meaningful:

- `.obj.ts` is permissive authoring syntax with comments/layout and compile-time
  schema validation;
- `.tson` requires canonical bytes and embedded identity;
- a TSON table document has one nominal table root;
- TSON restriction passes reject imports, functions, calls, assignments,
  loops, conditionals, filesystem/network/process logic, and other executable
  forms.

Those facts make `.obj.ts`/`.tson` attractive for a large logic-free individual
sheet, but less clean for this two-sheet M0: each TSON table is a separate root,
and tables cannot be nested inside a TSON record root. The fixture's `.ts`
module therefore uses a documented convention rather than a new enforced
data/query file boundary. Enforcing a multi-sheet data-only workbook document
would require a new TSON workbook-root algebra and was not justified here.

There is no runtime serialization of application objects into TSON. Authored
source is compiled directly.

## Immutable update law

Existing `with` supported records but deliberately rejected tables. The
smallest coherent extension reuses the existing named replacement grammar and
replaces complete columns:

```ts
function revisedScores(): Scores {
    return Scores with {
        score: [95.0, 84.0, 91.0]
    };
}
```

The law is:

```text
table value
+ one or more authored full-column constant replacements
+ same element types
+ same row count
→ new value of the same nominal table type
```

Replacement expressions must be authored array literals. Their cells must be
closed deeply immutable table constants. A replacement with two cells for a
three-row table fails with `COPE-TABLE-0008`; a runtime array variable fails
with `COPE-TABLE-0022`; an incompatible cell fails at its source position.
Empty and duplicate replacement sets have dedicated `0020` and `0021`
diagnostics.

The C# realization allocates only the replacement column array and a new table
carrier; untouched column arrays are shared. The JavaScript realization creates
a new frozen carrier and frozen replacement columns. No mutable table editing
API was added.

Runtime evidence:

```text
original-bob=81.5
revised-bob=84.0
```

Repeated reads of the authored singleton still return `81.5`.

## Query and view law

Queries are ordinary typed Copeland functions, not SQL or a LINQ provider.

Direct access:

```ts
function originalBobScore(): number {
    return Scores.score[1]!;
}
```

Aggregate:

```ts
function averageScore(scores: Scores): number {
    return (
        scores.score[0]!
        + scores.score[1]!
        + scores.score[2]!
    ) / 3.0;
}
```

Typed projection:

```ts
function highScores(scores: Scores): ScoreView[] {
    return [
        { name: scores.name[0]!, score: scores.score[0]! },
        { name: scores.name[2]!, score: scores.score[2]! }
    ];
}
```

The fixture also performs an inexpensive cross-sheet calculation:
`engineeringAverage()` reads `Employees.department` and the positionally
corresponding `Scores.score` cells and returns `93.00`.

Proof output:

```text
direct=81.5
view=2
average=89.17
engineering-average=93.00
```

This is useful compiled programming-language access, but table query ergonomics
are still primitive. There is no table iterator, dynamic `where`, table-valued
projection, key lookup, or join. The two-row high-score view is an explicit
typed projection for the known fixture, not a general filter operator.

## Generated artifact and DLL API

`dotnet build` from the fixture directory builds the solution, the workbook
library, and the consumer. The workbook's Release artifact is:

```text
Workbook/bin/Release/net10.0/Copeland.TsonWorkbookM0.dll
```

`export record table` is now an explicit opt-in to a generated CLR read surface.
The module exposes named table properties. Exported tables expose `RowCount`,
named column properties, `At(double)` for Result-valued table/column access, and
named row fields. Storage arrays, constructors, copy helpers, stable-ID helper
names, and layout remain private backend details.

Conceptual C# use, proven by `Consumer/Program.cs`, is:

```csharp
var original = WorkbookData.data();
var revised = WorkbookData.revisedScores();
var bob = original.score.At(1).Value;
var average = WorkbookData.averageScore(original);
```

The source values lower to ordinary generated C# arrays inside the assembly.
No embedded resource was necessary for six rows. This is the smallest honest
M0 embedding mechanism.

## Git diff evidence

`bob-score.diff` records the direct authoritative cell edit:

```diff
-    score: number = [95.0, 81.5, 91.0];
+    score: number = [95.0, 84.0, 91.0];
```

One conceptual cell change is one stable textual line change. Source and column
ordering are authored and deterministic. This is substantially easier to
review than an `.xlsx` container diff, while making no claim that `.xlsx` files
cannot be versioned.

The immutable derived revision is also localized: one `with` expression and one
replacement column. Because current storage is column-authored, a cell update
repeats that small column in source. A future row-oriented authoring view would
improve isolated row insertion diffs but would conflict with the current
canonical column law unless designed explicitly.

## Measurements and determinism

Environment: Windows, .NET SDK 10.0.302, Release fixture build. Two consecutive
full `Rebuild` operations used identical source and disabled shared compilation
to make the timing boundary explicit.

| Evidence | Result |
|---|---:|
| `Workbook.ts` size | 1,650 bytes |
| generated `Workbook.g.cs` size | 19,838 bytes |
| workbook DLL size | 19,456 bytes |
| first full rebuild | 12,115.978 ms |
| second full rebuild | 12,058.815 ms |
| generated C# SHA-256, both builds | `8BCA779F70D3FCEA50E5064F4BD80F4A32A889DF398BD1AAE2366809D4259183` |
| DLL SHA-256, both builds | `FC68A8936C76213EE0FBC08C56B9C1568C619ED87A382CA4E73EFF85112BC369` |
| first workbook access | 0.720 ms |
| first-access thread allocation | 872 bytes |
| 100,000 aggregate queries | 13.007 ms |
| measured query-loop allocation | 40 bytes |

The load/query figures are one local run and include JIT/timer effects; they are
architecture evidence, not a benchmark. The equal hashes prove deterministic
generated source and DLL output for identical inputs on this toolchain.

Generated C# is about twelve times the authored source for this tiny fixture,
because generic Result/table support dominates. It remains readable and
manageable at this size. Direct array literals will grow linearly with cells and
will eventually hurt source generation, C# parsing, metadata/IL size, build
time, and eager static memory.

## Validation

Focused validation proves:

- authored table declarations and stable nominal identities;
- full-column table `with` binding and deterministic MIR;
- row-count, authored-literal, and element-type failures with nonempty source
  positions;
- immutable original/derived behavior;
- repeated deterministic C# and JavaScript emission;
- C#/Node result parity for the new update;
- generated C# compilation;
- exported CLR table, column, row-count, and element access;
- normal MSBuild DLL generation and a normal project-reference consumer;
- direct access, typed projection, aggregate, and cross-sheet calculation.

Commands and outcomes:

| Validation | Outcome |
|---|---|
| fixture `dotnet build` | pass, 0 warnings / 0 errors |
| fixture consumer | pass with expected values |
| `Copeland.TS.MSBuild.Tests` | 9/9 pass |
| `TableFeatureTests` | 56/56 pass |
| table-with C#/JavaScript parity test | pass |
| full `Copeland.TS.Tests` | 883 pass, 1 inherited pinned-artifact failure |
| full C# backend suite | 238 pass, 4 inherited unrelated failures |
| full JavaScript backend suite | 173 pass, 6 inherited unrelated failures |

The inherited failures reproduce outside the workbook path:

- `NominalUnionTests.Nominal_union_corpus_artifacts_have_stable_bytes_and_hashes`
  expects a 1,268-byte checked-in C# artifact that is currently 1,320 bytes;
- two callable corpus tests disagree with already changed callable host-carrier
  emission;
- two unrelated checked-in C# hash pins (`inferred-reuse` and pure class) are
  stale;
- JavaScript async/npm assertions expect an older frame shape;
- pure-class/callable JavaScript pins expect the older callable carrier.

No unrelated pinned artifacts or expectations were rewritten. All table-related
regressions are passing.

## Excel comparison

| Concern | Excel workbook | Copeland workbook M0 |
|---|---|---|
| Source diff | ZIP/XML container; possible to version, awkward to review directly | typed textual source and small Git diffs |
| Runtime | Excel or a compatible workbook application for full behavior | ordinary .NET runtime |
| Scripting | formulas, VBA, Office object model | Copeland functions |
| Schema | usually informal cell/column conventions | compiler-known nominal tables and column types |
| Validation | rich interactive rules plus runtime/manual checking | compile-time shape/type/constant validation |
| Updates | interactive mutable document | source edit or explicit immutable derivation |
| Deployment | `.xlsx` plus compatible runtime | DLL/application artifact |
| Review | open/inspect workbook, specialized diff tooling | ordinary source review and tests |

Excel remains far ahead for interactive editing, formulas, recalculation graphs,
formatting, charts, pivot tables, ad hoc exploration, accessibility, import/
export, and broad user familiarity. Those features do not naturally follow from
compiled immutable tables and are conscious non-goals.

## Pandas comparison

| Concern | pandas | Copeland workbook M0 |
|---|---|---|
| Shape/types | dynamic dataframe/dtypes, runtime checks common | compile-time nominal table and column types |
| Environment | Python plus pandas/native dependencies | compiled .NET library |
| Update style | mutable and copy-returning workflows both common | authored immutable singleton plus explicit derivation |
| Query breadth | extensive dynamic filtering/grouping/join/reshape | direct access and ordinary hand-written functions |
| Data scale | designed for substantial runtime data | proven only for tiny compiled assets |
| Git source | usually code plus external CSV/Parquet/etc. | data itself can be reviewed source |

Static asset-like datasets and repeated typed deployment become simpler.
Exploratory loading, cleaning, missing-data work, dynamic columns, vectorized
analytics, group-by, reshaping, and large external datasets become much harder.

## Experimental questions

1. **Can authored record tables represent sheets cleanly?** Yes for small
   columnar sheets. The declaration is compact, typed, rectangular, and
   immutable.
2. **One `.ts` or `.obj.ts`/`.tson` separation?** One `.ts` is cleanest for
   this two-sheet M0. Data-only assets are cleaner per large sheet, but current
   TSON has a single table root rather than a workbook root.
3. **How should immutable updates be expressed?** Whole-column
   `table with { column: [closed constants] }` is the smallest coherent current
   law. Cell/row helpers should wait for a row/value-construction design.
4. **Are Git diffs readable?** Yes for cell replacement; row insertion in
   columnar syntax touches every column and is less attractive.
5. **How should large values be embedded?** Direct generated arrays for M0.
   Measure before choosing deterministic assembly resources for larger data.
6. **Does generated C# remain manageable?** Yes at six rows; support runtime
   dominates the 19,838-byte output. No larger-scale conclusion is justified.
7. **Are compiled queries ergonomic?** Scalar access and aggregates are clear.
   Dynamic filtering/table-valued projection are not yet ergonomic.
8. **How much Excel-like behavior follows naturally?** Typed sheets, direct
   cells/columns, deterministic formulas as functions, projections, aggregates,
   cross-sheet reads, and immutable scenarios.
9. **What does not fit?** Interactive grid editing, formatting, formula
   compatibility/recalculation, charts, pivots, macros, volatile functions,
   workbook UI state, and collaborative document editing.
10. **Which pandas workflows change?** Typed deployed asset queries simplify;
    dynamic ingestion, cleaning, reshape, missing values, joins, and exploratory
    analysis become harder.
11. **What scale stops direct source being practical?** Not established by M0.
    Growth is linear and the six-row proof cannot set a responsible cutoff.
12. **When move to external/embedded assets?** When generated-source parsing,
    DLL size, eager memory, or Git review becomes materially worse. Assembly
    resources preserve a single artifact; external assets are appropriate only
    when independent data lifecycle/scale outweighs the source-as-database
    thesis.
13. **Product direction or specialized format?** Evidence supports a
    specialized typed workbook/asset direction, not a database or Excel/pandas
    replacement.
14. **Which database-M0 assumptions are irrelevant?** Segment trees, leaf
    routing, manifests, storage readers, pruning, append/rewrite policy,
    tombstones, compaction, external binary formats, and database CLI concerns.

## Failures, awkwardness, and scale limits

- A table declaration still conflates nominal schema and authored singleton.
- Multi-sheet TSON has no workbook root.
- The `.ts` data/query logic boundary is conventional, not enforced.
- The projection is explicit because tables are not iterable query sources.
- There is no key/cross-sheet reference law; the proof uses positional
  correspondence.
- Full-column `with` is excellent for bounded scenario data but verbose for one
  cell in a wide or long table.
- Exported CLR names are useful generated API, not a promised long-term ABI.
- All table values are eagerly materialized arrays.
- Direct emission has not been tested beyond six rows.

## Additional work performed

- **Change:** added bounded whole-column table `with`.
  **Why:** the experiment required an actual immutable derived table while the
  prior language rejected all table updates.
  **Evidence:** compiler diagnostics, deterministic MIR, C#/Node parity, and
  `81.5 → 84.0` runtime proof.
  **Semantic impact:** table assignment/mutation remains rejected; only closed
  same-length whole-column replacement is admitted.
  **Follow-up:** consider cell/row syntax only after a coherent row-construction
  and diff law exists.
- **Change:** preserved `export record table` into MIR and generated a bounded
  CLR read surface.
  **Why:** a normal consumer otherwise received opaque public helper types with
  inaccessible columns.
  **Evidence:** the separately compiled C# consumer reads row count and Bob's
  score through typed properties.
  **Semantic impact:** export is opt-in; storage/layout/helpers remain private.
  **Follow-up:** version or facade the API before promising package ABI
  stability.
- **Change:** added JavaScript derivation parity.
  **Why:** the new shared MIR node must not make the second existing backend
  invalid.
  **Evidence:** focused C#/Node test passes; existing non-workbook table corpus
  remains byte-stable through demand emission.
  **Semantic impact:** only programs using table `with` receive the extra
  JavaScript construction path.
  **Follow-up:** none for the .NET workbook milestone.

## Recommendation

Treat this as a promising specialized Git-native typed workbook/asset format.
Do not revive the external database architecture and do not add SQL.

The next milestone should be a bounded scale-and-authoring study:

1. generate deterministic 1,000-, 10,000-, and 100,000-row fixtures;
2. measure Copeland parse/bind, generated C# size, Roslyn time, DLL size, load
   allocation, and query time;
3. compare direct arrays with one deterministic assembly-owned resource;
4. test row insertion Git diffs;
5. decide whether a TSON workbook root and a typed table iterator/projection are
   justified.

Stop the product direction if direct or resource-backed snapshots cease to be
pleasant Git-reviewed assets. The current evidence supports source-authored
compiled workbooks; it does not support a general database, dataframe engine,
or spreadsheet application.
