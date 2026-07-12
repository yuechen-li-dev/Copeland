# Machina MSDF Reference Diff Overlay M8r

## Purpose

M8r adds explicit browser-vs-Machina overlay, diff, wireframe, and report tooling for the standalone CPU MSDF proof path.

## Why this exists

M8o, M8p, M8n, M8q, and M8q.2 established a reliable local reference setup, fixed a major field-placement bug, and added kerning plus vertical-metrics evidence. At this point the remaining mismatch was being diagnosed indirectly and every new theory risked cascading error.

M8r stops that loop. It renders the same string to the same-sized browser and Machina canvases, overlays those outputs directly, and writes visual artifacts plus structured metrics that make the mismatch explicit before another rendering change is attempted.

This milestone is diagnostic tooling only:

- no MSDF sampling or threshold fix
- no baseline-placement fix
- no kerning or `GlyphFieldPlacement` contract change
- no production integration
- no magic offsets or tracking fudges

## Inputs

Primary fixture:

- font: `CrimsonText-Regular.ttf`
- size: `32px`
- canvas: `320x64`
- x: `8`
- baselineY: `40`
- baseline guide: enabled, `#ff0000`

Strings exported in `artifacts/m8r`:

- `Machina`
- `Hello Machina`
- `AV To Ta Wa Yo`
- `Aa0`
- `A A`

Command:

```powershell
.\tools\Export-MachinaFontReferenceDiff.ps1 -OutputDir artifacts\m8r
```

## Overlay modes

M8r writes these diagnostic modes per fixture:

- browser source PNG: `browser-*.png`
- Machina source PNG: `machina-msdf-*.png`
- color overlay: `overlay-*.png`
- absolute diff: `diff-*.png`
- threshold diff: `diff-threshold-*.png`
- wireframe and bounds overlay: `wireframe-*.png`
- side-by-side compare: `compare-*.png`

Color overlay policy:

- browser-only ink: cyan
- Machina-only ink: orange
- overlapping ink: white
- baseline guide: red
- background: dark neutral

## Ink mask policy

The diff workflow uses a shared ink-mask policy for browser and Machina images:

- background is `#101018`
- baseline guide is `#ff0000`
- any pixel close to the baseline-guide color is ignored
- any remaining pixel whose RGB distance from the background exceeds a small threshold is treated as ink

Current thresholds:

- ink distance threshold: `12`
- baseline-guide ignore threshold: `24`
- threshold-diff tolerance: `18`

This keeps the 1 px red baseline guide out of bounds and overlap metrics while still counting anti-aliased text pixels as ink.

## Bounds and diff metrics

M8r writes:

- `artifacts/m8r/diff-report.json`
- `artifacts/m8r/diff-report.txt`

Per fixture metrics include:

- `browserInkBounds`
- `machinaInkBounds`
- `deltaLeft`
- `deltaTop`
- `deltaRight`
- `deltaBottom`
- `deltaWidth`
- `deltaHeight`
- `browserInkArea`
- `machinaInkArea`
- `overlapArea`
- `browserOnlyArea`
- `machinaOnlyArea`
- `intersectionOverUnion`
- `meanAbsoluteDifference`
- `maxDifference`
- `mismatchPixelCount`
- `mismatchRatio`

Wireframe artifacts also draw:

- browser ink bounds
- Machina ink bounds
- baseline guide
- browser actual/font metric bounds when available
- Machina per-glyph draw rects from the placement report

## Generated artifacts

Representative required outputs:

- `artifacts/m8r/browser-machina.png`
- `artifacts/m8r/machina-msdf-machina.png`
- `artifacts/m8r/overlay-machina.png`
- `artifacts/m8r/diff-machina.png`
- `artifacts/m8r/diff-threshold-machina.png`
- `artifacts/m8r/wireframe-machina.png`
- `artifacts/m8r/browser-hello-machina.png`
- `artifacts/m8r/overlay-hello-machina.png`
- `artifacts/m8r/diff-hello-machina.png`
- `artifacts/m8r/wireframe-hello-machina.png`
- `artifacts/m8r/browser-kerning.png`
- `artifacts/m8r/overlay-kerning.png`
- `artifacts/m8r/diff-kerning.png`
- `artifacts/m8r/wireframe-kerning.png`
- `artifacts/m8r/diff-report.txt`
- `artifacts/m8r/glyph-placement-report.txt`

## Visual findings

The current `artifacts/m8r` inspection shows:

1. Bounds are shifted mostly downward and expanded to the right. The baseline guide itself lines up, but Machina ink typically starts around `4-5 px` lower than the browser ink and often extends much farther to the right.
2. Machina bounds are taller. The current diff report shows Machina height deltas of `+2 px` to `+7 px`.
3. The mismatch is not just a whole-run translation. Machina also looks wider/heavier, especially on longer runs like `Hello Machina` and `AV To Ta Wa Yo`.
4. The mismatch is not concentrated only below the baseline. Lower-edge differences are visible, but the dominant evidence is broader horizontal extent plus slightly lower placement.
5. Machina per-glyph draw rects appear aligned with Machina ink in the wireframes. The orange glyph boxes generally enclose the rendered orange ink instead of floating away from it.
6. The strongest evidence points toward field bounds / quad extent / metrics-contract investigation, not a sampling-only tweak. A pure coverage or threshold change would not explain the large right-edge and width deltas.

Representative current metrics from `artifacts/m8r/diff-report.txt`:

- `Machina`: `deltaTop=4`, `deltaWidth=63`, `deltaHeight=5`, `IoU=0.1529`
- `Hello Machina`: `deltaTop=4`, `deltaWidth=140`, `deltaHeight=5`, `IoU=0.1455`
- `AV To Ta Wa Yo`: `deltaTop=4`, `deltaWidth=181`, `deltaHeight=7`, `IoU=0.0951`

## What this proves

M8r proves that:

- browser and Machina artifacts can be generated separately for the same input contract
- those artifacts can be overlaid directly with no manual visual editing
- mismatch can be measured and localized with consistent bounds and mask rules
- the baseline line itself is not the dominant source of the current mismatch
- the current discrepancy is visible as run/glyph extent divergence, not just an ambiguous subjective visual impression

## What this does not prove

M8r does not prove:

- the exact one-line fix
- that the browser reference is a production contract
- that sampling/coverage is irrelevant in every case
- that the current outputs should become CI pixel gates
- any production `TextBlock`, Standard, renderer, Vulkan, or gallery behavior change

## Next fix recommendation

Do not change sampling, threshold, smoothing, or add offsets first.

The next evidence-backed step should inspect the remaining proof-path metrics contract around:

- Machina glyph/run width accumulation versus browser measured extent
- plane-bound to draw-quad extent interpretation
- whether the current browser reference extent should be compared against Machina ink bounds, Machina plane bounds, or both

If another narrow fix is attempted, it should be justified against the M8r overlays and reports first, and it should target bounds or metrics behavior before any coverage-only tweak.

## M8s follow-up

M8s keeps the M8r overlay idea, but inserts a direct-outline oracle between browser and MSDF and runs the comparison at multiple sizes.

- `artifacts/m8s` now adds pairwise browser-vs-direct, direct-vs-MSDF, and browser-vs-MSDF overlays plus a three-way overlay
- current M8s aggregate metrics show browser-vs-direct staying relatively stable while direct-vs-MSDF degrades sharply at `64px`
- that evidence shifts the likely next proof-only fix toward the MSDF generation/rendering side rather than another browser-vs-layout theory
