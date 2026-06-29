# Machina Presenter Navigation Interaction M10b

## Purpose

M10b wires pointer and wheel interaction into the presenter navigation shell that M10a introduced.

The goal is narrow:

- sidebar section selection
- local tab selection
- vertical page scrolling for the selected page
- deterministic exportable proof states

This remains sample-local presenter work. No production renderer default, `Machina.Core` document semantics, or shared `Machina.Layout` resolver behavior changed.

## Input backend boundary

The boundary is:

```text
Avalonia pointer/wheel event
  -> AvaloniaPresenterInputBackend
      -> PresenterInputEvent
          -> PresenterNavigationInputRouter
              -> PresenterNavigationActions
                  -> PresenterNavigationDispatch
                      -> PresenterNavigationState
```

`PresenterInputEvent`, hit targets, routing, action ids, and navigation state use only primitive sample-local types and existing `UiActionId`.

## Why Avalonia is only the current backend

Avalonia is still just the current sample host.

- Avalonia-specific translation stays in `samples/Machina.Presenter.Sample/AvaloniaPresenterInputBackend.cs`.
- navigation hit testing, routing, state, and dispatch do not take Avalonia types.
- this keeps the interaction model movable if the presenter sample later changes host technology or drops windowed input entirely.

## Navigation action flow

- sidebar press routes to `presenter.navigation.select-section|<sectionId>`
- tab press routes to `presenter.navigation.select-tab|<sectionId>|<tabId>`
- wheel over the content viewport routes to `presenter.navigation.set-scroll-offset|<pageId>|<offset>`
- optional scrollbar track press pages up/down through the same explicit scroll-offset action

No hidden mutable navigation state was added.

## Hit testing

M10b adds deterministic shell hit testing for:

- `SidebarSection`
- `LocalTab`
- `ContentViewport`
- `ScrollbarTrack`
- `ScrollbarThumb`
- `None`

The hit-test model uses geometry derived from the same presenter navigation layout and shell chrome geometry that render the shell.

## Sidebar interaction

- clicking a sidebar item selects that section
- the last selected tab for that section is restored
- if a section has no remembered tab, the first tab remains the deterministic fallback
- per-page scroll offsets remain attached to page ids

## Tab interaction

- clicking a visible local tab selects that tab
- tabs are only emitted for the selected section, so inactive-section tabs are not interactable
- the selected page restores its own scroll offset

## Scroll wheel behavior

- wheel only affects the selected page when the pointer is over the content viewport
- current rule is `scrollOffset -= wheelDeltaY * 48`
- positive wheel delta scrolls upward
- negative wheel delta scrolls downward
- offsets clamp to `0..maxScrollOffset`
- if content fits the viewport, wheel input does nothing

## Scrollbar interaction

Wheel scrolling is the required behavior for M10b.

M10b also adds low-risk scrollbar track paging:

- pressing above the thumb pages upward
- pressing below the thumb pages downward

Thumb dragging is still deferred.

## State ownership

- presenter app state owns selected section id
- section-local state owns the selected tab id for that section
- page-local state owns the scroll offset for that page id
- Avalonia does not own presenter navigation state

## Export commands

Build/test:

```powershell
dotnet test Copeland.slnx
dotnet build Copeland.slnx --no-restore
```

Representative M10b exports:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-overview.png -IncludeNavigationShell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-components-selected.png -IncludeNavigationShell -SelectedSection components
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-tab-selected.png -IncludeNavigationShell -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-scrolled.png -IncludeNavigationShell -SelectedSection components -SelectedTab controls -ScrollPage components.controls:120
```

The navigation manifest now writes:

- `artifacts/m10b/presenter-navigation-interaction-manifest.json`
- `artifacts/m10b/presenter-navigation-interaction-manifest.txt`

## What changed

- added backend-neutral presenter input event types
- added deterministic shell hit testing
- added `AvaloniaPresenterInputBackend` in the sample host
- routed sidebar, tab, wheel, and scrollbar-track input through explicit navigation actions
- added selected section/tab export controls
- added interaction-focused presenter tests and docs

## What did not change

- production presenter legacy mode still exists as an explicit compatibility path after M10c
- production renderer defaults
- `Machina.Core` document semantics
- `Machina.Layout` resolver behavior
- M9 font-phase closure
- generic routing framework work
- keyboard/focus/accessibility systems

M10c later makes this interacted shell the canonical/default presenter surface. See [docs/machina-presenter-page-organization-m10c.md](./machina-presenter-page-organization-m10c.md).

## Deferred work

- thumb dragging
- keyboard navigation
- focus/accessibility state
- reusable Standard sidebar/tab components
- any new font integration work unless a concrete consumer requires it
