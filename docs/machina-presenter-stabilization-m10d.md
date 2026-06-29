# Machina Presenter Stabilization M10d

## Purpose

M10d stabilizes the existing presenter navigation shell before any new workbench or card-system work resumes.

The goal is narrow:

- fix the immediate text-page layout failures
- make scrollbar mouse interaction work on scrollable presenter pages
- make presenter cards behave like bounded self-contained cells
- keep the M10a/M10b/M10c shell structure and default behavior intact

## Bugs fixed

- `Text -> DirectOutlineStatic` now renders through a bounded presenter proof card and uses a page content height that actually covers the proof surface.
- `Text -> Proofs` no longer throws `Machina.Layout.Diagnostics.LayoutError`; the failing fixed-height info card now uses an explicit bounded header/body card layout instead of an overfull stack.
- scrollable presenter pages now support deterministic scrollbar track paging and thumb dragging.
- presenter sample cards now use finite body regions with clipped/truncated sample-local copy instead of relying on implicit stack growth inside fixed-size cards.

## Scrollbar mouse interaction

The interaction path remains:

```text
Avalonia input
  -> AvaloniaPresenterInputBackend
  -> PresenterInputEvent
  -> PresenterNavigationInputRouter
  -> PresenterNavigationActions
  -> PresenterNavigationDispatch
```

M10d adds sample-local scrollbar drag state:

```csharp
public sealed record PresenterScrollbarDragState(
    string PageId,
    float DragStartPointerY,
    float DragStartScrollOffset);
```

Behavior:

- clicking the scrollbar track pages up/down deterministically
- pressing the thumb starts drag state
- pointer move while captured maps thumb travel back into page scroll offset
- pointer release clears drag state and pointer capture
- wheel scrolling from M10b still works

Avalonia remains only the current input backend. Drag state, hit testing, actions, and reducer logic stay backend-neutral and sample-local.

## Text page crash diagnosis

The immediate crash came from a fixed-height presenter card whose stacked children needed more vertical space than the card body actually had after card padding and gaps were applied.

Observed failure:

- exception: `Machina.Layout.Diagnostics.LayoutError`
- message: `Stack remaining main-axis space is negative for node 'text-proofs-status/ui_0'`

Root cause:

- sample cards were using fixed-height `StandardUI.Card(...)` stacks with rich text bodies
- several pages assumed those bodies would “just fit”
- the `Text -> Proofs` status card did not fit, so stack resolution hit negative remaining main-axis space
- `Text -> DirectOutlineStatic` also had sloppy shell/page sizing because the page content height under-reported the real proof-card extent

Fix:

- introduce a presenter-sample card helper with explicit title/badge/body regions
- give text pages finite body regions instead of implicit stack growth
- use sample-local line clipping/truncation for presenter card copy
- size the direct-outline page content to the actual proof card footprint

## Card/cell containment policy

Presenter cards now follow a sample-level containment rule:

- outer card rect is fixed and testable
- title/header area is separate from the body area
- body content gets a finite local content rect
- presenter copy is clipped/truncated before it can spill outside that rect
- hosted sample content such as the legacy M1e card and the direct-outline proof sits inside a bounded body frame

This is presenter-sample policy only. It does not change production renderer defaults, `Machina.Core` semantics, `Machina.Layout` shared resolver behavior, or `Standard.Text` semantics.

## Layout policy

M10d adopts this simple presenter rule:

```text
The page scroll region handles page overflow.
Cards handle their own local clipping, spacing, and bounded content regions.
Cards do not rely on unbounded stack growth inside fixed-size slots.
```

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-components-controls-scrolled.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:344
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-current.png -SelectedSection text -SelectedTab current
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-direct-outline.png -SelectedSection text -SelectedTab direct-outline -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-proofs.png -SelectedSection text -SelectedTab proofs -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-diagnostics-layout.png -SelectedSection diagnostics -SelectedTab layout
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
```

## What changed

- presenter sample cards now use explicit bounded card framing
- the direct-outline proof page uses a bounded proof card and corrected page height
- the proofs page no longer overflows a fixed-height stack card
- scrollbar track paging and thumb dragging are implemented in the sample shell
- regression tests now cover the crash paths, bounded card geometry, shell preservation, and scrollbar mouse behavior

## What did not change

- no production renderer default changed
- no `Machina.Core` document semantic changed
- no shared `Machina.Layout` resolver behavior changed
- no `Standard.Text` semantic changed
- no new component family was added
- no font rendering phase was resumed
- no DirectOutlineStatic or MSDF renderer behavior was redefined

## Deferred work

- richer shared clip-stack semantics in production renderer layers
- keyboard/focus/accessibility work for the presenter shell
- reusable Standard navigation widgets
- any broader presenter workbench or card-system feature work beyond this stabilization pass

## Follow-on note

M11a builds directly on this containment policy.

- Oblivion cards reuse the same bounded outer/body framing idea.
- `Oblivion` becomes the notebook/card/workbench layer inside the shell.
- `Visionary` remains documentation-only as the future code editor/source workspace layer.
- M10d remains the stabilization host; M11a does not reopen M9 font work or add execution runtime behavior.

## M11c follow-up

M10d kept scrollbar behavior functional, but its drag path still used nullable sample-local drag routing and rerendered more than necessary during scroll.

M11c keeps the M10d behavior surface intact while changing the internals:

- explicit scrollbar interaction states replace nullable drag bookkeeping
- pointer capture/release is requested explicitly by the interaction state machine
- scroll offset changes use cached page/shell layers plus cheap recomposition

This remains presenter-shell refactor work only. No Roslyn execution, no resumed font work, and no `[Fact]` / `[Theory]` notebook/runtime execution behavior are added here.

## M11e follow-up

M11e keeps the M10d bounded-card policy but hardens how cards are authored:

- presenter and Oblivion cards now derive body geometry from shared card-layout helpers
- body text clipping is centralized and uses explicit inner/body widths
- hosted-card wrappers no longer paint accidental full-width dark fills behind smaller hosted content

The containment policy remains sample-local, persistence remains the M11d JSON/TOML model, and no new runtime/editor capability is introduced.
