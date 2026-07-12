# Machina Component Gallery Export M7b

## Purpose

M7b formalizes the component gallery export path introduced in M7a.

The goal is a boring, repeatable local workflow for producing deterministic raster artifacts from the canonical gallery sample without relying on OS screenshot capture or a visible GUI window.

M7e follow-up note:

- the current default export directory is now `artifacts/m7e`
- the stable baseline and current limitation register live in `docs/Machina.UI/history/machina-component-gallery-known-limitations-m7e.md`

## Export contract

Canonical sample:

- `samples/Machina.UI/Machina.ComponentGallery.Sample`

Current default output directory:

- `artifacts/m7e`

Current canonical export files:

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`

Canonical default-state command:

```powershell
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-default
```

Canonical interactive-state command:

```powershell
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
```

Current default-script command:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
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
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7e
.\tools\Export-MachinaComponentGallery.ps1 -Configuration Debug
```

Behavior:

- resolves the repo root from the script location
- exports default and interactive gallery states
- optionally exports an MSDF proof gallery artifact when `-IncludeMsdfFontProof` is passed
- prints the created file paths
- exits non-zero if an export fails or an expected file is missing

## Manual commands

If PowerShell script execution is blocked by local execution policy, run the sample commands directly:

```powershell
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-default
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7e --export-name component-gallery-interactive --primary-clicks 1 --checkbox on --switch on
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m8m --export-name component-gallery-msdf-proof --include-msdf-font-proof
```

## Artifact policy

- M7b keeps the export docs and script checked in.
- Current gallery outputs under `artifacts/m7e/` are generated local artifacts for now.
- M7b does not introduce an automated pixel-diff gate.
- Historical checked-in artifacts from older milestone notes may remain in the repo.
- Current gallery exports are visual audit aids only.

See `artifacts/README.md` for the local regeneration note.

## Local visual inspection workflow

1. Run `.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7e`.
2. Open `artifacts/m7e/component-gallery-default.png`.
3. Open `artifacts/m7e/component-gallery-interactive.png`.
4. Confirm both files exist and render visible gallery content.
5. Confirm the interactive export differs where expected:
   primary click count advanced,
   checkbox shown checked,
   switch shown on.
6. Confirm text content is visible and there is no obvious overlap or black-frame failure.

This workflow is intentionally local and manual. It does not depend on `PrintWindow`, desktop screenshots, or browser tooling.

## M8m opt-in proof mode

M8m adds a third export mode without changing the default script behavior:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8m -IncludeMsdfFontProof
```

That command still exports the normal default and interactive gallery PNGs, and additionally writes:

- `artifacts/m8m/component-gallery-msdf-proof.png`

The MSDF proof card is experimental:

- it is opt-in only
- it uses the standalone `Machina.Fonts` CPU reference path
- it blits an image into the exported PNG after normal gallery rasterization
- it does not replace `UI.Text`, `StandardUI.TextBlock`, or control labels

See `docs/Machina.UI/history/machina-component-gallery-msdf-proof-m8m.md`.

## Tests

Dedicated gallery sample tests now cover:

- stable default export options
- parsing of the canonical default export command
- parsing of the canonical interactive export state
- headless export file creation into a requested directory

Existing gallery tests still cover document shape, hit targets, geometry stability, theme propagation, and deterministic render-command summaries.

M7e adds a small contract guard for stable export names and default output paths.

## Deferred visual-diff work

M7b intentionally defers:

- automated pixel-diff enforcement
- external image-comparison dependencies
- screenshot capture workflows
- broader component redesign or renderer architecture changes

Future work can add comparison policy after the export contract stays stable and useful in local practice.
