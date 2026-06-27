# Machina Checkbox Checked Visual Fix M6e.1

## Purpose

M6e.1 fixes a real presenter-visible defect where `StandardUI.Checkbox` was logically checked but the checked state was not visibly obvious in the current raster renderer.

## Screenshot-observed defect

- In the local Windows presenter sample, the Email Updates checkbox label showed `Email updates: on`.
- Hit testing and action state still behaved like a checked checkbox.
- The inner checked mark was effectively invisible in the rendered box.

## Root cause

`StandardUI.Checkbox` already emitted a stable inner `mark` rectangle when checked and the render bridge already painted that child after the box shell.

The defect was the default theme color contract:

- checkbox box background defaulted to `theme.Colors.Background` (white)
- checkbox mark defaulted to `theme.Colors.PrimaryForeground` (also white)

So the checked mark existed, had area, and rendered in the correct location, but defaulted to white-on-white and disappeared visually against the box fill.

## Fix

- Changed the default checkbox `MarkColor` from `theme.Colors.PrimaryForeground` to `theme.Colors.Primary`.
- Kept checkbox row ids and geometry stable across checked and unchecked states.
- Kept the existing deterministic inner-square mark shape instead of switching to a text glyph or renderer-specific check path.
- Strengthened Standard and presenter tests to assert visible contrast, not just mark presence.

## Checkbox visual contract

- Unchecked:
  checkbox shell remains visible with box fill + border
  inner mark row still exists for stable geometry, but its fill is transparent
- Checked:
  checkbox shell remains the same size and position
  inner mark is a centered filled square with non-transparent, high-contrast fill
- Checked and unchecked states must keep row identity and geometry stable
- Checkbox visuals must remain renderer-independent and must not depend on label text hacks

## Tests

- `Checkbox_CheckedState_EmitsVisibleMark`
- `Checkbox_UncheckedState_DoesNotEmitVisibleMarkOrUsesTransparentMark`
- `Checkbox_CheckedAndUnchecked_RowShapeStable`
- `PresenterSample_EmailUpdatesChecked_HasVisibleCheckboxMark`
- `PresenterSample_EmailUpdatesUnchecked_HidesCheckboxMark`
- Existing checkbox/presenter geometry and hit-test assertions still run unchanged

## Local visual validation

Validated locally on Windows with:

```powershell
dotnet run --project samples/Machina.Presenter.Sample/Machina.Presenter.Sample.csproj
```

The built executable was also launched for direct local validation:

```powershell
samples\Machina.Presenter.Sample\bin\Debug\net10.0\Machina.Presenter.Sample.exe
```

Capture methods attempted:

- launched `dotnet run` sample locally
- launched the built presenter executable locally
- attempted desktop/window capture with PowerShell + `System.Drawing.Graphics.CopyFromScreen(...)`
- attempted direct window capture with `PrintWindow(...)`
- exported the presenter raster frame to PNG from the same built sample output for reliable visual inspection

Artifacts:

- black `CopyFromScreen` capture:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e1\checkbox-checked-fixed.png`
- black `PrintWindow` capture:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e1\checkbox-checked-fixed-printwindow.png`
- reliable presenter raster export used for inspection:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e1\checkbox-checked-fixed-raster.png`

Inspection result:

- Email Updates renders checked with a dark centered inner square inside the checkbox box.
- The label still reads `Email updates: on`.
- The unchecked Notifications control remains visually off.
- The TextBlock probe remains visible and non-overlapping.

## Deferred issues

- Checkbox still uses a filled inner square rather than a raster check-glyph path. That is intentional for deterministic visibility in the current renderer.
- Richer disabled-state polish and future renderer-specific fidelity improvements remain out of scope for M6e.1.
