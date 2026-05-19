# Raster Text Renderer Contract (M0d)

M0d keeps the pluggable text seam for the raster backend and adds clip propagation.

## Packages

- `Machina.Renderer.Raster` remains pure pixel math and does not depend on Dominatus.
- `Machina.Renderer.Raster.Text` adds text rasterization abstractions and debug text implementation.
- `Machina.Renderer.Raster.Dominatus` consumes a registered text rasterizer via render options.

## `ITextRasterizer`

`ITextRasterizer` draws text pixels into an existing `RasterSurface`:

- deterministic and synchronous
- no-op for empty text
- accepts optional rectangular clip (`Rect? clip = null`)
- surface bounds clipping always still applies
- receives resolved `Rgba32` color (style color or default white)

## `DebugBitmapTextRasterizer`

M0d debug glyph behavior remains deterministic:

- `Sm`: 5x8
- `Md`: 6x10
- `H1`: 10x16
- 1px gap between glyphs
- whitespace advances without drawing
- optional clip is applied per glyph fill
- no wrapping, shaping, kerning, alignment, or font loading

## Dominatus behavior

`DrawTextCommand` behavior in raster Dominatus adapter:

- if no text rasterizer is registered: throw `NotSupportedException`
- if a text rasterizer is registered: draw text into active frame
- current active clip (from `PushClip` / `PopClip`) is applied to text pixels
