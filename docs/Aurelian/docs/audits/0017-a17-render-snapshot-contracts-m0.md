# A17 Render Snapshot Contracts M0 Audit

## 1. Files changed

Production contracts added under `src/Aurelian.Rendering.Contracts/Snapshots/`:

- `RenderTransform2.cs`
- `RenderResourceRef.cs`
- `RenderItem2D.cs`
- `RenderCamera2D.cs`
- `RenderSnapshot.cs`
- `RenderSnapshotStatus.cs`
- `RenderSnapshotDiagnostic.cs`
- `RenderSnapshotDiagnosticCodes.cs`
- `RenderSnapshotResult.cs`

Tests added under `tests/Aurelian.Rendering.Contracts.Tests/`:

- `RenderSnapshotM0Tests.cs`

Documentation updated:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0017-a17-render-snapshot-contracts-m0.md`

## 2. Task scope

A17 implements renderer-independent render snapshot/result DTO contracts only.

The milestone intentionally does not implement:

- a renderer;
- a null renderer;
- a GPU backend;
- backend command planning;
- extraction from world data snapshots;
- asset/shader bridge behavior;
- graphics or windowing package adoption.

## 3. Project-count decision

No new production project was created.

The new contracts live in the existing `Aurelian.Rendering.Contracts` project because A17 defines only the next contract surface inside an already-real rendering contract boundary. An `Aurelian.Rendering.Extraction` project would be premature because no extraction behavior is implemented yet. Future extraction should be split only when there is a real dependency boundary and not merely to make files smaller.

## 4. Render contract model

A17 adds the `Aurelian.Rendering.Contracts.Snapshots` namespace.

The M0 model contains:

- `RenderTransform2` for renderer-side 2D transform data;
- `RenderItem2D` for renderable 2D item DTOs;
- `RenderCamera2D` for simple 2D camera DTOs;
- `RenderSnapshot` for frame-scoped immutable-style snapshot data;
- `RenderSnapshotResult` for snapshot production/validation results;
- diagnostics, diagnostic codes, and status contracts.

The contracts use `RenderFrameId` from `Aurelian.Rendering.Contracts` and otherwise remain rendering-contract-local DTOs.

## 5. Resource refs

A17 adds typed resource references:

- `RenderMeshRef`;
- `RenderMaterialRef`;
- `RenderTextureRef`.

Each reference wraps a string value and returns that value from `ToString()`. M0 deliberately avoids a generic resource-reference hierarchy or backend resource handles.

## 6. Snapshot/result/diagnostic behavior

`RenderSnapshot` carries:

- a frame id;
- an ordered read-only camera list;
- an ordered read-only item list;
- an `IsEmpty` helper that is true only when both lists are empty.

`RenderSnapshotStatus` defines:

- `Ready`;
- `Empty`;
- `Rejected`.

`RenderSnapshotDiagnosticSeverity` defines:

- `Error`;
- `Warning`;
- `Info`.

`RenderSnapshotDiagnosticCodes` defines the M0 stable codes:

- `AR1001 MissingCamera`;
- `AR1002 MissingRenderable`;
- `AR1003 InvalidRenderItem`;
- `AR1004 InvalidCamera`.

`RenderSnapshotResult.Success` is true when the result status is not `Rejected` and no diagnostic has `Error` severity.

## 7. Tests added

A17 adds tests for:

- `RenderTransform2.Identity` values;
- resource reference `ToString()` behavior;
- empty snapshot detection;
- snapshots holding cameras and items;
- ready snapshot results without errors succeeding;
- rejected snapshot results with errors failing;
- rendering contracts project file not referencing the world project.

The rendering contracts tests do not reference world types.

## 8. Boundary checks

Boundary commands run:

```bash
rg -n "Aurelian.World|UnitId|WorldData|Transform2|WorldDocument|ResolvedWorld|Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget|Reflection|Activator|GetType\(|Type\." src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true

rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true

rg -n "ProjectReference" src/Aurelian.Rendering.Contracts src/Aurelian.World src/Aurelian.Actuation -g '*.csproj'

git status --short
```

The first boundary scan is expected to report the new `RenderTransform2` type and tests because the required A17 contract name intentionally contains the substring `Transform2`. Those hits are not world dependencies and do not reference world transform types. No world, asset, shader, graphics/windowing, backend, reflection, or vendor dependency was added.

The second boundary scan returned no vendor/reference matches.

The project-reference scan shows `Aurelian.Rendering.Contracts` references only `Aurelian.Core`; `Aurelian.World` does not reference rendering; and `Aurelian.Actuation` references world for world mutation contracts.

## 9. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Aurelian.World|UnitId|WorldData|Transform2|WorldDocument|ResolvedWorld|Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget|Reflection|Activator|GetType\(|Type\." src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "ProjectReference" src/Aurelian.Rendering.Contracts src/Aurelian.World src/Aurelian.Actuation -g '*.csproj'
git status --short
```

Results:

- Build passed.
- Tests passed.
- Boundary scans showed no forbidden production dependency; `RenderTransform2` substring hits are expected from the A17-required DTO name.
- No new production project was created.
- No graphics/windowing packages were added.
- No extractor or renderer/backend was added.

## 10. Deferred features

Deferred features remain:

- render command plan contracts;
- world-to-render snapshot extraction;
- renderable and camera world stores;
- null renderer;
- GPU renderer/backend;
- backend resource lifetime management;
- asset/shader manifest bridge;
- projection matrices;
- 3D camera and 3D render item contracts;
- visibility/culling policy;
- sorting/batching policy beyond simple item sort order.

## 11. Next recommendation

Recommended next milestone:

```text
A18 — Render command plan contracts M0
```

Render snapshots are now a backend-independent source DTO boundary. The next locality-preserving step should define backend-independent command plan contracts before extraction or backend work, so later world-to-render extraction and null/GPU renderers consume the same explicit command boundary.
