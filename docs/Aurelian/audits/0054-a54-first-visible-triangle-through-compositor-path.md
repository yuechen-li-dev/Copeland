# A54 — First visible triangle through compositor path

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Commanding/RenderPasses/VulkanRenderPassCommandEncoder.cs`
- `src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSwapchain.cs`
- `src/Aurelian.Graphics/Vulkan/Presentation/VulkanSwapchainFactory.cs`
- `src/Aurelian.Graphics/Vulkan/Resources/Barriers/VulkanLayoutTracker.cs`
- `tests/Aurelian.Graphics.Tests/VulkanRenderPassCommandM0Tests.cs`
- `tests/Aurelian.Graphics.Tests/VulkanVisibleTriangleThroughCompositorM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0054-a54-first-visible-triangle-through-compositor-path.md`

## 2. Task scope

A54 implements one visible-path proof, not a renderer. The new proof connects the existing offscreen draw path, compositor passthrough copy path, and swapchain acquire/present path through a single unavailable/headless-safe integration test.

The milestone does not add a frame loop, renderer facade, render graph, Dominatus compositor policy, differential compositor, multi-GPU transfer, descriptor sets, uniforms, index buffers, asset/TOML integration, shader compiler dependencies in graphics, VMA/VMASharp, Vortice, CodeReferences edits, or vendor/Dominatus edits.

## 3. Visible path chain

The test path is:

```text
presentation-enabled Vulkan plant
  -> swapchain
  -> acquire swapchain image
  -> presentation target wrappers
  -> offscreen color target texture
  -> render pass + framebuffer
  -> checked-in SPIR-V graphics pipeline
  -> vertex buffer upload
  -> record offscreen draw
  -> submit/wait offscreen draw
  -> wrap offscreen texture as PlantOutputRef
  -> dispatch compositor passthrough copy into acquired swapchain image
  -> present acquired image
```

If Vulkan, windowing, surface creation, swapchain creation, or acquire/present is unavailable in the current environment, the test asserts typed diagnostics and returns cleanly. In display-capable environments, it exercises the full acquire/draw/composite/present chain.

## 4. SPIR-V fixture use

A54 reuses the checked-in triangle SPIR-V fixture bytes from the graphics test fixture. It constructs a `CompiledShaderProgram` in the test from those bytes and SHA-256 hashes, with no runtime shader compilation and no `Aurelian.Shaders`, DXC, glslang, or shader compiler dependency added to `Aurelian.Graphics`.

The vertex input remains the A46 shape:

- binding `0`, stride `24` bytes;
- location `0`: `Float2`, offset `0`;
- location `1`: `Float4`, offset `8`.

## 5. Offscreen render setup

The test creates an offscreen color target with the selected swapchain format mapped to the existing M0 `VulkanTextureFormat` values. The offscreen extent matches the created swapchain extent, and the texture uses `ColorAttachment | TransferSource | TransferDestination` with `GpuOnly` memory and `Undefined` initial layout.

The render pass clears and stores the color attachment. Its final layout is `TransferSource`, which makes the rendered triangle texture a correct compositor source after the render pass completes.

## 6. Compositor passthrough use

The rendered offscreen texture is wrapped as:

```text
PlantOutputRef(plant 0, frame 54, "triangle.offscreen")
```

The acquired swapchain image is wrapped through `CreatePresentationTargetImageSet()` and addressed by a `PresentationTargetRef` containing the acquired swapchain image index. The dispatch request uses `CompositorPolicyKind.Passthrough`, so A54 uses the A53 mechanism path rather than bypassing the compositor with direct render-to-swapchain.

## 7. Present behavior

After compositor dispatch succeeds, the presentation target layout tracker is expected to be `Present`. The test then calls `swapchain.Present(acquiredImageIndex)` and accepts `Presented`/`Suboptimal` as success. `OutOfDate` and `Unavailable` remain clean environment results with diagnostics.

A54 also changes M0 acquire synchronization to use a per-acquire fence wait before returning an acquired image, which makes the synchronous proof path safe to record/copy/present without adding a present semaphore handoff yet. A54 also updates swapchain creation to request `TransferDst` image usage in addition to color attachment usage. If a surface cannot support the usage required by compositor copy, swapchain creation returns typed diagnostics instead of creating an image that the compositor cannot legally copy into.

## 8. Render pass final layout tracking

A54 adds a small production correctness fix: `VulkanRenderPassCommandEncoder` remembers the render pass/framebuffer pair for an active scope and, after successful `vkCmdEndRenderPass`, marks framebuffer attachment layout trackers to the render pass attachment final layouts.

This keeps the offscreen source tracker honest. Without the fix, a render pass could transition an image in Vulkan while the Aurelian tracker still reported `Undefined`, causing later compositor barrier planning to start from stale state.

`VulkanLayoutTracker` now exposes `TryMarkCurrentLayout(...)` for this non-emitting state update. It is intentionally separate from `Transition(...)`, because render-pass final-layout transitions are emitted by Vulkan render-pass execution rather than by an explicit image barrier command.

## 9. Tests added

- `VulkanVisibleTriangleThroughCompositor_WhenAvailable_AcquiresDrawsCompositesAndPresents`
- `VulkanRenderPassCommandEncoder_End_UpdatesAttachmentLayoutTrackersToFinalLayout`

The visible triangle test is not pixel validation. It validates that the real command/present conveyor belt can be assembled and executed when presentation is available, while remaining safe in headless CI.

## 10. Boundary checks

Boundary checks performed:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "Aurelian.Shaders|Dxc|DXC|Microsoft.Direct3D.DXC|SDSL|Sdslv|Hlsl|SpirvShaderArtifact" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Runtime|Dominatus|AiWorld|ActuatorHost|Hfsm|Blackboard|Aurelian.World|Aurelian.Rendering.Null|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "CreateVulkanSurface|AcquireNextImage|QueuePresent|CmdCopyImage|CmdDraw|CmdBeginRenderPass|CmdEndRenderPass|QueueSubmit" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

## 11. Validation results

Validation results for the final patch:

- `dotnet build Aurelian.slnx -c Debug` — passed.
- `dotnet test Aurelian.slnx -c Debug` — passed.
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug` — passed.
- Forbidden dependency searches only report expected assertion text, Silk.NET `StructureType` occurrences, and existing graphics mechanism references; no forbidden project/package dependency was added.

## 12. Deferred features

Deferred after A54:

- runtime/Dominatus compositor policy;
- present loop/frame pump;
- present semaphore integration beyond the current M0 synchronous proof path;
- differential compositor;
- multi-GPU and external-memory transfer;
- compute compositor pipeline;
- renderer facade and render graph;
- descriptors, uniforms, index buffers, texture sampling;
- assets/TOML integration;
- shader compiler integration in graphics;
- VMA/VMASharp and Vortice.

## 13. Next recommendation

A55 — Runtime Dominatus compositor policy M0.

Reason: A54 proves the graphics mechanism path exists from offscreen plant output through compositor passthrough to presentation. The next convergence step can begin policy in runtime/Dominatus without making `Aurelian.Graphics` depend on Dominatus and without hiding policy inside Vulkan object owners.

## Reference context extracted

A21's intended path to the first triangle was not direct swapchain rendering. The plan placed swapchain acquisition/presentation before the compositor seam, then required the first triangle path to pass through compositor passthrough before present. The Claude audit described the first-triangle sequence as `RenderSnapshot -> compositor passthrough -> present` and explicitly called out that the single-GPU first-triangle path should route through the compositor seam.

The compositor seam comes before the visible triangle because the architecture is multi-plant from day one. Even when plant 0 both renders and presents, plant outputs must be represented separately from presentation targets so that later runtime policy, multi-GPU transfer, reduced-frequency/differential composition, and Dominatus diagnostics can be added without retrofitting the presentation path.

What remains deferred is policy and cadence: Dominatus-driven dispatch decisions, frame-loop ownership, semaphore-ring present integration, swapchain recreation policy, differential/reduced-frequency policy, multi-GPU transfer, and full runtime renderer orchestration.
