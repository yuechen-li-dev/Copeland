# A43 — Compiled shader stage contract M0

## 1. Files changed

- Added neutral compiled shader contract DTOs under `src/Aurelian.Rendering.Contracts/Shaders`.
- Added compiled shader artifact exporter types under `src/Aurelian.Shaders/Language/Artifacts/Compiled`.
- Added a project reference from `Aurelian.Shaders` to `Aurelian.Rendering.Contracts`.
- Added compiled shader to Vulkan stage mapping types under `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics`.
- Added contract, shader exporter, and graphics mapper tests.
- Updated roadmap, dependency policy, SDSL-V compatibility, and README documentation.

## 2. Task scope

A43 implements only the compiled shader contract bridge from A42 shader artifacts to A39 Vulkan pipeline stage descriptors. It does not create pipelines from SDSL-V artifacts, does not add asset/TOML integration, and does not add runtime shader compilation.

## 3. Dependency boundary decision

The bridge uses `Aurelian.Rendering.Contracts` as the neutral ownership point. `Aurelian.Shaders` can export into the neutral DTOs, and `Aurelian.Graphics` can consume those DTOs. There is no direct `Aurelian.Graphics <-> Aurelian.Shaders` project reference.

## 4. Neutral compiled shader contract model

`Aurelian.Rendering.Contracts.Shaders` defines:

- `CompiledShaderStageKind` with vertex, fragment, and compute stages;
- `CompiledShaderStage` with entry point, profile, SPIR-V bytes, SPIR-V SHA-256, and source name;
- `CompiledShaderProgram` with `aurelian.compiled-shader-program/0` format versioning;
- small status and diagnostic DTOs for bridge-level results.

The contracts contain no DXC, Vulkan, Silk, shader artifact, or graphics backend references.

## 5. Shader artifact exporter

`CompiledShaderProgramExporter` exports both `SpirvShaderArtifact` and `SdslvSpirvShaderArtifact` values to neutral compiled shader programs. It rejects failed artifacts, missing nested SPIR-V artifacts, empty stage lists, duplicate stages, missing entry points, empty SPIR-V payloads, and invalid hash length. Artifact diagnostics are surfaced as exporter diagnostics instead of requiring graphics types.

## 6. Graphics mapper

`VulkanCompiledShaderStageMapper` maps neutral compiled shader programs to `VulkanShaderStageDescriptor` values. It supports vertex and fragment stages for graphics M0, rejects compute stages, validates non-empty SPIR-V bytes, requires byte lengths to be multiples of four, checks SPIR-V magic number `0x07230203`, and converts little-endian bytes to `uint` words.

## 7. Tests added

- `CompiledShaderContractsM0Tests` verifies neutral DTO storage and reference cleanliness.
- `CompiledShaderProgramExporterM0Tests` verifies failed artifact handling, successful HLSL/SPIR-V export, SDSL-V artifact export, and no graphics runtime dependency.
- `VulkanCompiledShaderStageMapperM0Tests` verifies vertex/fragment mapping, compute rejection, malformed byte rejection, invalid magic rejection, little-endian conversion, and no shader project dependency.

## 8. Boundary checks

Boundary checks were run with ripgrep against source and test projects to confirm:

- rendering contracts do not reference shaders, graphics, Silk/Vulkan, or DXC;
- shader exporter code does not reference graphics or Vulkan runtime types;
- graphics mapper code does not reference shader artifact/compiler types or DXC;
- project references preserve the neutral dependency direction.

## 9. Validation results

Validation result:

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed across the solution.
- `dotnet test tests/Aurelian.Rendering.Contracts.Tests/Aurelian.Rendering.Contracts.Tests.csproj -c Debug` passed with 21 tests.
- `dotnet test tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj -c Debug` passed with 97 tests.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed with 157 tests.
- Source-only boundary scans for forbidden shader/graphics/rendering-contract references returned no hits; broad source+test scans only hit negative assertion tests and expected project references.

## 10. Deferred features

Deferred features:

- pipeline creation directly from compiled shader programs;
- SDSL-V artifact to pipeline descriptor end-to-end API;
- asset/TOML shader artifact integration;
- runtime shader compilation;
- descriptor sets, uniform buffers, push constants, and reflection;
- draw commands and pipeline binding;
- swapchain/window/surface integration;
- compute pipelines.

## 11. Next recommendation

Recommended next milestone: `A44 — Pipeline consumes compiled shader program M0`.

Reason: A43 establishes the neutral DTO bridge. A44 can now add a graphics-side convenience path that creates or fills Vulkan graphics pipeline descriptors from compiled shader programs without introducing shader/compiler coupling.
