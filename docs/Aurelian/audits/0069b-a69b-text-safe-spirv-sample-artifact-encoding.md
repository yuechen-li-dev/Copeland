# A69b — Text-safe SPIR-V sample artifact encoding

## 1. Files changed

- Added `spirv_encoding` support to the shader artifact loader in `Aurelian.Assets`.
- Added text-hex SPIR-V encode/write support to the shader artifact writer in `Aurelian.Shaders`.
- Converted the visible triangle sample shader files from raw `.spv` binaries to `.spv.hex` text files.
- Updated loader/writer tests for hex success and failure paths.
- Updated A69 docs to describe binary versus hex SPIR-V file encodings.

## 2. Task scope

A69b is a source-control-safe encoding correction only. It keeps the A69 file-based shader artifact path, keeps runtime shader compilation out of the sample, and changes only the checked-in sample transport form for SPIR-V bytes.

## 3. Reason for follow-up

A69 checked raw `.spv` binary files into the visible sample asset directory. The artifact intent was correct, but the repository/PR policy requires checked-in sample assets to be text-safe. A69b replaces those sample binaries with `.spv.hex` text while preserving runtime delivery of real SPIR-V bytes through `CompiledShaderProgram`.

## 4. Encoding model

`spirv_encoding` is an optional per-stage TOML field. Supported M0 values are:

- `binary`: read `spirv_path` as raw SPIR-V bytes. This is the default when the field is omitted for A69 backward compatibility.
- `hex`: read `spirv_path` as text hex, ignore whitespace, decode into raw SPIR-V bytes, and then validate those decoded bytes.

`spirv_sha256` is always the lowercase SHA-256 of decoded/raw SPIR-V bytes, never the hash of the `.spv.hex` text.

## 5. Loader behavior

`ShaderArtifactLoader.LoadCompiledShaderProgram(...)` now parses `spirv_encoding`, defaults missing values to `binary`, rejects unsupported encodings, decodes `hex` files into bytes, rejects malformed hex text, rejects empty decoded bytecode, and verifies hashes over decoded/raw bytes before constructing neutral compiled shader stages.

## 6. Writer behavior

`ShaderArtifactFileWriter.WriteSdslvSpirvArtifact(...)` keeps binary output as the default. A new write options record allows `SpirvEncoding = "hex"`, which writes deterministic lowercase `.spv.hex` files with 32 bytes per line, emits `spirv_encoding = "hex"` in `shader.toml`, and keeps hashes based on raw SPIR-V bytes.

## 7. Sample artifact conversion

The visible triangle sample now contains:

```text
shader.toml
VSMain.spv.hex
PSMain.spv.hex
generated.hlsl
```

The sample still resolves `shader.toml` from `AppContext.BaseDirectory`, copies the entire asset directory to output through the existing wildcard, and receives real SPIR-V bytes through `Aurelian.Assets` at runtime.

## 8. Tests added

- Loader tests now cover loading hex artifacts, unsupported encodings, malformed hex text, and hash checks against decoded bytes.
- Writer tests now cover hex file output, `spirv_encoding = "hex"` manifest entries, and hashes that match raw bytes.

## 9. Boundary checks

Boundary checks confirm that no raw `.spv` files remain in the sample asset directory, the sample has no static SPIR-V C# arrays, the sample and Graphics remain free of DXC/SDSL-V/HLSL artifact runtime dependencies, and Assets remains Graphics-free.

## 10. Validation results

The solution build/test, focused Assets/Shaders tests, visible sample build, and boundary grep/status checks were run for A69b. The resulting implementation keeps the sample artifact path source-control-safe while continuing to hand real SPIR-V bytes to Graphics through neutral contracts.

## 11. Deferred features

A69b does not add hot reload, descriptor reflection, full asset management, runtime shader compilation, base64/TOML byte storage, generated sample C# byte arrays, or new package dependencies.

## 12. Next recommendation

**A70 — Asset manifest references shader artifacts M0**.

A69/A69b establish direct shader artifact files and text-safe checked-in sample transport. The next asset-pipeline step should let the existing asset manifest/TOML pipeline reference shader artifact manifests instead of requiring direct caller paths to `shader.toml`.
