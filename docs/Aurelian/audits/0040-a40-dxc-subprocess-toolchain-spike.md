# A40 — DXC subprocess toolchain spike

## 1. Files changed

- Added `Microsoft.Direct3D.DXC` central package version and a tool-facing package reference in `Aurelian.Shaders` only.
- Added DXC executable resolution, diagnostics, status/result records, and SPIR-V subprocess compile wrapper under `src/Aurelian.Shaders/Language/External/Dxc/`.
- Added tiny HLSL vertex and pixel fixtures under `tests/Aurelian.Shaders.Tests/Fixtures/Hlsl/`.
- Added `DxcSubprocessM0Tests` for resolver, request rejection, optional fixture compilation, invalid HLSL, and graphics-runtime boundary checks.
- Updated README and architecture docs to record the build/tooling-only DXC policy.

## 2. Task scope

A40 is a DXC subprocess audit/spike only. It verifies that the shader tooling layer can locate a DXC executable, invoke it as a subprocess, capture stdout/stderr/exit code, and return SPIR-V bytes through typed results when DXC is available.

A40 does not connect DXC to runtime graphics, Vulkan pipeline creation, SDSL-V artifact emission, reflection metadata, descriptor sets, windows, swapchains, or draw commands.

## 3. Package decision

A40 uses `Microsoft.Direct3D.DXC` instead of `Vortice.Dxc`.

The package reference is intentionally placed in `Aurelian.Shaders` because that project already owns SDSL-V parsing, validation, HLSL emission, shader artifact contracts, and external DXC validation. `Aurelian.Graphics` remains DXC-free and continues to consume raw SPIR-V bytes supplied by callers.

## 4. DXC executable resolution

The new resolver probes in deterministic order:

1. `AURELIAN_DXC` if it is set and points to an existing file.
2. `Microsoft.Direct3D.DXC` package content under NuGet package roots.
3. `PATH` for `dxc`/`dxc.exe`.

Normal missing-tool cases return `Unavailable` plus diagnostics rather than throwing.

Current package layout observed after restore on this Linux container:

- Package root: `/root/.nuget/packages/microsoft.direct3d.dxc/1.9.2602.24`.
- Windows executable/DLL files are present under `build/native/bin/x64`, `build/native/bin/x86`, and `build/native/bin/arm64`:
  - `dxc.exe`
  - `dxcompiler.dll`
  - `dxil.dll`
- Import libraries are present under `build/native/lib/{x64,x86,arm64}`.
- Headers are present under `build/native/include`.
- No Linux native `dxc` executable or `libdxcompiler.so` was present in this package layout, so packaged DXC resolves unavailable on this Linux platform unless `AURELIAN_DXC` or PATH provides a native tool.

## 5. Subprocess compile model

`DxcSpirvCompiler` writes HLSL source to a temporary `.hlsl` file, invokes DXC with argument-list escaping through `ProcessStartInfo.ArgumentList`, writes output to a temporary `.spv` file via `-Fo`, reads the produced SPIR-V bytes, and removes the temporary directory afterward.

M0 arguments are:

```text
-spirv
-fspv-target-env=vulkan1.3
-HV 2021
-E <entry>
-T <profile>
<input.hlsl>
-Fo <output.spv>
```

Additional caller arguments are accepted but no reflection or binding flags are added by default.

## 6. HLSL fixtures

A40 adds two checked-in HLSL fixtures:

- `tiny_triangle_vs.hlsl` with `VSMain` and profile `vs_6_0`.
- `tiny_triangle_ps.hlsl` with `PSMain` and profile `ps_6_0`.

These fixtures are deliberately HLSL-only. They are not emitted from SDSL-V and are not used by graphics pipeline creation in A40.

## 7. SPIR-V output behavior

Successful compilation returns:

- `Status = Compiled`;
- non-empty `SpirvBytes`;
- process `ExitCode`;
- captured stdout/stderr;
- exact argument list;
- diagnostics list.

Tests assert that fixture output starts with the SPIR-V magic number `0x07230203` when a DXC executable is available.

Invalid requests are rejected before subprocess launch. Missing DXC returns `Unavailable`. Nonzero DXC exits, missing output files, empty output files, or subprocess startup failures return `Failed` with captured output and/or diagnostics.

## 8. Tests added

`DxcSubprocessM0Tests` covers:

- resolver does not throw;
- resolver returns available or unavailable;
- empty source rejection;
- missing entry point rejection;
- missing profile rejection;
- tiny vertex fixture SPIR-V generation when DXC is available;
- tiny pixel fixture SPIR-V generation when DXC is available;
- invalid HLSL failure diagnostics when DXC is available;
- no `Aurelian.Graphics` / `Silk.NET.Vulkan` dependency from `Aurelian.Shaders`.

Tests pass when DXC is unavailable by asserting diagnostics and returning from availability-gated cases.

## 9. Boundary checks

Boundary checks confirmed:

- `Microsoft.Direct3D.DXC` appears only in central package metadata and `Aurelian.Shaders` project/package assets.
- `Vortice.Dxc` was not added.
- Runtime graphics projects have no DXC dependency.
- `Aurelian.Shaders` does not reference `Aurelian.Graphics` or Vulkan runtime APIs.

## 10. Validation results

Validation commands run for A40:

```bash
dotnet restore Aurelian.slnx
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Shaders.Tests/Aurelian.Shaders.Tests.csproj -c Debug
```

Additional boundary commands used `rg` to inspect package references and runtime-project dependency leakage.

On this Linux environment, packaged `Microsoft.Direct3D.DXC` content did not include native Linux DXC binaries. The availability-gated compile tests therefore remain clean and unavailable-safe unless a native `dxc` is supplied via `AURELIAN_DXC` or PATH.

## 11. Deferred features

Deferred from A40:

- SDSL-V artifact integration;
- HLSL-to-SPIR-V artifact manifest writing;
- shader reflection/binding metadata;
- artifact hashing beyond existing source/artifact contracts;
- `Aurelian.Graphics` pipeline integration;
- Vulkan shader-module/pipeline tests using DXC-produced fixture bytes;
- `Vortice.Dxc` fallback;
- direct SDSL-V -> SPIR-V generation, which remains intentionally not planned.

## 12. Next recommendation

A41 — HLSL -> SPIR-V shader artifact M0.

This would take the subprocess capability proven in A40 and wrap it in deterministic artifact emission for checked-in or generated HLSL. A42 can then connect SDSL-V HLSL emission into the artifact path without pulling DXC into runtime graphics.
