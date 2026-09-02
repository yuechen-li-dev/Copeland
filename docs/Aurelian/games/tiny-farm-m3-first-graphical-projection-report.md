# TinyFarm M3 First Graphical Projection Report

## 1. Outcome

**Outcome A — Graphical TinyFarm is a clean projection.** The real MonoGame window launches and presents the M2 world, while the canonical headless M1 and M2 hashes remain exact. The graphical and LLM controllers submit existing intents to `TinyFarmSession.Step`; neither owns a mutation path.

## 2–5. Backend, ownership, and dependencies

The implementation choice is **`MONOGAME_TEMPORARY_PROJECTION`**. Current Aurelian rendering is still a Vulkan/visible-triangle and low-level mechanism path rather than a comfortable 2D tile/text host. Completing texture, sprite, and text presentation there would make M3 a renderer milestone. The repository already has proven MonoGame samples, so the smallest credible application leaf is `src/TinyFarm/TinyFarm.MonoGame`.

```text
TinyFarm.Core
      ^
TinyFarm.Runtime
      ^
TinyFarm.MonoGame -> MonoGame.Framework.DesktopGL
```

Core contains no package or project dependencies. Runtime contains no MonoGame/XNA types or packages. The graphical executable references Runtime (and receives Core transitively). MonoGame is centrally versioned and contained in the leaf.

## 6–10. Authority, projection, and controller paths

`TinyFarmState` remains the sole owner of actors, locations, inventory, money, time, NPC state, plots, crops, stock, facts, and relationships. `TinyFarmFrameProjector` derives an immutable renderer-neutral `TinyFarmFrame` containing stable IDs, fixed presentation coordinates, actor/item/plot views, HUD values, interaction hints, and projected narrative. It has no mutation API.

Keyboard input is edge-detected in the leaf, mapped by `TinyFarmHumanController` to the existing closed intent family, and submitted through `TinyFarmSession.Step`. Dominatus continues to observe and submit NPC envelopes in that same step. The existing resolver orders and commits both sources. The runner/replay route remains unchanged.

The executable also exposes `--llm-control`. It accepts one existing semantic command per stdin line and emits one deterministic JSON response containing intent results, the canonical state hash, projection hash, and frame. `save`, `load`, `inspect`, and `quit` are shell controls; gameplay commands still pass through `TinyFarmCommandParser` and `TinyFarmSession.Step`. This lets an LLM play without keyboard/mouse emulation and does not require a live network model.

## 11–19. Playable projection

Render cadence does not advance authoritative time. Only existing actions, especially `WaitIntent`, change game time. Location-level movement remains location-level: the leaf spatially arranges Farmhouse, Town Square, General Store, and Riverside and draws semantic transitions; it does not introduce continuous position or physics truth.

The window renders:

- the player and each stable NPC identity at their authoritative location;
- the four current locations and their exit graph;
- both farm plots, with empty soil, watered soil, growth height, and gold harvest-ready state;
- current ground items and store/farm affordances;
- day, time, location, money, inventory, interaction hints, resolver rejection/status, and Ariadne prose;
- a bounded camera offset following the player's projected location.

The shop controls use existing buy/sell intents. Inventory is read from authoritative item ownership and product stacks. Dialogue displays existing `NarrativeLine` prose and Enter closes the presentation-only message. F5/F9 call the existing M2 chunked save codec; load replaces the session, after which the next frame is freshly derived. No graphical persistence exists.

## 20–24. Compatibility and audits

Headless runner, REPL, tests, M1 scenario, M2 seven-day scenario, persistence, and inspection JSON do not reference or load the graphical assembly. Existing replay semantics are compatible with visual projection because any replayed state can be passed to `TinyFarmFrameProjector`; an interactive replay timeline was not added because it was not required for the boundary proof.

Mutation search found no assignments to gameplay truth in `TinyFarm.MonoGame`. The leaf owns only graphics resources, prior keyboard state, camera calculation, status/narrative display, save-file path, and the replaceable session reference used after load. Money, inventory, crop growth, actor locations, NPC actions, and time are only read from the session frame.

Renderer leakage search found `MonoGame`, `Microsoft.Xna.Framework`, and graphics types only in `TinyFarm.MonoGame` plus central package version/solution/CI declarations. Core and Runtime contain zero renderer references. Replacing the backend requires replacing the leaf renderer/input adapter; game semantics, frame projector, resolver, persistence, NPC logic, replay, REPL, and LLM control semantics remain unchanged.

## 25–28. Canonical evidence

| Evidence | Result |
| --- | --- |
| M1 exact state hash | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| M2 exact state hash | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |
| M3 final state hash | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |
| M3 intent-result hash | `bdd11630a99983706abaacf5f4f5a24a6b981d780f5001b81496ef63a71e74ca` |
| M3 event-sequence hash | `c7bb0c793d58e375d09a8636239def942b3d2afc2d0b641cdcf73bbc4a3ca6aa` |
| M3 presentation hash | `74d98f63ec0e57eb508065038b5d58592fbdd5abefc19d66f8a4ede7553ff4a4` |

The M3 human-equivalent scenario runs the complete M2 acquisition, plant, water, multi-day advancement, autonomous NPC movement, crop transition, harvest, and sale sequence. At a mid-run boundary it saves, performs a visible time mutation, reloads, and proves the restored projection hash equals the pre-save projection and differs from the discarded mutation. Reprojecting a deep copy produces the same semantic projection hash. Pixels are deliberately outside the deterministic contract.

## 29–32. Tests, validation, artifacts, and operation

Nine M3 tests cover frame parity, crop/product projection, projection determinism, human intent mapping, resolver-only mutation, projected NPC movement, save/load reprojection, the full M3 scenario, and dependency topology. CI builds the graphical project without opening a device and runs the semantic projection proof alongside the exact headless proof.

Launch the game:

```powershell
dotnet run --project src/TinyFarm/TinyFarm.MonoGame/TinyFarm.MonoGame.csproj
```

Run the LLM controller:

```powershell
dotnet run --project src/TinyFarm/TinyFarm.MonoGame/TinyFarm.MonoGame.csproj -- --llm-control
```

Example stdin commands are `inspect`, `move general-store`, `wait 60`, `buy-product turnip-seed`, `save`, and `load`. Each response is one JSON line.

Compact retained evidence is limited to `artifacts/tiny-farm-m3/proof.json`, `projection.json`, and `manifest.json`. No screenshots, video, caches, compiled output, or LLM smoke save are retained.

The release window became targetable within the two-second visual-validation polling interval, uses vertical synchronization, and showed no visible input/render latency. M3 therefore adds no optimization or allocation work.

## 33–34. Diff and recommended M4

The final `git diff --stat` is recorded in the task report alongside validation results.

The exact recommended next milestone is **M4: Machina.UI game UI integration**. The observed M3 pressure is the deliberately leaf-local bitmap HUD/dialogue/shop text overlay. The semantic world projection is already adequate and Aurelian-native 2D would still require broader renderer work; replacing the temporary overlay with Machina composition is now the smallest concrete seam with a real consumer.
