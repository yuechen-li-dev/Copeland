# 0010 — A10 DXC optional validation M0

## 1. Files changed

- `src/Aurelian.Shaders/Language/External/Dxc/DxcArtifactValidationResult.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcCommandLineBuilder.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcDiscovery.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcExecutable.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcValidationRequest.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcValidationResult.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcValidationStatus.cs`
- `src/Aurelian.Shaders/Language/External/Dxc/DxcValidator.cs`
- `tests/Aurelian.Shaders.Tests/DxcValidationM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `docs/audits/0010-a10-dxc-optional-validation-m0.md`

## 2. Task scope

A10 adds optional DXC validation M0 for generated HLSL artifacts produced by the new Aurelian SDSL-V C# pipeline:

```text
source -> parse -> validate -> HLSL emit -> artifact/manifest -> optional DXC validation
```

DXC is treated only as external validation. It is not a compiler spine, renderer dependency, asset bridge, or SPIR-V path. Normal build and test commands do not require DXC, the Vulkan SDK, renderer/windowing, old Stride shader compiler behavior, CodeReferences coupling, `.sdslvtest`, or asset/TOML integration.

## 3. DXC model/API

The new namespace is:

```text
Aurelian.Shaders.Language.External.Dxc
```

M0 model types are intentionally small:

- `DxcValidationStatus` has explicit `Succeeded`, `Failed`, `SkippedToolUnavailable`, `SkippedNoEntryPoints`, and `SkippedNoHlsl` states.
- `DxcExecutable` wraps the executable path.
- `DxcValidationRequest` models one HLSL/profile/entry point validation.
- `DxcValidationResult` captures status, entry point, profile, exit code, stdout, stderr, and arguments.
- `DxcArtifactValidationResult` aggregates per-stage results and treats skip statuses as aggregate success so optional-tool absence does not fail normal tests.

## 4. Discovery behavior

`DxcDiscovery.FindDxc()` searches in this order:

1. `AURELIAN_DXC`, when it points to an existing file.
2. `dxc` on `PATH`.
3. `dxc.exe` on `PATH`.

Discovery uses environment variables and file probing only. It does not shell out for discovery and does not throw when DXC or malformed path entries are unavailable. For M0, an invalid `AURELIAN_DXC` path is ignored and discovery returns `null` if no other executable is found.

## 5. Command-line behavior

`DxcCommandLineBuilder.Build(...)` emits M0 validation arguments with:

```text
-T <profile>
-E <entryPoint>
-nHV 2021
-Ges
<input.hlsl>
-Fo <output.bin>
```

The validator writes generated HLSL to a temporary `.hlsl` file and asks DXC to write binary output to a temporary `.bin` file via `-Fo`, avoiding binary stdout pollution. A10 does not pass `-spirv` and does not create persistent output artifacts.

## 6. Validation behavior

`DxcValidator.ValidateHlsl(...)` validates one entry point. It returns:

- `SkippedNoHlsl` when the request HLSL is empty.
- `SkippedNoEntryPoints` when the entry point or profile is missing.
- `SkippedToolUnavailable` when no executable is supplied and discovery fails.
- `Succeeded` when DXC exits with code `0`.
- `Failed` when DXC exits non-zero or a normal process/temp-file I/O failure prevents validation.

`DxcValidator.ValidateArtifact(...)` runs one validation request per artifact stage entry point with a profile. It returns a single skip result for empty HLSL or no usable entry points, and one skipped-unavailable result per stage when DXC is missing. Temporary input/output files are cleaned in all normal paths.

## 7. Tests added

`tests/Aurelian.Shaders.Tests/DxcValidationM0Tests.cs` adds deterministic coverage for:

- `DxcDiscovery_FindDxc_DoesNotThrow`
- `DxcValidator_ValidateHlsl_WithNullExecutable_SkipsToolUnavailable`
- `DxcValidator_ValidateArtifact_WithEmptyHlsl_SkipsNoHlsl`
- `DxcValidator_ValidateArtifact_WithNoEntryPoints_SkipsNoEntryPoints`
- `DxcCommandLineBuilder_BuildsExpectedProfileAndEntryArguments`
- `DxcValidator_ValidateSmokeTriangleArtifact_WhenDxcAvailable_SucceedsOrReportsFailure`
- `DxcValidator_ValidateArtifact_WithNullExecutable_SkipsToolUnavailable`

Tests that require missing DXC temporarily clear `AURELIAN_DXC` and `PATH` inside a local lock, then restore them. The smoke DXC test returns without failure when no DXC is discoverable.

## 8. Optional-tool behavior

DXC remains optional. Missing DXC produces `SkippedToolUnavailable` results instead of exceptions or test failures. Empty generated HLSL and no stage entry points also produce explicit skipped statuses. Aggregate artifact validation treats these skipped statuses as successful optional validation outcomes.

## 9. Boundary checks

Boundary checks confirmed the new C# source and tests do not introduce CodeReferences, Stride, Machina, WyrmCoil, or Copeland production/test coupling. Additional checks confirmed the DXC folder and tests do not introduce renderer/windowing concepts such as Vulkan, graphics devices, windows, swap chains, or render targets.

## 10. Validation results

Validated with:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Vulkan|GraphicsDevice|Window|SwapChain|RenderTarget" src/Aurelian.Shaders/Language/External/Dxc tests/Aurelian.Shaders.Tests -g '*.cs' || true
git status --short
```

Results:

- build passed;
- tests passed without requiring DXC;
- boundary `rg` checks returned no prohibited coupling matches;
- `git status --short` showed only the expected A10 source, test, documentation, and report changes before commit.

## 11. Deferred features

Still deferred after A10:

- SPIR-V output;
- Vulkan SDK dependency;
- renderer/windowing/backend integration;
- asset/TOML bridge;
- `.sdslvtest` runner;
- Oct interpreter or CPU shader evaluator;
- persistent compiled shader output artifacts;
- full shader type checking and interface satisfaction;
- Stride SDSL, mixin, effect, or base-shader compatibility;
- CodeReferences imports or production references.

## 12. Next recommendation

Recommended next milestone:

```text
A11 — Shader asset manifest bridge M0
```

Reason: A9 established deterministic generated shader artifacts and A10 established optional external HLSL validation. The next useful boundary is to connect artifacts to the future asset manifest/TOML layer without adding renderer/backend execution or SPIR-V requirements.
