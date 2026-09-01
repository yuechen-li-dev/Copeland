# Oblivion TSON Table Card — M20e

## Result

M20e establishes a first-class read-only Table Card whose semantic content is the existing Copeland `TsonTable`. Oblivion adds only durable source metadata:

```text
OblivionCardKind.Table
OblivionTableSource(TsonTable, workspace-relative reference)
```

There is no Oblivion table schema, row, cell, dataframe, or generic table IR.

## Source contract

Structured vault metadata uses:

```toml
[table]
kind = "tson-table"
reference = "content/validation-evidence.obj.ts"
```

The reference must remain inside the vault and end in `.obj.ts` or `.tson`. App resolves the path, reads the file once, and calls `TsonDocumentReader.ReadSelfDescribed`. `.obj.ts` selects `ObjectTypeScript`; `.tson` selects `CanonicalTson`, including its exact canonical byte-spelling check. The document root must be `TsonTable`; Oblivion never selects among declarations or reinterprets another root kind.

The loaded `TsonTable.Schema`, `Columns`, and `RowCount` retain table identity, schema identity, column identities, declaration order, element types, cell order, rectangularity, and existing TSON bounds. A SHA-256 source hash is retained as provenance evidence, not semantic identity.

## Columnar-to-row projection

Rows exist only as presentation indices. For a visible `(rowIndex, columnIndex)`, the UI reads:

```text
table.Columns[columnIndex].Cells[rowIndex]
```

`OblivionTablePresentationSource` holds the production `TsonTable` reference. `OblivionTableProjection.Cell` performs the indexed access. The Avalonia `ListBox` receives only the integer range `0..RowCount`; its default virtualizing stack realizes visible row controls on demand. It does not receive row objects or copied cells.

## Display policy

Headers use `TsonTableColumn.Schema.Name` in declaration order, with a restrained second line for the element type. The non-semantic `#` gutter is the zero-based projection index. Visible columns remain correlated through their exact schema identities, which are also emitted in CLI and capture proof metadata.

Cell formatting is deterministic and invariant:

- Boolean: `true` or `false`.
- Number: round-trip invariant text; `-0`, `NaN`, `Infinity`, and `-Infinity` remain distinct.
- String: raw readable text with backslash and control newlines/tabs escaped for a one-line cell.
- Record: declaration-ordered `{field: value}` summary.
- Payload enum: `Case` or `Case(payload)`.
- Array: at most three displayed elements followed by `…` when more remain.

Display strings are capped at 160 characters. This is visual presentation only; the `TsonValue` remains complete.

## Layout, scrolling, and accessibility

Column widths use the header plus at most the first 32 cells. Each column is clamped to 180–320 px. A single horizontal scroller owns width overflow, while the virtualized `ListBox` owns vertical row overflow. Rows are compact, single-line, 34 px high. A valid zero-row table retains its headers and shows `No rows`.

Expanded Table Cards fill the assigned viewport slot and do not use the Markdown prose-width cap. Single, VerticalSplit, and HorizontalSplit use the existing numeric slot geometry. The dogfood table fits Single and VerticalSplit width; it requires horizontal scrolling in the narrower HorizontalSplit slot. In a half-height VerticalSplit slot it retains a header and a useful visible row range.

The mature Avalonia controls expose a named table surface, named headers/cells, row count/column count through presentation metadata, one-row selection, and selectable cell text. M20e adds no multi-range selection or table-specific copy/export commands.

## Appearance

The table consumes the existing resolved light/dark Oblivion tokens for surface, header, foreground, muted index, borders, and selection. `System` resolves through the existing application appearance boundary; no table-specific theme setting exists.

## CLI and reload

`oblivion card list` reports `table`. `card show` reports source kind/reference, profile, table/schema identities, row and column counts, ordered names/types/identities, source hash, load time, and diagnostics. It never dumps cells. `card content` returns `OBLIVION-CARD-CONTENT-NOT-TEXT` because structured TSON is not Markdown.

Workspace open and candidate reload validate each Table Card source through the App realizer. A valid source edit keeps Card identity and is visible after reload. An invalid candidate source makes reload fail and retains the prior session atomically. Selection and scroll are not specially preserved across a successful source-content reload beyond existing session reconciliation.

## Non-goals

M20e adds no sorting, filtering, querying, aggregation, grouping, pivoting, editing, row mutation, CSV/JSON/XLSX/Parquet/Arrow conversion, formula or spreadsheet ontology, database ontology, schema inference, custom table execution, or generic table presentation IR. TableScript and TSON semantics are unchanged.
