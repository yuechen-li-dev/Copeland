# Machina Presenter Sample M1d: Image-to-Root Coordinate Mapping

M1d adds an explicit, testable coordinate mapping step between presenter image-space pointer input and Machina root/raster coordinates.

## Why this exists

Before M1d, the presenter passed Avalonia image-local pointer coordinates directly into runtime hit testing. That works only when display coordinates are exactly 1:1 with the raster root coordinates.

M1d introduces a pure mapper so the conversion behavior is deterministic and independently testable.

## Coordinate spaces

- **Source/root space**: Machina raster coordinates (`RasterFrame.Width` x `RasterFrame.Height`).
- **Destination/presenter space**: the rectangle where image content is drawn in presenter coordinates.
- **Presented pointer point**: pointer coordinate in the same coordinate system as the destination rectangle.

## Mapper API

`Machina.Runtime.Input` now contains:

- `ImageStretchMode`:
  - `None`
  - `Fill`
  - `Uniform`
- `PresentedImageRect` (`X`, `Y`, `Width`, `Height`)
- `PresentedImageMapper.ToRootPoint(...)`

The mapper returns a `PointerPoint?`:

- `PointerPoint` when the presented coordinate maps to valid source content.
- `null` when the pointer is outside displayed source content.

## Stretch behaviors

### None

- No scaling.
- Content starts at destination `X`/`Y` and uses source size.
- Mapping: `rootX = pointX - destination.X`, `rootY = pointY - destination.Y`.
- Outside source half-open bounds => `null`.

### Fill

- Non-uniform scaling to fill destination width/height exactly.
- Mapping uses independent X/Y scale factors.
- Outside destination half-open bounds => `null`.

### Uniform

- Uniform scaling that preserves source aspect ratio.
- Content is centered inside destination (letterbox/pillarbox possible).
- Outside the centered content half-open bounds => `null`.

## Bounds policy

Half-open bounds are used consistently:

- `x >= left`
- `x < right`
- `y >= top`
- `y < bottom`

This removes right/bottom-edge ambiguity and keeps hit behavior deterministic.

## Validation policy

The mapper rejects invalid numeric inputs for source and destination dimensions/positions:

- source width/height must be finite and `> 0`
- destination width/height must be finite and `> 0`
- destination origin and pointer coordinates must be finite

Invalid inputs throw argument exceptions.

## Presenter sample behavior in M1d

The sample still uses explicit 1:1 presentation (`Stretch.None`) for minimal risk, but now pointer handling always passes through `PresentedImageMapper` with `ImageStretchMode.None`.

If mapping returns `null`, the sample treats the click as outside content and performs no state transition.

## Current non-goals

M1d intentionally does **not** add:

- scroll coordinate mapping
- focus/keyboard/input routing
- runtime hit-test algorithm changes
- renderer output changes
- generalized DPI abstraction
