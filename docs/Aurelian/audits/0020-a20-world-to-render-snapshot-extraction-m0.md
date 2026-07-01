# A20 — World-to-render snapshot extraction M0

## 1. Files changed

Production files changed or added:

- `src/Aurelian.World/Stores/Renderable2DData.cs`
- `src/Aurelian.World/Stores/Renderable2DStore.cs`
- `src/Aurelian.World/Stores/WorldDataDocument.cs`
- `src/Aurelian.World/Stores/WorldDataSnapshotBuilder.cs`
- `src/Aurelian.World/Stores/UnitDataSnapshot.cs`
- `src/Aurelian.Actuation/World/Requests/SetRenderable2DRequest.cs`
- `src/Aurelian.Actuation/World/Requests/RemoveRenderable2DRequest.cs`
- `src/Aurelian.Actuation/World/WorldActuationDiagnosticCodes.cs`
- `src/Aurelian.Actuation/World/WorldDataActuator.cs`
- `src/Aurelian.Runtime/Aurelian.Runtime.csproj`
- `src/Aurelian.Runtime/Rendering/WorldRenderSnapshotOptions.cs`
- `src/Aurelian.Runtime/Rendering/WorldRenderSnapshotDiagnosticCodes.cs`
- `src/Aurelian.Runtime/Rendering/WorldRenderSnapshotExtractor.cs`

Test files changed or added:

- `tests/Aurelian.World.Tests/Renderable2DStoreM0Tests.cs`
- `tests/Aurelian.Actuation.Tests/Renderable2DActuatorM0Tests.cs`
- `tests/Aurelian.Runtime.Tests/Aurelian.Runtime.Tests.csproj`
- `tests/Aurelian.Runtime.Tests/WorldRenderSnapshotExtractorM0Tests.cs`

Documentation files changed or added:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/world-model-doctrine.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0020-a20-world-to-render-snapshot-extraction-m0.md`

## 2. Task scope

A20 implements World-to-render snapshot extraction M0 and proves the full headless path:

```text
WorldDataDocument -> RenderSnapshot -> RenderCommandPlan -> NullRenderer
```

The scope is intentionally limited to data extraction and headless validation. A20 does not add a visual backend, GPU work, windows, shader compilation integration, asset material binding, an ECS manager, or a scene system.

## 3. Project-count/dependency decision

No new production project was created for extraction.

Extraction glue lives in `Aurelian.Runtime.Rendering` because runtime composition is allowed to reference both `Aurelian.World` and `Aurelian.Rendering.Contracts`.

Dependency boundary after A20:

```text
Aurelian.World:
  no rendering dependency

Aurelian.Rendering.Contracts:
  no world dependency

Aurelian.Runtime:
  references World + Rendering.Contracts

Aurelian.Rendering.Null:
  references Rendering.Contracts only
```

`Aurelian.Runtime` production code does not reference `Aurelian.Rendering.Null`. Runtime tests reference the null renderer only to prove the full headless path.

## 4. Renderable world store

A20 adds `Renderable2DData` and `Renderable2DStore` under `Aurelian.World.Stores`.

The renderable data is world-owned and symbolic:

- `WorldMeshRef` wraps a string mesh reference.
- `WorldMaterialRef` wraps a string material reference.
- `Renderable2DData` carries mesh, material, visibility, and sort order.
- `Renderable2DStore` is immutable-style: `Set` and `Remove` return new store instances without mutating the original store.

These refs are not render-contract refs, asset handles, shader handles, GPU handles, or backend objects.

`WorldDataDocument` now includes `Renderable2DStore Renderables`, and `WorldDataSnapshot` unit rows now include optional `Renderable2DData`.

## 5. Renderable actuation requests

A20 adds typed actuation requests:

- `SetRenderable2DRequest`
- `RemoveRenderable2DRequest`

`WorldDataActuator` now validates and applies these requests.

Validation behavior:

- setting a renderable requires an existing unit;
- mesh refs must be non-empty/non-whitespace;
- material refs must be non-empty/non-whitespace;
- removing a missing renderable returns `NoOp` with an info diagnostic;
- rejected and no-op results preserve the original document.

New diagnostic codes:

- `AAW1015` invalid mesh ref;
- `AAW1016` invalid material ref;
- `AAW1017` renderable missing.

Existing `AAW1013` and `AAW1014` remain assigned to the A16 name/transform removal diagnostics, so A20 used the next available codes to avoid changing existing code identities.

## 6. Extraction model

`WorldRenderSnapshotExtractor.Extract(...)` resolves the world, creates a default M0 camera, and maps visible renderable units to `RenderItem2D` values.

Extraction behavior:

1. Resolve the `WorldDocument` inside `WorldDataDocument`.
2. Reject extraction if world resolution fails.
3. Create one default `RenderCamera2D` from `WorldRenderSnapshotOptions`.
4. Iterate units in deterministic resolved snapshot preorder.
5. Skip units with no renderable data.
6. Skip units whose renderable data has `Visible == false`.
7. Use the unit transform if present; otherwise use identity.
8. Map `WorldMeshRef.Value` to `RenderMeshRef` and `WorldMaterialRef.Value` to `RenderMaterialRef`.
9. Use the unit id string as the render item id for deterministic output.
10. Return `Empty` with an info diagnostic when no visible renderables exist; otherwise return `Ready`.

Runtime-specific extraction diagnostics:

- `ARX1001` world resolution failed;
- `ARX1002` no renderable units.

## 7. Full headless chain test

A20 adds `WorldToRenderToCommandPlanToNullRenderer_ProducesDeterministicTrace` in `Aurelian.Runtime.Tests`.

The test builds a world data document with a root and renderable child, extracts a `RenderSnapshot`, builds a `RenderCommandPlan` via `RenderCommandPlanBuilder.FromSnapshot(...)`, renders it with `NullRenderer`, and asserts deterministic draw trace id, mesh, material, position, and sort order.

This proves the current headless pipeline:

```text
WorldDataDocument
  -> RenderSnapshot
  -> RenderCommandPlan
  -> NullRenderer trace
```

## 8. Boundary checks

A20 boundary checks run:

```bash
rg -n "ProjectReference" src/Aurelian.Runtime tests/Aurelian.Runtime.Tests -g '*.csproj'
rg -n "Aurelian.Rendering|RenderSnapshot|RenderItem|RenderMeshRef|RenderMaterialRef" src/Aurelian.World tests/Aurelian.World.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Rendering.Null|NullRenderer" src/Aurelian.Runtime -g '*.cs' -g '*.csproj' || true
rg -n "Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget2D|Reflection|Activator|GetType\(|Type\." src/Aurelian.World src/Aurelian.Runtime src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests tests/Aurelian.Runtime.Tests -g '*.cs' -g '*.csproj' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.World src/Aurelian.Runtime src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests tests/Aurelian.Runtime.Tests -g '*.cs' -g '*.csproj' || true
```

Expected boundary outcome was met:

- `Aurelian.World` has no rendering dependency or render-contract refs.
- `Aurelian.Runtime` production has no null renderer dependency.
- No forbidden GPU/window/assets/shaders dependency was introduced in the checked A20 production/test areas.
- No CodeReferences/vendor coupling was introduced in the checked A20 production/test areas.

## 9. Validation results

Validation commands passed:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
```

Observed results:

- build passed with 0 warnings and 0 errors;
- tests passed across the solution;
- world tests include renderable store/document/snapshot coverage;
- actuation tests include set/remove/validation/no-mutation coverage;
- runtime tests include extraction status behavior and the full world-to-null-renderer headless chain.

## 10. Deferred features

Deferred beyond A20:

- real GPU renderer/backend;
- windowing;
- graphics HAL;
- Silk.NET/Vortice/Vulkan/D3D integration;
- shader asset manifest bridge;
- asset/material loading;
- camera data stores beyond the M0 default camera;
- full scene system;
- ECS manager/processors;
- Dominatus world observation bridge;
- blackboards;
- image output or screenshots.

## 11. Next recommendation

A21 — First visual backend decision audit.

The world-to-render-to-command-plan-to-null-renderer path now exists and is validated headlessly. The next major architectural choice should be the first actual visual backend direction and package boundary, before introducing GPU/window code.
