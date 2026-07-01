# A51 — Compositor contracts M0

## 1. Files changed

- `src/Aurelian.Rendering.Contracts/Compositor/CompositorPolicyKind.cs`: policy-kind enum for passthrough, full-quality, reduced-frequency, and differential composition policy choices.
- `src/Aurelian.Rendering.Contracts/Compositor/PlantOutputRef.cs`: symbolic plant output reference.
- `src/Aurelian.Rendering.Contracts/Compositor/PresentationTargetRef.cs`: symbolic presentation target reference.
- `src/Aurelian.Rendering.Contracts/Compositor/PlantOutputReadinessStatus.cs`: readiness status enum.
- `src/Aurelian.Rendering.Contracts/Compositor/PlantOutputReadiness.cs`: readiness fact DTO.
- `src/Aurelian.Rendering.Contracts/Compositor/RequiredPlantOutputSet.cs`: required-output DTO plus pure satisfaction helper.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDiagnostics.cs`: neutral compositor diagnostics DTO.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorFrameFacts.cs`: runtime-observable compositor frame facts DTO.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDispatchRequest.cs`: neutral compositor mechanism dispatch request DTO.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDispatchStatus.cs`: dispatch result status enum.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDispatchDiagnostic.cs`: dispatch diagnostic severity enum and diagnostic record.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDispatchDiagnosticCodes.cs`: M0 compositor diagnostic code constants.
- `src/Aurelian.Rendering.Contracts/Compositor/CompositorDispatchResult.cs`: neutral dispatch result DTO and success helper.
- `tests/Aurelian.Rendering.Contracts.Tests/CompositorContractsM0Tests.cs`: M0 contract tests.
- `README.md`: added A51 status and A52 recommendation.
- `docs/architecture/mvp-roadmap.md`: added A51 milestone status and A52 recommendation.
- `docs/architecture/compositor-policy-mechanism-split.md`: updated the A50 design document with A51 contract status.
- `docs/architecture/dependency-policy.md`: added A51 dependency-boundary note.
- `docs/audits/0051-a51-compositor-contracts-m0.md`: this report.

No project references, packages, CodeReferences files, or vendor files were changed.

## 2. Task scope

A51 implements the neutral DTO seam for compositor policy/mechanism communication in `Aurelian.Rendering.Contracts.Compositor`. The milestone is contracts-only: it defines facts, symbolic references, requests, statuses, diagnostics, and results that later runtime policy and graphics mechanism milestones can share without either layer depending on the other.

A51 does not implement compositor policy, runtime Dominatus/HFSM integration, Vulkan compositor mechanism, image copy/blit/compute commands, swapchain image wrappers, plant output image wrappers, presentation synchronization, or a present loop.

Convergence state: **Success**. The intended neutral contract layer now builds and is covered by focused tests; the next backend blocker is isolated as swapchain image wrapper/addressability work.

## 3. Contract model

The new namespace is:

```text
Aurelian.Rendering.Contracts.Compositor
```

The M0 model separates:

- policy kind (`CompositorPolicyKind`);
- symbolic resource identities (`PlantOutputRef`, `PresentationTargetRef`);
- readiness facts (`PlantOutputReadinessStatus`, `PlantOutputReadiness`, `RequiredPlantOutputSet`);
- policy-observable frame facts (`CompositorFrameFacts`);
- mechanism-dispatch request and result DTOs (`CompositorDispatchRequest`, `CompositorDispatchResult`);
- neutral diagnostics (`CompositorDiagnostics`, `CompositorDispatchDiagnostic`, `CompositorDispatchDiagnosticCodes`).

The contracts use plain BCL types and existing project conventions: sealed records for DTOs, readonly record structs for small symbolic refs, enums for stable statuses, and pure convenience properties/helpers where they are useful to tests and future consumers.

## 4. Symbolic refs

`PlantOutputRef` identifies a plant output image symbolically by plant ID, frame ID, and image ID. Its deterministic string form is:

```text
{PlantId}:{FrameId}:{ImageId}
```

`PresentationTargetRef` identifies the acquired presentation target symbolically by plant ID, swapchain image index, and frame ID. Its deterministic string form is:

```text
{PlantId}:{FrameId}:swapchain[{SwapchainImageIndex}]
```

These refs are not Vulkan handles, do not own graphics resources, and do not describe backend lifetime. Consumers can validate non-empty image IDs and target availability at policy/mechanism boundaries.

## 5. Readiness and required-output satisfaction

Readiness is represented by `PlantOutputReadinessStatus` values:

- `Missing`
- `Pending`
- `Ready`
- `Reused`
- `Failed`

`PlantOutputReadiness` carries the output ref, status, optional completed fence value, and optional diagnostic code.

`RequiredPlantOutputSet.IsSatisfiedBy(...)` is intentionally pure. It returns true only when every required `PlantOutputRef` has a matching readiness entry with status `Ready` or `Reused`; extra readiness entries are ignored. This implements the A50 refinement from “all plants done” to “all required outputs for the selected policy are ready.”

## 6. Dispatch request/result model

`CompositorDispatchRequest` carries frame ID, policy kind, symbolic input refs, and symbolic target ref. `HasInputs` is a convenience helper for consumers that need to reject empty input requests.

`CompositorDispatchResult` carries status, frame ID, policy kind, symbolic target, compositor diagnostics, and dispatch diagnostics. `Success` is true only for `Dispatched` results with no error-severity dispatch diagnostics. Skipped, rejected, and failed results are not successful by default; consumers should inspect `Status` for non-dispatched outcomes.

M0 dispatch diagnostic codes are:

- `ACOMP1001` — missing inputs
- `ACOMP1002` — missing target
- `ACOMP1003` — required outputs not ready
- `ACOMP1004` — unsupported policy
- `ACOMP1005` — mechanism unavailable
- `ACOMP1006` — dispatch failed
- `ACOMP1007` — diagnostics invalid

## 7. Tests added

`tests/Aurelian.Rendering.Contracts.Tests/CompositorContractsM0Tests.cs` covers:

- expected `CompositorPolicyKind` values;
- deterministic symbolic ref string forms;
- readiness satisfaction for ready outputs;
- readiness satisfaction for reused outputs;
- false satisfaction for pending, missing, and failed outputs;
- ignoring extra readiness entries;
- empty diagnostics shape;
- dispatch request input/target storage;
- dispatch result success for dispatched/no-error results;
- dispatch result non-success for failed/error results;
- project-reference boundary check against graphics/runtime/world/Dominatus dependencies.

## 8. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Rendering.Contracts.Tests/Aurelian.Rendering.Contracts.Tests.csproj -c Debug
rg -n "Aurelian.Graphics|Aurelian.Runtime|Aurelian.World|Dominatus|Silk|Vulkan|Vk|Swapchain|Surface|DXC|Dxc|Microsoft.Direct3D.DXC|Aurelian.Shaders|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|ServiceLocator|Singleton|Activator|GetType\(|Type\." src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Compositor" src/Aurelian.Graphics src/Aurelian.Runtime src/Aurelian.World src/Aurelian.Shaders -g '*.cs' -g '*.csproj' || true
rg -n "ProjectReference" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.csproj'
git status --short
```

The broad forbidden-token scan produced expected existing or intentional hits only:

- `PresentationTargetRef.SwapchainImageIndex` and its symbolic `swapchain[...]` string, as requested by A51.
- Existing rendering-contract tests that assert absence of Silk/Vulkan/shader/graphics dependencies.

The compositor scan under graphics/runtime/world/shaders returned no hits, confirming A51 did not add compositor implementation outside contracts. The project-reference scan showed only the existing references from `Aurelian.Rendering.Contracts` to `Aurelian.Core` and from the test project to `Aurelian.Rendering.Contracts`.

## 9. Validation results

- `dotnet build Aurelian.slnx -c Debug`: passed, 0 warnings, 0 errors.
- `dotnet test Aurelian.slnx -c Debug`: passed.
- `dotnet test tests/Aurelian.Rendering.Contracts.Tests/Aurelian.Rendering.Contracts.Tests.csproj -c Debug`: passed, 35 tests.

## 10. Deferred features

Deferred beyond A51:

- Vulkan swapchain image wrappers;
- plant output image wrappers;
- compositor resource lookup/resolution;
- image layout transitions for compositor targets/inputs;
- copy/blit/compute dispatch recording;
- graphics submission and presentation semaphore handoff;
- runtime Dominatus compositor policy;
- HFSM/blackboard/utility graph construction;
- frame-loop integration;
- validation policies beyond simple DTO helpers.

## 11. Next recommendation

**A52 — Swapchain image wrappers M0**

A52 should:

- make acquired swapchain images addressable as backend mechanism targets;
- wrap swapchain image handles without owning image memory;
- initialize layout tracking for presentation images;
- keep contracts neutral and graphics-specific wrappers in `Aurelian.Graphics`.
