# A24 — Vulkan instance/device initialization M0

## 1. Files changed

- Added Vulkan init diagnostics and facts under `src/Aurelian.Graphics/Vulkan/Diagnostics/`.
- Added Vulkan plant options, initializer, and native handle owner under `src/Aurelian.Graphics/Vulkan/Device/`.
- Enabled unsafe blocks in `src/Aurelian.Graphics/Aurelian.Graphics.csproj` for the Vulkan interop boundary only.
- Added unavailable-safe Vulkan initialization tests in `tests/Aurelian.Graphics.Tests/VulkanPlantInitializerM0Tests.cs`.
- Updated `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md`.

## 2. Task scope

A24 implements instance/device initialization only for plant 0. It creates a Vulkan API object, Vulkan instance, selects a physical device and queue family, creates a logical device, retrieves the selected queue, exposes plain facts/diagnostics, and owns native lifetime through a disposable plant object.

A24 deliberately does not create a window, surface, swapchain, command buffers, fences, semaphores, buffers, textures, resources, renderer, VMA/VMASharp, Vortice dependency, global graphics singleton, or new project.

## 3. Native Vulkan boundary

The native boundary is contained in `Aurelian.Graphics.Vulkan.Device`. Unsafe code is enabled project-local because Silk.NET Vulkan creation and enumeration APIs use pointers, fixed buffers, and pNext chains. The native handles are owned by `AurelianVulkanPlant`; the A23 plant model remains native-free.

## 4. Instance creation behavior

`VulkanPlantInitializer.CreatePlant` calls `Vk.GetApi()` and returns `Unavailable` with `AGV1001` when the Vulkan loader/runtime cannot be loaded. Instance creation uses application/engine names from `VulkanPlantOptions`, requests Vulkan 1.2, and does not request surface/window extensions.

When validation is requested, `VK_LAYER_KHRONOS_validation` is enabled only if the layer is available. Missing validation produces warning `AGV1003` and does not fail initialization. Debug utils extension availability is checked with the same optional pattern: if `VK_EXT_debug_utils` is available it is enabled, otherwise warning `AGV1009` is recorded. A debug messenger is intentionally deferred.

## 5. Physical device selection

The initializer enumerates physical devices from the created instance. No devices returns `Unavailable` with `AGV1004`. Candidate devices must have a suitable queue family and, when required, timeline semaphore support. Selection prefers discrete GPU, then integrated GPU, then other suitable devices, with deterministic tie-break by device name and enumeration order.

## 6. Queue family selection

Queue family selection enumerates queue-family properties and chooses the first family with at least one queue and graphics + compute + transfer support. The implementation does not hardcode queue family index 0. The selected queue family index is recorded in `VulkanPlantFacts`.

## 7. Timeline semaphore requirement

A24 requires timeline semaphore support by default through `VulkanPlantOptions.RequireTimelineSemaphores`. Support is queried with `PhysicalDeviceFeatures2` and `PhysicalDeviceTimelineSemaphoreFeatures` in the pNext chain. Device creation enables the timeline semaphore feature through the device create pNext chain. Devices that cannot satisfy the requirement are rejected rather than silently accepted.

## 8. Diagnostics/facts model

The public result model is plain data:

- `VulkanInitStatus`: `Created`, `Unavailable`, `Rejected`, `Failed`.
- `VulkanInitDiagnostic`: stable code, severity, message, optional plant id.
- `VulkanPlantFacts`: plant id, physical-device facts, queue-family index, timeline semaphore support, and enabled layer/extension names.

Stable diagnostic codes use the requested `AGV1001` through `AGV1010` range.

## 9. Disposal/lifetime behavior

`AurelianVulkanPlant` implements `IDisposable` and safely double-disposes. Disposal destroys the logical device first, then the instance, then disposes the Silk.NET `Vk` API object. Native handles are per plant instance and no global device/singleton is introduced.

## 10. Tests added

`VulkanPlantInitializerM0Tests` covers:

- initialization does not throw;
- status is created, unavailable, or rejected for ordinary environments;
- created plants expose plant-zero facts;
- created plants dispose cleanly and double-dispose cleanly;
- created plants record the selected queue-family fact and device-selected diagnostic;
- unavailable results include diagnostics.

Tests intentionally pass on machines without a Vulkan loader, Vulkan runtime, physical GPU, or suitable Vulkan device.

## 11. Boundary checks

Boundary searches were run for prohibited dependencies and concepts: global static devices/Vk, service locator/singleton vocabulary, world/assets/shaders/null-renderer/vendor/CodeReferences references, Vortice/VMA, surface/window/swapchain, command buffers, buffers/textures, and hardcoded queue-family index patterns.

Expected text matches include intentional package smoke references to `Silk.NET.Windowing`, project references, `PlantId.Zero`, and Vulkan type names in the new Vulkan boundary.

## 12. Validation results

Validation commands:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "static .*Device|static .*Vk|Singleton|ServiceLocator|Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland|Vortice|VMASharp|Vma|CreateVulkanSurface|IWindow|Window.Create|SwapChain|Swapchain|CommandBuffer|Buffer|Texture" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
rg -n "queueFamilyIndex\s*=\s*0|QueueFamilyIndex\s*=\s*0" src/Aurelian.Graphics -g '*.cs' || true
rg -n "ProjectReference" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.csproj'
git status --short
```

## 13. Deferred features

Deferred beyond A24:

- debug utils messenger creation/callback lifetime;
- timeline fence/semaphore wrappers and waits;
- command pools and command buffers;
- buffers, textures, images, memory allocation, VMA/VMASharp;
- window, surface, swapchain, presentation;
- rendering and triangle output;
- Dominatus graphics policy and multi-plant selection.

## 14. Next recommendation

A25 — Timeline fences and resource pool M0

A25 should:

- add per-plant frame/command/copy timeline semaphore wrappers;
- avoid starvation-prone waits;
- add native-free pool contracts and maybe Vulkan command pool pool shape;
- still avoid swapchain/window/resources unless scoped otherwise.
