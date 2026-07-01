# A46 — Valid SPIR-V fixture / first offscreen draw recording proof

## 1. Files changed

- `tests/Aurelian.Graphics.Tests/Fixtures/Spirv/TriangleSpirvFixtures.cs`
- `tests/Aurelian.Graphics.Tests/VulkanOffscreenDrawRecordingM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0046-a46-valid-spirv-offscreen-draw-recording-proof.md`

## 2. Task scope

A46 is an offscreen draw recording proof for the existing Vulkan backend pieces. The milestone focuses on a valid shader-bytecode fixture and an integration-style graphics test that records the motivating command sequence without adding presentation or broader renderer features.

Out of scope:

- swapchain/window/surface creation;
- descriptor sets;
- uniforms/push constants;
- index buffers;
- texture sampling;
- asset/TOML integration;
- runtime shader compilation in graphics;
- VMA/VMASharp or Vortice adoption;
- visual output/readback validation.

## 3. SPIR-V fixture source

The new graphics-test fixture contains static SPIR-V byte arrays for a tiny triangle vertex shader and fragment shader. The shader source shape matches the A46 request:

- vertex input: `float2 Position : POSITION` and `float4 Color : COLOR0`;
- vertex output: `SV_Position` plus `COLOR0`;
- fragment input: interpolated color;
- fragment output: `SV_Target0` color.

The checked-in bytes were generated once from the tiny HLSL fixtures and validated with:

```bash
glslangValidator -D -V --target-env vulkan1.3 -e <entry> -S <stage> <fixture>.hlsl -o <stage>.spv
spirv-val --target-env vulkan1.3 <stage>.spv
```

The fixture is static test data. There is no runtime DXC/glslang dependency, no `Aurelian.Shaders` project reference from `Aurelian.Graphics.Tests`, and no shader compiler dependency added to `Aurelian.Graphics`.

## 4. Offscreen draw recording chain

The new proof test records this path when Vulkan is available:

```text
plant/device
  -> allocator
  -> command buffer pool
  -> fence bundle
  -> color attachment texture
  -> render pass
  -> framebuffer
  -> graphics pipeline
  -> vertex buffer
  -> vertex upload
  -> command buffer
  -> begin render pass
  -> draw vertices
  -> end render pass
  -> end command buffer
```

If Vulkan plant initialization is unavailable or rejected, the test follows the existing graphics-test convention: assert diagnostics are present and return cleanly so normal test runs without Vulkan do not fail.

## 5. Pipeline/vertex input setup

The pipeline is created through `VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline(...)`, using a neutral `CompiledShaderProgram` assembled in the test from the static SPIR-V fixture bytes.

Vertex input setup:

- binding `0`;
- stride `24` bytes;
- location `0`: `Float2`, offset `0`;
- location `1`: `Float4`, offset `8`.

This matches each triangle vertex as six `float` values: two position floats and four color floats.

## 6. Buffer upload usage

The test creates a 72-byte device-local vertex buffer with `Vertex | TransferDestination` usage. It uploads three vertices through the existing `VulkanBufferUploader`, then waits for the uploader's copy-fence signal value before recording the draw command buffer.

This reuses the existing upload helper and does not add a generalized renderer submission abstraction.

## 7. Recording/submission behavior

A46 records and ends an executable offscreen draw command buffer. It does not submit the draw command buffer. The only queue submission in the test is the existing vertex-buffer upload path, which is already part of the backend helper surface and is waited before draw recording.

Draw submission/wait remains deferred because adding a generalized command-submit helper would be a separate backend seam.

## 8. Tests added

- `TriangleSpirvFixtures_VertexAndFragment_HaveSpirvMagic`
- `TriangleSpirvFixtures_CanMapToVulkanShaderStages`
- `VulkanOffscreenDrawRecording_RecordTriangleCommands_WhenVulkanAvailable_SucceedsOrCleanlySkips`

The first two tests require no Vulkan runtime. The offscreen proof test cleanly returns with diagnostics when Vulkan is unavailable.

## 9. Boundary checks

The A46 code keeps the intended boundaries:

- no project reference from `Aurelian.Graphics.Tests` to `Aurelian.Shaders`;
- no shader compiler dependency in `Aurelian.Graphics`;
- no swapchain/window/surface path;
- no descriptors/uniforms/index buffers/texture sampling;
- no VMA/VMASharp or Vortice;
- no CodeReferences/vendor edits;
- draw command emission remains isolated to the existing draw encoder and graphics tests.

The source-boundary search reports only existing assertion text that verifies forbidden shader/compiler assemblies are not referenced; the new A46 fixture/test code adds no project dependency on shader compiler modules or packages.

## 10. Validation results

A46 validation performed:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
```

Boundary searches were also run for forbidden dependency and presentation terms under `src/Aurelian.Graphics` and `tests/Aurelian.Graphics.Tests`.

## 11. Deferred features

Deferred after A46:

- draw command buffer queue submission/wait helper;
- swapchain/window/surface presentation;
- readback or visual output validation;
- descriptor sets;
- uniforms/push constants;
- index buffers;
- texture sampling;
- generalized render backend orchestration.

## 12. Next recommendation

A47 — Command submit helper M0.

Because A46 proves complete offscreen draw command recording but intentionally does not submit the draw command buffer, the next narrow convergence step should add a reusable command submit/wait helper around existing timeline-fence patterns. After that helper exists, surface/swapchain M0 can build on a cleaner command-submission seam instead of duplicating queue-submit logic.
