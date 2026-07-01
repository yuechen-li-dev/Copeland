# A42 — SDSL-V -> HLSL -> SPIR-V shader artifact M0

## 1. Files changed

- Added `src/Aurelian.Shaders/Language/Artifacts/SdslvSpirv/` with the SDSL-V SPIR-V artifact model, diagnostics, M0 stage extraction, emitter, status enum, and deterministic JSON writer.
- Updated `src/Aurelian.Shaders/Language/Emission/Hlsl/HlslEmitter.cs` with the minimal DXC-friendly smoke-path semantic conventions required by A42.
- Added `tests/Aurelian.Shaders.Tests/SdslvSpirvArtifactM0Tests.cs` and updated the HLSL emission smoke expectation for semantic-bearing fields.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/sdslv-compatibility-matrix.md` with the A42 boundary and deferred-work notes.

## 2. Task scope

A42 is shader/compiler-side only. It connects the existing SDSL-V parser, validator, and HLSL emitter to the A41 HLSL-to-SPIR-V artifact layer without adding graphics runtime, Vulkan pipeline, asset/TOML, swapchain, rendering, or direct SDSL-V-to-SPIR-V integration.

## 3. SDSL-V artifact pipeline

The new public entry point is `SdslvSpirvShaderArtifactEmitter.EmitFromSource(string sourceText, string sourceName)`. The path is:

```text
.sdslv source
  -> SdslvParser.ParseModule
  -> SdslvValidator.ValidateModule
  -> HlslEmitter.EmitModule
  -> SdslvStageExtraction.ExtractM0Stages
  -> SpirvShaderArtifactEmitter.EmitFromHlslStages
  -> SdslvSpirvShaderArtifact + JSON manifest
```

Parse, validation, HLSL emission, stage extraction, DXC unavailable, and DXC failure cases are surfaced as A42 diagnostics with `ASV100x` codes.

## 4. HLSL emission/stage extraction

Stage extraction is intentionally convention-based M0. Generated HLSL must contain `VSMain` and `PSMain`; both stages currently use the same HLSL module text with separate entry points and shader profiles.

A42 chooses `vs_6_0` and `ps_6_0` because the existing A40/A41 DXC fixtures and tests already use those profiles successfully. Later work can raise profile versions after compatibility is proven across packaged and PATH DXC variants.

The smoke fixture now emits DXC-friendly semantics through temporary M0 conventions:

- `VertexInput.Position -> POSITION`
- `VertexOutput.Position -> SV_Position`
- `Color -> COLOR0`
- `PSMain` `float4` return -> `SV_Target0`

Explicit SDSL-V semantic annotations remain deferred.

## 5. DXC/SPIR-V integration

A42 reuses `SpirvShaderArtifactEmitter.EmitFromHlslStages(...)` and therefore reuses the A40 DXC subprocess compiler path through `Microsoft.Direct3D.DXC` or PATH discovery. DXC unavailable returns an artifact with unavailable diagnostics rather than failing normal tests.

## 6. Hashing/reproducibility

The SDSL-V artifact records a lowercase SHA-256 hash over the UTF-8 source text. Nested SPIR-V artifacts retain the A41 HLSL source hashes and SPIR-V byte hashes.

## 7. JSON manifest behavior

`SdslvSpirvShaderArtifactJsonWriter.Write(...)` writes indented deterministic JSON with a trailing newline. It includes format version, language, success, source name, source SHA-256, generated HLSL, an inline nested SPIR-V artifact object, and diagnostics.

## 8. Tests added

`SdslvSpirvArtifactM0Tests` covers invalid SDSL-V rejection, DXC-unavailable behavior, DXC-available vertex/fragment SPIR-V generation, stable hashes, deterministic JSON, and the no-graphics-runtime dependency boundary.

## 9. Boundary checks

Boundary checks confirm the implementation remains in `Aurelian.Shaders`, does not add an `Aurelian.Graphics` dependency, does not use `Vortice.Dxc`, and does not place shader artifact code in runtime graphics projects.

## 10. Validation results

Validation commands run for A42:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj -c Debug
rg -n "Aurelian.Graphics|Silk|Vulkan|Vk|CreateShaderModule|GraphicsPipeline" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Microsoft.Direct3D.DXC|Vortice.Dxc|Vortice|DXC|Dxc|dxcompiler|dxc" Directory.Packages.props src tests -g '*.props' -g '*.csproj' -g '*.cs' || true
rg -n "SdslvSpirvShaderArtifact|SpirvShaderArtifact|HlslShaderStageSource|DxcSpirvCompiler" src/Aurelian.Graphics src/Aurelian.Runtime src/Aurelian.World src/Aurelian.Rendering.Contracts src/Aurelian.Rendering.Null -g '*.cs' -g '*.csproj' || true
git status --short
```

## 11. Deferred features

- Real SDSL-V semantic annotations and reflection metadata.
- Arbitrary stage discovery beyond `VSMain`/`PSMain`.
- Asset/TOML shader artifact bridge.
- Graphics pipeline consumption of artifact bytes.
- Direct SDSL-V -> SPIR-V generation.
- Runtime DXC dependency.

## 12. Next recommendation

A43 — Pipeline consumes SPIR-V artifact M0.

This is the next convergence point because A42 now produces shader artifacts with stage bytes, hashes, and entry metadata, while graphics pipeline work should consume SPIR-V bytes without importing compiler dependencies.
