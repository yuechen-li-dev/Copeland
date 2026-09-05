# AURELIAN-GAME-SUBSTRATE-AUDIT-M0

## Decision

**Outcome B: the missing major layer is a coherent native 2D world-presentation kit.**

The semantic architecture is viable. A small action/farming game can reuse substantial work, but today its author must assemble camera projection, sprite playback, atlas/resource lifetime, painter ordering and the world/UI/host connection. This is more than a few convenience methods. It does **not** require an ECS, a new renderer, or a replacement decision kernel. Collision queries and game audio are separate important gaps after this first layer.

M0 audit outcome: **Success**. An executable integrated sketch and fresh regression/native checks support the ranked answer below. This does not declare the engine game-ready or the sketch a playable native game.

Audited Copeland base: `cae570daef8c067cc2c67f44ea4361ceef21609e`. At the original audit boundary, both the embedded reference and standalone Dominatus checkout were clean at `adbecd91cf1e07ca9a53c60a38fbb8356245b076`. M7a later advanced standalone Dominatus master, and M7b2 removed the embedded checkout. Active source-level integrations now resolve the standalone sibling repository; package-only consumers retain centrally pinned packages. Source availability, package availability and Aurelian integration remain distinct claims.

## 1. Engine definition and target

Aurelian is the reusable semantic application/world runtime over .NET 10: explicit host execution, world realization, actuation, rendering contracts/backends and integration kits. Dominatus owns decisions, policy and flow. Machina owns semantic UI, layout and interaction. Copeland/TSON owns compiled program/table meaning. Current implementation is stronger at systems foundations than application-facing game kits; the architecture definition is a direction with qualified portions, not a claim that TinyFarm-local infrastructure is already an engine API.

The benchmark is a small, polished, top-down LTTP-like slice: walking between rooms; one sword enemy; plots with planting/watering/growth/harvest; pickups and inventory/hotbar; scheduled NPCs; a branching conversation and simple quest; world time; small effects and sound; save/load. Modest tile maps, opaque and transparent sprites, stable painter order and deterministic semantic actions are sufficient. No open-world streaming, rigid-body simulation, cinematic editor or giant entity framework is required.

Preserved law:

```text
human / Dominatus / LLM / replay
    -> typed semantic intent
    -> application resolver and authoritative state
    -> world / UI / animation / audio / debug projections
```

Camera and animation have explicit presentation state only. Animation events may cue sound/VFX; damage and invulnerability must use authoritative simulation time. Scene caches, GPU handles and UI focus are not save-game truth.

## 2. Classification map

Letters apply to the named capability, not every possible feature in its category: **A** already solved/reusable; **B** emergent, formalize with a compatible consumer; **C** missing engine primitive; **D** app-specific; **E** defer. A local TinyFarm implementation earns B, not A for new games.

| # | Category | Class | Existing evidence and remaining work |
|---|---|---|---|
| 1 | Simulation host/clocks | A cadence; A kernel ticking | M5 `CadenceScheduler` now separates accepted/discarded host ticks, ordered rational cadences, pause/normal/fast-forward, explicit tie order, inspection, and stable configuration identity. TinyFarm and an independent 30/5/2 Hz consumer prove partition invariance. Game-minute meaning and resolver calls remain application-owned. |
| 2 | Scene/world runtime | A catalog/transition mechanism; A world stores | M5 adds generic scene/anchor/route validation and post-accept transition/resource/camera handoff. Active/coarse is a fact only; TinyFarm retains entities, scene objects, transition permission, and coarse policy. |
| 3 | Camera | B simple follow; C full kit | `RenderCamera2D` is a snapshot record; default extraction uses identity. TinyFarm `DrawWorld` has local clamp arithmetic. No native reusable follow/dead-zone/smoothing/zoom/shake/room-snap/override controller found. Start with explicit position/viewport/bounds/zoom and Follow/Snap; add overrides and shake only after the native path works. |
| 4 | 2D world rendering | A primitives; C integrated kit | Native ordered textured quads, MSDF and analytic SDF exist. `NativeQuadSubmission` takes pixel rectangle/UV/texture/tint. No typed world sprite/tile/camera lowering seam. Textured mode is opaque; `StraightAlphaBlend` is true only for MSDF/analytic modes. Need sprite transparency, pivots, camera conversion, layer/Y/stable-ID ordering, tile submission and lifetime. |
| 5 | Sprite animation | A atlas metadata; B/C playback | Dominatus SpriteForge has frames, grids, pivots, FPS and loop flags, and resolves animation frame rectangles. Godot connector delegates playback to Godot. No Aurelian clip sampler/controller bridge found. Reuse metadata; add deterministic elapsed-time sampling, loop/once/end, directional selection and presentation events with explicit restart rules. |
| 6 | Collision/physics | A reusable query substrate | M3 adds dependency-free `Aurelian.Spatial2D`: AABB/circle overlap, point queries, continuous sweep, bounded multi-contact slide, trigger enter/stay/exit, typed masks, transient volumes, stable ordering, and debug facts. TinyFarm continuous movement now queries this layer inside its authoritative resolver with blocked/unblocked parity. It returns facts and accepted displacement; it owns no game state or rigid-body simulation. Raycast and capsule remain deferred for demonstrated pressure. |
| 7 | Input | A focus/capture; B/C device bridge | Aurelian layers route pointer/key/text with focus/capture/consumption; TinyFarm emits intents. `LayerKey` is a small concrete list, no gamepad axis/button record. Need device-neutral actions, edge/held samples, gamepad and lost-focus release, plus world suppression when UI captures input. Bindings remain app policy. |
| 8 | Particles/VFX | A bounded small-game substrate | M8 adds typed semantic effect events/IDs, six immutable definitions, deterministic CPU bursts/ambient/trail, dedupe, expiry, bounded reject-newest capacity, inspection, explicit world/screen transforms, analytic particle/flash realization, and TinyFarm accepted-event projection. No gameplay authority, serialized particles, physics, editor, timeline, lighting, or post-processing graph was added. |
| 9 | Shader/effects | A compiler and first effect material path | `SoftShockwave.v.ts` is compiled Copeland GPU profile -> VD-MIR -> HLSL -> validated SPIR-V and realized by the existing Vulkan ordered-quad renderer. Its typed material carries age/lifetime/radius/thickness/intensity/seed. Stable hashes and a negative managed-allocation diagnostic are recorded. Additive blend and broader post effects remain deferred because straight alpha satisfies the qualified slice. |
| 10 | Audio | A resident playback; A generation; B streaming/Linux | M4 adds `Aurelian.Audio` typed resources/cues, bounded voices, buses, volume/mute, fades/crossfades, music/ambient policy, spatial pan/attenuation, dedupe, completion, null/offline mixing, host ownership, a Windows NAudio leaf, and TinyFarm accepted-result projection. `Dominatus.Actuators.Audio` remains generation authority behind a narrow artifact adapter. Long-resource streaming, compressed decoding, and Linux device output remain bounded seams. |
| 11 | Dialogue | A semantic flow and neutral presentation projection | M7b2 proves the same `DialoguePresentationSnapshot` in the VN demo and an in-world TinyFarm lower-third. Shared facts are stable operation/speaker/content/choice/pending identities; portrait/background/auto/skip/save controls remain skin or host policy. Typed effects return through the TinyFarm resolver. No new dialogue language, scheduler, voice, quest, relationship, or camera system. |
| 12 | Interaction | B law; D priorities | TinyFarm finds forward/range candidates and orders by category, distance, ordinal stable ID, then resolves contextual intent. Share stable selection/comparison only after compatible candidate consumers exist. Keep crop/shop/enemy target construction and intent mapping local. Do not export its nullable all-target record as universal engine API. |
| 13 | Combat | D rules; C spatial/timing primitives | M21 is selected/direct Sword intent -> one-hit Slime defeat with rejection/parity. Not damage/knockback/invulnerability framework. Reuse future queries and clocks; second game must demonstrate shared health/status law before extracting combat. |
| 14 | Farming | D | Plant, water, growth, harvest and day progression exist in TinyFarm. Specific turnips, yields, watering rules and future seasons stay local. Reuse world-time scheduling; no farming engine module. |
| 15 | Inventory/hotbar | B narrow laws; D identity rules | TinyFarm has selected slots, identity-item ownership, product stacks and concrete selected-use intent. Stable slot selection/binding and checked stack arithmetic may be shared; do not unify unique tools and fungible products into one giant container abstraction. Current projector requires a hotbar-capable state version. |
| 16 | Assets | A specialist pipelines; B/C game resource bridge | Shader manifests/hashes/validation, Machina fonts/vector atlases, TSON tables and SpriteForge metadata exist. No unified sprite/audio asset lifetime or packaged scene dependency closure. Reuse IDs/manifest law with typed sprite/audio IDs, content hash, dependencies and explicit resource scope; do not introduce an asset database. |
| 17 | Persistence | A container; B application envelope | Dominatus `SaveFile`, chunks and checkpoints plus TinyFarm versioned DTO validation/hash/load already exist. Slot paths, atomic replacement, migrations and definition compatibility need a game-facing bridge. Keep serializers/migrations/domain hash in their owner. Generic `Save<T>` alone would not remove this work. |
| 18 | Replay | A kernel replay; B game tape | TinyFarm semantic envelopes and hash proofs exist; Dominatus replay reconstructs external/actuation inputs around checkpoints. Its `RngSeed` event is documented as no-op pending wiring, so do not call it universal game determinism. Formalize ordered game-intent tape, seed/definition/rate versions and mismatch diagnostics alongside save envelopes. |
| 19 | Navigation | A algorithm; A coordination bridge | TinyFarm's single `DotRecastNavigationPlanner` builds/caches scene walkable geometry and proposes paths. M5 adds stable goal/request identity plus arrival, unavailable, blocked, replan, and interruption facts. Spatial2D/application resolver still accepts motion; Aurelian never mutates positions. |
| 20 | NPC scheduling | A decision kernel; A deterministic bridge | M5 extracts deterministic matching and schedule-to-application-goal values. Required/Open, utility, energy, jobs, recurrence meaning, TSON content, and persistent Dominatus policy remain TinyFarm-owned. |
| 21 | UI | A core; B game adapters | Machina authoring/layout/hit-test/prepared presentation and Aurelian layer integration cover HUD, inventory, menus, tooltip and quest/dialogue content composition. TinyFarm overlay is real. Gamepad focus traversal and dialogue completion mapping need adapters. Raster bitmap fallback has visible truncation/substitution in the probe; this does not invalidate native MSDF capability or qualify final game typography. |
| 22 | Debug/inspection | A evidence machinery; B runtime view | Semantic/frame hashes, intents/results, scenario JSON, navigation traces, shader manifests and GPU metrics exist. Dominatus has debug overlay/checkpoints. Need one bounded frame inspection DTO for scene/actors/clock plus camera/animation/collision/nav/resource facts as kits appear. No full Oblivion integration now. |
| 23 | World authoring | A typed tables; B sprite/map link; E editor | TSON scene/layout/anchor/route/schedule validation is sufficient for a small authored slice. SpriteForge TOML is already a specialist sprite contract. Visual tile placement, collision/portal preview and pivot preview could save time, but a read-only preview comes before an editor. No new general scene DSL. |
| 24 | Host/bootstrap | A loop/window mechanisms; B game bootstrap | Engine, runtime session, frame loop/pump and Silk/Vulkan presenter exist. VisibleTriangle still assembles sample graphics, screen stack and policy bridge; TinyFarm uses MonoGame. Need a bounded native game host owning resource disposal/resize/input/save-root/configuration, with explicit simulation callbacks. Avoid lifecycle-driven game truth. |
| 25 | Deployment | A local Windows evidence; B Linux qualification; E web | net10.0 projects and Silk/Vulkan are usable building blocks. Windows native execution was rerun here. Linux must qualify native library packaging, font/assets/case-sensitive paths, Vulkan presentation and audio on Linux; not proven by this Windows audit. Web is a separate renderer/runtime target, not a free consequence of Copeland JS output. Consoles/mobile deferred. |

## 3. Concrete pressure test

Source: `tests/TinyFarm/TinyFarm.Core.Tests/GameSubstrateAuditM0Tests.cs`.

The probe uses M21 content/state, places initial player/Mara fixture data in Farm, then uses **only `TinyFarmResolver.Resolve` for gameplay**. It accepts an `InteractIntent` and verifies a Mara conversation, rejects movement into `farm-tree` without changing semantic hash, accepts a small move away, projects both actors, follows the player, animates a nearby authored world object with two presentation frames, and composites the existing Machina HUD/hotbar through `MachinaPresentationTranslator` and the real Aurelian CPU raster renderer. A compositor key event produces a typed slot-selection DTO. The test verifies repeatable raster output, different animation pixels, and no gameplay mutation from rendering.

Measured facts from the successful run:

| Fact | Result |
|---|---|
| Interaction | Accepted; Conversation targets Mara |
| Collision | MovementBlocked; unchanged state hash |
| Camera X before/after move | 220.125 -> 216.125 projected pixels |
| Animated authored object | `farm-tree`; presentation frame 0/1 |
| UI | 38 real Machina-lowered operations; slot 3 command |
| State hash after projection | `f36302545c681999182057160094502f6c216697f5d0cc4139efd928737ddba8` |
| Frame 0 PPM SHA-256 | `A4D3F23257E613F4D220E462D35A2204BC38D7386D429B0A7DFEDE9B4D71EB11` |
| Frame 1 PPM SHA-256 | `3F5090AB60A7EA5D8639EB6CF131868F89168CF2CFC14CB7473AC8DAB60F7092` |

This original audit sketch deliberately used colored rectangles and a color-frame animation. Subsequent M1 and native-layer-compositor qualifications now prove atlas texture playback, straight-alpha sprites, camera projection, and native world/UI same-target composition. Gamepad and a playable native host remain unqualified.

Two concrete reconstruction hazards appeared while building it:

1. A continuous-scene M5 state cannot feed the existing hotbar projector: `Player UI projection requires TinyFarm player-hotbar state.` The fixture must use the real M21 state, not arbitrary similarly named DTOs.
2. Actor projection coordinates are fixed world units; scene object rectangles are tile units. A first render mixed these and produced identical animation images because the selected object was off-screen. Explicit conversion and selecting a visible nearby authored object fixed the probe. This motivates typed world/pixel/UV boundaries in the next kit.

PNG previews were decoded from the actual PPM output and visually inspected. The NPC/player and wall are visible, with the actual hotbar at the bottom. The bitmap UI clips long labels and substitutes unsupported symbols; final typography should use the qualified Machina text path. The probe makes no polished-UI claim.

## 4. Dominatus reuse and qualification

Use these existing seams before adding engine machinery:

- **Ariadne.OptFlow**: `Diag`, line/ask/choose commands, operation identity and flow waits. `Ariadne.Console/Scripts/DemoDialogue.cs` demonstrates branching. `Dominatus.StrideConn/Actuation/Dialogue` demonstrates the surface/completion adapter shape. Port the adapter boundary, not Stride dependencies.
- **Dominatus.Core**: policy/HFSM/utility, typed actuation, mail/events, `AiClock`, checkpoint chunks and replay. Do not reinterpret a float AI clock as TinyFarm's integer multi-rate world clock.
- **Dominatus.SpriteForge**: atlas/grid/absolute-frame metadata, pivots, FPS/loop, resolver and validation. `Dominatus.GodotConn` shows consuming sprite metadata and engine-specific animation/navigation/audio handlers. Reuse asset semantics; do not import Godot scene objects into Aurelian.
- **Dominatus.Assets.Toml**: asset identity, load diagnostics/source spans and pack loading. Aurelian shader identity is a separate specialist contract; a game manifest should compose these rather than replace them all.
- **Dominatus.MonoGameConn**: game-time/agent component/debug overlay patterns; these do not provide a native Aurelian host or sprite mixer.
- **Dominatus.Actuators.Audio**: useful offline/provider-generated audio artifacts, not SFX/music playback buses.

Fresh SpriteForge validation found a reuse qualification blocker. Running its vendored test project normally hits **NU1008** because Copeland central package management reaches a project with explicit package versions. Running with `-p:ManagePackageVersionsCentrally=false` builds, then yields **2 passed / 6 failed**. The failures report `toml.parse: Invalid \r not followed by \n`. The test fixture writer uses `content.Replace("\n", Environment.NewLine)`; on this CRLF checkout that duplicates CR characters. The hermetic fixture repeats the same operation. This is a concrete test-fixture portability problem, not evidence that the atlas model should be discarded or a new schema invented. Repair and qualify in the owning Dominatus lane before claiming ready-to-import loading. No submodule or standalone source was changed in this audit.

## 5. LLM reconstruction audit

Complexity is an engineering estimate of implementation plus qualification, not measured hours. S = small helper/adaptation, M = several interacting contracts, L = platform/lifetime or spatial work. “Engine?” identifies shared machinery; content/rules remain local.

| Task | Reconstructed glue today | Complexity | Engine? |
|---|---|---|---|
| Boot native game | presenter/frame loop, resize, resource disposal, simulation bridge | L | Yes |
| Move player | device samples -> typed intent, cadence connection | M | Yes bridge; app movement rules |
| Follow player | world/pixel conversion, clamp, viewport, follow state | M | Yes |
| Show sprites/tiles | atlas rect/pivot/UV, texture scope, alpha, layer/Y ordering | L | Yes |
| Animate objects | elapsed clock, loop/once/restart/direction, selected frame | M | Yes playback; app selection |
| Change rooms | validated routes/anchors, resource scopes, snap and inactive behavior | M | Yes mechanism; app transitions |
| Block movement/attack overlap | reusable shapes/queries, sweep/slide, stable contact ordering | L | Yes; app resolver decides |
| NPC routine | schedule observations -> goal -> path -> arrived/failed | M | Yes bridge; app schedule content |
| Contextual interaction | candidate collection and priority -> specific intent | M | Shared stable ordering only |
| Sword/slime | attack phase, damage, defeat, invulnerability/knockback rules | M | No framework; reuse queries/time |
| Farm plots | planting/watering/day growth/yield | M | No |
| Inventory/hotbar | stack checks, ownership, slot bindings and selected use | M | Narrow slot/stack mechanics only |
| Conversation | Ariadne completion <-> Machina choices, portraits and cues | M | Yes adapter; app branches/content |
| Quest | concrete progress state and resolver events | S/M | No |
| Dust/rain/hit burst | emitter capacity, spawn/lifetime, rendering and seed | M/L | Yes |
| SFX/music | decode, voice handles, buses, looping/fade/stream/disposal | L | Yes |
| Hit flash/day tint | semantic parameters -> compiled shader resource binding | M | Yes |
| Save slot | paths, atomic IO, version/definition compatibility, migration | M | Yes envelope; app codec |
| Replay | ordered intents/seed/rate metadata, restore and mismatch trace | M | Yes tape; app resolver/hash |
| HUD/menu | compose Machina plus data/capture adapter | S | Existing UI; app layout |
| Inspect game | stable IDs and frame facts joining existing traces | S/M | Yes kit diagnostics |
| Ship Linux | native dependency/asset closure and live qualification | M/L | Yes host/package lane |

The probe itself needed four local mechanics: camera projection, world rectangle lowering/unit conversion, painter ordering, and a two-frame presentation clock. An alias like `GameEngine<T>` would remove none of these responsibilities.

## 6. Priority ranking

Scores: 1 low, 5 high. LLM pain includes repeated correctness reasoning; reuse includes frequency and glue saved. Cost is relative effort including proof, not library size. Priority 1 is highest. Architectural clarity favors a projection/adapter with one owner over shared gameplay frameworks.

| Gap | Status | LLM pain | Reuse value | Cost | Priority |
|---|---|---:|---:|---:|---:|
| Native sprite/world presentation, alpha, camera, atlas lifetime | C with A primitives | 5 | 5 | 4 | 1 |
| SpriteForge loader qualification + playback bridge | B/C | 4 | 5 | 2 | 1 |
| Game input/native bootstrap | B/C | 5 | 5 | 3 | 2 |
| Shape query + static collision sweep/slide | C | 5 | 5 | 4 | 3 |
| Native game audio | C | 5 | 5 | 4 | 4 |
| Ordered multi-rate clock/scene/nav/schedule bridge | B | 4 | 5 | 3 | 5 |
| Save-slot/replay envelope bridge | B | 4 | 5 | 3 | 6 |
| Ariadne neutral dialogue projection | A | 1 | 4 | complete | qualified M7b2 |
| Parameterized sprite effects and small emitters | C | 4 | 4 | 3 | 8 |
| Inventory slot/stack conveniences | B/D | 2 | 3 | 2 | 9 |
| Unified frame inspection | B | 3 | 4 | 2 | Incremental with each kit |
| Linux packaging qualification | B | 3 | 4 | 3 | 10 |
| General combat/farming frameworks or full editor | D/E | 1 | 1 | 5 | Defer |

Audio ranks early despite not blocking a silent probe: a polished game cannot substitute TTS generation for SFX/music. Collision precedes combat expansion because an HP abstraction does not solve hit regions or walls.

## 7. Emergent contracts and changes made

Formalized **in this architecture report**, not published as APIs:

- World view uses explicit world coordinates; camera produces pixel coordinates; atlas resolver produces source rectangles/UVs. Conversion happens once in a reusable presentation adapter.
- Sprite clip sampling observes presentation time and gameplay-selected intent; it never changes gameplay state or decides hits.
- Multi-rate cadence produces ordered due-work boundaries with explicit accepted/discarded time; the game chooses world-minute meaning and resolver calls.
- Dialogue surface emits typed completions to existing Ariadne flow; actor/camera cues use typed actuation with correlated completion.
- Save/replay outer envelopes compose domain codecs, definition identity and intent versions rather than serializing renderer objects.

Code changes: one executable integration test and two test-project references for the real raster/Machina path. No production API, shared kit, TinyFarm semantics, dependency versions or rendering behavior changed. A second probe using TinyFarm is **not** a second independent game proving compatible camera/scene/save law. Extracting a camera now would freeze units and integration policy before qualifying the native consumer. The smallest useful M0 change is therefore evidence and a precise next kit boundary, not a speculative API.

Explicitly local: crops/seasons/yields, recipes, tools/unique ownership, enemy HP/damage/invulnerability policy, quests, target categories/priorities, schedule content, NPC energy, route authorization and scene activation policy.

Explicitly deferred: physics dynamics, capsule/polygon without a caller, general combat framework, universal inventory model, particle graph/editor, asset database, ECS/lifecycle graph, full Oblivion, giant world editor, open-world streaming, advanced postprocessing until simple effects work, networking, consoles/mobile and web port.

## 8. Milestone sequence and acceptance gates

1. **Qualified:** `AURELIAN-NATIVE-GAME-WORLD-2D-M1` plus `AURELIAN-NATIVE-LAYER-COMPOSITOR-M0` now provide the typed camera/sprite world and real Machina analytic/MSDF overlay in one compositor-owned native target. Direct compatible passes use one clear followed by loads, with no intermediate color surfaces.
2. **Next: AURELIAN-GAME-HOST-INPUT-M2** — make that same scene playable through reusable native host/bootstrap and keyboard/gamepad action sampling, UI capture, focus-loss release and clean resource shutdown. Configuration/save-root is explicit. Game code contains state/intents/content and projections, not Vulkan setup.
3. **AURELIAN-SPATIAL-2D-M3** — AABB/circle overlap plus static-map sweep/slide and trigger transitions, deterministic contact ties. Qualify no tunneling, corners, overlap, rejected movement and replay. Add hit-region/knockback use in a concrete game resolver, not a combat framework.
4. **AURELIAN-GAME-AUDIO-M4** — typed event playback, bounded voices, music/ambient loop, buses, volume, fade/crossfade and simple positional attenuation; qualify stop/disposal and replay event duplication policy.
5. **AURELIAN-SIMULATION-SCENE-KIT-M5** — now a second host consumer exists: extract ordered cadence and scene/nav/schedule adapters with TinyFarm parity across host-delta schedules, pause, fast-forward and active/inactive scenes. No duplicated pathfinder.
6. **AURELIAN-GAME-SAVE-REPLAY-M6** — versioned game envelope/slot IO and intent tape around existing containers/domain codecs; prove save mid-action, reload, definition mismatch, migration and replay hash parity.
7. **Qualified: AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B2** — VN and TinyFarm consume one renderer-neutral snapshot with input capture, completion IDs, typed resolver consequence, save continuation, and replay parity. Portrait/background/layout remain skin.
8. **AURELIAN-GAME-FEEDBACK-M8** — hit flash, tint, fade and bounded dust/harvest/hit emitters on compiled shader paths; explicit budgets and presentation lifetime.
9. **AURELIAN-GAME-SLICE-QUALIFICATION-M9** — assemble combat/farming/NPC/inventory/quest content, inspect real UI and sound, package clean Windows build and qualify Linux separately. Extract inventory helpers only if actual repetition remains.

“Complete enough” means the final slice can be authored with these kits without camera/animation/collision/audio/save infrastructure in its game project. A test count alone does not meet that gate.

## 9. Validation and reproduction

SDK detected on PATH: **10.0.400**. Fresh local results:

- `dotnet build Aurelian.slnx --nologo -v minimal`: success, zero warnings/errors.
- `dotnet build TinyFarm.slnx --nologo -v minimal`: success, zero warnings/errors.
- `dotnet test Aurelian.slnx --no-build --nologo -v minimal`: **750 passed**, zero failed/skipped, 12 test assemblies.
- `dotnet test TinyFarm.slnx --nologo -v minimal`: **273 passed**, zero failed/skipped, including the new integration sketch.
- `dotnet run --project tools/Aurelian.Native2DQuadM1 --no-restore`: RTX 3070, validation enabled, zero errors, 100 repeated passes stable; canonical hash `4fedd1050accc442864faaa8eeae11c7d02efc6b1009a85d80d080c1372b1192`.
- SpriteForge net10.0 source tests with central package management disabled: **2 passed / 6 failed**, isolated fixture CRLF issue described above. No claim of full Dominatus regression validation.
- Native sprite alpha/camera/animated scene, audio, Linux and remote CI were not qualified by this audit.

Reproduce the integrated artifact (PowerShell from repository root):

```powershell
$env:AURELIAN_GAME_AUDIT_OUTPUT = Join-Path $PWD 'artifacts/aurelian-game-substrate-audit-m0'
dotnet test tests/TinyFarm/TinyFarm.Core.Tests --filter FullyQualifiedName~GameSubstrateAuditM0Tests --nologo -v minimal
```

Retained artifacts: `artifacts/aurelian-game-substrate-audit-m0/probe.json`, inspected `scene-0.png` / `scene-1.png`, `validation.json` and the SpriteForge failure log. The test reproduces JSON and PPM files only when the output environment variable is set; bulky PPM intermediates are not retained. Existing native golden artifacts remain unchanged in `artifacts/aurelian-native-2d-quad-m1`; incidental timing changes from the rerun were discarded. Detailed command logs for this run are under `.tmp/game-audit-*.log`.

## 10. Source index

All paths below are repository-relative unless explicitly marked standalone; they identify inspected implementations rather than a theoretical feature inventory.

| Area | Primary source |
|---|---|
| Architecture | `docs/Aurelian/aurelian-engine-architecture-v1.md` |
| Time | `src/TinyFarm/TinyFarm.Runtime/TinyFarmSimulationHost.cs`, `FixedMovementStepper.cs`; `src/Aurelian/Aurelian.World/WorldClock.cs` |
| Scene/collision | `src/TinyFarm/TinyFarm.Core/SceneModel.cs`, `TinyFarmResolver.cs` (`ResolveSpatialMoveCore`) |
| Camera/world extraction | `src/Aurelian/Aurelian.Rendering.Contracts/Snapshots/RenderCamera2D.cs`; `src/Aurelian/Aurelian.Runtime/Rendering/WorldRenderSnapshotExtractor.cs`; `src/TinyFarm/TinyFarm.MonoGame/TinyFarmGame.cs` |
| GPU boundary | `src/Aurelian/Aurelian.Graphics/Vulkan/Native2D/Native2DContracts.cs`, `VulkanOrderedQuadRenderer.cs`, `Native2DSubmissionValidator.cs` |
| World projection units | `src/TinyFarm/TinyFarm.Runtime/TinyFarmFrame.cs` |
| Input/UI | `src/Aurelian/Aurelian.Composition/InputContracts.cs`; `src/TinyFarm/TinyFarm.Presentation/TinyFarmMachinaUiLayer.cs`; `src/TinyFarm/TinyFarm.Runtime/TinyFarmHumanController.cs` |
| Interaction | `src/TinyFarm/TinyFarm.Core/TinyFarmSpatialQueries.cs` |
| Game assets | `src/Aurelian/Aurelian.Assets/AssetPipeline.cs`; `../Dominatus/src/Dominatus.SpriteForge/SpriteForgeAtlas.cs`, `SpriteForgeResolver.cs` |
| Effects | `src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts`, `MsdfText.v.ts`; native quad proof compiler path |
| Persistence/replay | `src/TinyFarm/TinyFarm.Runtime/TinyFarmPersistence.cs`, `TinyFarmSession.cs`; `../Dominatus/src/Dominatus.Core/Persistence/ReplayDriver.cs`, `SaveFile.cs` |
| Nav/schedule | `src/TinyFarm/TinyFarm.Runtime/TinyFarmNavigation.cs`, `TinyFarmNpcSchedule.cs`, `Content/tiny-farm-npc-schedules.obj.ts` |
| Dialogue/audio | `../Dominatus/src/Ariadne.OptFlow/DiagSteps.cs`; `../Dominatus/src/Ariadne.Console/Scripts/DemoDialogue.cs`; `../Dominatus/src/Dominatus.Actuators.Audio/AudioModels.cs` |
| Host | `samples/Integrations/Aurelian.VisibleTriangle/Program.cs`; `src/Aurelian/Aurelian.Core/Engine/Frames`; `src/Aurelian/Aurelian.Runtime/Sessions` |
| Regression evidence | `tests/TinyFarm/TinyFarm.Core.Tests/TinyFarmM5Tests.cs`, M13–M21 tests and compositor tests; `tools/Aurelian.Native2DQuadM1` |

**Exact next milestone: AURELIAN-GAME-HOST-INPUT-M2.** The native world and Machina UI now compose directly in one ordered target; the remaining bounded seam is host/window, swapchain target, normalized keyboard/gamepad input, UI capture/focus-loss behavior and deterministic shutdown.
