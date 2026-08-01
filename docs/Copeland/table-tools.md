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
tscl table query Workbook.ts ProductCatalog --query-json query.json
tscl table query Workbook.ts ProductCatalog --where 'retail > 15.0' --explain --format json
```

Textual predicates are desugared only enough to place selected-table column
names in a typed row scope, then are compiled through the normal Copeland
`rows().where(...).select(...)` binding and C# relation executor. There is no
JavaScript-like evaluator or dictionary predicate engine. The predicate must
bind to exactly `boolean`; existing Copeland scalar typing, comparisons,
arithmetic, and literals remain authoritative.

`--select` accepts direct columns and the simple `column as outputName` rename.
Without it, the semantic table schema order is used. `--order-by` accepts one or
more `column asc|desc` terms for `int`, `number`/`float`, and `string` columns.
The operator law is source → where → stable order → select → skip → take. Source
row order breaks exact sort ties, and strings use ordinal rather than locale
ordering. `skip` and `take` are non-negative integers.

`--format json` returns a stable `command: "table.query"` envelope with typed
schema, provenance, resolved request metadata, rows, diagnostics, and the
`csharp-relation-plan` executor identity. `--format csv` writes selected headers
and rows to stdout with invariant scalar formatting and CSV escaping. `--explain`
and `--dry-run` bind and describe the plan without materializing rows.

For tools, `--query-json` reads a bounded JSON request with `where`, `select`,
`orderBy`, `skip`, and `take`. `where` is a tree of `{ column }`, `{ number }`,
`{ string }`, `{ boolean }`, or binary `{ operator, left, right }` nodes. Supported
operators are `equal`, `notEqual`, ordered comparisons, `and`, `or`, and basic
arithmetic. It lowers to the same plan as textual options; it is not a generic
TableScript AST or raw-code channel.

M2A deliberately defers grouping/aggregate operators (M2B), watch/live
exploration (M2C), dynamic joins, computed query projections, SQL, and database
execution.

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
