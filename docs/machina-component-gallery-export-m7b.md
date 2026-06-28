# Machina Component Gallery Export M7b

## Purpose

M7b formalizes the component gallery export path introduced in M7a.

The goal is a boring, repeatable local workflow for producing deterministic raster artifacts from the canonical gallery sample without relying on OS screenshot capture or a visible GUI window.

## Export contract

Canonical sample:

- `samples/Machina.ComponentGallery.Sample`

Canonical output directory:

- `artifacts/m7b`

Canonical export files:

- `artifacts/m7b/component-gallery-default.png`
- `artifacts/m7b/component-gallery-interactive.png`

Canonical default-state command:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-default
```

Canonical interactive-state command:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
```

Contract notes:

- export mode writes `.png` directly
- export mode creates the requested output directory deterministically
- export mode does not require a visible window
- export file names are stable and explicit
- the export path still goes through the real Machina raster pipeline

## Script usage

Primary script:

- `tools/Export-MachinaComponentGallery.ps1`

Default usage:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
```

Optional arguments:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7b
.\tools\Export-MachinaComponentGallery.ps1 -Configuration Debug
```

Behavior:

- resolves the repo root from the script location
- exports default and interactive gallery states
- prints the created file paths
- exits non-zero if an export fails or an expected file is missing

## Manual commands

If PowerShell script execution is blocked by local execution policy, run the sample commands directly:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-default
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7b --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
```

## Artifact policy

- M7b keeps the export docs and script checked in.
- M7b treats `artifacts/m7b/` outputs as generated local artifacts for now.
- M7b does not introduce an automated pixel-diff gate.
- Historical checked-in artifacts from older milestone notes may remain in the repo.
- Current gallery exports are visual audit aids only.

See `artifacts/README.md` for the local regeneration note.

## Local visual inspection workflow

1. Run `.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7b`.
2. Open `artifacts/m7b/component-gallery-default.png`.
3. Open `artifacts/m7b/component-gallery-interactive.png`.
4. Confirm both files exist and render visible gallery content.
5. Confirm the interactive export differs where expected:
   primary click count advanced,
   checkbox shown checked,
   switch shown on.
6. Confirm text content is visible and there is no obvious overlap or black-frame failure.

This workflow is intentionally local and manual. It does not depend on `PrintWindow`, desktop screenshots, or browser tooling.

## Tests

Dedicated gallery sample tests now cover:

- stable default export options
- parsing of the canonical default export command
- parsing of the canonical interactive export state
- headless export file creation into a requested directory

Existing gallery tests still cover document shape, hit targets, geometry stability, theme propagation, and deterministic render-command summaries.

## Deferred visual-diff work

M7b intentionally defers:

- automated pixel-diff enforcement
- external image-comparison dependencies
- screenshot capture workflows
- broader component redesign or renderer architecture changes

Future M7c/M7d work can add comparison policy after the export contract stays stable and useful in local practice.
