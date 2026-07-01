# Aurelian Vulkan Intent Port Audit
## Stride.Graphics → Aurelian Render Pipeline

This document is a Codex briefing for the Aurelian graphics bring-up. For each subsystem it covers:
- **Intent** — what the subsystem must accomplish, stripped of Stride's accidents
- **Do Not Carry Over** — bugs, hacks, and legacy debt to leave behind
- **Aurelian Improvements** — where to do better than Stride

---

## Multi-GPU Design Mandate

Aurelian is designed for explicit multi-GPU from the start. The architectural model is **controller and plants**: each `VkDevice` is an isolated plant that accepts a render task, executes it, and returns an image and timing telemetry. The Dominatus session is the controller that decomposes frames into tasks, assigns them to plants using a policy, composites outputs, and adjusts future assignments based on feedback. On a single-GPU system the plant registry has exactly one entry and the code path is identical.

This model — worked out in the Oct/Prometheus graphics reactor design thread — enables a novel multi-GPU split strategy: **differential rendering**, where GPU assignment is determined per-pixel by visual complexity rather than spatial or temporal splits. Plant 0 is the expensive accurate renderer; plant 1 is a cheaper approximation. The compositor compares outputs pixel-by-pixel and uses plant 1's result where they agree, plant 0's where they diverge. The P15 shadow calibration HFSM tracks per-scene agreement confidence and adjusts how often plant 0 needs to run. Nobody does this — existing SFR (split-frame) and AFR (alternate-frame) approaches are coarser. Differential rendering is a data-dependent split determined automatically by the compositor.

**Three invariants that must be true from day one, regardless of single-GPU bring-up:**

1. `AurelianVulkanDevice` is a value type that can be held in a `PlantContext[]` array. No singleton, no global state.
2. All resource handles carry a `uint PlantId` field. Zero means plant 0. This costs nothing at bring-up and prevents a later architectural rewrite.
3. The `DispatchActs` boundary (from `wyrmcoil.rs` `World.DispatchActs`) is where plant assignment happens. Draw acts carry a `PlantId` hint. The actuator routes them to the correct plant's command list.

Sections below note where multi-GPU support specifically influenced the design decisions.

---

## 1. Device Initialization (`GraphicsDevice.Vulkan.cs`)

### Intent
Bootstrap a `VkDevice` from a `VkPhysicalDevice`. The sequence is:
1. Query `vkGetPhysicalDeviceProperties` for limits (UBO alignment, timestamp period).
2. Cap descriptor type counts against device limits.
3. Query `vkGetPhysicalDeviceFeatures2` via a pNext chain: timeline semaphores (required), float16, uniform buffer standard layout, multiview (optional), portability subset (MoltenVK only).
4. Enumerate and enable only extensions that are actually present. Required: `VK_KHR_swapchain`. Conditional: `VK_KHR_portability_subset` (must enable if present — MoltenVK rule).
5. Fail hard if timeline semaphores are not supported — Aurelian's sync model depends on them.
6. Create a single graphics+compute queue from the best-scoring queue family.
7. Allocate three timeline semaphore fences: `FrameFence`, `CommandListFence`, `CopyFence`.
8. Create `ThreadLocal<CommandBufferPool>` for copy operations.
9. Create `HeapPool` (fence-tagged `VkDescriptorPool` recycler).
10. Create a 4MB+ ring upload buffer (`HOST_VISIBLE | HOST_COHERENT`), mapped persistently.
11. Create sentinel empty textures/buffers for null descriptor slots.

### Do Not Carry Over
- **`GraphicsDeviceStatus` is a dead stub.** The `GraphicsDeviceStatus` property has all its D3D11 device-lost detection commented out and always returns `Normal`. Aurelian should either implement real lost-device detection or not expose the concept.
- **Single queue family index 0 hardcoded.** `queueFamilyIndex = 0` is passed without selecting the best graphics-capable family. On some devices this is incorrect.
- **`AllocateMemory` does per-allocation `vkGetPhysicalDeviceMemoryProperties`.** Called on every buffer/texture creation. The memory properties are constant after device init — query once and cache.
- **Upload buffer is never recycled.** `AllocateUploadBuffer` discards the old buffer via `Collect()` (deferred destroy) but never reuses prior allocations. On repeated large uploads this creates unbounded allocations. Prometheus's arena shrink/grow lifecycle is the correct model.
- **`TODO D3D12` comments throughout.** Several methods contain stale D3D12-era comments that describe intended but never implemented behavior (upload buffer recycling with fences, ResourceStates). Codex should not interpret these as requirements.
- **`simulateReset` flag.** A test shim that bypasses real lost-device logic. Don't carry forward.
- **`DebugMessengerDevice` is a static.** Routes validation messages through a single global device reference. Fine for single-device use but architecturally incompatible with multi-GPU. Aurelian must attach the debug messenger per-device instance.

### Aurelian Improvements
- **`AurelianVulkanDevice` is a struct, not a singleton** *(multi-GPU)*. The entire init sequence must be parameterized by a caller-supplied `VkPhysicalDevice` with no implicit global state. A `PlantRegistry` holds a `PlantContext[]` where each entry is an initialized `AurelianVulkanDevice`. Single-GPU bring-up initializes one entry; multi-GPU initializes N.
- **Per-plant capability tier struct** *(multi-GPU)*. Each plant needs its own immutable `GpuCapabilityTier` (from `classify_capability_bucket` equivalents) because GPU 0 and GPU 1 may be different device classes (e.g. discrete + integrated). Store this at init time per plant, not as a global singleton.
- **Use VMASharp for memory allocation.** Replace the per-resource `AllocateMemory` (which calls `vkAllocateMemory` once per buffer or texture) with VMASharp. VMASharp allocators are naturally per-device, which aligns directly with the per-plant model.
- **Select the best queue family explicitly.** Score queue families for graphics+compute+transfer capability rather than assuming index 0.
- **Dominatus-owned upload budget policy.** The upload ring should have a Dominatus actuator governing grow/shrink decisions, directly applying the `prom_typed_arena` lifecycle pattern carried over from Prometheus.

---

## 2. Fence and Synchronization (`GraphicsDevice.FenceHelper`)

### Intent
Timeline semaphore wrapper. Three instances: `FrameFence` (end-of-frame), `CommandListFence` (per command list submit), `CopyFence` (copy queue). Each has a monotonically increasing `NextFenceValue`. GPU signals a value, CPU queries or waits on it. Resources are tagged with the fence value at which they become safe to reuse. `ResourcePool<T>` reuses objects only when `fenceValue <= completedFenceValue`.

The frame throttle in `End()` is: if `FrameFence.NextFenceValue > MaxFramesInFlight`, CPU waits for `NextFenceValue - MaxFramesInFlight` before proceeding. This keeps the CPU from running too far ahead of the GPU.

### Do Not Carry Over
- **`WaitForFenceCPUInternal` has a concurrency comment warning of starvation.** The `// TODO D3D12 in case of concurrency...` comment identifies a real bug: if two threads wait on different fence values, the lower-value waiter can block behind the higher-value one. The fix is a condition variable or a per-value wait — don't reproduce the current single-call-to-`vkWaitSemaphores` pattern in multithreaded code.
- **`LastCompletedFence` is unsynchronized.** Multiple threads calling `IsFenceCompleteInternal` can race on `LastCompletedFence`. Stride tolerates this because only one thread calls it per frame. Aurelian should use `Interlocked.Exchange` or restrict access.

### Aurelian Improvements
- **Three fences per plant, plus one cross-plant compositor fence** *(multi-GPU)*. `FrameFence`, `CommandListFence`, and `CopyFence` remain per-plant as in Stride. Additionally, Aurelian needs a `CompositorFence` — a cross-plant signal that fires when all plants assigned to a given frame have completed their render tasks and their outputs are ready for compositing. This concept does not exist in Stride at all and must be designed fresh. The compositor waits on all per-plant `FrameFence` values for the current frame before executing.
- **Expose fence values as a generation-tagged observable on the Dominatus blackboard.** The Prometheus pattern of staging fence values as blackboard facts (so the HFSM can observe GPU progress without directly polling) applies here. An actuator polls `GetCompletedValue()` per plant and stages the result; the controller observes all plant fence states as read-only blackboard facts before making assignment decisions.
- **Name the semaphores with `VK_EXT_debug_utils`.** Stride has commented-out debug naming throughout. Aurelian should wire up `vkSetDebugUtilsObjectNameEXT` for all semaphores, fences, and command pools at creation time, including plant index in the name string.

---

## 3. Resource Pool (`ResourcePool<T>`, `CommandBufferPool`, `HeapPool`)

### Intent
Fence-tagged FIFO recycler. On `RecycleObject(fenceValue, obj)` an object is enqueued with its retirement fence value. On `GetObject(completedFenceValue)` the front of the queue is dequeued and reset if its fence has passed, otherwise a new object is created. `CommandBufferPool` recycles `VkCommandBuffer` objects; `HeapPool` recycles `VkDescriptorPool` objects.

### Do Not Carry Over
- **`OptionalLock` uses `Monitor.Enter` with a TODO to consider spinlocks.** The implementation is functionally correct but the lock is held while allocating a new Vulkan object (`CreateObject`), which can stall. Aurelian should separate the "try to dequeue" fast path (lock) from the "create new" slow path (no lock).
- **`HeapPool.CreateObject` uses LINQ `Select`/`Where`/`ToArray` on the hot path.** Every new descriptor pool creation re-runs a LINQ query over `MaxDescriptorTypeCounts`. Cache the `VkDescriptorPoolSize[]` at device init time.
- **`CommandBufferPool` hardcodes `queueFamilyIndex = 0`.** Same issue as device init.

### Aurelian Improvements
- **Resource pools are per-plant** *(multi-GPU)*. Each `PlantContext` owns its own `CommandBufferPool` and `HeapPool`. There is no shared pool across plants. This is implicit in the per-device model but must be enforced explicitly — a command buffer allocated from plant 0's pool must never be submitted to plant 1's queue.
- **Apply the Prometheus selector cache pattern.** Before calling `GetObject`, check a generation-tagged cached decision: "is there likely a ready object at the head of the queue?" This avoids the lock entirely on the common non-exhausted path.
- **Size descriptor pools based on observed demand, not fixed constants.** The Prometheus `prom_sgemm_controller_state` adaptive approach — tracking actual usage and adjusting pool sizes based on high-water marks — is directly applicable here.

---

## 4. Command List (`CommandList.Vulkan.cs`)

### Intent
Records Vulkan commands into a `VkCommandBuffer`. Lifecycle: `Reset()` → record → `Close()` → `CompiledCommandList` → `GraphicsDevice.ExecuteCommandList()`. Key responsibilities:
- Manage a per-CB `Dictionary<Texture, BarrierLayout>` tracking what layout this CB believes each texture is in (not the global `NativeLayout`, which can be mutated concurrently).
- `TransitionBoundResources()` before every draw: transitions render targets, depth buffer, and sampled/storage textures to correct layouts, then calls `EnsureRenderPass()`.
- `EnsureRenderPass()`: lazily creates and caches `VkFramebuffer` objects keyed by `FramebufferKey` (render pass + attachment views). Begins `vkCmdBeginRenderPass` when dirty.
- `CleanupRenderPass()`: ends active render pass before any barrier. Barriers cannot be inside a render pass.
- `BindDescriptorSets()`: allocates a `VkDescriptorSet` from the current pool each draw call, either via `vkUpdateDescriptorSets` (write path) or `vkCopyDescriptorSets` (copy path, controlled by `STRIDE_GRAPHICS_NO_DESCRIPTOR_COPIES`). On pool exhaustion, retires current pool to the fence-tagged list and acquires a fresh one.
- `UpdateSubResource()`: copies CPU data to the upload ring buffer, records `vkCmdCopyBufferToImage` or `vkCmdCopyBuffer` with correct barriers.

### Do Not Carry Over
- **`NativeLayout` is a shared mutable field on `Texture` mutated by every CB.** The per-CB `currentCbLayouts` dictionary was added to fix this — but the fix is incomplete. On the first touch of a texture in a new CB, the code still falls back to `texture.NativeLayout`, which can be stale from a concurrent CB. Full correctness requires a "last-submitted layout" tracker separate from the "in-recording layout" tracker. The Aurelian intent port should treat `NativeLayout` as write-only from the submission path and read-only from the per-CB map.
- **`subresource` parameter in `ResourceBarrierTransition` is ignored.** The TODO comment says "Vulkan always transitions all subresources" — this is wrong for mipmap chains where different mips may need different layouts. Aurelian should implement per-subresource tracking using `LayoutTracker`.
- **`FramebufferKey` stores 10 fixed attachment slots.** Hard limit of 9 color attachments + 1 depth. Use a span-based hash instead.
- **`DrawInstanced` passes `startVertexLocation` as both first vertex and first instance.** Line 1092: `vkCmdDraw(..., (uint)startVertexLocation, (uint)startVertexLocation)` — the last argument should be `(uint)startInstanceLocation`. This is a real bug. Don't carry it over.
- **`DrawAuto`, `DrawIndexedInstanced(Buffer)`, `DrawInstanced(Buffer)`, `ClearReadWrite(Buffer)`, `CopyMultisample`, `CopyCount`** all `throw new NotImplementedException()`. Aurelian should either implement them or explicitly not expose them, not silently surface broken methods.
- **`Clear(Texture renderTarget)` has a TODO about calling `vkCmdClearAttachments` when inside a render pass.** The current implementation calls `CleanupRenderPass()` first, which ends and restarts the pass — wasteful. Inside a render pass, `vkCmdClearAttachments` is the correct call.
- **Descriptor set allocated per-draw-call with no caching.** Correct for correctness but expensive. Aurelian can implement a descriptor set hash cache to avoid reallocation when bindings haven't changed.

### Aurelian Improvements
- **Command lists are per-plant** *(multi-GPU)*. Each `AurelianCommandList` records into a command buffer belonging to a specific plant. The `PlantId` carried on a draw act determines which plant's command list receives the recording. The actuator that processes draw acts is the routing point — it dispatches to the correct plant's command list without the command list itself needing any knowledge of other plants.
- **Render pass abstraction.** Rather than creating `VkRenderPass` objects implicitly inside `PipelineState` and `CommandList`, define explicit `RenderPassDescriptor` plain-data structs (following the WyrmCoil M85/M86 staged boundary pattern) that are separately compiled to `VkRenderPass` and cached.
- **Barrier batch accumulation.** Stride issues one `vkCmdPipelineBarrier` per resource transition. Aurelian should batch all pre-draw barriers into a single call.
- **Dominatus-owned command buffer lifecycle.** The decision of when to flush, when to wait, and when to recycle command buffers should be expressed as Dominatus actuator decisions, not inline imperative logic.

---

## 5. Pipeline State (`PipelineState.Vulkan.cs`)

### Intent
Compiles a `PipelineStateDescription` into a `VkPipeline` + `VkPipelineLayout` + `VkRenderPass` + `VkDescriptorSetLayout`. For graphics pipelines, wires up: vertex input layout, input assembly, rasterizer state, depth-stencil state, color blend state per render target, multisampling (fixed Count1), viewport/scissor as dynamic state. For compute, wraps the single compute stage. Shader module creation: one `VkShaderModule` for the entire effect (all stages must share bytecode — Stride enforces this). Descriptor binding mapping remaps Stride's resource group layout to Vulkan binding slots.

### Do Not Carry Over
- **All stages must share the same SPIR-V bytecode.** The check `if (!stage.Data.SequenceEqual(shaderBytecode)) throw` enforces this. This is a Stride-specific constraint from its GLSL-to-SPIR-V pipeline that doesn't apply to Aurelian's SDSL-V → DXC → SPIR-V pipeline, which can produce per-stage SPIR-V. Aurelian should create one `VkShaderModule` per stage.
- **`VkPipelineCache.Null` — no pipeline cache.** Every pipeline is compiled from scratch. A `VkPipelineCache` serialized to disk gives significant startup speedup. Aurelian should create and persist a pipeline cache.
- **`depthBiasEnable = true` unconditionally.** Set to `true` with a `// TODO VULKAN` comment regardless of whether the rasterizer state actually uses depth bias. Should be set only when `DepthBias != 0 || SlopeScaleDepthBias != 0`.
- **`loadOp = Load` for all attachments unconditionally.** The comment explains this avoids tile garbage on Mali/Lavapipe for non-fullscreen draws — correct, but `Load` is expensive on tile-based GPUs when you actually want to clear. Aurelian should derive `loadOp` from whether the attachment was cleared this frame.
- **Pipeline recreation on `OnRecreate` re-runs the full compilation.** No incremental update or partial cache invalidation. Fine for now but should be noted.
- **`TODO VULKAN: Multisampling`** — multisampling is hardcoded to `Count1`. Aurelian should at least validate the description rather than silently ignoring the request.

### Aurelian Improvements
- **Pipeline state objects are per-plant** *(multi-GPU)*. A pipeline compiled for plant 0's `VkDevice` cannot be used on plant 1's device. Aurelian should compile pipelines lazily per plant on first use and cache them keyed by `(PlantId, PipelineStateHash)`. This is invisible to the caller — they hold a `PipelineHandle`; the plant registry resolves it to the correct `VkPipeline` for the target plant.
- **SDSL-V artifact feeds directly into pipeline creation.** The Aurelian shader pipeline (SDSL-V → DXC → SPIR-V artifact with SHA-256 hash) gives a natural key for pipeline cache lookup. Cache key = hash of SPIR-V + hash of pipeline state description.
- **Per-stage shader modules.** Aurelian's SDSL-V compiler produces stage-specific SPIR-V. Create one `VkShaderModule` per stage and destroy them after pipeline creation.
- **Explicit render pass descriptors.** Pipeline creation should accept an `AurelianRenderPassDescriptor` (plain data) that matches the render pass the pipeline will be used with, rather than creating the render pass implicitly inside pipeline state.

---

## 6. Swapchain and Presentation (`SwapChainGraphicsPresenter.Vulkan.cs`)

### Intent
Manages `VkSwapchainKHR`, `VkSurfaceKHR`, per-image `VkImageView`, and the acquire/present semaphore ring. The present loop (inspired by the Vulkan guide swapchain semaphore reuse example):
1. On `Present()`: submit a semaphore-signaling batch that waits on `FrameFence` and signals a binary `submitSemaphore[currentBufferIndex]`. Call `vkQueuePresentKHR` waiting on that semaphore. Advance `currentFrameIndex`. Wait for the next frame's `frameFence[currentFrameIndex]`. Reset it. Acquire next image using `acquireSemaphores[currentFrameIndex]`.
2. On `AcquireNextImage()`: call `vkAcquireNextImageKHR`. Submit a batch that chains `CommandListFence` (timeline) and the acquire semaphore (binary) as waits, signals `CommandListFence` at the next value. This ensures command buffers wait for image acquisition.
3. Handle `VK_ERROR_OUT_OF_DATE_KHR`, `VK_SUBOPTIMAL_KHR`, `VK_ERROR_SURFACE_LOST_KHR` by calling `OnRecreated()`.
4. Surface creation: Windows via `vkCreateWin32SurfaceKHR`, SDL via `SDL.VulkanCreateSurface`. Android/Linux non-SDL throw `NotImplementedException`.
5. Format selection: try requested format, then sRGB variants, then any supported format. Swap RGB/BGR if needed.
6. Present mode: `Fifo` (always supported) for vsync, `Mailbox` preferred over `Immediate` for no-vsync.
7. `preTransform = surfaceCapabilities.currentTransform` to avoid the OS rotation composition pass on mobile.

### Do Not Carry Over
- **`IsFullScreen` setter is entirely commented out.** 60+ lines of commented SharpDX fullscreen logic. Aurelian should not expose `IsFullScreen` as a setter if it's not implemented.
- **`CreateSurface` throws `NotImplementedException` for Android and non-SDL Linux.** Aurelian targets Linux explicitly. Use `VK_KHR_xcb_surface` or `VK_KHR_wayland_surface` directly via Silk.NET.Windowing rather than the SDL path.
- **`vkQueueWaitIdle` after `CreateBackBuffers` image layout initialization.** Correct but blunt. Should use `CopyFence` for the initial layout transition submit rather than stalling the whole queue.
- **`if (Debugger.IsAttached) Debugger.Break()` in format fallback.** Debug-only behavior that shouldn't be in production code.
- **`Description.SkipBackBufferClampToWindow`** — a workaround flag for a specific Android behavior. Don't carry forward as a general flag.

### Aurelian Improvements
- **The swapchain belongs to the compositor, not to a plant** *(multi-GPU)*. In Stride the swapchain is colocated with the single graphics device. In Aurelian's multi-plant model, only one device presents — the designated presentation plant. Other plants render to offscreen images that are transferred to the presentation plant (via `VK_KHR_external_memory` for cross-device sharing, or via readback for software compositor paths). The swapchain must be explicitly decoupled as a presentation-plant resource. At single-GPU bring-up this is transparent — plant 0 is both the render plant and the presentation plant — but the architectural split must be clean.
- **Silk.NET.Windowing for surface creation.** Use `Silk.NET.Windowing.IWindow.CreateVulkanSurface()` to get a cross-platform surface without duplicating platform-detection logic. Covers Windows, Linux (X11+Wayland), and macOS (MoltenVK).
- **Dominatus-orchestrated present decisions.** Swapchain recreation, format negotiation, and present mode selection are policy decisions that Dominatus can own. A `SwapchainPolicyFacts` blackboard struct (surface capabilities, current format, rotation) plus an actuator that executes the selected policy is cleaner than imperative recreation logic scattered through the presenter.
- **Explicit `VkPipelineCache` invalidation on swapchain recreation.** Stride doesn't invalidate the pipeline cache when the swapchain format changes (e.g., RGB↔BGR swap). Aurelian should tag pipeline cache entries with the swapchain format.

---

## 6b. Compositor (New Subsystem — No Stride Equivalent)

*This subsystem has no Stride equivalent. It is required by the multi-GPU plant/controller model and must exist as a first-class seam even in single-GPU bring-up.*

### Intent
The compositor is a Vulkan compute pipeline that takes N plant render output images and produces the final presentable image written to the swapchain backbuffer. In single-GPU operation it is a passthrough — one plant, one image, blit to backbuffer. In multi-GPU operation it implements the plant selection policy.

The differential rendering compositor policy: for each pixel, compare plant 0 (expensive, accurate) and plant 1 (cheap, approximate) outputs. Where they agree within a tolerance threshold, use plant 1's result. Where they diverge, use plant 0's. The Dominatus shadow calibration HFSM (from P15) tracks per-scene-type agreement confidence, adjusting how frequently plant 0 needs to render. High confidence → plant 0 renders every N frames, plant 1 fills intermediate frames with reprojection. Confidence drops → plant 0 renders every frame.

The compositor SPIR-V shader is built-in (not user-supplied) and operates on `VkImage` inputs from each plant. It runs on the presentation plant's device.

### Compositor Contract
```
CompositorInput {
    PlantOutput[] PlantImages;    // one per plant that rendered this frame
    CompositorPolicy Policy;      // passthrough | differential | SFR | AFR
    uint FrameIndex;
}

CompositorOutput {
    VkImage FinalImage;           // written to swapchain backbuffer
    CompositorDiagnostics Diag;   // agreement rate, plant utilization per frame
}
```

### Aurelian Improvements
- **Compositor seam exists at A6b even when passthrough.** The single-GPU first-triangle path routes through the compositor passthrough shader. This ensures the compositor seam is never bolted on later.
- **Compositor diagnostics feed the Dominatus blackboard.** Per-frame agreement rate, plant utilization, and pixel-level divergence statistics are staged as blackboard facts. The controller HFSM reads these to adjust plant assignment policy.
- **`VK_KHR_external_memory` as the cross-plant image transfer path.** On systems with multiple discrete GPUs, images rendered on plant 1 must be transferred to plant 0 (presentation plant) before the compositor can read them. VMA supports external memory handles; allocate compositor input images with `VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT` (Linux) or `VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT` (Windows) to avoid a readback round-trip through CPU memory.

---

## 7. Texture Resource (`Texture.Vulkan.cs`)

### Intent
Creates and manages `VkImage` + `VkDeviceMemory` + `VkImageView` objects for all texture types (1D/2D/3D/Cube, array, mipped, render target, depth-stencil, staging, shader resource, UAV). The staging path uses `VkBuffer` instead of `VkImage`. Image views are created at init time: `NativeImageView` (shader resource), `NativeColorAttachmentView` (render target), `NativeDepthStencilView` (depth-stencil). Initial data is uploaded via the device upload ring buffer, staged through a copy command buffer. Initial layout transitions are recorded into the copy command buffer and submitted via `ExecuteAndWaitCopyQueueGPU`.

### Do Not Carry Over
- **Per-texture `vkAllocateMemory`.** `AllocateMemory` calls `vkAllocateMemory` once per image/buffer. Vulkan has a `maxMemoryAllocationCount` limit (as low as 4096 on some devices). Aurelian must use VMASharp.
- **`NativeLayout` shared mutable field.** Same issue as CommandList — a shared field mutated by multiple command buffers. The `LayoutTracker` partial was added as a fix but is incompletely integrated. Aurelian should make layout tracking fully per-CB.
- **`InitializeData` calls `AllocateUploadBuffer` for the entire set of mips at once.** The total size calculation correctly accounts for 3D texture depth slices (fixed in Stride), but the upload buffer is never freed until the next allocation exceeds it. Prometheus's arena lifecycle should govern this.
- **`NativePipelineStageMask` for depth-stencil includes `ColorAttachmentOutput`.** Depth/stencil barriers should use `EarlyFragmentTests | LateFragmentTests` only.
- **`OnDestroyed` calls `GraphicsDevice.Collect()` which defers destruction to `FrameFence`.** `Collect` uses `FrameFence.NextFenceValue` (not current) meaning resources are kept alive an extra frame. Aurelian should use the last submitted fence value rather than the next.
- **Multisampling throws `NotImplementedException`** for any non-Texture2D multisample view.
- **`TODO VULKAN: Handle depth-stencil` in `UpdateSubResource`** — depth-stencil upload via the graphics queue is unimplemented.

### Aurelian Improvements
- **Resources carry `PlantId`** *(multi-GPU)*. Every `AurelianTexture` and `AurelianBuffer` carries a `uint PlantId` in its `GpuResourceState` struct. A texture created on plant 0 cannot be directly bound in a draw call recorded for plant 1 without an explicit cross-plant transfer. The resource system enforces this with a debug assertion on bind.
- **VMASharp allocation with usage hints.** Pass `VMA_MEMORY_USAGE_GPU_ONLY` for device-local textures, `VMA_MEMORY_USAGE_CPU_TO_GPU` for staging/dynamic. VMA handles dedicated allocation for large images automatically. VMASharp allocators are per-plant.
- **`LayoutTracker` as first-class per-subresource state.** Aurelian should track layout per (mip, array layer) to enable correct mip-band transitions. A flat array indexed by `mip * arraySize + arrayLayer` is sufficient.
- **Texture descriptors as plain-data contracts.** Follow the WyrmCoil M85/M86 pattern: `TextureCreatePlan` (format, dimensions, usage, initial layout) as a plain-data struct → validated → then `vkCreateImage`. This makes texture creation testable without a GPU.

---

## 8. Buffer Resource (`Buffer.Vulkan.cs`)

### Intent
Creates `VkBuffer` + `VkDeviceMemory` with usage flags derived from `BufferFlags`. Maps flags to Vulkan: `VertexBuffer` → `VK_BUFFER_USAGE_VERTEX_BUFFER_BIT`, `IndexBuffer` → index, `ConstantBuffer` → uniform, `StructuredBuffer` → storage, `ShaderResource` → uniform texel (with optional storage texel for UAV). Staging/dynamic buffers use `HOST_VISIBLE | HOST_COHERENT`. Device-local buffers upload via the copy queue. Typed buffers get a `VkBufferView`.

### Do Not Carry Over
- **`vkCmdFillBuffer` for null-initialized buffers.** Stride fills device-local buffers with `vkCmdFillBuffer(commandBuffer, NativeBuffer, 0, size, 0)` even when zero-initialization is not required. This is an unnecessary GPU operation. Only do it when the buffer content is read before being written.
- **`StructuredBuffer` sets `NativeAccessMask |= UniformRead`** — should be `ShaderRead` for storage buffers. `VK_ACCESS_UNIFORM_READ_BIT` is for uniform buffers; structured buffers use `VK_ACCESS_SHADER_READ_BIT`.
- **Dynamic buffer upload maps/unmaps per `Recreate` call.** Persistent mapping for dynamic buffers is cheaper. Map once at creation, unmap at destruction.
- **Per-buffer `vkAllocateMemory`.** Same VMASharp issue as textures.

### Aurelian Improvements
- **Buffers carry `PlantId`** *(multi-GPU)*. Same invariant as textures. A vertex buffer on plant 0 cannot be bound in a draw command on plant 1 without explicit cross-plant transfer. Debug assertion on bind.
- **Typed arena budget tracking for buffer pools.** Apply the Prometheus arena pattern: track required vs capacity bytes per buffer role (vertex, index, constant, staging). The Dominatus controller can decide when to grow or shrink pools.
- **Persistent mapping for dynamic/staging buffers.** Keep the mapped pointer alive for the buffer's lifetime. VMASharp supports this via `VMA_ALLOCATION_CREATE_MAPPED_BIT`.

---

## 9. Descriptor Set Layout (`DescriptorSetLayout.Vulkan.cs`)

### Intent
Wraps `VkDescriptorSetLayout` creation. Takes a list of `DescriptorSetLayoutBuilder.Entry` (class, type, array size, optional immutable sampler) and emits `VkDescriptorSetLayoutBinding[]`. Immutable samplers are embedded directly into the layout. Also outputs `typeCounts[]` — how many descriptors of each type this layout consumes — used by `BindDescriptorSets` to track pool exhaustion.

### Do Not Carry Over
- **`stageFlags = VkShaderStageFlags.All` for every binding.** Every descriptor is visible to all shader stages. This is correct but overly broad. Aurelian should derive stage visibility from the shader reflection data (which stage the resource is actually used in). Tighter stage flags improve driver optimization.
- **`TODO VULKAN: Handle immutable samplers for DescriptorCount > 1`** throws `NotImplementedException`. Immutable sampler arrays are a real use case (e.g., a set of pre-baked shadow map samplers). Aurelian should implement them.
- **`DescriptorTypeCount = 11` is a magic constant** that matches the number of `VkDescriptorType` values. Add a static assertion or derive it from the enum.

### Aurelian Improvements
- **Descriptor set layouts are per-plant** *(multi-GPU)*. A `VkDescriptorSetLayout` created on plant 0's device is not usable on plant 1. Layouts are cached per `(PlantId, LayoutHash)`. The caller holds a `DescriptorLayoutHandle`; the plant registry resolves it per plant lazily.
- **Cache descriptor set layouts by content hash.** Stride creates a new `VkDescriptorSetLayout` per pipeline state. Aurelian should cache and reuse identical layouts (same entries → same layout handle). This reduces `VkDescriptorSetLayout` object count significantly in scenes with many materials.
- **Reflect stage visibility from SDSL-V shader artifacts.** The SDSL-V artifact knows which stage each binding is used in. Use this to set precise `stageFlags` rather than `VkShaderStageFlags.All`.

---

## 10. Barrier Abstraction (`BarrierMapping.Vulkan.cs`)

### Intent
Pure static mapping: `BarrierLayout` enum (cross-platform) → `VkImageLayout`, `VkAccessFlags`, `VkPipelineStageFlags`. No state. Used by `CommandList.ResourceBarrierTransition` to convert the engine-facing layout enum to the three Vulkan barrier fields required by `vkCmdPipelineBarrier`.

### Do Not Carry Over
- **`BarrierLayout.ShaderResource` maps `VkPipelineStageFlags` to `FragmentShader | ComputeShader` only.** Misses `VertexShader`. A texture sampled in a vertex shader (e.g., a displacement map) requires `VK_PIPELINE_STAGE_VERTEX_SHADER_BIT` in the dst stage mask. The fix is `AllGraphics | ComputeShader` or to derive it from the binding's stage visibility.
- **`BarrierLayout.Common` maps to `VkImageLayout.General`.** `General` is valid but pessimistic — it disables tile compression on mobile GPUs. Only use it for UAV images; for other "undefined" cases use `Undefined` and track the real layout.
- **`BarrierLayout.ResolveSource/Dest` map to `TransferSrc/Dst` layouts.** Resolve operations in Vulkan use `VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL` / `VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL` for non-multisampled resolve, but the dedicated `VK_IMAGE_LAYOUT_ATTACHMENT_OPTIMAL` is available in Vulkan 1.3 for better performance.

### Aurelian Improvements
- **Add `CrossPlantTransferSrc` and `CrossPlantTransferDst` barrier layouts** *(multi-GPU)*. Cross-plant image transfers (plant 1 → presentation plant for the compositor) require queue family ownership transfers via `VkImageMemoryBarrier.srcQueueFamilyIndex` / `dstQueueFamilyIndex`. These are distinct from single-plant transfer barriers and need their own `BarrierLayout` variants with the correct queue family indices filled in from the plant registry.
- **Extend `BarrierLayout` with `VertexShaderResource` and `AllShaderResource` variants.** Gives precise stage masks without broadening all shader resource transitions to all stages.
- **Generation-tagged barrier cache per resource.** The Prometheus `prom_selector_cache` pattern — storing the last barrier decision with a generation tag and reusing it when dependencies haven't changed — directly applies to barrier selection. Most resources don't change layout every frame.

---

## 11. GraphicsResource Base (`GraphicsResource.Vulkan.cs`)

### Intent
Base state shared by all GPU resources: `VkDeviceMemory` handle, `NativePipelineStageMask`, fence values (`CopyFenceValue`, `CommandListFenceValue`), the `UpdatingCommandList` reference (for staging resource CPU-wait ordering), `LastBarrierCommandListId` (detects when a barrier must be re-issued on a new CB), and `LayoutTracker` (per-subresource layout tracking). `AllocateMemory` selects a memory type index from `physicalDeviceMemoryProperties` matching the requested `VkMemoryPropertyFlags`.

### Do Not Carry Over
- **`AllocateMemory` re-queries `vkGetPhysicalDeviceMemoryProperties` every call.** Should be cached once at device init.
- **`OnNameChanged` is a stub with commented-out `VK_EXT_debug_marker` code.** The old `VK_EXT_debug_marker` extension was deprecated in favor of `VK_EXT_debug_utils`. Aurelian should wire up `vkSetDebugUtilsObjectNameEXT` properly.
- **`UpdatingCommandList` creates a hard reference from a resource back to a command list.** This makes resource lifetimes dependent on command list lifetimes in ways that are subtle and easy to misuse. Aurelian should represent pending-update state as a fence value from the moment recording completes rather than a reference to the recording command list.

### Aurelian Improvements
- **`GpuResourceState` carries `PlantId`** *(multi-GPU)*. Extract `NativePipelineStageMask`, `NativeAccessMask`, `NativeLayout`, `LayoutTracker`, and `PlantId` into a value-typed `GpuResourceState` struct. The `PlantId` field is zero at single-GPU bring-up and costs nothing. It enables the plant registry to assert at bind time that a resource is not used on the wrong plant.
- **Dominatus blackboard for resource lifecycle events.** Staging resource ready/pending/invalid state maps directly to a `DwBoard` key pattern. The HFSM can observe when a staging readback completes without polling fence values in application code.

---

## Summary: Bring-Up Order for Aurelian

Based on the above analysis, the recommended milestone sequence for Codex is:

1. **A1** — `PlantContext` + `PlantRegistry`: `AurelianVulkanDevice` as a struct parameterized by `VkPhysicalDevice`, `PlantId`, per-plant `GpuCapabilityTier`. `PlantRegistry` holds `PlantContext[]`. Single-GPU bring-up initializes one entry. Timeline semaphore fences (three per plant). VMASharp allocator init per plant. Upload ring seed.
2. **A2** — `VulkanResourcePool<T>`: fence-tagged recycler with generation-tagged selector cache. `CommandBufferPool` and `DescriptorHeapPool` as per-plant instances. `CompositorFence` stub (cross-plant signal, always immediately complete at single-GPU).
3. **A3** — `AurelianBuffer` + `AurelianTexture`: resource creation with VMASharp, `GpuResourceState` value struct with `PlantId`, per-subresource `LayoutTracker`. PlantId assertion on bind (debug builds only at this stage).
4. **A4** — `BarrierHelper`: clean `BarrierLayout` → (layout, access, stages) mapping with precise stage visibility. `CrossPlantTransfer` variants as stubs. Batch barrier accumulation.
5. **A5** — `AurelianCommandList`: recording, barriers, copy, update subresource. Per-plant. No render pass yet.
6. **A6a** — `AurelianSurface` + `AurelianSwapchain`: Silk.NET.Windowing surface, swapchain on the presentation plant, acquire/present semaphore ring.
7. **A6b** — `AurelianCompositor` (passthrough): compositor seam as a Vulkan compute pipeline with passthrough SPIR-V. Takes `PlantOutput[]`, writes to swapchain backbuffer. Single plant → one image → passthrough blit. `CompositorDiagnostics` stub on the Dominatus blackboard.
8. **A7** — `AurelianPipelineState`: pipeline layout, descriptor set layout with stage visibility, `VkPipelineCache` per plant, graphics pipeline from SDSL-V SPIR-V artifacts. Per-plant lazy compilation keyed by `(PlantId, PipelineHash)`.
9. **A8** — `AurelianRenderPass`: explicit render pass descriptors, framebuffer cache, `CommandList` render pass integration.
10. **A9** — First triangle: `WorldUnit` with a mesh component, actuator that records draw acts with `PlantId = 0`, Dominatus-orchestrated frame loop. `TickControl` → `DispatchActs` → `TickSimulation` → `RenderSnapshot` → compositor passthrough → present.
11. **A10** — Dominatus policy layer: upload budget actuator per plant, descriptor pool demand tracker, barrier cache with generation tags, `CompositorFence` wired to real per-plant `FrameFence` completion.
12. **A11** *(future)* — Multi-GPU: second `PlantContext` in registry, `CrossPlantTransfer` barriers wired via `VK_KHR_external_memory`, differential compositor SPIR-V, shadow calibration HFSM governing plant 0 render frequency.
