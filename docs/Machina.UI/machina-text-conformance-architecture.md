# Machina text conformance architecture

This document is the current authority for the bounded Latin text path established by
`MACHINA-OUTLINE-CONFORMANCE-M1` and downstream
`MACHINA-MSDF-REALIZATION-M1`. The M0 and M8/M9 documents remain historical evidence.

## Ownership

```text
exact font bytes + text + size
              |
              v
Avalonia TextLayout -> ShapedTextRun -> GlyphRun/GlyphInfo
              |                         |
              |                         v
              |                 exact-byte SKFont.GetGlyphPath
              |                         |
              v                         v
       reference placement + positioned vector outline

Machina.Typography.OpenFont exact-byte face -> hmtx + GPOS -> MachinaGlyphRun
              |                              |
              v                              v
       Typography outline --------> positioned vector outline
                              |
                              v
              MachinaOutlineComparisonSpace
```

Avalonia is the external layout and shaping oracle and Skia is the reference outline
source. They own no Machina
application state and is absent from `Machina.Fonts.csproj`. The adapter uses real
Skia/HarfBuzz text formatting under Avalonia's headless host; the headless drawing
stub is forbidden because it substitutes fake font metrics.

`MachinaGlyphRun` owns semantic placement. Lines contain source span, baseline,
advance, line height, and ink bounds. Tokens contain deterministic source spans,
the first visible glyph anchor, advance width, and ink bounds. Glyphs contain source
span, origin, baseline, advance, renderer-neutral plane bounds, and token identity.
Atlas pages, UVs, padding, texture handles, masks, and pixels are deliberately absent
from the M1 comparison.

## Font and coordinate law

The M0 oracle embeds the qualified `CrimsonText-Regular.ttf` and rejects a request
whose SHA-256 does not match those bytes. Avalonia and Typography therefore observe
the same face. Machina consumes its pinned, source-built
`Machina.Typography.OpenFont` fork; its original upstream license and downstream
patch ledger are shipped with the package. Font metadata records units per em,
ascender, descender, and line gap.

Machina and Avalonia evidence use device-independent pixels at 96 DPI and a
pixels-per-DIP value of one. X increases
rightward and Y downward. A glyph origin is `(originX, baselineY)` plus its shaped
offset. Plane top/bottom are baseline-relative. Subpixel origins, advances, bounds,
and baselines remain `double`. M1 does not rasterize. Skia's sized glyph path is
already Y-down; Typography's Y-up font outline is normalized by
`comparisonY = baselineY - fontY`. Both use `fontSize / unitsPerEm`.

## Token and shaping law

The comparison tokenizer groups consecutive letters/digits/underscore as a word,
consecutive punctuation as punctuation, and consecutive whitespace as a separator.
It is a comparison boundary, not language semantics. A non-whitespace token's first
shaped glyph is its absolute anchor. Whitespace has no visual anchor. M1 compares
natural cumulative positions. Token-relative values and the first-glyph oracle remain
diagnostics only; they no longer reset the pen in the primary proof.

`DistanceFieldTextLayout` still accepts an explicit token-anchor map for historical
diagnostics. Applying an anchor
resets the pen and pair-adjustment predecessor before laying out that token. This is
the anti-cascade seam used by conformance and by any future qualified Machina-owned
anchor provider. Production code does not call Avalonia. M0 does not yet nominate a
production anchor provider. The M1 runner passes no map.

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

The production field generator consumes `GlyphOutline` line, quadratic, and cubic
segments directly. It removes only edges whose complete control polygon has exactly
zero extent. `MSDF-Sharp.Core` receives a vector-space projection: translation is
applied before scale, so Machina converts the desired pixel centering offset back to
outline units. Field plane bounds are the inverse projection of the complete storage
rectangle. They include range border, remain baseline-relative, and are distinct from
the packed atlas rectangle.

MSDF reconstruction samples texel centers, takes median RGB, and maps the signed
distance boundary at `0.5` to alpha coverage. Direct/MSDF qualification compares
that alpha at the same `0.5` boundary. RGB color against a transparent background is
not a geometry mask because it classifies every nonzero smoothing sample as full ink.

For the bounded smooth-contour failure where MSDF-Sharp produces a non-finite channel,
Machina regenerates a vector SDF from the same normalized shape and replicates the
distance into RGB. This is a direct-vector monochrome MSDF representation, not a
raster fallback and not a glyph-specific exception.

## Measurement and production handoff

The existing `ITextMeasurer` is not yet migrated. M0 records its boundary rather than
claiming parity. A future production realizer should accept `MachinaGlyphRun` and
submit DirectOutline pixels or MSDF quads. Aurelian's native quad renderer is a valid
leaf consumer, but it must not recompute advances, baselines, or atlas-derived layout.

The canonical outline command is:

```powershell
dotnet run --project tools/Machina.OutlineConformance/Machina.OutlineConformance.csproj
```

It emits five compact JSON files and keeps vector-only SVG diagnostics in the local
temporary directory reported by `proof.json`. The canonical MSDF command is:

```powershell
dotnet run --project tools/Machina.TextConformance/Machina.TextConformance.csproj -- --output artifacts/machina-msdf-realization-m1
```

It emits five compact JSON evidence files and keeps raster diagnostics under the local temporary
directory recorded in `manifest.json`.
