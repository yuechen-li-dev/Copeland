# Aurelian Build Topology M13b

## Purpose

M13b stabilizes the imported Aurelian solution and dependency topology without performing runtime integration. The goal is to keep `Aurelian.slnx` separate, remove stale import-era paths, and align Aurelian's active Dominatus dependency usage with the rest of the repo.

## Starting point

Before M13b:

- `Aurelian.slnx` referenced a missing sample project, `samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj`.
- `Aurelian.slnx` also referenced missing `vendor/Dominatus/src/...` projects.
- `src/Aurelian.Runtime/Aurelian.Runtime.csproj` project-referenced `../../vendor/Dominatus/src/Dominatus.Core/Dominatus.Core.csproj`.
- imported Aurelian package references for `System.CommandLine`, `Microsoft.Direct3D.DXC`, and Silk.NET packages were not centrally versioned in the root `Directory.Packages.props`.

## Dominatus dependency policy

Aurelian now follows the same active dependency doctrine as Machina:

- active Dominatus usage goes through NuGet packages
- `reference/dominatus` is reference-only
- `vendor/Dominatus` is not used
- Aurelian projects do not project-reference `reference/dominatus` or `vendor/Dominatus`

M13b specifically retargets `Aurelian.Runtime` to `PackageReference Include="Dominatus.Core"` with the centrally managed `0.4.0` version already used elsewhere in the repo.

## Solution topology

The current intended solution layout is:

```text
Copeland.slnx
  existing fast Copeland/Machina/Oblivion solution

Copeland.Slow.slnx
  existing slow/proof solution

Aurelian.slnx
  imported Aurelian solution, stabilized separately
```

M13b does not merge Aurelian into `Copeland.slnx`.

## Project reference cleanup

M13b makes these topology fixes:

- removes the missing `samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj` entry from `Aurelian.slnx`
- removes stale `vendor/Dominatus/src/...` solution entries from `Aurelian.slnx`
- replaces the `Aurelian.Runtime` project reference to `vendor/Dominatus` with a NuGet package reference to `Dominatus.Core`

The visible-triangle sample remains deferred rather than recreated as a placeholder in this milestone.

## Central package versions

M13b adds the missing central package versions needed by the imported Aurelian projects:

- `System.CommandLine` `2.0.9`
- `Microsoft.Direct3D.DXC` `1.9.2602.24`
- `Silk.NET.Vulkan` `2.23.0`
- `Silk.NET.Vulkan.Extensions.KHR` `2.23.0`
- `Silk.NET.Windowing` `2.23.0`

Existing central versions for `Dominatus.Core`, `Dominatus.OptFlow`, `Tomlyn`, xUnit packages, and `Microsoft.NET.Test.Sdk` remain in use.

## Build validation

Commands run for M13b:

```powershell
dotnet test Copeland.slnx
dotnet test Copeland.Slow.slnx
dotnet build Copeland.slnx --no-restore
dotnet restore Aurelian.slnx
dotnet build Aurelian.slnx --no-restore
dotnet test Aurelian.slnx --no-build
git diff --check
```

Observed results:

- `dotnet restore Aurelian.slnx`: passed
- `dotnet build Aurelian.slnx --no-restore`: passed
- `dotnet test Aurelian.slnx --no-build`: one remaining non-topology test failure
- `dotnet build Copeland.slnx --no-restore`: passed
- `dotnet test Copeland.Slow.slnx`: passed
- `dotnet test Copeland.slnx`: passed
- `git diff --check`: passed

Boundary checks run:

- `rg -n "vendor[/\\]Dominatus|reference[/\\]dominatus|Dominatus.Core.csproj|Dominatus.OptFlow.csproj" src tests Aurelian.slnx`
- `rg -n "ProjectReference.*reference[/\\]dominatus|ProjectReference.*vendor[/\\]Dominatus" . -g "*.csproj" -g "*.sln" -g "*.slnx" -g "*.props" -g "*.targets"`
- `rg -n "Aurelian.Graphics|Aurelian.Runtime|Aurelian.Vulkan|Vulkan" src/Machina.Core src/Machina.Standard src/Machina.Layout src/Machina.Runtime src/Machina.Pipeline`
- `rg -n "Aurelian" Copeland.slnx Copeland.Slow.slnx`

All four boundary searches returned no matches.

## Remaining blockers

The remaining Aurelian test failure after topology stabilization is not a solution/dependency topology failure:

- `tests/Aurelian.Shaders.Tests/ShaderArtifactFileWriterM0Tests.cs`
  - currently fails on a CRLF-versus-LF expectation for hex SPIR-V text output on Windows

M13c resolves that remaining blocker by normalizing line endings at the test assertion boundary rather than changing shader writer semantics. After that follow-through, `Aurelian.slnx` restore/build/test is test-clean again.

## What changed

- `Aurelian.slnx` cleanup removes stale missing-project entries
- `Aurelian.Runtime` now consumes `Dominatus.Core` from NuGet
- root central package management now covers the imported Aurelian package set
- Aurelian asset and asset-tool projects were validated against the centrally pinned `Tomlyn` and `System.CommandLine` packages during the restore/build pass
- compatibility doc stubs are added at legacy doc paths so existing tests and workspace assets can continue to resolve moved milestone docs

## What did not change

M13b does not:

- merge Aurelian into `Copeland.slnx`
- move SDSL-V into Copeland
- implement `Copeland.Shaders`
- wire Machina to Aurelian
- implement a `Machina.Aurelian` bridge
- change rendering behavior
- change Copeland Markdown semantics
- add Vulkan presenter integration
- rename the repository

## Deferred work

Likely follow-up lanes after M13b:

- `M13c`: docs dogfood and doc-path convergence cleanup
- `M13c`: completed as shader test normalization plus curated Aurelian docs dogfood through the existing Copeland Markdown / Oblivion path; no runtime integration was introduced
- `M13d`: SDSL-V compiler overlap audit and migration doctrine
- `M13e`: define `Copeland.Shaders` target architecture
- `M13f`: tighten Aurelian render-model and null-renderer boundary strategy
- `M13g`: design `Machina.Aurelian` bridge contracts
