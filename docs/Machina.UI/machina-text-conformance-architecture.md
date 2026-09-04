# Machina text conformance architecture

This document is the current authority for the bounded Latin text path established by
`MACHINA-TEXT-CONFORMANCE-M0`. The M8/M9 documents remain historical evidence.

## Ownership

```text
exact font bytes + text + size
              |
              v
Avalonia reference adapter (test/tooling only)
              |
              v
token anchors and shaped geometry evidence
              |
              v
MachinaGlyphRun (renderer neutral)
       |                    |
       v                    v
DirectOutline          MSDF/atlas
```

Avalonia is the external layout, shaping, and raster oracle. It owns no Machina
application state and is absent from `Machina.Fonts.csproj`. The adapter uses real
Skia/HarfBuzz text formatting under Avalonia's headless host; the headless drawing
stub is forbidden because it substitutes fake font metrics.

`MachinaGlyphRun` owns semantic placement. Lines contain source span, baseline,
advance, line height, and ink bounds. Tokens contain deterministic source spans,
the first visible glyph anchor, advance width, and ink bounds. Glyphs contain source
span, origin, baseline, advance, renderer-neutral plane bounds, and token identity.
Atlas pages, UVs, padding, and texture handles are deliberately absent.

## Font and coordinate law

The M0 oracle embeds the qualified `CrimsonText-Regular.ttf` and rejects a request
whose SHA-256 does not match those bytes. Avalonia and Typography therefore observe
the same face. Font metadata records units per em, ascender, descender, and line gap.

Machina and Avalonia evidence use device-independent pixels at 96 DPI. X increases
rightward and Y downward. A glyph origin is `(originX, baselineY)` plus its shaped
offset. Plane top/bottom are baseline-relative. Subpixel origins, advances, bounds,
and baselines remain `double`; rasterization quantizes only at sampling time.

## Token and shaping law

The comparison tokenizer groups consecutive letters/digits/underscore as a word,
consecutive punctuation as punctuation, and consecutive whitespace as a separator.
It is a comparison boundary, not language semantics. A non-whitespace token's first
shaped glyph is its absolute anchor. Whitespace has no visual anchor. Later glyphs
are compared relative to that anchor, so preceding-token error cannot contaminate
the measurement.

`DistanceFieldTextLayout` accepts an explicit token-anchor map. Applying an anchor
resets the pen and pair-adjustment predecessor before laying out that token. This is
the anti-cascade seam used by conformance and by any future qualified Machina-owned
anchor provider. Production code does not call Avalonia. M0 does not yet nominate a
production anchor provider.

Standard ligatures (`liga` and `clig`) are disabled in the Avalonia reference for M0
because the current Typography path is codepoint-based. Kerning remains enabled in
both paths. Arabic, Indic, CJK shaping, bidi, emoji, fallback, automatic wrapping,
and editing are explicitly deferred.

## Realization law

DirectOutline and MSDF consume the same `DistanceFieldTextLayoutResult.GlyphRun`.
DirectOutline uses font outlines as Machina's internal raster truth. MSDF draw bounds
come from `GlyphFieldPlacement` plane bounds translated by shared glyph origins.
Atlas rect dimensions and padding are storage facts only; they never advance the pen
or change a token anchor.

The M9f laws remain mandatory: scalable field dimensions grow with output/em size,
UV reconstruction samples texel centers, padding is storage-only, and plane bounds
determine draw rectangles. No threshold or visual offset may conceal layout error.

## Measurement and production handoff

The existing `ITextMeasurer` is not yet migrated. M0 records its boundary rather than
claiming parity. A future production realizer should accept `MachinaGlyphRun` and
submit DirectOutline pixels or MSDF quads. Aurelian's native quad renderer is a valid
leaf consumer, but it must not recompute advances, baselines, or atlas-derived layout.

The canonical command is:

```powershell
dotnet run --project tools/Machina.TextConformance/Machina.TextConformance.csproj -- --output artifacts/machina-text-conformance-m0
```

It emits compact JSON evidence and keeps raster diagnostics under the local temporary
directory recorded in `manifest.json`.
