# A57 — Aurelian.Core engine integration spine M0

## 1. Files changed

- `src/Aurelian.Core/Aurelian.Core.csproj`
- `src/Aurelian.Runtime/Aurelian.Runtime.csproj`
- `src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj`
- `src/Aurelian.World/Aurelian.World.csproj`
- `src/Aurelian.Actuation/Aurelian.Actuation.csproj`
- `src/Aurelian.Core/Engine/AurelianEngine.cs`
- `src/Aurelian.Core/Engine/AurelianEngineOptions.cs`
- `src/Aurelian.Core/Engine/AurelianEngineStatus.cs`
- `src/Aurelian.Core/Engine/AurelianEngineDiagnostic.cs`
- `src/Aurelian.Core/Engine/AurelianEngineDiagnosticCodes.cs`
- `src/Aurelian.Core/Engine/AurelianEngineResult.cs`
- `src/Aurelian.Core/Compositor/ICompositorMechanism.cs`
- `src/Aurelian.Core/Compositor/CompositorMechanismResult.cs`
- `src/Aurelian.Core/Compositor/CompositorActuationBridge.cs`
- `tests/Aurelian.Core.Tests/AurelianEngineM0Tests.cs`
- `tests/Aurelian.Core.Tests/CompositorActuationBridgeM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`

## 2. Task scope

A57 promotes `Aurelian.Core` to engine-spine M0. The scope is intentionally small: project role documentation, a minimal engine identity/lifecycle shell, a neutral compositor mechanism abstraction, and a bridge that forwards Runtime compositor acts to an abstract mechanism.

The scope excludes a host project, frame loop, Vulkan instantiation, concrete Graphics adapter, differential compositor, package additions, vendor changes, and CodeReferences changes.

## 3. Project role decision

No `Aurelian.Host` project was created. `Aurelian.Core` is the high-level integration layer for engine identity, lifecycle, subsystem seams, and future Dominatus-oriented orchestration.

`Aurelian.Core` now references `Aurelian.Runtime` and `Aurelian.Rendering.Contracts`. The pre-existing unused references from Runtime, Rendering.Contracts, World, and Actuation back to Core were removed to avoid a circular project dependency and to keep Runtime/Contracts below the Core integration spine.

## 4. Engine lifecycle shell

A57 adds `Aurelian.Core.Engine.AurelianEngine` with options, status, diagnostics, diagnostic codes, and result records.

M0 behavior:

- `Start()` transitions `Created` or `Stopped` engines to `Started`.
- `Stop()` transitions `Started` engines to `Stopped`.
- Duplicate `Start()` returns `ACE1001`.
- `Stop()` while not started returns `ACE1002`.
- No frame loop, subsystem creation, Vulkan initialization, or Dominatus world creation is performed.

## 5. Compositor mechanism abstraction

A57 adds `Aurelian.Core.Compositor.ICompositorMechanism` as a neutral compositor mechanism seam. It accepts `CompositorDispatchRequest` and returns `CompositorDispatchResult`, both from `Aurelian.Rendering.Contracts.Compositor`.

The abstraction includes no Vulkan, Graphics, Dominatus, windowing, swapchain, surface, shader, or backend-specific types.

## 6. Compositor actuation bridge

A57 adds `Aurelian.Core.Compositor.CompositorActuationBridge`. The bridge validates its mechanism and act input, extracts the neutral request from Runtime `CompositorDispatchAct`, forwards it to `ICompositorMechanism.DispatchAsync(...)`, and returns the mechanism's neutral result.

The bridge is a handler adapter seam for a future host/frame pump to register with Dominatus actuator infrastructure. It does not create a Dominatus host or instantiate any graphics backend.

## 7. Tests added

`tests/Aurelian.Core.Tests/AurelianEngineM0Tests.cs` covers default engine identity, start/stop transitions, duplicate start diagnostics, and stop-before-start diagnostics.

`tests/Aurelian.Core.Tests/CompositorActuationBridgeM0Tests.cs` covers neutral request forwarding, result propagation, failed result propagation, and the absence of Graphics/Vulkan requirements in Core/Core tests.

## 8. Boundary checks

A57 boundary checks verify:

- Core references Runtime and Rendering.Contracts.
- Core does not reference Graphics.
- Runtime does not reference Graphics.
- Graphics does not reference Runtime or Dominatus policy types.
- Rendering.Contracts remains free of Runtime/Graphics dependencies.
- Core and Core tests do not introduce Vulkan/Silk/swapchain/surface/service-locator/reflection patterns.

## 9. Validation results

Validation was performed with solution build/test commands, targeted Core/Runtime/Integration tests, and dependency-boundary ripgrep checks. See the final A57 handoff message for exact command outcomes.

## 10. Deferred features

Deferred features:

- No `Aurelian.Host` project.
- No production frame loop.
- No Vulkan initialization in Core.
- No concrete Core Vulkan compositor adapter.
- No differential compositor.
- No visible sample executable.
- No package additions.
- No vendor or CodeReferences changes.

## 11. Next recommendation

A58 — Core Vulkan compositor mechanism adapter M0.

Core now defines the abstract mechanism seam. The next convergent step is a production adapter that can wrap the existing Graphics compositor mechanism without adding a Graphics dependency to Runtime or a Runtime/Dominatus dependency to Graphics.
