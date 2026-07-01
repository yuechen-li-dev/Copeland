# A60 — Vulkan-backed visible frame pump integration M0

## 1. Files changed

- `tests/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj`
- `tests/Aurelian.Integration.Tests/Compositor/CoreVisibleFramePumpIntegrationM0Tests.cs`
- `tests/Aurelian.Integration.Tests/Support/VulkanVisibleFrameTestFixture.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0060-a60-vulkan-backed-visible-frame-pump-integration-m0.md`

## 2. Task scope

A60 is an integration-test proof that the A59 one-frame Core pump can drive the real Vulkan compositor mechanism and visible presentation path through already-existing seams. It does not turn the frame pump into a Vulkan/window owner and does not add a continuous frame loop, production host, sample executable, asset path, render graph, scheduler, or new production dependency.

## 3. Visible frame pump integration chain

The new test exercises this real chain when the environment supports presentation:

```text
external Vulkan setup
  -> visible offscreen triangle source
  -> VulkanCompositorMechanismAdapter
  -> AurelianFramePump.RunOneFrameAsync(...)
  -> Runtime CompositorPolicySession
  -> Dominatus CompositorDispatchAct
  -> Core CompositorActuationBridge
  -> Graphics VulkanCompositorPassthrough
  -> swapchain target copy
  -> present
```

The dispatch mechanism is the real graphics `VulkanCompositorPassthrough` wrapped by Core's `VulkanCompositorMechanismAdapter`; no fake compositor is used in the visible integration test.

## 4. External setup vs frame pump responsibility

`VulkanVisibleFrameTestFixture` is internal test support. It owns the external setup responsibilities: presentation-enabled plant creation, swapchain creation/acquire, memory allocator, fences, command buffer pool, submitter, offscreen color target, render pass, framebuffer, graphics pipeline from checked-in SPIR-V fixtures, vertex upload, offscreen draw submit, plant output image set, presentation target image set, passthrough compositor, Core adapter, bridge, started engine, and final present helper.

`AurelianFramePump` remains unchanged and consumes only prepared input plus its prepared compositor bridge. It creates a local per-frame `ActuatorHost` to run Runtime compositor policy once, but it does not create Vulkan plants, surfaces/windows, swapchains, textures, command pools, graphics resources, or compositor mechanisms.

## 5. Core/Runtime/Graphics boundary preservation

No production project references were added. The existing A58 shape remains intact: Core may reference Graphics for the engine-spine Vulkan adapter, Runtime remains graphics-free, Graphics remains Runtime/Dominatus-free, and Rendering.Contracts remains neutral DTO/contracts code.

The integration test project references Core, Runtime, Graphics, and Rendering.Contracts because it is the composition harness for this proof. A linked compile item reuses the graphics test SPIR-V fixture source without introducing a shader compiler dependency or moving shader fixture bytes into production Graphics.

## 6. Tests added

- `AurelianFramePump_RunOneVisibleFrame_WhenAvailable_DispatchesCompositorAndPresents`
- `AurelianFramePump_DoesNotCreateVulkanResources_ForVisibleFramePumpIntegration`

The visible test is headless-safe: Vulkan/presentation/swapchain/acquire unavailability paths assert typed diagnostics/statuses and return without failing normal headless runs.

## 7. Boundary checks

Boundary checks cover:

- frame pump source remains free of Vulkan/window/swapchain creation terms;
- Runtime remains free of Graphics/Silk/Vulkan/swapchain/surface references;
- Graphics remains free of Runtime/Dominatus policy references;
- Rendering.Contracts remains neutral;
- project references remain limited to the intended production and integration-test edges;
- no `Aurelian.Host`, service locator, singleton, reflection construction, Vortice, VMA/VMASharp, CodeReferences, or vendor/reference code path is introduced.

## 8. Validation results

Validation was run with solution build/test commands, targeted Core/Integration/Graphics/Runtime test projects, and ripgrep boundary checks. Headless presentation paths are accepted only when they produce diagnostics/statuses that identify environment unavailability rather than an unhandled test failure.

## 9. Deferred features

Deferred features remain:

- continuous frame loop;
- production window pump;
- production host object or `Aurelian.Host` project;
- frame-pump-owned Vulkan/window/swapchain/resource creation;
- render graph;
- scheduler/threading system;
- asset/TOML integration;
- shader compiler dependency in Graphics;
- differential compositor;
- present semaphore integration beyond the current synchronous proof path;
- VMA/VMASharp and Vortice.

## 10. Next recommendation

A61 — Engine graphics subsystem options M0.

Before adding a production frame loop or visible sample executable, Core needs a small explicit options/configuration model for prepared graphics subsystem ownership and lifecycle. That keeps A60's successful proof from turning into implicit host policy hidden inside the frame pump.
