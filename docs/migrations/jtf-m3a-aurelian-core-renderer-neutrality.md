# JTF-M3a — Aurelian Core renderer neutrality

## Result

JTF-M3a removes the production `Aurelian.Core -> Aurelian.Graphics` reference while preserving the current Vulkan compositor, presentation, frame-loop, and visible-triangle behavior. Core now coordinates only through typed renderer-neutral ports.

## Implemented changes

- Moved `ICompositorMechanism` to `Aurelian.Rendering.Contracts.Compositor` and `IPresentationMechanism` to `Aurelian.Rendering.Contracts.Presentation`.
- Moved the concrete Vulkan compositor adapter and its adapter diagnostics/results from Core to `Aurelian.Graphics.Vulkan.Compositor`.
- Moved `VulkanPresentationMechanism` from Core to `Aurelian.Graphics.Vulkan.Presentation`.
- Retained prepared graphics subsystem options and validation in Core as engine lifecycle policy.
- Removed the unused `CompositorMechanismResult` wrapper.
- Updated the visible-triangle sample and Vulkan integration fixtures to select the Graphics implementations in their explicit composition roots.
- Moved native-free adapter tests from the Core fast lane to the Graphics fast lane.

## Verification model

The dependency validator now rejects Core-to-Graphics and Runtime-to-Graphics references, package or source-level Silk/Vulkan leakage into Core/Runtime, any Contracts project/package or forbidden source dependency, Aurelian-to-Machina production references, invalid solution project paths, fast-lane integration leakage, and production graph cycles.

The Vulkan mechanism has not been rewritten: it receives the same neutral request and the same prepared image sets, delegates to the same `VulkanCompositorPassthrough`, and returns the same neutral dispatch result. Presentation retains its queue ordering, `Present`, diagnostic, cancellation, and failure semantics.

## Compatibility

This is an intentional pre-stable source and binary API correction. Consumers must update the two port namespaces and the moved Vulkan adapter namespaces. No temporary Core forwarding types are provided because they would obscure the corrected ownership boundary.

## Milestone ladder update

The former JTF-M3 work is now evidence-driven and sequenced as:

1. JTF-M3a — Aurelian Core renderer neutrality.
2. JTF-M3b — Aurelian-owned CPU raster backend.
3. JTF-M3c — `Aurelian.Machina` translation bridge from Machina presentation intent to Aurelian renderer vocabulary.
4. JTF-M3d — legacy Machina renderer compatibility retirement after parity proof.

M3a does not add Machina references, renderer UI operations, CPU pixels, or a bridge. Those remain deliberately deferred.
