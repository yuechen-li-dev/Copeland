# Copeland Markdown Frontend M12a

## Purpose

M12a adds a small deterministic Markdown frontend under `src/Copeland/Copeland.Markdown`.

This milestone is compiler-pipeline dogfooding:

- `.md` source text
- lexer/scanner
- parser
- Markdown AST
- backend-neutral document MIR
- diagnostics
- deterministic dump/export output

It does not add a Markdown editor, Roslyn execution, xUnit notebook execution, or Visionary. Oblivion follow-through lands separately in M12b as text-card body loading and in M12c as the first Markdown rendering dogfood pass.

## Why write our own Markdown frontend

Copeland already has a compiler-shaped culture and a compiler-shaped package layout. M12a uses Markdown as the next practical dogfood surface without importing a third-party Markdown dependency.

Reasons for the custom frontend:

- existing repo docs are already `.md`
- we only need a bounded useful subset first
- predictable compilation matters more than dialect compatibility
- the long-term value is the frontend pipeline and MIR, not claiming CommonMark completeness

## Dialect policy

The dialect name is **Copeland Markdown**.

Policy:

- `.md` remains the file extension for practical dogfooding and doc compatibility
- Copeland Markdown is not full CommonMark
- deterministic compilation is prioritized over dialect compatibility
- unsupported syntax should diagnose and recover instead of crashing
- no external Markdown parser dependency is used

## Supported subset

Blocks:

- ATX headings `#` through `######`
- paragraphs from consecutive non-blank lines
- single-level unordered lists using `-` or `*`
- ordered lists using `1.`, `2.`, ...
- fenced code blocks using triple backticks
- thematic breaks using `---` or `***`

Inline:

- plain text
- inline code with backticks
- strong with `**text**`
- emphasis with `*text*`
- inline links with `[label](target)`

## Deferred Markdown features

Still deferred in M12a:

- HTML passthrough
- tables
- footnotes
- task lists
- blockquotes
- reference-style links
- Setext headings
- nested list compatibility work
- CommonMark edge-case conformance
- live editing
- renderer integration into Oblivion as production behavior
- Roslyn or xUnit execution

## Pipeline

Current package path:

```text
src/Copeland/Copeland.Markdown
```

Current pipeline:

```text
.md source
  -> MarkdownSourceText
  -> MarkdownLexer
  -> MarkdownParser
  -> MarkdownDocument AST
  -> MarkdownToDocumentMirLowerer
  -> DocumentMir
  -> text/json dump backends
```

`Copeland.Cli` now exposes:

```powershell
dotnet run --project src/Copeland/Copeland.Cli -- markdown parse README.md --emit mir --format json
dotnet run --project src/Copeland/Copeland.Cli -- markdown export-corpus --output-dir artifacts\m12a
```

## AST

The Markdown AST is syntax-facing and Markdown-specific.

Primary shapes:

- `MarkdownDocument`
- `HeadingBlock`
- `ParagraphBlock`
- `BulletListBlock`
- `OrderedListBlock`
- `ListItemBlock`
- `CodeFenceBlock`
- `ThematicBreakBlock`
- `TextInline`
- `CodeInline`
- `EmphasisInline`
- `StrongInline`
- `LinkInline`

All syntax nodes carry `SourceSpan`.

## Document MIR

The MIR is intentionally Markdown-independent.

Primary shapes:

- `DocumentMir`
- `HeadingMir`
- `ParagraphMir`
- `ListMir`
- `ListItemMir`
- `CodeBlockMir`
- `ThematicBreakMir`
- `TextMir`
- `CodeSpanMir`
- `EmphasisMir`
- `StrongMir`
- `LinkMir`

Markdown is treated as a frontend. The MIR is treated as a backend-neutral document layer that future backends can lower into:

- Oblivion cards
- Machina UI document surfaces
- HTML
- plain text
- search/index data

## Diagnostics

M12a diagnostics are deterministic and span-based.

Current diagnostics cover:

- unsupported block syntax
- malformed heading marker
- unclosed code fence
- malformed link
- unmatched emphasis marker
- unmatched strong marker
- unmatched inline code marker
- unsupported inline image syntax
- nested-list not supported

Diagnostics include:

- stable id
- message
- severity
- source span with line and column

Parser recovery is best-effort and ordinary malformed Markdown does not throw.

## Corpus dogfooding

The M12a corpus uses existing repo docs:

- `README.md`
- `docs/Machina.UI/history/machina-oblivion-phase-closeout-m11g.md`
- `docs/Machina.UI/history/machina-oblivion-workspace-persistence-m11d.md`
- `docs/Machina.UI/history/machina-presenter-card-hardening-m11e.md`
- `docs/Machina.UI/history/machina-test-suite-topology-m11b.md`
- `docs/Machina.UI/history/machina-presenter-scrollbar-state-machine-m11c.md`

The goal is not zero diagnostics for every document. The goal is:

- no crash
- deterministic diagnostics
- useful AST/MIR output across real repo docs

## Relationship to Machina.Text / Standard.Text

Current relationship:

- `Machina.Standard.Text` remains unchanged in M12a
- `Machina.Standard.Text` still owns its restricted rich-text parser/layout contract for Standard component text
- `Machina.Standard.Text` still forbids Markdown headings and other broader document syntax by design
- `Copeland.Markdown` is a separate document frontend, not a replacement parser for Standard text

What overlaps:

- both systems parse bounded inline formatting
- both systems value deterministic diagnostics and deterministic recovery
- both benefit from explicit spans and boring parser logic

What remains separate for now:

- `Standard.Text` is text-in-a-box authoring/layout for current Standard UI surfaces
- `Copeland.Markdown` is document/frontend-to-MIR compilation
- no risky shared abstraction was forced in M12a

Preferred convergence path:

```text
Copeland Markdown -> Document MIR -> future lowering into Machina/Oblivion text or card surfaces
```

That means future Standard or Oblivion rendering can consume document MIR if that becomes useful, while current `Machina.Standard.Text` behavior stays stable.

## M12b and M12c follow-through note

M12b and M12c are the direct follow-through to this frontend milestone.

In M12b:

- Oblivion still keeps pages as stacks of typed cards
- `DocumentMir` is used as text-card body MIR only
- `workspace.oblivion.json` plus `*.card.toml` remain canonical storage
- external `body/*.md` files become text-card body inputs
- single-file Markdown remains future export/import work only

In M12c:

- `DocumentMir` lowers into presenter-side Machina UI nodes
- compact cards show richer Markdown previews
- the inspector shows fuller rendered headings, lists, code fences, and links
- diagnostics become visibly useful in dogfood mode

## CLI or dump workflow

Parse or inspect a file:

```powershell
dotnet run --project src/Copeland/Copeland.Cli -- markdown parse docs/Machina.UI/history/machina-oblivion-phase-closeout-m11g.md --emit ast
dotnet run --project src/Copeland/Copeland.Cli -- markdown parse docs/Machina.UI/history/machina-oblivion-phase-closeout-m11g.md --emit mir --format json
```

Export the selected corpus proof artifacts:

```powershell
.\tools\Export-CopelandMarkdownCorpus.ps1 -OutputDir artifacts\m12a
```

Generated local proof outputs:

- `artifacts/m12a/copeland-markdown-readme.mir.json`
- `artifacts/m12a/copeland-markdown-closeout.mir.json`
- `artifacts/m12a/copeland-markdown-corpus-report.json`
- `artifacts/m12a/copeland-markdown-corpus-report.txt`

## What changed

- added `src/Copeland/Copeland.Markdown`
- added a deterministic Markdown lexer/scanner
- added a deterministic parser and AST
- added backend-neutral document MIR lowering
- added text/json dump output
- added corpus export support in `Copeland.Cli`
- added `tools/Export-CopelandMarkdownCorpus.ps1`
- added focused Markdown tests plus corpus and boundary checks

## What did not change

- no external Markdown dependency
- no Markdown editor
- no production-wide Oblivion Markdown rendering dependency
- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no Visionary implementation
- no change to `Machina.Standard.Text` behavior

## Deferred work

- broader Markdown dialect support
- nested list handling
- blockquote/table/reference-link support
- broader MIR-to-Oblivion rendering beyond the current M12c dogfood pass
- existing repo docs rendered as first-class Oblivion dogfood cards, which now lands in M12d without changing the frontend/package boundary
- richer Oblivion inline styling on top of MIR-backed bodies
- MIR-to-Machina document rendering
- richer document diagnostics categories
- front matter policy
- asset/reference policy for Markdown documents
