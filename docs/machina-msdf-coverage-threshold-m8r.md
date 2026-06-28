# Machina MSDF Coverage / Threshold M8r

## Purpose

M8r audits the standalone CPU MSDF proof renderer's sampling-to-coverage path and fixes one proof-only lower-edge ink mismatch using measured browser/Machina coverage data.

This milestone stays inside `Machina.Fonts` proof rendering, browser oracle tooling, tests, docs, and the opt-in gallery proof path.
It does not change baseline placement, kerning, `GlyphFieldPlacement` semantics, production renderers, `TextBlock`, or runtime UI behavior.

## Observed defect

After M8q.2 added the shared red baseline guide, the browser and Machina proofs agreed materially on:

- baseline location
- glyph orientation
- glyph size
- horizontal spacing

The remaining issue was a small but repeatable extra lower-edge ink extent in the Machina proof output, especially visible in `Machina`, `Hello Machina`, and the proportional kerning fixture.

## Current sampling path

Audited proof path:

- `DistanceFieldSampling.SampleDistance(...)` bilinearly samples atlas UVs.
- `Sdf` and `Psdf` read one scalar channel directly.
- `Msdf` and `Mtsdf` both currently decode distance with median RGB.
- `DistanceFieldSampling.ComputeCoverage(...)` interprets `0.5` as the contour threshold and applies a symmetric smoothstep around that threshold.
- pre-M8r smoothing width was:
  `0.5 / max(1, pxRange * scale)`
- proof glyph quads sample the atlas rectangle with output-pixel-centered UV interpolation.
- `CpuDistanceFieldTextRenderer` previously hard-coded `threshold = 0.5` for proof text.

M8r adds explicit proof-path threshold and smoothing controls:

- `DistanceFieldRenderOptions.Threshold`
- `DistanceFieldRenderOptions.SmoothingMultiplier`
- `DistanceFieldTextRenderOptions.Threshold`
- `DistanceFieldTextRenderOptions.SmoothingMultiplier`

The default production-facing meaning of atlas data is unchanged.
This is still a proof-only CPU reference path.

## Browser vs Machina coverage metrics

The browser oracle now records actual canvas-image coverage, not only `TextMetrics`.

`browser-text-metrics.json` now includes per fixture:

- `coverage.inkTop`
- `coverage.inkBottom`
- `coverage.inkLeft`
- `coverage.inkRight`
- `coverage.inkHeight`
- `coverage.inkWidth`
- `coverage.alphaCoverageCountAbove001`
- `coverage.alphaCoverageCountAbove010`
- `coverage.alphaCoverageCountAbove050`
- `coverage.maxAlpha`
- `coverage.averageAlphaNonZero`
- `coverage.baselineY`
- `coverage.descentBelowBaseline`

`glyph-placement-report.txt/json` now includes matching Machina-side coverage metrics plus browser-side coverage bounds.

Coverage is derived from the rendered foreground/background color mix and ignores the red baseline guide color.
Ink bounds now use the `coverage > 0.01` rule rather than raw "pixel differs from background" detection.

Observed browser coverage at M8r baseline:

- `Machina`: descent below baseline = `-1`
- `Hello Machina`: descent below baseline = `-1`
- `AV To Ta Wa Yo`: descent below baseline = `-1`
- `Aa0`: descent below baseline = `-1`
- `A A`: descent below baseline = `-1`

Pre-fix Machina coverage at the old proof defaults was:

- `Machina`: `1`
- `Hello Machina`: `1`
- `AV To Ta Wa Yo`: `3`
- `Aa0`: `-1`
- `A A`: `-1`

## Threshold and smoothing experiment

M8r adds `coverage-experiment.json` and evaluates the proof strings across a local matrix.

Audited strings:

- `Machina`
- `Hello Machina`
- `AV To Ta Wa Yo`
- `Aa0`
- `A A`

Audited matrix:

- threshold: `0.48`, `0.50`, `0.52`, `0.54`, `0.56`, `0.58`, `0.60`
- smoothingMultiplier: `0.5`, `1.0`, `1.5`

Main outcomes:

- threshold and smoothing materially affect lower ink extent
- wider smoothing generally increases soft fringe coverage and worsens lower-edge drift
- `0.54 / 0.5` is the first stable combination that:
  - brings `Machina` to browser descent parity
  - brings `Hello Machina` to browser descent parity
  - keeps `Aa0` and `A A` at parity
  - improves `AV To Ta Wa Yo` from `3` px below baseline to `2` px below baseline
- more aggressive thresholds such as `0.58` and `0.60` also reduce descent, but thin the proof output more than necessary

## Diagnosis

Evidence from M8r supports these conclusions:

1. The mismatch is not a baseline bug.
   The M8q/M8q.1/M8q.2 baseline evidence remains valid.

2. The mismatch is not primarily a kerning or placement bug.
   No baseline, `GlyphFieldPlacement`, or pair-adjustment changes were needed to shift the lower-edge coverage.

3. The mismatch is sensitive to proof threshold and smoothing policy.
   The lower edge moves in the expected direction when threshold is raised and smoothing is tightened.

4. A sample-position rewrite was not justified.
   A texel-center sampling experiment was tried during M8r and removed because it did not improve the lower-edge metrics consistently.

5. The remaining `kerning`-fixture difference is smaller but not fully eliminated.
   That residual issue remains proof-only and is documented for later investigation.

## Fix

Landed proof-path fix:

- proof render options now carry explicit threshold and smoothing controls
- browser oracle metrics now scan actual canvas coverage and ignore the red guide line
- Machina proof reports now scan coverage from rendered output instead of raw non-background pixels
- proof defaults were changed to:
  - `threshold = 0.54`
  - `smoothingMultiplier = 0.5`

Those defaults are applied only in proof/audit paths:

- `FontReferenceOracleWorkflow`
- `FontProofWorkflow`
- `GalleryMsdfFontProofRenderer`

No production renderer, layout path, or UI text control was changed.

## Artifacts

Primary export:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8r
```

Gallery proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8r -IncludeMsdfFontProof
```

M8r artifacts include:

- `artifacts/m8r/browser-text-metrics.json`
- `artifacts/m8r/reference-*.png`
- `artifacts/m8r/machina-msdf-*.ppm`
- `artifacts/m8r/machina-msdf-*.png`
- `artifacts/m8r/compare-*.png`
- `artifacts/m8r/glyph-placement-report.txt`
- `artifacts/m8r/glyph-placement-report.json`
- `artifacts/m8r/coverage-experiment.json`
- `artifacts/m8r/component-gallery-msdf-proof.png`

## Tests

M8r adds or updates coverage/sampling tests for:

- `DistanceFieldSampling_MsdfMedianThreshold_IsStable`
- `DistanceFieldSampling_SmoothAlpha_UsesConfiguredSmoothing`
- `DistanceFieldSampling_SampleCoordinatesUsePixelCenters`
- `DistanceFieldSampling_BilinearOrNearestPolicyIsDocumented`
- `ReferenceOracle_ReportIncludesCoverageMetrics`
- `ReferenceOracle_CoverageScanIgnoresBaselineGuideColor`
- `MachinaCoverageMetrics_ReportsDescentBelowBaseline`
- `TypographyMsdfReferenceRender_ProofStringsRemainNonBlank`
- `TypographyMsdfReferenceRender_OutputIsDeterministic`
- `TypographyMsdfReferenceRender_LowerInkExtentDoesNotRegressAgainstM8q2Baseline`

## Deferred issues

- no baseline placement change
- no `GlyphFieldPlacement` semantic change
- no kerning or pair-adjustment change
- no Typography outline extraction change
- no production renderer integration
- no `TextBlock` integration
- no runtime/browser dependency at runtime
- no arbitrary vertical offset

Residual proof-only issue:

- the proportional kerning fixture still remains slightly heavier below the baseline than browser canvas, though it is improved from the pre-M8r default

## Next milestone

The next milestone should stay proof-only and focus on the remaining proportional lower-edge mismatch only if new evidence continues to justify it.

Likely areas:

- deeper per-glyph MSDF coverage inspection for proportional capitals such as `W`
- field-generation normalization audit if new evidence shows threshold tuning is insufficient
- richer proof reports before any production renderer integration
