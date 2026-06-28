# Artifacts

This directory holds deterministic render and visual-audit artifacts for Machina milestones.

## Policy

- Checked-in artifacts may exist for historical milestone notes, render-contract documentation, or tiny golden references.
- `artifacts/m7e/` is the current component-gallery export output directory.
- Current gallery artifacts are generated locally by script/command and are ignored by Git for now.
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

## Current component gallery outputs

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`

No automated pixel comparison runs against these files yet. M7e documents the current stable baseline and its limitations without changing that policy.
