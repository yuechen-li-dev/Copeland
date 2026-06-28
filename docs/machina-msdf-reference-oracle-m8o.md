# Machina MSDF Reference Oracle M8o

## Purpose

M8o adds a trusted local comparison fixture before any further spacing or field-placement tuning.

The goal is not to fix text yet.
The goal is to export one independent reference rendering, the current Machina MSDF rendering, side-by-side comparison artifacts, and a concrete glyph placement report so the next change is evidence-backed.

## Reference renderer chosen

The reference renderer is a local browser canvas path driven by checked-in tooling:

- `tools/font-reference/reference-render.html`
- `tools/font-reference/reference-render.js`
- `tools/Export-MachinaFontReferenceComparison.ps1`

The script uses headless Edge or Chrome if available, loads the checked-in fixture font with `@font-face`, renders the proof text on canvas, and screenshots the result.

Why this path was chosen:

- it is local and simple
- it uses a real font renderer
- it is independent from Machina MSDF placement logic
- it avoids new production dependencies

If automated browser capture is unavailable, the script still exports the Machina-side artifacts and writes manual reference instructions.

## Fixture font/text/size

Primary proof fixture:

- font: `tests/Machina.Fonts.Tests/Fixtures/Fonts/CrimsonText-Regular.ttf`
- family label in reference fixture: `Crimson Text`
- em/font size: `32px`
- output canvas: `320x64`
- origin X: `8`
- baseline Y: `40`
- foreground: `#f0f0f0`
- background: `#101018`

Required proof texts:

- `Machina`
- `Hello Machina`
- `AV To Ta Wa Yo`

Included optional texts:

- `Aa0`
- `A A`

## Artifacts generated

Primary export command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8o
```

Current artifact set under `artifacts/m8o` includes:

- `reference-*.png` browser canvas outputs
- `machina-msdf-*.ppm` raw Machina proof outputs
- `machina-msdf-*.png` PNG copies of the same Machina proof outputs
- `compare-*.png` browser-composed side-by-side comparisons
- `crimson-text-reference-oracle.font-atlas.toml`
- `crimson-text-reference-oracle.page*.dfpage`
- `glyph-placement-report.txt`
- `glyph-placement-report.json`

## Machina placement report

The placement report is generated from the current standalone Machina MSDF proof pipeline and records, per glyph:

- glyph index, character, and codepoint
- glyph key
- advance, bearings, width, height
- pair adjustment from the previous glyph when present
- pen X before and after pair adjustment
- draw X/Y
- atlas page and atlas rect
- UVs
- output field width/height
- computed field padding/compensation values
- whitespace status

This report is written to:

- `artifacts/m8o/glyph-placement-report.txt`
- `artifacts/m8o/glyph-placement-report.json`

## Visual comparison findings

Observed from the current `compare-*.png` artifacts:

1. `A A` is roughly plausible.
   The space advance path appears to work well enough for a simple separated case.

2. Contiguous proportional text is badly overlapped.
   `Machina`, `Hello Machina`, and `Aa0` all show adjacent glyphs colliding far more than the browser reference.

3. Kerning is present, but it is not the primary failure.
   The placement report shows real negative pair adjustments for `AV`, `To`, `Ta`, `Wa`, and `Yo`, but the visual error is much larger than those values.

4. The Machina glyph shapes are visually inflated relative to their advances.
   Narrow glyphs such as `a`, `c`, `i`, and `o` occupy much more horizontal space in the rendered result than the browser reference suggests they should.

5. Some vertical placement also looks inconsistent.
   The most obvious issue is horizontal overlap, but letters with taller or wider forms also show uneven apparent fit inside the 32x32 rendered field.

## Suspected root causes

These are likely causes, not final conclusions:

1. Machina currently renders every non-whitespace glyph through a fixed `32x32` output field.
   The placement report shows atlas rects and output field sizes staying at `32x32` for very different glyphs.

2. Layout advances are based on real typographic metrics, but drawn quads are effectively much wider than those advances.
   For example, the `Machina` report advances by values like `13.219`, `12.656`, and `8.813`, while the renderer still samples a visually large field for each glyph.

3. The current field compensation heuristic is underconstrained.
   `CpuDistanceFieldTextRenderer` reconstructs drawable fit and padding from metrics plus a fixed field size, but it does not have explicit glyph plane bounds from generation time.

4. Pair adjustment is functioning and therefore should not be the first suspected fix.
   The kerning rows show `-2.5` to `-3.125` pair adjustments, which is too small to explain the much larger overlaps seen in the comparison images.

In short: the evidence points more strongly at glyph field sizing and placement reconstruction than at pair-adjustment logic.

## Fix recommendations

Recommended next milestone:

1. Keep using the M8o oracle.
2. Change the proof pipeline to preserve enough glyph-space placement data to reconstruct the correct rendered quad, rather than inferring it from a fixed `32x32` field.
3. Re-run `artifacts/m8o` after that narrower placement-data change.
4. Only then evaluate whether any remaining kerning or bearing correction is still necessary.

Most likely next technical direction:

- capture and export glyph plane bounds or equivalent generator-space placement data
- render from those stored bounds instead of from atlas-cell size plus compensation heuristics

## Deferred issues

- no production renderer integration was added
- no `Standard.Text` layout behavior was changed
- no pixel-diff gate was added
- no browser tooling became a production dependency
- reference comparison is still primarily visual, even though the Machina-side report is numeric

## Next milestone plan

The next milestone should be a narrow placement-data correction pass, not another guess-based spacing patch.

Suggested order:

1. preserve explicit glyph render bounds in the local proof path
2. update the CPU MSDF proof renderer to use those bounds
3. regenerate `artifacts/m8o`
4. compare again against the browser oracle
5. only then decide whether any remaining mismatch is kerning, bearing, baseline, or atlas-origin related

## M8p follow-up

M8p is that next placement-data correction pass.

- generated fields now carry explicit `GlyphFieldPlacement` plane bounds
- atlas entries and `.font-atlas.toml` preserve those bounds
- the CPU proof renderer now draws from stored plane bounds instead of fixed-tile compensation
- regenerated `artifacts/m8p` comparisons materially reduce the contiguous-string overlap that M8o identified

The oracle remains local and proof-only, but it now validates a concrete field-placement contract rather than just exposing the absence of one.

## M8q follow-up

M8q keeps the browser oracle but adds explicit browser `TextMetrics` capture and merged vertical reporting.

- the browser fixture now exports `measureText(...)` fields such as actual bounding box ascent/descent, font bounding box ascent/descent, and baseline metrics
- `glyph-placement-report.txt/json` now includes both browser and Machina vertical metrics
- current M8q evidence shows both paths use the same alphabetic baseline and the same `baselineY = 40`
- the remaining mismatch is not a baseline-origin bug; Machina ink tops already line up with browser actual tops, while Machina ink bottoms still extend slightly lower

So M8q narrows the next problem from “vertical placement might be wrong” to “proof ink extent below the baseline is still a little heavier than browser canvas.”

## M8q.1 follow-up

M8q.1 uses that M8q evidence to fix one last-mile proof raster issue:

- browser and Machina still use the same alphabetic baseline value
- the remaining bug was not kerning or `BearingY` double-application
- the proof renderer could still disagree with itself by 1 px when rounding tile top separately from baseline position inside the rounded output tile

The M8q.1 fix keeps the oracle/browser side unchanged and only tightens proof-path raster placement math.

## M8q.2 follow-up

M8q.2 upgrades the oracle for easier visual inspection without changing the underlying browser-vs-proof baseline contract.

- the browser canvas fixture now draws a red 1 px horizontal guide at the requested `baselineY`
- the Machina proof export now draws the same guide in the proof image
- `glyph-placement-report.txt/json` now record guide enablement and Y metadata
- the compare PNGs therefore show the baseline explicitly in both panels

This is a tooling overlay for diagnosis, not a rendering fix, and no production text path changed.

## M8r follow-up

M8r keeps the M8o oracle contract but replaces side-by-side inspection with direct overlay and diff artifacts.

- `.\tools\Export-MachinaFontReferenceDiff.ps1 -OutputDir artifacts\m8r` now writes separate browser and Machina PNGs, overlays, absolute/threshold diffs, wireframes, and structured reports
- the remaining mismatch is now visible without guessing another fix first
- no additional rendering change is bundled into that tooling pass

## M8s follow-up

M8s keeps the M8o browser oracle, but adds a second Machina-owned oracle between browser and MSDF.

- `.\tools\Export-MachinaFontShapeDiff.ps1 -OutputDir artifacts\m8s` now exports browser masks, direct Typography-outline masks, and MSDF masks at `32px`, `48px`, and `64px`
- the direct-outline path shares Machina layout and pair-adjustment behavior while bypassing MSDF generation and sampling
- current M8s evidence shows browser-vs-direct stays relatively stable while direct-vs-MSDF degrades sharply at larger sizes

That means M8o's original “placement/extent first, not another blind spacing guess” conclusion still holds, but M8s narrows the next investigation more specifically toward the MSDF side of the proof stack.

## M9a follow-up

M9a does not remove the M8o browser oracle, but it changes how the project treats it.

- browser capture remains useful context
- browser horizontal kerning is not the main success target for the consolidated toolkit
- the new M9a workflow centers direct-outline geometry plus Machina MSDF diagnostics
- `Machina.Fonts.Tooling` is now the preferred home for human-facing export orchestration

For current local inspection, prefer the consolidated M9a export when the question is geometric measurement rather than “what does browser canvas do?”
