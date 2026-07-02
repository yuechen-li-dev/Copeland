# Machina Card Stack Reading Flow M15c

## Purpose

M15c turns the Oblivion card stack into a real reading flow instead of a selection-only staging area.

## Stack as primary reading surface

The Oblivion card stack is the primary reading surface.

Expanded Markdown cards now render their document body inline in the stack, so reading no longer depends on the inspector.

## Inspector as secondary surface

The inspector is for metadata, actions, diagnostics, artifacts, and secondary detail.

It remains useful, but it is no longer the primary body-reading surface for Markdown cards.

## Accordion-style interaction

Collapsed card:
  scannable summary surface

Expanded card:
  inline document-reading surface

Cards now behave like an accordion: scan, expand, read, scroll locally if needed, then collapse back into the list.

## Local scroll regions

Expanded card bodies own one bounded local scroll region with deterministic scrollbar geometry. This keeps long Markdown documents readable without forcing the whole page to become one oversized card.

## Relationship to M15b resizing/readability

M15c preserves the M15b presenter model:

- runtime presenter window remains resizable
- the presenter still uses a centered effective `16:9` surface
- shell mode still resolves from the live effective width
- readable collapsed preview behavior remains in place

M15c builds on that base by moving the real reading surface into the stack itself.

## Non-goals

- Markdown editing
- notebook execution
- Roslyn/xUnit execution
- Aurelian work
- `VD-MIR` work
- arbitrary `2D` layout solving

## Deferred work

- future editing work, only in a later explicit milestone
- richer inline document interactions if later earned
- any execution/runtime notebook behavior
