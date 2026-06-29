# Machina Presenter Page Organization M10c

## Purpose

M10c makes the presenter navigation shell the canonical sample surface for `samples/Machina.Presenter.Sample`.

The sample no longer opens on the old single-card root by default. Instead it opens inside the organized shell and keeps the old M1e card as preserved content inside that hierarchy.

## Why the shell is now the canonical presenter surface

M10a and M10b already proved the shell structure, immutable navigation state, per-page scroll offsets, deterministic scrollbar geometry, and backend-neutral input routing.

What remained awkward was the sample default:

- normal presenter runs still opened the old single-screen card first
- the shell felt like an export/proof mode instead of the real sample
- newer proof/sample content still risked accumulating into one long root surface

M10c resolves that by making the shell the actual presenter surface.

## Default run behavior

Default presenter behavior is now:

```text
run presenter
  -> navigation shell
  -> Overview
  -> Home
```

Compatibility behavior remains available:

- `--include-navigation-shell` is still accepted
- `--legacy-single-card` switches back to the old single-card root

Default export behavior also now uses the shell.

## Sidebar sections

M10c organizes the current presenter content into these sidebar sections:

- `Overview`
- `Components`
- `Text`
- `Diagnostics`
- `Legacy`

This is still sample organization only. No new production routing framework or new widget family was introduced.

## Local tabs

Current tab organization:

- `Overview`: `Home`, `Status`
- `Components`: `Controls`, `Cards`
- `Text`: `Current`, `DirectOutlineStatic`, `Proofs`
- `Diagnostics`: `Layout`, `Export`
- `Legacy`: `M1e Card`

Each selected section resolves to local tabs only for that section, and each section/tab resolves to one stable page id.

## Legacy M1e card page

The old single-card sample is preserved under:

- section: `Legacy`
- tab: `M1e Card`
- page id: `legacy.m1e-card`

That page keeps the old presenter card/sample content available without leaving it as the root application surface.

## Existing content migration

M10c keeps the current presenter content but redistributes it into the shell:

- overview/status copy now lives in `Overview`
- the existing settings card, increment button, email checkbox, notification switch, and text block remain available under `Components`
- direct-outline proof content, when requested, lives under `Text`
- scroll/export/layout notes live under `Diagnostics`
- the old root card is preserved under `Legacy`

No content was moved into a new component family.

## Export commands

Representative M10c exports:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-overview.png
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-text-direct-outline.png -SelectedSection text -SelectedTab direct-outline -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-diagnostics-layout.png -SelectedSection diagnostics -SelectedTab layout
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-scrolled.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:120
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-legacy-single-card.png -LegacySingleCard
```

Shell manifests now write as:

- `artifacts/m10c/presenter-shell-manifest.json`
- `artifacts/m10c/presenter-shell-manifest.txt`

## Interaction behavior preserved

M10c keeps the M10b interaction boundary:

- sidebar clicks still select sections
- local tab clicks still select tabs
- wheel input over the content viewport still updates selected-page scroll offsets
- M10d later adds scrollbar thumb dragging and card-bound containment without changing the shell model
- Avalonia remains only the current sample input backend
- state/actions/hit testing remain backend-neutral

## What changed

- the navigation shell is now the default presenter surface
- the old single-card sample is preserved as a page instead of the root app
- shell manifests now describe the canonical shell instead of an opt-in interaction mode
- export defaults now produce shell artifacts
- presenter content is organized into sidebar sections and local tabs

## What did not change

- no production renderer default changed
- no `Machina.Core` document model semantic changed
- no `Machina.Layout` resolver behavior changed
- no `Standard.Text` semantic changed
- no new font work was resumed
- no new component family was introduced
- no router framework was introduced

## Deferred work

- reusable Standard sidebar/tab components
- keyboard/focus/accessibility systems
- richer shared clipping/scrolling semantics
- any new font integration work unless a concrete integration need appears

## Follow-on note

M11a extends this shell with an `Oblivion` section:

- `Cards`
- `Execution Roadmap`
- `Artifacts`

`Oblivion` is the notebook/card/workbench layer. `Visionary` is still only the future code editor/source workspace layer and is not implemented in M11a.
