# JTF-M0 topology and ownership doctrine

This is the authoritative current architecture document for the Joint Task Force monorepo topology. It defines physical ownership and dependency policy; historical milestone documents remain historical records even when their older terminology differs.

## Subsystem responsibilities

### Copeland

Copeland owns reusable compiler infrastructure, compiler conventions and primitives, diagnostics, source provenance, frontends, parsing, lowering, MIR, artifact infrastructure, explicit Script and Markdown compiler lanes, and compiler CLI surfaces.

Copeland production code must not depend on Machina.UI, Aurelian, Vulkan, renderer backends, game-runtime concepts, or Dominatus.

### Machina.UI

Machina.UI owns UI authoring and document models, UI elements and standard components, layout and text layout, fonts and typography, presenter and screen composition, pointer/keyboard/UI input routing, hit testing, and simple local UI state and dispatch.

Existing assemblies and namespaces may remain `Machina.*` during this milestone.

Machina.UI must not depend on Aurelian, Vulkan, concrete renderer backends, game-world concepts, or Dominatus in its general UI core. Existing Dominatus-backed and renderer-named projects are explicitly recorded exceptions below.

### Aurelian

Aurelian owns the game engine and lifecycle, world and game-object models, actuation, frame-loop/runtime coordination, renderer-neutral engine render contracts, renderer backends including CPU/null/Vulkan implementations, assets, shader-domain behavior, and Dominatus integration for engine runtime and game-object orchestration.

### Integration packages

The future `Aurelian.Machina` integration lane will translate Machina.UI-owned presentation/render intent into Aurelian-owned renderer contracts. The producer owns semantic output and the consumer owns the adapter. JTF-M0 creates no bridge production API.

## Allowed dependency directions

- Copeland production projects may depend only on Copeland production projects and ordinary external compiler/tooling packages.
- Machina.UI production projects may depend on Machina.UI production projects and ordinary UI/font packages, subject to the temporary exceptions below.
- Aurelian production projects may depend on Aurelian production projects and the external packages needed for its engine/runtime/backend boundaries.
- Explicit integration projects under `src/Integrations` may depend on the subsystem contracts they adapt.
- Tests and samples may compose their owning subsystem and are not production dependency edges. Samples are not valid dependencies of production projects.
- Cross-subsystem production references must be placed in an explicitly named integration project; no such reference exists in the current production graph.

## Prohibited dependencies

The repository validator rejects:

- Copeland production references to Machina.UI or Aurelian production projects;
- Machina.UI production references to Aurelian production projects;
- Copeland or Machina.UI production references to Dominatus packages unless a named exception is recorded;
- any production project reference to a sample project;
- any unrecorded cross-subsystem production reference outside `src/Integrations`.

Run `pwsh ./tools/Validate-DependencyBoundaries.ps1` from the repository root.

## Temporary exceptions

These are retained because JTF-M0 is a physical topology refactor and must not perform semantic project surgery:

- `src/Machina.UI/Machina.Dominatus`: `Dominatus.Core` and `Dominatus.OptFlow` package references remain for the existing Machina Dominatus runtime proof. Intended migration: JTF-M1 or a later explicitly approved Machina runtime boundary milestone.
- `src/Machina.UI/Machina.Renderer.Raster.Dominatus`: `Dominatus.Core` remains for the existing renderer/orchestration adapter. Intended migration: JTF-M1 or a later renderer/integration ownership milestone.

The exceptions are machine-readable in `tools/dependency-boundary-exceptions.json`. They are narrow project/package entries, not a general waiver.

## Known deferred semantic migrations

The following are intentionally placed by current project name and build identity, not by the eventual doctrine:

- `Machina.Pipeline` is not split in JTF-M0.
- `Machina.Dominatus` remains physically under Machina.UI even though Dominatus orchestration is not general UI ownership.
- `Machina.Renderer.Raster`, `Machina.Renderer.Raster.Text`, and `Machina.Renderer.Raster.Dominatus` remain under Machina.UI for now.
- `Aurelian.Core/Presentation/Screens` remains in Aurelian.Core.
- concrete Vulkan integration currently present in `Aurelian.Core` is not moved in this milestone.
- generic-looking machinery inside `Aurelian.Shaders` is not migrated into Copeland.
- SDSL-V and VD-MIR remain in their current Aurelian lane.
- the future `Aurelian.Machina` bridge is not implemented.

These are JTF-M1-and-later work items. No classes, assemblies, namespaces, public APIs, or dependency semantics were changed to resolve them here.

## Physical topology and reviewer write scopes

```text
src/Copeland       tests/Copeland       docs/Copeland
src/Machina.UI     tests/Machina.UI     docs/Machina.UI
src/Aurelian       tests/Aurelian       docs/Aurelian
src/Integrations   tests/Integrations  repository-wide architecture/decisions
```

- Copeland reviewer: `src/Copeland`, `tests/Copeland`, `docs/Copeland`.
- Machina.UI reviewer: `src/Machina.UI`, `tests/Machina.UI`, `docs/Machina.UI`.
- Aurelian reviewer: `src/Aurelian`, `tests/Aurelian`, `docs/Aurelian`.
- Architecture/orchestrator lane: `src/Integrations`, `tests/Integrations`, root solution/build files, and repository-wide architecture/decision documents.

Subsystem reviewers should not independently edit shared root files unless a task explicitly grants that ownership.
