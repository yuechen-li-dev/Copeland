# Machina Oblivion Workspace Persistence M11d

## Purpose

M11d moves Oblivion from sample-local hardcoded catalog data to a persisted workspace model.

The milestone stays narrow:

- define a persisted workspace model
- use JSON for the workspace root manifest
- use TOML for page, card, and artifact assets
- load a static workspace from disk into the existing presenter shell
- keep execution, editors, and Visionary deferred

## JSON root / TOML asset split

M11d uses a deliberate format split:

- `workspace.oblivion.json` is the workspace graph/tree
- `*.page.toml`, `*.card.toml`, and `*.artifact.toml` are human-editable asset units

Analogy:

```text
workspace.oblivion.json ~= .sln
*.page.toml / *.card.toml / *.artifact.toml ~= .csproj-like asset metadata
```

## Why JSON is the workspace root

The root manifest is nested and tree-shaped:

- workspace metadata
- section/page hierarchy
- page ordering
- card ordering
- relative asset references

That makes JSON the practical choice for the `.sln`-like coordination file.

## Why TOML is used for cards/assets

Cards, pages, and artifact metadata are intended to stay readable and hand-editable.

TOML works well for:

- explicit key/value metadata
- short lists such as tags
- repeated tables such as actions and artifacts
- multiline body text

## File layout

Sample workspace:

```text
samples/Machina.Presenter.Sample/OblivionSampleWorkspace/
  workspace.oblivion.json
  pages/
    cards.page.toml
    execution-roadmap.page.toml
    artifacts.page.toml
  cards/
    intro.card.toml
    status.card.toml
    ui-preview-placeholder.card.toml
    artifact-placeholder.card.toml
    code-fact-placeholder.card.toml
    code-theory-placeholder.card.toml
    execution-roadmap.card.toml
    visionary-relationship.card.toml
    artifacts-overview.card.toml
    artifact-policy.card.toml
  artifacts/
    workspace-manifest.artifact.toml
    presenter-proof.artifact.toml
```

## Root workspace schema

Representative shape:

```json
{
  "format": 1,
  "kind": "oblivion-workspace",
  "workspaceId": "machina-sample",
  "title": "Machina Sample Workspace",
  "defaultPageId": "cards",
  "sections": [
    {
      "id": "oblivion",
      "title": "Oblivion",
      "pages": [
        {
          "id": "cards",
          "title": "Cards",
          "asset": "pages/cards.page.toml",
          "cards": [
            "cards/intro.card.toml",
            "cards/status.card.toml"
          ]
        }
      ]
    }
  ]
}
```

Required root concerns:

- format version
- kind
- workspace id
- title
- section/page hierarchy
- stable ids
- ordered card references
- relative paths only

## Card TOML schema

Representative shape:

```toml
format = 1
kind = "card"
id = "oblivion-intro-note-card"
card_kind = "note"
status = "idle"
title = "Oblivion workbench substrate"
subtitle = "Notebook/card/workbench layer"
tags = ["oblivion", "m11d", "workspace"]

[body]
format = "plain"
text = """
Oblivion is the notebook/card layer for Machina Workbench.
Execution is deferred to M12+.
"""
```

Supported card kinds:

- `note`
- `status`
- `ui-preview`
- `artifact`
- `code-fact`
- `code-theory`

Supported statuses:

- `idle`
- `passing`
- `failing`
- `warning`
- `deferred`
- `placeholder`

Actions and artifact entries are metadata only in M11d.

## Page TOML schema

Representative shape:

```toml
format = 1
kind = "page"
id = "cards"
title = "Cards"
description = "Persisted Oblivion cards loaded from disk."
tags = ["oblivion", "cards", "persistence"]
```

Page TOML is optional at the manifest level, but the sample workspace uses it for page metadata.

## Loader and validation

Sample-local persistence services now include:

- `OblivionWorkspaceJsonReader`
- `OblivionWorkspaceJsonWriter`
- `OblivionPageTomlReader`
- `OblivionPageTomlWriter`
- `OblivionCardTomlReader`
- `OblivionCardTomlWriter`
- `OblivionArtifactTomlReader`
- `OblivionArtifactTomlWriter`
- `OblivionWorkspaceLoader`
- `OblivionWorkspaceValidator`

Validation covers:

- unsupported format versions
- unknown root kind
- duplicate section ids
- duplicate page ids
- unknown card kind
- unknown card status
- missing page/card/artifact assets
- page/artifact id mismatches

Serialization is deterministic and intentionally omits timestamps.

## Path safety

Asset references are resolved relative to the workspace root.

The loader rejects:

- absolute asset paths unless explicitly allowed by loader options
- path traversal outside the workspace root
- missing page/card/artifact assets with stable diagnostics

## Presenter integration

The presenter shell keeps the existing public page IDs:

- `oblivion.cards`
- `oblivion.execution-roadmap`
- `oblivion.artifacts`

Behavior:

- default startup stays `Overview -> Home`
- Oblivion pages now render cards loaded from the sample workspace on disk
- `--oblivion-workspace` can override the default sample workspace path
- if workspace loading fails, Oblivion renders a bounded error/status card instead of crashing
- the old hardcoded catalog remains available as a fallback when the default sample workspace is absent

## Export commands

Representative exports:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-artifacts.png -SelectedSection oblivion -SelectedTab artifacts
```

Manifest outputs:

- `artifacts/m11d/oblivion-workspace-persistence-manifest.json`
- `artifacts/m11d/oblivion-workspace-persistence-manifest.txt`

## What changed

- Oblivion now has a persisted workspace model
- the workspace root is JSON
- page/card/artifact assets are TOML
- the presenter loads a static sample workspace from disk
- deterministic serialization and roundtrip tests now cover the persistence layer
- export manifests now record the JSON/TOML split and load counts

## What did not change

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` runtime execution behavior
- no markdown editor
- no Visionary editor
- no artifact execution/generation runtime
- no production renderer/core/layout behavior change
- no reopened font or MSDF milestone work

## Deferred work

- Roslyn-backed executable cards
- notebook/runtime `[Fact]` / `[Theory]` execution
- markdown authoring/editing
- Visionary source workspace/editor implementation
- richer artifact generation/runtime behavior
- broader project/file explorer behavior
