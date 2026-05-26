# Raster Text Renderer Contract (M0d)

M1a keeps the pluggable text seam for the raster backend and upgrades default text to a readable deterministic bitmap font.

## Packages

- `Machina.Renderer.Raster` remains pure pixel math and does not depend on Dominatus.
- `Machina.Renderer.Raster.Text` adds text rasterization abstractions plus deterministic bitmap text implementations.
- `Machina.Renderer.Raster.Dominatus` consumes a registered text rasterizer via render options.

## `ITextRasterizer`

`ITextRasterizer` draws text pixels into an existing `RasterSurface`:

- deterministic and synchronous
- no-op for empty text
- accepts optional rectangular clip (`Rect? clip = null`)
- surface bounds clipping always still applies
- receives resolved `Rgba32` color (style color or default white)

## `ReadableBitmapTextRasterizer`

M1a readable glyph behavior remains deterministic:

- 5x7 canonical glyph bitmap
- 1px base glyph gap
- scales by `TextSize` (`Sm=1`, `Md=2`, `H1=3`)
- lowercase maps to uppercase
- unknown characters render deterministic fallback (`?`)
- optional clip is applied per pixel-block fill
- no wrapping, shaping, kerning, alignment, or font loading

`DebugBitmapTextRasterizer` remains as a compatibility wrapper over the readable rasterizer.

## Dominatus behavior

`DrawTextCommand` behavior in raster Dominatus adapter:

- if no text rasterizer is registered: throw `NotSupportedException`
- if a text rasterizer is registered: draw text into active frame
- current active clip (from `PushClip` / `PopClip`) is applied to text pixels
