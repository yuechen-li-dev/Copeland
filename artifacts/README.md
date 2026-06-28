# Artifacts

This directory holds deterministic render and visual-audit artifacts for Machina milestones.

## Policy

- Checked-in artifacts may exist for historical milestone notes, render-contract documentation, or tiny golden references.
- `artifacts/m7b/` is the current component-gallery export output directory.
- M7b gallery artifacts are generated locally by script/command and are ignored by Git for now.
- These files are visual audit aids, not an automated pixel-diff baseline gate.

## Regenerating the component gallery artifacts

From the repo root:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
```

Manual fallback:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-default
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
```

## Current component gallery outputs

- `artifacts/m7b/component-gallery-default.png`
- `artifacts/m7b/component-gallery-interactive.png`

No automated pixel comparison runs against these files yet. Future M7c/M7d work may add comparison policy after the export contract stays stable for a while.
