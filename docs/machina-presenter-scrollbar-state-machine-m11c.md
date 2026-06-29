# Machina Presenter Scrollbar State Machine M11c

## Purpose

M11c refactors presenter scrollbar interaction and scroll rendering so drag/wheel updates are explicit state-machine transitions and cheap composition work instead of repeated full shell/page rerenders.

This milestone is interaction architecture and compositing-cache work only.

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` notebook/runtime execution
- no new Oblivion runtime behavior
- no resumed font work

## Problem with the M10d implementation

M10d made scrollbar behavior functionally correct, but the drag path was still architecturally naive:

- scrollbar drag state was a nullable ad hoc record threaded through the router
- pointer move during drag re-rendered page content
- pointer move during drag re-rendered shell chrome
- every drag step then recomposed and blitted again

That made scrolling pay layout/raster costs that should have been avoided.

## Dominatus orchestration ladder applied to Machina UI

Relevant Dominatus reading:

- `reference/dominatus/docs/user/ORCHESTRATION_LADDER.md`
- `reference/dominatus/docs/user/AUTHORING_GUIDE.md`
- `docs/machina-dominatus-runtime.md`
- `docs/dominatus-integration.md`

Applied interpretation:

- input translation belongs to the sample backend adapter: Avalonia event args stop at `AvaloniaPresenterInputBackend`.
- interaction state belongs to an explicit backend-neutral presenter state machine: `Idle` and `ThumbDragging`.
- action dispatch belongs to the existing deterministic presenter reducer path: router -> `PresenterNavigationActions` -> `PresenterNavigationDispatch`.
- rendering/composition belongs to a cached render session: page layer, shell layer, then composition layer.
- direct Dominatus HFSM was not used for this sample interaction because the problem is one focused local UI interaction, not a long-lived tick/world/mailbox workflow.
- a local Dominatus-style state machine was used instead: explicit states, explicit transitions, explicit pointer-capture side effects, and deterministic action emission.
- `Machina.Dominatus` remains the bridge boundary for render-command/runtime experiments; M11c keeps the presenter interaction model sample-local and backend-neutral instead of coupling UI drag behavior to a full Dominatus world.

Why this avoids React-style hidden lifecycle state:

- state is data, not mount/unmount side effect
- drag ownership is explicit and inspectable
- pointer capture/release is requested as transition output
- scroll composition does not depend on hidden host lifecycle callbacks

## Interaction state machine

M11c replaces nullable drag routing with explicit interaction states:

```text
Idle
  -> ThumbDragging
      -> Idle
```

Transitions:

- `Idle + PointerPressed on thumb -> ThumbDragging`
- `ThumbDragging + PointerMoved -> ThumbDragging + SetScrollOffset`
- `ThumbDragging + PointerReleased -> Idle + release pointer capture`
- `Idle + PointerPressed on track -> page up/down action`
- `Idle + Wheel over viewport -> scroll action`

While `ThumbDragging` is active, unrelated sidebar/tab routes are suppressed structurally instead of by scattered null checks.

## Avalonia as input backend

Avalonia remains only the current sample input backend.

- Avalonia types stay in `samples/Machina.Presenter.Sample/AvaloniaPresenterInputBackend.cs`
- presenter interaction state, routing, hit testing, actions, and dispatch stay Avalonia-free
- no new Avalonia references were added to production Machina packages

## Pointer capture side effects

Pointer capture is now explicit transition output:

- drag start requests capture
- drag end requests release
- the Avalonia window performs the host-side capture/release effect

The state machine owns the request; the host backend owns the platform call.

## Cached render/composition layers

M11c introduces a persistent presenter render session with layered caching:

```text
page layer
  rerender when page id, proof options, demo state, or viewport width changes

shell layer
  rerender when section/tab/page/chrome selection or shell geometry changes

composition layer
  rerun on every scroll offset change
```

Effect:

- scroll offset changes do not rerender page content
- scroll offset changes do not rerender shell chrome
- scroll offset changes still produce a new composed frame and updated scrollbar thumb

## ComposeFrame optimization

`PresenterNavigationFrameComposer` now:

- computes one clamped blit rectangle up front
- removes repeated inner-loop bounds checks
- preserves the previous transparent-pixel behavior
- draws the scrollbar thumb during composition instead of shell rerender

## Tests

M11c adds coverage for:

- explicit scrollbar interaction states and transitions
- pointer capture/release requests
- suppression of unrelated routes while dragging
- Avalonia boundary checks
- cached page/shell/composition counters
- clamped blit geometry and composition equivalence
- regression preservation for wheel/track/thumb/per-page-scroll behavior

`[Fact]` / `[Theory]` execution as notebook/runtime behavior remains deferred to M12+.

## What changed

- scrollbar drag now uses explicit state records instead of nullable drag routing
- pointer capture/release requests are explicit transition outputs
- presenter rendering uses a persistent cached render session
- shell composition draws the dynamic scrollbar thumb without rerendering the shell
- manifest output records interaction/caching diagnostics

## What did not change

- Avalonia is still sample-only
- presenter dispatch remains deterministic and backend-neutral
- no production renderer/core/layout semantics changed
- no Roslyn/card execution was added
- no font pipeline behavior changed

## Deferred work

- momentum/inertia scrolling
- generalized focus/accessibility system
- broader direct Dominatus HFSM adoption for presenter UI
- notebook/runtime execution work
- any `[Fact]` / `[Theory]` execution model beyond ordinary test-suite validation

## M11d dependency note

M11d uses this shell interaction/composition baseline as the host for persisted Oblivion workspaces:

- the shell page IDs stay stable
- workspace loading changes page content, not scrollbar/layout behavior
- load failures are bounded into error cards instead of changing shell/runtime behavior
- no Roslyn execution, xUnit notebook/runtime execution, or Visionary editor work is introduced
