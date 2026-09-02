# Aurelian Game Architecture Ground Report

## Decision

**Outcome B — the stack is mostly ready, but authoritative renderer-independent game state and deterministic intent resolution are a genuinely partial foundation.**

This is not a request for another engine. Dominatus already owns agent lifecycle, HFSMs, utility decisions, blackboards, typed mail, actuation, deterministic staging, checkpoints, and replay. TinyTown proves life-sim behavior patterns; FishTank and MonoGameRtsDemo prove a current rendered host; RTSBenchmark proves the missing simulation law. The game needs to compose those pieces without promoting sample types into shared infrastructure.

The first game should be an independent product/domain that depends on Dominatus and later projects into Aurelian. It should not live beneath `Aurelian.Runtime`, and Aurelian should remain a renderer dependency rather than the owner of farming semantics.

## Grounded composition

```text
human input script          Dominatus agents
        |                         |
        +------> typed GameIntent +
                          |
                 deterministic resolver
                          |
                 authoritative GameState
                    |             |
          GameSave/checkpoint     immutable presentation snapshot
                    |             |
             Dominatus save       temporary MonoGame adapter
                                  eventual Aurelian adapter

Machina.UI consumes a UI projection; Oblivion consumes read-only snapshots.
Neither owns or mutates GameState directly.
```

The repeated law is already visible in `reference/dominatus/samples/Dominatus.RTSBenchmark/Simulation/BattleSimulation.cs`: `SensorPhase`, `DecisionPhase`, `SortActions`, `ResolutionPhase`, and `EventPhase` separate observations from buffered `ShipAction` values and authoritative `ShipState`. Copy that pattern into the game. Do not extract the RTS domain types or add a general engine-wide intent framework in M1.

## State ownership

| State | Owner | Examples | Rule |
| --- | --- | --- | --- |
| World | `TinyFarm.Core` | tiles, positions, plots, crops, inventory, money, relationships, calendar, RNG state | sole gameplay truth; versioned and saved |
| Agent | Dominatus plus a game adapter | observations, current choice, HFSM path, decision memory, in-flight actuation | no physical or economic authority |
| Presentation | `TinyFarm.Runtime` projection | sprite IDs, source cells, screen/world positions, labels, HUD values | immutable derived data; replaceable renderer |

`reference/dominatus/samples/Dominatus.FishTank/FishtankGame.cs` currently places position and velocity in agent blackboards. `reference/dominatus/samples/Dominatus.TinyTown/TinyTownDemo.cs` also stores needs and abstract locations in blackboards while keeping relationships and memories in side dictionaries. Those are useful demos, not the ownership model for the product. `reference/dominatus/samples/Dominatus.RTSBenchmark/Simulation/ShipState.cs` is the closer precedent.

## Project ownership

Follow the repository's product-family grouping without putting the domain under a renderer:

```text
src/Games/TinyFarm/
  TinyFarm.Core/          authoritative types, rules, IDs, data contracts
  TinyFarm.Runtime/       Dominatus adapter, phases, save composition, projections
  TinyFarm.MonoGame/      disposable input/window/render adapter
tests/Games/
  TinyFarm.Tests/         headless simulation, determinism, save/replay proofs
```

`TinyFarm.Core` must not reference MonoGame, Godot, Aurelian, Machina, Oblivion, Avalonia, or Vulkan. `TinyFarm.Runtime` may reference `Dominatus.Core` and `Dominatus.OptFlow`. `TinyFarm.MonoGame` may reference Core/Runtime and MonoGame. A later Aurelian adapter can sit beside it. Machina and Oblivion integrations should also be leaf adapters.

## Existing capability used directly

- `Dominatus.Core/Runtime/AiWorld.cs` and `AiAgent.cs`: lifecycle, clock, agents, blackboards, events, tick.
- `Dominatus.OptFlow/Ai.cs`: decisions, actions, events, waits, and behavior composition.
- `Dominatus.Core/Runtime/AiEventBus.cs`, `ActuatorHost.cs`, and `ParallelAiWorldRunner.cs`: messaging, host effects, and deterministic staged parallel execution.
- `Dominatus.Core/Persistence/DominatusSave.cs`, `DominatusCheckpointBuilder.cs`, `ReplayDriver.cs`, and `ISaveChunkContributor.cs`: one save container, agent runtime checkpoint, replay, and game-owned extension chunk.
- Copeland records/tables and TSON: optional static item, crop, shop, NPC, and schedule data.
- Machina Core/Layout/Presentation/Runtime and standard controls: later HUD/menu composition.
- Oblivion Table, Markdown, Function, and Diagram cards: later read-only inspection without a new card kind.

## Deliberately game-local

The first implementation should keep stable game IDs, the grid, deterministic A*, calendar, inventory, farming, economy, relationships, objectives, `GameIntent`, resolution rules, and the serializable PRNG in the game domain. The evidence does not justify a shared ECS, economy, relationship framework, pathfinding library, controller framework, event bus, or second persistence system.

Only three cross-sample concepts have enough evidence to discuss:

| Candidate | Result | Evidence |
| --- | --- | --- |
| Agent lifecycle/events/checkpoint | `ALREADY_EXISTS` | Dominatus Core and every sample |
| Observe/decide/buffer/resolve/commit | `GAME_LOCAL_FIRST` | FishTank loop plus explicit RTS phases/action buffer |
| Renderer projection | `ALREADY_EXISTS` at contract level, adapter remains local | Aurelian `RenderSnapshot`; RTS visual state and FishTank draw reads |
| Controller source | `GAME_LOCAL_FIRST` | RTS AI action creation and sample host input, but no shared human/AI contract |

No candidate is `JUSTIFIED_SHARED` beyond APIs that already exist. A second consumer should be demonstrated before extraction.

## Renderer and UI decisions

Recommendation: **`USE_MONOGAME_TEMPORARILY`**.

`src/Aurelian/Aurelian.Rendering.Contracts/Snapshots/RenderSnapshot.cs` is renderer-neutral, and `src/Aurelian/Aurelian.Runtime/Rendering/WorldRenderSnapshotExtractor.cs` proves projection. However, `src/Aurelian/Aurelian.Rendering.Contracts/Resolved2D/Resolved2DOperations.cs` and `src/Aurelian/Aurelian.Rendering.Raster/AurelianCpuRasterRenderer.cs` only realize filled/stroked rectangles, positioned text, and rectangular clips. The Vulkan path proves a visible triangle and resource mechanisms, not a sprite/tile renderer. SDSL-V lowering is constrained to the smoke-triangle `VSMain`/`PSMain` shape and lacks texture/sampler/UV semantics.

GodotTinyTown has sprites, navigation, and audio, but `TinyTownWorld`, `TinyTownVillagerBrain`, and `TinyTownPresentation` bind world/agent/presentation concerns to Godot nodes and types. FishTank and MonoGameRtsDemo offer the smaller disposable host. The game should emit its own immutable presentation snapshot; a MonoGame adapter may translate it now, and an Aurelian adapter may translate it later.

Machina can express the desired HUD, shop, dialogue, pause, cards, text, grids, scrolling, and buttons. Missing work is a host composition adapter: UI-first hit testing/focus, unhandled input forwarded to the game controller, and ordered composition of the world and Machina frame. Keep that out of M1.

## Save and replay law

Use one `DominatusSave` file. The game supplies one versioned `GameSave` chunk through `ISaveChunkContributor`; Dominatus owns its checkpoint and replay chunks. Save only after the action buffer and event delivery are complete, mirroring `RtsBenchmarkCheckpoint.TickBoundary`.

The game chunk owns world entities, inventory, crops, economy, relationships, calendar, stable ID mapping, and explicit PRNG state. Dominatus owns agent blackboards, HFSM paths, and in-flight actuation cursors. Do not place domain records in blackboards and expect them to persist: `BbJsonCodec` supports only a small primitive set and skips unsupported entries.

## Exactly one M1

**TINY-FARM-M1 — Headless deterministic week.**

Create only `TinyFarm.Core`, `TinyFarm.Runtime`, and `TinyFarm.Tests`. Run a scripted seven-day, renderer-free slice with one small grid, the player, four Dominatus NPCs, four crop definitions, one shop, and these typed intents: move, plant, water, harvest, buy, sell, talk, and wait. Human script and AI must submit through the same deterministic resolver. Persist at a stable tick boundary using one game chunk plus the Dominatus checkpoint, reload, and prove the same final canonical state hash as uninterrupted execution.

M1 succeeds only when tests prove:

1. the seven-day run completes twice with the same hash;
2. save/reload continuation equals uninterrupted execution;
3. human and AI intents enter the same resolver;
4. NPC schedule outcomes are deterministic;
5. crop and buy/sell mutations occur only in the resolver; and
6. no renderer, UI framework, Oblivion, or Aurelian graphics package is loaded.

M1 explicitly excludes a window, art, audio, Machina, Oblivion transport, LLM calls, native Aurelian sprite work, generalized extraction, and a full content set.

## Stop conditions for M1

Stop honestly if the game-domain save chunk cannot round-trip alongside a Dominatus checkpoint, if restoring agent identity cannot be mapped deterministically, or if the same resolver cannot accept both player and agent intents without changing Dominatus Core. Those are the only findings that would invalidate the composition thesis; renderer/UI incompleteness does not.

## M1 evidence update — 2026-09-01

TINY-FARM-M1 replaced the earlier seven-day farming proposal with the smaller headless Ariadne adventure requested by the milestone. The implementation is in `src/TinyFarm` and its proof is in `artifacts/tiny-farm-m1/proof.json`.

The central M0 thesis survived: one game-owned state plus closed typed intents, stable ordering, a single resolver, semantic results, and save composition is enough to make a complete small game. The player and three scheduled NPCs live through a ten-hour day; the player moves, talks, trades, takes and gives items, completes a favor, saves, reloads, and replays to the same canonical hash. Two NPCs move autonomously during a wait, and a player/NPC collision over the same item has one stable winner.

Two M0 details changed:

- Dominatus 1.0.0 source generation removed the need to hand-author flow-definition graphs. TinyFarm authors three attributed NPC states and one attributed Ariadne state; generated factories construct both definitions.
- A persistent Dominatus checkpoint is unnecessary for this slice. NPC decisions are observation-pure and use fresh bounded flow instances; physical/economic truth never enters a blackboard. The save therefore records the game, deterministic sequence cursor, recent semantic events, and explicit agent/narrative composition metadata. A future persistent-memory agent is the threshold for adding a Dominatus checkpoint.

No new shared Aurelian abstraction was justified. TinyFarm.Runtime reuses Aurelian's existing sequential Dominatus world runner. Intent envelopes, ordering, saves, clocks, and hashing remain game-local until a second non-TinyFarm game demonstrates the same contract. TinyFarm.Core has no package or project dependencies.

The evidence recommends **M2 A: a headless deterministic week with farming/economy**. The next bounded work is crop definitions and plant/water/harvest/day-transition rules on this resolver and save model. Graphics, ECS, generalized persistence, and UI remain outside that milestone.

## M2 evidence update — 2026-09-02

TINY-FARM-M2 completed the seven-day pressure test with two farming/economy cycles, recurring weekday/market/weekend schedules, Dominatus-origin movement and commerce, TSON-authored immutable content, six exact save/reload continuations, and canonical state/result/event replay. The authoritative-state plus typed-intent model remained coherent. Duration added explicit plot, product-stack, finite-stock, day-boundary, and definition-provenance records; it did not create pressure for ECS or hidden update systems.

Dominatus `SaveFile`, `SaveChunk`, `SaveWriteContext`, and `SaveReadContext` now provide the sole chunk container. TinyFarm owns four versioned semantic payloads and validates a complete candidate before session construction. No generic persistence extension or Aurelian abstraction was justified. The detailed evidence is in `docs/Aurelian/games/tiny-farm-m2-headless-week-report.md` and `artifacts/tiny-farm-m2/proof.json`.

M2 recommends **M3 A: the first graphical projection**, using immutable state projection and preserving TinyFarm Core as the only farming/economy authority.
