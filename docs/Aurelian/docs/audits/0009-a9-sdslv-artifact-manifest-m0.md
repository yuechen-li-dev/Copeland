# 0009 — A9 SDSL-V artifact manifest M0

## 1. Files changed

- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifact.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifactEmitter.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifactJsonWriter.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifactManifest.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifactOptions.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderArtifactStage.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderSource.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderSourceHash.cs`
- `src/Aurelian.Shaders/Language/Artifacts/SdslvShaderStageKind.cs`
- `tests/Aurelian.Shaders.Tests/SdslvArtifactM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `docs/audits/0009-a9-sdslv-artifact-manifest-m0.md`

## 2. Task scope

A9 adds artifact manifest M0 over the new Aurelian SDSL-V C# production path:

```text
source -> parse -> validate -> HLSL emit -> artifact/manifest
```

The new code lives under `Aurelian.Shaders.Language.Artifacts` so it is clearly tied to the new `Language` parser/validator/HLSL emitter pipeline rather than the carried-over legacy artifact namespace.

A9 does not add DXC execution, SPIR-V generation, renderer/windowing, an asset/TOML bridge, `.sdslvtest`, an Oct interpreter, old Stride `.sdsl` support, mixins/effects/base-shader compatibility, CodeReferences coupling, or legacy pipeline changes.

## 3. Artifact model

The M0 artifact model is intentionally small and deterministic. It includes:

- format version, currently `aurelian.sdslv.artifact/0`;
- language name, currently `Aurelian SDSL-V`;
- source display name;
- source hash metadata;
- generated HLSL text;
- stage entry point metadata;
- combined parse, validation, and emission diagnostics;
- success derived from the absence of error diagnostics.

`SdslvShaderArtifactManifest` is a JSON-facing DTO shape. It mirrors the artifact and maps diagnostics to string-valued severity/phase plus span fields so the manifest remains readable and stable.

## 4. Artifact emitter pipeline

`SdslvShaderArtifactEmitter.Emit(...)` performs the M0 pipeline:

1. Compute SHA-256 for the source text.
2. Parse the source text with `SdslvParser.ParseModule(...)`.
3. Collect stage entry points from parsed shader stage methods when a module is available.
4. Validate with `SdslvValidator.ValidateModule(...)` only when parsing succeeds.
5. Emit HLSL with `HlslEmitter.EmitModule(...)` only when parsing and validation succeed by default.
6. Combine parse, validation, and emission diagnostics in order.
7. Return an in-memory `SdslvShaderArtifact`.

Default behavior is deterministic and conservative: if parsing/validation/emission reports an error, the returned artifact contains an empty HLSL string unless `EmitPartialHlslOnError` is explicitly enabled.

## 5. Source hashing

`SdslvShaderSourceHash.ComputeSha256(...)` uses BCL SHA-256 over UTF-8 source text and returns:

- algorithm: `SHA-256`;
- value: lowercase hexadecimal;
- deterministic 64-character output.

The tests assert determinism, algorithm name, lowercase hex shape, and length.

## 6. Stage entry point collection

M0 stage collection reads shader `StageMethods` from the new AST. Each stage method name becomes an entry point.

Stage/profile inference is deliberately heuristic in M0:

- parsed stage label or function name starting with `VS` or containing `Vertex` maps to Vertex / `vs_6_0`;
- parsed stage label or function name starting with `PS` or containing `Pixel` or `Fragment` maps to Pixel / `ps_6_0`;
- parsed stage label or function name starting with `CS` or containing `Compute` maps to Compute / `cs_6_0`;
- otherwise the stage is Unknown and profile is null.

This is not full stage annotation semantics. It is only enough to make entry point provenance visible in artifacts until richer language metadata exists.

## 7. JSON manifest writer

`SdslvShaderArtifactJsonWriter.WriteManifest(...)` uses `System.Text.Json` from the BCL. It writes indented camelCase JSON with stable DTO property order and no environment-specific values.

For M0, HLSL is included directly under the JSON `hlsl` property so a complete artifact can be inspected in one string. Later asset pipeline work may split HLSL into separate output files and leave references in the manifest.

## 8. Tests added

`tests/Aurelian.Shaders.Tests/SdslvArtifactM0Tests.cs` adds coverage for:

- successful artifact emission from `smoke_triangle.sdslv`;
- source hash determinism and SHA-256 shape;
- stage entry point collection for `VSMain` and `PSMain`;
- HLSL inclusion in the artifact;
- deterministic JSON manifest writing and key presence;
- invalid source returning validation diagnostics and failure with no default HLSL output.

## 9. Boundary checks

Boundary commands were run to confirm:

- no `CodeReferences`, Stride, Machina, WyrmCoil, or Copeland coupling was introduced in `src/Aurelian.Shaders` or `tests/Aurelian.Shaders.Tests` C# files/projects;
- the new artifact layer and tests do not contain DXC/SPIR-V/renderer/windowing terms;
- the checked-in smoke fixture still exists.

The boundary `rg` checks returned no matches.

## 10. Validation results

Validated with:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "DXC|dxc|SPIR|spirv|Vulkan|D3D|GraphicsDevice|Window" src/Aurelian.Shaders/Language/Artifacts tests/Aurelian.Shaders.Tests -g '*.cs' || true
test -f tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv
git status --short
```

Results:

- build passed;
- tests passed;
- artifact M0 boundary checks returned no prohibited coupling matches;
- fixture existence check passed.

## 11. Deferred features

Still deferred after A9:

- DXC validation or execution;
- SPIR-V output;
- renderer/windowing integration;
- asset pipeline or TOML manifest bridge;
- `.sdslvtest` runner;
- Oct interpreter or CPU shader evaluator;
- full stage annotation semantics;
- full expression type checking and interface satisfaction;
- old Stride `.sdsl`, mixins, effects, or base-shader compatibility;
- CodeReferences imports or production references.

## 12. Next recommendation

Recommended next milestone:

```text
A10 — DXC optional validation M0
```

Reason: A9 establishes deterministic artifact boundaries with source identity, HLSL, diagnostics, success, and entry point profile hints. Optional DXC validation can now consume artifacts without being entangled with parsing, validation, emission, renderer, or asset-manifest concerns.
