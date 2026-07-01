# Compositor policy/mechanism split

## 1. Purpose

A50 defines the compositor boundary before any compositor passthrough code is implemented. A49 proved presentation-aware plant creation, surface/swapchain creation, swapchain image views, binary presentation semaphores, typed `AcquireNextImage(...)`, and typed `Present(...)`; it deliberately did not render to the swapchain, add a compositor, or add a frame loop.

The next compositor work needs a clear contract seam because it sits between three very different concerns:

- neutral rendering contracts that describe frame facts, plant outputs, targets, requests, results, and diagnostics;
- runtime policy that can use Dominatus/HFSM/utility logic to decide what should happen this frame;
- graphics mechanism that owns Vulkan images, barriers, commands, submission, and presentation synchronization.

This document started as the A50 design audit. A51 has now implemented the neutral compositor contract layer in `Aurelian.Rendering.Contracts.Compositor`; the document still does not authorize swapchain image wrapping, image copy/blit, Vulkan commands, Dominatus runtime policy, or a frame loop.

## 2. What the compositor is and is not

The compositor is the graphics mechanism that converts one or more plant output images into the image that will be presented. In M0, the first useful mechanism should be passthrough: one ready plant output is copied/blitted/rendered into the acquired presentation target and then presented through the A49 presentation seam.

The compositor is not the entire runtime decision system. The decision of whether to composite, which inputs are required, whether to use passthrough/full-quality/reduced-frequency/differential behavior, and whether confidence or cadence permits a cheaper path is policy. That policy belongs in runtime/Dominatus, not in Vulkan object owners.

The compositor is also not a world-model feature. It should not query world units directly, own plant scheduling, hide frame-loop policy, or become a renderer facade that reintroduces global graphics state.

## 3. Policy vs mechanism

```text
Compositor policy (runtime/Dominatus):
  observes neutral frame facts;
  decides which plant outputs are required for the selected policy;
  chooses Passthrough, FullQuality, ReducedFrequency, or Differential;
  handles confidence, agreement, cadence, and wait decisions;
  emits neutral compositor dispatch requests/acts;
  stores policy state in Dominatus blackboard/HFSM/utility structures.

Compositor mechanism (graphics/Vulkan):
  resolves neutral plant-output and presentation-target references to Vulkan images;
  owns swapchain image wrappers, plant output image wrappers, barriers, copy/blit/compute pipelines, command recording, submission, and semaphore handoff;
  executes a neutral compositor dispatch request;
  returns neutral dispatch results and diagnostics;
  never depends on Dominatus or runtime policy types.
```

The two layers communicate through typed contracts. Contracts must be DTOs only: no Vulkan handles, no Silk.NET structs, no Dominatus types, no `Aurelian.Graphics` object references, and no world objects.

## 4. Contract layer

Neutral compositor contracts should live under:

```text
src/Aurelian.Rendering.Contracts/Compositor/
```

This matches existing renderer-independent render snapshots, command plans, and compiled shader contracts. Both `Aurelian.Runtime` and `Aurelian.Graphics` already depend on `Aurelian.Rendering.Contracts`, so this location allows policy and mechanism to share DTOs without either layer depending on the other.

A51 implemented the M0 contracts as neutral DTOs:

```csharp
public enum CompositorPolicyKind
{
    Passthrough,
    FullQuality,
    ReducedFrequency,
    Differential,
}

public readonly record struct PlantOutputRef(
    uint PlantId,
    ulong FrameId,
    string ImageId);

public readonly record struct PresentationTargetRef(
    uint PlantId,
    uint SwapchainImageIndex,
    ulong FrameId);

public enum PlantOutputReadinessStatus
{
    Missing,
    Pending,
    Ready,
    Reused,
    Failed,
}

public sealed record PlantOutputReadiness(
    PlantOutputRef Output,
    PlantOutputReadinessStatus Status,
    ulong? CompletedFenceValue = null,
    string? DiagnosticCode = null);

public sealed record RequiredPlantOutputSet(
    ulong FrameId,
    CompositorPolicyKind Policy,
    IReadOnlyList<PlantOutputRef> RequiredOutputs);

public sealed record CompositorDiagnostics(
    double? AgreementRate,
    IReadOnlyDictionary<string, double> Metrics);

public sealed record CompositorFrameFacts(
    ulong FrameId,
    IReadOnlyList<PlantOutputReadiness> Outputs,
    CompositorDiagnostics PreviousDiagnostics,
    double? ShadowCalibrationConfidence);

public sealed record CompositorDispatchRequest(
    ulong FrameId,
    CompositorPolicyKind Policy,
    IReadOnlyList<PlantOutputRef> Inputs,
    PresentationTargetRef Target);

public enum CompositorDispatchStatus
{
    Dispatched,
    Skipped,
    Rejected,
    Failed,
}

public sealed record CompositorDispatchDiagnostic(
    string Code,
    CompositorDispatchDiagnosticSeverity Severity,
    string Message);

public sealed record CompositorDispatchResult(
    CompositorDispatchStatus Status,
    ulong FrameId,
    CompositorPolicyKind Policy,
    PresentationTargetRef Target,
    CompositorDiagnostics Diagnostics,
    IReadOnlyList<CompositorDispatchDiagnostic> DispatchDiagnostics);
```

M0 keeps this set intentionally small. It names images and targets symbolically rather than exposing backend handles. `RequiredPlantOutputSet.IsSatisfiedBy(...)` is pure readiness matching for policy tests: every required output must have matching `Ready` or `Reused` readiness, and extra readiness entries are ignored. If a later milestone needs richer identities, add fields to these contracts instead of leaking Vulkan owners into runtime policy.

## 5. Runtime Dominatus policy proposal

Runtime policy should live under:

```text
src/Aurelian.Runtime/Compositor/
```

`Aurelian.Runtime` is the current composition layer: it already references world data, rendering contracts, and Dominatus. That makes it the right place to translate frame facts into policy decisions while keeping graphics backend execution separate.

Potential future runtime types:

- `CompositorPolicySession`: owns the Dominatus `AiWorld`, policy agent, blackboard keys, and tick/update method for compositor policy only.
- `CompositorPolicyFacts`: runtime-facing wrapper or adapter around `CompositorFrameFacts` if session-local bookkeeping is needed.
- `CompositorPolicyResult`: reports wait/dispatch decisions without executing graphics commands.
- `CompositorDispatchAct`: a Dominatus actuation command whose payload is a `CompositorDispatchRequest`.
- `CompositorPolicyGraphBuilder`: builds the HFSM root/options after the neutral mechanism contract exists.

The policy session should observe `CompositorFrameFacts`, decide whether all required outputs for the selected policy are ready, and either emit a compositor dispatch act or wait. It should not contain Vulkan image handles, command buffers, semaphores, swapchain owners, or plant resource wrappers.

## 6. Graphics Vulkan mechanism proposal

The Vulkan mechanism should live under:

```text
src/Aurelian.Graphics/Vulkan/Compositor/
```

The repository already contains this folder as an empty graphics backend area, and it is a cleaner top-level mechanism home than burying the compositor below presentation. Presentation owns surface/swapchain acquire/present; the compositor will need presentation targets, but it will also need plant output images, command buffers, barriers, pipelines, resource lookup, and submit integration. A top-level Vulkan compositor folder keeps those concerns near Vulkan mechanism code without implying that presentation policy owns composition.

Potential future graphics types:

- `AurelianVulkanCompositor`: executes `CompositorDispatchRequest` values and returns `CompositorDispatchResult` values.
- `VulkanCompositorPassthrough`: first M0 mechanism variant after contracts exist.
- `VulkanCompositorDispatchActuator`: optional adapter that turns a Dominatus actuation command into a call to the graphics compositor, while the handler itself remains in runtime/host composition rather than forcing Dominatus into graphics.
- `VulkanPlantOutputImage`: backend wrapper that maps a `PlantOutputRef` to an actual Vulkan image/image view/layout tracker.
- `VulkanPresentationTargetImage`: backend wrapper that maps `PresentationTargetRef` and acquired swapchain image index to a Vulkan swapchain image/image view wrapper.

The mechanism should use existing seams where possible: presentation acquire/present results, command-buffer leases, barrier command emission, and queue submit helpers. It should return diagnostics rather than hiding out-of-date, suboptimal, unavailable, or synchronization failures behind exceptions.

## 7. Claude HFSM sketch assessment

Accepted:

- The policy layer can be Dominatus HFSM/blackboard/utility logic.
- No new control primitive is required for compositor policy M0; Dominatus already has blackboard keys, HFSM nodes, utility decisions, acts, waits, and actuator dispatch.
- Compositor execution is naturally an actuator boundary: policy emits an act/request; mechanism completes it with a result.
- Differential-rendering confidence is policy data: agreement rate, calibration confidence, and scene-type metrics can be blackboard facts and utility/HFSM inputs.

Refined:

- “All plants done” should become “all required outputs for the selected policy are ready.” Passthrough may require one output; full-quality may require more; reduced-frequency may intentionally reuse an older trusted output.
- Policy, mechanism, diagnostics, and contracts are separate layers even if a future runtime session ticks them in one frame loop.
- Readiness, quality/trust, cadence, and dispatch are separate concerns. They can be coordinated by one Dominatus policy graph, but their data should stay explicit.
- Vulkan resources must not be stored in Dominatus state.
- Dominatus must not be referenced by `Aurelian.Graphics`.

## 8. First Dominatus use recommendation

Use Dominatus first for runtime compositor policy, but only after a neutral contract package exists and the first graphics passthrough mechanism is at least contract-shaped. The practical sequence is:

1. define compositor DTOs in `Aurelian.Rendering.Contracts`;
2. build the graphics passthrough mechanism against those DTOs;
3. then introduce a small Dominatus policy session that observes `CompositorFrameFacts` and emits `CompositorDispatchRequest` when required outputs are ready.

This avoids designing Dominatus graph state around nonexistent mechanism details, and it prevents Vulkan code from absorbing policy decisions just to make passthrough work.

## 9. Milestone plan A51+

Recommended sequence:

```text
A51 — Compositor contracts M0
  Add neutral DTOs under Aurelian.Rendering.Contracts/Compositor.
  No Vulkan, no Dominatus, no runtime policy, no graphics implementation.

A52 — Swapchain image wrappers M0
  Wrap acquired swapchain images/image views as backend-owned presentation target images.
  No copy/blit compositor yet.

A53 — Vulkan compositor passthrough copy M0
  Implemented mechanism-only passthrough copy in Aurelian.Graphics: resolve one plant output, resolve one acquired presentation target, emit explicit barriers, record vkCmdCopyImage, submit/wait, and return neutral dispatch results plus Vulkan diagnostics.
  Present semaphore handoff remains deferred.

A54 — First visible triangle through compositor path
  Implemented as an unavailable/headless-safe graphics integration test: acquire a swapchain image, draw a checked-in-SPIR-V triangle offscreen, copy through compositor passthrough into the acquired presentation target, and present. No direct render-to-swapchain bypass and no frame loop.

A55 — Runtime Dominatus compositor policy M0
  Add the first runtime policy session using Dominatus blackboard/HFSM/acts to decide dispatch-or-wait for passthrough/full-quality M0 facts.
```

A51 implemented neutral contracts first. A52 then added graphics-side swapchain image wrappers after the policy/mechanism seam was explicit. A53 then added the first graphics-side passthrough copy mechanism without adding runtime/Dominatus policy. A54 then proved the first visible triangle path through the compositor seam rather than bypassing it; runtime/Dominatus policy, differential composition, and frame-loop ownership remain future work.

## 10. Anti-goals

- No Vulkan handles in contracts.
- No Silk.NET structs in contracts.
- No Dominatus dependency in `Aurelian.Graphics`.
- No `Aurelian.Graphics` dependency in runtime policy contracts.
- No policy hidden in the Vulkan compositor.
- No compositor direct world dependency.
- No multi-GPU implementation in M0.
- No differential rendering until passthrough works.
- No frame loop or renderer facade as part of the contract milestone.
- No vendor/Dominatus or CodeReferences modifications for compositor M0.


## 11. A51 implementation status

A51 implements the neutral compositor DTO layer in `Aurelian.Rendering.Contracts.Compositor`. It includes policy kinds, symbolic plant output and presentation target references, readiness facts, required-output satisfaction, diagnostics, frame facts, dispatch requests, dispatch statuses, dispatch diagnostics, and dispatch results.

A51 intentionally adds no Vulkan/Silk handles, no graphics mechanism, no Dominatus/runtime policy, no world dependency, no new packages, and no new project references. The next recommended milestone is **A52 — Swapchain image wrappers M0**, which should make acquired presentation images addressable by the graphics mechanism while keeping contracts neutral.


## 12. A52 implementation status

A52 implements graphics-side swapchain image wrappers under `Aurelian.Graphics.Vulkan.Compositor`. The wrappers are backend mechanism targets, not neutral contracts and not ordinary allocated textures. `AurelianVulkanSwapchain.CreatePresentationTargetImageSet()` creates one non-owning wrapper per swapchain image, preserving image order and carrying plant ID, swapchain image index, format, extent, internal native image/image-view handles, and a per-image one-mip/one-layer layout tracker initialized to `Present`.

`VulkanPresentationTargetResolver` maps neutral `PresentationTargetRef` values to backend wrappers and rejects missing image sets, plant mismatches, and out-of-range image indices with typed diagnostics. A52 still emits no barriers, copy/blit commands, render commands, queue submits, acquire calls, present calls, frame loop code, or Dominatus policy. The next recommended milestone is **A53 — Vulkan compositor passthrough copy M0**.


## A53 implementation note

The Vulkan compositor mechanism now accepts a neutral `CompositorDispatchRequest` with `CompositorPolicyKind.Passthrough`, requires exactly one input, resolves that input through `VulkanPlantOutputResolver`, resolves the swapchain target through `VulkanPresentationTargetResolver`, validates same-plant/same-size/same-format M0 constraints, and records a deterministic `vkCmdCopyImage` path with explicit source and target layout transitions.

This keeps the design split intact: `Aurelian.Graphics` owns backend wrappers, image barriers, command recording, copy, and submit/wait; runtime/Dominatus policy still belongs under `Aurelian.Runtime/Compositor`; differential and multi-GPU behavior remain future work.

## 13. A55 runtime policy implementation status

A55 implements the first Dominatus-backed runtime policy session under `Aurelian.Runtime.Compositor`. The policy consumes only neutral compositor facts and refs, supports passthrough M0 readiness checks, and emits a runtime-only `CompositorDispatchAct` containing the neutral `CompositorDispatchRequest` for a compositor actuator to execute.

The A55 tests use a fake Dominatus actuator, not Vulkan, to prove the actuation shape: ready and reused outputs dispatch, pending outputs wait, unsupported non-passthrough policy requests reject, and failed neutral dispatch results propagate as runtime policy failures. The graphics mechanism bridge remains deferred, so `Aurelian.Graphics` still owns compositor mechanism execution and does not reference Dominatus/runtime policy.

## 14. A56 integration-test bridge status

A56 composes the split only in `tests/Aurelian.Integration.Tests`. The integration project may reference `Aurelian.Runtime`, `Aurelian.Graphics`, and `Aurelian.Rendering.Contracts` because tests are the temporary host boundary for this proof. It registers Dominatus `CompositorDispatchAct` handlers in test code: one fake handler captures the neutral passthrough request, and one real handler delegates to `VulkanCompositorPassthrough.Dispatch(...)` with Vulkan plant-output and presentation-target wrappers.

The production split remains unchanged. Runtime policy still emits only neutral compositor requests through a runtime-local act and does not reference graphics/Vulkan. The Vulkan compositor mechanism still consumes neutral requests and backend wrappers and does not reference runtime policy or Dominatus. A56 intentionally defers production host/frame-loop ownership until the frame pump shape is known.

## A57 Core bridge promotion

A57 moves the conceptual Runtime/Graphics bridge pattern proven by A56 into production Core without promoting the Vulkan-specific mechanism. `Aurelian.Core.Compositor.ICompositorMechanism` is the abstract, graphics-free mechanism seam: it accepts neutral `CompositorDispatchRequest` values and returns neutral `CompositorDispatchResult` values from `Aurelian.Rendering.Contracts.Compositor`.

`Aurelian.Core.Compositor.CompositorActuationBridge` is the handler adapter between Runtime policy acts and that abstract mechanism. It depends on Runtime only for `CompositorDispatchAct` and on Rendering.Contracts for neutral compositor DTOs. It must not depend on `Aurelian.Graphics`, Silk.NET, Vulkan, swapchains, surfaces, or concrete presentation resources in A57.

A future milestone may add a production Vulkan adapter that implements the Core mechanism seam while preserving the existing rule that Runtime does not reference Graphics and Graphics does not reference Runtime/Dominatus.

## A58 implementation note

A58 promotes the A56 integration-test bridge into a production Core adapter. `Aurelian.Core.Graphics.Vulkan.Compositor.VulkanCompositorMechanismAdapter` implements the neutral `ICompositorMechanism` seam and delegates to the graphics-side `VulkanCompositorPassthrough` using prebuilt `VulkanPlantOutputImageSet` and `VulkanPresentationTargetImageSet` dependencies.

This preserves the policy/mechanism split: Runtime/Dominatus policy emits neutral dispatch requests, Core performs engine-spine wiring, and Graphics owns Vulkan image resolution, command recording, copy submission, and backend diagnostics. A58 deliberately does not add a frame loop, host project, differential compositor, or automatic swapchain/window instantiation in Core.

## A59 Core frame pump bridge

A59 adds the first Core-owned one-frame orchestration layer above the compositor policy/mechanism split. `AurelianFramePump` accepts explicit `AurelianFrameInput` containing compositor policy facts, creates a local per-frame `ActuatorHost`, registers a narrow handler that invokes `CompositorActuationBridge`, and runs `CompositorPolicySession.RunOnceAsync(...)`.

The flow remains split by policy and mechanism boundaries:

```text
Core frame input/facts
  -> Runtime compositor policy session
  -> Runtime CompositorDispatchAct
  -> Core CompositorActuationBridge
  -> neutral Core ICompositorMechanism
  -> mechanism-specific implementation supplied by caller
  -> typed Core frame result
```

The pump does not call Vulkan APIs, create windows, create swapchains, or allocate graphics resources. Vulkan remains behind the existing Core adapter and `Aurelian.Graphics` mechanism implementation, while A59 unit tests prove the frame pump against a fake `ICompositorMechanism`.

## A60 visible frame pump integration

A60 connects the A59 one-frame pump to the A58 Vulkan mechanism adapter in an integration test without changing the policy/mechanism split. The visible setup remains outside the frame pump: the test harness owns presentation-enabled plant creation, swapchain/image acquisition, offscreen triangle rendering, graphics resource lifetimes, the passthrough compositor, the Core Vulkan adapter, and final present.

The exercised chain is:

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
  -> test-owned present
```

This keeps Runtime policy free of Graphics, keeps Graphics free of Runtime/Dominatus, and keeps the frame pump free of Vulkan/window/swapchain ownership. Continuous frame loops, host objects, and engine graphics subsystem lifecycle policy remain future work.

## A61 prepared graphics subsystem options

A61 adds Core-side graphics subsystem vocabulary without changing the policy/mechanism split. `AurelianEngineGraphicsOptions` records whether the engine is `Headless` or `PreparedVisible`, and M0 only supports `External` ownership so callers remain responsible for creating and disposing any Vulkan/window/swapchain resources.

`AurelianPreparedGraphicsSubsystem` groups the neutral compositor mechanism with an optional-for-headless, required-for-prepared-visible presentation mechanism. Validation reports typed Core diagnostics for missing options, missing prepared mechanisms, unsupported ownership, and presentation supplied to headless mode. The frame pump still consumes the existing bridge/mechanism path and does not create graphics resources; future visible loops/samples should pass through this explicit prepared-subsystem vocabulary instead of embedding host policy in the pump.

## A62 sample-path note

A62 exercises the documented compositor policy/mechanism split from a standalone sample executable. The visible triangle sample prepares graphics resources outside Core, runs `AurelianFramePump.RunOneFrameAsync(...)` so Runtime/Dominatus policy emits the neutral compositor dispatch, routes that dispatch through `CompositorActuationBridge`, executes the Vulkan compositor passthrough mechanism through `VulkanCompositorMechanismAdapter`, and presents the compositor-produced swapchain image.

The sample path is intentionally not direct swapchain rendering and not a new policy layer. It keeps the neutral contract boundary intact, uses sample-local static SPIR-V bytes rather than runtime shader compilation, and leaves continuous frame-loop ownership, differential composition, asset/shader artifact loading, and input/window abstractions deferred.

## A63 Core frame loop orchestration

A63 layers a minimal Core-owned frame loop above the A59 one-frame pump without changing the compositor policy/mechanism split. `AurelianFrameLoop` consumes prepared `AurelianFrameInput` instances from `IAurelianFrameInputProvider`, delegates each frame to `AurelianFramePump`, and optionally presents through the already-abstract `IPresentationMechanism` only after a successful frame.

The loop remains prepared-input and prepared-mechanism based. It has no Vulkan/window/swapchain/resource creation path, does not instantiate compositor mechanisms, does not pump window events, and does not become a host or graphics lifecycle owner. Unit tests prove orchestration with fake input providers, fake compositor mechanisms, and fake presentation mechanisms; the A62 sample remains on the direct one-frame pump path until a later sample conversion milestone.
