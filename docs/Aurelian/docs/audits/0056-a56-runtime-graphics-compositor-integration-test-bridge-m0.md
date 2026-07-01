# A56 — Runtime/Graphics compositor integration test bridge M0

## 1. Files changed

- Added `tests/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj` and added it to `Aurelian.slnx`.
- Added integration compositor tests under `tests/Aurelian.Integration.Tests/Compositor/RuntimeGraphicsCompositorBridgeM0Tests.cs`.
- Added test-only support helpers under `tests/Aurelian.Integration.Tests/Support/`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/compositor-policy-mechanism-split.md`, and `docs/architecture/dependency-policy.md`.
- Added this audit report.

## 2. Task scope

A56 is an integration-test composition milestone. It proves runtime policy can emit a Dominatus `CompositorDispatchAct`, and test-layer handlers can execute that act either through a fake neutral dispatch result or through the real Vulkan compositor passthrough mechanism when Vulkan presentation is available.

A56 is not a production host layer, frame loop, sample executable, renderer facade, differential compositor policy, multi-GPU path, or present-semaphore integration milestone.

## 3. Integration-project dependency decision

The new `tests/Aurelian.Integration.Tests` project references:

- `src/Aurelian.Runtime/Aurelian.Runtime.csproj`
- `src/Aurelian.Graphics/Aurelian.Graphics.csproj`
- `src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj`

This is intentional because integration tests are the allowed composition boundary for subsystem proofs. No production project reference was added between runtime and graphics.

## 4. Fake actuator bridge test

`RuntimeGraphicsCompositorBridge_FakeActuator_DispatchesNeutralRequestThroughDominatus` builds passthrough-ready compositor facts, registers a fake `CompositorDispatchAct` handler in a Dominatus `ActuatorHost`, runs `CompositorPolicySession.RunOnceAsync(...)`, and asserts that the captured neutral request has the expected frame id, passthrough policy, plant output ref, and presentation target ref.

This proves the integration project can host runtime policy and an actuator without constructing graphics or Vulkan objects.

## 5. Real Vulkan compositor bridge test

`RuntimeGraphicsCompositorBridge_RealVulkanPassthrough_WhenAvailable_DispatchesThroughGraphicsMechanism` creates a presentation-enabled Vulkan plant, swapchain, acquired presentation target, offscreen source texture, command pool, submitter, fence bundle, plant-output image set, presentation-target image set, and `VulkanCompositorPassthrough` when the environment supports them.

The test registers a real integration-layer Dominatus handler for `CompositorDispatchAct`. The handler calls `VulkanCompositorPassthrough.Dispatch(...)` with the neutral request produced by runtime policy and returns the neutral `CompositorDispatchResult` from the graphics mechanism. Headless or unavailable Vulkan/presentation environments assert diagnostics and return cleanly.

## 6. Production dependency boundary

The production boundary remains clean:

- `Aurelian.Runtime` still does not reference `Aurelian.Graphics`.
- `Aurelian.Graphics` still does not reference `Aurelian.Runtime` or Dominatus.
- `Aurelian.Rendering.Contracts` remains neutral and does not reference runtime, graphics, Dominatus, Silk.NET, or Vulkan.

The bridge is test-only and does not create `Aurelian.Host`, `Aurelian.Runtime.Graphics`, or `Aurelian.Graphics.Runtime`.

## 7. Tests added

- `RuntimeGraphicsCompositorBridge_FakeActuator_DispatchesNeutralRequestThroughDominatus`
- `RuntimeGraphicsCompositorBridge_RealVulkanPassthrough_WhenAvailable_DispatchesThroughGraphicsMechanism`
- `RuntimeGraphicsCompositorBridge_ProductionProjectsRemainDecoupled`

## 8. Boundary checks

Boundary checks inspect forbidden dependency terms in production runtime, graphics, and neutral contracts, and inspect project references across production and integration projects. The integration test project is expected to reference both runtime and graphics; production projects are not.

## 9. Validation results

Validation commands for the final patch include solution build, solution tests, focused integration/runtime/graphics test projects, and boundary `rg` checks. Vulkan/presentation-specific paths are headless-safe: unavailable environments must produce diagnostics and return instead of failing the suite.

## 10. Deferred features

- Production host layer.
- Frame loop/frame pump ownership.
- Sample executable.
- Differential compositor policy.
- Present semaphore integration.
- Multi-GPU external memory.
- VMA/VMASharp or Vortice adoption.
- Runtime-to-graphics production project reference or bridge assembly.

## 11. Next recommendation

A57 — Minimal graphics host/frame pump M0.

The integration bridge now proves the runtime policy-to-graphics mechanism seam in tests. The next useful step is deciding and creating the minimal production host/frame-pump shape without polluting runtime or graphics dependency boundaries.
