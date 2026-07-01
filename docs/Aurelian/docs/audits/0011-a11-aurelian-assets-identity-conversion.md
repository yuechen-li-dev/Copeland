# 0011 — A11 Aurelian assets identity conversion

## 1. Files changed

- Renamed `src/StriV.AssetPipeline/` to `src/Aurelian.Assets/`.
- Renamed `src/StriV.AssetPipeline/StriV.AssetPipeline.csproj` to `src/Aurelian.Assets/Aurelian.Assets.csproj`.
- Renamed `src/StriV.AssetTool/` to `src/Aurelian.AssetTool/`.
- Renamed `src/StriV.AssetTool/StriV.AssetTool.csproj` to `src/Aurelian.AssetTool/Aurelian.AssetTool.csproj`.
- Updated production namespaces and imports under `src/Aurelian.Assets/` and `src/Aurelian.AssetTool/` from Stri-V identities to Aurelian identities.
- Added `tests/Aurelian.Assets.Tests/` smoke tests.
- Added `tests/Aurelian.AssetTool.Tests/` smoke tests.
- Linked the converted projects and new tests in `Aurelian.slnx`.
- Added central package versions for the newly linked carryover dependencies, `Tomlyn` and `System.CommandLine`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/vendor-strategy.md`.

## 2. Task scope

A11 was an identity conversion and solution-integration milestone for the remaining carried-over Stri-V asset modules. It intentionally avoided broad asset-system redesign, Stride asset-system porting, renderer/windowing integration, CodeReferences changes, vendor/Dominatus changes, Machina references, WyrmCoil source references, and new Stride dependencies.

## 3. Existing asset project inspection

The required inspection found no pre-existing `src/Aurelian.Assets` directory to merge. The repository contained the two unlinked carried-over projects:

```text
src/StriV.AssetPipeline/
src/StriV.AssetTool/
```

`StriV.AssetPipeline` contained a single asset pipeline source file and a project file with a `Tomlyn` package reference plus an obsolete `StriV.ShaderPipeline` project reference. `StriV.AssetTool` contained the CLI program, CLI diagnostic formatter, a `System.CommandLine` package reference, and an obsolete `StriV.AssetPipeline` project reference.

## 4. Conversion strategy

Because `src/Aurelian.Assets` did not already exist, A11 used a direct rename strategy:

```text
src/StriV.AssetPipeline -> src/Aurelian.Assets
src/StriV.AssetTool -> src/Aurelian.AssetTool
```

No placeholder merge was required. The carryover code was kept minimal and behavior-preserving except for identity, project-reference, nullable-warning, and obvious CLI branding changes.

## 5. Project rename summary

- `StriV.AssetPipeline` became `Aurelian.Assets`.
- `StriV.AssetTool` became `Aurelian.AssetTool`.
- The old `src/StriV.AssetPipeline` and `src/StriV.AssetTool` directories are gone.
- The converted projects are now linked in `Aurelian.slnx`.

## 6. Namespace/project metadata changes

`src/Aurelian.Assets/Aurelian.Assets.csproj` now declares:

```xml
<AssemblyName>Aurelian.Assets</AssemblyName>
<RootNamespace>Aurelian.Assets</RootNamespace>
<TargetFramework>net10.0</TargetFramework>
```

`src/Aurelian.AssetTool/Aurelian.AssetTool.csproj` now declares:

```xml
<AssemblyName>Aurelian.AssetTool</AssemblyName>
<RootNamespace>Aurelian.AssetTool</RootNamespace>
<TargetFramework>net10.0</TargetFramework>
<OutputType>Exe</OutputType>
```

Production namespaces were converted from:

```text
StriV.AssetPipeline -> Aurelian.Assets
StriV.AssetTool -> Aurelian.AssetTool
```

The obvious root CLI help text was updated from Stri-V branding to Aurelian branding.

## 7. Project reference changes

`Aurelian.Assets` now references:

```text
../Aurelian.Shaders/Aurelian.Shaders.csproj
```

`Aurelian.AssetTool` now references:

```text
../Aurelian.Assets/Aurelian.Assets.csproj
```

The obsolete references to `StriV.ShaderPipeline` and `StriV.AssetPipeline` were removed. No Stride, CodeReferences, Machina, WyrmCoil, or Copeland references were added.

## 8. Tests added/converted

No existing Stri-V asset/tool tests were present. A11 added minimal smoke tests:

```text
tests/Aurelian.Assets.Tests/
tests/Aurelian.AssetTool.Tests/
```

The asset tests verify that the production assembly identity is `Aurelian.Assets`, that a basic TOML shader manifest parses, and that validator accepts a basic manifest record. The CLI tests verify that formatter code loads from `Aurelian.AssetTool` and that text diagnostic formatting remains usable.

## 9. Boundary checks

Boundary checks were run for old Stri-V asset/tool namespaces, obsolete Stri-V project references, forbidden Stride/CodeReferences/Machina/WyrmCoil/Copeland references in the converted asset/tool modules and their tests, and current project references across the solution.

The old Stri-V asset/tool source directories are absent. The only remaining references to `StriV.AssetPipeline` and `StriV.AssetTool` are historical documentation/audit mentions and explicit A11 conversion notes. No forbidden runtime dependencies were found in the converted source or test projects.

## 10. Validation results

Validation commands completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
```

Build and tests pass with nullable and warnings-as-errors enabled.

## 11. Deferred asset/schema work

A11 did not redesign the asset manifest schema, implement a full TOML asset pipeline, port the Stride asset system, introduce editor/Game Studio assumptions, or integrate renderer/windowing. `Aurelian.Assets` is still an early carried-over asset pipeline that needs schema convergence toward Aurelian's TOML/manifest asset direction. `Aurelian.Shaders` remains responsible for SDSL-V parsing, validation, HLSL emission, shader artifacts, and optional DXC validation.

## 12. Next recommendation

A12 — Shader asset manifest bridge M0

A12 should use `Aurelian.Assets` to define a TOML manifest that references `.sdslv` shader sources, call `Aurelian.Shaders` artifact emission, optionally call DXC validation, and produce deterministic artifact JSON/HLSL files. Renderer execution, windowing, and backend integration should remain out of scope.
