# 0003 — A3 Aurelian.Shaders identity conversion

## 1. Files changed

- Renamed `src/StriV.ShaderPipeline/` to `src/Aurelian.Shaders/`.
- Renamed `src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj` to `src/Aurelian.Shaders/Aurelian.Shaders.csproj`.
- Updated production namespaces and imports under `src/Aurelian.Shaders/` from `StriV.ShaderPipeline` to `Aurelian.Shaders`.
- Added `tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj`.
- Added `tests/Aurelian.Shaders.Tests/ShaderPipelineIdentityTests.cs`.
- Updated `Aurelian.slnx` to link the new production and test projects.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/vendor-strategy.md` with A3 status and boundaries.
- Created this audit report.

## 2. Task scope

A3 was an identity cleanup and solution-integration milestone. Its goal was to convert the carried-over `StriV.ShaderPipeline` project identity into an Aurelian-owned module named `Aurelian.Shaders` without changing shader language semantics.

The following were intentionally out of scope:

- Parser semantic rewrite.
- AST rewrite.
- WyrmCoil feature implementation.
- Removal of carried-over mixin, effect, or base-shader code.
- Porting old `.sdsl` corpus.
- SDSL-V M0 implementation.
- Stri-V asset pipeline/tool migration.
- Renderer/HAL work.
- `CodeReferences/*` modifications.
- `vendor/Dominatus/*` modifications.
- Aurelian runtime/world behavior changes.

## 3. Project rename summary

The carried-over project folder moved from:

```text
src/StriV.ShaderPipeline/
```

to:

```text
src/Aurelian.Shaders/
```

The project file moved from:

```text
src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj
```

to:

```text
src/Aurelian.Shaders/Aurelian.Shaders.csproj
```

No `src/StriV.AssetPipeline` or `src/StriV.AssetTool` files were modified.

## 4. Namespace/project metadata changes

Project metadata now identifies the module as `Aurelian.Shaders`:

```xml
<AssemblyName>Aurelian.Shaders</AssemblyName>
<RootNamespace>Aurelian.Shaders</RootNamespace>
```

The stale Stri-V build profile import was removed:

```xml
<Import Project="../../build/StriV.Core.Profile.props" />
```

The project target framework remains aligned with existing Aurelian projects at `net10.0`.

All production C# namespaces and intra-project imports under `src/Aurelian.Shaders/` were renamed from `StriV.ShaderPipeline` to `Aurelian.Shaders`.

## 5. Solution integration

`Aurelian.slnx` now links:

```text
src/Aurelian.Shaders/Aurelian.Shaders.csproj
tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj
```

The remaining Stri-V salvage projects are not linked in `Aurelian.slnx`.

## 6. Tests added

A minimal test project was added at:

```text
tests/Aurelian.Shaders.Tests/
```

Smoke coverage is intentionally narrow and identity-focused:

- Verifies the production assembly name is `Aurelian.Shaders`.
- Verifies the carried-over lexer can tokenize a tiny shader-like source.
- Verifies the diagnostic factory can create a diagnostic.

These tests do not assert final WyrmCoil SDSL-V semantics.

## 7. Behavior preservation statement

A3 preserves current carried-over shader pipeline behavior. The work only changed project identity, namespace identity, solution linkage, and minimal smoke-test coverage.

Carried-over parser, AST, lowering, artifact, mixin/effect, and base-shader behavior remain migration scaffold behavior and should not be treated as final Aurelian SDSL-V semantics.

## 8. Boundary checks

Boundary checks were run against `src/Aurelian.Shaders` and `tests/Aurelian.Shaders.Tests` for old identity strings and forbidden reference names:

```bash
rg -n "StriV\.ShaderPipeline|namespace StriV|StriV.Core.Profile|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
```

The check returned no matches.

Project-reference inventory was also run:

```bash
rg -n "ProjectReference" Aurelian.slnx src tests -g '*.csproj' -g '*.slnx'
```

The inventory shows `tests/Aurelian.Shaders.Tests` references `src/Aurelian.Shaders`. It also shows pre-existing references in the unlinked salvage projects under `src/StriV.AssetPipeline` and `src/StriV.AssetTool`; those projects were intentionally not modified in A3.

## 9. Validation results

Validation commands completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
```

Result: build succeeded with 0 warnings and 0 errors.

```bash
dotnet test Aurelian.slnx -c Debug
```

Result: all linked test projects passed, including `Aurelian.Shaders.Tests` with 3 passing tests.

## 10. Next recommendation

A4 — Aurelian.Shaders AST convergence toward WyrmCoil SDSL-V

A4 should:

- replace old Stri-V/Stride-shaped AST with WyrmCoil-shaped SDSL-V module/declaration/type model;
- keep old parser/lowerer as temporary legacy/reference if needed;
- add tests for records, streams, enums, shader declarations, and type refs;
- not attempt full HLSL emission rewrite yet unless necessary.
