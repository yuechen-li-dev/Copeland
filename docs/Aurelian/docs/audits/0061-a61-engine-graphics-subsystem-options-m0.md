# A61 — Engine graphics subsystem options M0

## 1. Files changed

- `src/Aurelian.Core/Engine/AurelianEngineOptions.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianEngineGraphicsMode.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianEngineGraphicsOwnership.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianEngineGraphicsOptions.cs`
- `src/Aurelian.Core/Engine/Graphics/IPresentationMechanism.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystem.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystemValidation.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystemResult.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystemStatus.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystemDiagnostic.cs`
- `src/Aurelian.Core/Engine/Graphics/AurelianPreparedGraphicsSubsystemDiagnosticCodes.cs`
- `tests/Aurelian.Core.Tests/AurelianEngineGraphicsOptionsM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/compositor-policy-mechanism-split.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0061-a61-engine-graphics-subsystem-options-m0.md`

## 2. Task scope

A61 adds Core-side graphics subsystem options and lifecycle vocabulary for prepared graphics resources. It intentionally does not create Vulkan resources, move A60's integration setup into production Core, add a continuous frame loop, add a production visible sample executable, create `Aurelian.Host`, add packages, or modify vendor/CodeReferences content.

## 3. Graphics mode/ownership model

`AurelianEngineGraphicsMode` defines the M0 modes:

- `Headless` — no visible presentation is expected.
- `PreparedVisible` — the caller has externally prepared visible graphics, presentation, and compositor mechanism resources before handing Core a bundle.

`AurelianEngineGraphicsOwnership` defines only `External` in M0. This means Core does not own or dispose Vulkan, window, swapchain, allocator, command, fence, or presentation resources. Later milestones may add engine-owned policy, but A61 deliberately does not.

`AurelianEngineGraphicsOptions` supplies static `Headless` and `PreparedVisible` presets. Both presets use external ownership.

## 4. Prepared graphics subsystem model

A61 adds `AurelianPreparedGraphicsSubsystem` as a neutral Core bundle containing:

- graphics options;
- an optional `ICompositorMechanism` reference;
- an optional `IPresentationMechanism` reference.

The presentation seam is an interface instead of a raw delegate so future frame/sample code can use an explicit mechanism object without making Core depend on Vulkan, Silk.NET, window, surface, or swapchain types.

## 5. Validation behavior

`AurelianPreparedGraphicsSubsystemValidation.Validate(...)` returns `AurelianPreparedGraphicsSubsystemResult` with `Valid` or `Rejected` status plus typed diagnostics.

A61 validation rules:

- null subsystem/options are rejected with `ACG1001 OptionsMissing`;
- ownership values other than `External` are rejected with `ACG1004 UnsupportedOwnership`;
- `PreparedVisible` requires a compositor mechanism with `ACG1002 PreparedVisibleRequiresCompositorMechanism`;
- `PreparedVisible` requires a presentation mechanism with `ACG1003 PreparedVisibleRequiresPresentationMechanism`;
- `Headless` allows missing mechanisms;
- `Headless` with a presentation mechanism remains valid but returns warning `ACG1005 HeadlessIgnoresPresentationMechanism`.

## 6. Engine options integration

`AurelianEngineOptions` now carries `AurelianEngineGraphicsOptions`. Null graphics configuration is normalized to `AurelianEngineGraphicsOptions.Headless`, preserving existing engine defaults while making graphics mode explicit for future production frame-loop/sample work.

`AurelianEngine` still only stores options and lifecycle status. It does not create graphics resources.

## 7. Tests added

`tests/Aurelian.Core.Tests/AurelianEngineGraphicsOptionsM0Tests.cs` covers:

- headless preset uses external ownership;
- prepared-visible preset uses external ownership;
- engine options default graphics is headless;
- headless subsystem allows no mechanisms;
- prepared-visible subsystem requires a compositor mechanism;
- prepared-visible subsystem requires a presentation mechanism;
- prepared-visible subsystem with fake neutral mechanisms is valid;
- headless subsystem with presentation mechanism reports a warning;
- prepared graphics subsystem source does not reference Vulkan/Silk/swapchain/surface/native Vulkan abbreviations.

## 8. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet test tests/Aurelian.Core.Tests/Aurelian.Core.Tests.csproj -c Debug
dotnet test tests/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj -c Debug
rg -n "CreateVulkanSurface|Window.Create|Vk.GetApi|vkCreate|vkCmd|vkQueue|Swapchain|Surface|Silk|Vulkan|Vk" src/Aurelian.Core/Engine/Graphics src/Aurelian.Core/Engine/Frames tests/Aurelian.Core.Tests -g '*.cs' || true
rg -n "Aurelian.Runtime|Dominatus|AiWorld|ActuatorHost|Hfsm|Blackboard|CompositorDispatchAct|CompositorPolicySession" src/Aurelian.Graphics -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Graphics|Silk|Vulkan|Vk|Swapchain|Surface" src/Aurelian.Runtime -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Runtime|Aurelian.Graphics|Silk|Vulkan|Dominatus" src/Aurelian.Rendering.Contracts -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Host|ServiceLocator|Singleton|Activator|GetType\(|Type\.|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src tests -g '*.cs' -g '*.csproj' || true
git status --short
```

The first boundary check reports known A58 Core Vulkan adapter unit-test references and string-based guard tests under `tests/Aurelian.Core.Tests`; it does not report Vulkan/Silk/native references in `src/Aurelian.Core/Engine/Graphics` or `src/Aurelian.Core/Engine/Frames` production files. The remaining dependency scans preserve the established boundaries: Runtime remains graphics-free, Rendering.Contracts remains neutral, and Graphics remains free of Runtime/Dominatus references.

## 9. Validation results

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed.
- `dotnet test tests/Aurelian.Core.Tests/Aurelian.Core.Tests.csproj -c Debug` passed: 33 tests.
- `dotnet test tests/Aurelian.Integration.Tests/Aurelian.Integration.Tests.csproj -c Debug` passed: 5 tests.

## 10. Deferred features

A61 defers:

- engine-owned visible graphics lifecycle;
- production continuous frame loop;
- production visible sample executable;
- production host project;
- automatic Vulkan plant/device/window/surface/swapchain creation in Core;
- moving A60 integration test setup into production Core;
- presentation semaphore handoff policy;
- render graph, scheduler, world extraction/update, and asset/shader integration.

## 11. Next recommendation

A62 — Minimal visible triangle sample executable.

Rationale: Core now has explicit prepared graphics subsystem options and validation, and A60 already proves the visible compositor path in integration tests. A small sample executable can become the human-facing proof while continuing to keep Vulkan/window/swapchain ownership outside the Core frame pump.
