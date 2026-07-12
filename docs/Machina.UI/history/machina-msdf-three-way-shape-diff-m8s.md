# Machina MSDF Three-Way Shape Diff M8s

## Purpose

M8s turns the current browser-vs-Machina proof stack into a three-way numeric diagnostic:

- browser canvas mask
- Machina direct Typography-outline mask
- Machina CPU MSDF mask

The goal is to identify where the remaining mismatch enters without mixing in another rendering fix.

M8s is diagnostic tooling only.
It does not change production rendering, `TextBlock`, baseline placement, kerning behavior, `GlyphFieldPlacement`, or MSDF sampling policy.

## Why direct outline mask

M8o, M8q, M8q.2, and M8r proved that:

- the browser reference path is useful,
- baseline alignment is materially correct,
- and the remaining mismatch is real.

But the previous comparison was still missing one critical middle oracle:
the exact same Machina glyph metrics, layout, and pair-adjustment path rendered directly from Typography outlines with no MSDF generation, atlas packing, or MSDF sampling in the loop.

That direct-outline mask lets M8s separate:

- browser-vs-direct-outline mismatch
- direct-outline-vs-MSDF mismatch
- browser-vs-MSDF mismatch

## Inputs

Primary fixture:

- font: `tests/Machina.UI/Machina.Fonts.Tests/Fixtures/Fonts/CrimsonText-Regular.ttf`
- fill rule: even-odd
- direct-outline supersample: `4x`
- curve subdivision: `24` fixed steps per quadratic/cubic segment
- baseline guide: enabled, `#ff0000`

Texts:

- `Machina`
- `Hello Machina`
- `AV To Ta Wa Yo`
- `Aa0`
- `A A`

## Font sizes

M8s runs the same proof at three required sizes:

- `32px` -> canvas `320x64`, `x=8`, `baselineY=40`
- `48px` -> canvas `480x96`, `x=12`, `baselineY=60`
- `64px` -> canvas `640x128`, `x=16`, `baselineY=80`

This keeps the previous canvas convention but scales it linearly with font size so larger-size divergence becomes visible numerically and visually.

## Direct outline rasterization

M8s adds proof-only direct outline rasterization under `src/Machina.UI/Machina.Fonts/ReferenceRendering/`.

Policy:

- Typography `GlyphOutline` contours are flattened deterministically into polylines.
- Lines pass through unchanged.
- Quadratic and cubic curves are subdivided with a fixed count.
- Pixel coverage is estimated with supersampling.
- Coverage is unioned across glyphs with no arbitrary offsets or tracking hacks.

This is not a production vector renderer.
It is a deterministic diagnostic raster path intended to share Machina layout and pair-adjustment behavior while bypassing MSDF generation and sampling.

## Mask extraction

Browser and MSDF mask extraction use the same threshold policy:

- background color: `#101018`
- baseline guide color: `#ff0000`
- ink distance threshold: `12`
- baseline-guide ignore threshold: `24`

Any pixel near the red baseline guide is excluded from the ink mask.
Any remaining pixel whose RGB distance from the background exceeds the threshold counts as ink.

The direct-outline path renders coverage directly into an `InkMask` and writes that mask back out as a grayscale PNG with the same baseline guide.

## Shape metrics

For each pairwise comparison M8s computes:

- `intersectionOverUnion`
- `intersectionArea`
- `unionArea`
- per-side ink area
- per-side only area
- bounds for each mask
- per-axis bounds deltas
- symmetric edge-distance summaries:
  - mean
  - p50
  - p95
  - max
- mismatch area above the baseline
- mismatch area below the baseline

The report also attaches conservative heuristic labels such as:

- `msdf-render-mismatch`
- `outline-mismatch`
- `global-shift`
- `horizontal-overrun`
- `vertical-overrun`
- `coverage-heavy`
- `coverage-light`
- `unknown`

## Artifacts

Primary export command:

```powershell
.\tools\Export-MachinaFontShapeDiff.ps1 -OutputDir artifacts\m8s
```

Current output structure:

- `artifacts/m8s/32/`
- `artifacts/m8s/48/`
- `artifacts/m8s/64/`
- `artifacts/m8s/browser-shape-diff-captures.json`
- `artifacts/m8s/shape-diff-report.txt`
- `artifacts/m8s/shape-diff-report.json`

Per size/text M8s writes:

- `browser-*.png`
- `direct-outline-*.png`
- `msdf-*.png`
- `diff-browser-vs-direct-*.png`
- `diff-direct-vs-msdf-*.png`
- `diff-browser-vs-msdf-*.png`
- `overlay-three-way-*.png`
- `wireframe-*.png`

## Findings

Current export findings from `artifacts/m8s/shape-diff-report.txt`:

- average browser-vs-direct IoU: `0.528`
- average direct-vs-MSDF IoU: `0.465`
- average browser-vs-MSDF IoU: `0.344`

By size:

- `32px`: browser-vs-direct `0.532`, direct-vs-MSDF `0.584`, browser-vs-MSDF `0.432`
- `48px`: browser-vs-direct `0.511`, direct-vs-MSDF `0.496`, browser-vs-MSDF `0.347`
- `64px`: browser-vs-direct `0.542`, direct-vs-MSDF `0.316`, browser-vs-MSDF `0.253`

Current overall M8s classification:

- `msdf-render-mismatch`
- confidence: `0.88`

Current overall note:

> Browser-vs-direct IoU stays relatively stable (0.532 at 32px vs 0.542 at 64px), while direct-vs-MSDF drops sharply (0.584 to 0.316). The mismatch most likely enters in the MSDF generation or MSDF rendering stage, and it becomes more obvious at larger sizes.

Important nuance:

- not every string isolates the same way
- `AV To Ta Wa Yo` still shows large browser-vs-direct disagreement at all sizes
- that means browser shaping/hinting differences or some remaining layout/extent convention issue still contribute for some strings

So M8s narrows the next investigation, but it does not claim one universal single-line fix for every case.

## What this proves

M8s proves that:

- direct outline mask rendering now exists in `Machina.Fonts` proof tooling
- browser, direct-outline, and MSDF masks can be generated under one shared size/text contract
- the mismatch can be decomposed numerically instead of visually guessed
- the MSDF path degrades faster than the direct-outline path as size increases
- baseline alignment is still not the dominant remaining problem

## What this does not prove

M8s does not prove:

- the exact one-line MSDF fix
- whether the MSDF issue is generation, sampling, or proof render extent reconstruction
- that browser canvas behavior is a production contract
- that all remaining browser/direct disagreement is unimportant
- that any production renderer or `TextBlock` integration should happen yet

## Recommended next fix

The next milestone should stay proof-only and target the MSDF side specifically.

Recommended order:

1. keep the M8s three-way export as the regression oracle
2. inspect direct-outline-vs-MSDF divergence first, especially at `64px`
3. audit MSDF generation scale/range and proof-side MSDF draw extent before changing browser/direct placement logic
4. only revisit browser/direct-outline disagreement separately for strings like `AV To Ta Wa Yo`

Do not start with:

- baseline nudges
- kerning rewrites
- `GlyphFieldPlacement` redesign
- smoothing/threshold tweaks presented as the fix

## M9a follow-up

M9a keeps M8s as important historical evidence, but moves the human-facing orchestration into `Machina.Fonts.Tooling`.

- the new consolidated export entry point is `.\tools\Export-MachinaFontDiagnostics.ps1`
- the new diagnostic PNGs add CAD-style grid, axis, baseline, bounds, and wireframe overlays
- browser horizontal kerning is no longer treated as the primary target oracle for the consolidated workflow
- direct-outline text with Machina's own kerning remains the current geometry reference for M9a diagnostics

M8s therefore remains valid as the proof that the direct-outline oracle matters, while M9a turns that lesson into a clearer toolkit boundary.

## M9b follow-up

M9b keeps that same diagnostic lesson, but expresses it through configurable layers and named presets inside `Machina.Fonts.Tooling`.

- three-way comparison is now one preset instead of one hardcoded export shape
- CAD-style grid, bounds, axes, baseline, labels, and diff layers can now be recombined for humans and LLMs
- this is still tooling ergonomics only, not a renderer fix

## M9d follow-up

The M8s direct-outline path is now the formal static geometry reference inside the tooling stack.

Browser horizontal kerning is still not the oracle, and M9d still does not attempt an MSDF repair.

See `docs/Machina.UI/history/machina-direct-outline-static-text-m9d.md`.
