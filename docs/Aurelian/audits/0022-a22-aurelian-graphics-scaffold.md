# A22 — Aurelian.Graphics scaffold and Silk.NET package smoke

## 1. Files changed

- `Directory.Packages.props` adds central versions for `Silk.NET.Vulkan` and `Silk.NET.Windowing`.
- `Aurelian.slnx` includes the new production and test projects.
- `src/Aurelian.Graphics/Aurelian.Graphics.csproj` creates the first graphics HAL project boundary.
- `src/Aurelian.Graphics/AurelianGraphicsProject.cs` adds the project identity smoke type.
- `src/Aurelian.Graphics/Vulkan/VulkanPackageSmoke.cs` adds package/API smoke access for Silk.NET Vulkan and windowing types.
- `src/Aurelian.Graphics/Plants/` and `src/Aurelian.Graphics/Vulkan/*/` add the planned graphics HAL folder scaffold.
- `tests/Aurelian.Graphics.Tests/Aurelian.Graphics.Tests.csproj` creates the graphics test project.
- `tests/Aurelian.Graphics.Tests/AurelianGraphicsProjectTests.cs` covers project identity, package smoke values, and direct forbidden assembly references.
- `README.md`, `docs/architecture/mvp-roadmap.md`, and `docs/architecture/dependency-policy.md` document the A22 scaffold boundary and A23 recommendation.

## 2. Task scope

A22 is a scaffold/package-smoke milestone only. It creates the first `Aurelian.Graphics` project and test project, wires central Silk.NET package versions, and proves package types are visible to Aurelian-owned code.

A22 deliberately does not implement Vulkan or rendering behavior. It does not create a Vulkan instance, window, surface, physical device, logical device, swapchain, command buffer, renderer, resource implementation, plant registry, or plant context.

## 3. Project scaffold

Created production scaffold:

```text
src/Aurelian.Graphics/
```

Created test scaffold:

```text
tests/Aurelian.Graphics.Tests/
```

Created planned folder structure:

```text
src/Aurelian.Graphics/Plants/
src/Aurelian.Graphics/Vulkan/Device/
src/Aurelian.Graphics/Vulkan/Sync/
src/Aurelian.Graphics/Vulkan/Resources/
src/Aurelian.Graphics/Vulkan/Commanding/
src/Aurelian.Graphics/Vulkan/Pipelines/
src/Aurelian.Graphics/Vulkan/Presentation/
src/Aurelian.Graphics/Vulkan/Compositor/
src/Aurelian.Graphics/Vulkan/Diagnostics/
```

Empty scaffold folders are retained with `.gitkeep` files only; they contain no graphics implementation.

## 4. Package references

Central package versions added:

- `Silk.NET.Vulkan` `2.23.0`
- `Silk.NET.Windowing` `2.23.0`

`Aurelian.Graphics` references those two packages. `Silk.NET.Core` was not added explicitly because the smoke code compiles and tests pass with the core package remaining transitive through Silk.NET packages.

No Vortice packages were added. No VMASharp/VMA packages were added.

## 5. Project references/dependency boundary

`Aurelian.Graphics` references only:

- `src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj`
- `Silk.NET.Vulkan`
- `Silk.NET.Windowing`

It does not reference world, assets, shaders, Dominatus, the null renderer, vendor source, or code reference source. `Aurelian.Graphics.Tests` references only `Aurelian.Graphics` plus normal xUnit test packages.

## 6. Package smoke code

The production smoke code exposes:

- `AurelianGraphicsProject.Name`, returning `Aurelian.Graphics`.
- `VulkanPackageSmoke.VulkanApiName`, using `nameof(Silk.NET.Vulkan.Vk)`.
- `VulkanPackageSmoke.WindowOptionsName`, using `nameof(Silk.NET.Windowing.WindowOptions)`.

This proves the package types are visible without creating any native object.

## 7. Tests added

Added `tests/Aurelian.Graphics.Tests/AurelianGraphicsProjectTests.cs` with:

- `AurelianGraphicsProject_Name_IsAurelianGraphics`
- `VulkanPackageSmoke_ExposesSilkTypes`
- `AurelianGraphics_DoesNotRequireWorldAssetsShadersOrNullRenderer`

The last test checks the direct referenced assemblies of `Aurelian.Graphics` and avoids introducing forbidden dependency literals into project files.

## 8. Boundary checks

Boundary checks were run after implementation:

```bash
rg -n "Vortice|VMASharp|Vma|vkCreateInstance|CreateVulkanSurface|IWindow|Window.Create|new Vk|Vk.GetApi|CreateDevice|vkCreateDevice|SwapChain|Swapchain|GraphicsDevice" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
```

Result: no matches.

```bash
rg -n "Aurelian.World|Aurelian.Assets|Aurelian.Shaders|Aurelian.Rendering.Null|Dominatus|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.cs' -g '*.csproj' || true
```

Result: no matches.

```bash
rg -n "Silk.NET.Vulkan|Silk.NET.Windowing|Silk.NET.Core|Vortice|VMASharp" Directory.Packages.props src tests -g '*.props' -g '*.csproj'
```

Result: only `Silk.NET.Vulkan` and `Silk.NET.Windowing` appear in `Directory.Packages.props` and `src/Aurelian.Graphics/Aurelian.Graphics.csproj`. No `Silk.NET.Core`, Vortice, or VMASharp references appear.

```bash
rg -n "ProjectReference" src/Aurelian.Graphics tests/Aurelian.Graphics.Tests -g '*.csproj'
```

Result: `Aurelian.Graphics` references rendering contracts only; `Aurelian.Graphics.Tests` references `Aurelian.Graphics` only.

## 9. Validation results

Validation commands passed:

```bash
dotnet restore Aurelian.slnx
```

```bash
dotnet build Aurelian.slnx -c Debug
```

```bash
dotnet test Aurelian.slnx -c Debug
```

The full solution restore, build, and test suite completed successfully.

## 10. Deferred features

Deferred beyond A22:

- Vulkan instance creation
- Window creation
- Surface creation
- GPU enumeration
- Logical device creation
- Swapchain creation
- Command buffers
- Renderer implementation
- Resource implementation
- Plant registry implementation
- Plant context implementation
- Vortice adoption
- VMASharp/VMA adoption
- Stride.Graphics porting
- CodeReferences/vendor modifications

## 11. Next recommendation

A23 — PlantContext + PlantRegistry M0

A23 should:

- define `PlantId`, `PlantContext`, and `PlantRegistry`;
- define graphics diagnostics DTOs;
- support one fixed plant descriptor/context without native Vulkan device creation if possible;
- preserve one plant in M0;
- avoid a global graphics singleton;
- avoid Vulkan instance/device creation unless absolutely needed for shape tests.
