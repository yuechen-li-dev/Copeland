# A0 Aurelian Bootstrap Report

## 1. Files changed

A0 created the root solution/build discipline files, initial Aurelian source and test projects, architecture documentation, helper scripts, and this audit report.

Key files and directories created:

- `Aurelian.slnx`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `README.md`
- `build/aurelian-build.sh`
- `build/aurelian-test.sh`
- `docs/architecture/aurelian-charter.md`
- `docs/architecture/vendor-strategy.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/audits/0000-a0-aurelian-bootstrap.md`
- `src/Aurelian.*`
- `tests/Aurelian.*`

## 2. Task scope

A0 bootstraps the greenfield Aurelian C# engine/runtime skeleton only. It intentionally does not vendor Dominatus, implement rendering, open a window, draw a triangle, port Stride graphics, port SDSL-V, port the TOML asset pipeline, integrate Machina, modify reference code, or migrate Stri-V salvage projects.

## 3. Repository structure created

A0 created production projects under:

```text
src/Aurelian.Core/
src/Aurelian.Actuation/
src/Aurelian.World/
src/Aurelian.Runtime/
src/Aurelian.Rendering.Contracts/
```

A0 created test projects under:

```text
tests/Aurelian.Core.Tests/
tests/Aurelian.Actuation.Tests/
tests/Aurelian.World.Tests/
tests/Aurelian.Runtime.Tests/
tests/Aurelian.Rendering.Contracts.Tests/
```

A0 created architecture and audit documentation under:

```text
docs/architecture/
docs/audits/
```

## 4. Projects created

Production projects:

- `Aurelian.Core`
- `Aurelian.Actuation`
- `Aurelian.World`
- `Aurelian.Runtime`
- `Aurelian.Rendering.Contracts`

Test projects:

- `Aurelian.Core.Tests`
- `Aurelian.Actuation.Tests`
- `Aurelian.World.Tests`
- `Aurelian.Runtime.Tests`
- `Aurelian.Rendering.Contracts.Tests`

Project reference shape:

- `Aurelian.Core` has no project references.
- `Aurelian.Actuation` references `Aurelian.Core`.
- `Aurelian.World` references `Aurelian.Core`.
- `Aurelian.Runtime` references `Aurelian.Core`, `Aurelian.Actuation`, and `Aurelian.World`.
- `Aurelian.Rendering.Contracts` references `Aurelian.Core`.
- Each test project references only its corresponding production project.

## 5. Solution format

The installed .NET SDK supports solution XML (`.slnx`), so A0 created:

```text
Aurelian.slnx
```

Only Aurelian production and test projects were added to the solution.

## 6. Build discipline

A0 created `global.json` pinned to the locally installed .NET SDK `10.0.300` with feature roll-forward enabled.

A0 created `Directory.Build.props` with:

- nullable reference types enabled;
- implicit usings enabled;
- deterministic builds enabled;
- warnings treated as errors;
- latest analysis level;
- latest C# language version.

A0 created `Directory.Packages.props` with central package management and test-only package versions for:

- `Microsoft.NET.Test.Sdk`
- `xunit`
- `xunit.runner.visualstudio`
- `coverlet.collector`

No graphics, windowing, Dominatus, Stride, Machina, WyrmCoil, or asset/shader pipeline packages were added.

## 7. Reference/vendor boundaries

Initial inspection found no root solution, no root `global.json`, no root `Directory.Build.props`, and no root `Directory.Packages.props`.

Initial inspection found existing project files in reference and salvage areas only:

- `CodeReferences/Machina/*/*.csproj`
- `CodeReferences/Stride/*/*.csproj`
- `src/StriV.AssetPipeline/StriV.AssetPipeline.csproj`
- `src/StriV.AssetTool/StriV.AssetTool.csproj`
- `src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj`

Initial inspection found these exact top-level reference folders:

- `CodeReferences/Machina`
- `CodeReferences/Stride`
- `CodeReferences/WyrmCoil`
- `CodeReferences/oct`

A0 did not add any `CodeReferences/*` project to `Aurelian.slnx`. A0 did not add any `src/StriV.*` project to `Aurelian.slnx`. A0 did not add Dominatus references. Dominatus remains planned for `vendor/Dominatus/` in A1.

## 8. Tests added

A0 added smoke tests for:

- `AurelianProject.Name`
- `EntityId`
- `ActId`
- `WorldClock`
- `AurelianRuntime.Tick()`
- `RenderFrameId`

## 9. Validation results

Validation commands run successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
git status --short
```

Build completed with 0 warnings and 0 errors. Test execution completed with all smoke tests passing.

## 10. Next recommendation

A1 — Vendor Dominatus runtime smoke

A1 should:

- vendor Dominatus under `vendor/Dominatus/`;
- add buildable Dominatus projects to solution;
- add first Dominatus runtime smoke in `Aurelian.Runtime`;
- keep renderer out of scope.
