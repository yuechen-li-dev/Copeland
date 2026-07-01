# A70 — Asset manifest references shader artifacts M0

## 1. Files changed

- Extended `src/Aurelian.Assets/AssetPipeline.cs` with asset-manifest shader artifact references, diagnostics, parsing, and validation.
- Added `src/Aurelian.Assets/Shaders/LoadedShaderAsset.cs` for loaded shader asset records.
- Added `src/Aurelian.Assets/Shaders/ShaderAssetManifestLoadResult.cs` for aggregate manifest shader load results.
- Added `src/Aurelian.Assets/Shaders/ShaderAssetManifestLoader.cs` to parse asset manifests, validate shader references, resolve artifact paths, and load shader artifacts.
- Added `tests/Aurelian.Assets.Tests/AssetManifestShaderReferencesM0Tests.cs` for parser, validation, loader, failure mapping, and graphics-boundary coverage.
- Added `samples/Aurelian.VisibleTriangle/Assets/assets.toml` and updated the visible sample project so the manifest is copied to output.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `samples/Aurelian.VisibleTriangle/README.md` for A70 behavior and boundaries.

## 2. Task scope

A70 implements asset-manifest shader artifact references only. It connects asset manifest TOML to the A69/A69b shader artifact TOML + SPIR-V bridge and returns neutral `CompiledShaderProgram` data through loaded shader asset records.

The milestone does not make the visible triangle sample consume the manifest yet. The sample manifest is staged for A71.

## 3. Manifest schema extension

A70 adds repeated plural `[[shaders]]` entries to asset manifests:

```toml
[[shaders]]
id = "smoke_triangle"
path = "Shaders/SmokeTriangle/shader.toml"
```

The schema intentionally references an existing shader artifact manifest rather than source shader code. Paths are interpreted relative to the asset manifest directory.

## 4. Parser/validation behavior

Parser behavior:

- absent `[[shaders]]` produces an empty shader reference list;
- each `[[shaders]]` entry reads `id` and `path` as strings;
- malformed non-array `shaders` sections produce an asset diagnostic;
- existing legacy `[[shader]]` parser behavior is preserved.

Validation behavior:

- `AA2001` rejects missing or empty shader ids;
- `AA2002` rejects missing or empty shader paths;
- `AA2003` rejects duplicate shader ids;
- `AA2004` rejects absolute shader paths;
- `AA2005` rejects paths containing `..` traversal segments.

## 5. Shader artifact loading behavior

`ShaderAssetManifestLoader.LoadShadersFromManifest(...)`:

1. reads the asset manifest file;
2. parses asset manifest TOML;
3. validates shader reference entries;
4. resolves each accepted shader artifact path relative to the asset manifest directory;
5. checks that the shader artifact manifest exists;
6. calls `ShaderArtifactLoader.LoadCompiledShaderProgram(...)`;
7. returns `LoadedShaderAsset` records with shader id, resolved shader artifact manifest path, neutral `CompiledShaderProgram`, and per-asset diagnostics;
8. maps missing artifact files to `AA2006` and shader artifact loader failures to `AA2007` asset diagnostics.

The loader does not create graphics resources, Vulkan shader modules, pipeline objects, or runtime compiler work.

## 6. Sample manifest addition

The visible triangle sample now includes:

```text
samples/Aurelian.VisibleTriangle/Assets/assets.toml
```

with:

```toml
[[shaders]]
id = "smoke_triangle"
path = "Shaders/SmokeTriangle/shader.toml"
```

The sample project copies that manifest to the output directory. Sample code still loads `Assets/Shaders/SmokeTriangle/shader.toml` directly until A71.

## 7. Tests added

Added `AssetManifestShaderReferencesM0Tests` covering:

- shader reference parsing;
- absent shader reference sections;
- missing id validation;
- missing path validation;
- duplicate id validation;
- absolute path rejection;
- path traversal rejection;
- manifest-relative shader artifact loading;
- shader artifact failure diagnostic mapping;
- no `Aurelian.Graphics` assembly reference from `Aurelian.Assets`.

## 8. Boundary checks

Boundary checks performed:

- `Aurelian.Assets` still has no `Aurelian.Graphics` project reference and the test assembly verifies the Assets assembly does not reference `Aurelian.Graphics`.
- No Vulkan shader modules, swapchains, surfaces, graphics runtime objects, material system, mesh pipeline, texture pipeline, cache, hot reload, or runtime shader compilation was introduced.
- The visible sample manifest is present, but sample code still does not load `assets.toml`.
- No packages were added.
- No CodeReferences or vendor files were modified.

## 9. Validation results

Commands run successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Assets.Tests/Aurelian.Assets.Tests.csproj -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
git diff --check
```

Additional ripgrep boundary inspections were run for graphics references in Assets, runtime shader compiler references in the sample, A70 manifest symbols, and forbidden host/service-locator/vendor/reference-code patterns.

## 10. Deferred features

Deferred deliberately:

- visible sample consumption of `assets.toml`;
- material asset system;
- mesh asset pipeline;
- texture asset pipeline;
- asset manager/cache;
- hot reload;
- runtime shader compilation;
- runtime DXC;
- Vulkan shader module creation from the asset layer;
- Graphics dependency in Assets;
- VMA/VMASharp or Vortice adoption.

## 11. Next recommendation

A71 — Visible sample loads shader through asset manifest M0

A71 should:

- update `samples/Aurelian.VisibleTriangle` to load `assets.toml`;
- resolve shader id `smoke_triangle`;
- pass loaded `CompiledShaderProgram` into pipeline setup;
- stop directly referencing `Shaders/SmokeTriangle/shader.toml` in sample code.
