# Downstream patches

## Preserve glyph identity for empty outlines

`Upstream/Tables.TrueType/Glyf.cs` now creates an empty `Glyph` carrying the
actual glyph index for every zero-length `glyf` entry. Upstream reused one empty
glyph whose index was zero. Callers that correctly queried metrics through
`glyph.GlyphIndex` therefore received the `.notdef` advance for spaces and other
empty-outline glyphs.

The motivating Crimson Text face has space glyph ID 556. Upstream returned a
`Glyph` whose `GlyphIndex` was 0, causing the unofficial package to report the
`.notdef` advance of 374 font units instead of the space advance of 229 font
units. Preserving identity removes cumulative word-origin drift while leaving
outline geometry and shaping behavior unchanged.

## Safe maintenance refactors

- Centralized empty-glyph construction so TrueType and WOFF2 loaders use the same
  identity-preserving rule.
- Removed a write-only layout-mode field while retaining the clone behavior it
  described.
- Removed unused exception and composite-scan locals without changing stream reads,
  branching, or error behavior.
