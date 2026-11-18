# Downstream patches

## 1.0.1: warning-free build and exact binary reads

- Removed eleven never-assigned fields and the otherwise-unused internal
  `EbdtComponent` declaration from unimplemented legacy bitmap-format stubs.
  No supported bitmap decoder or public font API was removed; those formats
  remain unimplemented. No warnings are suppressed.
- Enabled warnings-as-errors for the vendored project.
- Replaced byte reversal and host-endian conversion in the internal big-endian
  reader with explicit unsigned assembly and checked-length reads. A stream can
  legitimately return short reads: the reader now continues until a complete
  field is available, or throws `EndOfStreamException`. Previously it could
  silently interpret stale buffer bytes as valid font data after truncation.
- Retained the single eight-byte reusable buffer and the existing unsupported
  reader methods. Removed the redundant disposal override and sealed the
  internal reader, which has no derived implementations.
- Package validation compares public API compatibility against published 1.0.0.
  Original namespaces, netstandard2.0 support, dependencies and licensing remain.

`BigEndianReaderTests` covers fragmented streams, truncated fields after a
previous read, mixed-width signed/unsigned values, IEEE double bits and cursor
positions. Existing glyph-identity tests remain in place.

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

- Removed an unused Debug-only WOFF2 guard while preserving the known-table tag
  check on construction.
- Centralized empty-glyph construction so TrueType and WOFF2 loaders use the same
  identity-preserving rule.
- Removed a write-only layout-mode field while retaining the clone behavior it
  described.
- Removed unused exception and composite-scan locals without changing stream reads,
  branching, or error behavior.
