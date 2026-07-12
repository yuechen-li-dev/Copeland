# Machina Standard TextBlock Local Visual Audit M6e

## Environment

- OS: Windows 11 (`10.0.26200`)
- .NET SDK used locally: `10.0.301`
- Repo: `C:\Users\yuech\source\repos\Copeland`

## Command run

Primary requested command:

```powershell
dotnet run --project samples/Machina.UI/Machina.Presenter.Sample/Machina.Presenter.Sample.csproj
```

The sample also ran directly from the built Windows executable for a reliable screenshot capture of the same build output:

```powershell
samples\Machina.UI\Machina.Presenter.Sample\bin\Debug\net10.0\Machina.Presenter.Sample.exe
```

## Screenshot / inspection method

- Local Windows app launch
- PowerShell window-rectangle capture via `System.Drawing.Graphics.CopyFromScreen(...)`
- Image inspection through Codex local image viewing

Artifacts:

- Initial local capture before the bullet glyph fix:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e\machina-standard-textblock-m6e.png`
- Final local capture after the bullet glyph fix:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e\machina-standard-textblock-m6e-fixed.png`
- Exact `dotnet run` capture attempt:
  `C:\Users\yuech\source\repos\Copeland\artifacts\m6e\machina-standard-textblock-m6e-dotnet-run.png`

Note:
The `dotnet run` capture artifact was black/unusable even though the sample process launched. The authoritative visual inspection used the direct executable capture from the same built output.

## Initial observations

- The new rich text probe was visible inside the settings card.
- Paragraph text wrapped inside the card content area.
- Existing title, count, button, checkbox, and switch remained visible and did not overlap the probe.
- The rich text probe did not disturb existing primitive `UI.Text` content.
- Bullet lines were present structurally, but the bullet marker rendered as `?` instead of a visible bullet glyph.

## Defects found

1. Bullet glyph fallback in the bitmap text rasterizer.
   The `Machina.Standard.Text` layout output emitted `•`, but the readable bitmap rasterizer did not have a glyph for that character and fell back to `?`.

## Fixes applied

1. Added a deterministic `•` glyph to `ReadableBitmapTextRasterizer`.

## Deferred issues

- Inline `strong`, `emphasis`, and link styling still do not render with distinct visual fidelity in the current bitmap renderer.
- The bitmap text renderer still normalizes visible text to its existing uppercase-style glyph system.
- Ellipsis, scroll, and richer clipping behavior remain deferred.

These are renderer/style-fidelity limitations, not `MachinaTextLayoutEngine` placement failures.

## Final visual status

- `StandardUI.TextBlock` renders visibly in the presenter sample.
- Paragraph and bullet lines are visible.
- Bullet markers render correctly after the rasterizer glyph fix.
- Text stays inside the card’s assigned content area in the audited sample.
- Existing primitive text and standard controls still render correctly.
- No overlap was observed between the rich text probe and the existing controls in the final screenshot.
