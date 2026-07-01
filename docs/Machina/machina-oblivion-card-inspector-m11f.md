# Machina Oblivion Card Inspector M11f

## Purpose

M11f adds static card selection and a bounded inspector panel for `Oblivion` pages inside the existing presenter shell.

## Why cards need an inspector

Cards should remain compact cells. Full text, source metadata, action metadata, artifact metadata, and deferred execution notes now live in a separate inspector so card bodies do not bloat.

## Selection model

Selection is page-local and stored as selected card id by page id.

- If no explicit selection exists, the first card on that page is selected.
- If selection is cleared, the inspector shows an empty state.
- If a stored selected card disappears after reload, selection falls back deterministically to the first available card.

## Hit testing

Oblivion pages record stable card bounds after layout resolution. Pointer clicks inside those bounds dispatch `SelectOblivionCard(pageId, cardId)`. Hit testing applies page scroll offset and remains backend-neutral.

## Inspector layout

The current layout is a workbench-style split view:

- left column: compact cards
- right column: inspector/detail cards

This preserves the M10/M11 presenter shell behavior while giving `Oblivion` a place to expand static detail.

## Metadata shown

The inspector shows:

- title
- kind
- status
- tags
- body
- action metadata
- artifact metadata
- source path
- page id
- card id
- workspace id

## Actions and artifacts are metadata only

M11f does not execute actions or artifacts. The inspector labels them as metadata-only and non-executable.

## Execution result placeholder

The inspector includes a static result card:

- `Not executed in M11g.`
- `Markdown cards come first; Roslyn/xUnit execution deferred to M13+.`

`CodeFact` and `CodeTheory` remain placeholder cards only.

## M11g closeout note

M11g keeps the inspector model unchanged but repoints the roadmap:

- M11 closes out the static persisted-card substrate
- M12 should focus on Markdown cards and Markdown document dogfooding
- no Markdown editor is implemented yet
- no Markdown renderer is implemented yet beyond static planning copy
- Visionary remains future-only

## Persistence integration

M11d persistence remains the source of truth. Persisted cards now carry source-path metadata into the in-memory card model so the inspector can show where a selected card came from.

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11f\presenter-oblivion-card-inspector-intro.png -SelectedSection oblivion -SelectedTab cards -SelectedCard intro
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11f\presenter-oblivion-card-inspector-code-fact.png -SelectedSection oblivion -SelectedTab cards -SelectedCard code-fact-placeholder
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11f\presenter-oblivion-card-inspector-artifact.png -SelectedSection oblivion -SelectedTab artifacts
```

Short selectors such as `intro` and `code-fact-placeholder` resolve against persisted card asset names.

## What changed

- added selected-card state per Oblivion page
- added explicit select/clear selection actions
- added scroll-aware card hit testing
- added right-side inspector rendering
- added source-path metadata on persisted cards
- added M11f inspector manifest outputs

## What did not change

- no Roslyn compilation or execution
- no xUnit `[Fact]` / `[Theory]` runtime
- no action execution
- no artifact generation execution
- no markdown editor
- no Visionary editor

## Deferred work

M12+ remains the earliest execution phase. Roslyn, xUnit execution, artifact generation, markdown editing, and Visionary are still deferred.
