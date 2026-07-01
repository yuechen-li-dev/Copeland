# A38 — Vulkan render pass begin/end command M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanColorClearValue.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassBeginRequest.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandStatus.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandDiagnostic.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandResult.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandEncoder.cs`
- `tests/Aurelian.Graphics.Tests/VulkanRenderPassCommandM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/graphics-memory-allocation.md`
- `docs/audits/0038-a38-render-pass-begin-end-command-m0.md`

## 2. Task scope

A38 implements render pass boundary command emission only. It records render pass begin/end into an existing Vulkan command buffer for existing A36/A37 render pass and framebuffer resources. It supports exactly one color clear value and derives the render area from the framebuffer extent.

A38 does not implement graphics pipelines, draw commands, vertex/index binding, descriptor sets, framebuffer caching, depth/stencil, MSAA, swapchains/windows/surfaces, VMA/VMASharp, Vortice, or any vendor/reference-code modifications.

## 3. Reference material read

Read current Aurelian graphics state and the A36/A37 audits before implementation:

- `docs/audits/0037-a37-framebuffer-m0.md`
- `docs/audits/0036-a36-render-pass-descriptor-m0.md`
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- render pass/framebuffer/command buffer/texture code under `src/Aurelian.Graphics/Vulkan`

Read Claude intent-port guidance in `docs/claude/aurelian-vulkan-intent-port-audit.md`. The borrowed intent is explicit per-plant command lists, explicit render pass descriptors, render pass begin/end discipline, and avoiding shared mutable native layout state.

Read Stride references through `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`, especially `EnsureRenderPass()` and `CleanupRenderPass()`. Borrowed intent: Vulkan render pass boundaries are command-buffer records around a framebuffer/render-pass pair with inline subpass contents. Pitfalls avoided:

- no implicit lazy render-pass begin hidden in draw paths;
- no render pass cleanup coupled to barriers;
- no clear path that ends/restarts a render pass just to clear;
- no framebuffer cache or fixed-slot framebuffer key complexity.

## 4. Render pass command model

A38 adds `Aurelian.Graphics.Vulkan.Commanding.RenderPasses` as a command namespace. The public M0 API is:

- `VulkanColorClearValue` with transparent/opaque black defaults;
- `VulkanRenderPassBeginRequest` carrying render pass, framebuffer, and clear color;
- `VulkanRenderPassCommandEncoder.Begin(...)`;
- `VulkanRenderPassCommandEncoder.End(...)`;
- result/status/diagnostic types with stable `AGRP` codes.

The encoder keeps active render pass state local to the encoder instance: active command buffer, active render pass, and active framebuffer. This gives M0 double-begin and end-without-begin validation without adding global state or mutating command-buffer lease state.

## 5. Begin/end validation

Begin validates:

- target plant and command buffer are non-null;
- command buffer is recording;
- command buffer belongs to the target plant;
- no render pass is already active in the encoder;
- render pass is present, non-disposed, and has a native handle;
- framebuffer is present, non-disposed, and has a native handle;
- render pass and framebuffer belong to the target plant;
- framebuffer was created for the render pass supplied in the request.

End validates:

- target plant and command buffer are non-null;
- command buffer is recording;
- command buffer belongs to the target plant;
- this encoder has an active render pass;
- the end command uses the same command buffer that began the active render pass;
- active resources still match the target plant.

## 6. Native command emission

Begin creates a `RenderPassBeginInfo` with:

- render pass = `AurelianVulkanRenderPass.NativeRenderPass`;
- framebuffer = `AurelianVulkanFramebuffer.NativeFramebuffer`;
- render area offset `(0, 0)`;
- render area extent = framebuffer width/height;
- one color `ClearValue` mapped from `VulkanColorClearValue`;
- subpass contents = `Inline`.

It then records `CmdBeginRenderPass`. End records `CmdEndRenderPass` and clears the encoder's local active render-pass state.

## 7. Tests added

Added `tests/Aurelian.Graphics.Tests/VulkanRenderPassCommandM0Tests.cs` covering:

- Vulkan-unavailable clean skip pattern;
- command buffer not recording rejection;
- plant mismatch rejection;
- disposed render pass rejection;
- disposed framebuffer rejection;
- double-begin rejection;
- end-without-begin rejection;
- successful begin/end recording when a Vulkan plant is available.

Tests create real A24-A37 resources when Vulkan is available: plant, raw allocator, color texture, render pass, framebuffer, command buffer pool, command-buffer lease, and render pass encoder. No queue submit, pipeline, draw, swapchain, or window is used.

## 8. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "VMASharp|Vma|Vortice|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Pipeline|Draw|vkCmdDraw|vkCreateGraphicsPipelines|vkCmdCopyBufferToImage|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCmdBeginRenderPass|CmdBeginRenderPass|vkCmdEndRenderPass|CmdEndRenderPass" src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

## 9. Validation results

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed. The graphics test project reported 140 passing tests.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed with 140 passing tests.
- Boundary grep showed expected existing false positives for Vulkan `StructureType`, barrier `PipelineStageFlags`/`CmdPipelineBarrier`, A36 render-pass `PipelineBindPoint.Graphics`, and existing tests/docs strings. It showed no VMA/VMASharp, Vortice, swapchain/window/surface implementation, draw command implementation, service locator, or new reference/vendor dependency in the A38 command folder.
- Render pass begin/end command grep showed the expected command calls only in `VulkanRenderPassCommandEncoder`.

## 10. Deferred features

Deferred beyond A38:

- graphics pipeline descriptors/state;
- graphics pipeline creation/binding;
- draw commands;
- vertex/index binding;
- descriptor sets;
- framebuffer cache;
- depth/stencil;
- MSAA;
- multiple color attachments;
- swapchain/window/surface;
- VMA/VMASharp;
- Vortice.

## 11. Next recommendation

Recommended next milestone: **A39 — Graphics pipeline descriptor/state M0**.

Reason: command buffers can now enter and leave render passes around existing framebuffers, but there is still no graphics pipeline state object to bind inside the pass and no path toward later draw command validation.
