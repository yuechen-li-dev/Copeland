# A69 — Shader artifact TOML + SPIR-V file bridge M0

## 1. Files changed

- Added shader artifact file writer/result/diagnostic contracts under `src/Aurelian.Shaders/Language/Artifacts/Files/`.
- Added shader artifact manifest loader/result/diagnostic contracts under `src/Aurelian.Assets/Shaders/`.
- Added visible sample shader artifact files under `samples/Aurelian.VisibleTriangle/Assets/Shaders/SmokeTriangle/`.
- Updated the visible triangle sample to load shader artifacts through `Aurelian.Assets`.
- Removed the sample-local `TriangleSpirvFixtures.cs` runtime shader byte-array path.
- Added A69 shader writer and loader tests.
- Updated README, roadmap, dependency policy, and visible sample documentation.

## 2. Task scope

A69 implements the M0 file bridge only: write real shader artifacts to files, load those files back into neutral compiled shader contracts, and route the visible triangle sample through that checked-in artifact path.

## 3. Artifact format decision

The primary runtime artifact format is TOML metadata plus external SPIR-V byte files. A69b adds `spirv_encoding`, where generated build artifacts may use raw binary `.spv` (`binary`) and checked-in sample artifacts use text-safe `.spv.hex` (`hex`). TOML stores metadata, relative paths, encoding, and lowercase SHA-256 hashes only. SPIR-V is not stored as TOML integer arrays or base64. `generated.hlsl` is optional and is treated as a debug/build artifact, not as runtime compiler input.

## 4. TOML manifest model

The manifest format version is `aurelian.shader-artifact/0`. The M0 manifest contains source metadata, optional generated HLSL path/hash metadata, and repeated `[[stages]]` entries with lowercase stage names (`vertex`, `fragment`, `compute`), entry point, profile, optional `spirv_encoding` (`binary` default or `hex`), SPIR-V relative path, SPIR-V hash, and source name.

## 5. SPIR-V file model

Each compiled stage uses an external file resolved relative to the manifest directory. For `binary`, the loader reads raw bytes. For `hex`, the loader reads text, removes whitespace, decodes hex to bytes, rejects malformed text, computes SHA-256 over decoded/raw bytes, and compares it to the manifest hash before constructing a `CompiledShaderStage`.

## 6. Shader artifact writer

`ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(...)` accepts successful `SdslvSpirvShaderArtifact` data, creates the output directory, writes `shader.toml`, writes one SPIR-V file per stage using the stage entry point as the file stem (`.spv` by default, `.spv.hex` when requested), writes `generated.hlsl` when non-empty, computes hashes, and returns a file set plus diagnostics.

## 7. Shader artifact loader

`ShaderArtifactLoader.LoadCompiledShaderProgram(...)` parses TOML through the existing `Tomlyn` dependency in `Aurelian.Assets`, validates M0 format/stage rules, verifies SPIR-V file hashes over decoded/raw bytes, rejects duplicate stages, and returns a neutral `CompiledShaderProgram`. It does not reference `Aurelian.Graphics`, invoke DXC, parse HLSL, or depend on shader compiler code.

## 8. Visible sample update

The visible triangle sample now copies `Assets/Shaders/SmokeTriangle/**` to its output directory, uses text-safe `.spv.hex` shader files, and resolves `shader.toml` with `Path.Combine(AppContext.BaseDirectory, ...)`. The sample passes the loaded `CompiledShaderProgram` into the existing Vulkan pipeline descriptor path. It has no runtime dependency on DXC, SDSL-V compilation, or sample-local static SPIR-V arrays.

## 9. Tests added

- `ShaderArtifactFileWriterM0Tests` covers TOML/SPV/HLSL writing, manifest hash consistency, and failed-artifact rejection.
- `ShaderArtifactLoaderM0Tests` covers successful compiled program loading, missing manifest, unsupported format, missing SPIR-V file, unsupported encoding, malformed hex, hash mismatch, and duplicate stage rejection.

## 10. Boundary checks

Boundary checks confirm the intended layering: Graphics has no shader compiler dependency, the sample has no runtime DXC/SDSL-V/HLSL artifact dependency, the sample has no static SPIR-V C# array usage, and Assets has no Graphics dependency.

## 11. Validation results

Validation commands were run for solution build/test, focused shader/assets tests, visible sample build, and dependency boundary searches. The full solution build/test and focused project tests passed in the development environment.

## 12. Deferred features

A69 intentionally defers a full asset manager, hot reload, descriptor reflection, shader resource binding metadata beyond M0 stage metadata, runtime shader compilation, generated C# shader byte arrays for samples, and any Graphics dependency on shader tooling or asset loading.

## 13. Next recommendation

**A70 — Asset manifest references shader artifacts M0**.

A69 creates and loads shader artifact files directly. The next asset-pipeline step should allow the existing asset manifest/TOML pipeline to reference those shader artifact manifests instead of requiring direct sample or caller paths to `shader.toml`.
