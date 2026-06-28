# Machina Font Toolkit Layers M9b

## Purpose

M9b adds a configurable layer/composition model on top of the M9a toolkit boundary in `Machina.Fonts.Tooling`.

This milestone is diagnostic tooling only.
It does not change production font rendering behavior.
It does not claim an MSDF or renderer fix.

## Layer model

`Machina.Fonts.Tooling` now models diagnostic outputs as ordered, configurable layers inside one composition.

Core pieces:

- `DiagnosticLayerComposition`
- `DiagnosticLayer`
- `LayerCompositor`
- `LayerPresets`

Each layer has:

- an id
- a label
- `Visible`
- `Opacity`
- `ZIndex`

This lets Codex, other LLMs, and humans build repeatable visual inspections without hardcoding one comparison shape per export path.

## Supported layer types

M9b supports these diagnostic layer types:

- `DiagnosticImageLayer`
- `DiagnosticMaskLayer`
- `DiagnosticBoundsLayer`
- `DiagnosticGridLayer`
- `DiagnosticAxisLayer`
- `DiagnosticBaselineLayer`
- `DiagnosticTextLabelLayer`
- `DiagnosticDifferenceLayer`
- `DiagnosticGlyphWireframeLayer`

These layers cover:

- browser/direct/MSDF image surfaces
- binary or alpha masks
- bounds and draw rectangles
- CAD-style grid measurement
- axes and baseline inspection
- simple diagnostic labels
- pairwise or three-way diff overlays
- glyph wireframe rectangles

## Presets

M9b adds named presets so the local workflow can ask for intent instead of one hardcoded comparison path.

Current presets:

- `browser-vs-direct`
- `direct-vs-msdf`
- `browser-vs-msdf`
- `three-way`
- `grid-only`
- `bounds-only`
- `cad-debug`
- `msdf-debug`

Preset intent:

- `browser-vs-direct`: browser image, direct image, bounds, baseline, optional grid
- `direct-vs-msdf`: direct image, MSDF image, diff, bounds, baseline, grid
- `browser-vs-msdf`: browser image, MSDF image, bounds, baseline, grid
- `three-way`: browser/direct/MSDF mask-style overlap view where available
- `cad-debug`: grid, axes, baseline, bounds, labels, wireframes
- `msdf-debug`: MSDF image, mask, bounds, wireframes, baseline, grid

## Script usage

Current local export entry point:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9b -Preset direct-vs-msdf -GridStep 8 -ShowUnitLabels
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9b -Preset cad-debug -GridStep 8 -ShowUnitLabels -ShowBounds
```

Key options:

- `-Preset`
- `-GridStep`
- `-ShowUnitLabels`
- `-ShowBounds`
- `-ShowGrid`
- `-ShowAxes`

The script remains a tooling/export surface only.
It does not wire anything into production UI or production text rendering.

## CAD-style workflow

The CAD-style view exists to make measurement visible:

- grid spacing
- major intervals
- axes
- baseline
- bounds
- glyph draw boxes
- small labels

That makes it easier to answer questions like:

- how far did ink move
- which extent widened
- whether the baseline is stable
- whether a mismatch is shape-only, bounds-only, or draw-extent related

## LLM/Codex inspection workflow

M9b is explicitly meant to help both humans and LLMs inspect font diagnostics.

The layer model makes it easier to:

- request a specific preset
- hide noisy layers
- keep baseline/grid visible while swapping image sources
- report layer order, visibility, opacity, and source paths numerically

Browser kerning remains useful historical context, but it is not the primary horizontal-spacing success oracle in the current toolkit workflow.
Direct-outline text with Machina's own kerning remains the current internal geometry reference.

## Artifact outputs

Current M9b outputs are generated under `artifacts/m9b`.

Representative outputs include:

- `artifacts/m9b/32/m9b-direct-vs-msdf-hello-machina.png`
- `artifacts/m9b/32/m9b-browser-vs-direct-hello-machina.png`
- `artifacts/m9b/32/m9b-three-way-hello-machina.png`
- `artifacts/m9b/32/m9b-cad-debug-hello-machina.png`
- `artifacts/m9b/32/m9b-msdf-debug-hello-machina.png`
- `artifacts/m9b/layer-composition-report.txt`
- `artifacts/m9b/layer-composition-report.json`
- `artifacts/m9b/shape-diff-report.txt`
- `artifacts/m9b/shape-diff-report.json`

The layer-composition report records:

- presets generated
- layers included
- visibility/order/opacity
- source image paths
- grid settings
- bounds settings

## Tests

`tests/Machina.Fonts.Tooling.Tests` now cover:

- layer ordering and validation
- compositor opacity and non-mutation behavior
- grid, baseline, and bounds drawing
- required preset coverage
- preset-driven export determinism

These tests use synthetic inputs where possible and avoid brittle real-font pixel goldens.

## Deferred future vector/SVG tooling

The layer model is intentionally generic enough to support lightweight vector or SVG inspection later.

That future is still deferred.
M9b stays focused on font diagnostics and export ergonomics first.
