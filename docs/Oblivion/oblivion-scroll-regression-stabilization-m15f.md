# Oblivion Scroll Regression Stabilization M15f

## Purpose

M15f is a regression-stabilization pass for the M15e independent-scroll work.

It fixes the broken main card stack wheel/scrollbar path, investigates the new inspector scroll lag, and lands only the smallest verified fixes needed to restore predictable behavior.

## User-reported regressions

- the main card stack scrollbar stopped working for wheel and thumb interaction
- the inspector scrollbar still moved, but scrolling introduced a visible lag that was not present before M15e

## Root-cause investigation method

For each regression, M15f traced the exact presenter path:

1. pointer location
2. hit-tested scroll region
3. selected scroll target
4. dispatched action
5. state field updated
6. layout/render path consuming that state

The fixes in M15f are based on that traced path rather than speculative routing changes.

## Main card stack scroll regression

The M15e wide Oblivion card stack registered itself as `OblivionMainCardStack`, but the scrollbar reducer still emitted the generic page-scroll action.

That action was then clamped through the wide-page scroll path.

M15e had already changed wide Oblivion `GetPageContentHeight(...)` to return the viewport height because wide mode now uses local panes instead of one tall page document.

That meant the generic page-scroll clamp always computed `maxScrollOffset = 0` for wide Oblivion pages.

Wheel and thumb-drag input both arrived, but both were reduced into a no-op scroll offset.

## Main card stack root cause

Exact break:

`OblivionMainCardStack`
  -> `PresenterScrollbarInteractionStateMachine.BuildSetScrollAction(...)`
  -> `PresenterNavigationActions.SetScrollOffset(...)`
  -> `PresenterNavigationDispatch.TryParseSetScrollOffset(...)`
  -> wide Oblivion page-height clamp
  -> offset forced back to `0`

This was not an inspector hit-test overlap bug and not a pointer-coordinate bug.

The scroll target was correct.

The dispatched action type was wrong for the new wide-pane model.

## Main card stack fix

M15f adds a dedicated `SetOblivionMainCardStackScrollOffset` action and routes `OblivionMainCardStack` wheel/thumb interaction to that action instead of the generic page-scroll action.

Dispatch and normalization now clamp that state through `ClampMainCardStackScrollOffset(...)`, which uses the actual card-column content height rather than wide-page document height.

Result:

- wheel over the wide card stack updates the main stack offset again
- main stack thumb drag updates the main stack offset again
- drag does not toggle expansion
- inspector scroll remains independent

## Inspector scroll lag

The inspector lag investigation used presenter render diagnostics plus a new raw-source layout counter.

Observed behavior:

- inspector scroll still invalidates the page render count because the wide inspector pane is rendered inside the page layer
- shell render count does not increase for the same scroll step
- before the M15f fix, raw Markdown source line clipping/layout rebuilt again on each inspector scroll tick

## Inspector lag root cause

The visible lag was not caused by accidental wheel bubbling into the main stack.

It also was not caused by shell-chrome rerender.

The traced cause was:

1. inspector scroll changes local pane geometry, so the page layer rerenders
2. raw Markdown source layout/clipping work was rebuilt again on every tick
3. docs-page inspector scrolling therefore paid repeated raw-source layout work inside an already-required page rerender

The new diagnostic regression test proves the post-fix behavior:

- page render count still advances from `1` to `2` across two inspector scroll positions
- raw-source layout build count stays at `1`

## Inspector lag fix

M15f keeps the existing narrow presenter architecture and avoids a broad pane-composition rewrite.

Instead, it caches prepared raw Markdown source layout by raw text, width, and line metrics.

That removes repeated raw-source clipping/layout work across inspector scroll ticks while preserving:

- independent panes
- deepest-region wheel routing
- raw-source scroll state
- clip-to-bounds behavior

M15f does not claim that inspector scroll is now composition-only.

It claims the observed repeated raw-source layout rebuild was removed safely.

## Regression tests

M15f adds focused regression coverage for:

- main-stack wheel routing
- main-stack thumb drag
- main-stack root-cause action type
- effective-surface coordinate routing
- inspector offset isolation
- inspector single-region wheel routing
- cached raw-source layout across repeated inspector scroll ticks
- preserved M15e pane/body/raw-source/culling behavior

## Preserved M15e behavior

M15f preserves:

- independent main-stack and inspector scroll panes
- expanded Markdown local body scroll
- inspector raw-source local scroll
- deepest-region wheel routing
- partial viewport culling for intersecting Markdown blocks
- clip-to-bounds enforcement for expanded body and raw source

## What changed

- main card stack now uses its own explicit scroll action and clamp path
- wide Oblivion normalization no longer collapses main-stack scroll back to zero
- raw Markdown source layout is cached across repeated inspector scroll ticks
- deterministic M15f regression tests and manifest were added

## What did not change

- no new UX features
- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work
- no broad scroll architecture rewrite

## Deferred work

- a future pane-local composition path if inspector scroll must avoid page-layer rerender entirely
- any broader scroll/render performance work outside this regression-stabilization scope
