# Machina CPU MSDF Spacing and Kerning M8n

## Purpose

M8n audits and fixes the proof-path CPU MSDF text placement contract before any `TextBlock`, Standard, or renderer integration work begins.

The milestone stays inside standalone `Machina.Fonts` and the gallery's opt-in proof mode. It does not migrate production UI text and does not adopt shaping, ligatures, bidi, or native dependencies.

## Visual defect observed

M8m proved that the managed Typography + MSDF + atlas + CPU sampling path could render upright, non-blank text, but the gallery proof still looked too tight and not production-ready.

The suspected causes were:

- missing kerning/pair adjustment,
- incorrect bearing or advance conventions,
- incorrect field-canvas compensation,
- or a limitation of the monospaced `SpaceMono-Regular.ttf` fixture.

## Current placement audit

### Metrics and placement

- `GlyphMetrics.Advance` is the horizontal pen advance from Typography/OpenFont, normalized to the requested proof `EmSize`.
- `GlyphMetrics.BearingX` is the left side bearing and matches `glyph.MinX` for the current Typography fixture glyphs that were audited.
- `GlyphMetrics.BearingY` is the glyph top above the baseline and is currently derived from `glyph.MaxY`.
- `GlyphMetrics.Width` and `GlyphMetrics.Height` are the glyph outline bounds size, not the fixed field-canvas size.
- these values are in the current proof layout space because `TypographyGlyphOutlineSource` requests `normalizeToEm: true`.
- CPU string layout uses `Advance` directly for pen movement.
- CPU rendering places the metric box at `penX + BearingX` and `baselineY - BearingY`.

For the current coordinate convention:

- `BaselineY - BearingY` is correct for top-to-bottom output storage with upright proof sampling via `FlipY = true`.
- `penX + BearingX` is correct for the current left side bearing convention.

### Field canvas / atlas placement

- the generated MSDF is a fixed-size canvas per glyph, currently `FieldWidth x FieldHeight`, not a tight glyph-bounds bitmap.
- `MsdfSharpDistanceFieldGenerator` scales the outline to the drawable area `(field - 2 * pixelRange)` and centers it in the field canvas.
- `GeneratedFieldAtlasPacker` stores atlas rects for the full field canvas, not the glyph bounds.
- `CpuDistanceFieldGlyphRenderer` samples the same full rect convention that packing exports.

The old compensation bug was that the CPU renderer assumed centered padding could be inferred from:

```text
(fieldSize - metricsSize) / 2
```

That was incomplete because the generator first scales the glyph to the drawable area. The actual left/top padding depends on:

- field size,
- pixel range,
- glyph metric bounds,
- and the fit scale used by the generator.

M8n fixes this by recomputing the same fit-to-drawable-area convention in the CPU renderer before placing the field box.

### Current issue classification

- `A. kerning/pair adjustment missing`: true for real proportional fonts and fixed in the proof path when available.
- `B. bearing/advance convention bug`: not the primary defect; the current advance and bearing conventions are consistent with the audited Typography metrics.
- `C. field-canvas compensation bug`: true and fixed locally in the CPU proof renderer.
- `D. fixture font limitation`: true for `SpaceMono-Regular.ttf`, which is monospaced and has no useful proof pairs for `AV/To/Ta/Wa/Yo`.
- `E. proof-only smoothing/scale artifact`: still a secondary proof-path limitation, but not the main spacing defect.
- `F. unknown`: no longer the leading classification after the audit.

## Typography kerning audit

Typography/OpenFont `1.0.0` exposes two useful low-level paths:

1. classic `kern` access through `Typeface.GetKernDistance(...)`
2. raw `GPOS` lookup execution through `Typeface.GPOSTable.LookupList` and `LookupTable.DoGlyphPosition(...)`

Audit findings:

- low-level classic `kern` values are exposed when the font supports them.
- low-level `GPOS` tables are also exposed.
- pair positioning is available without adopting a whole shaping engine, as long as Machina stays within simple adjacent-glyph horizontal pair adjustment.
- `SpaceMono-Regular.ttf` has no usable adjustment for the audited proof pairs.
- `CrimsonText-Regular.ttf` was added as a checked-in OFL fixture to prove real kerning.

M8n does not adopt a shaping engine. It uses only adjacent horizontal pair adjustment in the proof path.

## Fixture font decision

`SpaceMono-Regular.ttf` remains the deterministic monospaced fixture for the original M8g-M8m proof strings.

`CrimsonText-Regular.ttf` was added under:

```text
tests/Machina.Fonts.Tests/Fixtures/Fonts/
```

Why this fixture:

- SIL OFL 1.1 license,
- checked-in and deterministic,
- common Latin coverage,
- visible pair adjustments for `AV`, `To`, `Ta`, `Wa`, and `Yo`.

No OS fonts are required for tests or proofs.

## Pair adjustment model

M8n adds a Machina-owned optional seam:

```csharp
public interface IGlyphPairAdjustmentSource
{
    ValueTask<GlyphPairAdjustment?> GetPairAdjustmentAsync(
        GlyphKey left,
        GlyphKey right,
        CancellationToken cancellationToken = default);
}
```

Supporting records:

- `GlyphPairKey`
- `GlyphPairAdjustment`

Policy:

- pair adjustment is optional,
- missing support keeps advance-only layout behavior,
- only horizontal adjacent-pair adjustment is used,
- no shaping, ligatures, reordering, or bidi behavior is introduced.

Whitespace policy:

- M8n does not kern across whitespace in the proof path.

## Text layout changes

`DistanceFieldTextLayout` now supports optional pair adjustments.

Current proof flow is:

```text
penX starts at X
for each glyph:
  if previous non-whitespace glyph exists and a pair adjustment exists:
    penX += pairAdjustment.AdvanceX

  place glyph metric box from penX and baseline
  penX += glyph.Advance
```

`DistanceFieldTextPipeline` and `FontProofExporter` now collect pair adjustments from an optional `IGlyphPairAdjustmentSource` before layout.

`TypographyGlyphOutlineSource` implements both:

- `IGlyphOutlineSource`
- `IGlyphPairAdjustmentSource`

## Field canvas / metric box convention

Current proof convention after M8n:

- metric box origin:
  - `x = penX + BearingX`
  - `y = baselineY - BearingY`
- field box size:
  - full packed atlas rect
- field box placement:
  - metric box origin minus recomputed centered fit padding

The important fix is that padding is now based on the same fit-to-drawable-area model that the generator uses, not on raw `fieldSize - metricsSize`.

This remains proof-only because the atlas contract still does not store an explicit glyph-origin or field-origin record. A future production contract should carry that directly instead of recomputing it.

## Proof artifacts

M8n local proof outputs under `artifacts/m8n` include:

- `msdf-machina.ppm`
- `msdf-hello-machina.ppm`
- `msdf-av-to-wa.ppm`
- `msdf-spacing-proof.ppm`
- `component-gallery-msdf-proof.png`

The proof export script also still writes the earlier M8l strings for continuity.

## Visual inspection findings

Current local inspection findings:

- `Machina` and `Hello Machina` are upright, non-blank, and no longer show the same exaggerated field-placement drift seen before the fix.
- whitespace still advances without rendering a quad.
- `CrimsonText-Regular.ttf` visibly changes pair spacing for `AV`, `To`, `Ta`, `Wa`, and `Yo`.
- the component gallery proof remains sample-only and now reflects the corrected proof-path placement contract.

Remaining limitations:

- `Space Mono` is still monospaced and therefore cannot prove real kerning by itself.
- proof images are still CPU reference outputs, not production renderer evidence.
- this milestone does not add shaping, fallback, multiline layout, or final shader tuning.

## Tests

M8n adds or updates tests for:

- `TypographyGlyphOutlineSource_AdvanceAndBearing_AreStableForFixtureGlyphs`
- `TypographyPairAdjustmentSource_ReturnsExpectedAdjustmentForKnownPair`
- `DistanceFieldTextLayout_UsesAdvanceForPenMovement`
- `DistanceFieldTextLayout_UsesBearingForDrawPlacement`
- `DistanceFieldTextLayout_WhitespaceAdvancesWithoutQuad`
- `DistanceFieldTextLayout_AppliesPairAdjustment`
- `DistanceFieldTextLayout_NoAdjustmentWhenSourceMissing`
- `DistanceFieldTextPipeline_KerningChangesTextWidthForKnownPair`
- `FontProofExporter_WritesKerningProofArtifacts`
- `FontProofExporter_KerningProofIsDeterministic`
- `FontProofExporter_AVPairDiffersWithKerningIfFixtureSupportsIt`

## Deferred issues

- no `TextBlock` integration
- no `Machina.Standard.Text` integration
- no control-label migration
- no production renderer integration
- no Vulkan or Aurelian work
- no shaping, ligatures, bidi, grapheme clustering, or fallback glyph chain
- no pixel-diff visual testing
- no explicit stored atlas field-origin metadata yet

## M8o plan

Likely M8o follow-up work:

- formalize stored field-origin metadata instead of recomputing fit padding,
- decide whether the eventual runtime atlas contract should preserve or normalize the current `FlipY` proof convention,
- start UI-facing integration only after the glyph-origin, pair-adjustment, and fallback contracts are explicit.

## M8p update

M8p replaces the recomputed fit-padding workaround with an explicit stored field-placement contract.

- `MsdfSharpDistanceFieldGenerator` now exports generator-derived plane bounds directly
- `GlyphAtlasEntry` now carries `GlyphFieldPlacement`
- `.font-atlas.toml` persists placement metadata for import/export roundtrip
- `CpuDistanceFieldTextRenderer` now uses stored plane bounds and no longer treats a fixed field tile as the draw contract

Kerning behavior from M8n remains intact and still applies before placement.

## M8q update

M8q re-checks the remaining vertical question without changing M8n kerning behavior.

- browser `TextMetrics` are now captured for the same proof strings
- the proof report now records browser actual/font bounds alongside Machina plane/ink bounds
- new tests prove the renderer uses baseline-relative plane bounds and does not double-apply `BearingY`

Current result:

- horizontal/kerning behavior from M8n and M8p stays intact
- the remaining discrepancy is not another spacing guess target
- no vertical magic constant was added

## M8r follow-up

M8r keeps the M8n kerning work intact and uses the `AV To Ta Wa Yo` fixture as a dedicated overlay-diff diagnostic.

- the M8r kerning overlays show that pair adjustment is no longer the dominant missing feature
- the remaining mismatch is still wider/lower than the browser reference even with kerning data present
- the next fix should not be another speculative kerning pass
