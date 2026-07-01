# A44 — Pipeline consumes compiled shader program M0

## 1. Files changed

Implementation files:

- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineDescriptorFactory.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineDescriptorResult.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineCreateResult.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineDiagnostic.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanCompiledGraphicsPipelineStatus.cs`

Test file:

- `tests/Aurelian.Graphics.Tests/VulkanCompiledGraphicsPipelineDescriptorM0Tests.cs`

Documentation files:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `docs/audits/0044-a44-pipeline-consumes-compiled-shader-program-m0.md`

## 2. Task scope

A44 implements the graphics-side compiled shader program bridge only. The milestone accepts neutral `CompiledShaderProgram` values from `Aurelian.Rendering.Contracts.Shaders`, validates the M0 graphics shader shape, maps compiled stages to Vulkan shader stage descriptors through the existing A43 mapper, combines them with explicit vertex input descriptors and fixed graphics pipeline options, and produces `VulkanGraphicsPipelineDescriptor` values.

A44 also adds an optional native helper that delegates to the existing A39 `VulkanGraphicsPipelineFactory.Create` when the caller supplies an `AurelianVulkanPlant` and `AurelianVulkanRenderPass`.

## 3. Dependency boundary

The dependency boundary remains:

```text
Aurelian.Shaders -> Aurelian.Rendering.Contracts <- Aurelian.Graphics
```

Boundary decisions:

- `Aurelian.Graphics` consumes only neutral compiled shader contracts, not `Aurelian.Shaders` artifact types.
- `Aurelian.Graphics` does not reference `Aurelian.Shaders`.
- `Aurelian.Graphics` does not reference or invoke DXC.
- `Aurelian.Graphics` does not integrate SDSL-V, asset/TOML manifests, or shader compiler internals.
- `Aurelian.Shaders` does not reference `Aurelian.Graphics` or Vulkan pipeline APIs.

## 4. Descriptor factory behavior

`VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor`:

1. Rejects a missing compiled shader program.
2. Requires a vertex compiled shader stage.
3. Requires a fragment compiled shader stage.
4. Rejects compute stages for graphics M0.
5. Rejects duplicate stage kinds.
6. Validates vertex input descriptor lists are non-null, use unique bindings/locations, use non-zero vertex buffer strides, and do not reference missing bindings.
7. Reuses `VulkanCompiledShaderStageMapper.ToVulkanShaderStages` to validate/convert SPIR-V byte payloads into Vulkan shader stage descriptors.
8. Returns a `VulkanGraphicsPipelineDescriptor` without creating native Vulkan objects.

Descriptor creation therefore requires no Vulkan runtime, no window/surface/swapchain, no shader compiler, and no DXC executable.

## 5. Native pipeline helper behavior

`VulkanCompiledGraphicsPipelineDescriptorFactory.CreatePipeline`:

1. Calls `CreateDescriptor` first.
2. Returns the descriptor diagnostics unchanged if descriptor creation is rejected or fails.
3. Validates that native creation inputs include a plant and render pass.
4. Delegates native creation to `VulkanGraphicsPipelineFactory.Create`.
5. Converts native pipeline diagnostics into compiled-pipeline diagnostics.
6. Returns a native pipeline only when the existing A39 factory succeeds.

Native pipeline creation remains optional and cleanly reports failures when Vulkan is unavailable or when the supplied magic-only SPIR-V bytes are not a valid full module.

## 6. Tests added

Added `tests/Aurelian.Graphics.Tests/VulkanCompiledGraphicsPipelineDescriptorM0Tests.cs` covering:

- missing program rejection;
- missing vertex stage rejection;
- missing fragment stage rejection;
- compute stage rejection for graphics M0;
- duplicate stage rejection;
- vertex + fragment stage mapping;
- vertex input preservation;
- invalid SPIR-V bytes rejection;
- invalid vertex input rejection;
- no `Aurelian.Shaders` or DXC assembly references from graphics;
- optional native helper clean failure/skip behavior when Vulkan or full valid SPIR-V is unavailable.

## 7. Boundary checks

Boundary commands:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Graphics|Silk|Vulkan|Vk|CreateShaderModule|GraphicsPipeline" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Shaders|Aurelian.Graphics|Silk|Vulkan|DXC|Dxc|Microsoft.Direct3D.DXC" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Vortice|VMASharp|Vma|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Draw|vkCmdDraw|Aurelian.World|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "ProjectReference" src/Aurelian.Shaders src/Aurelian.Graphics src/Aurelian.Rendering.Contracts tests/Aurelian.Shaders.Tests tests/Aurelian.Graphics.Tests tests/Aurelian.Rendering.Contracts.Tests -g '*.csproj'
git status --short
```

## 8. Validation results

Validation result after implementation:

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed.
- Boundary scans showed no direct `Aurelian.Graphics` -> `Aurelian.Shaders` project reference, no DXC/SDSL-V compiler dependency in graphics, no graphics/Vulkan dependency in shaders, and neutral rendering contracts.
- The broad forbidden-feature scan showed expected pre-existing textual matches from existing graphics milestones/tests, but no A44 draw command, swapchain/window/surface, Vortice, VMA, vendor, or reference-code integration.

## 9. Deferred features

Deferred features:

- DXC invocation from graphics;
- direct SDSL-V or shader artifact integration in graphics;
- asset/TOML shader manifest integration;
- descriptor sets;
- uniforms;
- push constants;
- pipeline bind commands;
- vertex/index bind commands;
- draw commands;
- swapchain/window/surface;
- VMA/VMASharp;
- Vortice adoption;
- valid checked-in shader fixture requirements for native successful pipeline creation.

## 10. Next recommendation

Recommended next milestone: **A45 — Pipeline bind + draw command M0**.

Reason: A44 enables graphics pipeline descriptors and optional native pipelines from neutral compiled shader programs. Command recording can already enter render passes, but the graphics command path still cannot bind the pipeline or issue a draw.
