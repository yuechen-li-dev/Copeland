# A71 — Visible sample loads shader through asset manifest M0

## 1. Files changed

- Added `samples/Aurelian.VisibleTriangle/VisibleTriangleShaderAssets.cs` as the sample-local manifest shader loading helper.
- Updated `samples/Aurelian.VisibleTriangle/VisibleTriangleSampleFrame.cs` so prepared Vulkan setup receives an already-loaded `CompiledShaderProgram` instead of directly loading a shader artifact TOML path.
- Updated `samples/Aurelian.VisibleTriangle/Program.cs` so startup loads the shader through `Assets/assets.toml` before creating the Vulkan sample frame.
- Updated `samples/Aurelian.VisibleTriangle/README.md`, `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md` for A71 behavior and boundaries.
- Added this A71 audit report.

## 2. Task scope

A71 is sample asset-manifest consumption only. It updates the visible triangle sample to consume the A70 `[[shaders]]` manifest path and leaves the engine architecture boundaries unchanged.

The milestone does not implement a material system, mesh asset system, texture asset system, general asset manager/cache/hot reload, runtime shader compilation, DXC/SDSL runtime dependency, `Aurelian.Host`, new packages, CodeReferences changes, or vendor changes.

## 3. Sample asset loading flow

The visible triangle executable now resolves:

```text
Path.Combine(AppContext.BaseDirectory, "Assets", "assets.toml")
```

It calls `ShaderAssetManifestLoader.LoadShadersFromManifest(...)`, which parses the copied manifest, validates shader references, resolves artifact TOML paths relative to the manifest directory, and delegates artifact decoding/hash validation to `ShaderArtifactLoader` inside `Aurelian.Assets`.

The resulting `CompiledShaderProgram` is passed into `VisibleTriangleSampleFrame.Create(...)`, and the existing `CreatePipeline(...)` path continues to build the graphics pipeline from neutral shader contracts.

## 4. Shader id resolution

The sample-local helper selects shader id:

```text
smoke_triangle
```

The helper is intentionally sample-local. It knows only the visible sample's manifest path and shader id; it is not a general asset manager, cache, service locator, hot reload system, or reflection-based resolver.

The sample C# runtime path no longer directly references:

```text
Assets/Shaders/SmokeTriangle/shader.toml
```

That path remains in `Assets/assets.toml`, where it belongs for A71.

## 5. Diagnostics behavior

When manifest loading fails or `smoke_triangle` is missing, the helper prints:

- the resolved manifest path;
- the requested shader id;
- whether the requested shader was found;
- aggregate asset diagnostics;
- per-loaded-shader diagnostics when any shaders were loaded.

The helper then throws `VisibleTriangleSampleException`, which the top-level sample already maps to a nonzero environment/runtime failure exit code. This keeps expected missing-file or invalid-manifest failures legible and avoids unhelpful raw exceptions.

## 6. Build/test validation

Validation commands run for A71:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
dotnet test tests/Aurelian.Assets.Tests/Aurelian.Assets.Tests.csproj -c Debug
git diff --check
```

All completed successfully.

## 7. Boundary checks

Boundary inspections confirmed:

- visible sample code no longer calls `ShaderArtifactLoader.LoadCompiledShaderProgram(...)` or hardcodes `Assets/Shaders/SmokeTriangle/shader.toml`;
- visible sample references `Aurelian.Assets` but not `Aurelian.Shaders`, `Aurelian.AssetTool`, or DXC packages;
- `Aurelian.Assets` remains free of `Aurelian.Graphics`, Vulkan, swapchain, surface, and window references;
- `Aurelian.Graphics` remains free of `Aurelian.Assets`, `Aurelian.Shaders`, and shader compiler/runtime asset loader dependencies;
- no material, mesh, texture, cache, hot reload, service locator, singleton, reflection construction path, `Aurelian.Host`, CodeReferences, or vendor modifications were introduced.

## 8. Deferred features

Deferred deliberately:

- material asset system;
- mesh asset system;
- texture asset system;
- general asset manager/cache;
- hot reload;
- runtime SDSL-V/HLSL compilation;
- runtime DXC;
- graphics resource ownership in `Aurelian.Assets`;
- `Aurelian.Assets` dependency in `Aurelian.Graphics`;
- `Aurelian.Graphics` dependency in `Aurelian.Assets`;
- `Aurelian.Host`;
- VMA/VMASharp or Vortice adoption.

## 9. Next recommendation

A72 — Aurelian checkpoint audit after visible sample + shader asset bridge

A72 should:

- summarize the current engine architecture;
- list what is real vs scaffold;
- identify boundary health;
- identify next priority options;
- pause implementation so roadmap can be discussed.
