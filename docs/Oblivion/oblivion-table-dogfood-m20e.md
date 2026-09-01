# Oblivion Table Card dogfood — M20e

## Real table and task

The maintained dogfood source is `M20eTsonTables.oblivion/content/validation-evidence.obj.ts`, with an exact canonical companion `.tson`. It contains 16 source-ordered M20e qualification checks and 7 columns:

```text
order, lane, subsystem, required, risk, proofs, evidence
```

The cells cover Number, String, Boolean, payload Enum, Array, and Record. The review task asked:

1. Which subsystem owns the most required checks?
2. Which checks carry UI risk and need visual/runtime evidence?

## Review result

Standalone owns five checks, more than App (four), UI (three), and every other subsystem (one each). All five `Ui`-risk checks are Standalone concerns: Single geometry, VerticalSplit usability, HorizontalSplit width overflow, dark appearance, and light appearance. Direct source-ordered row review was sufficient to reach both answers.

## Findings

- Single: all 16 rows are visible at 2560×1440; the table uses its slot without a prose-width cap.
- VerticalSplit: the header remains fixed above a vertically scrollable body and the half-height slot remains useful beside the exact Markdown question.
- HorizontalSplit: the deterministic preferred width exceeds the narrow slot, so horizontal scrolling is required rather than unreadable column shrinking.
- Light and dark: header, text, separators, and compound cells remain readable with existing tokens.
- Compound summaries made the evidence column reviewable without nested controls.
- The authored and canonical cards realize equivalent identity, order, types, row count, and displayed cell values.

The visual table is intentionally sparse after row 15 because the expanded Card owns the full slot rather than resizing around a small dataset. This was preferable to expanding the Card only to content height.

## Desired next operations

| Desire | Priority | Evidence |
|---|---|---|
| Column resize | NICE | The 320 px evidence column clips some summaries; horizontal space remains available in Single. |
| Copy selected cell text | NICE | Selectable text is present, but no explicit table copy command or feedback exists. |
| Freeze header during long vertical review | NOT_NEEDED | The M20e realization already keeps the header outside the virtualized vertical row scroller. |
| Sort | NOT_NEEDED | The review depended on maintained source order and manual counting was easy. |
| Filter/search | NOT_NEEDED | Sixteen rows remained directly scannable. |
| Query/aggregate/group | NOT_NEEDED | The two questions were answered without a query frontend or aggregate model. |
| Edit/add/delete row | NOT_NEEDED | The table is qualification evidence authored in `.obj.ts`; source editing remains correct ownership. |

No desire was REPEATED or BLOCKING in this one bounded dogfood pass.

## Research answers

Direct row projection of columnar TSON is sufficient for useful human review. The only observed pressure is ordinary presentation polish, led by optional column resizing; it does not justify another semantic model.

Copeland TSON remained fully authoritative. Oblivion did not need a table schema, durable row, cell object, dataframe, JSON bridge, or generic Table IR.

## Recommended M20f

Do not schedule a semantic Table milestone yet. If a second real table repeats the clipped-summary problem, M20f should be narrowly: **session-only user column resizing over exact TSON column identities, with no persisted schema, sorting, filtering, query, or editing.** Until that repetition exists, retain M20e unchanged.
