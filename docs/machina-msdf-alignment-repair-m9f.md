# Machina MSDF Alignment Repair M9f

## Purpose

M9f is the first real repair milestone for `MsdfScalableExperimental` after the M9 proof/tooling work.

It diagnoses the remaining direct-vs-MSDF mismatch against `DirectOutlineStatic`, fixes the smallest real MSDF-side contract errors that were still present, and proves the improvement with numeric and visual artifacts.

M9f remains proof/tooling work only.
It does not change production UI text rendering defaults or integrate MSDF into production UI text.

## DirectOutlineStatic as geometry oracle

`DirectOutlineStatic` is the internal geometry truth for M9f.

For the same font, text, em size, origin, baseline, advances, pair adjustments, and outlines:

- direct-outline layout is the shared placement contract
- direct-outline mask is the comparison oracle
- browser kerning is not the target oracle for this milestone

## What was wrong

Two MSDF-side issues were still compounding:

1. the experimental/scalable proof path was still generating a fixed `32x32` field even for larger output sizes such as `64px`
2. atlas UV sampling still used a pixel-edge style reconstruction contract instead of a texel-center contract

The first issue was the dominant one.
At larger sizes, the right glyph layout was being reconstructed from under-resolved fields, which made the MSDF output drift lower/wider against the direct-outline oracle and made the mismatch grow with size.

## Rect and placement contracts

M9f re-audited the shared text layout contract and kept direct-outline as the source of truth:

- same font face
- same em size
- same glyph order
- same whitespace handling
- same pen positions
- same pair-adjustment application order

No direct-outline geometry was changed to match broken MSDF output.
No arbitrary visual offsets were added.

## Atlas packing contract

The atlas contract remains:

- packed rects store bitmap storage inside the page
- `GlyphFieldPlacement` stores drawable plane bounds relative to the baseline
- atlas padding remains storage-only and is not re-applied as draw placement

M9f keeps those contracts explicit and adds tests that the draw rect comes from plane bounds, not atlas rect dimensions or padding guesses.

## UV and texel-center contract

M9f fixes the sampling contract to map normalized atlas UVs back to source texels with a texel-center convention.

That removes the small but real edge-sampling mismatch that remained when MSDF glyph quads were reconstructed from packed atlas rects.

## Pixel range and smoothing contract

M9f does not hide the mismatch with threshold nudges.

The main repair is not a smoothing tweak.
The dominant improvement comes from scaling experimental field resolution with em size so larger proof renders are not reconstructed from the same small fixed field.

Pixel range remains explicit and stable.

## Fix applied

M9f applies two real MSDF-side fixes:

1. `MsdfScalableExperimental` proof/export paths now scale field dimensions with em size instead of hardcoding `32x32` for every size
2. atlas UV reconstruction now uses a texel-center sampling contract

This keeps `DirectOutlineStatic` as the default static/UI-text proof backend and keeps MSDF explicit experimental/scalable.

## Numeric before/after results

The strongest improvement is at `64px`, where the old fixed-field path degraded sharply.

Representative M9f result:

- `Hello Machina` direct-vs-MSDF IoU improved from roughly `0.31` to roughly `0.43` in the main M9f export
- bounds deltas and p95 edge distances also shrink materially at larger sizes
- the systematic “lower and wider” MSDF drift is no longer the dominant pattern in the repaired export

Machine-readable and human-readable summaries are written to:

- `artifacts/m9f/msdf-alignment-report.json`
- `artifacts/m9f/msdf-alignment-report.txt`

## Artifacts

Primary M9f export command:

```powershell
.\tools\Export-MachinaMsdfAlignmentRepairM9f.ps1 -OutputDir artifacts\m9f -Clean
```

Key outputs include:

- `artifacts/m9f/m9f-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-settings.png`
- `artifacts/m9f/m9f-msdf-debug-hello-machina.png`
- `artifacts/m9f/m9f-cad-debug-hello-machina.png`
- `artifacts/m9f/m9f-before-after-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/shape-diff-report.json`
- `artifacts/m9f/shape-diff-report.txt`
- `artifacts/m9f/font-diagnostic-export-manifest.json`
- `artifacts/m9f/font-diagnostic-export-manifest.txt`
- `artifacts/m9f/msdf-alignment-report.json`
- `artifacts/m9f/msdf-alignment-report.txt`

## What changed

- MSDF proof/export paths now scale field resolution with em size
- UV sampling uses a texel-center mapping
- new parity, placement, UV, and alignment regression tests were added
- M9f export automation now writes before/after evidence under `artifacts/m9f`

## What did not change

- `DirectOutlineStatic` stayed the geometry oracle
- production UI text rendering behavior did not change
- MSDF did not become the default static text path
- browser kerning did not become the target oracle
- no arbitrary visual offsets or fudge factors were added
- no forbidden image/font/native dependencies were introduced

## Deferred MSDF work

- any future smoothing-only tuning should be evaluated separately from placement/alignment repair
- production UI integration remains out of scope
- broader scalable-text/runtime decisions remain separate milestones after the repaired proof contract

## M9g boundary

M9g follows M9f but does not extend this MSDF repair scope.

- `DirectOutlineStatic` keeps acting as the proof geometry oracle
- the new layout contract work is direct-outline proof-only
- MSDF remains explicit experimental/scalable and is not changed again in M9g

## M9i boundary

M9i also does not extend MSDF scope.

- no new MSDF generation change
- no new MSDF sampling change
- no production default switch
- MSDF stays explicit experimental/scalable while direct-outline closes the current proof phase
