# A15 Dependency Policy Audit

## 1. Files changed

- `docs/architecture/dependency-policy.md`
- `docs/architecture/aurelian-charter.md`
- `docs/architecture/mvp-roadmap.md`
- `README.md`
- `docs/audits/0015-a15-dependency-policy.md`

## 2. Task scope

A15 is a docs-only architecture milestone. It documents Aurelian's dependency and library adoption doctrine before the engine starts adding physics, navigation, rendering, windowing, asset, or backend integrations.

A15 does not modify production source, add packages, add project references, add tests, change solution files, modify `vendor/Dominatus`, modify `CodeReferences`, or implement physics, navigation, rendering, or windowing.

## 3. Dependency policy summary

The dependency policy establishes this core rule:

```text
Aurelian owns the architecture spine. Libraries provide bounded implementation plumbing.
```

Aurelian should use useful .NET/native libraries when they solve correctness-heavy, low-ROI, high-cost problems, but only when they can remain behind Aurelian-owned contracts. Aurelian owns the world model, runtime, Dominatus orchestration, actuators, render snapshots/command plans, asset manifests, SDSL-V compiler artifacts, and locality doctrine.

## 4. Library category decisions

- Silk.NET / Vortice are acceptable for native graphics, window, and platform binding plumbing behind rendering/HAL contracts.
- BEPUphysics is acceptable for physics simulation behind Aurelian-owned physics store, actuator, event, and snapshot contracts.
- DotRecast is acceptable for navmesh/pathfinding behind Aurelian-owned navigation request/result and world integration contracts.
- Tomlyn is acceptable for TOML parsing while Aurelian owns manifest schema, validation, diagnostics, and artifact models.
- System.Text.Json is acceptable for deterministic JSON artifacts/manifests through explicit DTOs rather than reflection-first runtime object graphs.
- DXC is acceptable as an optional external shader validation/compiler tool and must not be required for normal tests.
- Avalonia / Machina may be useful later for tools/UI, but must remain outside core runtime packages.

## 5. Reflection/NativeAOT policy

The policy states:

```text
Aurelian core is reflection-free by default.
```

Core behavior should avoid runtime reflection, assembly scanning, `Activator.CreateInstance` dependencies, reflective property paths, and reflection-defined runtime-core serialization. Aurelian should prefer source generation, manifests, typed registries, and explicit request/result contracts.

Reflection may be allowed in tooling, tests, source generators, and build-time asset tools when the result becomes explicit generated/static metadata or artifacts. The guiding rule is:

```text
If the compiler cannot see the dependency, Aurelian should distrust it.
```

## 6. Layering rules

- `Aurelian.Core` stays minimal and dependency-light.
- `Aurelian.World` must not depend on rendering, assets, shaders, Dominatus, physics, navigation, or UI.
- `Aurelian.Actuation` may depend on world contracts for world mutation.
- `Aurelian.Runtime` integrates Dominatus and dispatch.
- Backend packages may depend on external libraries.
- Tools/editor packages may depend on UI/windowing libraries.
- Dependencies must not create reverse references into core layers.

## 7. Anti-goals

- No NIH rewrite of commodity infrastructure.
- No dependency capture.
- No reflection-first core.
- No editor/tool dependency in runtime.
- No adopting a full engine architecture just for rendering.
- No hidden global state from dependencies crossing into Aurelian's core model.

## 8. Next recommendation

```text
A16 — World typed data stores M0
```

A16 should:

- add minimal typed stores for world data;
- keep mutation through actuators;
- keep `Aurelian.World` library-free;
- avoid behavior/runtime/render/assets integration.

## 9. Validation results

Commands run:

```bash
test -f docs/architecture/dependency-policy.md
test -f docs/audits/0015-a15-dependency-policy.md
git status --short
```

Results:

- Dependency policy document exists.
- A15 audit report exists.
- Git status shows docs-only changes.
- No build or test command was required for this docs-only milestone.
