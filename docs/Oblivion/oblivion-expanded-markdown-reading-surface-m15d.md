# Oblivion Expanded Markdown Reading Surface M15d

## Purpose

M15d hardens the M15c expanded Markdown path into a readable document surface instead of a small preview-shaped panel.

## User must-fix issues

- rendered Markdown in expanded cards was unreadable because dark text could land on a dark blue surface
- expanded card height was too short to function like a document reader
- the inspector rendered formatted Markdown in a narrow column and overflowed badly

## Style model

Expanded Markdown cards are document reading surfaces.

Rendered Markdown belongs in the expanded card body.

The inspector is a secondary surface for metadata, actions, diagnostics, artifacts, and raw source inspection.

Dark backgrounds are allowed; unreadable contrast is not.

Machina styles are explicit immutable records / TOML-loadable data, not CSS.

Expanded card:
  rendered Markdown document

Inspector:
  metadata
  actions
  diagnostics
  artifacts
  raw Markdown source text

## Dark reading surface contrast

M15d uses one explicit `OblivionMarkdownReadingStyle` record with a dark reading surface and light foregrounds.

- expanded Markdown body text now uses explicit readable foreground colors
- headings, paragraphs, lists, links, diagnostics, and code blocks all route through the shared reading style
- code blocks keep a distinct darker code surface, but the code foreground remains high-contrast and readable

## Expanded card height policy

Expanded Markdown cards now use document-scale height derived from the available presenter viewport instead of a small fixed preview height.

The expanded card aims to cover nearly the full visible stack height while still respecting shell chrome, card header content, margins, and the inspector column.

## Single-expanded-card policy

M15d adopts one expanded Markdown card per page.

Expanding one Markdown card collapses other expanded Markdown cards on the same page.

This keeps the reading model simple and avoids multiple oversized nested scroll regions competing in the same stack.

## Local body scrolling

The expanded card body still owns its own local scroll region.

Long Markdown documents scroll locally inside the expanded card body rather than forcing the whole page to become one giant freeform layout.

## Inspector raw Markdown source

The inspector no longer renders formatted Markdown body content.

Instead it shows:

- metadata
- actions
- diagnostics
- artifacts
- raw Markdown source text in a bounded scrollable source block

The inspector source view is read-only. It preserves the original Markdown text and uses bounded vertical scrolling with clipped long lines rather than becoming a second document renderer.

## Selection and expansion behavior

Selection and expansion remain separate explicit states.

- expanding a card selects it
- selection alone does not expand it
- collapsing an expanded card keeps it selected
- local body scroll remains page-local state on the expanded card

## Export evidence

Proof artifacts live under `artifacts/m15d/`:

- `m15d-oblivion-expanded-dark-readable-1280x720.png`
- `m15d-oblivion-expanded-full-height-1280x720.png`
- `m15d-oblivion-expanded-scrolled-1280x720.png`
- `m15d-oblivion-inspector-raw-markdown-1280x720.png`
- `m15d-oblivion-docs-compact-expanded-960x540.png`
- `m15d-oblivion-cards-expanded-1280x720.png`
- `oblivion-expanded-markdown-reading-surface-manifest.json`
- `oblivion-expanded-markdown-reading-surface-manifest.txt`

## What changed

- added an explicit immutable Markdown reading style record for expanded reading surfaces
- routed expanded Markdown rendering through that style instead of scattered local colors
- increased expanded Markdown cards to document-scale height
- preserved local body scrolling for long documents
- changed the inspector from rendered Markdown body to scrollable raw Markdown source
- adopted one expanded Markdown card per page

## What did not change

- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work
- no CSS-like style cascade
- no arbitrary `2D` layout solver

## Deferred work

- TOML loading for the Markdown reading style record
- pointer-drag scrolling for the inspector raw-source block if later earned
- any future editor or execution work
