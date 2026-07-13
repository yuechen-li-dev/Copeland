# Aurelian Core renderer neutrality — JTF-M3a

JTF-M3a makes `Aurelian.Core` renderer-neutral without changing the current Vulkan frame path. It is the first subdivision of the former JTF-M3 milestone; it deliberately precedes CPU raster ownership and the Machina bridge.

## Dependency graph

Before:

```text
Aurelian.Core -> Aurelian.Runtime
Aurelian.Core -> Aurelian.Rendering.Contracts
Aurelian.Core -> Aurelian.Graphics -> Silk.NET Vulkan + Windowing
```

After:

```text
Aurelian.Core -> Aurelian.Runtime -> Aurelian.Rendering.Contracts
Aurelian.Core -> Aurelian.Rendering.Contracts
Aurelian.Graphics -> Aurelian.Rendering.Contracts -> no production Aurelian dependencies

sample / integration test -> Aurelian.Core + Aurelian.Graphics
```

`Aurelian.Core` has no Graphics, Vulkan, Silk.NET, windowing, compositor-backend, or presentation-backend reference. `Aurelian.Runtime` remains Graphics/Vulkan/windowing-free. `Aurelian.Rendering.Contracts` remains free of Core, Runtime, Dominatus, Machina, Silk.NET, and Vulkan. No Aurelian production project references Machina.

## Ownership disposition

| Area and types | Disposition | Owner after M3a |
| --- | --- | --- |
| `AurelianFrameId`, `AurelianFrameInput`, `AurelianFrameCompositorInputs`, frame pump, frame loop, input-provider types, loop/frame diagnostics and results | Core engine policy | `Aurelian.Core` |
| `AurelianEngine`, `AurelianEngineOptions`, engine status/results/diagnostics | Core engine policy | `Aurelian.Core` |
| `AurelianEngineGraphicsMode`, `AurelianEngineGraphicsOwnership`, `AurelianEngineGraphicsOptions`, `AurelianPreparedGraphicsSubsystem` and its validation/status/result/diagnostics | Core lifecycle and prepared-subsystem policy | `Aurelian.Core` |
| `CompositorActuationBridge` | Core engine coordination: adapts Runtime's local act to a renderer-neutral port | `Aurelian.Core` |
| `ICompositorMechanism` | Backend-neutral renderer port | `Aurelian.Rendering.Contracts.Compositor` |
| `IPresentationMechanism` | Backend-neutral presentation port | `Aurelian.Rendering.Contracts.Presentation` |
| `CompositorDispatchRequest`, `CompositorDispatchResult`, compositor refs/facts/diagnostics/statuses | Backend-neutral renderer data contract | `Aurelian.Rendering.Contracts.Compositor` |
| render snapshots, command plans, symbolic shader/pipeline/target refs, compiled-shader contracts | Existing backend-neutral renderer data contract; retained | `Aurelian.Rendering.Contracts` |
| `VulkanCompositorMechanismAdapter`, `VulkanCompositorMechanismAdapterResult`, `VulkanCompositorMechanismAdapterDiagnostic`, `VulkanCompositorMechanismAdapterDiagnosticCodes` | Concrete Vulkan compositor implementation | `Aurelian.Graphics.Vulkan.Compositor` |
| `VulkanPresentationMechanism` | Concrete Vulkan presentation implementation | `Aurelian.Graphics.Vulkan.Presentation` |
| Vulkan plant, surface, swapchain, command/resource/pipeline mechanisms | Existing Vulkan-specific implementation; retained | `Aurelian.Graphics` |
| `PresenterScreenStack`, screen layers, `IPresenterScreen` | Existing engine presentation/screen policy; not part of backend ownership repair | `Aurelian.Core` |
| `CompositorMechanismResult` | Obsolete redundant wrapper around `CompositorDispatchResult` | Removed |
| visible-triangle setup and Aurelian integration fixture | Composition-root wiring | sample and integration projects |

The two ports moved because both Core and Graphics need them. Keeping their interfaces in Core would force Graphics to reference Core and recreate the reverse dependency this milestone eliminates. The prepared-subsystem options and validation remain Core-owned because they express engine lifecycle policy, not reusable renderer behavior.

## Composition and compatibility

The visible-triangle sample and Vulkan integration fixture now compose `Aurelian.Graphics.Vulkan.Compositor.VulkanCompositorMechanismAdapter` and `Aurelian.Graphics.Vulkan.Presentation.VulkanPresentationMechanism` directly, then supply them to Core's prepared-subsystem and frame-loop policy. Core constructs neither mechanism.

The moved public interfaces have assembly and namespace changes:

- `Aurelian.Core.Compositor.ICompositorMechanism` becomes `Aurelian.Rendering.Contracts.Compositor.ICompositorMechanism`.
- `Aurelian.Core.Engine.Graphics.IPresentationMechanism` becomes `Aurelian.Rendering.Contracts.Presentation.IPresentationMechanism`.
- Vulkan adapter namespaces change from `Aurelian.Core.Graphics.Vulkan.*` to `Aurelian.Graphics.Vulkan.*`.

No forwarding type is retained because a forwarding interface in Core would keep the renderer port in the wrong assembly and because this pre-stable API change is small and mechanically migratable.

## Enforcement and evidence

`tools/Validate-DependencyBoundaries.ps1` now checks the Core/Graphics, Runtime/Graphics, Contracts isolation, and Aurelian/Machina rules; scans the protected assemblies for forbidden concrete dependency tokens; validates all solution project paths and integration-project exclusion from the fast Aurelian and Joint Task Force lanes; and rejects cycles in the production project graph.

Behavior is preserved by the same explicit composition path: the Vulkan adapter still delegates the same neutral compositor dispatch to `VulkanCompositorPassthrough`, and the presentation mechanism keeps the same acquired-image queue, present call, diagnostics, cancellation, and exception behavior. Fast Core tests use neutral fake ports; adapter tests now belong to Graphics; the existing visible-triangle and Vulkan bridge proofs remain integration-owned.

## Non-goals and M3b preparation

M3a does not reference Machina, consume `MachinaPresentationFrame`, add `Aurelian.Machina`, move CPU raster projects, add UI operations, change screen/input semantics, or redesign shaders/Vulkan rendering.

The immediate M3b scope is an Aurelian-owned CPU raster backend that consumes existing Aurelian renderer vocabulary and proves deterministic pixel behavior without changing Core's port or adding a Machina dependency. M3c can then introduce `Aurelian.Machina` as the consumer-owned translation from Machina's presentation frame to the established Aurelian renderer vocabulary. M3d retires the legacy Machina renderer compatibility path only after parity evidence exists.
