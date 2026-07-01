# A37 — Vulkan Framebuffer M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Pipelines/Framebuffers/` with Aurelian-owned framebuffer descriptor, diagnostics, result/status, factory, and native owner.
- Added `tests/Aurelian.Graphics.Tests/VulkanFramebufferM0Tests.cs`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with A37 status/dependency/allocation notes.

## 2. Task scope

A37 is limited to native framebuffer creation/lifetime M0 over the existing A36 render pass and A33 Texture2D layers. It implements exactly one color attachment. It does not implement command-buffer render pass begin/end, pipeline creation, draw commands, swapchain/window/surface support, depth/stencil, MSAA, MRT, descriptor sets, VMA/VMASharp, Vortice, or framebuffer caching.

## 3. Reference material read

Read the A36 render pass audit, A33 texture resource audit, and A21 Vulkan intent-port plan before coding. Read the Claude Vulkan intent-port audit sections covering framebuffers, render passes, attachments, image views, and Stride pitfalls. Read Stride framebuffer references by searching `CodeReferences/Stride/Stride.Graphics` for framebuffer/render-pass/image-view creation and by inspecting `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`.

Stride framebuffer intent borrowed:

- a framebuffer is a native object made from a render pass plus attachment image views;
- framebuffer dimensions come from the bound attachments/render target surface;
- render-pass compatibility matters before command recording can begin.

Stride pitfalls avoided:

- no fixed attachment-slot framebuffer key;
- no implicit render-pass/framebuffer creation hidden inside a command list;
- no framebuffer cache before descriptors and compatibility keys are stable;
- no render pass begin/end coupling in this milestone.

Aurelian improvements:

- explicit framebuffer descriptor;
- one color attachment M0;
- no cache yet;
- native owner/disposal with no ownership of render pass or textures.

## 4. Framebuffer descriptor model

`VulkanFramebufferDescriptor` is plain Aurelian-owned data containing width, height, and `IReadOnlyList<AurelianVulkanTexture>` color attachments. M0 intentionally accepts exactly one color attachment texture.

## 5. Compatibility validation

`VulkanFramebufferFactory.Create(...)` validates before native calls:

- nonzero framebuffer dimensions;
- live plant/device and live render pass;
- render pass belongs to the requested plant;
- exactly one color attachment for M0;
- attachment exists and is not disposed;
- attachment belongs to the requested plant;
- attachment size matches descriptor size;
- attachment includes `VulkanTextureUsage.ColorAttachment`;
- attachment has a native image view;
- render pass descriptor has one color attachment;
- render pass color attachment format matches texture format.

Validation failures return `VulkanFramebufferStatus.Rejected` and diagnostic codes in the `AGFB1001`-`AGFB1012` range.

## 6. Native framebuffer creation/disposal

On a valid descriptor, the factory calls `vkCreateFramebuffer` with the existing render pass and the texture's native image view. Creation failures return `VulkanFramebufferStatus.Failed` with `AGFB1011`.

`AurelianVulkanFramebuffer` stores plant, dimensions, descriptor, and render pass reference for compatibility context. It owns only the native `VkFramebuffer`; disposal calls `vkDestroyFramebuffer` once and is idempotent. It does not dispose the render pass or attachment textures.

## 7. Tests added

Added tests for:

- Vulkan-unavailable clean skip behavior;
- invalid dimensions rejection;
- no color attachments rejection;
- multiple color attachments rejection;
- attachment size mismatch rejection;
- attachment missing color usage rejection;
- render-pass/texture format mismatch rejection;
- one-color framebuffer creation success or clean failure;
- idempotent framebuffer disposal;
- framebuffer disposal not disposing render pass or texture.

## 8. Boundary checks

Planned and executed checks:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "VMASharp|Vma|Vortice|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Pipeline|Draw|vkCmdDraw|vkCreateGraphicsPipelines|vkCmdBeginRenderPass|vkCmdEndRenderPass|vkCmdCopyBufferToImage|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCreateFramebuffer|CreateFramebuffer|vkDestroyFramebuffer|DestroyFramebuffer" src/Aurelian.Graphics/Vulkan/Pipelines/Framebuffers tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

## 9. Validation results

`dotnet build Aurelian.slnx -c Debug` passed during implementation. The final test and boundary-check results are recorded in the task transcript/final response.

## 10. Deferred features

Deferred:

- command-buffer render pass begin/end;
- graphics pipeline descriptors/native pipelines;
- draw commands;
- swapchain/window/surface;
- depth/stencil;
- MSAA;
- multiple render targets;
- descriptor sets;
- framebuffer cache;
- VMA/VMASharp;
- Vortice.

## 11. Next recommendation

A38 — Render pass begin/end command M0.

Reason: render pass and framebuffer owners now exist, so command-buffer render pass begin/end is the next direct dependency before any meaningful draw path.
