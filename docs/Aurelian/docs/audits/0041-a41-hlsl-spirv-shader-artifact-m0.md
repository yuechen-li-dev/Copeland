# A41 — HLSL -> SPIR-V shader artifact M0

## 1. Files changed

- Added `Aurelian.Shaders.Language.Artifacts.Spirv` model, diagnostics, emitter, and JSON writer files under `src/Aurelian.Shaders/Language/Artifacts/Spirv/`.
- Added `tests/Aurelian.Shaders.Tests/SpirvShaderArtifactM0Tests.cs` for validation, DXC availability-safe compilation, hashes, JSON behavior, and graphics-runtime boundary checks.
- Updated README and architecture documents to record the A41 artifact boundary and deferred work.

## 2. Task scope

A41 implements HLSL-to-SPIR-V shader artifact M0 only. The milestone starts from HLSL stage sources and ends at typed in-memory SPIR-V artifacts plus a deterministic JSON manifest. It deliberately does not integrate SDSL-V emission, assets, Vulkan graphics pipeline creation, runtime DXC invocation, or any renderer/backend path.

The boundary remains:

```text
HLSL source
  -> DXC subprocess
  -> SPIR-V bytes
  -> shader artifact manifest
```

## 3. Artifact model

The artifact model introduces:

- `HlslShaderStageKind` with `Vertex`, `Fragment`, and `Compute` values.
- `HlslShaderStageSource` carrying stage kind, HLSL source text, entry point, profile, and source name.
- `SpirvShaderStageArtifact` carrying stage metadata, source hash, SPIR-V hash, SPIR-V bytes, and DXC arguments.
- `SpirvShaderArtifact` carrying format version `aurelian.spirv.shader-artifact/0`, language `HLSL`, stage artifacts, diagnostics, and a success predicate.

The in-memory artifact stores SPIR-V bytes directly for M0 completeness. Later asset-pipeline work can split `.spv` payloads out of the manifest.

## 4. DXC subprocess integration

`SpirvShaderArtifactEmitter.EmitFromHlslStages(...)` invokes the existing A40 `DxcSpirvCompiler` and `DxcExecutableResolver`; it does not add an in-process compiler binding. Resolution remains `AURELIAN_DXC`, packaged `Microsoft.Direct3D.DXC` content, then PATH through the existing resolver.

The artifact emitter resolves DXC once after input validation. If DXC is unavailable, it returns an artifact with no compiled stages and an `ASSV1005 DxcUnavailable` diagnostic, keeping normal tests unavailable-safe.

## 5. Hashing/reproducibility

The emitter computes deterministic lowercase SHA-256 hashes without new package dependencies:

- `SourceSha256`: SHA-256 over UTF-8 HLSL source text.
- `SpirvSha256`: SHA-256 over raw SPIR-V bytes returned by DXC.

Stage artifacts also preserve the DXC argument list returned by the subprocess wrapper so the compile invocation remains diagnosable and reproducible. The A40 wrapper's temporary input/output paths may differ per invocation, so tests assert stable hashes rather than exact argument-list text across separate emissions.

## 6. JSON manifest behavior

`SpirvShaderArtifactJsonWriter.Write(...)` uses `System.Text.Json` with explicitly shaped manifest objects and deterministic property order. The manifest includes:

- `formatVersion`;
- `language`;
- `success`;
- `stages` with stage, entry point, profile, source name, source hash, SPIR-V hash, `spirvBase64`, and DXC arguments;
- `diagnostics` with code, severity, and message.

The writer returns indented JSON plus a trailing newline and is deterministic for the same artifact object.

## 7. HLSL fixtures

A41 uses the existing A40 HLSL fixtures:

- `tests/Aurelian.Shaders.Tests/Fixtures/Hlsl/tiny_triangle_vs.hlsl` as `Vertex`, `VSMain`, `vs_6_0`.
- `tests/Aurelian.Shaders.Tests/Fixtures/Hlsl/tiny_triangle_ps.hlsl` as `Fragment`, `PSMain`, `ps_6_0`.

No SDSL-V fixture is wired into the SPIR-V artifact path in A41.

## 8. Tests added

`SpirvShaderArtifactM0Tests` covers:

- empty stage-list rejection;
- duplicate stage rejection;
- stage/profile mismatch rejection;
- unavailable-safe DXC behavior;
- vertex and fragment SPIR-V artifact production when DXC is available;
- SPIR-V magic number checks when DXC is available;
- stable source and SPIR-V hashes when DXC is available;
- deterministic JSON writing;
- base64 SPIR-V manifest payloads;
- no `Aurelian.Graphics` or `Silk.NET.Vulkan` runtime dependency from the shader artifact layer.

## 9. Boundary checks

Boundary checks confirm the A41 compiler/artifact layer does not reference graphics runtime APIs, does not add Vortice.Dxc, and does not place SPIR-V artifact code into runtime graphics projects. `Microsoft.Direct3D.DXC` remains scoped to `Aurelian.Shaders` tooling/package metadata.

## 10. Validation results

Validation commands run for A41:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Aurelian.Graphics|Silk|Vulkan|Vk|CreateShaderModule|GraphicsPipeline" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Microsoft.Direct3D.DXC|Vortice.Dxc|Vortice|DXC|Dxc|dxcompiler|dxc" Directory.Packages.props src tests -g '*.props' -g '*.csproj' -g '*.cs' || true
rg -n "SpirvShaderArtifact|spirvBase64|HlslShaderStageSource|DxcSpirvCompiler" src/Aurelian.Graphics src/Aurelian.Runtime src/Aurelian.World src/Aurelian.Rendering.Contracts src/Aurelian.Rendering.Null -g '*.cs' -g '*.csproj' || true
```

On this Linux environment, packaged `Microsoft.Direct3D.DXC` content does not provide a native Linux DXC executable, so availability-gated compile paths remain clean and return unavailable diagnostics unless `AURELIAN_DXC` or PATH supplies native `dxc`.

## 11. Deferred features

Deferred from A41:

- SDSL-V -> HLSL -> SPIR-V artifact integration;
- `Aurelian.Graphics` pipeline consumption of shader artifacts;
- `Aurelian.Assets` integration;
- shader reflection/binding metadata;
- split `.spv` payload files;
- runtime DXC invocation;
- Vortice.Dxc or Vortice.Vulkan;
- direct SDSL-V -> SPIR-V generation.

## 12. Next recommendation

A42 — SDSL-V -> HLSL -> SPIR-V artifact M0.

A41 packages HLSL stage artifacts and proves the DXC subprocess artifact boundary. The next source-side step should wire existing SDSL-V HLSL emission into the same artifact path without changing graphics runtime boundaries.
