# Machina Presenter 16:9 Resizing M15b

## Purpose

M15b turns the presenter runtime back into a controlled usability workbench by enabling live resize without opening arbitrary freeform 2D layout work.

## Why 16:9 for this pass

Runtime presenter resizing is 16:9 constrained for M15b.

The OS window may be resizable, but the presenter content surface keeps a fixed 16:9 aspect ratio.

The presenter enforces a minimum usable size.

Layout recomputes from the effective presenter surface, not arbitrary window dimensions.

This is live resizing without full 2D constraint solving.

The letterboxed surface model is the safer path for this pass because it keeps runtime behavior deterministic and avoids platform-specific aspect-ratio enforcement quirks.

## Runtime versus export sizing

Runtime and export sizing are now intentionally separated.

- Runtime defaults to a `1280x720` usable surface and enforces a `960x540` minimum.
- Export still accepts explicit width and height values, including non-16:9 proof sizes where an artifact workflow needs them.
- Runtime no longer snaps the OS window back to export-sized composed frames on redraw.

## Presenter surface model

The runtime now resolves one small model:

```text
Window:
  resizable

Presenter surface:
  largest centered 16:9 rectangle inside the window client area
  minimum usable surface enforced
  neutral letterbox/background outside the presenter surface if the window is not exactly 16:9

Layout:
  sees effective presenter surface width/height only

Shell mode:
  resolves from effective presenter surface width
```

## Minimum size

M15b uses:

- minimum presenter surface: `960x540`
- default runtime presenter surface: `1280x720`

These are large enough to keep the workbench readable while still letting us observe compact versus wide shell behavior under a controlled breakpoint.

## Resize behavior

- the Avalonia runtime window is now resizable
- runtime redraw no longer forces the OS window size back to the rendered frame
- the presenter image sits inside a neutral letterboxed host
- when the window is not exactly `16:9`, the effective presenter surface stays centered and bounded

## Layout recomposition

- layout is rebuilt from the live effective presenter surface width and height
- page width, viewport height, scroll-region geometry, and shell chrome all recompute from that live surface
- render-session cache keys already depended on layout and content width, so resize invalidation remains natural and deterministic
- the fast path remains: if the effective surface size does not change, no rebuild is forced

## Adaptive shell behavior

Adaptive shell mode now resolves from the effective runtime presenter surface width rather than only the startup width.

For M15b the breakpoint is intentionally high enough that `960x540` can still exercise compact mode while `1280x720` and larger surfaces remain wide.

## What changed

- added a small presenter-surface sizing helper
- enabled runtime resize
- moved runtime defaults to a `16:9` surface
- enforced a minimum usable presenter size
- separated runtime surface sizing from export frame sizing
- rebuilt layout on effective-surface changes
- resolved shell mode from live effective surface width

## What did not change

- no arbitrary freeform 2D layout solver
- no CSS/flex/grid-like responsive engine
- no renderer architecture rewrite
- no editor or execution work
- no Aurelian work
- no VD-MIR work

## Deferred work

- richer resize affordances if later needed
- inspector-local scrolling if later readability work proves it necessary
- broader shell visual refinement after the controlled resize/readability pass
- any future freeform workbench layout model, only if later earned
