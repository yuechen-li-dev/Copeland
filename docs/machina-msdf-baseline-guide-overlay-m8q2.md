# Machina MSDF Baseline Guide Overlay M8q.2

## Purpose

M8q.2 adds an explicit 1 px red baseline guide to the local browser-oracle and Machina CPU MSDF proof artifacts.

This is a tooling overlay for visual diagnosis.
It is not itself a rendering fix, and no production text path was changed.

## What changed

- `tools/font-reference/reference-render.js` now draws a red 1 px horizontal baseline guide in the browser reference render and reports its metadata.
- `Machina.Fonts.ReferenceRendering.CpuDistanceFieldTextRenderer` now supports an optional baseline guide overlay in proof renders.
- the reference-oracle export enables that guide for browser and Machina outputs by default
- glyph placement reports now record baseline-guide enablement, Y position, and color
- the component gallery MSDF proof path reuses the same proof-side guide configuration

## Baseline guide convention

- guide color: `#ff0000`
- guide thickness: `1 px`
- guide Y: the exact `baselineY` used for text placement
- browser convention: line is stroked on the pixel row for the requested baseline
- Machina convention: line is written to the rounded baseline row in the output image

Coordinate note:
font outline coordinates still use `+Y up` relative to baseline, while output images use `+Y down` from top-left.

## Where the line is drawn

- browser oracle: across the full canvas width after the text is drawn
- Machina proof: across the full output image width after glyph rendering
- comparison artifacts: visible because each source panel image now already contains the guide
- gallery proof: visible in each MSDF proof line when the opt-in proof export is enabled

## Artifact inspection workflow

Reference-oracle export:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q2
```

Gallery proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q2 -IncludeMsdfFontProof
```

Inspect these first:

- `artifacts/m8q2/compare-machina.png`
- `artifacts/m8q2/compare-hello-machina.png`
- `artifacts/m8q2/compare-kerning.png`
- `artifacts/m8q2/compare-aa0.png`
- `artifacts/m8q2/compare-a-space-a.png`
- `artifacts/m8q2/component-gallery-msdf-proof.png`

The guide should make it immediately obvious whether browser and Machina ink sit flush to the same baseline or diverge above/below it.

## Non-goals

- no glyph spacing change
- no kerning change
- no plane-bounds math change
- no font atlas generation change
- no MSDF generation change
- no Typography outline extraction change
- no production UI text rendering change
- no `StandardUI.TextBlock` integration change
- no arbitrary vertical-offset “fix”

## M8r follow-up

M8r reuses the M8q.2 baseline guide unchanged inside the new overlay workflow.

- the red baseline remains diagnostic-only
- the M8r ink-mask policy explicitly ignores baseline-guide pixels for bounds and overlap metrics
- the overlay evidence shows the baseline line itself is aligned even when browser and Machina ink differ

## M8s follow-up

M8s keeps the same red baseline-guide contract and applies the same ignore policy to all three mask paths.

- browser, direct-outline, and MSDF artifacts at `32px`, `48px`, and `64px` all include the same baseline line
- the extracted ink masks explicitly ignore that guide before computing IoU, bounds, or edge-distance metrics
- the resulting report still points away from the baseline line itself as the dominant mismatch source

## M9a follow-up

M9a keeps the M8q.2 baseline guide idea and folds it into the consolidated toolkit grid.

- baseline remains a tooling overlay, not a renderer fix
- the new toolkit adds axes, grid density, optional unit labels, and bounds on top of the same baseline-visibility goal
- the red baseline is now part of a broader CAD-style measurement surface under `Machina.Fonts.Tooling`
