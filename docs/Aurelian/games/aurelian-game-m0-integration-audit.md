# AURELIAN-GAME-M0 Cross-Project Integration Audit

## 1. Outcome

**Outcome B.** The repository contains nearly all supporting machinery, but the product-level authority seam is only demonstrated inside samples. The missing foundation is a renderer-independent `GameState` plus typed, deterministically ordered intent resolution and save composition. It belongs in the game first, not in Dominatus or Aurelian.

Classification terms in this report are literal: `FOUND`, `PARTIAL`, `MISSING`, `WRONG_OWNER`, `DEMO_ONLY`, and `PRODUCTION_READY`.

## 2. Current dependency topology

The machine-readable graph is in `artifacts/aurelian-game-m0/repository-topology.json`.

| Family | Direct evidence | Current owner |
| --- | --- | --- |
| Dominatus | `reference/dominatus/src/Dominatus.Core/Runtime/AiWorld.cs`; `AiAgent.cs`; `Persistence/*` | agent/runtime state, events, actuation, checkpoint/replay |
| Aurelian | `src/Aurelian/Aurelian.*/*.csproj` | renderer contracts/mechanism, render-facing world data, shader assets |
| Machina | `src/Machina/Machina.*/*.csproj` | UI nodes/layout/presentation/runtime; optional Dominatus adapter |
| Oblivion | `src/Oblivion/Oblivion.*/*.csproj` | workbench model, persistence, presentation, app commands, CLI/UI hosts |
| Marionette | `src/Marionette/Marionette.Core/Marionette.Core.csproj` | Skyrim import/world contracts, not generic game state |
| Skyrim product | `src/Skyrim/Aurelian.Marionette/Aurelian.Marionette.csproj` | Skyrim scenarios and adapters |
| Copeland | `src/Copeland/*` | compiler, TSON, record tables, build/tooling substrate |
| JointTaskForce | `JointTaskForce.slnx` and integration solution | build aggregation, no runtime ownership |

Important directions: `Aurelian.Runtime` references `Aurelian.World`, rendering contracts, and the `Dominatus.Core` package; `Aurelian.Machina` references Machina Presentation/Runtime and Aurelian Core/contracts; `Machina.Dominatus` references both families; `Oblivion.Standalone` is the composition root for Oblivion, Avalonia, Aurelian.Machina, and Raster. A game placed under Aurelian Runtime would invert product ownership. Marionette types would inject Skyrim-shaped authority. Both are blockers to a clean domain and are avoided by an independent game family.

## 3. Dominatus capabilities

`AiWorld.Tick` advances `AiClock`, expires world blackboard entries, ticks `ActuatorHost`, and ticks agents. `AiAgent` owns a blackboard, typed event bus, HFSM, change tracking, and in-flight actuations. `Dominatus.OptFlow/Ai.cs` supplies `Decide`, `Act`, `Event`, `Wait`, `Await`, and `Perform`. `ParallelAiWorldRunner` and staged surfaces provide deterministic commit/conflict behavior. These are `FOUND + PRODUCTION_READY` as AI/runtime infrastructure.

`DominatusCheckpointBuilder` captures clock, world/agent blackboards, active HFSM path, and actuation cursor. `DominatusSave` writes meta/HFSM/replay chunks and accepts `ISaveChunkContributor`. `ReplayDriver` handles advance, choice, text, external JSON, and RNG-seed events. These are reusable directly, subject to the stable-boundary and domain-chunk rules below.

Dominatus remains AI infrastructure rather than a complete game engine because it does not own a general entity world, scene transitions, reset, a human controller, collision/navigation, game rules, calendar, or authoritative action resolution. Pause/time-scale exist in the MonoGame connector host, not as a general world law.

## 4. TinyTown reusable pieces

`reference/dominatus/samples/Dominatus.TinyTown/TinyTownDemo.cs` defines townie profiles and traits, needs, `WorkSchedule`, relationships, memories, dialogue outcomes, typed messages, and snapshot/options. It creates four profiles, updates needs, consumes mail, ticks `AiWorld`, and applies game-specific actions. Utility decisions use `Ai.Decide`; work time uses `CurrentTick % 24`; locations are strings.

Direct reuse is at API/pattern level: `Ai.Decide`, typed mail, cassette LLM calls, deterministic tests, bounded parsing, relationship-effect clamping, and append-only memories. The profile, schedule, relationship, memory, location, and action types are `DEMO_ONLY` and should remain game-specific. No missing Dominatus contract is implied by those domain types.

The 19 tests in `reference/dominatus/tests/Dominatus.TinyTown.Tests/TinyTownDemoTests.cs` cover repeatability, needs/actions, mail, relationships, memory, LLM fallback, and clamping without live dependencies. That test style is directly reusable.

## 5. FishTank reusable pieces

`reference/dominatus/samples/Dominatus.FishTank/FishtankGame.cs` owns the MonoGame `Game`, window/device, input, update/draw loops, `AiWorld`, fish agents, and food. Its loop reads input, spawns food, updates perception, ticks Dominatus, integrates motion, resolves food collection, and draws blackboard-derived state.

The host sequence and actuator composition are `COPY_PATTERN_ONLY`. Fish rules, renderer, bindings, and unseeded `Random` are `LEAVE_SAMPLE_LOCAL`. Position/velocity in private agent blackboards is the key `WRONG_OWNER` precedent: a complete game needs positions and collision state in authoritative `GameState`, with observations copied into agent blackboards.

## 6. RTS reusable pieces

Yes—the missing law already exists in `reference/dominatus/samples/Dominatus.RTSBenchmark/Simulation/BattleSimulation.cs`:

```text
SensorPhase -> DecisionPhase -> StageAction -> SortActions
            -> ResolutionPhase -> EventPhase
```

`ShipState.cs` holds authoritative entities; `ShipAction.cs` is the typed buffer item; `SortActions` establishes tick/priority/faction/actor/target/type ordering; `DeterminismHasher.cs` creates canonical hashes; `RtsBenchmarkCheckpoint` names a tick-complete/action-buffer-cleared/event-delivery-complete boundary. Parallel decisions can use `ParallelAiWorldRunner` while resolution stays deterministic.

This is `FOUND + DEMO_ONLY`: copy the phase law, not the ship types. `Dominatus.MonoGameRtsDemo` reinforces separation between simulation state and renderer glue.

## 7. Aurelian rendering readiness

Renderer-neutral contracts are `FOUND`: `Aurelian.Rendering.Contracts/Snapshots/RenderSnapshot.cs` contains cameras and `RenderItem2D` values; `RenderCommandPlanBuilder.FromSnapshot` canonicalizes order; NullRenderer traces plans; `Aurelian.Runtime/Rendering/WorldRenderSnapshotExtractor.cs` projects Aurelian world data.

Current 2D realization is `PARTIAL`. `src/Aurelian/Aurelian.Rendering.Contracts/Resolved2D/Resolved2DOperations.cs` deliberately limits operations to fill/stroke rectangles, positioned text, and rectangular clips, and `src/Aurelian/Aurelian.Rendering.Raster/AurelianCpuRasterRenderer.cs` realizes only those. The Vulkan path contains texture/resource/command mechanisms and a visible-triangle qualification, but not an end-to-end sprite/tile material path. `src/Aurelian/Aurelian.World/Stores/WorldDataDocument.cs` is render-facing names/transforms/renderables, not a game world.

## 8. 2D rendering recommendation

**`USE_MONOGAME_TEMPORARILY`.** Current Aurelian cannot render a small sprite/tile game through a qualified production path today. The smallest eventual Aurelian subset is textured-quad/sprite realization, texture loading/lifecycle, source rectangles/UVs, orthographic camera realization, and a real input/window host.

GodotTinyTown is visually closer but binds lifecycle and movement to `DominatusWorldNode`, `DominatusAgentNode`, `CharacterBody2D`, and `NavigationAgent2D`. FishTank/MonoGameRtsDemo make a smaller replaceable adapter. No MonoGame type may cross into Core/Runtime.

## 9. Dominatus-as-engine readiness

| Concern | Result | Evidence |
| --- | --- | --- |
| main update lifecycle/world ticking | `FOUND` | `AiWorld.Tick` |
| agent lifetime | `FOUND` | `AiWorld.CreateAgent`; `AiAgent` |
| scheduling/time | `PARTIAL` | `AiClock`; demo-local schedules |
| event routing/effects | `FOUND` | `AiEventBus`; `ActuatorHost` |
| pause/reset/scene transition | `MISSING` as general contracts | pause/time scale only in connector/sample hosts |
| persistence/determinism/replay | `FOUND + PARTIAL` | runtime state is covered; game domain needs a contributor |
| controller abstraction | `MISSING` | samples create actions directly |

Dominatus is sufficient as the agent/runtime dependency, not as owner of the whole product loop.

## 10. World-state ownership recommendation

`TinyFarm.Core` should own stable IDs, grid cells, positions, plots, crops, inventory, money, relationships, calendar, objective state, and PRNG state. None exists in a suitable reusable owner. Aurelian's Unit/Entity IDs and world data are renderer-facing; Dominatus AgentId is agent identity; Marionette IDs encode Skyrim import provenance. A game-local ordinary record model is sufficient. An ECS is unnecessary for this MVP.

## 11. Agent-state ownership recommendation

Dominatus owns observations, choice/HFSM state, decision memory, event inbox, and in-flight actuation. Game IDs map explicitly to `AgentId`; auto-incremented `AgentId` must not become persisted product identity. Physical location, inventory, money, relationships, and crop ownership stay out of blackboards.

## 12. Presentation-state ownership recommendation

The game runtime emits an immutable presentation snapshot derived after world commit. It contains logical asset IDs, transforms, draw order, source cells, and HUD values but no authoritative state. MonoGame, a later Aurelian adapter, Machina, and Oblivion consume projections only.

## 13. Intent/action model result

`IActuationCommand`/`Ai.Act` are appropriate for asynchronous host effects, but not a complete deterministic world-action buffer. RTS `ShipAction` proves the needed contract but is domain-specific. Add one game-local typed `GameIntent` family and resolver; do not add it to Dominatus Core in M1 and do not create another event/command framework.

## 14. Player/AI controller equivalence

**Small adapter needed.** A human input adapter and a Dominatus decision adapter can both emit the same `GameIntent`. Current APIs permit this, but no sample exposes a shared contract. Resolution must not know which controller originated an intent except where rules explicitly require actor identity.

## 15. Tick-phase result

The repeated evidence supports this game-local loop:

```text
input -> observe -> decide -> collect intents -> stable sort
      -> resolve/commit -> advance time -> deliver events -> project
```

Dominatus Core owns agent ticking, not the entire sequence. FishTank approximates it; TinyTown interleaves domain mutation; RTS makes it explicit. Keep the orchestrator game-local until another product demonstrates the same API need.

## 16. Tile/grid/pathfinding result

No reusable backend-neutral tile map, collision grid, or A* was found. RTS spatial grids are battle-sample-local; GodotTinyTown delegates to Godot navigation. A small deterministic game-local grid/A* with blocked occupancy, interaction range, and stable neighbor ordering is appropriate. No physics engine or new shared pathfinding package is justified.

## 17. Time/schedule result

`AiClock` and `WorldClock` provide elapsed/tick time. TinyTown's `WorkSchedule` and `CurrentTick % 24` demonstrate schedule scoring but use abstract hours/locations. The game needs a small persisted integer day/minute value and domain rules for opening, crop day transitions, and sleep. Dominatus receives the current time as observation; it does not own the calendar.

## 18. Persistence result

One farm save slot is possible without another persistence subsystem. Use `DominatusSave` as the container, a Dominatus checkpoint for runtime state, and one versioned game-domain chunk through `ISaveChunkContributor`. `GameSave` contains all authoritative world/domain data and the stable game-ID/AgentId mapping.

`BbJsonCodec` only supports bool, integer/floating primitives, string, and Guid and silently skips unsupported values. Therefore relationships, inventories, crops, and other domain records cannot rely on blackboard persistence. Save only at the RTS-style stable tick boundary.

## 19. Replay/determinism result

Checkpoint, restore, replay events, staged parallel execution, deterministic sample tests, cassette LLM responses, and RTS canonical hashing are reusable. The game must define its own canonical state serialization/hash and log external player/LLM inputs. This is enough for headless regression testing.

## 20. RNG result

FishTank's ordinary unseeded `Random` is unsuitable. Replay has an RNG-seed event but no serializable RNG state service. Use a tiny explicit deterministic PRNG in the game domain and persist seed plus state/draw counter. Do not build a shared RNG framework in M1.

## 21. Copeland/TSON data-authoring result

Copeland nominal records, enums, arrays, record tables, and TSON fit static crop, item, shop, NPC-profile, and schedule data. Use them when the C# loading/validation adapter is cheaper than hardcoded fixtures. They are useful now for authored data and build validation, interesting later for behavior/shader authoring, and not required for M1 logic.

## 22. SDSL-V readiness

**Partially ready, not ready for a textured 2D sprite shader.** Parser/validation/HLSL/DXC/SPIR-V artifacts work for the smoke triangle. `src/Aurelian/Aurelian.Shaders/Language/Artifacts/SdslvSpirv/SdslvStageExtraction.cs` recognizes the exact `VSMain`/`PSMain` profiles; `src/Aurelian/Aurelian.Shaders/Language/VdMir/Lowering/VdMirM0Lowerer.cs` intentionally accepts the smoke-triangle shapes and Position/SvPosition/Color0 semantics. Texture/sampler resources, UV semantics, material fields, and general entry shapes are blockers. No shader work belongs in this milestone.

## 23. Machina.UI readiness

**Ready with minor missing widgets.** Stack/Grid/Layer/Placement/Rect/RichText/Scroll plus Button, Card, Badge, Label, TextBlock, Field, Input, Checkbox, Switch, and Separator can express HUD, toolbelt, inventory/shop grids, dialogue, pause menus, and overlays. Purpose-built icon/slot widgets and richer focus/editing are ergonomic gaps, not engine blockers. Machina input includes pointer, keys, text, resize, and close.

## 24. Machina/render-host integration result

**Partial.** `src/Machina.UI/Machina.Presentation/Screens/ScreenLayers.cs` defines Background, World, Hud, Overlay, Modal, Debug, and Cursor ordering. `PresenterScreenStack.cs` orders metadata, while `src/Integrations/Aurelian.Machina/MachinaPresentationTranslator.cs` converts a Machina frame into Aurelian CPU rectangle/text operations. There is no composed world+HUD surface, texture-upload bridge, or unified game/UI input and focus routing.

The smallest later seam is one host adapter that owns an ordered input batch, sends UI events/hit testing first, forwards unhandled events to the player controller, and composites an immutable world presentation with the Machina frame.

## 25. Oblivion inspector readiness

Existing `OblivionCardKind` values in `src/Oblivion/Oblivion.Model/OblivionCardModel.cs` cover the need: Table for NPC/world/schedule/relationships, Markdown for logs/summaries, Function for tests and invocations, and Diagram for state/flow views. Decision scores can be Table or Markdown. No `GameInspectorCard` is justified. A projection/materialization adapter is needed; Oblivion remains a read-only development consumer.

## 26. Oblivion runtime transport result

Oblivion has structured vault persistence, exact artifact references, atomic reload commands, CLI JSON/TSON formatting, and Function invocation. It has no general live game IPC. The smallest MVP inspector path is a compact snapshot file written by the game/tooling and materialized into existing Table/Markdown cards, then loaded/reloaded normally. Same-process hosting couples lifetimes; IPC is unjustified until live latency is proven necessary.

## 27. Marionette reusable infrastructure result

`src/Marionette/Marionette.Core/LegacyAgentImportContracts.cs` and `SkyrimWorldContracts.cs` encode Skyrim actor origin, save/timeline data, body candidates, and deterministic imported-agent IDs. The `src/Skyrim/Aurelian.Marionette` app owns wire/scenario glue. Classification: contracts are `SKYRIM_SPECIFIC` or `MARIONETTE_SPECIFIC`; deterministic ID hashing is `COPY_PATTERN_ONLY`; nothing is `GENERIC_GAME_INFRA` or `SHOULD_MOVE_TO_SHARED` now.

## 28. Stable identity result

No universal ID exists without wrong ownership. Use typed deterministic game-local IDs for player, NPC, plot, and world object, serialized as compact stable values. Maintain explicit maps to Dominatus `AgentId` and presentation IDs. Do not use Aurelian scene IDs as game truth or generate unrelated GUIDs for every concept.

## 29. Asset/audio/input/camera result

Aurelian Assets currently centers on shader manifests; Raster lacks sprites; Vulkan resource mechanisms are not a qualified sprite path. Dominatus Godot connectors load sprite/audio artifacts; MonoGame samples use framework APIs but provide no shared content pipeline. No cross-backend texture/font/audio hot-reload path exists. The temporary host should load logical texture/font/audio/data IDs with backend-local files. Audio is optional and may use MonoGame `Song`/`SoundEffect` locally.

Keyboard and mouse are sufficient. FishTank proves direct MonoGame input; Machina provides UI input contracts. Controller support is deferred. Aurelian `RenderCamera2D` supplies basic transform/viewport data but no follow/bounds behavior; a simple camera projection belongs in the adapter. Tile occupancy and interaction range belong in Core.

## 30. Farming-specific missing work

| Feature | Existing support | Result |
| --- | --- | --- |
| soil plot/crop state | typed records/TSON can describe data | game-specific state missing |
| hoe/seed/water/harvest | game-local typed intents can follow RTS buffering | rules/resolution missing |
| growth | clock/tick exists | day-transition rule missing |
| inventory transfer/sale | deterministic resolver pattern exists | domain model and rules missing |

All are expected game logic, not missing engine infrastructure.

## 31. Economy-specific missing work

Money, seed/crop prices, shop inventory, buy, and sell have no reusable model. TSON can author catalog/price data, and the intent resolver can validate and commit trades. Keep the ledger and rules game-specific; a generic economy framework has no second consumer.

## 32. NPC spatial-schedule missing work

TinyTown is close in needs and utility scheduling but uses abstract string locations. GodotTinyTown adds physical travel through Godot navigation, which is the wrong owner for headless truth. The game needs scheduled destination selection plus deterministic grid paths and occupancy rules. Wake/work/eat-socialize/home/sleep are game data and policies over persisted time.

## 33. Dialogue/relationship result

TinyTown already proves deterministic fallback, optional cassette-backed LLM calls, bounded structured parsing, clamped relationship effects, memory append, and tests. Reuse those patterns without Dominatus Core changes. Relationship and memory types stay in the game domain because their semantics are content-specific. M1 excludes live/optional LLM calls.

## 34. Headless-mode readiness

**Supported with small composition glue.** TinyTown and RTSBenchmark already run headlessly, Dominatus Core has no renderer dependency, and NullRenderer exists for render-contract tests. The proposed dependency boundaries make the renderer absent by construction. The game projects presentation only after a committed tick.

## 35. Renderer-swap readiness

For MonoGame-to-Aurelian replacement, Core/Runtime must not expose `Game`, `SpriteBatch`, `Texture2D`, XNA vectors/colors/input, Godot nodes/vectors, Vulkan handles, or frame timing. Logical asset IDs, numeric transforms/source cells, camera data, and draw order belong in an immutable game presentation snapshot. Backend adapters own resource caches, viewport conversion, batching, and input translation.

## 36. Shared-infrastructure candidates

| Candidate | Classification | Reason |
| --- | --- | --- |
| agent lifecycle/events/checkpoint | `ALREADY_EXISTS` | Dominatus Core is used across all samples |
| typed world intent buffer/resolver | `GAME_LOCAL_FIRST` | explicit in RTS, implicit in FishTank/TinyTown; domains differ |
| renderer-neutral snapshot | `ALREADY_EXISTS` | Aurelian `RenderSnapshot`, though sprite realization is partial |
| controller source | `GAME_LOCAL_FIRST` | player and AI can converge, but no repeated stable API exists |
| game tick orchestration | `GAME_LOCAL_FIRST` | repeated phases exist but ownership/order differs by domain |

No new shared abstraction passes all three tests: existing APIs insufficient, two current consumers need the same shape, and game-local code is inadequate. Therefore M0 proposes none.

## 37. Duplication traps

Do not create a new AI lifecycle, utility system, event bus, actuation host, persistence file format, replay log, UI toolkit, inspector/card kind, compiler data schema, renderer super-interface, ECS, generic pathfinder, economy framework, relationship framework, or calendar framework. Compose Dominatus save chunks, Machina controls, Oblivion cards, Copeland/TSON, and the existing Aurelian projection contract.

## 38. Missing-piece table

The full machine-readable table is `artifacts/aurelian-game-m0/capability-matrix.json`.

| Capability | Existing owner | Status | Reuse directly? | Missing glue |
| --- | --- | --- | --- | --- |
| game loop | Dominatus + sample hosts | `FOUND/PARTIAL` | tick yes | game phase orchestrator |
| world state | none suitable | `MISSING` | no | game `GameState` |
| entity identity | several scoped IDs | `PARTIAL` | map explicitly | typed game IDs |
| agent AI/utility | Dominatus | `FOUND/PRODUCTION_READY` | yes | observation/intent adapter |
| events/messages | Dominatus | `FOUND/PRODUCTION_READY` | yes | domain event types |
| intent resolution | RTS sample | `PARTIAL/DEMO_ONLY` | pattern only | game intent/resolver |
| time/schedules | clocks + TinyTown | `PARTIAL` | clocks/pattern | game calendar/spatial schedule |
| grid/pathfinding | Godot/sample-local | `MISSING` | no | small deterministic grid/A* |
| persistence/replay | Dominatus | `FOUND/PARTIAL` | container/checkpoint yes | game chunk/stable boundary |
| RNG | replay seed only | `PARTIAL` | seed event | serializable game PRNG |
| inventory/economy/farming | none | `MISSING` | no | game-specific rules |
| relationships/dialogue | TinyTown | `DEMO_ONLY/PARTIAL` | patterns | game types/content |
| rendering/camera/input | Aurelian partial; MonoGame demos | `PARTIAL` | temporary host pattern | presentation/input adapters |
| UI | Machina | `FOUND/PARTIAL` | controls/layout yes | host composition, slot ergonomics |
| inspection | Oblivion | `FOUND/PARTIAL` | cards/reload yes | snapshot materializer |
| data authoring | Copeland/TSON | `FOUND/PRODUCTION_READY` | yes | optional C# adapter |
| testing/headless | xUnit/Dominatus demos | `FOUND/PARTIAL` | harness patterns | product composition tests |

## 39. Risk register

| Rank | Risk | Impact | Likelihood | MVP blocker |
| --- | --- | --- | --- | --- |
| 1 | blackboards become world truth | high | high | yes |
| 2 | MonoGame types leak into semantics | high | medium | yes |
| 3 | Dominatus checkpoint is mistaken for complete save | high | medium | yes |
| 4 | native Aurelian sprite work expands MVP | high | high | no with temporary host |
| 5 | Machina surface/input composition is absent | medium | high | no for headless M1 |
| 6 | framework navigation contaminates deterministic truth | medium | medium | no |
| 7 | Oblivion becomes product truth | medium | medium | no |
| 8 | unseeded/framework RNG breaks replay | medium | medium | yes |

Controls and exact wording are in `artifacts/aurelian-game-m0/integration-risks.json`.

## 40. Artifact-budget CI result

`.github/workflows/artifact-budget.yml` runs `tools/Test-ArtifactBudget.ps1` against files newly added relative to the PR/push base. The guard rejects `artifacts/m*` milestone bundle paths, more than 16 unlisted files in one artifact root, files over 256 KiB, and non-compact generated media/output. Compact JSON/text formats and small paths named golden/goldens/fixtures remain eligible. `.github/artifact-budget-allowlist.txt` provides narrow reviewed wildcard exceptions. It intentionally does not retroactively condemn historical tracked release assets.

## 41. Recommended language

**`C# GAME LOGIC`.** Dominatus, MonoGame samples, Aurelian, Machina, and persistence APIs are C#-native. Copeland/TSON is useful for authored data; moving runtime logic to Copeland TS adds an unproven boundary without solving an MVP gap.

## 42. Recommended renderer

**`USE_MONOGAME_TEMPORARILY`.** It is the smallest proven current window/input/render host and can be kept behind an immutable projection. Aurelian is the eventual target once its sprite subset is separately qualified. Godot is not selected because the current life-sim sample couples domain lifecycle/navigation/presentation to engine nodes.

## 43. Proposed project ownership/layout

```text
src/Games/TinyFarm/TinyFarm.Core
src/Games/TinyFarm/TinyFarm.Runtime
src/Games/TinyFarm/TinyFarm.MonoGame
tests/Games/TinyFarm.Tests
```

The game is independent. Core owns truth; Runtime composes Dominatus/save/projections; MonoGame is a leaf adapter; tests are headless. Future Aurelian, Machina, and Oblivion adapters are siblings, not parents.

## 44. Exact first implementation milestone

**TINY-FARM-M1 — Headless deterministic week.** Create Core, Runtime, and Tests only. Prove one scripted seven-day simulation with a small grid, player, four Dominatus NPCs, four crop definitions, one shop, shared typed player/AI intents, deterministic resolution, and one save/reload boundary. Acceptance is identical uninterrupted/restored canonical hashes plus deterministic schedules, crop changes, and buy/sell changes. Exclude renderer, UI, art/audio, LLM calls, Oblivion, Aurelian sprite work, and shared extraction.

## 45. Git diff stat

The command and final output are recorded in `artifacts/aurelian-game-m0/aurelian-game-m0-manifest.txt` after validation. Only audit documents, compact audit artifacts, and the small CI guard belong in this milestone.
