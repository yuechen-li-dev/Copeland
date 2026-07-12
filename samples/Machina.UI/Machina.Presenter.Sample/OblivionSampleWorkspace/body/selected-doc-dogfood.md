# Copeland Markdown frontend dogfood

This card is a curated dogfood slice of `docs/Copeland/history/copeland-markdown-frontend-m12a.md`.

## Why this matters

Codex writes planning docs, roadmaps, and closeout notes in Markdown constantly.
Oblivion should make those docs readable enough to inspect without pretending that Markdown is the whole page model.

- `Copeland.Markdown` owns lexing, parsing, diagnostics, and `DocumentMir`.
- Oblivion owns presenter-side lowering from `DocumentMir` into visible card UI.
- Full CommonMark, images, tables, and video stay deferred.

### Rendering contract

1. Headings should be visibly distinct.
2. Paragraphs and lists should be readable with spacing.
3. Links should show both label and target.

Use the [frontend milestone doc](../../../../docs/Copeland/history/copeland-markdown-frontend-m12a.md) and the [phase closeout note](../../../../docs/Machina.UI/history/machina-oblivion-phase-closeout-m11g.md) as the source material.
