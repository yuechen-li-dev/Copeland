# Machina Presenter M0c

## What M0c adds

M0c closes the first visual UI loop in the sample using sample-local state:

- local `_count` state in the presenter window
- UI declaration rebuilt from current `count`
- click hit-test/action resolution from M0b
- `increment` action mutates `_count`
- full lowering/layout/render/raster redraw after mutation
- Avalonia bitmap/image updated from the new frame
- hit-test index rebuilt after every redraw
- title/status updated with action and count

This milestone proves:

```text
input click
  -> hit test
  -> action
  -> state change
  -> redeclare UI
  -> rerender
  -> update window
```

## Current behavior

On startup the sample renders:

- `Count: 0`
- `Increment` button

On click:

- Clicking `Increment` resolves `UiAction.Named("increment")`, increments count, redraws, and updates title/status (for example `Machina Presenter M0c - action: increment, count: 1`).
- Clicking non-action regions keeps count unchanged and updates title/status with `action: <none>`.
- Unknown action names are displayed/logged without mutating count.

## Coordinate assumption

M0c keeps the M0b **1:1 raster-to-window mapping** assumption:

- image uses `Stretch.None`
- window/image dimensions match the raster frame
- pointer coordinates are used directly as Machina root-local coordinates

## Scope boundaries kept

M0c intentionally does **not** add:

- Dominatus runtime mailbox/event ingress
- Dominatus blackboard/state loop integration
- new Machina.Runtime hit-test behavior
- presenter abstractions outside sample

Count remains sample-local imperative C# state for this milestone.

## Run

```bash
dotnet run --project samples/Machina.Presenter.Sample
```

In headless environments, rely on build/test validation and treat interactive click validation as manual.


## M0d follow-up: Dominatus-backed counter runtime

The sample now routes button actions through `CounterUiRuntime` in `Machina.Dominatus.Runtime` instead of mutating presenter-local state.
Count state is stored in a Dominatus blackboard key (`counter.count`), and presenter redraw is driven by runtime-generated UI declarations.

This keeps presenter responsibilities narrow (pointer capture, hit-test, action forwarding, redraw) while state/action handling sits in the Dominatus runtime loop.
