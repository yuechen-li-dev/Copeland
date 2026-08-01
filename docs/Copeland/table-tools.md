# Record table CLI

`tscl table` is a deterministic compiler-aware source transformation tool. It
does not use an LLM. It exists so humans and LLMs do not need to rewrite table
columns manually.

Compiler-projected layout tables use the manifest-aware project context:

```console
tscl table list --project ./manifest.tsx
tscl table rows layout::Boxes --project ./manifest.tsx --format json
```

`--source <entry.ts>` searches upward for `manifest.tsx`; when found, it opens
the same materialized package/backend contracts as TSPack. These projected
commands are read-only, report a deterministic `graphFingerprint` in JSON, and
never install packages or start a browser. A source without a manifest remains
the intentionally limited source-only mode.

Record tables remain typed, immutable, columnar Copeland source. A row is only
a projected view and an editing intent.

```console
tscl table list Workbook.ts --format json
tscl table schema Workbook.ts Scores --format json
tscl table rows Workbook.ts Scores --offset 0 --limit 20 --format json
tscl table query Workbook.ts Scores --where 'score >= 90.0' --select 'name, score' --order-by 'score desc' --take 20
tscl table set Workbook.ts Scores --row 1 --column score --value 84.0
tscl table add-row Workbook.ts Employees --json '{"id":4,"name":"Dana","department":"Engineering"}'
tscl table delete-row Workbook.ts Employees --row 2
tscl table validate Workbook.ts --format json
tscl table export Workbook.ts Scores --format csv --output Scores.csv --result-format json
tscl table import Workbook.ts Scores --format csv --input Scores.csv --replace --result-format json
```

The primary discovery law is explicit: every command takes an authored source
file and, except `list` and `validate`, one exact table name. Tables and columns
are returned in source declaration order. `--format json` provides a stable
envelope with `schemaVersion: 1`, a command identity, typed projected values,
and authored source locations. Failures have nonzero exit status and structured
diagnostics in JSON mode.

## Read-only relation queries

`table query` is a read-only frontend to the compiler's typed row-relation
path. It accepts authored tables and derived tables, including declared-reference
joins; it never writes the workbook, generated source, or CSV files.

```console
tscl table query Workbook.ts ProductCatalog
tscl table query Workbook.ts ProductCatalog --where 'retail > 15.0'
tscl table query Workbook.ts ProductCatalog --select 'productName, retail'
tscl table query Workbook.ts ProductCatalog --order-by 'retail desc'
tscl table query Workbook.ts ProductCatalog --take 3 --format json
tscl table query Workbook.ts ProductCatalog --format csv
tscl table query Workbook.ts ProductCatalog --aggregate 'sum(retail) as totalRetail, count() as productCount'
tscl table query Workbook.ts ProductCatalog --group-by categoryName --aggregate 'sum(retail) as totalRetail, count() as productCount' --order-by 'totalRetail desc'
tscl table query Workbook.ts ProductCatalog --query-json query.json
tscl table query Workbook.ts ProductCatalog --where 'retail > 15.0' --explain --format json
```

Ad-hoc queries are compiler-owned typed artifacts. The CLI adapts text or JSON
into one `TableQueryRequest`; the compiler resolves the relation, schema,
provenance, predicate scope, grouping, accumulators, ordering, and pagination
before lowering a query MIR artifact. Roslyn source generation is the C#
materialization and compilation host. It receives bounded generated C# source,
not unresolved CLI expressions, and it does not define TableScript semantics or
inspect arbitrary table fields to infer behavior.

The generated entrypoint returns an exact internal result relation with typed
column arrays. The CLI only renders that result to text, JSON, or CSV. The sole
remaining reflection boundary loads the emitted assembly and invokes the known
`Execute` entrypoint for the stable query artifact ID; it never discovers table
fields, schemas, or performs aggregate execution. The former CLI-private
executor remains internal legacy/reference code while source generation is the
default path.

`--select` accepts direct columns and the simple `column as outputName` rename.
Without it, the semantic table schema order is used. `--order-by` accepts one or
more `column asc|desc` terms for `int`, `number`/`float`, and `string` columns.
Without aggregation, the operator law remains source → where → stable order →
select → skip → take. Source row order breaks exact sort ties, and strings use
ordinal rather than locale ordering. `skip` and `take` are non-negative integers.

## Typed aggregates and groups (M2B)

`--aggregate` makes aggregation a typed relation operation, before output is
rendered. Each declaration uses one canonical form and requires an output alias:

```console
tscl table query Workbook.ts ProductCatalog \
  --aggregate 'sum(retail) as totalRetail, count() as productCount'
```

Supported calls are `count()`, `count(column)`, `sum(column)`,
`average(column)`, `min(column)`, and `max(column)`. `count` returns `int`;
`sum` preserves the existing numeric column result type; `average` accepts a
`number` column; and `min`/`max` accept existing orderable scalar columns.
Aggregates are deliberately not a free-form expression language: their input is
one direct relation column, so the compiler-known result schema and provenance
remain exact.

An aggregate with no `--group-by` produces one row, after `where`. On an empty
input, `count` is `0` and `sum` is a typed zero. `average`, `min`, and `max`
produce a direct query diagnostic instead of inventing `null` or `undefined`.

`--group-by` takes one or more direct group columns. The group columns are first
in the result schema in declared order, followed by aggregate aliases:

```console
tscl table query Workbook.ts ProductCatalog \
  --group-by categoryName \
  --aggregate 'sum(retail) as totalRetail, count() as productCount' \
  --order-by 'totalRetail desc'
```

The aggregate operator law is source → where → group-by → aggregate → order-by
→ skip → take → materialize. Groups default to first occurrence in the filtered
source relation; explicit `order-by` operates only on group keys or aggregate
result columns and keeps first occurrence as the exact-sort tie breaker.
`--select` is rejected with `--aggregate`, because the aggregate declarations
already define the exact output relation.

The same bounded request is available through `--query-json`:

```json
{
  "groupBy": [{ "column": "categoryName" }],
  "aggregates": [
    { "function": "sum", "input": { "column": "retail" }, "as": "totalRetail" },
    { "function": "count", "as": "productCount" }
  ],
  "orderBy": [{ "column": "totalRetail", "direction": "descending" }]
}
```

JSON, text, CSV, and explain all use the same result schema. Aggregate output
provenance identifies the aggregate function, direct input column (where
applicable), source/join provenance, and the query filter. Group-key provenance
is copied from its source relation column. Pivot is intentionally deferred: it
is a presentation/materialization of this grouped aggregate relation, not a
relation operator in M2B. Watch/live querying is M2C; no SQL or database engine
is introduced.

`--format json` returns a stable `command: "table.query"` envelope with typed
schema, provenance, resolved request metadata, rows, diagnostics, and the
`csharp-relation-plan` compatibility executor identity (with a Roslyn source
generator materializer). `--format csv` writes selected headers
and rows to stdout with invariant scalar formatting and CSV escaping. `--explain`
and `--dry-run` bind and describe the plan without materializing rows.

For tools, `--query-json` reads a bounded JSON request with `where`, `select`,
`groupBy`, `aggregates`, `orderBy`, `skip`, and `take`. `where` is a tree of
`{ column }`, `{ number }`, `{ string }`, `{ boolean }`, `{ enum }`, or binary
`{ operator, left, right }` nodes. Supported
operators are `equal`, `notEqual`, ordered comparisons, `and`, `or`, and basic
arithmetic. It lowers to the same plan as textual options; it is not a generic
TableScript AST or raw-code channel.

M2B deliberately defers pivot presentation, watch/live exploration (M2C),
dynamic joins, computed query projections, SQL, and database execution.

CSV commands use `--format csv` for their interchange payload and
`--result-format text|json` for their command result. JSON export requires an
`--output` file so CSV and the result envelope never compete for stdout.

Mutations follow one transaction: read and hash the source, parse and bind it,
plan syntax-node-local changes, validate the candidate through the real
compiler, compare the source hash, then atomically replace the file. A failed
operation does not write the source. `--dry-run` performs every parse, bind, and
candidate validation step but does not write.

`set` replaces only the selected cell expression. `add-row` appends one parsed
value to every declared column. `delete-row` removes the same positional index
from every column. These commands preserve equal column lengths by construction
and report a compact semantic summary.

Command values are deliberately bounded: `int`, `number`/`float`, `string`,
`boolean`, and zero-payload enum cases are accepted. Strings in `add-row` are
JSON strings; enum fields use a natural case spelling such as `Engineering`.
The candidate source is always bound again, so compiler type validation is the
final authority.

CSV is an interchange format, never a source of truth. Export uses declaration
order, UTF-8, invariant scalar formatting, and RFC-style quoting. Import
requires `--replace`, exact headers in declared order, and an existing declared
table shape; it cannot infer schema or add columns. Use an isolated working copy
for imports when reviewing a proposed CSV replacement.

When an imported `number` or `float` value fits the existing column's plain
decimal precision, import retains that column's decimal presentation. This
keeps an ordinary CSV edit from rewriting unaffected values such as `18.50` to
`18.5`. Values requiring more precision, exponent notation, or a non-decimal
source convention retain their validated input spelling instead of being
rounded.

The editing surface intentionally does not include project-wide discovery, a
visual grid, schema inference, payload enum authoring, `.xlsx`, or Git/LLM
integration. Localized array replacement preserves surrounding source and table
declarations. Import may reformat the affected multiline arrays; it does not
reformat the file.
