# A47 — Vulkan command submit helper M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Commanding/VulkanCommandBufferLease.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitRequest.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitResult.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitStatus.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitDiagnostic.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitter.cs`
- `tests/Aurelian.Graphics.Tests/VulkanCommandSubmitterM0Tests.cs`
- `tests/Aurelian.Graphics.Tests/VulkanOffscreenDrawRecordingM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0047-a47-command-submit-helper-m0.md`

## 2. Task scope

A47 implements the one-command-buffer Vulkan submit helper M0. The helper accepts exactly one executable `VulkanCommandBufferLease`, submits it to the owning plant queue, signals the per-plant command-list timeline fence, optionally waits for completion, retires the lease through `VulkanCommandBufferPool`, and returns typed result/diagnostics/fence value data.

Out of scope remains unchanged: no swapchain/window/surface, present/acquire, render backend facade, render graph, descriptor sets, uniforms, index buffers, VMA/VMASharp, Vortice, runtime shader compiler dependency, global singleton/service locator, or vendor/reference-code changes.

## 3. Reference material read

The A47 implementation inspected the requested command/fence/upload state, A46/A45/A31 audits, the Claude Vulkan intent-port audit, Stride submit references, and Prometheus submit/fence search hits.

Stride submit intent borrowed:

- timeline fences are per-device/plant synchronization primitives;
- command-list submissions signal monotonically increasing fence values;
- command buffers and resource pools retire against fence values and reuse only after completion.

Stride pitfalls avoided:

- no hidden global submit path was added;
- command-list lifecycle was not mixed with broad resource cleanup;
- fence state remains owned by `VulkanTimelineFence` instead of unsynchronized external counters.

Prometheus lessons applied:

- keep ownership explicit by validating plant IDs before queue submission;
- return diagnostics with stage-specific failure codes instead of opaque booleans;
- retire/reuse command resources only through the owning pool and an observed fence value.

## 4. Submit request/result model

`VulkanCommandSubmitRequest` is a small one-buffer request containing the command buffer lease, wait flag, timeout, and debug name.

`VulkanCommandSubmitResult` contains:

- `Submitted`, `Rejected`, or `Failed` status;
- nullable signal fence value;
- ordered diagnostics;
- a `Success` convenience property that requires `Submitted` and a signal value.

Diagnostics use A47 codes `AGCS1001` through `AGCS1008` for missing command buffers, invalid executable state, plant mismatch, fence allocation failure, queue submit failure, wait failure, disposed submitter, and retirement failure.

## 5. Queue submit/fence behavior

The submitter allocates the signal value from `fences.CommandListFence`, builds a Silk.NET `TimelineSemaphoreSubmitInfo` plus `SubmitInfo`, submits exactly one command buffer to `plant.GraphicsQueue`, and signals the command-list timeline semaphore to the allocated value. If requested, it waits on the same timeline fence value with the request timeout.

No wait semaphores, multiple command buffers, copy/frame fence variants, present/acquire semaphores, or generalized queue scheduler were added.

## 6. Command buffer retirement behavior

After a successful queue submission and optional wait, the command buffer lease is retired through the provided `VulkanCommandBufferPool` with the signal fence value. The pool remains the owner of lease recycling decisions. The submitter does not own or dispose the plant, fence bundle, or command buffer pool.

Validation rejects disposed, retired, non-executable, wrong-plant, missing, and active-render-pass leases before queue submission.

## 7. Offscreen draw proof update

The A46 offscreen draw proof now records the triangle command buffer, ends it, submits it through `VulkanCommandSubmitter`, waits for completion, and verifies that `CommandListFence` completed at least the returned signal value. The proof still uses no swapchain, window, surface, present, or acquire path.

## 8. Tests added

`tests/Aurelian.Graphics.Tests/VulkanCommandSubmitterM0Tests.cs` covers:

- Vulkan-unavailable clean skip convention;
- missing command buffer rejection;
- non-executable command buffer rejection;
- plant mismatch rejection;
- active render pass rejection;
- executable empty command buffer submit, fence signal, wait, retire, and reuse;
- submit without wait returning a signal value;
- idempotent dispose;
- submit-after-dispose diagnostic.

The offscreen draw recording test was updated to record and submit the triangle command buffer.

## 9. Boundary checks

A47 boundary checks verified that queue submit calls remain in upload/submit seams and that no swapchain/window/surface/present/acquire, shader compiler dependency, VMA/VMASharp, Vortice, global service locator, reflection construction, vendor/reference dependency, or new project was introduced.

## 10. Validation results

Validation commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug
rg -n "SwapChain|Swapchain|Surface|CreateVulkanSurface|IWindow|Window.Create|Present|AcquireNextImage|Aurelian.World|Aurelian.Rendering.Null|Aurelian.Shaders|Dxc|DXC|SDSL|Sdslv|Hlsl|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "vkQueueSubmit|QueueSubmit|Submit" src/Aurelian.Graphics/Vulkan/Commanding/Submit src/Aurelian.Graphics/Vulkan/Resources/Uploads tests/Aurelian.Graphics.Tests -g '*.cs' || true
git status --short
```

In the current environment the build and tests passed, including the graphics test project. Boundary grep produced expected matches in the new submit seam, existing upload seams, package references/test names/assertion strings, and the intentionally checked documentation/report text; no forbidden runtime feature was added.

## 11. Deferred features

Deferred beyond A47:

- swapchain/window/surface creation;
- present/acquire;
- render backend facade;
- render graph;
- descriptor sets;
- uniforms/push constants;
- index buffers;
- command batching/scheduler;
- multi-buffer submit;
- wait semaphores;
- separate frame/copy submit variants;
- offscreen readback/screenshot;
- VMA/VMASharp and Vortice.

## 12. Next recommendation

Recommended next milestone:

```text
A48 — Surface/swapchain M0
```

Reason: A47 proves the offscreen command path can now record, submit, signal, wait, and retire. The next missing visual path is presentation.
