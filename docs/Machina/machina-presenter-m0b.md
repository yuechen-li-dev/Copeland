# Machina Presenter M0b

## What M0b adds

M0b keeps the static presenter frame from M0a and adds click-to-action plumbing in the sample:

- Avalonia pointer input handling on the rendered image.
- Conversion from pointer coordinates to root-local `PointerPoint`.
- Runtime hit-test index construction via `UiHitTestIndex.Build(resolved, lowering.Actions)`.
- Action resolution display by updating the window title and writing to console.

This milestone proves:

```text
Avalonia pointer click
  -> root-local PointerPoint
  -> UiHitTestIndex
  -> UiAction
  -> visible/logged result
```

## Current behavior

The sample UI still renders a static frame:

- title text
- `Count: 0`
- `Increment` button with `UiAction.Named("increment")`

On click:

- Clicking `Increment` updates the title to `Machina Presenter M0b - action: increment` and logs the same action.
- Clicking non-action regions updates the title to `Machina Presenter M0b - action: <none>` and logs a miss.

## Coordinate assumption

M0b assumes **1:1 raster-to-window mapping**:

- The image is shown with `Stretch.None`.
- The window and image dimensions match the raster frame dimensions.
- Pointer coordinates from the image are used directly as Machina root-local coordinates.

Complex DPI/scale conversion is intentionally deferred.

## What M0b does not add

M0b intentionally does **not** add:

- count mutation
- redraw-on-click loop
- Dominatus mailbox/event ingress
- presenter/runtime event routing framework

## Run

```bash
dotnet run --project samples/Machina.Presenter.Sample
```

In headless environments, use build/test validation and treat interactive click validation as manual.
