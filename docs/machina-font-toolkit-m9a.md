# Machina Font Toolkit M9a

## Purpose

M9a consolidates Machina's font proof, comparison, diff, grid, and report workflows behind a dedicated toolkit boundary:

- `src/Machina.Fonts.Tooling/`
- `tests/Machina.Fonts.Tooling.Tests/`
- `tools/Export-MachinaFontDiagnostics.ps1`

This milestone is about diagnostic clarity and workflow cleanup.
It is not a new renderer fix.

## Why this exists

Late M8 proved a lot of useful things:

- Typography/OpenFont outline extraction works
- `MSDF-Sharp.Core` generation works
- atlas packing and `.font-atlas.toml` / `.dfpage` export work
- direct outline rasterization works as a diagnostic reference
- CPU MSDF rendering works as a proof path
- browser reference capture is useful context
- baseline overlays, shape diffs, and placement reports are useful evidence

But the tooling was spread across:

- `src/Machina.Fonts/ReferenceRendering/`
- `tests/Machina.Fonts.Tests/Rendering/`
- `tools/*.ps1`
- sample-only proof hooks

M9a creates a cleaner place for human-facing and LLM-facing diagnostics so production font code does not keep accumulating ad hoc proof orchestration.

## Production vs tooling boundary

Production side:

- `src/Machina.Fonts`
- font records
- outline extraction adapters
- MSDF generation
- atlas packing
- `.font-atlas.toml` / `.dfpage` contracts
- low-level reference-rendering substrate already used by proof code

Tooling side:

- `src/Machina.Fonts.Tooling`
- diagnostic grid and axis overlays
- bounds and wireframe overlays
- consolidated artifact export
- shape-diff reports for humans and LLMs

Important constraints:

- `Machina.Fonts.Tooling` may reference `Machina.Fonts`
- production packages do not reference `Machina.Fonts.Tooling`
- M9a does not change production text rendering behavior

## Toolkit responsibilities

M9a's toolkit is responsible for:

- direct-outline vs MSDF diagnostic export
- visual diagnostic PNG generation
- grid, axis, and baseline overlays
- bounds and wireframe overlays
- structured shape-diff reports
- repeatable script entry points for local artifact generation

Legacy M8 scripts remain as historical workflows and compatibility paths, but the new consolidated entry point is:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9a -ShowGrid -GridStep 8 -ShowBounds
```

## Diagnostic grid

M9a adds a CAD-style measurement layer over diagnostic images.

Current grid capabilities:

- X axis
- Y axis
- major and minor grid lines
- configurable `GridStep`
- configurable `AxisStep`
- optional unit labels
- optional origin marker
- explicit baseline line

Default visual intent:

- grid: muted gray
- major lines: stronger gray
- axes: brighter gray
- baseline: red

The grid exists so humans and LLMs can read distances, extents, and baseline placement without manually eyeballing raw glyph pixels.

## Axes, baseline, and unit labels

The grid overlay treats image-space measurement and text baseline as separate concepts:

- X axis: top image axis
- Y axis: left image axis
- baseline: explicit text baseline row

Unit labels are intentionally small and simple.
They are tooling text, not part of the font proof itself.

## Bounds and wireframe overlays

M9a overlays stable diagnostic bounds with deterministic colors:

- browser bounds: cyan
- direct outline bounds: green
- Machina/MSDF bounds: orange
- wireframe / draw bounds: amber

The overlay is allowed to omit a bound when the source bound is missing.
Missing bounds are not treated as an error for the drawing layer.

## Shape diff workflow

M9a keeps the late-M8 lesson:

- browser kerning differences are not the primary target
- direct outline rasterized text with Machina's own kerning is the current internal geometry reference

The new consolidated report therefore emphasizes:

- direct-outline vs MSDF IoU
- edge-distance summaries
- bounds deltas
- per-size fixture reports

Browser capture remains useful context in older workflows, but M9a does not keep chasing browser horizontal kerning as the success target.

## Artifact export workflow

Primary command:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9a -ShowGrid -GridStep 8 -ShowBounds
```

Current required M9a outputs include:

- `artifacts/m9a/32/font-diagnostic-machina-grid.png`
- `artifacts/m9a/32/font-diagnostic-hello-machina-grid.png`
- `artifacts/m9a/32/font-diagnostic-kerning-grid.png`
- `artifacts/m9a/64/font-diagnostic-machina-grid.png`
- `artifacts/m9a/64/font-diagnostic-hello-machina-grid.png`
- `artifacts/m9a/64/font-diagnostic-kerning-grid.png`
- `artifacts/m9a/shape-diff-report.txt`
- `artifacts/m9a/shape-diff-report.json`

The exporter also writes direct-outline, MSDF, wireframe, and atlas side artifacts that help deeper audits.

## What this does not do

M9a does not:

- change Typography outline extraction
- change MSDF generation
- change atlas packing
- change `TextBlock` or `Standard.Text`
- integrate MSDF into production UI
- add browser as a production dependency
- fix browser kerning
- apply arbitrary spacing nudges
- claim a production renderer improvement

## Future vector/SVG tooling direction

The toolkit boundary is intentionally named broadly enough to grow into lightweight vector inspection later.

Possible future directions:

- SVG path overlay diagnostics
- generic vector bounds inspection
- shape-diff workflows for non-font vector assets

But M9a stays focused on font diagnostics first.

## M9b follow-up

M9b keeps the M9a boundary and overlay work, but moves the export surface from mostly hardcoded comparisons to configurable layer compositions and named presets.

- `Machina.Fonts.Tooling` remains the diagnostic-only boundary
- layer visibility, order, opacity, and colors are now configurable in tooling
- preset-driven exports now live under `artifacts/m9b`

See `docs/machina-font-toolkit-layers-m9b.md`.

## M9c follow-up

M9c keeps the same tooling-only boundary and focuses on export hygiene instead of another rendering change.

- repeated exports now have an explicit clean mode
- source availability is now reported structurally instead of being implied by placeholder labels
- export folders now include deterministic manifest files so full and partial runs are harder to misread

See `docs/machina-font-toolkit-export-hygiene-m9c.md`.
