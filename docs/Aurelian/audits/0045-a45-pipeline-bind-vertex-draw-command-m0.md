# A45 — Pipeline bind + vertex draw command M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Commanding/VulkanCommandBufferLease.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassScope.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassBeginResult.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandEncoder.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanViewportScissor.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawVerticesRequest.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawCommandStatus.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawCommandDiagnostic.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawCommandDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawCommandResult.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Draw/VulkanDrawCommandEncoder.cs`
- `tests/Aurelian.Graphics.Tests/VulkanRenderPassCommandM0Tests.cs`
- `tests/Aurelian.Graphics.Tests/VulkanDrawCommandM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0045-a45-pipeline-bind-vertex-draw-command-m0.md`

## 2. Task scope

A45 implements one explicit Vulkan graphics draw command path in `Aurelian.Graphics`: require an active render pass scope, set viewport/scissor, bind a graphics pipeline, bind one vertex buffer at binding 0 with offset 0, and record a non-indexed `vkCmdDraw` with instance count 1 and first instance 0.

The milestone intentionally does not implement index buffers, descriptor sets, uniform buffers, push constants, multiple vertex buffers, pipeline state caching, swapchains/windows/surfaces, presentation, render-target scheduling, asset/shader-pipeline integration, or `RenderCommandPlan` execution.

## 3. Render pass scope design

Render pass begin now returns a typed `VulkanRenderPassScope` through `VulkanRenderPassBeginResult`. The scope carries the plant id, process-local command buffer lease id, and process-local render pass scope id. These ids are validation/diagnostic identities, not global graphics-device state.

Draw and render pass end both require the scope token and validate it against the command buffer lease that currently owns the active render pass. This avoids the A38 encoder-local-only state model and avoids relying on Vulkan validation layers to discover draw commands issued outside a render pass.

## 4. Command buffer active render pass state

`VulkanCommandBufferLease` now owns the minimal active render-pass state required by A45:

- `LeaseId`
- `HasActiveRenderPass`
- internal `ActiveRenderPassScope`
- `MarkRenderPassActive(PlantId)`
- `TryClearRenderPass(VulkanRenderPassScope)`
- `IsActiveScope(VulkanRenderPassScope)`

The state is cleared when command buffers reset, begin a fresh recording session, successfully end recording, retire, or dispose. No pipeline binding state, descriptor state, framebuffer cache, or draw-state cache was added.

## 5. Draw request/validation model

`VulkanDrawVerticesRequest` contains exactly the A45 draw inputs: graphics pipeline, one vertex buffer, vertex count, first vertex, and viewport/scissor. `VulkanDrawCommandResult` and diagnostics use stable `AGD` codes.

`VulkanDrawCommandEncoder.DrawVertices(...)` validates:

- command buffer is recording;
- command buffer has an active render pass;
- supplied render pass scope is active on the command buffer lease;
- plant ownership matches for command buffer, pipeline, and vertex buffer;
- pipeline is present, not disposed, and has a native handle;
- vertex buffer is present, not disposed, has a native handle, and includes `VulkanBufferUsage.Vertex`;
- vertex count is greater than zero;
- viewport/scissor width and height are positive, finite, and depth bounds are inside `[0, 1]` with `MinDepth <= MaxDepth`.

## 6. Native command recording

After validation succeeds, draw recording emits:

1. `CmdSetViewport`
2. `CmdSetScissor`
3. `CmdBindPipeline` with `PipelineBindPoint.Graphics`
4. `CmdBindVertexBuffers` with binding 0 and offset 0
5. `CmdDraw(vertexCount, instanceCount: 1, firstVertex, firstInstance: 0)`

A45 explicitly avoids the Stride `DrawInstanced` first-instance pitfall: this M0 non-instanced draw hardcodes `firstInstance` to `0` and does not expose instancing yet.

## 7. Tests added

Added `tests/Aurelian.Graphics.Tests/VulkanDrawCommandM0Tests.cs` covering:

- command buffer not recording rejection;
- no active render pass rejection;
- invalid render pass scope rejection;
- disposed pipeline rejection when a native pipeline fixture can be created;
- disposed vertex buffer rejection;
- buffer without vertex usage rejection;
- zero vertex count rejection;
- invalid viewport rejection;
- Vulkan-available draw recording path shape, gated by whether a native graphics pipeline can be created from current test fixtures.

Updated render pass command tests to consume `VulkanRenderPassBeginResult.Scope` and pass `VulkanRenderPassScope` to render pass end.

## 8. Boundary checks

Commands run:

```bash
git status --short
find src/Aurelian.Graphics -type f | sort
find tests/Aurelian.Graphics.Tests -type f | sort
sed -n '1,520p' docs/audits/0038-a38-render-pass-begin-end-command-m0.md || true
sed -n '1,520p' docs/audits/0044-a44-pipeline-consumes-compiled-shader-program-m0.md || true
sed -n '1,520p' docs/audits/0039-a39-graphics-pipeline-descriptor-state-m0.md || true
sed -n '1,260p' docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md
find src/Aurelian.Graphics/Vulkan/Commanding -type f | sort
find src/Aurelian.Graphics/Vulkan/Pipelines -type f | sort
find src/Aurelian.Graphics/Vulkan/Resources/Buffers -type f | sort
find src/Aurelian.Graphics/Vulkan/Resources/Allocation -type f | sort
sed -n '1,760p' src/Aurelian.Graphics/Vulkan/Commanding/VulkanCommandBufferLease.cs
sed -n '1,760p' src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandEncoder.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/AurelianVulkanGraphicsPipeline.cs
sed -n '1,760p' src/Aurelian.Graphics/Vulkan/Pipelines/Graphics/VulkanGraphicsPipelineFactory.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Resources/Buffers/AurelianVulkanBuffer.cs
sed -n '1,620p' src/Aurelian.Graphics/Vulkan/Resources/Buffers/VulkanBufferUsage.cs
rg -n "Draw|vkCmdDraw|DrawInstanced|startInstance|BindPipeline|BindVertex|Viewport|Scissor|RenderPass|CommandList|Do Not Carry Over" docs/claude/aurelian-vulkan-intent-port-audit.md
rg -n "vkCmdDraw|DrawInstanced|Draw\(|BindVertex|vkCmdBindVertexBuffers|vkCmdBindPipeline|vkCmdSetViewport|vkCmdSetScissor|EnsureRenderPass|PipelineState" CodeReferences/Stride/Stride.Graphics -g '*.cs' > /tmp/a45-stride-draw-search.txt || true
wc -l /tmp/a45-stride-draw-search.txt
head -n 260 /tmp/a45-stride-draw-search.txt
sed -n '1,260p' CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs || true
dotnet build src/Aurelian.Graphics/Aurelian.Graphics.csproj -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Vortice|VMASharp|Vma|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Aurelian.World|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCmdBindPipeline|CmdBindPipeline|vkCmdBindVertexBuffers|CmdBindVertexBuffers|vkCmdSetViewport|CmdSetViewport|vkCmdSetScissor|CmdSetScissor|vkCmdDraw|CmdDraw" src/Aurelian.Graphics/Vulkan/Commanding/Draw tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

Boundary-search notes:

- Shader/DXC search hits are existing negative-reference assertions in compiled shader mapping tests, not production dependencies.
- The broad forbidden-token search reports existing Silk.NET `StructureType` usages, existing allocator enum value `Vma`, and an existing plant registry reflection-style property inspection test; no new A45 dependency on Vortice, VMA/VMASharp, surfaces/windows/swapchains, vendor code, or service locators was added.
- Draw command search reports only the new A45 encoder native command calls.

## 9. Validation results

- `dotnet build src/Aurelian.Graphics/Aurelian.Graphics.csproj -c Debug` passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed: 178 tests.
- `dotnet build Aurelian.slnx -c Debug` passed.
- `dotnet test Aurelian.slnx -c Debug` passed.
- The final graphics test run passed: 178 tests.

A45 reaches meaningful progression/success for the validated command API and native emission path. The only remaining proof gap is not the draw encoder itself but the lack of a stable valid SPIR-V fixture in graphics tests; native draw success is therefore gated on current pipeline fixture availability rather than introducing a shader-compiler dependency into graphics.

## 10. Deferred features

Deferred deliberately:

- valid graphics-owned SPIR-V fixture generation or fixture ingestion;
- index buffers and indexed draw;
- descriptor sets;
- uniform buffers;
- push constants;
- instancing;
- multiple vertex buffers;
- pipeline binding state cache;
- `RenderCommandPlan` execution;
- swapchain/window/surface/presentation;
- shader/assets integration;
- VMA/VMASharp, Vortice, or vendor/reference-code adoption.

## 11. Next recommendation

A46 — Valid SPIR-V fixture / first offscreen draw recording proof.

This should provide a graphics-owned valid SPIR-V vertex/fragment fixture or a neutral fixture bridge that does not add an `Aurelian.Graphics -> Aurelian.Shaders` dependency. With that fixture, A46 can prove the native pipeline creation plus render pass begin plus `DrawVertices` plus render pass end path without expanding into swapchains, descriptors, uniforms, or presentation.

## Reference extraction summary

Stride draw intent borrowed:

- draw path records viewport/scissor, pipeline bind, vertex buffer bind, and `vkCmdDraw` on a command buffer;
- graphics pipeline bind point is explicit;
- render pass boundaries matter around draw commands.

Stride pitfalls avoided:

- no implicit render pass begin hidden in draw path (`EnsureRenderPass` remains reference-only intent, not Aurelian behavior);
- no `DrawInstanced` first-instance bug: A45 non-instanced draw uses `firstInstance = 0`;
- no binding state hidden in a large command-list object;
- no descriptor/pipeline/render-pass coupling in the draw command;
- no framebuffer cache or presentation path pulled into draw M0.

Aurelian improvements:

- explicit render pass scope returned by begin;
- command buffer lease owns the active render pass state;
- draw commands require and validate the scope token;
- single validated draw request rather than an implicit mutable command-list state machine;
- no hidden state outside the command buffer lease.
