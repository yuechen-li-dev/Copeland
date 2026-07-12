# Machina Component Gallery Local Visual Audit M7a

This document remains the historical M7a audit note.

For the current repeatable export contract and artifact policy, use `docs/Machina.UI/history/machina-component-gallery-export-m7b.md`.

## Environment

- OS: Windows 11 x64
- Repo: `C:\Users\yuech\source\repos\Copeland`
- Sample: `samples/Machina.UI/Machina.ComponentGallery.Sample`
- Audit artifacts: `artifacts/m7a`

## Command run

Window launch smoke run:

```powershell
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj
```

Deterministic export:

```powershell
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7a --export-name component-gallery-initial
dotnet run --project samples/Machina.UI/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7a --export-name component-gallery-final --primary-clicks 1 --checkbox on --switch on
```

## Screenshot / inspection method

- Launched the sample window locally on Windows and confirmed startup succeeded.
- Used the built-in deterministic raster export path to write:
  - `artifacts/m7a/component-gallery-initial.ppm`
  - `artifacts/m7a/component-gallery-final.ppm`
- Generated inspection-friendly PNG copies from those exported PPM files:
  - `artifacts/m7a/component-gallery-initial.png`
  - `artifacts/m7a/component-gallery-final.png`
- Visually inspected the final PNG artifact as the reliable local audit artifact.

## Initial observations

- The wall layout was readable and sectioned correctly.
- Checked and unchecked checkbox states were visually distinct.
- Switch on/off states were visually distinct.
- `StandardUI.TextBlock` paragraph and bullet content rendered visibly.
- The initial sample copy exposed a few clipped primitive captions and an input-value glyph issue.

## Fixes applied

- Increased gallery root/section sizes so StandardUI content fit deterministic stack/layout constraints without overlap.
- Split static state examples from interactive probes so the widget wall stayed readable and hit-testable.
- Shortened a few primitive caption strings to avoid sample-only clipping.
- Widened gallery input shells with a local style override for readability.
- Replaced the input example string with renderer-safe text after the current bitmap path displayed `@` poorly.
- Kept the export path deterministic and local by writing `.ppm` directly from the sample and converting copies to `.png` during the audit.

## Deferred issues

- No automated pixel diff workflow exists yet.
- The current bitmap text renderer still has limited glyph coverage and small-text polish.
- Richer typography fidelity and broader glyph support belong to later renderer/text milestones, not M7a.
- The gallery is a fixed-size workbench page; scrolling and resize behavior are still out of scope.

## Final visual status

- All required sections are visible in one deterministic wall.
- Title is visible and not clipped.
- `TextBlock` paragraph, markup, and bullet content are visible.
- Default and variant buttons are readable.
- Checked checkbox mark is visible with contrast.
- Unchecked checkbox remains visibly unchecked.
- Switch off/on states remain distinct.
- Placeholder and value inputs are readable in the gallery sample.
- Card padding reads sensibly.
- Theme probe visibly differs from the default theme section.

M7a therefore has a usable local visual proof path without introducing browser tooling or automated visual diff infrastructure.
