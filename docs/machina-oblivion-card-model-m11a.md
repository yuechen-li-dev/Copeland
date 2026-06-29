# Machina Oblivion Card Model M11a

## Purpose

M11a introduces the first static Oblivion notebook/workbench substrate inside the existing presenter shell.

The milestone is intentionally narrow:

- add a first-class deterministic card model
- render bounded static cards in the M10 shell
- prove future notebook/code/artifact directions without implementing execution

## Why Oblivion

`Oblivion` is the notebook/card/workbench layer for Machina.

The name marks the place where bounded notes, proofs, previews, executable cards, and export surfaces can eventually live without forcing M11a to jump straight into Roslyn or editor work.

## Relationship to Presenter

M10 remains the host surface.

M11a adds a new presenter-shell section:

```text
Overview
Components
Text
Diagnostics
Oblivion
Legacy
```

`Oblivion -> Cards` is the primary proof page. `Execution Roadmap` and `Artifacts` stay static.

M10d card containment policy remains the practical starting point:

- finite outer card rect
- finite content/body rect
- explicit header/body spacing
- deterministic clipping/truncation before text can bleed

## Relationship to Visionary

```text
Oblivion:
  notebook/card/workbench layer

Visionary:
  future code editor/source workspace layer
```

M11a documents `Visionary` only as future direction. No editor behavior, code editor UI, or source-workspace runtime is implemented here.

## Card model

M11a keeps the model sample-local in `samples/Machina.Presenter.Sample`:

- `OblivionCardKind`
- `OblivionCardStatus`
- `OblivionCardId`
- `OblivionCard`
- `OblivionCardAction`
- `OblivionCardArtifact`

Each card carries stable identity plus deterministic metadata:

- id
- kind
- status
- title
- subtitle
- tags
- body lines
- actions
- artifacts

## Card kinds

- `Note`
- `Status`
- `UiPreview`
- `Artifact`
- `CodeFact`
- `CodeTheory`

## Card statuses

- `Idle`
- `Passing`
- `Failing`
- `Warning`
- `Deferred`
- `Placeholder`

## Static card rendering

The renderer is also sample-local and follows the presenter-card containment lesson from M10d.

Required proof cards now render under `Oblivion -> Cards`:

- intro note
- static status card
- UI preview placeholder
- artifact placeholder
- code fact placeholder
- code theory placeholder

Visible elements include title, kind, status, tags, body text, and optional action/artifact labels. Actions remain visual metadata only.

## Non-execution boundary

M11a does not implement:

- Roslyn compilation or JIT execution
- xUnit `[Fact]` / `[Theory]` runtime
- markdown editing
- a code editor
- project/file explorer behavior

The code-placeholder cards render deferred snippets and explicitly label them as `not executed in M11a`.

M9 font work remains closed:

- `DirectOutlineStatic` stays the static/reference path
- MSDF stays explicit experimental/scalable

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-execution-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-artifacts.png -SelectedSection oblivion -SelectedTab artifacts
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-scrolled.png -SelectedSection oblivion -SelectedTab cards -ScrollPage oblivion.cards:220
```

The shell export path also writes:

- `artifacts/m11a/oblivion-card-model-manifest.json`
- `artifacts/m11a/oblivion-card-model-manifest.txt`

## What changed

- added a sample-local Oblivion card model
- added a bounded static Oblivion card renderer
- added `Oblivion` section/tabs to the presenter shell
- added deterministic card-model manifest export
- added presenter tests for model/rendering/navigation/export/non-execution boundaries

## What did not change

- no production renderer default changed
- no `Machina.Core` semantic changed
- no shared `Machina.Layout` resolver behavior changed
- no Roslyn execution was added
- no xUnit execution runtime was added
- no Visionary editor behavior was added
- no new font or MSDF milestone work was resumed

## Deferred work

- M11b follow-up only reorganizes test topology around this static card proof work. It does not add execution behavior.
- Roslyn-backed executable cards
- real `[Fact]` / `[Theory]` execution, still deferred to M12 or later
- artifact capture from executing cards
- markdown authoring/editing
- Visionary source editor implementation
- richer notebook/workbench interaction beyond static proof rendering

## M11c dependency note

Before Oblivion resumes runtime-facing work, the presenter host now uses the M11c scrollbar/input/composition refactor:

- explicit interaction states for scrollbar drag
- Avalonia still isolated as the current sample backend
- cached page/shell composition so scroll offset changes do not force full rerender

M11c does not add notebook execution, Roslyn execution, or `[Fact]` / `[Theory]` execution behavior.

## M11d follow-up

M11d keeps the M11a boundary but replaces the hardcoded sample-local card catalog with persisted workspace data:

- `workspace.oblivion.json` is now the `.sln`-like workspace graph/tree
- `*.page.toml`, `*.card.toml`, and `*.artifact.toml` are now the `.csproj`-like human-editable asset units
- the presenter now loads static Oblivion cards from disk

M11d still adds no Roslyn execution, no xUnit notebook/runtime execution, no markdown editor, and no Visionary editor behavior.
