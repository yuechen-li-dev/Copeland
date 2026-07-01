# A23 — PlantContext + PlantRegistry M0

## 1. Files changed

- Added native-free plant model files under `src/Aurelian.Graphics/Plants/`:
  - `PlantId.cs`
  - `PlantKind.cs`
  - `GpuCapabilityTier.cs`
  - `PlantContext.cs`
  - `PlantRegistry.cs`
  - `PlantSelection.cs`
  - `PlantRegistryDiagnostic.cs`
  - `PlantRegistryDiagnosticCodes.cs`
  - `PlantRegistryResult.cs`
- Added deterministic plant registry tests in `tests/Aurelian.Graphics.Tests/PlantRegistryM0Tests.cs`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md` to record A23 and keep native Vulkan initialization deferred.

## 2. Task scope

A23 establishes the graphics plant/controller model before native Vulkan bring-up. The milestone is plant data and registry behavior only.

It deliberately does not create a Vulkan instance, enumerate physical devices, create a window, create a surface, create a swapchain, create a logical device, create queues, create command buffers, implement resources, implement renderer/backend execution, add VMA/VMASharp, add Vortice, add new projects, or modify `CodeReferences/*` or `vendor/Dominatus/*`.

## 3. Plant model

The M0 plant model contains:

- `PlantId`, a value identity where `PlantId.Zero` is the default single-GPU plant.
- `PlantKind`, currently `Unknown` and `Vulkan` only.
- `GpuCapabilityTier`, a plain-data placeholder record with `Unknown`, `SoftwareSmoke`, and `VulkanM0` presets.
- `PlantContext`, a native-free record carrying plant id, kind, capability, display name, and explicit presentation ownership.

`PlantId` is not a native handle. It is the identity that later render, draw, resource, and work records can carry to prove which plant owns them.

## 4. Registry behavior

`PlantRegistry` owns a deterministic, immutable view of plant contexts. Registry order is sorted by `PlantId.Value`, so the same input set produces the same order independent of caller ordering.

Registry validation rejects:

- empty or null plant lists;
- duplicate `PlantId` values;
- missing presentation plant;
- multiple presentation plants.

`TryGet` returns a registered plant by id without throwing. `GetRequired` returns the plant or throws `KeyNotFoundException` for missing ids.

`SingleVulkanPlant()` creates the M0 one-plant path with `PlantId.Zero`, `PlantKind.Vulkan`, `GpuCapabilityTier.VulkanM0`, and `IsPresentationPlant = true`. It performs no native calls.

## 5. Selection policy

`SelectDefault()` returns the presentation plant id. In the M0 single-GPU factory this is plant zero, giving a fixed plant-zero selection policy.

This is intentionally temporary. Dominatus graphics policy is expected to replace this fixed selection later, while preserving explicit `PlantId` flow through resources and work.

## 6. Diagnostics/result model

`PlantRegistry.Create()` returns `PlantRegistryResult` rather than throwing for ordinary invalid input. The result carries a nullable `Registry`, a diagnostics list, and a computed `Success` property.

Diagnostics use `PlantRegistryDiagnosticSeverity` and stable codes:

- `AGP1001 NoPlants`
- `AGP1002 DuplicatePlantId`
- `AGP1003 MissingPresentationPlant`
- `AGP1004 MultiplePresentationPlants`

The public constructor still validates and throws `ArgumentException`, which keeps direct constructor use suitable for programmer-error paths.

## 7. Native-free boundary

All A23 plant files are plain C# data/control model files. They contain no Silk.NET types, no Vulkan handles, no `Vk.GetApi()` call, no windowing types, no native integer handles, no unsafe code, and no device/queue/surface/swapchain/command-buffer model.

The only `Vulkan` text in the plant namespace is intentional plant identity vocabulary: `PlantKind.Vulkan`, `GpuCapabilityTier.VulkanM0`, and `SingleVulkanPlant()`. These are not native API calls or handles.

## 8. Tests added

`PlantRegistryM0Tests` covers:

- `PlantId.Zero` value identity;
- invariant `PlantId.ToString()` formatting;
- single-plant Vulkan registry factory behavior;
- deterministic sorting by `PlantId`;
- `TryGet` and `GetRequired` behavior;
- diagnostics for empty, duplicate, missing-presentation, and multiple-presentation registries;
- default selection returning the presentation plant;
- test-only reflection that `PlantContext` exposes only managed plant data and not native handle-shaped properties.

## 9. Boundary checks

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Silk|Vulkan|Vk|IWindow|WindowOptions|nint|IntPtr|unsafe|Vk[A-Z]|Handle|Device|Queue|Surface|Swapchain|CommandBuffer|vkCreate|Vk.GetApi|GetApi" src/Aurelian.Graphics/Plants tests/Aurelian.Graphics.Tests/PlantRegistryM0Tests.cs -g '*.cs' || true
rg -n "Vortice|VMASharp|Vma|vkCreateInstance|CreateVulkanSurface|IWindow|Window.Create|new Vk|Vk.GetApi|CreateDevice|vkCreateDevice|SwapChain|Swapchain|GraphicsDevice" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
```

The first boundary grep reports only intentional `Vulkan` plant vocabulary in `PlantKind`, `GpuCapabilityTier`, `SingleVulkanPlant`, and corresponding tests. It reports no Silk.NET types, `Vk.GetApi()`, windowing types, native handles, unsafe code, native creation calls, device/queue/surface/swapchain/command-buffer implementation, VMA/Vortice references, or forbidden project references.

## 10. Validation results

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed across the solution, including `Aurelian.Graphics.Tests` with 15 passing tests.
- Boundary checks found no native Vulkan creation, no Vortice/VMA, and no forbidden Aurelian/Dominatus/CodeReferences dependencies in the graphics project/test boundary.

## 11. Deferred features

Deferred from A23:

- Vulkan instance creation;
- physical device enumeration;
- logical device creation;
- queue-family selection;
- queue creation;
- debug utils setup;
- windows, surfaces, swapchains, and presentation execution;
- command buffers and command pools;
- resource allocation and VMA/VMASharp decision;
- renderer/backend execution;
- Dominatus graphics policy integration.

## 12. Next recommendation

A24 — Vulkan instance/device init M0

A24 should:

- create a Vulkan instance/device for plant 0;
- select physical device and queue family;
- require timeline semaphores;
- enable debug utils if available;
- expose diagnostics;
- still avoid swapchain/window unless scoped otherwise.
