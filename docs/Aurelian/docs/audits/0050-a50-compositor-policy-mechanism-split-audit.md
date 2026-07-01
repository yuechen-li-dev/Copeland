# A50 — Compositor policy/mechanism split audit

## 1. Files changed

- `docs/architecture/compositor-policy-mechanism-split.md`: added the design recommendation for neutral compositor contracts, runtime Dominatus policy, graphics/Vulkan mechanism, Claude sketch refinements, anti-goals, and the A51+ sequence.
- `docs/audits/0050-a50-compositor-policy-mechanism-split-audit.md`: added this audit report and command log.
- `README.md`: added A50 status to the project status narrative.
- `docs/architecture/mvp-roadmap.md`: added the A50 milestone and A51 recommendation.
- `docs/architecture/dependency-policy.md`: added the compositor split dependency note.

No source projects, packages, production code, vendor/Dominatus files, or CodeReferences files were modified.

## 2. Task scope

A50 is a docs/design milestone after A49 swapchain acquire/present M0 and before compositor passthrough implementation. The scope was to inspect the current repository state, verify the existing rendering/graphics/presentation/runtime/Dominatus seams, and produce an implementation plan for:

1. neutral compositor contract placement;
2. runtime Dominatus compositor policy placement;
3. Vulkan compositor mechanism/actuator placement;
4. minimal contracts before passthrough;
5. first Dominatus use based on the current codebase;
6. the next milestone recommendation.

A50 intentionally did not implement compositor contracts, swapchain image wrappers, image copy/blit, Dominatus policy code, Vulkan commands, new packages, project references, vendor changes, or CodeReferences changes.

Convergence state: **Success**. The repository now has an explicit compositor policy/mechanism architecture document and an audit-backed A51 recommendation without changing production code.

## 3. Reference material inspected

The audit inspected the requested references and current code paths:

- `docs/audits/0049-a49-swapchain-acquire-present-m0.md`
- `docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `docs/architecture/world-model-doctrine.md`
- `docs/architecture/dependency-policy.md`
- `src/Aurelian.Rendering.Contracts/**`
- `src/Aurelian.Graphics/Vulkan/Presentation/**`
- `src/Aurelian.Graphics/Vulkan/Commanding/**`
- `src/Aurelian.Graphics/Vulkan/Resources/**`
- `src/Aurelian.Graphics/Vulkan/Pipelines/**`
- `src/Aurelian.Runtime/**`
- `tests/Aurelian.Runtime.Tests/**`
- `vendor/Dominatus/src/**`
- `vendor/Dominatus/samples/**`
- `vendor/Dominatus/docs/**`

## 4. Current codebase findings

### Rendering contracts

`Aurelian.Rendering.Contracts` is already the neutral DTO layer. It contains renderer-independent snapshots, command plans, shader contracts, status/result/diagnostic records, and only references `Aurelian.Core`. This is the right dependency shape for compositor facts/requests/results because runtime and graphics can both consume it without referencing each other.

Current render snapshot contracts model frame IDs, cameras, and items without backend handles. Current command plans model symbolic pipeline/shader/target refs. Current compiled shader contracts carry SPIR-V bytes and metadata as neutral records. These patterns are directly applicable to compositor contracts: symbolic IDs and structured diagnostics instead of backend/native handles.

### Presentation facts and results

A49 presentation lives under `src/Aurelian.Graphics/Vulkan/Presentation`. The current shape is typed and backend-owned:

- `VulkanSwapchainFacts` records plant ID, dimensions, selected format/color space/present mode, image count, image-view count, and transform.
- `VulkanSwapchainAcquireResult` returns `Acquired`, `OutOfDate`, `Suboptimal`, `Unavailable`, `Rejected`, or `Failed`, with optional image index and diagnostics.
- `VulkanSwapchainPresentResult` returns `Presented`, `OutOfDate`, `Suboptimal`, `Unavailable`, `Rejected`, or `Failed`, with diagnostics.
- `AurelianVulkanSwapchain` owns the swapchain handle, image handles, image views, binary presentation semaphore set, and acquire/present methods.
- `VulkanSwapchainFactory` creates surface/swapchain resources and reports typed diagnostics when Vulkan capabilities or native calls fail.

This is a good mechanism seam, but it is still Vulkan-specific. The compositor contract should not reuse `VulkanSwapchainAcquireResult` directly; it should refer to a neutral `PresentationTargetRef` and let the Vulkan mechanism map that target to the acquired swapchain image.

### Resource and command submit seams

The graphics backend already has command-buffer leases, barrier plans/emitters, texture/buffer resource wrappers, and a queue submit helper. `VulkanCommandSubmitter` validates executable command-buffer leases, plant ownership, render-pass state, submits one command buffer, signals the per-plant timeline semaphore, optionally waits, and retires command buffers through the command-buffer pool. `VulkanBarrierCommandEmitter` accepts backend texture/buffer owners plus pure barrier plans and records `vkCmdPipelineBarrier` without mutating policy state.

The compositor mechanism can fit by reusing these seams:

- acquire image index through presentation;
- resolve `PresentationTargetRef` to a swapchain image wrapper;
- resolve `PlantOutputRef` to a plant output image wrapper;
- record layout transitions through the barrier emitter;
- record copy/blit/compute commands in a command-buffer lease;
- submit through the submitter or a later presentation-aware submit path;
- present through the existing swapchain present call.

The missing mechanism pieces are explicit and should remain deferred until after contracts: swapchain image wrappers, plant output image wrappers, copy/blit/compute recording, and presentation wait-semaphore handoff.

### Runtime and Dominatus integration

`Aurelian.Runtime` currently references `Aurelian.Core`, `Aurelian.Actuation`, `Aurelian.World`, `Aurelian.Rendering.Contracts`, and `vendor/Dominatus/src/Dominatus.Core`. It contains a tiny Dominatus smoke runtime and world-to-render snapshot extraction. This makes runtime the right home for policy code because it already composes world/rendering contracts and Dominatus, while graphics does not reference Dominatus.

The smoke runtime creates an `ActuatorHost`, registers a handler, creates an `AiWorld`, builds an `HfsmGraph`, creates an `HfsmInstance`/`AiAgent`, ticks the world, emits an `Act`, awaits typed actuation completion, and reads blackboard keys. This is enough to prove the runtime can host a future compositor policy agent.

## 5. Proposed contract location

Recommended location:

```text
src/Aurelian.Rendering.Contracts/Compositor/
```

Reasons:

- Existing render snapshots, command plans, and compiled shader contracts already live in `Aurelian.Rendering.Contracts`.
- `Aurelian.Runtime` can reference compositor contracts without referencing graphics.
- `Aurelian.Graphics` can execute compositor requests without referencing runtime or Dominatus.
- The project currently references only `Aurelian.Core`, so DTO purity is enforceable.

The contract namespace should remain neutral and should contain only DTOs/enums/records: no Vulkan handles, no Silk.NET types, no Dominatus types, no `Aurelian.Graphics` resource owners, and no world units.

## 6. Proposed runtime policy location

Recommended location:

```text
src/Aurelian.Runtime/Compositor/
```

Reasons:

- Runtime already owns composition between world snapshots, rendering contracts, and Dominatus.
- Dominatus policy sessions should not live in graphics.
- Runtime can observe frame facts, update blackboard keys, tick an HFSM/utility policy, and emit neutral compositor requests/acts.

Potential future runtime types:

- `CompositorPolicySession`
- `CompositorPolicyFacts`
- `CompositorPolicyResult`
- `CompositorDispatchAct`
- `CompositorPolicyGraphBuilder`

A50 does not implement these types. A54 is the first recommended runtime policy implementation milestone.

## 7. Proposed graphics mechanism location

Recommended location:

```text
src/Aurelian.Graphics/Vulkan/Compositor/
```

Reasons:

- The folder already exists as an empty Vulkan backend area.
- The mechanism will need more than presentation: plant output image wrappers, swapchain target wrappers, barriers, command buffers, submit, and possibly compute/copy/blit pipelines.
- Keeping it top-level under `Vulkan` avoids implying that presentation owns composition policy.
- The graphics mechanism can still call into `Vulkan/Presentation` for acquire/present-owned resources.

Potential future graphics types:

- `AurelianVulkanCompositor`
- `VulkanCompositorPassthrough`
- `VulkanPlantOutputImage`
- `VulkanPresentationTargetImage`
- optionally `VulkanCompositorDispatchActuator` as an adapter, provided Dominatus-specific hosting remains outside `Aurelian.Graphics`.

## 8. Dominatus pattern findings

Dominatus patterns relevant to compositor policy:

- `IActuationCommand` is a marker for typed commands dispatched through `ActuatorHost`.
- `ActuatorHost` registers typed handlers, evaluates actuation policies, dispatches commands, and publishes immediate or deferred completion events.
- `Blackboard`/`BbKey<T>` provide typed facts and policy state.
- `HfsmGraph`, `HfsmStateDef`, and `HfsmInstance` model root/leaf state machines.
- `AiStep` helpers include acts, awaits, waits, success/failure, and decision helpers in samples.
- Fishtank demonstrates utility-based root decisions from blackboard facts and repeated command acts with waits.
- TinyTown demonstrates richer blackboard-driven simulation and policy state.
- RTSBenchmark demonstrates utility scorers over tactical facts and deterministic agent creation.
- Ariadne.Console demonstrates an application host creating an `ActuatorHost`, registering handlers, creating an `AiWorld`, building an HFSM graph, adding an agent, and ticking until a blackboard completion fact changes.

Aurelian's current runtime smoke follows the same minimal pattern: an actuation command is emitted, a handler completes it, and the policy node awaits typed completion. That maps well to compositor dispatch: `CompositorDispatchAct` can wrap `CompositorDispatchRequest`, the graphics mechanism can complete with `CompositorDispatchResult`, and the policy graph can use facts/diagnostics on subsequent ticks.

## 9. Claude sketch assessment

Accepted:

- Compositor policy can be Dominatus HFSM/blackboard/utility logic.
- No new control primitive is necessary for M0.
- Compositor dispatch is naturally an actuator boundary.
- Differential rendering confidence belongs in blackboard facts and utility/HFSM decisions.

Refined:

- “All plants done” should be “all required outputs for the selected policy are ready.” A passthrough path may require one output; full-quality or differential paths may require more; reduced-frequency policy may intentionally reuse trusted older output.
- The compositor is not “just Dominatus.” Policy is Dominatus/runtime; mechanism is graphics/Vulkan.
- Diagnostics are part of the typed contract flow and should not be hidden in either layer.
- Readiness, quality/trust, cadence, and dispatch are separate concerns even if one policy session coordinates them.
- Vulkan handles and graphics resource owners must not be stored in Dominatus state.
- Dominatus must not be added to `Aurelian.Graphics`.

## 10. Recommended contract model

A51 should add the following minimal neutral model under `Aurelian.Rendering.Contracts/Compositor`:

- `CompositorPolicyKind`: `Passthrough`, `FullQuality`, `ReducedFrequency`, `Differential`.
- `PlantOutputRef`: symbolic plant/frame/image identity.
- `PresentationTargetRef`: symbolic plant/swapchain-image/frame identity.
- `PlantOutputReadiness`: output readiness, optional completed fence value, optional diagnostic code.
- `RequiredPlantOutputSet`: selected policy plus required inputs.
- `CompositorDiagnostics`: agreement rate and metric dictionary.
- `CompositorFrameFacts`: frame ID, output readiness list, previous diagnostics, optional shadow calibration confidence.
- `CompositorDispatchRequest`: frame ID, policy, inputs, target.
- `CompositorDispatchStatus`: `Dispatched`, `Skipped`, `Rejected`, `Failed`.
- `CompositorDispatchResult`: status, frame ID, policy, target, diagnostics, diagnostic codes.

Contract rules:

- DTOs only.
- No Vulkan handles.
- No Silk.NET structs.
- No Dominatus types.
- No `Aurelian.Graphics` types.
- No world units or direct world-document dependency.

## 11. Recommended milestone sequence

Recommended sequence:

```text
A51 — Compositor contracts M0
A52 — Swapchain image wrappers M0
A53 — Vulkan compositor passthrough copy M0
A54 — Runtime Dominatus compositor policy M0
A55 — First visible triangle through compositor path
```

A51 should implement neutral contracts first. This is preferable to implementing swapchain image wrappers first because wrappers would otherwise define backend identities before the policy/mechanism boundary is explicit.

A52 should then make acquired swapchain images addressable as backend mechanism targets. A53 should implement passthrough copy/blit against the neutral request. A54 should introduce Dominatus policy once there is a real mechanism contract to dispatch against. A55 can finally connect the visible path.

## 12. Boundary/anti-goals

A50/A51 boundaries:

- no Vulkan handles in contracts;
- no Dominatus dependency in graphics;
- no graphics dependency in runtime policy contracts;
- no policy hidden in Vulkan compositor;
- no compositor direct world dependency;
- no multi-GPU implementation in M0;
- no differential rendering until passthrough works;
- no frame loop in contracts M0;
- no new packages or project references for A50;
- no vendor/Dominatus or CodeReferences modifications.

## 13. Validation/command log

Commands run for inspection and validation:

```bash
git status --short
find src -maxdepth 4 -type f | sort
find tests -maxdepth 4 -type f | sort
find docs/audits -maxdepth 1 -type f | sort
find docs/architecture -maxdepth 2 -type f | sort
sed -n '1,520p' docs/audits/0049-a49-swapchain-acquire-present-m0.md || true
sed -n '1,760p' docs/audits/0021-a21-aurelian-graphics-vulkan-intent-port-plan.md
sed -n '1,900p' docs/claude/aurelian-vulkan-intent-port-audit.md
sed -n '1,620p' docs/architecture/world-model-doctrine.md
sed -n '1,520p' docs/architecture/dependency-policy.md
find src/Aurelian.Rendering.Contracts -type f | sort
find src/Aurelian.Graphics/Vulkan/Presentation -type f | sort
find src/Aurelian.Graphics/Vulkan/Commanding -type f | sort
find src/Aurelian.Graphics/Vulkan/Resources -type f | sort
find src/Aurelian.Graphics/Vulkan/Pipelines -type f | sort
sed -n '1,520p' src/Aurelian.Rendering.Contracts/Snapshots/RenderSnapshot.cs
sed -n '1,520p' src/Aurelian.Rendering.Contracts/CommandPlans/RenderCommandPlan.cs
sed -n '1,520p' src/Aurelian.Rendering.Contracts/Shaders/CompiledShaderProgram.cs
sed -n '1,900p' src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSwapchain.cs
sed -n '1,900p' src/Aurelian.Graphics/Vulkan/Presentation/AurelianVulkanSurface.cs
sed -n '1,900p' src/Aurelian.Graphics/Vulkan/Presentation/VulkanSwapchainFactory.cs
sed -n '1,760p' src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitter.cs
sed -n '1,760p' src/Aurelian.Graphics/Vulkan/Resources/Barriers/VulkanBarrierCommandEmitter.cs
find src/Aurelian.Runtime -type f | sort
find tests/Aurelian.Runtime.Tests -type f | sort
find vendor/Dominatus/src -maxdepth 5 -type f | sort
find vendor/Dominatus/samples -maxdepth 5 -type f | sort | head -n 300
sed -n '1,520p' src/Aurelian.Runtime/DominatusSmokeRuntime.cs 2>/dev/null || true
sed -n '1,520p' src/Aurelian.Runtime/AurelianRuntimeProject.cs 2>/dev/null || true
find src/Aurelian.Runtime -type f -name '*.cs' -print -exec sed -n '1,260p' {} \;
rg -n "AiWorld|ActuatorHost|Actuator|Act|Immediate|WaitTicks|Hfsm|HfsmGraph|Blackboard|Board|Utility|Score|State|Transition|Agent|Policy|Dispatch|Command|Result|TinyTown|Fishtank|RTSBenchmark" vendor/Dominatus/src vendor/Dominatus/samples src/Aurelian.Runtime tests/Aurelian.Runtime.Tests -g '*.cs' > /tmp/a50-dominatus-patterns.txt || true
wc -l /tmp/a50-dominatus-patterns.txt
head -n 1800 /tmp/a50-dominatus-patterns.txt
find vendor/Dominatus/samples -type f | grep -Ei 'TinyTown|Fishtank|RTSBenchmark|Ariadne.Console|Demo|Program|\.cs$' | sort | head -n 80
sed -n '1,260p' vendor/Dominatus/samples/Dominatus.TinyTown/TinyTownDemo.cs
sed -n '1,260p' vendor/Dominatus/samples/Dominatus.Fishtank/FishCommands.cs
sed -n '1,300p' vendor/Dominatus/samples/Dominatus.Fishtank/FishActuatorHandlers.cs
sed -n '1,320p' vendor/Dominatus/samples/Dominatus.Fishtank/FishNodes.cs
sed -n '1,260p' vendor/Dominatus/samples/Dominatus.RTSBenchmark/Simulation/ShipAgentFactory.cs
sed -n '1,260p' vendor/Dominatus/samples/Dominatus.RTSBenchmark/Simulation/BenchmarkBlackboardKeys.cs
sed -n '1,260p' vendor/Dominatus/samples/Dominatus.RTSBenchmark/Simulation/UtilityScorers.cs
sed -n '1,220p' vendor/Dominatus/samples/Ariadne.Console/Program.cs
find vendor/Dominatus/docs -maxdepth 3 -type f | sort 2>/dev/null | head -n 100
test -f docs/architecture/compositor-policy-mechanism-split.md
test -f docs/audits/0050-a50-compositor-policy-mechanism-split-audit.md
git status --short
```

Validation result:

- `test -f docs/architecture/compositor-policy-mechanism-split.md`: passed.
- `test -f docs/audits/0050-a50-compositor-policy-mechanism-split-audit.md`: passed.
- `git status --short`: showed only the intended docs changes.

No build was required because this milestone is docs-only and modifies no project/source files.

## 14. Next recommendation

```text
A51 — Compositor contracts M0
```

A51 should implement:

- neutral compositor DTOs in `src/Aurelian.Rendering.Contracts/Compositor`;
- tests for DTO construction/equality/basic success helpers if helpers are added;
- no Vulkan code;
- no Dominatus code;
- no graphics implementation;
- no source project dependency changes unless the existing contracts test project needs normal test-file additions.
