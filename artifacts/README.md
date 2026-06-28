# Artifacts

This directory holds deterministic render and visual-audit artifacts for Machina milestones.

## Policy

- Checked-in artifacts may exist for historical milestone notes, render-contract documentation, or tiny golden references.
- `artifacts/m7e/` is the current component-gallery export output directory.
- `artifacts/m8m/` is the current opt-in component-gallery MSDF proof output directory.
- `artifacts/m8l/` is the current local CPU MSDF text proof audit output directory.
- Current gallery artifacts are generated locally by script/command and are ignored by Git for now.
- Current M8l proof artifacts are generated locally by script/command and are ignored by Git for now.
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

These remain local proof artifacts for CPU MSDF audit work. They do not imply UI integration, PNG adoption, or a committed golden-image baseline.

The M8m gallery proof PNG is also a local audit artifact only. It does not imply production `TextBlock` migration, renderer integration, Vulkan/Aurelian work, or a committed golden-image baseline.
