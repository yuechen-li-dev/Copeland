# Machina stream/layout comparison — M19c

## Decision

Choose **C: semantic content stream plus optional local layout groups**. It preserves ordinary source order for most technical explanation, permits explicit comparison only where spatial relationship carries meaning, and keeps Machina's deterministic layout machinery below the authoring contract.

Ratings are qualitative and relative to agent-authored technical presentations, not arbitrary application UI.

| Model | LLM generation ease | Human source readability | Diffability | Semantic clarity | Layout reuse | Responsive reuse | Verbosity | Debuggability | Implementation complexity | MIR leakage |
|---|---|---|---|---|---|---|---|---|---|---|
| Manual coordinates | Poor: every edit requires geometry arithmetic | Poor: intent is buried in numbers | Poor: content edits churn positions | Poor | Poor | Poor | High | Good only for final rectangles | Low kernel, high author burden | Extreme |
| Nested `HStack`/`VStack` | Good for small local trees | Good until nesting becomes deep | Fair: rearrangement moves subtrees | Fair: layout dominates meaning | Poor: content is owned by the tree | Fair with additional policy | Medium | Good local structural trace | Medium | Medium |
| Native `UI.Stack` / `UI.Grid` | Fair: typed but requires tracks/items/gaps | Fair for components, weak for briefings | Fair | Fair: semantic nodes and layout are mixed | Poor | Fair | Medium-high | Strong because lowering is explicit | Existing | Medium-high |
| MachinaLayout.JS row/table | Fair for bulk regular geometry, poor for narrative | Fair as a table, poor as a document | Excellent record-level diffs | Fair for named boxes, weak for content meaning | Good below authoring | Good with separate variants | High | Excellent inspection/oracle shape | Existing prototype | High |
| Copeland TS `stream` + explicit advanced layout | Good for static named regions and bounded collections | Good when content and layout are one composition | Good | Good named regions; mixed content/geometry | Fair: explicit layout/bind split is reusable, concise streams are not | Good through explicit profiles | Low-medium | Excellent normalized table inspection | High compiler investment already exists | Medium |
| Proposed C# semantic presentation stream | Excellent: matches model-native summaries, Markdown, code, diagrams, artifacts, decisions, and actions | Excellent: reads in human explanation order | Excellent: content edits stay local; layout references IDs | Excellent | Excellent | Excellent through filters plus optional groups | Low by default | Strong when projected IDs correlate to cards and layout rows | Medium bounded adapter | Low |

## Rationale by representation

### Manual coordinates

Coordinates belong in resolved geometry, golden fixtures, bounded overlays, or emergency escape hatches. They are unsuitable for ordinary presentation authoring because text length, viewport, and inserted content force unrelated arithmetic changes.

Layer: `RESOLVED_LAYOUT` when numeric output; `LAYOUT_DECLARATION` only as an explicit escape hatch.

### `HStack` / `VStack`

Function-shaped stacks are the best current option for localized visual components. They express hierarchy directly and eliminate parent-row bookkeeping. They still make layout own content, so changing projection shape reconstructs the tree.

Layer: `LAYOUT_DECLARATION` producing tree-shaped `PRESENTATION_IR`.

### Native `UI.Stack` / `UI.Grid`

Native C# provides stronger typed stack items, tracks, grid cells, padding, and lowering than a loose UI DSL. That precision is valuable downstream. It is too specific as the first thing Codex writes when the intended message is “summary, evidence, decision.”

Layer: `LAYOUT_DECLARATION`; lowered rows are `LAYOUT_MIR`.

### MachinaLayout.JS rows and tables

Rows are easy to serialize, diff, patch, inspect, validate, and compare with resolved geometry. Tables are especially effective for repeated spatial records and overlays. They split narrative hierarchy and renderer binding across IDs and columns, so they should remain an inspection/compatibility/MIR form.

Layer: `LAYOUT_MIR`; table syntax may be an alternate `LAYOUT_DECLARATION` for genuinely tabular spatial data.

### Copeland TS stream

TS `stream` proved that compiler-generated slots, bindings, identities, and normalized tables remove repetitive obligations. Named regions and bounded ordered collections are good authoring laws. The current stream grammar co-authors content and layout; it does not by itself permit one content list to swap layouts. The explicit `layout` + `bind` path does, at greater ceremony.

Layer: mixed `AUTHORING_API`, `SEMANTIC_CONTENT`, and `LAYOUT_DECLARATION`, lowering to shared `LAYOUT_MIR` and binding `PRESENTATION_IR`.

### Proposed C# semantic stream

The proposed source models what the agent is trying to communicate. Default order is the layout. Optional `Compare`, `Columns`, `Grid`, or `Focus` groups reference content IDs, so a layout can change without reconstructing content. Renderer-specific card and Machina node materialization happens later.

Layer: `AUTHORING_API` + `SEMANTIC_CONTENT`, with a separate bounded `LAYOUT_DECLARATION`.

## Comparison, not universal UI

The recommendation is specific to human-facing technical explanation. A settings screen or interactive tool may correctly use `UI.Stack`, `UI.Grid`, and components directly. A presentation should only descend to those APIs when the spatial or interactive relationship is itself authored meaning.
