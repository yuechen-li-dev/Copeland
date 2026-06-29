# Machina Font Toolkit Export Hygiene M9c

## Purpose

M9c hardens the `Machina.Fonts.Tooling` export workflow.

This milestone is diagnostic tooling hygiene only:

- safer output-directory handling
- explicit source-availability reporting
- strict versus partial preset policy
- deterministic export manifests

M9c does not change production font rendering behavior and does not apply a rendering fix.

## Output directory handling

`FontDiagnosticArtifactExporter` now owns output-directory preparation for diagnostic exports.

- `CleanOutputDirectory = true` deletes and recreates the requested export directory
- locked or inaccessible paths now fail clearly instead of silently producing a partial export
- repeated exports without clean mode preserve existing behavior but record overwrite/stale-file warnings

Guardrails reject clean mode when the output directory resolves to:

- the repository root
- a drive root
- the user profile root
- an empty/invalid path

## Clean export mode

The script entry point now accepts:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9c -Preset direct-vs-msdf -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Recommended local/Codex workflow:

- use `-Clean` for repeated runs into the same folder
- use a fresh output folder when comparing separate export attempts

## Source availability model

M9c adds a structured availability contract:

- browser reference
- direct outline
- MSDF
- browser mask
- direct mask
- MSDF mask
- placement report
- shape-diff report

The manifest records availability plus warnings and errors so humans and LLMs can distinguish full versus partial exports without inferring from placeholder labels alone.

## Preset source requirements

Preset requirements are now explicit in `LayerPresets`.

Current policy highlights:

- `browser-vs-direct` requires browser reference and direct outline
- `direct-vs-msdf` requires direct outline and MSDF
- `browser-vs-msdf` requires browser reference and MSDF
- `three-way` requires browser reference, direct outline, and MSDF
- `grid-only` requires no font source
- `cad-debug`, `bounds-only`, and `msdf-debug` stay diagnostic-only and do not require browser availability

## Strict vs partial exports

Default policy is strict:

- presets with missing required sources fail the export clearly
- the export manifest still records the failure

Optional partial mode:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9c-partial -Preset browser-vs-direct -AllowPartial -Clean
```

In partial mode:

- the preset can degrade
- warnings are written into the manifest and text reports
- the export is marked incomplete

This keeps browser-unavailable cases explicit instead of easy to misread.

## Export manifest

Each export folder now writes:

- `font-diagnostic-export-manifest.json`
- `font-diagnostic-export-manifest.txt`

The manifest records:

- format, kind, and milestone
- output directory
- presets requested
- clean/partial/grid/bounds options
- source availability
- preset requirement outcomes
- generated artifact paths
- warnings and errors
- complete versus partial status

`generatedAtUtc` is omitted by default to keep the manifest deterministic.

## Script usage

Current recommended commands:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9c -Preset direct-vs-msdf -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9c-browser-partial -Preset browser-vs-direct -AllowPartial -Clean
```

Key script options now include:

- `-Preset`
- `-Clean`
- `-AllowPartial`
- `-GridStep`
- `-ShowUnitLabels`
- `-ShowBounds`
- `-ShowGrid`
- `-ShowAxes`

## Tests

`tests/Machina.Fonts.Tooling.Tests` now cover:

- clean-mode guardrails and directory cleanup
- locked-file failure reporting
- source-availability reporting
- preset requirement contracts
- strict versus partial source policy
- manifest creation, ordering, warnings/errors, and timestamp omission
- deterministic export behavior

## Deferred issues

- M9c does not implement a new browser source
- M9c does not change direct-outline rasterization
- M9c does not change MSDF sampling, smoothing, or thresholds
- M9c does not change atlas packing or production renderer integration

## M9d follow-up

M9d keeps the M9c export-hygiene contract and adds an explicit backend policy:

- `DirectOutlineStatic` is now the default static/UI-text proof backend
- `MsdfScalableExperimental` remains opt-in
- manifests and reports now record the strategy split directly

See `docs/machina-direct-outline-static-text-m9d.md`.

## M9e follow-up

M9e keeps the M9c export hygiene and manifest contract, but adds a separate sample/gallery proof export surface.

- `.\tools\Export-MachinaComponentGallery.ps1` now accepts `-IncludeDirectOutlineTextProof`
- direct-outline gallery proof exports stay local and deterministic under an explicit output directory such as `artifacts\m9e`
- `font-diagnostic-export-manifest.json/txt` remain the diagnostic-toolkit manifest outputs, while the gallery proof writes standalone PNG crops for direct-outline and backend comparison

This still does not change production UI text behavior.

## M9f follow-up

M9f keeps the M9c export-hygiene and manifest discipline while adding a dedicated alignment-repair export path.

- `.\tools\Export-MachinaMsdfAlignmentRepairM9f.ps1 -OutputDir artifacts\m9f -Clean`
- `msdf-alignment-report.json/txt` now summarize before/after direct-vs-MSDF metrics
- `DirectOutlineStatic` remains the geometry oracle
- `MsdfScalableExperimental` remains explicit experimental/scalable
- no browser-kerning oracle swap and no arbitrary visual offsets are introduced

## M9h follow-up

M9h keeps the M9c export-hygiene and manifest discipline while adding a separate render-bridge proof export surface.

- `.\tools\Export-MachinaComponentGallery.ps1` now accepts `-IncludeDirectOutlineRenderBridgeProof`
- render-bridge proof exports stay local and deterministic under an explicit output directory such as `artifacts\m9h`
- `font-diagnostic-export-manifest.json/txt` remain the diagnostic-toolkit manifest outputs
- the bridge contract itself lives in `Machina.Fonts.ReferenceRendering`, not in `Machina.Fonts.Tooling`

This still does not change production UI text behavior or production package dependencies.
