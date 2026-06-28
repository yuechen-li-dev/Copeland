# Machina MSDF Vertical Metrics M8q

## Purpose

M8q extends the proof-only browser oracle and Machina placement report so vertical alignment can be diagnosed from measured baseline and bounds data instead of another guessed offset tweak.

This milestone stays inside `Machina.Fonts`, proof tooling, tests, and the sample proof path.
It does not add production renderer integration, `TextBlock` integration, or browser/runtime dependencies outside the local proof workflow.

## Observed defect

After M8p fixed the field-tile overlap problem, side-by-side browser/Machina comparisons still looked vertically inconsistent.

The next suspected failure was specifically:

- alphabetic baseline mismatch,
- plane-top/plane-bottom sign/orientation mismatch,
- or accidental double application of `BearingY` after explicit `GlyphFieldPlacement` was added.

## Browser TextMetrics captured

`tools/font-reference/reference-render.js` now supports a metrics export mode and explicitly sets:

```js
context.textBaseline = "alphabetic";
context.textAlign = "left";
const metrics = context.measureText(text);
```

`artifacts/m8q/browser-text-metrics.json` now records, per proof string:

- `width`
- `actualBoundingBoxLeft`
- `actualBoundingBoxRight`
- `actualBoundingBoxAscent`
- `actualBoundingBoxDescent`
- `fontBoundingBoxAscent`
- `fontBoundingBoxDescent`
- `emHeightAscent`
- `emHeightDescent`
- `alphabeticBaseline`
- `hangingBaseline`
- `ideographicBaseline`
- `fontFamily`
- `fontSize`
- `canvasWidth`
- `canvasHeight`
- `x`
- `baselineY`
- `textBaseline`
- `textAlign`
- `text`

Observed current Chrome fixture values:

- `Machina`: browser actual top/bottom = `18/40`
- `Hello Machina`: browser actual top/bottom = `18/40`
- `AV To Ta Wa Yo`: browser actual top/bottom = `18/41`
- `Aa0`: browser actual top/bottom = `19/41`
- `A A`: browser actual top/bottom = `19/41`

`emHeightAscent` and `emHeightDescent` were unavailable in the current browser export and are therefore written as `null`.

## Machina placement metrics captured

`artifacts/m8q/glyph-placement-report.txt` and `.json` now include:

- report-level coordinate convention notes
- run-level `baselineY`
- run-level `computedTextTop`
- run-level `computedTextBottom`
- run-level `minPlaneTop`
- run-level `maxPlaneBottom`
- run-level `minInkTop`
- run-level `maxInkBottom`
- browser vertical metrics when available

Per glyph they now include:

- `char`
- `codepoint`
- numeric `codepointValue`
- `advance`
- `bearingX`
- `bearingY`
- `metricsWidth`
- `metricsHeight`
- `planeLeft`
- `planeTop`
- `planeRight`
- `planeBottom`
- `pixelRange`
- `projectionScale`
- `penX`
- `baselineY`
- `drawX`
- `drawY`
- `drawWidth`
- `drawHeight`
- atlas rect/page
- pair adjustment data

## Diagnosis

The measured evidence answers the baseline questions directly:

1. Browser reference uses alphabetic baseline.
   `browser-text-metrics.json` records `textBaseline = "alphabetic"` for every fixture.

2. Machina proof uses the same baseline value.
   Both the browser oracle and Machina proof use `baselineY = 40`.

3. `PlaneTop`/`PlaneBottom` are being used with the documented image-down convention.
   `drawY = baselineY + planeTop` is consistent with the stored placement contract.

4. `FlipY = true` is not acting as a hidden placement offset.
   It still only changes vertical sampling inside the UV rectangle.

5. `BearingY` is not being double-applied in the proof renderer.
   The draw quad comes from `GlyphFieldPlacement`, and new renderer tests prove that changing `BearingY` alone does not move the final quad when placement is fixed.

6. The remaining browser/Machina difference is not a baseline-origin shift.
   The current M8q artifacts show Machina ink tops matching browser actual tops for the audited strings:
   `18` vs `18` for `Machina`, `Hello Machina`, and `AV To Ta Wa Yo`
   `19` vs `19` for `Aa0` and `A A`

7. The remaining difference is extra lower-edge ink extent in the proof render.
   Current M8q artifacts show Machina ink bottoms at `41/41/43/40/40`, while browser actual bottoms are `40/40/41/41/41`.

That means the evidence does not support a proof-path baseline offset fix.
Top alignment is already correct.
The residual mismatch is a proof-rendered ink/coverage extent issue below the baseline, not a `baselineY`, `PlaneTop`, or `BearingY` convention bug.

## Fix

M8q intentionally does **not** apply a magic vertical offset, sign flip, or `BearingY` compensation change.

The landed fix is the measurement and reporting contract:

- browser `TextMetrics` are now captured into `browser-text-metrics.json`
- Machina proof reports now include vertical run/glyph metrics
- browser and Machina vertical data are merged into one proof report
- renderer tests now lock the plane-relative baseline convention

This avoids shipping a false baseline fix that would move already-correct top alignment.

## Coordinate convention

Current documented proof convention:

- font outline space uses `+Y up` relative to baseline
- output image storage uses `+Y down` from the top-left
- `PlaneTop` and `PlaneBottom` are stored as image-down offsets relative to baseline
- negative `PlaneTop` means “above the baseline”
- positive `PlaneBottom` means “below the baseline”
- `FlipY = true` affects sampling orientation only

## Baseline / bearing policy

Current proof-path policy after M8p and M8q:

- layout still advances the pen from glyph metrics and pair adjustment
- the final drawn quad is baseline-relative `GlyphFieldPlacement`
- `BearingY` remains useful as source metrics/report data
- `BearingY` is not used a second time once explicit placement is available

The renderer contract remains:

```text
drawX = penX + placement.PlaneLeft
drawY = baselineY + placement.PlaneTop
drawWidth = placement.PlaneRight - placement.PlaneLeft
drawHeight = placement.PlaneBottom - placement.PlaneTop
```

## Artifacts

Current M8q export command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q
```

Current M8q artifact set includes:

- `artifacts/m8q/browser-text-metrics.json`
- `artifacts/m8q/reference-*.png`
- `artifacts/m8q/machina-msdf-*.ppm`
- `artifacts/m8q/machina-msdf-*.png`
- `artifacts/m8q/compare-*.png`
- `artifacts/m8q/glyph-placement-report.txt`
- `artifacts/m8q/glyph-placement-report.json`
- `artifacts/m8q/crimson-text-reference-oracle.font-atlas.toml`
- `artifacts/m8q/crimson-text-reference-oracle.page*.dfpage`

The gallery proof was also regenerated with:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q -IncludeMsdfFontProof
```

## Tests

M8q adds or updates tests for:

- `CpuDistanceFieldTextRenderer_UsesPlaneBoundsRelativeToBaseline`
- `CpuDistanceFieldTextRenderer_DoesNotDoubleApplyBearingYWhenUsingPlacement`
- `GlyphPlacementReport_IncludesVerticalMetrics`
- `ReferenceOracle_ReportIncludesBrowserAndMachinaVerticalMetrics`
- `ReferenceOracle_TextMetricsScriptContainsBaselineAndBoundsCapture`

## Deferred issues

- no `TextBlock` integration
- no `Machina.Standard.Text` integration
- no production renderer integration
- no Vulkan/Aurelian work
- no magic vertical offset
- no attempt to use browser metrics at runtime

The remaining 1-2 px lower-edge mismatch appears to belong to proof render coverage/ink extent, not baseline convention.

## Next milestone

The next milestone should target the remaining proof-only lower-edge ink/coverage mismatch without re-opening kerning or baseline conventions.

Likely focus:

- proof-render coverage/threshold audit
- field-to-ink extent accounting
- or tighter proof-only ink-bound diagnostics

## M8q.1 follow-up

M8q.1 verified that one narrow proof-renderer raster bug still existed after M8q.

- `RenderGlyphInto` does not compute a second explicit baseline branch
- but `ComputeDrawBounds` previously rounded `drawY` from `PlaneTop` while the rasterized baseline position inside the rounded output tile was implied by `drawHeight`
- those two rounded quantities could disagree by 1 px for fractional height-constrained glyphs

The fix keeps the work proof-only and uses one authoritative raster baseline invariant:

```text
baselineInOutput = round((-PlaneTop / PlaneHeight) * drawHeight)
drawY = round(baselineY) - baselineInOutput
```

No magic vertical offset, generator change, or production renderer integration was introduced.

## M8q.2 follow-up

M8q.2 keeps the M8q and M8q.1 conclusions intact, but makes the evidence easier to inspect visually.

- browser reference renders now include a 1 px red baseline guide at the exact `baselineY`
- Machina proof renders now include the same 1 px red baseline guide
- compare artifacts and the opt-in gallery proof export therefore show the shared baseline explicitly
- reports now record `baselineGuideEnabled`, `baselineGuideY`, and guide color metadata

This remains a tooling overlay for visual diagnosis only.
It is not a new rendering fix, and no production text path changed.

## M8r follow-up

M8r uses the M8q vertical evidence exactly as intended:

- baseline placement stayed fixed
- browser `TextMetrics` were preserved
- new browser/Machina image-coverage metrics were added on top of the earlier vertical report
- the next landed change was proof-only coverage tuning, not another baseline correction

So M8q's core conclusion remains valid: the remaining mismatch was below-baseline ink coverage, not baseline-origin math.
