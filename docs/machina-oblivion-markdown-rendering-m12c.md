# Machina Oblivion Markdown Rendering M12c

## Purpose

M12c makes Copeland Markdown visibly useful inside Oblivion.

The goal is not editing or execution. The goal is to render `DocumentMir` clearly enough that Codex-authored docs, roadmaps, and note cards can be inspected directly in the presenter shell.

## Dogfood goal

Markdown is already the format Codex produces most often.

M12c therefore focuses on practical dogfood:

- author Markdown externally in Notepad or VS Code
- load it through `Copeland.Markdown`
- render it in compact cards and the inspector
- keep code fences static and non-executing

## Rendering pipeline

```text
.md body file
  -> Copeland.Markdown
  -> Markdown AST
  -> DocumentMir
  -> Oblivion presenter-side Markdown renderer
  -> Machina UI nodes
```

`Copeland.Markdown` stays frontend/compiler-only. The M12c renderer lives in the presenter/sample Oblivion layer and does not add a Machina UI dependency back into the Markdown package.

## Compact preview rendering

Compact cards now render a bounded Markdown preview instead of a plain text dump.

Preview behavior:

- first heading if present
- first paragraph or list summary
- fenced code summary as `code: <language>`
- Markdown and diagnostics badges when present
- no full-body expansion in the compact card

## Inspector rendering

The inspector now hosts a fuller Markdown rendering card backed by presenter-side lowering helpers.

Inspector behavior:

- headings render with explicit level markers and stronger typography
- paragraphs keep readable spacing
- bullet and ordered lists render with markers and indentation
- fenced code blocks render as bounded static code regions
- body source path and card source path remain visible in metadata
- diagnostics render in a separate readable panel

## Supported block rendering

Current rendered block coverage:

- headings `#` through `######`
- paragraphs
- bullet lists
- ordered lists
- fenced code blocks
- thematic breaks

This is a useful bounded subset, not full CommonMark.

## Supported inline rendering

Current rendered inline coverage:

- plain text
- inline code
- strong
- emphasis
- links rendered as label plus visible target

Where current text primitives are limited, M12c uses a readable fallback instead of blocking the milestone.

## Diagnostics rendering

Markdown diagnostics are now visible in both surfaces:

- compact cards show a diagnostics badge
- inspector shows severity, code, line/column or span, and message

Malformed Markdown does not crash workspace loading or presenter rendering.

## Code fences are static

Code fences remain presentation-only in M12c.

They are rendered as bounded static code regions with preserved line breaks and optional language labels. No Roslyn compilation, JIT, test execution, or syntax execution is added here.

## Relationship to Copeland.Markdown

`Copeland.Markdown` continues to own:

- source text
- lexer/scanner
- parser
- Markdown AST
- diagnostics
- `DocumentMir`

M12c consumes `DocumentMir`. It does not turn the frontend into a UI package.

## Relationship to Oblivion typed-card model

The canonical model remains:

```text
Oblivion page:
  Stack<Card>

Text/Note card:
  Copeland Markdown body

Image/table/video/code/artifact:
  future typed cards

Single-file Markdown:
  future export/import target, not canonical storage
```

M12c still does not treat one Markdown file as an entire Oblivion page.

## No editor yet

Markdown can be authored externally in Notepad or VS Code for now.

M12c does not add:

- keyboard input editor UI
- live editing
- file watcher
- Roslyn execution
- xUnit `[Fact]` / `[Theory]` execution
- Visionary
- full CommonMark compatibility

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12c\presenter-markdown-rendering-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12c\presenter-markdown-rendering-inspector-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard markdown-first-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12c\presenter-markdown-rendering-inspector-code.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard execution-deferred
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12c\presenter-markdown-rendering-diagnostics.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard markdown-diagnostics-sample
```

The renderer also writes:

- `artifacts/m12c/oblivion-markdown-rendering-manifest.json`
- `artifacts/m12c/oblivion-markdown-rendering-manifest.txt`

## M12d follow-through

M12d builds directly on this renderer.

Instead of only showing copied sample Markdown bodies, the sample workspace now adds `Oblivion -> Docs` and loads selected existing repo docs as generated Markdown cards. Those docs still render as compact cards plus inspector detail, still keep per-doc diagnostics, and still do not become whole Oblivion pages.

## What changed

- added presenter-side Markdown lowering/rendering helpers
- upgraded compact card previews for Markdown bodies
- upgraded inspector Markdown rendering with clearer block distinctions
- upgraded diagnostics display with line/column data
- added curated dogfood Markdown samples, including a doc-derived body and a diagnostics sample
- enabled the next M12d step where selected existing repo docs can reuse the same rendering path as generated cards under `Oblivion -> Docs`
- added M12c tests, exports, and manifest output

## What did not change

- no Markdown editor
- no file watcher
- no Roslyn execution
- no xUnit execution
- no Visionary
- no production renderer/core/layout semantic change
- no external Markdown renderer dependency

## Deferred work

- deeper inline richness and richer typography policy
- tables, images, video, and other unsupported Markdown block types
- single-file Markdown export/import workflow
- Markdown editing UI
- trusted local execution and notebook behavior
