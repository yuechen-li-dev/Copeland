# Oblivion Independent Scroll Panes M15e

## Purpose

M15e hardens Oblivion reading and inspection around explicit local scroll panes instead of one shared page stack.

## User issues

Selection couples the main stack and inspector content.

Scrolling does not.

The main card stack and inspector are separate scroll panes.

Nested scroll regions must have explicit focus/routing.

A document viewport must render partially visible text; block-level all-or-nothing culling is not acceptable for readable documents.

## Main stack and inspector separation

Wide Oblivion pages now render the card stack and inspector as separate local panes with separate scroll offsets and separate scrollbars.

Selecting a card still updates the inspector content immediately.

Scrolling either pane no longer scrolls the other pane.

## Selection versus scrolling

Selection remains page-local card selection.

Inspector scroll resets to top when the selected card changes.

If the same card stays selected, inspector scroll and raw-source scroll remain local deterministic state instead of snapping the main stack.

## Scroll region model

M15e uses explicit presenter-local scroll regions:

- `MainCardStack`
- `ExpandedMarkdownBody`
- `InspectorPane`
- `InspectorRawMarkdownSource`

`PageScrollOffsetByPageId` continues to back the main card stack in wide Oblivion mode.

`InspectorScrollOffsetByPageId` and `RawMarkdownSourceScrollOffsetByCardId` now track the inspector-side surfaces explicitly.

## Pointer and wheel routing

Wheel input routes to the deepest scrollable region under the pointer.

Pointer over raw source:
  wheel scrolls raw source first

Pointer over expanded Markdown body:
  wheel scrolls the expanded body first

Pointer over inspector outside raw source:
  wheel scrolls the inspector pane

Pointer over the main stack outside nested body viewports:
  wheel scrolls the main card stack

## Scrollbar drag capture

Scrollbar thumb drag now uses explicit capture/release state for:

- expanded Markdown body
- inspector pane
- inspector raw source
- main card stack

Track clicks keep deterministic page-style paging behavior.

Scrollbar drag does not toggle expansion and does not change selection unless selection was already the intended routed action.

## Inspector raw source scrolling

The inspector raw Markdown source block now owns real scroll state, real wheel behavior, real thumb dragging, and real viewport clipping.

The scrollbar is not decorative anymore.

## Expanded Markdown body scrolling

Expanded Markdown bodies still own local scroll state from M15c/M15d.

M15e adds direct thumb dragging, explicit capture, and stronger nested routing so the body scroll region behaves like a deliberate document viewport instead of a wheel-only surface.

## What changed

- wide Oblivion pages now use independent main-stack and inspector panes
- inspector scroll is explicit state
- raw Markdown source scroll is explicit state
- deepest-region wheel routing is explicit
- local scrollbar drag/capture works across nested reading surfaces
- inspector and expanded Markdown viewports now use enforceable clipping

## What did not change

- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work
- no browser-like event system
- no arbitrary `2D` layout solver

## Deferred work

- any future editor surface
- any future execution/runtime work
- any future style/TOML broadening beyond this scroll hardening pass

## M15f follow-through

M15f is the regression-stabilization follow-through for this pane model.

It documents and fixes the M15e main-stack regression where `OblivionMainCardStack` scroll input still dispatched the generic page-scroll action and was then clamped back to zero in wide Oblivion mode.

It also documents the inspector-scroll lag investigation and the narrow safe fix: caching prepared raw-source layout across repeated inspector scroll ticks without adding new feature work or a broad scroll-architecture rewrite.

## M15g closeout note

M15g is the closeout/planning follow-through for the M15 reading-surface arc.

It does not change the M15e pane model. It documents the current golden path, records the remaining UX backlog, and treats independent panes plus nested local scroll regions as the current baseline rather than an active churn target.
