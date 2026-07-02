# Machina Document Viewport Culling M15e

## Purpose

M15e removes block-level all-or-nothing document culling from the expanded Markdown reading path.

## Previous all-or-nothing culling failure

The prior viewport test only rendered blocks fully inside the viewport.

That meant a paragraph could disappear entirely when it was partially visible, then pop back only after the whole block fit.

That is not acceptable for a readable document surface.

## Block layout versus render visibility

Document viewport rendering:
  skip blocks fully outside viewport
  render visible portions of blocks that intersect the viewport
  clip top/bottom lines intentionally
  never disappear a paragraph just because it is partially visible

M15e keeps block measurement/layout, but changes render visibility so intersecting blocks stay eligible for rendering.

## Partial block rendering

Expanded Markdown now treats a block as visible whenever its bottom is below the viewport top and its top is above the viewport bottom.

Fully outside blocks are still skipped.

Intersecting blocks are rendered and then clipped by the owning viewport.

## Line-level clipping

The current renderer intentionally allows top and bottom lines to be clipped by the viewport.

This produces continuous scrolling instead of block snapping.

`lineLevelClippingImplemented=true` in the M15e manifest reflects that the rendered result now behaves like a real document viewport even though the implementation stays narrow and presenter-local.

## Clip region contract

M15e adds an explicit clip-to-bounds path in the existing render bridge and uses it for:

- expanded Markdown body viewports
- inspector raw-source viewports
- wide Oblivion main-stack and inspector panes

Scrollbars remain outside clipped text content and stay visible.

## Export evidence

Proof artifacts live under `artifacts/m15e/`:

- `m15e-independent-panes-overview-1280x720.png`
- `m15e-expanded-markdown-partial-scroll-1280x720.png`
- `m15e-expanded-markdown-mid-paragraph-1280x720.png`
- `m15e-inspector-raw-source-scrolled-1280x720.png`
- `m15e-inspector-pane-scrolled-1280x720.png`
- `m15e-compact-expanded-scroll-960x540.png`
- `oblivion-independent-scroll-panes-manifest.json`
- `oblivion-independent-scroll-panes-manifest.txt`

## Limitations

This is still a presenter-local document surface, not a full browser/CSS layout engine.

The implementation stays intentionally narrow:

- no general event bubbling model
- no arbitrary freeform layout solver
- no editing/runtime behavior

## What changed

- block culling now keeps intersecting blocks renderable
- expanded Markdown viewports now clip instead of relying on full-block admission
- raw-source viewports use the same clipping contract

## What did not change

- no font-pipeline rewrite
- no renderer architecture rewrite
- no Markdown editing
- no notebook execution
- no Aurelian work
- no `VD-MIR` work

## Deferred work

- any future richer line-level visibility optimization beyond this presenter-local hardening pass
- any future generalized clip/style system if other surfaces truly need it

## M15f follow-through

M15f does not change the M15e partial-viewport culling contract.

The M15f stabilization work preserves:

- intersecting Markdown blocks remain renderable
- fully outside blocks are still skipped
- expanded body and raw-source clip regions remain enforced

The regression work stays focused on scroll routing, scroll action/clamp correctness, and inspector-side raw-source layout reuse.
