# Machina Presenter Adaptive Shell Modes M12h

## Purpose

M12h adds adaptive presenter shell modes without turning Machina into a CSS-style responsive layout system.

The milestone is intentionally narrow:

- add named shell modes: `Wide` and `Compact`
- resolve shell mode from one deterministic width breakpoint
- keep adaptive behavior at the top-level shell/page assembly
- preserve navigation, selection, Markdown rendering, card actions/effects, and keyboard input
- add a compact sidebar rail plus compact card-list/inspector swap for Oblivion pages

M12h does not add a generic responsive solver, continuous interpolation, Markdown editing, Roslyn execution, xUnit execution, or Visionary.

## Why not continuous scaling

Continuous scaling would push breakpoint logic and layout negotiation into every page and card.

That is explicitly not the direction here.

M12h keeps layout deterministic by choosing one of two shell documents and then rendering fixed regions inside that chosen document.

## Responsive behavior as document selection

Current doctrine:

```text
Responsive behavior is document selection, not layout negotiation.

Window width:
  -> ShellMode

ShellMode:
  -> BuildWideShell() or BuildCompactShell()

Cards:
  receive bounded regions
  do not know or negotiate global shell mode
```

Forbidden mental model:

```text
CSS-like continuous flex/grid constraint solving
fluid interpolation
shared algebra between sidebar/list/inspector columns
component-level breakpoint checks everywhere
```

Allowed mental model:

```text
one top-level breakpoint
two named shell documents
deterministic regions inside each mode
```

## Shell modes

M12h adds:

- `PresenterShellMode.Wide`
- `PresenterShellMode.Compact`

It also adds explicit compact pane state:

- `PresenterCompactPane.CardList`
- `PresenterCompactPane.Inspector`

Cards do not receive shell mode and do not branch on shell mode.

## Breakpoint policy

The current breakpoint is `760px`.

Policy:

- width below `760` resolves to `Compact`
- width at or above `760` resolves to `Wide`
- width resolution happens once before shell/page document assembly

This exists to keep the presenter comfortable at two named sizes without introducing continuous scaling.

## Wide mode

Wide mode preserves the current presenter shell structure as closely as possible.

Behavior:

- full sidebar
- local tabs
- normal page viewport
- Oblivion pages continue to render card list and inspector side-by-side
- non-Oblivion pages keep their existing content behavior

## Compact mode

Compact mode is not squeezed wide mode.

Behavior:

- compact sidebar rail
- same section/tab navigation state
- one main region
- Oblivion pages render either a card-list document or an inspector document
- selecting a card switches compact pane to `Inspector`
- Back or `Escape` returns compact pane to `CardList`
- section/tab changes reset compact pane to `CardList`
- selected card survives switching between wide and compact

## Sidebar rail

Compact mode uses a fixed narrow rail instead of the wide sidebar.

Current compact rail policy:

- fixed `64px` width
- no animation
- no interpolation
- short deterministic labels such as `OVR`, `CMP`, `TXT`, `OBL`, `LEG`
- pointer and keyboard navigation remain available

## Card list / inspector swap

For Oblivion pages in compact mode:

- `CardList` shows compact cards only
- clicking a card selects it and switches pane to `Inspector`
- `Inspector` shows the selected-card inspector in the full main region
- `Inspector` includes a Back control
- `Escape` also returns to `CardList`

This is explicit compact pane state, not null-selection inference.

## Input behavior

M12h preserves:

- sidebar pointer selection
- tab pointer selection
- wheel scroll
- scrollbar drag
- M12g keyboard navigation
- selected-card effect routing

M12h adds:

- compact Back action
- compact `Escape -> CardList` behavior

It does not add touch gestures, zooming, or editor input.

## Export commands

Representative exports:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-wide-overview.png -SelectedSection overview -SelectedTab home -Width 1120 -Height 760
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-wide-oblivion-docs.png -SelectedSection oblivion -SelectedTab docs -SelectedCard doc-copeland-markdown-frontend-m12a -Width 1120 -Height 760
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-compact-overview.png -SelectedSection overview -SelectedTab home -Width 720 -Height 760
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-compact-card-list.png -SelectedSection oblivion -SelectedTab docs -Width 720 -Height 760
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-compact-inspector.png -SelectedSection oblivion -SelectedTab docs -SelectedCard doc-copeland-markdown-frontend-m12a -CompactPane Inspector -Width 720 -Height 760
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m12h\presenter-shell-compact-back.png -SelectedSection oblivion -SelectedTab docs -SelectedCard doc-copeland-markdown-frontend-m12a -CompactPane Inspector -Width 720 -Height 760
```

Optional deterministic override remains available:

- `-ShellMode Wide`
- `-ShellMode Compact`

Normal usage should still let width resolve the mode.

## What changed

- added explicit shell mode and compact pane enums
- added a deterministic width resolver with a documented breakpoint
- generalized presenter layout to wide/compact fixed documents
- kept wide mode close to the existing shell
- added compact sidebar rail labels and geometry
- added compact Oblivion card-list and inspector documents
- added compact Back and `Escape` routes
- added adaptive-shell manifest output
- added M12h tests and export coverage

## What did not change

- no continuous scaling
- no CSS/flex/grid-style responsive solver
- no component-level breakpoint checks across cards
- no Markdown editor
- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no Visionary implementation
- no new execution or editor features

## Deferred work

- animated mode transitions
- touch gestures
- zoom/scaling input
- broader compact navigation refinement if future pages need it
- Markdown editing
- Roslyn compilation and execution
- xUnit runtime execution
- Visionary code editor/source workspace
