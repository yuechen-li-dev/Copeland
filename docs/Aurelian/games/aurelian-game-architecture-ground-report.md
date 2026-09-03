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

## M3 evidence update — 2026-09-02

TINY-FARM-M3 added `TinyFarm.MonoGame` as a temporary graphical leaf over the immutable, deterministic `TinyFarmFrame` projection. Keyboard and line-oriented LLM controls both produce the existing intent family and enter `TinyFarmSession.Step`; Dominatus NPC envelopes and human envelopes still commit through the one resolver. MonoGame/XNA types do not appear in Core or Runtime, and render cadence does not advance authoritative time.

The real window now shows the four-location world, player, stable NPCs, farm plots and crop stages, ground items, HUD, interactions, Ariadne prose, economy controls, and save/load. The retained semantic proof covers the full M2 farm loop, visible autonomous NPC movement, save/mutate/load reprojection, exact M1/M2 hashes, and deterministic frame hashing. Replacing MonoGame requires changing only the graphical leaf; state, resolver, persistence, NPC logic, replay, REPL, LLM command semantics, and frame projection remain reusable.

Observed integration pressure is now the leaf-local bitmap overlay, so the exact M4 recommendation is **Machina.UI game UI integration**. Native Aurelian 2D remains a later adapter replacement and should not be coupled to that UI milestone.

## M4 evidence update — 2026-09-02

TINY-FARM-M4 followed newer play pressure and added scene composition before UI integration. The authoritative model is now explicit: scene definitions are validated object/layout/spawn/route tables; actor scene placements are versioned world records; route interaction is an ordinary resolver reduction; and `TinyFarmFrame` projects only the active scene. The M3 world map became the Overworld scene without adding a mutable scene graph or second world.

Scene definition law is `static validated tables + persistent game state -> immutable active-scene projection`. Layout never depends on object trees or renderer allocation. Routes derive the graph between Farm, Overworld, Town, General Store, and Riverside and store target spawn IDs. Save/load uses the existing four Dominatus chunks and restores exact player scene and integer tile.

Dominatus continues to choose high-level schedule destinations. TinyFarm performs deterministic coarse movement for inactive NPCs and exposes authoritative scene placement to the active projection; the renderer never spawns actors independently. MonoGame now uses the live viewport with a shallow HUD at 2560x1440 and 1280x720. TSON and Copeland record tables fit future authored definitions, while M4 deliberately keeps readable C# definitions and adds no language feature or shared UI/game runtime.

The detailed proof and next recommendation are in `docs/Aurelian/games/tiny-farm-m4-scene-composition-report.md` and `artifacts/tiny-farm-m4/proof.json`.

## M5 evidence update — 2026-09-02

TINY-FARM-M5 replaced authoritative tile-centered actors with deterministic `ScenePosition` values at 1,024 integer units per tile while retaining `GridPosition` for scene authoring, collision cells, portals, plots, and navmesh input. Four-way facing and forward semantic target selection now drive talk, farm, shop, and portal interaction through the existing resolver. MonoGame owns only held-key sampling and a rational fixed-step accumulator; the frame exposes integer scene coordinates and the current semantic target.

Runtime derives and caches scene-local DotRecast 2026.3.1 Recast/Detour data from the validated layout tables. DotRecast types remain in one adapter file, paths are not persisted, semantic collision remains final authority, and typed failures never teleport actors. Dominatus continues to choose coarse `LocationId` goals; visible NPCs use cached waypoints and the same `SpatialMoveIntent` reducer as the player, while inactive NPCs retain coarse progression. Scene graph routing and spatial navigation remain separate layers.

The extraction result is `KEEP_TINYFARM_LOCAL`: the contracts look genre-neutral but lack a second concrete consumer. M5 instead exposes one bounded next pressure: high-level schedule destinations currently map to game-local hard-coded scene points. The recommended M6 is authored semantic scene anchors plus active/inactive NPC handoff, without new content or a navigation framework. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m5-continuous-navigation-report.md` and `artifacts/tiny-farm-m5/proof.json`.

## M6 evidence update — 2026-09-02

TINY-FARM-M6 removes the remaining schedule-coordinate mapping. Stable typed `SceneAnchorId` rows now join a semantic place to its owning scene and integer realization. Spawn is an anchor kind rather than a parallel coordinate table, routes target anchors, Dominatus observations carry semantic anchor goals, and active lowering is anchor -> position -> DotRecast -> canonical waypoint -> ordinary `SpatialMoveIntent`. Missing and unreachable anchors are typed failures and never snap actors.

The player's authoritative current scene defines detailed fidelity. Inactive NPCs reuse the existing deterministic location schedule and issue zero path queries; their one exact `ActorSceneState` remains persisted at a stable entry/last-known position. Activation derives a fresh path from that position to the scheduled anchor. Deactivation discards path cache while retaining semantic location, exact placement, facing, and schedule derivation. This is not streaming: no async loading, background simulation, LOD, crowd, or second world-state store exists.

The authority invariant is `ActorState.Location -> compatible SceneId == ActorSceneState.Scene`; location is coarse scene-scale truth and `ActorSceneState` is the sole exact placement. Compatibility is exact except for the existing Overworld transit scene, which corresponds to the Town Square coarse graph node. Arrival is a deterministic authored-radius event. Save/load in either fidelity reproduces exact authoritative state and later realization, and the frame exposes semantic NPC targets without transferring ownership to MonoGame.

The extraction recommendation is `DEFER_UNTIL_SECOND_GAME`. TinyFarm now supplies strong evidence for semantic addresses and a fidelity handoff contract, but no second product consumes them. The table shape does justify the bounded next milestone: TSON-author the existing Scenes, Anchors, and Routes through the established loader, without a DSL, editor, hot reload, language feature, or Aurelian package extraction. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m6-semantic-anchors-handoff-report.md` and `artifacts/tiny-farm-m6/proof.json`.

## M7 evidence update — 2026-09-02

TINY-FARM-M7 moves the complete five-scene catalog from C# factories into five human-readable Object TypeScript record tables: scenes, objects, tile layout, semantic anchors, and routes. Five files are used because each self-described TSON document has one exact nominal table root. An explicit filename list fixes load and aggregate-hash order. Row order is never identity; the loader converts textual keys into the existing typed IDs and `SceneDefinition` values, whose established deterministic ordering remains intact.

The authority boundary is `TSON -> TinyFarmDefinitionLoader -> TinyFarmSceneCatalog -> SceneDefinition -> runtime`. TSON validates representation first; `TinyFarmScenes.Validate` then validates game semantics including uniqueness, bounds, walkability, object/layout joins, typed semantic references, portal triggers, target scenes, and target anchors. Raw TSON types stay in the Runtime loader. Core, reducers, DotRecast, persistence, projections, MonoGame, and LLM control consume only the typed catalog. Scene provenance records source hashes and load timings separately from gameplay/save truth; the unchanged M2 definition identity preserves M6-format save compatibility.

Production loading, hostile fixtures, row-reorder identity, authored-to-canonical-TSON reload, existing TableScript queries, and the composed M4/M6 scenario prove parity. M1/M2 hashes and M6 state/result/event/anchor/navigation/projection hashes remain exact. The scene aggregate is `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa`. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m7-tson-scene-authoring-report.md` and `artifacts/tiny-farm-m7/`.

M7 does not add schedule logic. The remaining concrete pressure is the growing game-local `ScheduledAnchor` branch selection in front of an existing Dominatus utility flow. The bounded next recommendation is a Dominatus utility-authored schedule/transition selection milestone that preserves the exact schedule and semantic hashes; it is not a scene-loader concern and does not justify a general planner.

## M8 evidence update — 2026-09-02

TINY-FARM-M8 removes the production actor/time anchor branch. Eleven immutable Runtime schedule rows now describe the exact prior law, including Mara's day-6 and day-7 priority overrides. A generated Dominatus OptFlow uses the stable `TinyFarm.NpcSchedule.Anchor` decision slot and five semantic-anchor options. The highest-priority active window scores its anchor at 1 and all others at 0; conflicting top-priority anchors fail deterministically. Hysteresis and commitment are zero, so boundaries remain exact. `ScheduledAnchor` survives only as a delegating compatibility API.

The authority path is now `actor identity + absolute world minute -> Dominatus decision -> SceneAnchorId -> existing active/inactive realization`. Active NPCs still derive DotRecast paths and submit ordinary movement intents; inactive NPCs use the same goal for coarse semantic progression with zero navigation. A changed goal invalidates derived path identity on the next observation. Schedule goals remain observation-pure and are recomputed after save/load, including immediately before and after transitions and while moving. Renderer, scene loader, navigation adapter, Ariadne, and TinyFarm Core remain isolated from Dominatus schedule internals.

Minute-by-minute test-only comparison covers every NPC across all seven days, including every transition's preceding, exact, and following minute. M1/M2, M6 state/result/event/handoff/navigation/projection, and M7 scene-content hashes remain exact. No Dominatus Core feature, scheduler, planner, calendar, DSL, random behavior, or new content was added. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m8-dominatus-schedule-selection-report.md` and `artifacts/tiny-farm-m8/`.

The remaining concrete pressure is content ownership: the schedule is now exactly a flat validated table. The bounded next recommendation is **TINY-FARM-M9 — TSON-authored NPC schedule windows**, limited to moving the eleven rows through the existing definition loader with overlap, coverage, actor, and anchor validation while preserving the M8 decision graph and hashes.

## M9 evidence update — 2026-09-02

TINY-FARM-M9 moves all eleven schedule rows into the single nominal `NpcSchedules` Object TypeScript table. Its columns are actor ID, validated day token, half-open minute bounds, semantic anchor ID, explicit priority, and inspection reason. `TinyFarmDefinitionLoader` converts the source once into immutable `TinyFarmScheduleDay` and `TinyFarmScheduleWindow` values, validates actor/anchor references, bounds, duplicates, seven-day coverage, and unambiguous highest-priority overlap, then builds a canonical actor index. The schedule semantic content hash is `649ef384a746e358a7463548f33574c43f2d33dd19d0cb2ed03a04bd3b946b55`.

The authority path is `TSON schedule rows -> typed TinyFarmScheduleCatalog -> existing generated Dominatus selector -> SceneAnchorId -> existing navigation/handoff/runtime`. Raw TSON remains inside the Runtime loader, while the typed catalog travels with `TinyFarmDefinitions`; saves retain only semantic state and IDs. The generated five-option graph remains static and is not rebuilt per decision. Source row order is cosmetic and a reversed authored table preserves all 30,240 decisions.

M1, M2, M7 scene content, and all M8 decision/anchor/state/result/event/handoff/navigation/projection hashes remain exact. Existing TableScript tooling lists, queries Mara, queries Day6, emits rows, and validates the production table. A future hybrid regime can add one typed discriminant when concrete utility behavior exists; M9 adds no regime column or fallback. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m9-tson-npc-schedules-report.md` and `artifacts/tiny-farm-m9/`.

## M10 evidence update — 2026-09-02

TINY-FARM-M10 replaces M9's flat `Every`/`Day1` ... `Day7` workaround with the nominal payload enum `ScheduleDay { Every, Day(value: number) }`. Production `Day(6)` and qualified `ScheduleDay.Day(6)` TableScript queries execute successfully. Stable window IDs and a normalized `UtilityCandidates` table add exactly two regimes: Required and Open. Required rows carry one semantic anchor; Open rows carry none and reference a bounded candidate set through their stable ID.

The selection law is structural: choose the highest-priority authored window, return its anchor immediately when Required, otherwise enter the persistent Dominatus utility path for only that Open window's candidates. Mara's 17:00–22:00 window is the sole Open proof with existing `farm.home` and `town.square` anchors and deterministic current-anchor stickiness. At 22:00 the Required `farm.home` window wins immediately, the Open score trace disappears, and the existing goal identity causes active navigation to replan. Inactive progression uses the same semantic winner with zero path queries.

The persistent `FlowDefinition`, static options, `KeepRootFrame`, world, and per-actor agents from INFRA-M10A remain. Saves persist no candidates, scores, runners, or paths; Open and hard-boundary winners recompute identically after load. Exact 30,240 migration parity plus M1, M2, M7, M9 decision, and M9 anchor hashes remain. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m10-hybrid-scheduling-report.md` and `artifacts/tiny-farm-m10/`.

## M11 evidence update — 2026-09-02

TINY-FARM-M11 turns the M10 Open selector into a persistent, indexed runtime primitive without changing its authored or gameplay semantics. A canonical candidate backing array is indexed once by stable window ID and exposed as an allocation-free `ArraySegment`; hot lookup and expected-winner selection use bounded loops. Each session owns one schedule runtime that lazily owns isolated per-actor worlds, agents, HFSM roots, result slots, and defensive locks, while the static option definitions and `Decide` step are reused. There is no catalog-wide decision lock.

Structured score traces are now opt-in: ordinary execution uses scalar score locals and a shared empty trace, while inspection materializes one exact-size array of readonly score structs. Catalog is observed once per actor, unchanged reference observations are blackboard no-ops, and minute is no longer boxed into the Dominatus blackboard. Required still returns before all Open machinery.

Save/load persists no catalog ranges, runners, frames, decision slots, or score buffers. Runtimes hold no session or world-state reference, so session replacement cannot retain stale session state; semantic observations reconstruct the same winner. Warmed candidate retrieval measures 0 B, and warmed Open selection drops from 3,065.75 B to 432 B per decision. The remainder is Dominatus's score tuple array plus live-context and blackboard callback construction. At 785.356 ns/Open decision, the isolated capacity is roughly 1,273,308 decisions/s; this is not full simulation throughput.

All M1, M2, M7, M9, and M10 hashes remain exact. The next recommendation is a first bounded Proto-Sim behavior expansion using this qualified primitive, while any generic Dominatus allocation work remains an independent measured follow-up. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m11-persistent-action-flow-report.md` and `artifacts/tiny-farm-m11/`.

## M12 evidence update — 2026-09-02

TINY-FARM-M12 adds one game-domain need: fixed-point NPC Energy in `[0, 10000]`. Authoritative minutes decay active NPCs by 8 units/minute and recover resting NPCs by 40 units/minute. Energy and Rest state live in `TinyFarmState`, persist in the existing chunked save, enter the version-5 semantic hash, and are projected through inspection and MonoGame. Renderer frames, wall time, Dominatus blackboards, and Ariadne prose own none of this truth.

One TSON-authored Hearth House scene contains three uniquely owned Bed objects and three personal Rest anchors. Authored routes connect it to Farm. Definition validation joins actor ownership, Bed object, walkable anchor, Required bedtime, and own-bed Open candidate. Active NPCs use the existing semantic-goal -> scene route -> DotRecast path; inactive NPCs use the same goal and Rest law with zero path queries.

Open Rest utility is `base + stickiness + (10000 - Energy) / 10000 × 0.8`. Required bedtime still returns the personal bed structurally and emits no utility trace. The persistent five-option M11 graph remains five options by resolving the existing home slot to the active NPC's authored personal bed. Candidate lookup and the Energy scorer allocate 0 B; ordinary Open selection remains 432 B.

The M12 catalog is explicitly versioned beside, rather than over, the historical catalog. M1–M11 scenarios therefore retain their exact executable hashes while the M12 scene and schedule hashes identify the new content. Detailed proof is in `docs/Aurelian/games/tiny-farm-m12-energy-rest-protosim-report.md` and `artifacts/tiny-farm-m12/`. A second need remains out of scope; the next bounded pressure is playback of recovery-driven departure from bed.

## M13 evidence update — 2026-09-02

TINY-FARM-M13 adds the thin game-owned `TinyFarmSimulationHost` after auditing Aurelian, Dominatus, JTF, Machina.UI, Oblivion, Marionette, and existing TinyFarm infrastructure. Aurelian's ordinal clock/frame loop and Dominatus's float AI clock/MonoGame connector do not express independent renderer, locomotion, world-minute, decision, load-reset, and catch-up laws, so no generic clock was forked or extended. The host owns typed Pause/Play/FastForward mode and integer accumulation; `TinyFarmSession`, `WaitIntent`, the resolver, Energy/crops/schedules, active/inactive fidelity, persistence, and projections remain authoritative in their existing layers.

MonoGame now sends only `ElapsedGameTime` and semantic commands to the host, while CLI, LLM, and tests use the same command surface. Play is five real seconds per game minute, FastForward is 10×, locomotion opportunities are 60 Hz, and host updates are clamped to five seconds with excess discarded rather than backlogged. NPC utility runs at world-minute or semantic invalidation cadence, never render/held-movement cadence. A versioned renderer-free simulation snapshot serializes deterministically to canonical TSON with typed IDs/enums. Detailed proof is in `docs/Aurelian/games/tiny-farm-m13-simulation-host-multirate-clock-report.md` and `artifacts/tiny-farm-m13/`.

M14 places active-scene NPC path following in that qualified 60 Hz domain. Policy decides why/where, event-driven DotRecast planning decides the canonical waypoint path, fixed locomotion submits bounded `SpatialMoveIntent` reductions, and rendering observes. Inactive NPCs remain coarse and never wander at 60 Hz. Two TSON-authored Farm Wander anchors provide deterministic Open-regime visible activity without randomness or a new need; low Energy and Required bedtime still select personal Rest. NPC movement is kinematic at 16 integer units/opportunity, arrival and Rest begin on the physical reaching step, and paths remain derived across save/load. Current measured pressure is 6,720 B per full authoritative NPC movement reduction, so any follow-on should address that concrete resolver projection cost rather than add a new scheduler or movement framework. Detailed proof is in `docs/Aurelian/games/tiny-farm-m14-active-npc-fixed-step-locomotion-report.md` and `artifacts/tiny-farm-m14/`.

## M15 evidence update — 2026-09-02

TINY-FARM-M15 reduces the warmed active-NPC authoritative movement path from M14's 6,720 B/reduction to 857.5 B/reduction while preserving the canonical M14 semantic hash. `TinyFarmResolver.ResolveSpatialMoveCore` is the single semantic core for public, NPC, and fixed-player movement: it validates actor, cardinal delta, distance, bounds, and collision before replacing one authoritative actor placement, updating facing, and clearing Rest. The public resolver retains copied-state batch semantics and materializes the same result/event contract; ordinary fixed locomotion skips only the proof-facing defensive state snapshot.

Movement events remain exact and ordered, including `AnchorReached` on the entry step. State-owned actor indexes and validated scene-owned blocking occupancy are derived caches rebuilt by construction/load and are not save truth. The follower continues to reuse its path plus integer waypoint index; a typed goal identity and session-owned order buffer remove per-step strings, LINQ sorting, and temporary arrays. Frame, simulation DTO, TSON, semantic hash, and inspection projections remain downstream and on request.

The movement core itself measures 48 B/reduction, the localized immutable `ActorSceneState` replacement. Remaining full-path allocation is observable intent/envelope/result/event machinery plus bounded step collection and amortized arrival/path/policy work. That pressure does not justify a second authority or movement framework. Detailed proof is in `docs/Aurelian/games/tiny-farm-m15-authoritative-locomotion-allocation-report.md` and `artifacts/tiny-farm-m15/`.

## M16 evidence update — 2026-09-02

TINY-FARM-M16 adds the first player-facing RPG control surface without moving inventory or gameplay authority. Existing inventory stacks remain TinyFarmState truth. A fixed eight-slot semantic hotbar binds existing Turnip Seed and Turnip products plus six explicit empty slots; only the selected slot is version-6 player state. Selection enters the semantic hash, persists in the existing world chunk, and mutates through SelectHotbarSlotIntent, TinyFarmSession, and TinyFarmResolver. Inventory-open, hover/focus, and movement suppression remain unsaved UI-session state.

The mandatory audit found that Machina.UI already owns strong authoring, stack/grid/anchor layout, Standard controls, semantics, input records, hit testing, and backend-neutral presentation IR, and Oblivion demonstrates those paths in its raster/standalone hosts. No qualified MonoGame same-window presentation translator or input adapter exists. M16 therefore selects MONOGAME_TEMPORARY_UI: TinyFarmPlayerUiView and its responsive layout stay renderer-independent, while the existing MonoGame leaf draws and hit-tests the bounded hotbar/panel. This avoids a new framework and isolates the exact future integration seam.

Number keys 1–8 now own hotbar selection. Space toggles Pause/Play, F toggles Play/FastForward, and N replaces Space for Wait. Keyboard and pointer selection share one controller path and one typed intent. Opening inventory never pauses; it suppresses player movement/interaction while keeping selection, close, and simulation controls predictable. The version-2 simulation DTO, canonical TSON, CLI, and LLM surface expose inventory, hotbar, and selected semantics without GUI types or mouse emulation. Real 2560×1440 and 1280×720 windows showed readable, unclipped overlays with Elias continuing to wander. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m16-inventory-hotbar-ui-report.md` and `artifacts/tiny-farm-m16/`.

## M17 evidence update — 2026-09-02

TINY-FARM-M17 completes the first resolver-owned item-action loop. Existing `TakeIntent` remains pickup authority for identity-bearing `ItemState`/`ItemId`; M17 adds only the missing authoritative `GroundScene` plus fixed-point `GroundPosition` needed for continuous facing/range targeting. Pickup clears ground placement, assigns the item owner, adds the same identity to `ActorState.Inventory`, emits `ItemTaken`, and lets frame/UI projections observe the new state. Version-7 persistence validates the item container/placement invariant. Ground items never block movement.

Target choice is deterministic: actor, portal, ground item, plot, shop; then squared distance; then ordinal stable ID. `UseSelectedIntent` reads the semantic hotbar binding and performs one closed lowering: available Turnip Seed plus targeted farm plot becomes the existing `PlantIntent(plot, turnip)` resolver path. Plant validation, crop construction, and seed decrement were not copied. Direct Plant and selected-use paths have equal state/event/result semantics, including occupied-plot rejection.

The future extension seam is intentionally the same closed controller pattern: select semantic binding, select semantic target, request a game-owned use intent, lower to an existing authoritative intent. Sword/Attack, Potion/Consume, Pickaxe/Mine, and Spell/Cast are illustrative future pairs, not M17 types or registries. The demonstrated next pressure is narrower: one ground representation for a stackable product so pickup can affect a hotbar count. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m17-pickup-use-selected-report.md` and `artifacts/tiny-farm-m17/`.

## M18 evidence update — 2026-09-02

TINY-FARM-M18 establishes the complementary world-producer law without overloading M17 item identity: an authored forage node produces a `ProductId` stack, while an `ItemState` remains one durable `ItemId` transferred between containers. One M18 TSON row defines the Riverside Hen-of-the-Woods node, scene position, product, and fixed yield. `TinyFarmState` persists only Available/Depleted state; authored definition data is not rewritten into mutable saves.

Contextual Interact uses the existing facing/range query with deterministic priority actor, portal, ground item, forage node, plot, shop. `TinyFarmResolver` validates actor, node, scene, range, availability, product, and yield before atomically incrementing `InventoryStacks` and depleting the node. The renderer and UI only project the resulting absence, hint, and inventory count. Version-8 save/hash and simulation snapshot v4 preserve and expose the node independently of the active scene.

The future environmental seam belongs on static availability policy over the concrete definition: scene/biome, season, weather, day, and rarity may later decide whether a node is available, but none is present in M18. The crafting seam is already sufficient: a future recipe can consume `HenOfTheWoods ProductId` without changing forage architecture. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m18-hen-of-the-woods-foraging-report.md` and `artifacts/tiny-farm-m18/`.

## M19 evidence update — 2026-09-02

TINY-FARM-M19 establishes the first flat transformation law: declarative authored recipe plus semantic station plus authoritative `InventoryStacks` becomes `CookIntent`, which only `TinyFarmResolver` may execute. One TSON station row adds `hearth-house-kitchen` to the existing Hearth House scene. Separate recipe and repeated-input TSON tables define exactly `hen-of-the-woods x1 -> sauteed-hen-of-the-woods x1`; loader validation resolves product identities, positive counts, unique recipes/inputs, and the one-case `Cooking` station compatibility. Recipes and stations contribute to definition identity, never mutable state hash.

Contextual Interact retains the shared 1,280-unit facing corridor and deterministic priority actor, portal, ground item, forage node, plot, cooking station, shop. The single-recipe convenience lowers directly to Cook; a future multi-recipe station must present explicit selection. The resolver reads all ingredient counts, validates every failure, computes the output count, then consumes inputs and adds the one output in one reduction. `RecipeCooked` is the sole success event; UI/frame/DTO re-read state, while save/load and replay use existing inventory semantics with no version bump.

The future generalized seam is conceptual rather than infrastructural: `Station + flat RecipeDefinition + Inputs -> station-specific transformation intent`. Cooking remains explicit; a later Smithing, Alchemy, or Workbench feature must earn its own verb and shared abstraction from real duplication. M19 adds no `CraftIntent`, graph, unlocks, skills, quality, food effects, duration, energy, or batch quantity. Detailed evidence is in `docs/Aurelian/games/tiny-farm-m19-hen-of-the-woods-cooking-report.md` and `artifacts/tiny-farm-m19/`.
