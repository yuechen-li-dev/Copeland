# Marionette and Aurelian ownership

Aurelian owns renderer-generic engine, world, actuation, asset, shader, and rendering infrastructure. Marionette owns Skyrim domain identity, imported-NPC semantics, lifecycle, checkpoint correlation, transport, and application composition.

## Physical and dependency topology

```text
src/Aurelian/Aurelian.Actuation
        ↑
src/Marionette/Marionette.Core
        ↑
src/Skyrim/Aurelian.Marionette
```

`Marionette.Core` contains the former Skyrim world contracts and legacy imported-agent registry under `Marionette.Skyrim`. `Skyrim/Aurelian.Marionette` is the executable/composition root under `Marionette.Skyrim.App`; it owns the wire protocol, live lifecycle coordinator, Dominatus scenarios, candidate selection, and checkpoint correlation. Tests moved to matching `tests/Marionette` and `tests/Skyrim` roots.

`Aurelian.slnx` no longer includes the Skyrim app or its tests. `Marionette.slnx` is the bounded ownership validation lane. A topology test scans Aurelian C# and project sources and rejects Skyrim or Marionette ownership moving back into the engine.

No compatibility namespaces or aliases were retained. Historical milestone documents may mention the old `Aurelian.Marionette.Transport` path as historical evidence; the operational bootstrap document uses the current app path.

## Renderer boundary and Godot recon

Marionette currently has no renderer-facing semantic projection: the application is a transport/runtime composition root, and its domain state depends only on generic Aurelian actuation contracts. Introducing a renderer interface before a concrete view-state consumer would be speculative surface area, so this pass does not add one.

A temporary Godot proof is not justified in this bounded hygiene pass. The repository has no Godot project, package dependency, scene, or adapter contract to reuse, and adding those would be new product and asset-pipeline work. If visual pressure arrives before SDSL-V is ready, the smallest permitted path is:

```text
Marionette-owned immutable view projection
        ↓
renderer adapter
        ├─ Godot host (temporary)
        └─ Aurelian/SDSL-V host (future)
```

Godot nodes, scenes, scripts, and resource IDs must remain adapter state. They must not become authoritative actor, world, save, lifecycle, or identity state. The same projection boundary must allow the future Aurelian renderer to replace a temporary host without rewriting Marionette semantics.
