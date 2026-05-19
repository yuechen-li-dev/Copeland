# Machina Renderer Raster Dominatus Adapter (M0b)

`Machina.Renderer.Raster.Dominatus` wires Dominatus render commands into the CPU rasterizer.

## Supported commands

- `BeginFrameCommand`
- `FillRectCommand`
- `EndFrameCommand`

Behavior:

- `BeginFrameCommand` creates a transparent `RasterSurface`.
- `FillRectCommand` converts `ColorToken` (`0xRRGGBBAA`) and calls `Rasterizer.FillRect`.
- `EndFrameCommand` stores a completed `RasterFrame`.
- Completed frames export PPM bytes via `RasterFrame.ToPpm()`.

## Unsupported commands (explicit)

- `DrawTextCommand` -> `NotSupportedException` with: `DrawTextCommand is not supported by Raster M0b. Text rendering is deferred to M0c.`
- `PushClipCommand` -> `NotSupportedException`
- `PopClipCommand` -> `NotSupportedException`

## Boundary

- `Machina.Renderer.Raster` remains a pure CPU raster package.
- Dominatus dependency is isolated in `Machina.Renderer.Raster.Dominatus`.
- No text rendering, presenter, windowing, input, or hit testing in M0b.


## M0c text seam update

M0c adds optional DrawText support through `Machina.Renderer.Raster.Text`.
`Machina.Renderer.Raster.Dominatus` accepts `RasterRenderOptions` with an optional `ITextRasterizer`.
When absent, DrawText remains explicitly unsupported.
PushClip/PopClip remain unsupported.
