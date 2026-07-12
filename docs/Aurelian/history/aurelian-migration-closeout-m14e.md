# Aurelian Migration Closeout M14e

## Purpose

M14e closes out the current Aurelian migration arc as a documentation, handoff, and artifact milestone only.

It records what is now golden-pathed enough to treat as stable reviewer-facing topology, what remains deferred, and where primary work should resume next.

M14e does not add new renderer behavior, does not change the visible triangle runtime path, does not migrate SDSL-V into Copeland, and does not activate a new `VD-MIR`-driven sample route.

## Migration arc

The Aurelian migration arc covered:

- `M13a`: monorepo import audit
- `M13b`: solution/build topology stabilization
- `M13c`: test normalization and docs dogfood
- `M13d`: Copeland compiler-workshop doctrine
- `M13e`: SDSL-V lane audit and GPU MIR target analysis
- `M13f`: `VD-MIR` architecture doctrine
- `M13g`: visible triangle topology and proof-boundary audit
- `M14d`: visible triangle world-screen route through `PresenterScreenStack`

M14e closes this arc for now because the Aurelian lane has reached a real local runtime path, a documented solution/sample/test topology, and a clear handoff boundary for future specialized reviewers.

## Golden path summary

The current golden path is:

```text
Aurelian source/tests/docs are in the monorepo.

Aurelian.slnx:
  restores
  builds
  tests

Aurelian uses Dominatus via NuGet:
  no vendor/reference project refs

Aurelian docs:
  selected docs dogfood through Oblivion Docs

SDSL-V lane:
  audited
  current path documented
  VD-MIR pressure documented
  no migration into Copeland has been adopted

VD-MIR:
  architecture doctrine exists
  no Copeland-hosted implementation or active visible-triangle route exists

Aurelian.VisibleTriangle:
  present under samples
  in Aurelian.slnx
  builds
  routes through PresenterScreenStack as semantic world screen
  local present path has passed
  visual pixel confirmation not claimed
```

Historical note:

- exploratory `src/Aurelian/Aurelian.Shaders/Language/VdMir` code and `artifacts/m14a` remain in-tree from earlier compiler-only work
- M14e does not extend, activate, or treat that slice as the current golden path
- the visible-triangle runtime still does not route through `VD-MIR`

Current non-golden and deferred items:

```text
VD-MIR M0 is not adopted as the current migration continuation lane
visible triangle is not proven through VD-MIR
PTX is not implemented
Slang is not implemented
SDSL-V is not moved into Copeland
no Machina/Aurelian general bridge exists
no headless deterministic visible triangle proof gate exists
repo is not renamed
```

## Current solution topology

Current Aurelian solution topology:

```text
Aurelian.slnx
  /samples/
    Aurelian.VisibleTriangle
  /src/
    Aurelian.Actuation
    Aurelian.Assets
    Aurelian.AssetTool
    Aurelian.Core
    Aurelian.Graphics
    Aurelian.Rendering.Contracts
    Aurelian.Rendering.Null
    Aurelian.Runtime
    Aurelian.Shaders
    Aurelian.World
  /tests/
    Aurelian.Actuation.Tests
    Aurelian.Assets.Tests
    Aurelian.AssetTool.Tests
    Aurelian.Core.Tests
    Aurelian.Graphics.Tests
    Aurelian.Integration.Tests
    Aurelian.Rendering.Contracts.Tests
    Aurelian.Rendering.Null.Tests
    Aurelian.Runtime.Tests
    Aurelian.Shaders.Tests
    Aurelian.VisibleTriangle.Tests
    Aurelian.World.Tests
```

Related Copeland topology facts:

- `Copeland.slnx` and `Machina.UI.Slow.slnx` still exclude Aurelian projects
- Aurelian remains its own solution lane
- Dominatus is consumed through package references rather than vendored/reference project links

## Current sample topology

Current visible sample topology:

```text
samples/Aurelian/Aurelian.VisibleTriangle
  Program.cs
  VisibleTrianglePresenterScreenStack.cs
  VisibleTriangleWorldScreen.cs
  VisibleTriangleSampleFrame.cs
  VisibleTriangleFrameInputProvider.cs
  SilkNetPresenterBackend.cs
  SilkNetFrameInputProvider.cs
  Assets/
    assets.toml
    Shaders/SmokeTriangle/
      shader.toml
      generated.hlsl
      VSMain.spv.hex
      PSMain.spv.hex
```

The sample remains:

- Aurelian-owned
- artifact-driven at runtime
- Vulkan/window/present capable in a local graphics environment
- routed through `PresenterScreenStack` on the semantic `world` layer

## Current docs dogfood status

Selected `docs/Aurelian/...` content is still dogfooded through the existing `Copeland.Markdown -> Oblivion -> Docs` path.

This means:

- Aurelian docs are present in the monorepo as first-class source
- Oblivion docs dogfood continues to include curated Aurelian material
- M14e adds closeout/handoff docs without changing the dogfood runtime path

## Current shader/compiler status

The active visible sample shader/runtime path remains:

```text
historical SDSL-V source
  -> HLSL emission
  -> DXC
  -> checked-in SPIR-V artifacts
  -> Aurelian asset manifests
  -> CompiledShaderProgram
  -> Aurelian.Graphics Vulkan pipeline
  -> visible triangle sample
```

Current compiler ownership facts:

- `Aurelian.Shaders` still hosts the active SDSL-V lane
- SDSL-V has not moved into Copeland
- no PTX backend exists
- no Slang backend exists
- no new compiler contract was introduced in M14e

## Current VD-MIR status

`VD-MIR` remains doctrine-first in the active migration story:

- the architecture doctrine from M13f stands
- the visible triangle proof boundary from M13g stands
- the visible triangle runtime is not wired through `VD-MIR`
- no `Copeland.Mir.Vd`, `Copeland.Mir.VdMir`, `Copeland.Backends.Ptx`, `Copeland.Backends.Slang`, or `Copeland.Frontends.Sdslv` package was created

Historical nuance:

- there is still an exploratory `Aurelian.Shaders/Language/VdMir` slice in-tree
- M14e treats that slice as deferred follow-up material for future reviewers rather than as the adopted mainline outcome of the migration closeout

## Current visible triangle world-screen status

M14d established the current sample route:

```text
Silk.NET Presenter backend
  -> PresenterScreenStack
      -> VisibleTriangleWorldScreen(world)
  -> existing Aurelian frame-loop/compositor/present path
```

Current status:

- the familiar sample command remains
- `PresenterScreenStack` owns screen ordering and visibility selection
- `VisibleTriangleWorldScreen` registers as a semantic `world` screen
- the Aurelian frame-loop/compositor/present path is preserved
- no new general render contract was introduced
- local runs succeeded for `--frames 1 --no-hold` and `--frames 120`
- `visibleTriangleRenderedLocally` remains `false` in manifests because human pixel confirmation was not claimed

## What is now stable enough

Stable enough for handoff:

- Aurelian monorepo placement and subsystem naming
- Aurelian separate-solution topology
- Dominatus package consumption doctrine
- selected Aurelian docs dogfood through Oblivion Docs
- the visible triangle sample as a maintained proof surface
- the Presenter world-screen seam from M14d
- the documented ownership split between Aurelian runtime/render work and future Copeland compiler work

## What remains deferred

Deferred after M14e:

- active `VD-MIR` continuation work
- visible triangle proof through `VD-MIR`
- any SDSL-V migration into Copeland
- PTX backend work
- Slang backend work
- a general Machina/Aurelian bridge
- any new deterministic headless visible-triangle proof gate
- repo rename work

## Future Aurelian reviewer ownership

Future Aurelian reviewers should own:

- rendering infrastructure
- visible triangle sample maintenance
- runtime/render topology
- Vulkan/native path
- render contracts
- Aurelian sample surfaces
- any future renderer-behavior changes

## Future Copeland / VD-MIR reviewer ownership

Future Copeland / `VD-MIR` reviewers should own:

- compiler workshop follow-through
- SDSL-V audit follow-up
- `VD-MIR` `M0` / `M1` / `M2` / `M3`
- HLSL/DXC backend doctrine follow-through
- Slang/PTX backend planning
- any future decision about whether the exploratory Aurelian-hosted `VdMir` slice should be revised, extracted, replaced, or retired

Shared contracts and milestone docs remain the coordination mechanism across reviewer lanes.

## Future Machina / Oblivion ownership

Primary ownership after M14e returns to Machina and Oblivion:

- Presenter shell doctrine
- `PresenterScreenStack` reuse and ergonomics
- workbench and document UX
- Oblivion cards/docs/inspector/action/effect surfaces
- user-facing workbench milestones

Recommended next main-lane milestone:

```text
M15a:
  Machina/Oblivion workbench usability re-entry audit
```

Likely goals:

- inspect current presenter/workbench UX after the Aurelian detour
- stabilize `PresenterScreenStack` doctrine across samples
- revisit Oblivion cards/docs/inspector ergonomics
- choose the next user-facing workbench milestone
- avoid new compiler/rendering work in the main lane

## Validation summary

M14e validation focuses on:

- `Aurelian.slnx` restore/build/test
- `Copeland.slnx` and `Machina.UI.Slow.slnx` test/build confirmation
- boundary checks for forbidden Copeland package creation
- boundary checks for Copeland solution exclusion of Aurelian
- `git diff --check`

The exact command results for this closeout are recorded in the closeout manifest and M14e delivery notes.

## What changed

M14e changes:

- adds this closeout document
- adds a Visionary subsystem handoff document
- updates roadmap and milestone docs to record closeout/handoff status
- updates the root README and artifact index for the closeout checkpoint
- adds deterministic closeout manifest files under `artifacts/m14e`
- may add lightweight manifest/topology tests

## What did not change

M14e does not:

- implement new `VD-MIR` functionality
- change renderer behavior
- change visible triangle runtime behavior
- change `PresenterScreenStack` behavior
- add a new render contract
- move SDSL-V into Copeland
- add Aurelian projects to `Copeland.slnx` or `Machina.UI.Slow.slnx`
- build a general Machina/Aurelian bridge
- rename the repository
- add Machina/Oblivion feature work

## Deferred work

- future Aurelian reviewer: continue runtime/render/sample work only when a new rendering milestone is chosen
- future Copeland / `VD-MIR` reviewer: resume from the `M14a` / `M14b` plan space if `VD-MIR` work is reactivated
- future Machina / Oblivion reviewer: resume the main lane with `M15a` workbench usability re-entry audit
