# Aurelian Architecture Charter

Aurelian is a greenfield C# engine/runtime created after the Stri-V rescue effort was paused. Stri-V remains valuable research, but Aurelian starts from a clean runtime foundation rather than treating Stride as the core engine base.

## Core principles

- Aurelian is a greenfield C# engine/runtime.
- Aurelian adopts a Dominatus-native behavior/runtime spine beginning in A1, with Dominatus vendored as buildable source under `vendor/Dominatus/`.
- The world model is explicit data first: engine state should be visible, testable, serializable where appropriate, and not hidden behind editor-first object graphs.
- Lifecycle flow should use typed lifecycle events rather than stringly or implicit processor callbacks.
- Side effects belong to actuators. Actuators own interactions with external systems and make effects intentional at architectural boundaries.
- Rendering should flow through render snapshots and command plans so runtime state and render submission remain separated.
- Asset work should move toward TOML/manifest-based assets.
- Shader work should move toward an SDSL-V-style compiler pipeline when that phase begins.
- Renderer/HAL selection is deferred until later MVP phases.
- Dependency adoption follows `docs/architecture/dependency-policy.md`: Aurelian uses useful libraries pragmatically behind Aurelian-owned contracts while keeping the core explicit, NativeAOT-oriented, and reflection-free by default.

## Non-goals

- No Stride processor architecture as the runtime core.
- No Stride asset system as the Aurelian asset foundation.
- No editor-first strategy.
- No renderer implementation in A1.
- No window creation or triangle rendering in A1.
- No Machina, WyrmCoil, Stri-V salvage, graphics package, or windowing package linkage in A1.
- No dependency capture: external libraries must not replace Aurelian-owned world, runtime, actuation, render snapshot, asset, or shader contracts.

## Current spine status

A1 establishes only a minimal Dominatus runtime smoke path. The smoke constructs a tiny Dominatus world, graph, agent, and actuation host, ticks once deterministically, and observes an immediate Dominatus actuation completion payload. It is not the final runtime API and does not add renderer, asset, shader, windowing, or world-store architecture. A15 adds the dependency policy before later implementation proceeds to world stores, actuation, render contracts, and backend integration under explicit library boundaries.
