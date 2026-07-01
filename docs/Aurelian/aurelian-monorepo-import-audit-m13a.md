# Aurelian Monorepo Import Audit M13a

## Purpose

This document audits the imported Aurelian source tree, documentation tree, and solution topology after import into the current repository. It is a migration map, not an integration plan. M13a does not move large trees, merge solutions, or change runtime behavior.

## Imported source layout

Imported Aurelian source projects currently present under `src/`:

- `Aurelian.Actuation`
- `Aurelian.Assets`
- `Aurelian.AssetTool`
- `Aurelian.Core`
- `Aurelian.Graphics`
- `Aurelian.Rendering.Contracts`
- `Aurelian.Rendering.Null`
- `Aurelian.Runtime`
- `Aurelian.Shaders`
- `Aurelian.World`

Imported Aurelian test projects currently present under `tests/`:

- `Aurelian.Actuation.Tests`
- `Aurelian.Assets.Tests`
- `Aurelian.AssetTool.Tests`
- `Aurelian.Core.Tests`
- `Aurelian.Graphics.Tests`
- `Aurelian.Integration.Tests`
- `Aurelian.Rendering.Contracts.Tests`
- `Aurelian.Rendering.Null.Tests`
- `Aurelian.Runtime.Tests`
- `Aurelian.Shaders.Tests`
- `Aurelian.World.Tests`

Representative source volume from the import:

| Project | Approximate `.cs`/`.csproj` file count | Audit note |
| --- | ---: | --- |
| `Aurelian.Graphics` | 202 | Largest imported area; mostly Vulkan-oriented backend work. |
| `Aurelian.Shaders` | 104 | Main compiler/shader-language overlap area with Copeland. |
| `Aurelian.Core` | 53 | Engine/frame-loop and composition spine. |
| `Aurelian.Rendering.Contracts` | 44 | Neutral renderer DTO/contracts lane. |
| `Aurelian.Runtime` | 30 | Runtime/session/compositor-policy lane. |
| `Aurelian.World` | 26 | World document/store/unit model. |
| `Aurelian.Actuation` | 20 | World mutation request/actuation layer. |
| `Aurelian.Assets` | 13 | Shader asset manifest loading/artifact layer. |
| `Aurelian.Rendering.Null` | 9 | Headless null-render backend. |
| `Aurelian.AssetTool` | 3 | CLI over asset pipeline. |

## Imported docs layout

Imported Aurelian docs currently live under `docs/Aurelian/` with three visible buckets:

- `architecture/`: 10 files
- `audits/`: 76 files
- `claude/`: 1 file

The docs are detailed and milestone-oriented. They contain important prior thinking about SDSL-V, render contracts, compositor policy/mechanism splits, dependency policy, and Vulkan progression. Some older entries still describe a standalone Aurelian world and mention import-era paths such as `vendor/Dominatus`, but M13b now stabilizes the active repo topology around repo-local source plus NuGet dependencies.

## Projects/folders

| Project/folder | Purpose | Likely layer | Key dependencies | Easy build status read | Relationship to Copeland/Machina/Dominatus | Migration/integration risk | Recommended future home |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Aurelian.World` | World document, units, typed stores, renderable data | Domain core | none | likely self-contained | future source for render extraction; separate from Machina UI model | low | keep under `Aurelian.World` |
| `Aurelian.Actuation` | typed world mutation requests and actuators | domain mutation layer | `Aurelian.World` | likely self-contained | conceptually parallel to Dominatus actuation, but domain-specific | low | keep under `Aurelian.Actuation` |
| `Aurelian.Rendering.Contracts` | render snapshots, command plans, compiled shader/compositor DTOs | neutral render-contract layer | none | likely self-contained | strongest future bridge target for Machina-to-Aurelian rendering | low | keep under `Aurelian.Rendering.Contracts` |
| `Aurelian.Rendering.Null` | deterministic null renderer over command plans | headless backend | `Aurelian.Rendering.Contracts` | likely self-contained | useful proof backend for future bridge testing | low | keep under `Aurelian.Rendering.Null` |
| `Aurelian.Runtime` | runtime tick/session/compositor policy and world-to-render extraction | runtime orchestration layer | `Aurelian.World`, `Aurelian.Rendering.Contracts`, `Dominatus.Core` | topology stabilized in M13b via NuGet package usage | overlaps with Machina.Dominatus control-plane concepts | medium | keep under `Aurelian.Runtime` |
| `Aurelian.Core` | engine/frame loop and graphics/runtime composition spine | composition/core engine layer | `Aurelian.Runtime`, `Aurelian.Rendering.Contracts`, `Aurelian.Graphics` | depends on runtime and graphics topology stabilizing | potential eventual presenter-facing engine seam, but not yet | medium | keep under `Aurelian.Core` |
| `Aurelian.Graphics` | Vulkan-oriented graphics backend and plant/device/resource work | backend implementation layer | `Aurelian.Rendering.Contracts`, Silk.NET packages | package/version topology likely incomplete in current central props | future backend provider for Aurelian, not for direct Machina dependency | medium-high | keep under `Aurelian.Graphics`; future `Aurelian.Vulkan` split possible |
| `Aurelian.Shaders` | SDSL-V language, lexing, parsing, lowering, artifact generation, DXC bridge | shader compiler frontend/tooling layer | `Aurelian.Rendering.Contracts`, DXC package | package/version topology likely incomplete in current central props | strongest overlap with Copeland compiler infrastructure | high | temporary under `Aurelian.Shaders`; long-term candidate for `Copeland.Shaders` frontend split |
| `Aurelian.Assets` | shader asset manifests and artifact loading | asset/tooling layer | `Aurelian.Rendering.Contracts`, `Aurelian.Shaders`, Tomlyn | package/version topology likely incomplete in current central props | future bridge between shader compiler outputs and runtime/backend inputs | medium | keep under `Aurelian.Assets` |
| `Aurelian.AssetTool` | CLI around asset pipeline | tooling | `Aurelian.Assets`, `System.CommandLine` | package/version topology likely incomplete in current central props | analogous to Copeland CLI role but scoped to assets | medium | keep under `Aurelian.AssetTool` |

## Compiler overlap with Copeland

### What Aurelian's SDSL-V compiler surface looks like today

The imported compiler surface is concentrated in `src/Aurelian.Shaders`:

- `Lexing/`: token kinds, tokens, source spans, lexer
- `Parsing/`: parser entry points and helper scanners
- `Language/Ast/`: SDSL-V AST records
- `Lowering/`: shader lowering passes
- `Language/Artifacts/`: SDSL-V, HLSL, and SPIR-V artifact emission

The audit docs in `docs/Aurelian/docs/audits/0002-*` through `0009-*` also confirm that shader language work is a first-class Aurelian concern.

### Where lexer/parser/AST/IR/diagnostic pieces are

Present in Aurelian now:

- lexer: `Aurelian.Shaders/Lexing`
- parser: `Aurelian.Shaders/Parsing`
- AST: `Aurelian.Shaders/Language/Ast`
- diagnostics: parser/lowering/artifact diagnostic records
- lowering/artifact stage: `Aurelian.Shaders/Lowering` and `Language/Artifacts`

Present in Copeland now:

- compiler lexer/parser/AST/binder/MIR/backend: `src/Copeland.Script`
- Markdown lexer/parser/diagnostics/MIR lowering: `src/Copeland.Markdown`

### How it overlaps with Copeland

The overlap is architectural more than semantic in M13a:

- both systems own lexer/parser/token/diagnostic pipelines
- both systems own typed AST records
- both systems own lowering stages into more stable intermediate artifacts
- both systems expose deterministic artifact emission

The overlap is not that `Copeland.Markdown` is shader-related. The useful comparison is that Copeland already has recognizable compiler patterns and milestone doctrine that Aurelian.Shaders is independently re-solving.

### What should eventually move under Copeland

Likely eventual `Copeland.Shaders` candidates:

- reusable compiler staging conventions
- token/diagnostic/source-span conventions
- parser infrastructure patterns
- AST/lowering orchestration patterns
- CLI/compiler-host patterns where shared abstractions become worthwhile
- SDSL-V frontend/parser/validation infrastructure if the team chooses one compiler lane

### What should remain under Aurelian

Should remain Aurelian-owned even if compiler plumbing converges:

- render-facing shader semantic model
- compiled shader program contracts
- renderer/backend stage semantics
- asset-manifest interpretation for shader artifacts
- Vulkan/backend realization policy

### M13a recommendation

Document the target direction as:

```text
SDSL-V frontend and compiler mechanics
  -> eventual Copeland.Shaders candidate

Aurelian render/shader semantic contracts
  -> remain under Aurelian.Rendering.Contracts and Aurelian-owned render layers

Vulkan/backend realization
  -> remain under Aurelian.Graphics/Aurelian.Runtime or a future Aurelian.Vulkan split
```

M13a does not move code.

## Rendering overlap with Machina

### What render data model exists

The imported render model is already reasonably explicit:

- `RenderSnapshot` with cameras and render items
- `RenderCommandPlan` with passes, targets, pipelines, shaders, and draw items
- symbolic refs such as `RenderTargetRef`, `RenderShaderRef`, and `RenderPipelineRef`
- compositor-oriented DTOs under `Aurelian.Rendering.Contracts/Compositor`

### What null renderer exists

`Aurelian.Rendering.Null` provides a deterministic headless backend that consumes `RenderCommandPlan` and produces `NullRenderTrace` results rather than pixels or native GPU state. That is valuable for future bridge testing because it offers a non-Vulkan proof path.

### What assets/pipelines exist

Relevant pieces include:

- shader artifact manifests and loaders in `Aurelian.Assets`
- compiled shader DTOs in `Aurelian.Rendering.Contracts/Shaders`
- Vulkan graphics pipeline and render-pass creation work in `Aurelian.Graphics`

### What pieces could eventually back Machina rendering

Potentially reusable Aurelian-facing pieces:

- neutral render contracts and symbolic resource refs
- command-plan notion of passes/draw items/targets
- null renderer for non-native proof work
- future backend implementations once bridge contracts exist

Machina should not consume `Aurelian.Graphics` directly. The safe convergence seam is a bridge package that translates Machina's render intent into Aurelian's contracts.

### What bridge would be needed

Preferred future direction:

```text
Machina document/render intent
  -> Machina.Aurelian bridge
      -> Aurelian rendering contracts/model
          -> Aurelian backend/null/Vulkan
```

The existing Machina raster pipeline is useful evidence here. `Machina.Dominatus.Rendering.Bridge.MachinaRenderBridge` already lowers UI/layout state into typed render-ish commands for a backend-specific dispatcher. That pattern is conceptually aligned with a future `Machina.Aurelian` adapter, even though the current commands are raster/Dominatus-oriented rather than Aurelian contracts.

## Orchestration overlap with Dominatus

### Where Aurelian uses orchestration/state/effects

Current evidence:

- `Aurelian.Runtime` references a Dominatus project path
- `DominatusSmokeRuntime` hosts a tiny `ActuatorHost`/`AiWorld`/HFSM flow
- runtime/compositor session types suggest policy-and-dispatch orchestration rather than pure rendering logic

### How that compares to Machina's current Dominatus-style state/effect routing

Machina already has two related patterns:

- `Machina.Dominatus` uses actual `Dominatus.Core` and `Dominatus.OptFlow` packages for rendering/actuation experiments
- `Machina.Runtime.Dispatch` carries a lighter action-dispatch table pattern for UI state changes without requiring all consumers to host Dominatus directly

This means Machina has both a direct Dominatus seam and a subsystem-local dispatch seam. Aurelian should likely follow the same philosophy: keep domain state local, use orchestration where it adds value, and avoid coupling raw backend handles to control-plane state.

### Is direct Dominatus usage appropriate

Direct Dominatus usage is appropriate for policy/orchestration lanes, but not as a hard dependency for every domain layer.

Recommended boundary:

- domain packages own domain state and effect requests
- orchestration hosts translate those requests into typed control-plane actions where useful
- backend/mechanism packages remain free of Dominatus references

### Eventual boundary doctrine

Preferred doctrine:

```text
Dominatus:
  lifecycle, orchestration, effect routing

Subsystems:
  own domain state and effect requests

Bridges:
  translate subsystem-specific actions/effects into Dominatus-oriented orchestration when useful
```

M13a should not force Aurelian to depend on `reference/dominatus`, and it should not force Machina-style direct package usage everywhere.

## Build/test status

Initial topology findings before M13b validation:

- `Copeland.slnx` remains the current fast/core solution for Copeland/Machina work.
- `Copeland.Slow.slnx` remains the current slow/proof solution.
- `Aurelian.slnx` is present and should remain separate for now.
- `Aurelian.slnx` referenced paths that did not exist in this repository snapshot:
  - `samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj`
  - `vendor/Dominatus/src/...`
- `src/Aurelian.Runtime/Aurelian.Runtime.csproj` referenced `../../vendor/Dominatus/src/Dominatus.Core/Dominatus.Core.csproj`, but the repo uses `reference/dominatus` for inspection only and no active `vendor/` tree.
- imported package references for `System.CommandLine`, `Microsoft.Direct3D.DXC`, and several Silk.NET packages were not centrally versioned in the current root `Directory.Packages.props`.

M13b resolves those topology issues:

- `Aurelian.slnx` stays separate from `Copeland.slnx` and `Copeland.Slow.slnx`.
- stale `vendor/Dominatus` and missing sample solution entries are removed from `Aurelian.slnx`.
- `Aurelian.Runtime` now uses a NuGet `PackageReference` to `Dominatus.Core`.
- `reference/dominatus` remains reference-only and is not project-referenced by Aurelian.
- central package management now covers the imported Aurelian package set needed for restore/build.
- `dotnet restore Aurelian.slnx` and `dotnet build Aurelian.slnx --no-restore` pass after stabilization.
- remaining Aurelian test failures are non-topology issues and are recorded in [aurelian-build-topology-m13b.md](/C:/Users/yuech/source/repos/Copeland/docs/Aurelian/aurelian-build-topology-m13b.md).

Validation command results are recorded after the repository docs changes in this milestone's closeout report.

## Immediate risks

- stale pathing from standalone Aurelian import (`vendor/Dominatus`, missing sample project)
- package-version gaps under current central package management
- docs that still describe standalone Aurelian assumptions rather than current monorepo topology
- temptation to integrate SDSL-V, presenter, and Vulkan paths before solution/build boundaries stabilize

## Recommended migration phases

- `M13a`: audit and organization.
- `M13b`: solution/build topology stabilization for imported Aurelian.
- `M13c`: completed as shader test normalization plus curated Aurelian docs dogfood through the existing Copeland Markdown/Oblivion path, while keeping SDSL-V migration, Machina bridging, and Vulkan presenter integration deferred.
- `M13d`: SDSL-V compiler audit against Copeland compiler patterns.
- `M13e`: define `Copeland.Shaders` target architecture and migration doctrine.
- `M13f`: Aurelian render-model boundary and null-renderer proof strategy.
- `M13g`: `Machina.Aurelian` bridge design.
- `M14+`: triangle proof, Vulkan proof, and presenter integration after the boundaries are stable.

## What not to integrate yet

Do not perform these in M13a:

- merge `Aurelian.slnx` into `Copeland.slnx`
- rename the repository
- move SDSL-V code into Copeland
- wire Machina presenter flows to Aurelian runtime or graphics
- add Vulkan runtime integration to Machina
- change Copeland Markdown behavior
- resume Roslyn/xUnit notebook execution work

M13c keeps those non-goals intact. It only normalizes the remaining shader test line-ending assertion and dogfoods selected `docs/Aurelian/...` files as generated Oblivion Markdown cards.
