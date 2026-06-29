# Machina Test Suite Topology M11b

## Purpose

M11b cleans up Machina test topology so the normal development loop stays fast while the expensive proof/export diagnostics remain available on purpose.

This milestone is test infrastructure only:

- split fast tooling tests from slow full-pipeline tooling tests
- keep pure unit coverage in the regular solution
- keep real export/MSDF/visual proof coverage in the slow solution
- document script smoke validation as intentional slow/manual workflow

## Problem

Before M11b, `tests/Machina.Fonts.Tooling.Tests` mixed:

- in-memory layer/compositor/boundary tests
- pure manifest/source/preset policy assertions
- full font diagnostic export integration tests
- script-style smoke workflows

That made `Copeland.Slow.slnx` directionally correct but too coarse. Ordinary development lost access to fast tooling coverage, while expensive export work was repeated more than necessary.

## Test categories

M11b defines three categories:

1. fast suite
2. slow suite
3. script/artifact smoke validation

## Fast suite

Command:

```powershell
dotnet test Copeland.slnx
```

Includes:

- core unit tests
- presenter sample tests
- component gallery sample tests
- font unit tests
- `tests/Machina.Fonts.Tooling.Unit.Tests`
- pure layer/compositor/preset/boundary tests
- pure output-cleaner/source-availability/preset-requirement/manifest-builder tests

Avoids:

- repeated full diagnostic export loops
- MSDF before/after proof exports
- large artifact smoke workflows

## Slow suite

Command:

```powershell
dotnet test Copeland.Slow.slnx
```

Includes:

- `tests/Machina.Fonts.Tooling.Tests`
- real font diagnostic export integration tests
- MSDF alignment regression tests
- determinism coverage for representative export outputs
- explicit smoke tests used by the export PowerShell scripts

## Script/artifact smoke validation

Commands:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 ...
.\tools\Export-MachinaPresenter.ps1 ...
.\tools\Export-MachinaComponentGallery.ps1 ...
```

These are documented as milestone/CI smoke workflows, not ordinary fast-loop unit tests.

For font diagnostics and M9f alignment, the scripts now target explicit slow smoke tests instead of mixed-in ordinary test names.

## Font tooling test split

Fast project:

- `tests/Machina.Fonts.Tooling.Unit.Tests`
- `LayerCompositionTests`
- `LayerCompositorTests`
- `LayerPresetTests`
- `RenderBridgeBoundaryTests`
- pure cleaner/source/preset/manifest tests

Slow project:

- `tests/Machina.Fonts.Tooling.Tests`
- `FontDiagnosticExportTests`
- `MsdfAlignmentRegressionTests`
- `FontDiagnosticExportSmokeTests`
- `MsdfAlignmentSmokeTests`

## MSDF alignment fixture

M11b adds `MsdfAlignmentExportFixture`.

- before/after export work now runs once per test class
- regression assertions share the same expensive export pair
- artifact existence checks also reuse that fixture output

## Determinism test policy

`FontDiagnosticsExport_IsDeterministic` stays in the slow suite, but now uses a minimal export configuration and compares:

- manifest JSON
- shape-diff report JSON
- one representative PNG

This keeps determinism coverage without re-comparing every output file from a broad matrix.

## Commands

Fast loop:

```powershell
dotnet test Copeland.slnx
```

Slow validation:

```powershell
dotnet test Copeland.Slow.slnx
```

Additional validation:

```powershell
dotnet build Copeland.slnx --no-restore
dotnet test tests/Machina.Presenter.Sample.Tests/Machina.Presenter.Sample.Tests.csproj
dotnet test tests/Machina.ComponentGallery.Sample.Tests/Machina.ComponentGallery.Sample.Tests.csproj
dotnet test tests/Machina.Fonts.Tests/Machina.Fonts.Tests.csproj
dotnet test tests/Machina.Fonts.Tooling.Unit.Tests/Machina.Fonts.Tooling.Unit.Tests.csproj
dotnet test tests/Machina.Fonts.Tooling.Tests/Machina.Fonts.Tooling.Tests.csproj
```

Smoke examples:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m11b-smoke-fonts -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11b-smoke\presenter-oblivion-cards.png -SelectedSection oblivion -SelectedTab cards
```

## What changed

- added `tests/Machina.Fonts.Tooling.Unit.Tests`
- moved fast tooling tests back into `Copeland.slnx`
- kept slow export/MSDF/smoke tests in `Copeland.Slow.slnx`
- extracted pure helper logic from `FontDiagnosticArtifactExporter`
- reduced slow export defaults for ordinary integration assertions
- added a shared MSDF before/after fixture
- reclassified script workflows as explicit smoke tests

## What did not change

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` runtime execution feature
- no notebook runtime behavior
- no font rendering behavior change
- no `DirectOutlineStatic` / MSDF policy change
- no presenter shell/workbench behavior change beyond docs and test organization

## Deferred work

- real `[Fact]` / `[Theory]` execution remains deferred to M12 or later
- notebook/runtime execution remains deferred
- any production text integration work remains outside M11b

## M11c note

M11b remains test-topology work only, but M11c adds more presenter fast-loop coverage on top of that topology:

- explicit scrollbar interaction-state tests
- cached composition counter tests
- compose/blit geometry tests

`[Fact]` / `[Theory]` execution as notebook/runtime behavior is still deferred to M12+; these are ordinary test-suite validations only.

## M11d note

M11d builds on this split without changing its intent:

- persistence roundtrip and loader validation tests stay in the fast presenter sample suite
- no Roslyn execution is added
- no xUnit notebook/runtime execution is added
- execution remains deferred to M12+
