# A36 — Render pass descriptor M0

## 1. Files changed

- Added `src/Aurelian.Graphics/Vulkan/Pipelines/RenderPasses/` with attachment load/store enums, color attachment descriptors, render pass descriptors, diagnostics/status/result records, the native render pass owner, and the render pass factory.
- Added `tests/Aurelian.Graphics.Tests/VulkanRenderPassM0Tests.cs` for Vulkan-optional creation, validation rejection, disposal, and boundary checks.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/dependency-policy.md`, and `docs/architecture/graphics-memory-allocation.md` with A36 scope, deferrals, and dependency/allocation notes.

## 2. Task scope

A36 implements render pass descriptor/native render pass M0 only:

- Aurelian-owned render pass descriptor records;
- color attachment descriptor M0;
- caller-selected load/store operations;
- format mapping for the existing `VulkanTextureFormat` family;
- initial/final layout mapping through the existing barrier layout facts;
- native `VkRenderPass` creation;
- native render pass owner/disposal;
- structured diagnostics for validation and creation failures;
- tests that pass whether Vulkan is available or unavailable.

No framebuffer, graphics pipeline, draw command, command-list begin/end render pass, swapchain/window/surface, depth/stencil, MSAA, descriptor set, VMA/VMASharp, Vortice, global singleton/service locator, or reflection work is introduced.

## 3. Reference material read

Read before implementation:

- `docs/audits/0035-a35-texture-upload-m0.md` for the latest graphics state and A35 deferrals.
- `docs/audits/0034-a34-barrier-command-emission-m1.md` for the layout/barrier mapping boundary.
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md` for the Vulkan intent-port plan.
- `docs/claude/aurelian-vulkan-intent-port-audit.md` render pass, framebuffer, and pipeline sections.
- Stride render pass and pipeline references via `CodeReferences/Stride/Stride.Graphics/Vulkan/PipelineState.Vulkan.cs` and `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`.

Stride render pass intent borrowed:

- Native render passes describe attachment formats, load/store behavior, attachment layouts, and subpass attachment references.
- A graphics subpass uses color attachment references and Vulkan subpass dependencies to express attachment synchronization around the pass.
- Pipelines will eventually need render-pass compatibility data.

Stride pitfalls avoided:

- No implicit render pass creation buried inside pipeline state.
- No unconditional `loadOp = Load`; the descriptor chooses `Load`, `Clear`, or `DontCare`.
- No render pass/framebuffer coupling; A36 creates no framebuffer and stores no attachment image views.
- No swapchain format assumptions; the descriptor uses Aurelian texture formats supplied by the caller.

Aurelian improvements:

- Render pass descriptors are explicit plain data.
- Native render passes are compiled from descriptors and owned/disposed by `AurelianVulkanRenderPass`.
- Future pipeline compatibility keys can derive from descriptor data/hash instead of hidden backend state.

## 4. Render pass descriptor model

`VulkanRenderPassAttachmentDescriptor` contains attachment name, `VulkanTextureFormat`, `VulkanAttachmentLoadOp`, `VulkanAttachmentStoreOp`, initial layout, and final layout.

`VulkanRenderPassDescriptor` contains the color attachment list. M0 validates that at least one color attachment exists and rejects more than one color attachment with `AGR1002 MultipleColorAttachmentsUnsupported`. Multiple render targets are intentionally deferred.

## 5. Format/layout/load-store mapping

Format mapping mirrors the current texture M0 format family:

- `Rgba8Unorm` -> `R8G8B8A8Unorm`;
- `Bgra8Unorm` -> `B8G8R8A8Unorm`;
- `Rgba8Srgb` -> `R8G8B8A8Srgb`;
- `Bgra8Srgb` -> `B8G8R8A8Srgb`.

Load/store mapping is descriptor-driven:

- `Load`, `Clear`, and `DontCare` map to Vulkan attachment load ops;
- `Store` and `DontCare` map to Vulkan attachment store ops.

M0 accepted initial layouts are `Undefined`, `ColorAttachment`, and `Present`. M0 accepted final layouts are `ColorAttachment`, `ShaderResourceFragment`, `Present`, and `TransferSource`. Layouts are converted through `VulkanBarrierMappings.Map(...)` so render pass layout facts stay aligned with the existing barrier vocabulary.

## 6. Native render pass creation/disposal

`VulkanRenderPassFactory.Create(...)` validates the descriptor, builds one `AttachmentDescription`, one `AttachmentReference`, one graphics `SubpassDescription`, and two simple external/subpass dependencies, then calls `CreateRenderPass`.

`AurelianVulkanRenderPass` owns the native handle, plant id, and descriptor. Disposal is idempotent and calls `DestroyRenderPass` only when the handle and device are live. The native handle is internal-only; there is no framebuffer, pipeline, or command-list integration in A36.

## 7. Tests added

Added `VulkanRenderPassM0Tests` covering:

- clean Vulkan-unavailable behavior;
- no-color-attachment rejection;
- multiple-color-attachment rejection;
- unsupported initial layout rejection;
- unsupported final layout rejection;
- single-color render pass creation success or clean native failure when a plant exists;
- idempotent render pass disposal;
- source boundary check that render pass M0 does not create deferred framebuffer/pipeline/draw/render-command objects.

## 8. Boundary checks

Executed boundary checks:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "VMASharp|Vma|Vortice|SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Framebuffer|Pipeline|Draw|vkCmdDraw|vkCreateFramebuffer|vkCreateGraphicsPipelines|vkCmdBeginRenderPass|vkCmdEndRenderPass|vkCreateImage|vkCmdCopyBufferToImage|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkCreateRenderPass|CreateRenderPass|vkDestroyRenderPass|DestroyRenderPass|AttachmentDescription|SubpassDescription|SubpassDependency" src/Aurelian.Graphics/Vulkan/Pipelines/RenderPasses tests/Aurelian.Graphics.Tests -g '*.cs' || true
```

The broad forbidden-term scan still reports pre-existing or unavoidable Vulkan vocabulary such as the `Pipelines` namespace/folder, `PipelineStageFlags`, prior texture `vkCreateImage` work, prior upload `vkCmdCopyBufferToImage` work, and existing `GetType()` usage in older diagnostics/tests. A36 adds no framebuffer creation, graphics pipeline creation, draw command, begin/end render pass command, swapchain/window/surface, VMA/VMASharp, Vortice, CodeReferences, Dominatus, or vendor changes.

## 9. Validation results

Validation result:

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed all tests.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` passed 122 tests.

## 10. Deferred features

Deferred from A36:

- framebuffer creation and ownership;
- graphics pipeline descriptor/state;
- draw commands;
- command-list render pass begin/end;
- swapchain/window/surface;
- depth/stencil attachments;
- MSAA;
- multiple color attachments;
- descriptor sets;
- pipeline compatibility cache keys/hashing;
- render pass cache;
- VMA/VMASharp and Vortice.

## 11. Next recommendation

A37 — Framebuffer M0.

A native render pass now exists. The next dependency before command-list render pass begin/end is a framebuffer object that binds compatible image views to the render pass attachment slots without pulling in pipelines, draw commands, or swapchain/window/surface work.
