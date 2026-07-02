# Oblivion Expandable Markdown Cards M15c

## Purpose

M15c makes the Oblivion card stack the primary reading surface for Markdown-backed cards.

## Why body content moves into the stack

The Oblivion card stack is the primary reading surface.

The inspector is for metadata, actions, diagnostics, artifacts, and secondary detail.

Markdown body content should be readable inline in expanded cards.

A user should be able to scan collapsed cards, expand one, read it, scroll its body if needed, and collapse it again without leaving the stack.

## Collapsed card behavior

Collapsed card:
  scannable summary surface

Collapsed cards stay compact and show title, source label, tags/status chips, and a bounded readable summary line instead of the full Markdown body.

## Expanded card behavior

Expanded card:
  inline document-reading surface

Expanded Markdown cards keep the header and metadata visible, render the Markdown body inline inside the stack, and bound the body height so one card does not consume the whole page.

## Expansion state model

Expansion state is explicit, page-local, and card-id keyed through `OblivionCardViewState`.

The stored state currently includes:

- `IsExpanded`
- `BodyScrollOffset`

Body scroll offset is preserved deterministically when a card collapses.

## Selection versus expansion

Selection and expansion are separate explicit states.

Expanding a card also selects it, but selection alone does not imply expansion.

## Local body scrolling

Expanded cards own one local body scroll region.

Wheel input over that region scrolls the card body first. If the local region cannot scroll further, normal page scrolling can continue through the existing shell behavior.

## Markdown rendering

M15c reuses the existing read-only Markdown rendering path.

Headings, paragraphs, lists, links, and fenced code blocks render inline in the expanded body. Unsupported syntax remains diagnostic-only and non-fatal.

## Inspector role

Inspector:
  metadata/action/diagnostic/artifact surface

The inspector still renders selected-card metadata, deferred actions/effect state, diagnostics, and artifacts. It is no longer the only place to read the card body.

## Input behavior

- Click card header/title: select card and toggle expansion.
- Click inside expanded body: select card, do not collapse.
- `Enter` / `Space` on the selected card: toggle expansion.
- `Escape`: collapse the selected expanded card first, then fall back to prior shell behavior.

## Export evidence

Proof artifacts live under `artifacts/m15c/`:

- `m15c-oblivion-docs-collapsed-1280x720.png`
- `m15c-oblivion-docs-expanded-1280x720.png`
- `m15c-oblivion-docs-expanded-scrolled-1280x720.png`
- `m15c-oblivion-cards-expanded-1280x720.png`
- `m15c-oblivion-docs-compact-expanded-960x540.png`
- `m15c-oblivion-inspector-after-expand-1280x720.png`
- `oblivion-expandable-markdown-cards-manifest.json`
- `oblivion-expandable-markdown-cards-manifest.txt`

## What changed

- added explicit page-local card expansion state
- rendered Markdown body content inline inside expanded cards
- kept collapsed cards compact and scannable
- added local body scroll state plus deterministic scrollbar geometry
- routed pointer and keyboard expansion/collapse through explicit shell actions

## What did not change

- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work
- no arbitrary `2D` layout solver

## Deferred work

- pointer-drag local scrollbar behavior if later needed
- richer per-card density controls if later earned
- any future editor or execution work

## M15d follow-through

M15d hardens the M15c reading surface rather than widening scope.

- rendered Markdown now uses an explicit shared reading-style record with readable contrast
- expanded Markdown cards now use document-scale height instead of a short preview-like panel
- the inspector no longer renders formatted Markdown body content
- the inspector now shows raw Markdown source text in a bounded scrollable source block

M15d still does not add Markdown editing, execution, Aurelian work, or `VD-MIR` work.

## M15e follow-through

M15e keeps M15c expansion behavior, but hardens nested scrolling around it.

- the main stack and inspector are now independent panes
- expanded body scrollbars can now be dragged directly
- selection still couples the inspector content, but scrolling does not
