# Machina Scroll Region Routing M15f

## Purpose

M15f documents the stabilized presenter-local scroll-routing model after the M15e regressions were traced and fixed.

## Scroll region model

Wide Oblivion uses explicit presenter-local scroll regions:

- `MainCardStack`
- `InspectorPane`
- `ExpandedMarkdownBody`
- `InspectorRawMarkdownSource`

These are not generic browser regions and do not imply a new global event system.

## Hit-test ordering

Routing enumerates only regions whose bounds contain the pointer and then orders them from deepest/narrowest intent to broader pane intent:

1. `InspectorRawMarkdownSource`
2. `ExpandedMarkdownBody`
3. `InspectorPane`
4. `MainCardStack`

This ordering is explicit and local to the Oblivion presenter interaction map.

## Effective surface coordinates

Presenter input first arrives in root/shell coordinates.

`PresenterNavigationInputRouter` converts root coordinates into content-viewport-local coordinates before routing into Oblivion page interaction.

M15f regression coverage keeps this path explicit so main-stack wheel routing continues to use effective presenter-surface coordinates instead of raw page-local assumptions.

## Wheel routing

Wheel input routes to the deepest scrollable region under the pointer.

If a matching region can scroll, it emits the region-specific scroll action and suppresses broader routing.

This is why:

- wheel over raw source scrolls raw source
- wheel over expanded Markdown body scrolls body
- wheel over inspector pane scrolls inspector
- wheel over the card stack scrolls the main stack

## Scrollbar capture

Scrollbar drag uses explicit capture/release state keyed by the concrete scroll target, not by a browser-like bubbling model.

Thumb drag:

- captures on primary press
- updates only the captured region while dragging
- releases deterministically on pointer up

## Main stack region

M15f clarifies that the wide main card stack is not generic page scroll.

Its scroll target is `OblivionMainCardStack`, and its reducer now emits `SetOblivionMainCardStackScrollOffset(...)`.

That state is clamped against actual card-stack content height rather than wide-page document height.

## Inspector region

The wide inspector pane remains its own local scroll region with its own offset.

Inspector selection content still follows selected-card state.

Inspector scrolling does not modify main-stack scroll.

## Nested body/source regions

Expanded Markdown body and inspector raw source remain nested scrollable regions inside their owning panes.

Their routing priority stays above the parent pane so wheel/drag interaction targets the deepest intended region first.

## Performance/caching notes

Inspector-pane scroll still rerenders the page layer because the pane is part of the page document.

M15f does not rewrite that architecture.

The traced lag fix instead removes repeated raw-source line clipping/layout work by caching prepared raw-source layout across repeated scroll ticks with the same raw text, width, and line metrics.

## Non-goals

- no browser/CSS-like event model
- no arbitrary freeform `2D` layout solver
- no Markdown editing
- no notebook execution
- no Aurelian work
- no `VD-MIR` work
