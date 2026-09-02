# TinyFarm M4 Scene Composition Report

## Outcome

**Outcome A — scene composition cleanly turns the M3 location diagram into a scalable, deterministic game world.**

The real path is now:

```text
keyboard or semantic LLM command
  -> existing GameIntent family
  -> TinyFarmSession.Step
  -> TinyFarmResolver
  -> TinyFarmState version 3
  -> immutable TinyFarmFrame
  -> MonoGame leaf
```

M1 and M2 states remain versions 1 and 2. Their canonical hash algorithms and scenarios are unchanged.

## M3 audit and M4 state

M3 stored only a typed `LocationId` on each `ActorState`. Its apparent positions were fixed coordinates in `TinyFarmFrameProjector`; arrow keys selected a neighboring location and therefore movement was semantic teleportation. Farm plots stored a coarse `LocationId`, interactions checked co-location, persistence saved those records, and MonoGame never owned gameplay state.

M4 adds a game-owned `ActorSceneState` table. Each row is `(ActorId, SceneId, GridPosition)`. `TinyFarmState.CurrentScene` is the player's row; all actor rows persist in the existing `tinyfarm.world` chunk. Legacy actor locations remain the high-level schedule, economy, item, and M1/M2 compatibility vocabulary. Version-3 semantic hashing adds actor scene and integer tile values.

There is no separately mutable renderer world and no general scene-instance object. Scene instantiation means selecting a validated static definition, combining it with saved actor and gameplay records, and projecting the active scene.

## Interactive-document scene law

`SceneDefinition` contains four flat tables:

| Table | Stable identity and purpose |
| --- | --- |
| Objects | `SceneObjectId`, kind, label, collision, optional semantic reference |
| Layout | object ID, integer X/Y, width/height, layer |
| Spawns | `SceneSpawnId`, integer position |
| Routes | route ID, source, trigger object, target scene, target spawn, interaction label |

The initial definitions are deliberately readable C#. Current TSON is a good future fit for these rectangular records, but forcing runtime TSON authoring into M4 would mix scene work with a new content schema and diagnostics surface. Copeland `record table` is also a natural future authoring form: objects, layouts, spawns, and routes already have ordered, typed columns. No language feature or Scene DSL was added.

Static validation rejects duplicate scene, object, and spawn IDs; missing layout objects; more or fewer than one layout row per object; non-positive/out-of-bounds rectangles; invalid route source/portal/target/spawn references; and invalid persisted actor placements. Definitions fail before play.

## Composition and reducer routing

The route tables derive this graph:

```text
Farm <-> Overworld <-> Town <-> GeneralStore
                     |
                     +----------> Riverside
```

`InteractIntent` is sufficient; there is no `SceneTransitionIntent`. The resolver finds a route whose portal layout contains the actor tile, replaces the actor's scene/position with the target spawn, synchronizes the compatible high-level location, and emits `SceneExited` plus `SceneEntered` events containing the selected route ID. Walking onto a portal never transitions by itself.

The canonical proof records the reductions for Farm -> Overworld, Overworld -> Town, Town -> Store, Store -> Town, Town -> Overworld, and Overworld -> Farm. `routes.json` is the machine-readable composition inventory.

## Five scenes

- **Overworld** reinterprets the M3 world-map role as coarse spatial navigation. Farm, Town, and Riverside are portal landmarks.
- **Farm** is 18x12 with a farmhouse, two stable plot references, a fence collision row, Elias, and an Overworld exit. Farming resolves only when the player is exactly one Manhattan tile from the referenced plot.
- **Town** is 20x14 with Mara, a well, market stall, Store entrance, and Overworld exit.
- **General Store** is 10x8 with Sela, a counter, shelves, and Town exit. Buy/sell retain shop inventory and money authority and additionally require spatial proximity to Sela.
- **Riverside** is a small exterior proof with blocking river/reeds geometry and an Overworld return. No fishing or water simulation was added.

Collision is a scene-local bounds and flat-layout query. Portals and plots are walkable; blocked props are not. Derived lookups may be added later, but the current small tables need no persistent index.

## Player and NPC movement

Arrow/WASD keys produce unit-cardinal `SpatialMoveIntent`; Enter/E produces `InteractIntent`. Plant, water, harvest, buy, sell, talk, wait, save, and load still produce their existing intents. Keyboard edge state remains in MonoGame.

Dominatus still chooses a high-level scheduled `LocationId`, not individual directions. For M4's small world, inactive NPC simulation advances one deterministic legacy location edge per observation and synchronizes authoritative scene placement to a canonical spawn. The active projection simply includes actor placement rows whose scene matches the player. There is no renderer-side spawn script. This is the explicit M4 high/low split; tile-by-tile NPC navigation was not required by observed play pressure.

## Projection, viewport, and controls

`TinyFarmFrame` now optionally carries active-scene ID/bounds, flat scene-object rows, route rows, visible actor rows, visible plots, inventory, and contextual hints. Coordinates remain renderer-neutral integer tiles. Reprojecting the same state produces the same SHA-256 hash.

MonoGame starts at 2560x1440, is resizable, reads the live graphics viewport, and fits the logical scene into the area above a shallow 76-112 pixel HUD. At 2560x1440 the Farm uses a 110-pixel tile scale with a 112-pixel HUD; at 1280x720 it uses a 50-pixel tile scale with a 76-pixel HUD. Game coordinates do not change. Portal hints use authored text such as `ENTER TOWN`, `ENTER STORE`, and `RETURN TO OVERWORLD`. Immediate transitions were kept; fade animation adds no semantic value yet.

The line-oriented `--llm-control` path now starts the scene state and accepts `move up 5 units` (or any cardinal direction and positive distance) plus `interact`, while retaining one-unit commands, semantic high-level location moves, and all existing action commands. A multi-unit move is one typed intent: the resolver validates every traversed tile and either commits the final position or rejects the entire move without partial mutation. It never reasons about pixels or emulates keyboard/mouse input.

## Persistence, determinism, and proof

Version-3 worlds use `tiny-farm-m4@3` inside the same Dominatus `SaveFile`/`SaveChunk` container and the same four chunks. Version-2 worlds retain `tiny-farm-m2@2`. The required save scenario enters Town, moves, saves, enters Store, and reloads; the restored scene, exact position, and canonical hash match the saved state.

The bounded M4 journey is Farm -> Overworld -> Town -> Store -> buy seed -> Town -> Overworld -> Farm -> plant seed, with a save/reload in Town. It also advances the existing schedule enough for an NPC to cross scenes. Repeating the full input sequence yields the same final-state, intent-result, event, route, and projection hashes.

## Boundary audits

- No `SceneNode`, `GameObject`, child/parent hierarchy, transform hierarchy, lifecycle forest, ECS, physics engine, navmesh, streaming, arbitrary scene nesting, or renderer persistence was added.
- Core and Runtime contain no XNA, MonoGame, `GraphicsDevice`, or `SpriteBatch` references.
- MonoGame creates intents and reads immutable frames; it has zero `TinyFarmState` mutation sites.
- `SceneDefinition` has zero gameplay mutation sites. All movement, routes, farming, trading, dialogue, and time changes remain in `TinyFarmResolver`.
- Plot, actor, and shop identities are referenced rather than duplicated into scene-owned gameplay records.
- Active scenes are detailed; inactive NPCs use deterministic coarse schedule progression. Inactive scenery has no simulation loop.

## Conceptual and future fit

A 2D scene and a UI are both interactive documents with objects, layout, state, reducers, and routing. TinyFarm and Machina.UI should share that law, not one runtime. A later Aurelian adapter can lower the same immutable scene frame to native render operations without changing state or resolver semantics. TSON plus Copeland record tables and templates could author and statically validate the present tables, but M4 found no need for a Scene DSL.

## Evidence

The focused M4 suite adds 11 tests covering definition validation, duplicate/invalid references, movement, multi-unit semantic CLI movement, collision, interaction-required portals, route/spawn reduction, exact save/load, spatial farm adjacency, NPC cross-scene movement, deterministic projection, canonical traversal, and repeated hash equality. The complete TinyFarm suite is 45/45 green.

Canonical values:

| Evidence | SHA-256 |
| --- | --- |
| M1 state | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| M2 state | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |
| M4 final state | `161b4b37d988ed594a6237490f2fe1b935e356af1adf6ca98499a27868bb6c53` |
| M4 intent results | `eddb9a3cadad7f50519549a2f0d3b5e8220de7db50fa1544d4ebe318aef8dfad` |
| M4 events | `8b82cc12735108b22bcc58789905aa45a78d96d2973c7ab16c320a7f2978895a` |
| M4 routes | `536ec6457b490cfa2eeb3e31bbfd91fb4b963abb3f65bcf71fa9b27459f25638` |
| M4 projection | `ce80e5f9ea335166841f12ff190d5af4041824eeb4dae38d268e412322f46ecb` |

No screenshot is retained. The five required artifacts are compact JSON, and the existing artifact-budget guard remains authoritative.

## Exact M5 recommendation

**TINY-FARM-M5 — interaction targeting and short-path NPC locomotion, only.**

Observed pressure is now input ergonomics rather than composition: actions choose the first adjacent eligible plot/actor, and inactive NPC placement snaps at scene boundaries. M5 should add an explicit player facing/target rule and a tiny deterministic scene-local waypoint step for visible NPCs. It should not add Machina.UI, ECS, a generic pathfinder, content expansion, or a Scene DSL until those two real play seams are resolved.
