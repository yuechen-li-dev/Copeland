# A53 — Vulkan compositor passthrough copy M0

## 1. Files changed

- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputImage.cs`: non-owning plant output wrapper over offscreen `AurelianVulkanTexture` sources.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputImageSet.cs`: output collection and lookup helpers.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputResolver.cs`: neutral `PlantOutputRef` to backend source resolver.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputResolutionResult.cs`: typed plant-output resolution result.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputStatus.cs`: plant-output resolver status enum.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputDiagnostic.cs`: typed plant-output diagnostics.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanPlantOutputDiagnosticCodes.cs`: A53 plant-output diagnostic code constants.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorPassthrough.cs`: mechanism-only Vulkan passthrough compositor dispatch path.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorResult.cs`: typed mechanism result plus neutral dispatch result.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorStatus.cs`: mechanism status enum.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorDiagnostic.cs`: typed mechanism diagnostics.
- `src/Aurelian.Graphics/Vulkan/Compositor/VulkanCompositorDiagnosticCodes.cs`: A53 compositor diagnostic code constants.
- `src/Aurelian.Graphics/Vulkan/Resources/Barriers/VulkanPresentationTargetBarrierEmission.cs`: presentation-target image barrier emission record.
- `src/Aurelian.Graphics/Vulkan/Resources/Barriers/VulkanBarrierCommandEmitter.cs`: minimal presentation-target image barrier emission support.
- `src/Aurelian.Graphics/Vulkan/Commanding/Submit/VulkanCommandSubmitter.cs`: exposes the last known command-list fence value for command-buffer rental.
- `tests/Aurelian.Graphics.Tests/VulkanPlantOutputImageM0Tests.cs`: headless-safe plant-output wrapper/resolver tests.
- `tests/Aurelian.Graphics.Tests/VulkanCompositorPassthroughM0Tests.cs`: headless-safe compositor rejection and optional real-copy tests.
- `tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj`: enables unsafe helper construction for synthetic texture wrapper tests.
- `README.md`, `docs/architecture/mvp-roadmap.md`, `docs/architecture/compositor-policy-mechanism-split.md`, and `docs/architecture/dependency-policy.md`: A53 status and boundary notes.
- `docs/audits/0053-a53-vulkan-compositor-passthrough-copy-m0.md`: this report.

No CodeReferences files, vendor/Dominatus files, runtime policy files, world files, shader compiler files, VMA/VMASharp, or Vortice references were changed.

## 2. Task scope

A53 implements only the first Vulkan compositor mechanism path. It consumes neutral compositor requests, supports passthrough policy, resolves one source image and one presentation target image, records barriers, records `vkCmdCopyImage`, submits/waits through the existing submitter, and returns neutral `CompositorDispatchResult` data wrapped in Vulkan mechanism diagnostics.

Convergence state: **Success**. The real mechanism path exists and the optional Vulkan/presentation test validates copy submission when the environment supports it, while headless environments exit through typed diagnostics.

## 3. Reference material read

Read before implementation:

- `docs/audits/0052-a52-swapchain-image-wrappers-m0.md`
- `docs/audits/0051-a51-compositor-contracts-m0.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/claude/aurelian-vulkan-intent-port-audit.md`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/CommandList.Vulkan.cs`
- `CodeReferences/Stride/Stride.Graphics/Vulkan/SwapChainGraphicsPresenter.Vulkan.cs`
- `/tmp/a53-stride-compositor-copy-search.txt`

Borrowed compositor intent: passthrough composition should be the first mechanism, copying one final/offscreen plant image into the acquired presentation image without pulling runtime policy into Vulkan owners.

Stride pitfalls intentionally avoided:

- hidden backbuffer coupling;
- presenter-owned render policy;
- no explicit compositor seam;
- global/implicit layout state.

Aurelian improvements preserved:

- neutral request/result contracts;
- backend source and target resolution;
- explicit barriers;
- explicit submit/present split;
- policy deferred to Runtime/Dominatus.

## 4. Plant output wrapper model

`VulkanPlantOutputImage` wraps an existing offscreen `AurelianVulkanTexture` as a compositor source. It does not own the texture, image, image view, memory allocation, or layout tracker. Construction validates that the texture is live, belongs to the `PlantOutputRef` plant, and has `TransferSource` usage for A53 M0 passthrough copy.

`VulkanPlantOutputImageSet` stores source wrappers and resolves exact neutral `PlantOutputRef` values. `VulkanPlantOutputResolver` returns typed diagnostics for missing sets, missing outputs, plant mismatches, disposed textures, and missing transfer-source usage.

## 5. Presentation target resolution use

A53 reuses the A52 `VulkanPresentationTargetImageSet` and `VulkanPresentationTargetResolver`. The compositor resolves the neutral `PresentationTargetRef` to an acquired swapchain image wrapper and validates same-plant, same-format, and same-size constraints before recording commands.

## 6. Passthrough dispatch behavior

`VulkanCompositorPassthrough` is per plant and does not own the plant, command buffer pool, or submitter. It supports only `CompositorPolicyKind.Passthrough` and rejects unsupported policies, zero inputs, multiple inputs, source resolution failures, target resolution failures, size mismatches, format mismatches, and dispatch after dispose with typed mechanism diagnostics.

Successful dispatch returns `VulkanCompositorStatus.Dispatched`, a neutral `CompositorDispatchResult` with `CompositorDispatchStatus.Dispatched`, and the submitter's signaled command-list fence value.

## 7. Barrier/copy/submit behavior

The command path is:

1. Rent a command buffer using the submitter's last known completed command-list fence value.
2. Begin recording.
3. Capture the source texture's current layout.
4. Transition source to `TransferSource` through `VulkanBarrierCommandEmitter`.
5. Transition presentation target from its tracked layout to `TransferDestination` through the new presentation-target barrier emission record.
6. Record `vkCmdCopyImage` for mip 0/layer 0/full extent.
7. Transition the presentation target back to `Present`.
8. Restore the source layout when it was not already `TransferSource`.
9. End the command buffer.
10. Submit with wait through `VulkanCommandSubmitter`.

If barrier planning mutates a tracker and later native emission fails, rollback remains deferred and diagnosable rather than hidden behind brittle state repair.

## 8. Tests added

Plant output tests cover wrapper ownership, disposed texture rejection, missing transfer-source usage rejection, successful resolution, missing output rejection, and plant mismatch rejection.

Passthrough compositor tests cover headless/unavailable presentation skip, unsupported policy rejection, missing input rejection, multiple input rejection, missing plant output rejection, missing presentation target rejection, optional real copy-and-fence signal when Vulkan presentation is available, idempotent dispose, and dispatch-after-dispose diagnostics.

## 9. Boundary checks

Boundary checks include full solution build/test, focused graphics tests, forbidden dependency search, compositor copy/barrier/submit search, project-reference search, and git status inspection.

## 10. Validation results

Validated locally:

- `dotnet build Aurelian.slnx -c Debug`
- `dotnet test tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj -c Debug --no-restore`

Final full solution test and boundary checks are recorded in the task summary.

## 11. Deferred features

Deferred:

- Runtime/Dominatus compositor policy;
- differential compositor behavior;
- reduced-frequency policy;
- multi-GPU external memory and queue-family ownership transfer policy;
- compute compositor pipelines;
- frame loop/render graph integration;
- asset/TOML integration;
- shader compiler dependency in graphics;
- present semaphore integration beyond the current submit/wait split;
- pixel validation.

## 12. Next recommendation

A54 — First visible triangle through compositor path.

The mechanism can now copy a source image into an acquired presentation target. The next motivating case is to render a tiny offscreen image and route it through this compositor path to prove the first visible compositor-backed frame. If present semaphore handoff becomes the immediate blocker during that proof, narrow A54 to compositor present semaphore integration M0.
