# A58 — Core Vulkan compositor mechanism adapter M0

## 1. Files changed

- `src/Aurelian.Core/Aurelian.Core.csproj`
- `src/Aurelian.Core/Graphics/Vulkan/Compositor/VulkanCompositorMechanismAdapter.cs`
- `src/Aurelian.Core/Graphics/Vulkan/Compositor/VulkanCompositorMechanismAdapterResult.cs`
- `src/Aurelian.Core/Graphics/Vulkan/Compositor/VulkanCompositorMechanismAdapterDiagnostic.cs`
- `src/Aurelian.Core/Graphics/Vulkan/Compositor/VulkanCompositorMechanismAdapterDiagnosticCodes.cs`
- `src/Aurelian.Graphics/Vulkan/Compositor/IVulkanCompositorPassthroughMechanism.cs`
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorPassthrough.cs`
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPresentationTargetImageSet.cs`
- `tests/Aurelian.Core.Tests/VulkanCompositorMechanismAdapterM0Tests.cs`
- `tests/Aurelian.Core.Tests/CompositorActuationBridgeM0Tests.cs`
- `tests/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj`
- `tests/Aurelian.Integration.Tests/Compositor/RuntimeGraphicsCompositorBridgeM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0058-a58-core-vulkan-compositor-mechanism-adapter-m0.md`

## 2. Task scope

A58 adds a production Core-level adapter that lets the engine spine call the existing Vulkan compositor mechanism through `ICompositorMechanism`. The adapter accepts neutral `CompositorDispatchRequest` values and returns neutral `CompositorDispatchResult` values.

The scope remains adapter-only. A58 does not create `Aurelian.Host`, a frame loop, a frame pump, a Core-owned swapchain/window bootstrap path, a differential compositor, multi-GPU external memory, new packages, vendor changes, or CodeReferences changes.

## 3. Dependency decision

`Aurelian.Core` now references `Aurelian.Graphics`. This is intentional because A57 promoted Core to the high-level engine integration spine.

The lower-level dependency rules remain intact:

- `Aurelian.Runtime` does not reference `Aurelian.Graphics`.
- `Aurelian.Graphics` does not reference `Aurelian.Runtime` or Dominatus.
- `Aurelian.Rendering.Contracts` does not reference `Aurelian.Runtime` or `Aurelian.Graphics`.

## 4. Adapter model

`VulkanCompositorMechanismAdapter` is non-owning. It receives prebuilt graphics dependencies from the caller:

- a Vulkan passthrough mechanism;
- a plant output image set;
- a presentation target image set.

It implements `ICompositorMechanism.DispatchAsync`, validates the neutral request, delegates to the Vulkan passthrough mechanism, and returns only the neutral dispatch result. The adapter does not expose `VulkanCompositorResult` through the Core mechanism interface and does not create or dispose Vulkan resources.

A small `IVulkanCompositorPassthroughMechanism` seam was added in Graphics so Core adapter unit tests can use fake/lightweight graphics mechanisms without constructing native Vulkan plants.

## 5. Integration test update

The real A56 integration path now constructs the production Core adapter and a `CompositorActuationBridge`. The Dominatus actuation handler calls the Core bridge instead of directly invoking `VulkanCompositorPassthrough.Dispatch(...)` from the test handler.

This proves the real integration path is now:

```text
Runtime compositor policy -> Dominatus act -> Core actuation bridge -> Core Vulkan adapter -> Graphics Vulkan passthrough mechanism
```

The fake actuator integration test remains in place.

## 6. Boundary checks

Boundary checks were run with ripgrep to verify the intended dependency shape. The expected Core Vulkan references are now present under the adapter namespace and Core project file. Runtime remains free of Graphics/Vulkan references, Graphics remains free of Runtime/Dominatus policy references, and Rendering.Contracts remains neutral.

## 7. Validation results

Validation was performed with solution build/test commands, targeted Core/Integration/Runtime/Graphics test commands, and dependency-boundary ripgrep checks. The full command list and outcomes are recorded in the A58 final handoff.

## 8. Deferred features

Deferred features:

- no frame loop or frame pump;
- no `Aurelian.Host` project;
- no automatic Core window/swapchain creation;
- no present semaphore integration;
- no differential compositor;
- no multi-GPU external-memory path;
- no new packages;
- no vendor or CodeReferences changes.

## 9. Next recommendation

A59 — Minimal frame pump M0.

Core now has the engine spine and a production Vulkan compositor mechanism adapter, but there is still no repeatable frame lifecycle that acquires, dispatches, presents, and records frame-level diagnostics through a stable engine-owned path.
