# Machina Typography Outline Adapter M8g

## Purpose

M8g lands the first real font-outline extraction proof behind the Machina-owned `IGlyphOutlineSource` seam.

This milestone proves:

- explicit font file loading
- codepoint-to-glyph lookup
- glyph metrics extraction
- contour extraction into Machina-owned records
- deterministic pipeline handoff into the existing fake distance-field generator

This milestone does **not** add real MSDF generation, atlas integration, artifact export integration, renderer integration, TextBlock/gallery changes, shaping, bidi, ligatures, or native dependencies.

## Dependency choice

Chosen package:

- `WycliffeAssociates.Typography.OpenFont` `1.0.0`

Target framework context:

- `Machina.Fonts` remains `net10.0`
- the chosen package ships `lib/netstandard2.0/Typography.OpenFont.dll`

Package evidence gathered during M8g:

- official NuGet registration metadata shows `WycliffeAssociates.Typography.OpenFont` `1.0.0` published on 2020-07-23
- the `.nupkg` contains `lib/netstandard2.0/Typography.OpenFont.dll`
- the `.nupkg` nuspec declares `<license type="file">LICENSE.md</license>`
- the `.nupkg` contains `LICENSE.md`
- the nuspec repository URL points to `https://github.com/WycliffeAssociates/Typography`

License evidence:

- package-embedded `LICENSE.md` states the whole project license is MIT
- the same file also lists permissive upstream sources and instructs consumers to preserve per-file provenance where applicable

Why this package is acceptable for a proof:

- it is the cleanest currently consumable package that exposes the needed `Typography.OpenFont` API surface on a modern compatible TFM
- it avoids native dependencies
- it keeps the dependency fully inside `Machina.Fonts`
- it preserves the M8f adapter boundary so this package can be swapped later if packaging or maintenance quality becomes unacceptable

Why other visible packages were not chosen:

- `QuickLook.Typography.OpenFont` has a clearer MIT package expression and more recent publish date, but its package ships only `net462`, which is not a clean dependency shape for this `net10.0` proof
- `Typography.OpenFont.NetCore` has weaker metadata and packaging quality than the chosen package

## Fixture font and license

Checked-in fixture:

- `tests/Machina.Fonts.Tests/Fixtures/Fonts/SpaceMono-Regular.ttf`

License and attribution:

- `tests/Machina.Fonts.Tests/Fixtures/Fonts/SpaceMono-Regular.LICENSE.txt`
- `tests/Machina.Fonts.Tests/Fixtures/Fonts/README.md`

Fixture source:

- `googlefonts/spacemono`

Fixture license:

- SIL Open Font License 1.1

Why this fixture is acceptable:

- deterministic checked-in binary
- permissive redistribution license
- no OS font dependency
- stable Latin glyph set that covers the proof codepoints, including whitespace and `U+2022`

## Adapter shape

New public adapter types live under `src/Machina.Fonts/Generation/Typography/`:

- `TypographyFontFaceSource`
- `TypographyGlyphOutlineSource`

Internal helpers:

- `TypographyFontFaceCache`
- `TypographyOutlineConversion`

`TypographyGlyphOutlineSource` accepts an explicit `FontFaceId -> TypographyFontFaceSource` map. There is no OS font discovery, no global font registry, and no `Typography` type leaks across the public Machina seam.

## Font loading

Current loading policy:

- load from explicit file path only
- cache one parsed `Typeface` per configured `FontFaceId`
- support face index `0` in this proof
- reject missing files or invalid face configuration with `OutlineLoadFailed` or `InvalidGenerationSettings` diagnostics

The underlying `Typography.OpenFont` APIs are synchronous. The adapter returns `ValueTask` but performs synchronous parsing and extraction, which is appropriate for later use inside an async worker pipeline.

## Codepoint to glyph lookup

For each request:

1. validate the Unicode scalar value
2. build a Machina `GlyphKey`
3. resolve the configured cached `Typeface`
4. call `Typeface.GetGlyphIndex(codepoint)`
5. call `Typeface.GetGlyph(glyphIndex)` when present

Current missing-glyph policy:

- glyph index `0` is treated as missing for this proof
- the adapter returns `Success = false`
- diagnostics include `MissingGlyph`

## Metrics scaling

Scaling rule:

```text
scale = NormalizeToEm ? EmSize / UnitsPerEm : 1
```

Current metric mapping:

- advance: `Typeface.GetAdvanceWidthFromGlyphIndex(...) * scale`
- bearing X: `Typeface.GetLeftSideBearing(...) * scale`
- bearing Y: `glyph.MaxY * scale`
- width: `(glyph.MaxX - glyph.MinX) * scale`
- height: `(glyph.MaxY - glyph.MinY) * scale`

Bounds use the same scale and are emitted as Machina `GlyphBounds`.

This means:

- `NormalizeToEm = true` returns metrics and outline points in requested em-space
- `NormalizeToEm = false` returns raw font-unit coordinates and metrics

## Outline conversion

The adapter uses `Typography.OpenFont.IGlyphTranslator` plus `IGlyphReaderExtensions.Read(...)` for TrueType outlines.

Mapping:

- `MoveTo` starts a new contour
- `LineTo` becomes `GlyphLineSegment`
- `Curve3` becomes `GlyphQuadraticSegment`
- `Curve4` becomes `GlyphCubicSegment`
- `CloseContour` finalizes the contour and adds an explicit closing line when the contour end point does not already equal the start point

Conversion rules:

- Typography types do not escape the adapter
- empty contours are dropped
- all output points are validated through existing Machina record constructors

Current proof coverage is TrueType/quadratic oriented. If a resolved glyph reports `IsCffGlyph`, the adapter returns `UnsupportedGlyph` and defers that path to later work.

## Whitespace and missing glyph policy

Whitespace policy:

- whitespace glyphs succeed
- metrics are returned
- zero-contour outlines are allowed
- no error diagnostic is emitted for empty whitespace outlines

Missing glyph policy:

- missing glyphs return `Success = false`
- diagnostics include `MissingGlyph`
- no fallback OS font lookup is attempted

Non-whitespace empty-outline policy:

- if a glyph resolves but yields no contours, the adapter returns `EmptyOutline`

## Tests

Focused tests now cover:

- fixture existence and local license files
- fixture loading through the real adapter
- deterministic metrics for `A`
- whitespace success with empty contours
- contour extraction for `A`
- lowercase and digit extraction
- quadratic-segment preservation
- missing glyph diagnostics
- cancellation propagation
- deterministic repeated loads
- pipeline integration with `FakeGlyphDistanceFieldGenerator`

All of these tests live under `tests/Machina.Fonts.Tests/Generation/Typography/`.

## Deferred issues

- no `MSDF-Sharp.Core` yet
- no real distance-field generation yet
- no atlas integration yet
- no renderer/TextBlock/gallery integration yet
- no OS font lookup
- no TTC/TTC-face-index proof beyond face index `0`
- no CFF/cubic proof path yet
- no shaping, ligatures, bidi, or grapheme-cluster work
- no native dependency fallback path

## M8h plan

M8h can now focus on the next seam only:

1. keep `TypographyGlyphOutlineSource` as the real outline provider
2. add an `IGlyphDistanceFieldGenerator` implementation backed by `MSDF-Sharp.Core`
3. convert Machina-owned contours into MSDF shape input
4. validate deterministic generated field output for the same checked-in fixture font

The renderer, atlas consumption, and visual integration steps remain later milestones.
