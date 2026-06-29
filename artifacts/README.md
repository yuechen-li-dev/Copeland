# Artifacts

This directory holds deterministic render and visual-audit artifacts for Machina milestones.

## Policy

- Checked-in artifacts may exist for historical milestone notes, render-contract documentation, or tiny golden references.
- `artifacts/m7e/` is the current component-gallery export output directory.
- `artifacts/m8m/` is the current opt-in component-gallery MSDF proof output directory.
- `artifacts/m8l/` is the current local CPU MSDF text proof audit output directory.
- `artifacts/m8n/` is the current local CPU MSDF spacing and kerning audit output directory.
- `artifacts/m8o/` is the current local MSDF reference-oracle comparison output directory.
- `artifacts/m8q/` is the current local MSDF vertical-metrics comparison output directory.
- `artifacts/m8q1/` is the current local MSDF baseline-rounding comparison output directory.
- `artifacts/m8q2/` is the current local MSDF baseline-guide overlay comparison output directory.
- `artifacts/m8r/` is the current local browser-vs-Machina overlay diff output directory.
- `artifacts/m8s/` is the current local browser/direct-outline/MSDF three-way shape-diff output directory.
- `artifacts/m9a/` is the historical first consolidated Machina font toolkit diagnostic output directory.
- `artifacts/m9b/` is the historical preset-driven Machina font toolkit diagnostic output directory.
- `artifacts/m9c/` is the current export-hygiene and source-contract font toolkit diagnostic output directory.
- `artifacts/m9e/` is the current direct-outline component-gallery proof output directory.
- `artifacts/m9f/` is the current MSDF alignment-repair diagnostic output directory.
- `artifacts/m9g/` is the current direct-outline text-layout proof output directory.
- `artifacts/m9h/` is the current direct-outline render-bridge proof output directory.
- Current gallery artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8l proof artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8n proof artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8o comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8q comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8q.1 comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8q.2 comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8r comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8s comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9a comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9b comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9c comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9e comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9f comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9g comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M9h comparison artifacts are generated locally by script/command and are ignored by Git for now.
- These files are visual audit aids, not an automated pixel-diff baseline gate.

## Regenerating the component gallery artifacts

From the repo root:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
```

Manual fallback:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-default
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
```

Current M7e audit command:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7e
```

Current M8m proof audit command:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8m -IncludeMsdfFontProof
```

Current M9e proof audit command:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9e -IncludeDirectOutlineTextProof
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9e -IncludeDirectOutlineTextProof -IncludeMsdfFontProof
```

Current M9g proof audit commands:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9g -IncludeDirectOutlineTextProof -IncludeDirectOutlineTextLayoutProof
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9g -IncludeDirectOutlineTextProof -IncludeDirectOutlineTextLayoutProof -IncludeMsdfFontProof
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9g -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Current M9h proof audit commands:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9h -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9h -IncludeDirectOutlineRenderBridgeProof
```

## Regenerating the Machina font proof artifacts

From the repo root:

```powershell
.\tools\Export-MachinaFontProofs.ps1
```

Manual fallback:

```powershell
$env:MACHINA_FONT_PROOF_OUTPUT_DIR = (Resolve-Path artifacts\m8l)
dotnet test tests/Machina.Fonts.Tests/Machina.Fonts.Tests.csproj --filter "FullyQualifiedName~Machina.Fonts.Tests.Rendering.FontProofExporterTests.FontProofExporter_ScriptWorkflowExportsProofSet"
Remove-Item Env:\MACHINA_FONT_PROOF_OUTPUT_DIR
```

Current M8l audit command:

```powershell
.\tools\Export-MachinaFontProofs.ps1 -OutputDir artifacts\m8l
```

Current M8n audit command:

```powershell
.\tools\Export-MachinaFontProofs.ps1 -OutputDir artifacts\m8n
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8n -IncludeMsdfFontProof
```

Current M8o audit command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8o
```

Current M8q audit command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q -IncludeMsdfFontProof
```

Current M8q.1 audit command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q1
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q1 -IncludeMsdfFontProof
```

Current M8q.2 audit command:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q2
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8q2 -IncludeMsdfFontProof
```

Current M8r audit command:

```powershell
.\tools\Export-MachinaFontReferenceDiff.ps1 -OutputDir artifacts\m8r
```

Current M8s audit command:

```powershell
.\tools\Export-MachinaFontShapeDiff.ps1 -OutputDir artifacts\m8s
```

Current M9d audit commands:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9d -Preset cad-debug -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9d-msdf -Preset msdf-debug -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Current M9f audit command:

```powershell
.\tools\Export-MachinaMsdfAlignmentRepairM9f.ps1 -OutputDir artifacts\m9f -Clean
```

## Current component gallery outputs

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`
- `artifacts/m8m/component-gallery-msdf-proof.png`
- `artifacts/m9e/component-gallery-direct-outline-text-proof.png`
- `artifacts/m9e/component-gallery-text-backend-comparison.png`
- `artifacts/m9e/direct-outline-static-text-proof.png`
- `artifacts/m9g/component-gallery-direct-outline-text-layout-proof.png`
- `artifacts/m9g/direct-outline-text-box-layout-proof.png`
- `artifacts/m9g/direct-outline-text-alignment-grid.png`
- `artifacts/m9g/font-diagnostic-export-manifest.txt`
- `artifacts/m9g/font-diagnostic-export-manifest.json`
- `artifacts/m9h/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-layout-grid.png`
- `artifacts/m9h/font-diagnostic-export-manifest.txt`
- `artifacts/m9h/font-diagnostic-export-manifest.json`

No automated pixel comparison runs against these files yet. M7e documents the current stable baseline and its limitations without changing that policy.

## Current M8l proof outputs

- `artifacts/m8l/msdf-machina.ppm`
- `artifacts/m8l/msdf-aa0.ppm`
- `artifacts/m8l/msdf-a-space-a.ppm`
- `artifacts/m8l/msdf-machina-0.ppm`
- `artifacts/m8l/msdf-hello-machina.ppm`

## Current M8n proof outputs

- `artifacts/m8n/msdf-machina.ppm`
- `artifacts/m8n/msdf-aa0.ppm`
- `artifacts/m8n/msdf-a-space-a.ppm`
- `artifacts/m8n/msdf-machina-0.ppm`
- `artifacts/m8n/msdf-hello-machina.ppm`
- `artifacts/m8n/msdf-av-to-wa.ppm`
- `artifacts/m8n/msdf-spacing-proof.ppm`
- `artifacts/m8n/component-gallery-msdf-proof.png`

## Current M8o reference-oracle outputs

- `artifacts/m8o/reference-machina.png`
- `artifacts/m8o/reference-hello-machina.png`
- `artifacts/m8o/reference-kerning.png`
- `artifacts/m8o/reference-aa0.png`
- `artifacts/m8o/reference-a-space-a.png`
- `artifacts/m8o/machina-msdf-machina.ppm`
- `artifacts/m8o/machina-msdf-machina.png`
- `artifacts/m8o/machina-msdf-hello-machina.ppm`
- `artifacts/m8o/machina-msdf-hello-machina.png`
- `artifacts/m8o/machina-msdf-kerning.ppm`
- `artifacts/m8o/machina-msdf-kerning.png`
- `artifacts/m8o/compare-machina.png`
- `artifacts/m8o/compare-hello-machina.png`
- `artifacts/m8o/compare-kerning.png`
- `artifacts/m8o/glyph-placement-report.txt`
- `artifacts/m8o/glyph-placement-report.json`

These remain local proof artifacts for CPU MSDF audit work. They do not imply `TextBlock` integration, Standard text migration, production renderer integration, shaping adoption, or a committed golden-image baseline.

The M8m gallery proof PNG is also a local audit artifact only. It does not imply production `TextBlock` migration, renderer integration, Vulkan/Aurelian work, or a committed golden-image baseline.

## Current M8q vertical-metrics outputs

- `artifacts/m8q/browser-text-metrics.json`
- `artifacts/m8q/reference-machina.png`
- `artifacts/m8q/reference-hello-machina.png`
- `artifacts/m8q/reference-kerning.png`
- `artifacts/m8q/reference-aa0.png`
- `artifacts/m8q/reference-a-space-a.png`
- `artifacts/m8q/machina-msdf-machina.ppm`
- `artifacts/m8q/machina-msdf-machina.png`
- `artifacts/m8q/machina-msdf-hello-machina.ppm`
- `artifacts/m8q/machina-msdf-hello-machina.png`
- `artifacts/m8q/machina-msdf-kerning.ppm`
- `artifacts/m8q/machina-msdf-kerning.png`
- `artifacts/m8q/compare-machina.png`
- `artifacts/m8q/compare-hello-machina.png`
- `artifacts/m8q/compare-kerning.png`
- `artifacts/m8q/glyph-placement-report.txt`
- `artifacts/m8q/glyph-placement-report.json`
- `artifacts/m8q/component-gallery-msdf-proof.png`

These remain local proof artifacts for browser/Machina vertical-metrics audit work only. They do not imply runtime browser dependency, `TextBlock` integration, Standard text migration, or production renderer integration.

## Current M8q.1 baseline-rounding outputs

- `artifacts/m8q1/browser-text-metrics.json`
- `artifacts/m8q1/reference-machina.png`
- `artifacts/m8q1/reference-hello-machina.png`
- `artifacts/m8q1/reference-kerning.png`
- `artifacts/m8q1/reference-aa0.png`
- `artifacts/m8q1/reference-a-space-a.png`
- `artifacts/m8q1/machina-msdf-machina.ppm`
- `artifacts/m8q1/machina-msdf-machina.png`
- `artifacts/m8q1/machina-msdf-hello-machina.ppm`
- `artifacts/m8q1/machina-msdf-hello-machina.png`
- `artifacts/m8q1/machina-msdf-kerning.ppm`
- `artifacts/m8q1/machina-msdf-kerning.png`
- `artifacts/m8q1/compare-machina.png`
- `artifacts/m8q1/compare-hello-machina.png`
- `artifacts/m8q1/compare-kerning.png`
- `artifacts/m8q1/glyph-placement-report.txt`
- `artifacts/m8q1/glyph-placement-report.json`
- `artifacts/m8q1/component-gallery-msdf-proof.png`

These remain local proof artifacts for CPU MSDF baseline-rounding audit work only. They do not imply runtime browser dependency, `TextBlock` integration, Standard text migration, or production renderer integration.

## Current M8q.2 baseline-guide overlay outputs

- `artifacts/m8q2/browser-text-metrics.json`
- `artifacts/m8q2/reference-machina.png`
- `artifacts/m8q2/reference-hello-machina.png`
- `artifacts/m8q2/reference-kerning.png`
- `artifacts/m8q2/reference-aa0.png`
- `artifacts/m8q2/reference-a-space-a.png`
- `artifacts/m8q2/machina-msdf-machina.ppm`
- `artifacts/m8q2/machina-msdf-machina.png`
- `artifacts/m8q2/machina-msdf-hello-machina.ppm`
- `artifacts/m8q2/machina-msdf-hello-machina.png`
- `artifacts/m8q2/machina-msdf-kerning.ppm`
- `artifacts/m8q2/machina-msdf-kerning.png`
- `artifacts/m8q2/compare-machina.png`
- `artifacts/m8q2/compare-hello-machina.png`
- `artifacts/m8q2/compare-kerning.png`
- `artifacts/m8q2/glyph-placement-report.txt`
- `artifacts/m8q2/glyph-placement-report.json`
- `artifacts/m8q2/component-gallery-msdf-proof.png`

These remain local proof artifacts for baseline-visualization audit work only. The red line is a tooling overlay for visual diagnosis, not a rendering fix, and no production text path changed.

## Current M8r browser-vs-Machina overlay diff outputs

- `artifacts/m8r/browser-machina.png`
- `artifacts/m8r/browser-hello-machina.png`
- `artifacts/m8r/browser-kerning.png`
- `artifacts/m8r/browser-aa0.png`
- `artifacts/m8r/browser-a-space-a.png`
- `artifacts/m8r/machina-msdf-machina.png`
- `artifacts/m8r/machina-msdf-hello-machina.png`
- `artifacts/m8r/machina-msdf-kerning.png`
- `artifacts/m8r/overlay-machina.png`
- `artifacts/m8r/overlay-hello-machina.png`
- `artifacts/m8r/overlay-kerning.png`
- `artifacts/m8r/diff-machina.png`
- `artifacts/m8r/diff-hello-machina.png`
- `artifacts/m8r/diff-kerning.png`
- `artifacts/m8r/diff-threshold-machina.png`
- `artifacts/m8r/diff-threshold-hello-machina.png`
- `artifacts/m8r/diff-threshold-kerning.png`
- `artifacts/m8r/wireframe-machina.png`
- `artifacts/m8r/wireframe-hello-machina.png`
- `artifacts/m8r/wireframe-kerning.png`
- `artifacts/m8r/compare-machina.png`
- `artifacts/m8r/compare-hello-machina.png`
- `artifacts/m8r/compare-kerning.png`
- `artifacts/m8r/diff-report.txt`
- `artifacts/m8r/diff-report.json`
- `artifacts/m8r/glyph-placement-report.txt`
- `artifacts/m8r/glyph-placement-report.json`

These remain local diagnostic artifacts only. M8r makes mismatch explicit with overlays and metrics, but it does not apply another rendering fix, add a CI pixel gate, or introduce production text integration.

## Current M8s three-way shape-diff outputs

- `artifacts/m8s/browser-shape-diff-captures.json`
- `artifacts/m8s/shape-diff-report.txt`
- `artifacts/m8s/shape-diff-report.json`
- `artifacts/m8s/32/browser-machina.png`
- `artifacts/m8s/32/direct-outline-machina.png`
- `artifacts/m8s/32/msdf-machina.png`
- `artifacts/m8s/32/diff-browser-vs-direct-machina.png`
- `artifacts/m8s/32/diff-direct-vs-msdf-machina.png`
- `artifacts/m8s/32/diff-browser-vs-msdf-machina.png`
- `artifacts/m8s/32/overlay-three-way-machina.png`
- `artifacts/m8s/32/wireframe-machina.png`
- `artifacts/m8s/48/...`
- `artifacts/m8s/64/...`

These remain local diagnostic artifacts only. M8s adds a direct-outline mask oracle, multi-size numeric shape-diff metrics, and three-way overlays without changing MSDF sampling, baseline placement, kerning, `GlyphFieldPlacement`, or any production text path.

## Current M9d consolidated font toolkit outputs

- `artifacts/m9d/32/direct-outline-hello-machina.png`
- `artifacts/m9d/32/m9d-cad-debug-hello-machina.png`
- `artifacts/m9d/32/m9d-direct-vs-msdf-hello-machina.png`
- `artifacts/m9d/shape-diff-report.txt`
- `artifacts/m9d/shape-diff-report.json`
- `artifacts/m9d/font-diagnostic-export-manifest.txt`
- `artifacts/m9d/font-diagnostic-export-manifest.json`

These remain local diagnostic artifacts only. M9d makes direct-outline the default static proof backend, keeps MSDF explicit as scalable/experimental, records backend policy in the manifest, and does not change production text behavior.

## Current M9f MSDF alignment-repair outputs

- `artifacts/m9f/m9f-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-settings.png`
- `artifacts/m9f/m9f-msdf-debug-hello-machina.png`
- `artifacts/m9f/m9f-cad-debug-hello-machina.png`
- `artifacts/m9f/m9f-before-after-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/shape-diff-report.txt`
- `artifacts/m9f/shape-diff-report.json`
- `artifacts/m9f/font-diagnostic-export-manifest.txt`
- `artifacts/m9f/font-diagnostic-export-manifest.json`
- `artifacts/m9f/msdf-alignment-report.txt`
- `artifacts/m9f/msdf-alignment-report.json`

These remain local diagnostic artifacts only. M9f repairs MSDF-side alignment against the direct-outline oracle, keeps MSDF explicit experimental/scalable, uses no arbitrary visual offsets, and does not change production UI text behavior.

## Current M9e direct-outline gallery proof outputs

- `artifacts/m9e/component-gallery-direct-outline-text-proof.png`
- `artifacts/m9e/component-gallery-text-backend-comparison.png`
- `artifacts/m9e/direct-outline-static-text-proof.png`

These remain local proof artifacts only. M9e proves direct-outline static UI-ish text inside the component gallery through an explicit opt-in sample path. It does not switch production UI text defaults, does not change `Standard.Text`, and does not promote MSDF beyond explicit experimental comparison.

## Current M9g direct-outline text-layout proof outputs

- `artifacts/m9g/component-gallery-direct-outline-text-layout-proof.png`
- `artifacts/m9g/direct-outline-text-box-layout-proof.png`
- `artifacts/m9g/direct-outline-text-alignment-grid.png`
- `artifacts/m9g/font-diagnostic-export-manifest.txt`
- `artifacts/m9g/font-diagnostic-export-manifest.json`

These remain local proof artifacts only. M9g adds a deterministic `DirectOutlineStatic` text-in-rect layout contract with padding, alignment, clipping, line boxes, and explicit newline support for gallery/tooling proof work. It does not change production UI text behavior and does not promote MSDF beyond explicit experimental/scalable comparison.

## Current M9h direct-outline render-bridge proof outputs

- `artifacts/m9h/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-layout-grid.png`
- `artifacts/m9h/font-diagnostic-export-manifest.txt`
- `artifacts/m9h/font-diagnostic-export-manifest.json`

These remain local proof artifacts only. M9h adds a renderer-facing bridge contract for direct-outline static text and an opt-in gallery proof that exercises it. It does not change the production UI text default, and `Machina.Fonts.Tooling` remains out of production package dependencies.
