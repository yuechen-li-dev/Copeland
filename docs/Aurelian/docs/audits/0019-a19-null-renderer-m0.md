# A19 Null Renderer M0 Audit

## 1. Files changed

Production backend project added:

- `src/Aurelian.Rendering.Null/Aurelian.Rendering.Null.csproj`
- `src/Aurelian.Rendering.Null/NullRenderStatus.cs`
- `src/Aurelian.Rendering.Null/NullRenderDiagnostic.cs`
- `src/Aurelian.Rendering.Null/NullRenderDiagnosticCodes.cs`
- `src/Aurelian.Rendering.Null/NullRenderTrace.cs`
- `src/Aurelian.Rendering.Null/NullRenderPassTrace.cs`
- `src/Aurelian.Rendering.Null/NullRenderDrawTrace.cs`
- `src/Aurelian.Rendering.Null/NullRenderResult.cs`
- `src/Aurelian.Rendering.Null/NullRenderer.cs`

Test project added:

- `tests/Aurelian.Rendering.Null.Tests/Aurelian.Rendering.Null.Tests.csproj`
- `tests/Aurelian.Rendering.Null.Tests/NullRendererM0Tests.cs`

Solution and documentation updated:

- `Aurelian.slnx`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/audits/0019-a19-null-renderer-m0.md`

## 2. Task scope

A19 implements the first backend-shaped renderer implementation: a headless null renderer over `Aurelian.Rendering.Contracts.CommandPlans.RenderCommandPlan`.

The null renderer consumes command plans and returns deterministic trace/result DTOs suitable for tests before a GPU, window, or visual backend exists.

A19 intentionally does not implement:

- GPU command execution;
- window creation;
- swap chains;
- images or screenshots;
- visual rendering;
- world-to-render extraction;
- asset or shader manifest bridging;
- backend-native handles;
- graphics/windowing package adoption.

## 3. Project-count justification

A new production project is justified for A19 because `Aurelian.Rendering.Null` is the first actual backend implementation boundary.

`Aurelian.Rendering.Contracts` remains pure renderer-independent DTO/contracts. It still owns snapshots, command plans, symbolic refs, statuses, diagnostics, and the contract-local snapshot-to-plan builder, but it does not reference the null renderer or any backend implementation.

The dependency direction is one-way:

```text
Aurelian.Rendering.Null -> Aurelian.Rendering.Contracts
```

## 4. Null renderer model

The null renderer model lives in namespace `Aurelian.Rendering.Null` and contains:

- `NullRenderStatus` with `Rendered`, `NoOp`, and `Rejected`;
- `NullRenderDiagnosticSeverity` with `Error`, `Warning`, and `Info`;
- `NullRenderDiagnostic` for code/severity/message diagnostics;
- `NullRenderDiagnosticCodes` with stable M0 codes:
  - `ANR1001` command plan rejected;
  - `ANR1002` empty plan no-op;
  - `ANR1003` invalid ready plan;
- trace records for passes and draw items;
- `NullRenderResult` with success derived from status and diagnostic severities;
- `NullRenderer.Render(RenderCommandPlan plan)` as the M0 entry point.

## 5. Render trace/result behavior

Rejected command plans return:

- `NullRenderStatus.Rejected`;
- an empty trace;
- `ANR1001` with error severity;
- a message that includes the command-plan reason.

Empty command plans return:

- `NullRenderStatus.NoOp`;
- an empty trace;
- `ANR1002` with info severity.

Ready command plans are validated before tracing:

- a ready plan must contain at least one pass;
- every pass must contain at least one draw item.

Valid ready plans return:

- `NullRenderStatus.Rendered`;
- deterministic primitive trace values for pass name, target, pipeline, shader, draw id, mesh, material, transform X/Y, and sort order;
- no diagnostics.

Malformed ready plans return:

- `NullRenderStatus.Rejected`;
- an empty trace;
- `ANR1003` with error severity.

The null renderer does not mutate the input command plan and does not perform backend work.

## 6. Tests added

A19 adds `NullRendererM0Tests` covering:

- `NullRenderer_RenderReadyPlan_ProducesDeterministicTrace`;
- `NullRenderer_RenderReadyPlan_ReportsPassAndDrawCounts`;
- `NullRenderer_RenderEmptyPlan_ReturnsNoOpTrace`;
- `NullRenderer_RenderRejectedPlan_ReturnsRejectedDiagnostic`;
- `NullRenderer_RenderMalformedReadyPlan_ReturnsInvalidReadyPlanDiagnostic`;
- `NullRenderer_DoesNotRequireWorldAssetsShadersOrBackend`.

The tests construct command-plan DTOs directly. They do not use world, assets, shader modules, graphics libraries, images, or windows.

## 7. Boundary checks

Boundary checks run for A19:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Aurelian.World|UnitId|WorldData|WorldDocument|ResolvedWorld|Aurelian.Assets|Aurelian.Shaders|GraphicsDevice|Silk|Vortice|Vulkan|D3D|Window|SwapChain|RenderTarget2D|Reflection|Activator|GetType\(|Type\." src/Aurelian.Rendering.Null tests/Aurelian.Rendering.Null.Tests -g '*.cs' -g '*.csproj' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Rendering.Null tests/Aurelian.Rendering.Null.Tests -g '*.cs' -g '*.csproj' || true
rg -n "ProjectReference" src/Aurelian.Rendering.Null tests/Aurelian.Rendering.Null.Tests -g '*.csproj'
```

## 8. Validation results

Validation results:

- `dotnet build Aurelian.slnx -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test Aurelian.slnx -c Debug` passed, including the 6 new null renderer tests.
- Dependency boundary search found no forbidden world/assets/shaders/graphics/windowing/backend-native references in the null renderer production or test code.
- CodeReferences/vendor coupling search found no forbidden references in the null renderer production or test code.
- Project-reference search showed `Aurelian.Rendering.Null` references only `Aurelian.Rendering.Contracts`; the test project references the null renderer and contracts only.

## 9. Deferred features

Deferred beyond A19:

- world-to-render snapshot extraction;
- shader asset manifest bridge;
- real render backend package scaffold;
- visual backend decision and package selection;
- GPU buffers, command lists, render targets, swap chains, and windows;
- first triangle rendering;
- image output or screenshots;
- asset/shader/world integration.

## 10. Next recommendation

A20 — World-to-render snapshot extraction M0.

Snapshots, command plans, and a headless backend consumer now exist. The next missing link is extracting renderer-independent snapshots from world data so the existing snapshot-to-plan builder and null renderer can validate the world-to-render path before any GPU/window backend is introduced.
