# Readable Deterministic Bitmap Font (M1a)

M1a introduces a built-in readable bitmap font for raster text rendering.

## What changed

- `ReadableBitmapTextRasterizer` now provides the default deterministic text rendering path.
- Glyphs are defined in-process as a 5x7 bitmap table.
- Horizontal advance is `(5 + 1) * scale`.
- Lowercase input is mapped to uppercase glyphs for M1a.

## Supported glyphs

The M1a table includes:

- Uppercase `A-Z`
- Digits `0-9`
- Space and punctuation used by current samples/components:
  - `: . , - _ + / ! ? ( ) [ ] ' " #`

Unknown characters resolve to the fallback glyph (`?`) deterministically.

## Size mapping

- `Sm`: scale 1 (5x7 visible glyph)
- `Md`: scale 2 (10x14 visible glyph)
- `H1`: scale 3 (15x21 visible glyph)

## Still intentionally limited

M1a is intentionally not real typography.

Not included:

- external font loading
- platform font APIs
- Unicode shaping/kerning/ligatures
- wrapping/alignment/baseline layout
- glyph atlas caching

`ITextRasterizer` remains the seam for future richer text backends.
