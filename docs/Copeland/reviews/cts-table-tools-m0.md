# CTS-TABLE-TOOLS-M0 review

## Result

M0 adds a compiler-aware `tscl table` command group for explicitly named
authored `.ts` sources. It operates on Copeland syntax nodes and bound table
metadata, not regexes, JSON source models, spreadsheets, databases, or LLMs.
The canonical representation remains immutable columnar Copeland source.

## Command surface

```text
tscl table list <source> [--format text|json]
tscl table schema <source> <table> [--format text|json]
tscl table rows <source> <table> [--offset n] [--limit n] [--columns a,b] [--format text|json]
tscl table set <source> <table> --row n --column name --value value [--dry-run] [--format text|json]
tscl table add-row <source> <table> --json object [--dry-run] [--format text|json]
tscl table delete-row <source> <table> --row n [--dry-run] [--format text|json]
tscl table validate <source> [--format text|json]
tscl table export <source> <table> --format csv [--output file] [--result-format text|json]
tscl table import <source> <table> --format csv --input file --replace [--dry-run] [--result-format text|json]
```

`schemaVersion: 1` JSON has a stable command identity, source locations, and
deterministic source-order tables, columns, rows, and diagnostics. Rows are
synthesized from bound columns; no row-oriented file or mutable runtime model
is introduced.

## Editing law

Every mutation reads and SHA-256 hashes the source, parses and binds it, plans
precise syntax spans, applies changes in memory, reparses and rebinds through
the ordinary compiler, compares the source hash, then writes a temporary UTF-8
file and atomically replaces the target. If validation or the hash check fails,
the original source is never changed. `--dry-run` validates the same candidate
without replacing the file.

Set-cell changes only the selected expression. Add/delete map one row intent to
every column in declaration order, making unequal lengths impossible to
publish. Scalars and zero-payload enum cases are parsed under the declared
bound type; the complete candidate compiler validation remains authoritative.

## CSV law

CSV export is deterministic declaration-order UTF-8 with quoting and invariant
value formatting. Import is intentionally explicit `--replace`; headers must
exactly equal the existing declared columns, in declaration order. It cannot
infer a schema, add a column, or become authoritative source data.

## Proof and fixture

The accepted workbook at
`samples/copeland-ts/tson-workbook-m0/Workbook/Workbook.ts` is the M0 proof
fixture. The focused CLI integration test copies an equivalent isolated fixture,
proves list/schema/rows JSON, rejected mutation byte identity, set/add/delete,
CSV export/import dry-run, table validation, and an isolated MSBuild C# consumer
that observes the edited score (`84`).

The full compiled consumer proof remains:

```console
dotnet build samples/copeland-ts/tson-workbook-m0/TsonWorkbookM0.slnx --no-restore
dotnet run --project samples/copeland-ts/tson-workbook-m0/Consumer/Consumer.csproj --no-build
```

For a mutation proof, copy the fixture first, run the sequence in
`docs/Copeland/table-tools.md`, then build the copied solution and run its C#
consumer. This keeps the canonical committed fixture unchanged.

## Additional work performed

- Added compiler-bound table projection and localized syntax-span rewrite code
  in the CLI; M0 needs one reusable translation layer from row intents to
  canonical columns.
- Added deterministic CSV codec, structured result DTO projection, source hash
  guard, and atomic write path; these make automated edits reviewable and safe.
- Added CLI integration coverage and the user guide; they document and prove
  the deterministic actuator contract.

## Limitations and next milestone

M0 deliberately defers visual editing, query/filter syntax, keys, formulas,
payload-enum/record literal authoring, `.xlsx`, project-wide discovery,
semantic Git diffs, concurrent collaborative editing, and LLM integration.
Affected multiline arrays are deterministically formatted on CSV replacement;
one-cell updates retain all surrounding text. A sensible M1 is richer bounded
literal support (payload enums and records) plus project-aware table discovery,
without weakening compiler ownership of the source.
