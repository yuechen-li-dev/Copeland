# Raster Text Renderer Contract (M0c)

M0c adds a pluggable text seam for the raster backend.

## Packages

- `Machina.Renderer.Raster` remains pure pixel math and does not depend on Dominatus.
- `Machina.Renderer.Raster.Text` adds text rasterization abstractions and debug text implementation.
- `Machina.Renderer.Raster.Dominatus` consumes a registered text rasterizer via render options.

## `ITextRasterizer`

`ITextRasterizer` draws text pixels into an existing `RasterSurface`:

- deterministic and synchronous
- no-op for empty text
- clips naturally to surface bounds through raster fill behavior
- receives resolved `Rgba32` color (style color or default white)

## `DebugBitmapTextRasterizer`

M0c uses deterministic debug glyph cells instead of real fonts:

- `Sm`: 5x8
- `Md`: 6x10
- `H1`: 10x16
- 1px gap between glyphs
- whitespace advances without drawing
- no wrapping, shaping, kerning, alignment, or font loading

This is intentionally not real typography.

## Dominatus behavior

`DrawTextCommand` behavior in raster Dominatus adapter:

- if no text rasterizer is registered: throw `NotSupportedException`
- if a text rasterizer is registered: draw text into active frame
- `PushClipCommand` and `PopClipCommand` remain unsupported in M0c
