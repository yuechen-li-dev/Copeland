# Machina MSDF Baseline Rounding Fix M8q.1

## Purpose

M8q.1 fixes a narrow proof-renderer raster-placement issue in the standalone CPU MSDF path.

This milestone is proof-path only:

- no production renderer integration
- no `TextBlock` integration
- no Standard text integration
- no magic vertical offset

## Root cause

The baseline mismatch was not a second explicit baseline branch inside `RenderGlyphInto`.

The real bug was a raster rounding inconsistency inside `ComputeDrawBounds`:

- `drawY` was rounded directly from `baselineY + planeTop`
- `drawHeight` was rounded separately from `planeHeight`
- the rasterized baseline inside the output tile is determined by the rounded output tile height

For some fractional `PlaneTop` / `PlaneBottom` combinations, those two rounded values disagree by one pixel.

That makes the rendered tile obey two different raster interpretations at once:

- one for the top edge
- one for the baseline position inside the stretched tile

## Old behavior

Old proof placement was effectively:

```text
drawY = round(baselineY + planeTop * scale)
drawHeight = round((planeBottom - planeTop) * scale)
```

That is usually close, but it can be wrong by 1 px when:

- the glyph is height-constrained,
- `PlaneTop` is fractional,
- and `drawHeight` rounds differently than the implied baseline position inside the tile.

One concrete CrimsonText example from M8q:

- `i` in `Machina`
- old `drawY = 17`
- rounded output height `= 27`
- baseline position implied by the rounded output tile `= 24`
- `17 + 24 = 41`, not the requested baseline pixel `40`

## Fix

M8q.1 switches to one authoritative baseline invariant.

First compute the rounded output tile height:

```text
outputHeight = round(planeHeight * scale)
```

Then compute the baseline position inside that rounded tile from the plane fraction:

```text
baselineFraction = -planeTop / planeHeight
baselineInOutput = round(baselineFraction * outputHeight)
```

Then place the tile from that baseline position:

```text
drawY = round(baselineY) - baselineInOutput
```

Current implementation keeps X placement unchanged and only applies this invariant to Y.

No string-specific offset, no hard-coded `+1/-1`, and no generator/atlas contract change was introduced.

## Baseline invariant

Current proof-path raster invariant:

```text
drawY + baselineInOutput == round(baselineY)
```

Where:

- `baselineInOutput` is derived from the rounded output tile height
- `drawY` is derived from that one baseline position

This removes the prior double-round disagreement between top-edge placement and baseline-in-tile placement.

## Tests

M8q.1 adds or updates tests for:

- `CpuDistanceFieldTextRenderer_BaselineLandsOnRequestedBaseline`
- `CpuDistanceFieldTextRenderer_DoesNotDoubleRoundBaseline`
- `TypographyMsdfReferenceRender_CrimsonTextBaselineRegression`

Existing nonblank and determinism coverage continues to pass for:

- `Machina`
- `Hello Machina`
- `AV To Ta Wa Yo`

## Artifacts

Current export command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q1
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q1 -IncludeMsdfFontProof
```

Relevant artifacts:

- `artifacts/m8q1/browser-text-metrics.json`
- `artifacts/m8q1/glyph-placement-report.txt`
- `artifacts/m8q1/glyph-placement-report.json`
- `artifacts/m8q1/compare-machina.png`
- `artifacts/m8q1/compare-hello-machina.png`
- `artifacts/m8q1/compare-kerning.png`
- `artifacts/m8q1/component-gallery-msdf-proof.png`

Observed result:

- the known CrimsonText lowercase `i` regression now uses the corrected baseline invariant
- the visual change is subtle, consistent with a 1 px proof-path fix
- horizontal spacing and kerning do not regress
- no upside-down, mirrored, or clipping regression was introduced

## Deferred issues

- remaining browser/Machina differences still exist below the baseline for some strings
- those remaining differences now look more like proof ink/coverage extent behavior than baseline placement math
- no production renderer conclusions should be drawn from this proof-only fix
