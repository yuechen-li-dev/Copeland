# Machina Renderer Raster Dominatus Adapter (M0d)

`Machina.Renderer.Raster.Dominatus` wires Dominatus render commands into the CPU rasterizer.

## Supported commands

- `BeginFrameCommand`
- `FillRectCommand`
- `DrawTextCommand` (when a text rasterizer is registered)
- `PushClipCommand`
- `PopClipCommand`
- `EndFrameCommand`

Behavior:

- `BeginFrameCommand` creates a transparent `RasterSurface` and resets clip state.
- `FillRectCommand` converts `ColorToken` (`0xRRGGBBAA`) and fills using the current effective clip.
- `DrawTextCommand` draws with registered `ITextRasterizer` and the current effective clip.
- `PushClipCommand` intersects the incoming clip with the current clip and pushes stack depth.
- `PopClipCommand` restores the previous clip.
- `EndFrameCommand` stores a completed `RasterFrame` and requires balanced clip push/pop.
- Completed frames export PPM bytes via `RasterFrame.ToPpm()`.

## Explicit unsupported behavior

- `DrawTextCommand` without a registered text rasterizer -> `NotSupportedException`
- non-rectangular clipping (rounded/path)
- transforms
- scroll/overflow semantics from Core

## Boundary

- `Machina.Renderer.Raster` remains a pure CPU raster package.
- Dominatus dependency is isolated in `Machina.Renderer.Raster.Dominatus`.
- No presenter, windowing, input, or hit testing in M0d.


## M0e artifact harness

- Raster Dominatus integration is now exercised by golden artifact tests that render real `UiNode` samples through lowering, layout resolve, bridge command generation, and raster actuation.
- Optional `.ppm` artifact emission is opt-in via `MACHINA_WRITE_RENDER_ARTIFACTS=1`.
