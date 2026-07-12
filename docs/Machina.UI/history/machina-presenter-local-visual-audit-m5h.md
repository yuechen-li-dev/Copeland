# Machina Presenter Local Visual Audit M5h

## Environment
- OS: Windows 11 (10.0.26200) x64
- .NET SDK: 10.0.301 (`dotnet --info`), with 8.0.422 and 9.0.315 also installed
- Dominatus package/source: `Dominatus.Core` 0.4.0 and `Dominatus.OptFlow` 0.4.0 from NuGet; vendored `vendor/Dominatus` remains in-repo for historical/reference use but is no longer part of the active Copeland solution build path
- Command run: `dotnet run --project samples/Machina.UI/Machina.Presenter.Sample/Machina.Presenter.Sample.csproj`

## Screenshot / observation method

- Launched the Avalonia presenter sample locally on Windows with `dotnet run`.
- Activated the native window by title (`Machina Presenter M1e - action: startup, count: 0, email: on, notifications: off`).
- Captured the visible window area with a PowerShell `System.Drawing.Graphics.CopyFromScreen(...)` script after foregrounding the sample window.
- Saved screenshots to:
  - `artifacts/m5h/presenter-window-initial.png`
  - `artifacts/m5h/presenter-window-final.png`

## Initial observations

- Root background filled the window correctly.
- Card placement and card inset looked correct.
- Theme contrast was legible overall: dark button, dark title text, muted secondary text, visible checkbox and switch geometry.
- The title text and button label were both visibly using the current bitmap text renderer, which made clipping defects easy to confirm.

## Text defects found

1. Title clipping:
   `Machina Presenter` rendered as truncated text in the initial screenshot because layout measured the text much smaller than the bitmap rasterizer actually drew it.
2. Button label clipping:
   `Increment` was visibly clipped in the initial screenshot for the same reason.
3. Button label alignment:
   After fixing measurement, `Increment` fit but still rendered flush-left inside the button shell because the draw command used the intrinsic text rect instead of the full button label region.
4. Small but acceptable deferred text quality:
   Count text, checkbox/switch labels, and footnote remained readable but are still limited by the current simple bitmap font. This is a renderer capability limitation, not a new geometry bug.

## Fixes applied

1. Replaced vendored Dominatus project references with NuGet package references:
   - `Dominatus.Core` 0.4.0
   - `Dominatus.OptFlow` 0.4.0
2. Removed vendored Dominatus projects from `Machina.UI.slnx` so the active solution build path is NuGet-based.
3. Updated `DeterministicTextMeasurer` to use the same glyph advance/scale math as the current `ReadableBitmapTextRasterizer`.
   - This fixed title and button-label clipping without introducing full M6c measurement/layout work.
4. Updated `MachinaRenderBridge` so centered button labels draw into their `.label-region` rect when present.
   - This fixed the `Increment` horizontal centering defect in the live sample.
5. Refreshed and extended regression tests around:
   - deterministic measurement sizing,
   - button text placement,
   - presenter raw text fit,
   - bridge draw-command rect selection,
   - snapshot/geometry expectations affected by corrected measurement.

## Deferred issues

- No full text measurement/layout engine was introduced.
- No multiline wrapping, ellipsis, paragraph layout, glyph shaping, or dynamic text fitting was added.
- The current bitmap font still produces small, blocky text for secondary labels and footnotes.
- Switch off-state thumb/track contrast remains simple but legible; improving polish there belongs to later visual refinement rather than this milestone.

## Validation results

- Dominatus package discovery:
  - Verified via `dotnet package search Dominatus --source https://api.nuget.org/v3/index.json`
  - Confirmed `Dominatus.Core` 0.4.0 and `Dominatus.OptFlow` 0.4.0 exist on NuGet.
- Machina-focused validation passed:
  - `dotnet restore Machina.UI.slnx`
  - `dotnet build Machina.UI.slnx --no-restore`
  - `dotnet test tests/Machina.UI/Machina.Core.Tests/Machina.Core.Tests.csproj`
  - `dotnet test tests/Machina.UI/Machina.Dominatus.Tests/Machina.Dominatus.Tests.csproj`
  - `dotnet test tests/Machina.UI/Machina.Standard.Tests/Machina.Standard.Tests.csproj`
  - `dotnet test tests/Machina.UI/Machina.Pipeline.Tests/Machina.Pipeline.Tests.csproj`
  - `dotnet test tests/Machina.UI/Machina.Presenter.Sample.Tests/Machina.Presenter.Sample.Tests.csproj`
- Boundary checks:
  - `rg -n "Avalonia|Window|Presenter" src/Machina.UI/Machina.Layout src/Machina.UI/Machina.Core src/Machina.UI/Machina.Standard src/Machina.UI/Machina.Runtime src/Machina.UI/Machina.Dominatus src/Machina.UI/Machina.Renderer.Raster src/Machina.UI/Machina.Renderer.Raster.Text src/Machina.UI/Machina.Renderer.Raster.Dominatus src/Machina.UI/Machina.Pipeline`
  - Result: no matches in source packages; presenter/window references remain confined to the sample host.
  - `rg -n "ProjectReference.*Dominatus|vendored|Vendor|Vended|third_party|submodule|Dominatus" . -g "*.csproj" -g "*.props" -g "*.targets" -g "*.sln" -g "*.slnx" -g "*.md"`
  - Result: Machina project files now use NuGet package references for Dominatus; docs still mention vendored history as expected.
- `git diff --check`
  - Passed.
- Full solution test status:
  - Historical note: during M5h, `dotnet test Machina.UI.slnx` was still blocked by unrelated Windows-sensitive failures in `Copeland.Script.Tests` and `Copeland.Cli.Tests`.
  - Follow-up M5i fixed those repo-wide Windows test issues and restored full solution validation.
  - See [copeland-windows-test-triage-m5i.md](../../Copeland/history/copeland-windows-test-triage-m5i.md).

## Conclusion

M5h achieved the Machina-side goal set:

- Upstream NuGet Dominatus 0.4 works for the Machina integration projects.
- Vendored Dominatus project references were removed from the active Machina build path.
- The presenter sample was run locally on Windows and audited from real screenshots.
- The two screenshot-backed text defects were fixed:
  - title truncation,
  - `Increment` clipping/alignment.

That historical full-solution blocker was unrelated repository-wide Windows test debt, not the Machina M5h changes themselves, and is now resolved by M5i.

## M6d follow-up note

M6d explicitly audits and preserves the M5h text-measurement fix by centralizing deterministic bitmap text measurement across:

- `MachinaTextMeasurers.Deterministic`
- `DeterministicTextMeasurer`
- `ReadableBitmapTextRasterizer.MeasureText(...)`

The rich-text bridge proof in M6d is therefore built on the same measurement reality that fixed the M5h clipping bug, rather than introducing a parallel measurement algorithm.
