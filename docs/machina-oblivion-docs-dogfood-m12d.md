# Machina Oblivion Docs Dogfood M12d

## Purpose

M12d turns selected existing repo docs into real Oblivion Markdown cards.

The goal is narrow:

- add an `Oblivion -> Docs` tab
- load a curated list of existing `docs/*.md` files
- compile each file through `Copeland.Markdown`
- preserve source paths and per-doc diagnostics
- keep the page model as a stack of typed cards

M12e keeps that docs dogfood path intact and routes those generated doc cards through the same note-card handler contract as persisted Markdown notes.

M12f keeps the same docs dogfood cards and adds deferred note-card actions plus routed deferred effect results in the inspector. Existing docs still do not execute anything.

M12g keeps the same docs dogfood cards and adds keyboard input plumbing in the presenter shell. Text input now translates through the backend seam, but the docs cards still do not become an editor.

M12d does not add a Markdown editor, file watcher, Roslyn execution, xUnit execution, Visionary, or single-file Markdown import/export.

## Why dogfood existing docs

Codex already writes a large amount of Markdown into this repo.

That means the most honest next dogfood step is not another copied sample body file. It is loading the real docs we already maintain, compiling them through the same frontend, and rendering them through the same compact-card plus inspector path.

## Curated docs list

M12d intentionally uses a hand-curated deterministic list:

- `docs/machina-oblivion-phase-closeout-m11g.md`
- `docs/machina-oblivion-workspace-persistence-m11d.md`
- `docs/machina-presenter-card-hardening-m11e.md`
- `docs/machina-test-suite-topology-m11b.md`
- `docs/machina-presenter-scrollbar-state-machine-m11c.md`
- `docs/copeland-markdown-frontend-m12a.md`
- `docs/machina-oblivion-markdown-body-integration-m12b.md`
- `docs/machina-oblivion-markdown-rendering-m12c.md`

M12d does not attempt broad automatic repo indexing.

## Docs as cards

The canonical Oblivion storage model still remains:

```text
workspace.oblivion.json
  -> section/page topology

*.page.toml
  -> page metadata

*.card.toml
  -> ordinary persisted card metadata
```

The `Docs` page is still one Oblivion page inside that topology.

Each curated Markdown file becomes one generated `note` card plus one synthetic top-level `status` index card:

- title from the first heading when available
- file-name fallback when no heading exists
- tags including `docs`, `dogfood`, and `markdown`
- `Passing` when no diagnostics exist
- `Warning` when Markdown diagnostics exist
- `Failing` only when the doc could not be loaded

Markdown files are not treated as whole pages.

## Source path preservation

Each generated doc card keeps the repo-relative source path:

- `card.SourcePath`
- `body.BodySourcePath`

The inspector surfaces those paths directly so the user can edit the real file externally in Notepad or VS Code and then reload later.

M12d keeps path handling deterministic and local to the repository. It does not accept arbitrary absolute file references for this dogfood path.

## Diagnostics behavior

Each selected doc compiles through `Copeland.Markdown`.

Behavior:

- diagnostics stay attached per doc card
- compact cards show diagnostics badges when needed
- inspector diagnostics remain readable per selected doc
- the top docs index card summarizes total diagnostics and unsupported-syntax counts
- malformed or unsupported syntax does not crash the page

## Rendering behavior

M12d reuses the M12c presenter-side Markdown renderer.

That means:

- compact cards show headings plus bounded summaries
- inspector renders headings, paragraphs, lists, code fences, inline code, strong/emphasis, and links
- long docs remain bounded by the existing page and inspector geometry
- Markdown still acts only as a text-card body language inside the typed-card shell

## Index/status card

The `docs-dogfood-index` card is synthesized at the top of the page.

It records:

- docs loaded count
- cards generated count
- total diagnostics count
- unsupported syntax count
- reminder that docs are edited externally
- reminder that Markdown is still card body language only

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12d\presenter-docs-dogfood-index.png -SelectedSection oblivion -SelectedTab docs -SelectedCard docs-dogfood-index
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12d\presenter-docs-dogfood-closeout-doc.png -SelectedSection oblivion -SelectedTab docs -SelectedCard doc-machina-oblivion-phase-closeout-m11g
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12d\presenter-docs-dogfood-markdown-frontend-doc.png -SelectedSection oblivion -SelectedTab docs -SelectedCard doc-copeland-markdown-frontend-m12a
```

The docs dogfood manifest also writes:

- `artifacts/m12d/oblivion-docs-dogfood-manifest.json`
- `artifacts/m12d/oblivion-docs-dogfood-manifest.txt`

## What changed

- added `Oblivion -> Docs`
- added deterministic curated existing-doc loading
- preserved repo-relative source paths on generated cards
- compiled each selected doc through `Copeland.Markdown`
- surfaced per-doc diagnostics in compact cards and the inspector
- added a synthetic docs index/status card
- added M12d tests, exports, and manifest output

## What did not change

- no Markdown editor
- no keyboard-input authoring flow
- no file watcher or live editing
- no single-file Markdown export/import implementation
- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no Visionary editor/source workspace implementation
- no replacement of the canonical JSON/TOML typed-card page model

## Deferred work

- broader curated-doc selection and filtering policy
- richer docs-page metadata and navigation
- single-file Markdown export/import as a future interchange format
- Markdown editing UI
- trusted local execution and notebook behavior
