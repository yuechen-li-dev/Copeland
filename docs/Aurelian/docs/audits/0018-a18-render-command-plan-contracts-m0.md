# A18 Render Command Plan Contracts M0 Audit

## 1. Files changed

Production contracts added under `src/Aurelian.Rendering.Contracts/CommandPlans/`:

- `RenderCommandPlanStatus.cs`
- `RenderCommandPlanReason.cs`
- `RenderCommandPlanDiagnostic.cs`
- `RenderCommandPlanDiagnosticCodes.cs`
- `RenderPipelineRef.cs`
- `RenderShaderRef.cs`
- `RenderTargetRef.cs`
- `DrawItem2D.cs`
- `RenderPassPlan.cs`
- `RenderBufferUploadPlan.cs`
- `RenderCommandPlan.cs`
- `RenderCommandPlanResult.cs`
- `RenderCommandPlanBuilder.cs`

Tests added under `tests/Aurelian.Rendering.Contracts.Tests/`:

- `RenderCommandPlanM0Tests.cs`

Documentation updated:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0018-a18-render-command-plan-contracts-m0.md`

## 2. Task scope

A18 implements backend-independent render command plan DTO contracts only, plus a tiny contract-local snapshot-to-plan builder.

The milestone intentionally does not implement:

- a renderer;
- a null renderer;
- a GPU backend;
- backend-native command execution;
- world-to-render extraction;
- asset/shader manifest bridge behavior;
- graphics or windowing package adoption.

## 3. Project-count decision

No new production project was created.

The command-plan contracts live in the existing `Aurelian.Rendering.Contracts` project because the dependency boundary is the same renderer-independent contract boundary introduced for snapshots in A17. Splitting command plans into a new production project would be premature because A18 adds only DTOs and contract-local conversion from snapshot DTOs.

## 4. Command plan model

A18 adds the `Aurelian.Rendering.Contracts.CommandPlans` namespace.

The M0 model contains:

- `RenderCommandPlan` as the full command-plan boundary;
- `RenderPassPlan` as a backend-independent pass description;
- `DrawItem2D` as draw intent copied or derived from snapshot items;
- symbolic `RenderPipelineRef`, `RenderShaderRef`, and `RenderTargetRef` values;
- `RenderBufferUploadPlan` as a minimal count-only upload DTO;
- statuses, reasons, diagnostics, and diagnostic codes.

The symbolic refs are not GPU handles and do not represent backend-native objects.

## 5. Snapshot-to-plan builder

A18 includes `RenderCommandPlanBuilder.FromSnapshot` because it references only snapshot contracts inside the same project.

The builder behavior is deterministic:

- an empty snapshot returns `Empty` with `EmptySnapshot`;
- a snapshot with items and no camera returns `Rejected` with `MissingCamera`;
- a snapshot with cameras and no items returns `Empty` with `MissingDrawItems`;
- missing pipeline or shader refs return `Rejected`;
- invalid draw item ids/mesh refs/material refs return `Rejected`;
- valid items are sorted by `SortOrder`, then `Id` using ordinal string comparison;
- valid snapshots produce one pass named `Main2D`.

## 6. Status/reason/diagnostic behavior

`RenderCommandPlanStatus` defines:

- `Ready`;
- `Empty`;
- `Rejected`.

`RenderCommandPlanReason` defines:

- `Ready`;
- `EmptySnapshot`;
- `MissingCamera`;
- `MissingDrawItems`;
- `InvalidDrawItem`;
- `MissingPipeline`;
- `MissingShader`;
- `UnsupportedFeature`.

`RenderCommandPlanDiagnosticSeverity` defines:

- `Error`;
- `Warning`;
- `Info`.

`RenderCommandPlanDiagnosticCodes` defines stable M0 codes:

- `ACP1001 EmptySnapshot`;
- `ACP1002 MissingCamera`;
- `ACP1003 MissingDrawItems`;
- `ACP1004 InvalidDrawItem`;
- `ACP1005 MissingPipeline`;
- `ACP1006 MissingShader`;
- `ACP1007 UnsupportedFeature`.

`RenderCommandPlan.Success` is true when the plan is not rejected and no diagnostic has `Error` severity. `RenderCommandPlan.IsEmpty` is true when status is `Empty`.

## 7. Tests added

A18 adds tests for:

- ready command plans without errors succeeding;
- rejected command plans with errors failing;
- empty command plans succeeding and reporting empty;
- symbolic render refs returning their values from `ToString()`;
- render pass plans holding draw items;
- empty snapshots converting to empty command plans;
- missing cameras converting to rejected command plans;
- snapshots with cameras and items converting to a ready single-pass plan;
- builder sorting by sort order and then id;
- the rendering contracts project file avoiding world, asset, shader, backend, and graphics/windowing references.

## 8. Boundary checks

Boundary commands run:

```bash
rg -n "Aurelian.World|UnitId|WorldData|Transform2|WorldDocument|ResolvedWorld|Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget2D|Reflection|Activator|GetType\(|Type\." src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true

rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true

rg -n "ProjectReference" src/Aurelian.Rendering.Contracts -g '*.csproj'

git status --short
```

The first boundary scan is expected to report `RenderTransform2` references because the A17 DTO name intentionally contains the substring `Transform2`. These are rendering snapshot DTOs, not world transform dependencies. The scan may also report string literals in tests that assert forbidden dependencies are absent from the project file. Those are test guard strings, not dependencies.

The second boundary scan returned no vendor/reference matches.

The project-reference scan shows `Aurelian.Rendering.Contracts` still references only `Aurelian.Core`.

## 9. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Aurelian.World|UnitId|WorldData|Transform2|WorldDocument|ResolvedWorld|Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget2D|Reflection|Activator|GetType\(|Type\." src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Rendering.Contracts tests/Aurelian.Rendering.Contracts.Tests -g '*.cs' -g '*.csproj' || true
rg -n "ProjectReference" src/Aurelian.Rendering.Contracts -g '*.csproj'
git status --short
```

Results:

- Build passed.
- Tests passed.
- Boundary scans showed no forbidden production dependency.
- No new production project was created.
- No graphics/windowing packages were added.
- No extractor or renderer/backend was added.

## 10. Deferred features

Deferred features remain:

- null renderer;
- GPU renderer/backend;
- backend command execution;
- backend resource lifetime management;
- world-to-render snapshot extraction;
- renderable and camera world stores;
- asset/shader manifest bridge;
- projection matrices;
- depth/stencil/blend state;
- render target dimensions;
- clear color policy;
- batching and culling policy;
- 3D camera and 3D draw item contracts.

## 11. Next recommendation

Recommended next milestone:

```text
A19 — Null renderer M0
```

Command plans can now be consumed by a deterministic headless backend before world extraction or GPU work. A null renderer should prove the command-plan consumer boundary without introducing graphics/windowing dependencies.
