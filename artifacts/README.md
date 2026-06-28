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
- Current gallery artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8l proof artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8n proof artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8o comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8q comparison artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8q.1 comparison artifacts are generated locally by script/command and are ignored by Git for now.
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

## Current component gallery outputs

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`
- `artifacts/m8m/component-gallery-msdf-proof.png`

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
