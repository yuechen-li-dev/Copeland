# Machina CPU MSDF Text Proof Audit M8l

## Purpose

M8l turns the M8k CPU reference renderer into a repeatable local proof-audit workflow.

The goal is to generate deterministic `.ppm` text artifacts, inspect them visually, stabilize the current renderer conventions, and fix only small local convention bugs before any UI integration work begins.

This milestone stays fully inside standalone `Machina.Fonts`.

## Proof artifact workflow

Repo-root script:

```powershell
.\tools\Export-MachinaFontProofs.ps1 -OutputDir artifacts\m8l
```

Implementation shape:

```text
FontProofExporter
  -> real Typography outline extraction
  -> real MSDF-Sharp.Core generation
  -> one deterministic packed atlas
  -> .font-atlas.toml + .dfpage export/import
  -> CPU reference text rendering
  -> deterministic PPM proof images
```

The script is headless and wraps a focused `Machina.Fonts.Tests` proof-export path so the checked-in `SpaceMono-Regular.ttf` fixture remains the single real font dependency.

## Artifacts generated

M8l generates these proof images:

- `artifacts/m8l/msdf-machina.ppm`
- `artifacts/m8l/msdf-aa0.ppm`
- `artifacts/m8l/msdf-a-space-a.ppm`
- `artifacts/m8l/msdf-machina-0.ppm`
- `artifacts/m8l/msdf-hello-machina.ppm`

It also writes the supporting atlas artifacts for the shared proof atlas:

- `artifacts/m8l/space-mono-msdf-proofs.font-atlas.toml`
- `artifacts/m8l/space-mono-msdf-proofs.page0.dfpage`

These are local audit aids and are ignored by Git.

## Visual inspection findings

Inspection set:

- `Machina`
- `Aa0`
- `A A`
- `Machina 0`
- `Hello Machina`

Classification:

- `A. Fix now`: real Typography/MSDF proofs were vertically inverted until sampling used `FlipY = true`.
- `A. Fix now`: the initial fixed proof canvas clipped `Machina 0` and `Hello Machina`; the proof workflow now uses a wider deterministic output canvas.
- `C. No issue`: glyphs are now visible, upright, not mirrored, baseline-consistent enough for proof work, whitespace advances visibly, and output remains deterministic across runs.
- `B. Defer`: kerning, shaping, bidi, grapheme fallback, multiline layout, final shader tuning, MTSDF alpha use, and production atlas contracts remain deferred.

## Coordinate orientation

Current documented convention:

- `.dfpage` payloads are stored row-major and interpreted top-to-bottom in `DistanceFieldPageReference`.
- output images are also row-major and top-to-bottom.
- `FlipY` affects sampling inside a glyph UV rectangle only; it does not rewrite page data or output image orientation.
- the real Typography/MSDF proof path currently needs `FlipY = true` to render upright glyphs from the current managed atlas/generator convention.

That means the audit contract is:

- page storage stays top-to-bottom,
- output storage stays top-to-bottom,
- proof rendering opts into `FlipY = true` for the real Typography/MSDF artifact path.

## Baseline and bearing policy

Current CPU text placement stays intentionally explicit:

- pen starts at `options.X`
- each glyph placement keeps a fixed `options.BaselineY`
- X destination = `placement.X + (BearingX * scale) - paddedLeft`
- Y destination = `placement.BaselineY - (BearingY * scale) - paddedTop`
- final pixel positions use midpoint-away-from-zero rounding

Whitespace still contributes advance and baseline position, but no atlas entry and no rendered quad.

## Field canvas compensation

The current renderer still compensates for the fixed-size generated field canvas:

- `paddedLeft = max(0, entry.Width - metrics.Width) * 0.5 * scale`
- `paddedTop = max(0, entry.Height - metrics.Height) * 0.5 * scale`
- those centered-padding offsets are subtracted from the metric-box placement

Why it exists:

- M8k/M8l proofs generate fixed-size distance-field images around variable-size glyph metric boxes.
- without this compensation, the visible glyph would drift inside its field rectangle.

Why it is still proof-only:

- a production renderer should eventually consume an explicit atlas contract for glyph origin and usable field bounds instead of inferring centered padding from field size vs metric size.

## Smoothing and threshold policy

Current proof policy:

- threshold defaults to `0.5`
- coverage smoothing is derived from `PxRange` and output scale
- `Sdf` and `Psdf` sample the scalar channel
- `Msdf` samples median RGB
- `Mtsdf` currently also uses median RGB and still defers alpha interpretation

This remains a CPU proof convention, not the final shader-quality tuning contract.

## Whitespace and missing glyph policy

Whitespace:

- metrics-only
- advances the pen
- no atlas rect
- no page quad

Missing visible glyphs:

- fail the proof export by default
- no fallback glyph is synthesized yet

This keeps the artifact audit honest while the renderer contract is still settling.

## Fixes applied

M8l applied two local convention fixes:

- real proof exports now render with `FlipY = true`, which makes the Typography/MSDF proofs upright instead of vertically inverted
- the proof workflow now uses a `320x64` canvas so the requested audit strings fit without clipping

No UI/TextBlock/gallery/renderer integration was introduced.

## Deferred issues

- no `TextBlock` integration
- no production component gallery text integration
- no Machina renderer replacement
- no Vulkan or Aurelian work
- no PNG dependency in `Machina.Fonts`
- no shaping, kerning, bidi, ligatures, grapheme clustering, or fallback glyph chain
- no multiline text layout
- no production contract yet for field-origin metadata beyond the current centered-padding compensation
- no MTSDF alpha-channel policy yet

## Tests

M8l adds or updates tests for:

- `FontProofExporter_WritesExpectedArtifacts`
- `FontProofExporter_IsDeterministicAcrossRuns`
- `FontProofExporter_WritesNonBlankImages`
- `FontProofExporter_WhitespaceAdvancesInAspaceA`
- `FontProofExporter_LongestProofStringsLeaveRightEdgeClear`
- `TypographyMsdfReferenceRender_FlipYProducesUprightGlyphOrientation`

These are still proof-oriented tests. M8l does not add pixel-diff golden comparisons.

## M8m plan

M8m consumes the stabilized audit evidence without over-promoting the CPU proof path.

It now adds an opt-in component-gallery proof card that blits CPU MSDF proof text into the exported gallery PNG while leaving normal bitmap gallery text unchanged. See `docs/machina-component-gallery-msdf-proof-m8m.md`.

That still does not mean production text integration exists.

## Post-M8m follow-up

Likely next work:

- decide the eventual production glyph-origin contract that replaces the centered-padding inference
- decide whether the future renderer should always sample with the current `FlipY` convention or whether atlas export should normalize orientation earlier
- begin UI-facing integration only after those contracts are explicit

Until then, the CPU reference renderer remains a standalone audit/debug tool, not the final Machina text backend.

## M8n follow-up

M8n closes the biggest open M8l spacing question without crossing into production UI integration.

- centered field compensation now recomputes the generator's fit-to-drawable-area padding instead of assuming `(fieldSize - metricsSize) / 2`
- optional adjacent pair adjustment now exists through Machina-owned `IGlyphPairAdjustmentSource`
- Typography/OpenFont pair adjustment is now proven through low-level `GPOS` lookup execution
- `SpaceMono-Regular.ttf` remains the monospaced audit fixture, but `CrimsonText-Regular.ttf` was added to prove real kerning pairs in deterministic local tests and proof artifacts

This is still proof-path only:

- no `TextBlock` integration
- no Standard/UI migration
- no production renderer integration
- no shaping or bidi engine adoption

## M8q follow-up

M8q keeps the M8l proof-only baseline/orientation policy but adds browser-measured validation for it.

- the browser oracle now confirms explicit `alphabetic` baseline use
- the merged report shows Machina proof uses the same `baselineY`
- renderer tests now lock the “plane bounds relative to baseline” contract

M8q therefore does not reintroduce the pre-M8p `baselineY - bearingY - inferred padding` placement model.
