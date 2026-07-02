# Visionary Monorepo Architecture M13a

## Purpose

M13a records the repository-wide naming, ownership, and dependency doctrine after importing Aurelian source and docs into the existing Copeland/Machina/Oblivion repository. This milestone is audit and organization only. It does not rename the repository, merge compiler stacks, wire Machina to Aurelian, or change runtime behavior.

## Why Aurelian is being monorepoed

Aurelian overlaps with existing work in three important ways:

- Aurelian's SDSL-V shader compiler work duplicates frontend/compiler concerns that already exist in Copeland.
- Aurelian needs a presenter and long-term visual host, while Machina already has a document/layout/presenter shell.
- Both Aurelian and Machina benefit from explicit orchestration and effect-routing seams that align with Dominatus-style control-plane work.

Keeping the systems separate would continue the current duplication of compiler plumbing, render abstractions, presenter infrastructure, and orchestration seams. Monorepoing them creates a place for shared contracts and future bridge packages without forcing premature integration.

## Naming model

`Visionary` is the proposed umbrella product and monorepo name. It is architectural language only in M13a. The repository has not been renamed yet.

Subsystem names remain:

- `Copeland`: compiler workshop infrastructure.
- `Machina`: UI/presenter/workbench shell.
- `Oblivion`: notebook/card/workbench layer hosted inside Machina.
- `Aurelian`: rendering infrastructure, render contracts, shader work, and Vulkan-oriented backend path.
- `Dominatus`: orchestration, lifecycle, and effect-routing infrastructure.
- `Leviathan`: future web/auth/payment/social/networked application layer.

## Subsystem responsibilities

The monorepo should converge on explicit subsystem lanes rather than a blended identity.

## Copeland

Copeland remains the compiler workshop lane:

- shared compiler primitives and conventions
- lexer/parser and lowering patterns
- diagnostics and compilation staging
- explicit frontends
- domain-specific HIR/MIR lanes when earned
- targeted backends and compiler CLI surfaces

M13d refines this further: Copeland should host multiple explicit compiler lanes without mandating one universal IR and without naming the whole architecture after shaders.

## Machina

Machina remains the UI and presenter lane:

- UI authoring and document model
- layout and text measurement
- presenter/workbench shell
- sample application hosts
- backend-neutral render intent at the UI layer

Machina should not depend directly on Vulkan or Aurelian graphics backends.

## Oblivion

Oblivion remains the notebook/card/workbench lane hosted by Machina:

- page/card model
- card inspector and persistence
- Markdown-backed note/document surfaces
- future applet-style card ownership boundaries

Oblivion is not the place for direct renderer ownership or shader backend policy.

## Aurelian

Aurelian remains the rendering and shader lane:

- renderer-neutral render contracts and command planning
- world/render snapshot extraction
- asset and shader artifact contracts
- SDSL-V language, parsing, lowering, and artifact generation for now
- backend realization work, including the Vulkan-oriented path

M13a keeps Aurelian as an imported subsystem rather than integrating it into Copeland or Machina.

## Dominatus

Dominatus remains the orchestration lane:

- lifecycle coordination
- effect routing
- typed actuation/command dispatch
- policy/control-plane infrastructure

It should remain orchestration infrastructure, not a dumping ground for domain models from Machina, Aurelian, or Copeland.

## AI reviewer ownership model

The intended monorepo workflow is subsystem-specialized reviewer ownership rather than one reviewer reasoning about every layer at once.

Recommended reviewer lanes:

- `Copeland reviewer`: compiler frontends, AST/MIR, diagnostics, lowering, CLI compiler flows.
- `Machina reviewer`: UI authoring, layout, text, presenter shell, workbench behavior.
- `Oblivion reviewer`: notebook/card model, persistence, Markdown dogfood, workbench interaction.
- `Aurelian reviewer`: render contracts, shader pipeline, asset flow, graphics/Vulkan topology.
- `Dominatus reviewer`: orchestration, actuation, effect-routing seams, cross-subsystem control-plane boundaries.
- `Architecture reviewer`: monorepo topology, dependency direction, bridge-package design, solution shape.

This matches the user's intended "multiple specialist ChatGPT reviewers" model and is a primary reason to preserve crisp subsystem boundaries instead of integrating early.

## Dependency direction

Allowed future directions:

- `Copeland` compiler abstractions may be used by future Aurelian compiler frontends/backends where the fit is clean.
- `Aurelian` may depend on Copeland compiler abstractions if shader frontend work is converged intentionally.
- `Machina.Aurelian` bridge packages may translate Machina render intent into Aurelian rendering contracts.
- presenter samples may reference bridge packages for demonstrations.
- `Dominatus` may coordinate lifecycle and effect routing for subsystem-owned state machines.

Explicitly avoid:

- `Machina.Core -> Aurelian.Graphics`
- `Machina.Core -> Aurelian.Runtime`
- `Machina.Core -> Vulkan/native backend packages`
- `Copeland.Core -> Aurelian runtime`
- `Aurelian.Core -> Machina.Presenter.Sample`
- production packages depending on sample projects

## What changed

M13a changes the repository understanding, not the runtime:

- Aurelian source projects are now present under `src/Aurelian.*`.
- Aurelian docs are now present under `docs/Aurelian/...`.
- `Aurelian.slnx` exists at the repo root as an imported, initially separate solution.
- monorepo architecture, ownership, and migration doctrine are now documented.

M13b then stabilizes the imported Aurelian build lane without integrating runtime behavior:

- `Aurelian.slnx` remains separate from `Copeland.slnx` and `Copeland.Slow.slnx`.
- Aurelian now follows the active Dominatus dependency doctrine already used by Machina: NuGet packages for builds, `reference/dominatus` for inspection only.
- stale `vendor/Dominatus` and missing sample solution references are removed from the imported Aurelian topology.
- central package management now covers the imported Aurelian package set needed for restore/build.

M13c then proves two narrow follow-through points without changing subsystem boundaries:

- the remaining Aurelian shader test issue is fixed by assertion-boundary line-ending normalization only
- selected Aurelian docs now dogfood through the existing Copeland Markdown and Oblivion docs-card path

M13d then defines Copeland as the compiler workshop for Visionary:

- no universal IR mandate
- no `Copeland.Shaders` monolith as the architectural umbrella
- explicit document, script, shader, kernel, numeric, and future domain lanes
- shared abstractions promoted only after repeated concrete use

M13e then audits the active Aurelian SDSL-V lane and records the hidden GPU-MIR-shaped pressure already present in HLSL emission, stage extraction, SPIR-V artifact emission, DXC tooling, and older lowering helpers.

M13f then names the future common GPU-oriented MIR target `VD-MIR` (`Visual Direct MIR`):

- `VD-MIR` is a future common GPU-oriented MIR candidate, not a universal Copeland IR
- SDSL-V should eventually lower into `VD-MIR` before HLSL/DXC
- HLSL/DXC, Slang, and PTX are backend routes from `VD-MIR`, not semantic centers
- one common MIR remains the starting assumption until evidence proves a shader/kernel split is necessary
- `samples/Aurelian.VisibleTriangle` exists as a future proof target but is not wired to `VD-MIR` in M13f

## What did not change

M13a intentionally does not:

- rename the repository
- merge `Aurelian.slnx` into `Copeland.slnx`
- move SDSL-V into Copeland
- wire Machina's presenter to Aurelian runtime or renderer
- implement Vulkan presenter integration
- change Copeland Markdown behavior
- resume Roslyn/xUnit notebook execution work

## Deferred work

Recommended near-term phases:

- `M13a`: audit and organization.
- `M13b`: Aurelian solution/build topology stabilization. Completed as a separate-solution cleanup and NuGet dependency retargeting pass; no runtime integration was introduced.
- `M13c`: completed as shader test normalization plus curated Aurelian docs dogfood through existing Copeland Markdown/Oblivion paths where safe.
- `M13d`: Copeland compiler workshop architecture. Completed as doctrine, lane taxonomy, roadmap, and manifest work only.
- `M13e`: Aurelian SDSL-V lane audit and GPU MIR target analysis. Completed as recon-only audit work.
- `M13f`: VD-MIR architecture doctrine. Completed as doctrine only; no implementation or migration was added.
- `M13g`: Aurelian.VisibleTriangle sample topology and proof-boundary audit. Completed as audit/topology work only; the sample remains a future proof target and is not wired to `VD-MIR` yet.
- `M13h`: design `Machina.Aurelian` bridge contracts.
- `M14d`: visible triangle routed through `PresenterScreenStack` as a semantic `world` screen while preserving the existing Aurelian runtime/render path.
- `M14e`: Aurelian migration closeout and subsystem handoff; primary active focus returns to Machina and Oblivion while future Aurelian and `VD-MIR` continuation moves to separate reviewer lanes.
- `M14+`: later triangle proof expansion, Vulkan proof, and presenter integration after boundaries stabilize.
