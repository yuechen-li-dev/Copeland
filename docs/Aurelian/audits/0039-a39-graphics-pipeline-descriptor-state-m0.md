# A39 — Vulkan graphics pipeline descriptor/state M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanShaderStageKind.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanShaderStageDescriptor.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanVertexAttributeFormat.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanVertexAttributeDescriptor.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanVertexBufferLayoutDescriptor.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineDescriptor.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineStatus.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineDiagnostic.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineCreateResult.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/AurelianVulkanGraphicsPipeline.cs`
- `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineFactory.cs`
- `tests/Aurelian.Graphics.Tests/VulkanGraphicsPipelineM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0039-a39-graphics-pipeline-descriptor-state-m0.md`

## 2. Task scope

A39 implements graphics pipeline creation only. It adds Aurelian-owned descriptor/result/diagnostic records for raw SPIR-V graphics shader stages, optional vertex input, fixed M0 graphics state, and native Vulkan pipeline creation against an explicit existing render pass.

The milestone remains intentionally narrow. It does not implement shader compilation, SDSL-V integration, DXC/Vortice.Dxc, assets/shaders dependencies, descriptor sets, uniform buffers, push constants, pipeline bind commands, vertex binding command emission, draw commands, pipeline caches, compute pipelines, swapchains/windows/surfaces, VMA/VMASharp, Vortice.Vulkan, service locators, globals, reflection, or vendor/reference-code changes.

## 3. Reference material read

Read current Aurelian graphics state and prior audits:

- `docs/audits/0038-a38-render-pass-begin-end-command-m0.md`
- `docs/audits/0036-a36-render-pass-descriptor-m0.md`
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- render pass, framebuffer, command buffer, and plant code under `src/Aurelian.Graphics/Vulkan`

Read Claude intent-port sections in `docs/claude/aurelian-vulkan-intent-port-audit.md`, especially pipeline state, render pass abstraction, descriptor layout, pipeline cache, and do-not-carry-over guidance.

Read Stride pipeline references via the requested `rg` search into `/tmp/a39-stride-pipeline-search.txt` and `CodeReferences/Stride/Stride.Graphics/Vulkan/PipelineState.Vulkan.cs`. Borrowed intent: pipeline state lowers explicit shader stages, vertex input, input assembly, rasterization, multisample, color blend, dynamic viewport/scissor, pipeline layout, and render pass compatibility into `VkGraphicsPipelineCreateInfo`.

Stride pitfalls avoided:

- no all-stages-share-same-bytecode constraint;
- no implicit render pass creation inside pipeline state;
- no long-term `VkPipelineCache.Null` assumption exposed as policy;
- no unconditional depth bias;
- no ignored multisampling request hidden behind a public option;
- no descriptor set layout magic or implicit descriptor mapping.

Aurelian improvements in A39:

- raw SPIR-V is supplied per stage;
- render pass dependency is explicit through `AurelianVulkanRenderPass`;
- pipeline layout is empty for M0 by design;
- descriptor sets, push constants, pipeline cache, and shader artifact production are deferred instead of stubbed.

## 4. Shader stage descriptor model

`VulkanShaderStageKind` supports `Vertex` and `Fragment` only. `VulkanShaderStageDescriptor` carries the stage, entry point name, and `IReadOnlyList<uint>` SPIR-V words. M0 requires exactly one vertex shader and exactly one fragment shader.

Validation rejects missing stages, duplicate stages, null/empty/whitespace entry points, null stage descriptors, and empty SPIR-V word lists. A39 does not parse, validate semantically, or compile shader source. Native Vulkan remains responsible for rejecting malformed non-empty SPIR-V during shader module or graphics pipeline creation.

## 5. Vertex input model

`VulkanVertexBufferLayoutDescriptor` defines a binding and stride. `VulkanVertexAttributeDescriptor` defines a location, binding, `VulkanVertexAttributeFormat`, and byte offset. M0 formats are:

- `Float2` -> `Format.R32G32Sfloat`
- `Float3` -> `Format.R32G32B32Sfloat`
- `Float4` -> `Format.R32G32B32A32Sfloat`

All bindings use `VertexInputRate.Vertex`. Empty vertex input is valid for shader-only tests and future `gl_VertexIndex`-style shaders. Validation rejects null vertex input lists, duplicate bindings, zero strides, duplicate locations, and attributes that reference unknown bindings.

## 6. Pipeline descriptor model

`VulkanGraphicsPipelineDescriptor` contains shader stages, vertex buffer layouts, vertex attributes, and M0 depth toggles. The native factory uses fixed state:

- primitive topology: triangle list;
- polygon mode: fill;
- cull mode: none;
- front face: counter-clockwise;
- viewport/scissor: dynamic;
- color blend: disabled, RGBA write mask enabled;
- sample count: 1;
- render pass: explicit supplied `AurelianVulkanRenderPass`, subpass 0;
- depth/stencil: absent.

Depth test/write requests are rejected until depth render pass attachments and depth/stencil pipeline state are implemented.

## 7. Native pipeline creation/disposal

`VulkanGraphicsPipelineFactory.Create(...)` validates plant/render pass ownership, disposed render pass state, shader stages, vertex input, and unsupported depth state before native calls. It creates one temporary `VkShaderModule` per shader stage, creates an empty `VkPipelineLayout`, and creates one `VkPipeline` with `vkCreateGraphicsPipelines`.

Shader modules are destroyed after pipeline creation regardless of success/failure. On failure, the factory destroys any created shader modules and pipeline layout before returning a failed result with diagnostics. `AurelianVulkanGraphicsPipeline` owns only the successful native `VkPipeline` and `VkPipelineLayout`; `Dispose()` destroys both idempotently and does not own the render pass or shader source data.

## 8. Shader/SPIR-V strategy

A39 consumes raw SPIR-V word arrays and intentionally does not produce them. This keeps the Vulkan pipeline milestone independent from shader compiler internals and from any asset pipeline.

The intended future shader artifact flow remains:

```text
SDSL-V
  -> HLSL or Slang as compiler-facing MIR
  -> DXC
  -> SPIR-V artifact
  -> Vulkan pipeline creation
```

There is no direct SDSL-V-to-SPIR-V implementation plan in A39. Because no checked-in valid SPIR-V fixture was introduced, native-available tests verify that invalid non-empty SPIR-V fails cleanly through shader module or graphics pipeline diagnostics rather than asserting successful pipeline creation.

## 9. Tests added

Added `tests/Aurelian.Graphics.Tests/VulkanGraphicsPipelineM0Tests.cs` covering:

- Vulkan unavailable clean skip behavior;
- missing vertex shader rejection;
- missing fragment shader rejection;
- duplicate shader stage rejection;
- empty SPIR-V rejection;
- invalid entry point rejection;
- unsupported depth state rejection;
- invalid vertex input rejection;
- disposed render pass rejection;
- invalid SPIR-V native failure as a clean result;
- graphics pipeline disposal idempotence if a pipeline is ever created by the supplied descriptor/runtime.

Tests create a real Vulkan plant and A36 render pass only when the runtime is available. They do not require a window, surface, swapchain, framebuffer, command buffer, draw command, DXC, shader asset, or descriptor system.

## 10. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Vortice|Vortice\.Dxc|DXC|Dxc|SDSL|Aurelian.Shaders|Aurelian.Assets|VMASharp|Vma|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Draw|vkCmdDraw|vkCmdBindPipeline|vkCreateFramebuffer|vkCmdBeginRenderPass|vkCmdEndRenderPass|Aurelian.World|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCreateGraphicsPipelines|CreateGraphicsPipelines|vkCreatePipelineLayout|CreatePipelineLayout|vkCreateShaderModule|CreateShaderModule|vkDestroyPipeline|DestroyPipeline|vkDestroyPipelineLayout|DestroyPipelineLayout|vkDestroyShaderModule|DestroyShaderModule" src/Aurelian.Graphics/Vulkan/Pipelines/Graphics tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

## 11. Validation results

Validation result:

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed across the solution, including 151 graphics tests.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed with 151 tests.
- The broad dependency boundary scan produced only expected pre-existing hits such as Silk.NET `StructureType` names, existing A35 upload `vkCmdCopyBufferToImage` text, existing A37/A38 framebuffer/render-pass command text, the existing `Vma` enum case, and test reflection coverage; it did not show new DXC, Vortice, SDSL-V, shader/assets project, swapchain/window/surface, draw, bind-pipeline, service locator, singleton, vendor, or reference-code coupling from A39.
- The native-call isolation scan showed the new shader module, pipeline layout, graphics pipeline, and destroy calls isolated to `src/Aurelian.Graphics/Vulkan/Pipelines/Graphics`.

## 12. Deferred features

Deferred features:

- SDSL-V integration;
- HLSL/Slang/DXC/SPIR-V artifact production;
- valid checked-in shader fixture package;
- descriptor sets and descriptor set layouts;
- uniform buffers;
- push constants;
- pipeline bind command emission;
- vertex/index binding command emission;
- draw commands;
- pipeline cache and compatibility keys;
- depth/stencil attachments and state;
- MSAA;
- compute pipelines;
- swapchain/window/surface;
- VMA/VMASharp;
- Vortice.Vulkan;
- Vortice.Dxc package adoption.

## 13. Next recommendation

Recommended next milestone: `A40 — Vortice.Dxc package spike / DXC backend audit`.

Reason: A39 now consumes SPIR-V artifacts through explicit pipeline descriptors. The next missing source-side step is evidence about how Aurelian should produce those artifacts from its SDSL-V/HLSL path through DXC without coupling `Aurelian.Graphics` to shader compiler internals or making DXC mandatory for normal tests.
