# TinyFarm M5 — Continuous locomotion, targeting, and navigation

## Outcome

**Outcome A.** TinyFarm actors now inhabit continuous authoritative scene space, interactions are selected from semantic spatial targets, and visible NPCs use DotRecast-derived paths without transferring world authority to the navigation library or MonoGame.

## M4 migration audit

M4 stored `ActorSceneState.Position` as `GridPosition`. Movement advanced whole cells, collision tested only the destination cells, portals required occupying a trigger cell, proximity used Manhattan distance, save/hash serialized tile X/Y, `TinyFarmFrame` projected tile coordinates, MonoGame drew at cell centers, and scheduled NPC movement immediately replaced both coarse location and scene spawn. Those were the only whole-tile assumptions migrated. Scene object, layout, spawn, plot, portal, and collision authoring remain grid based.

## Authoritative spatial law

M5 adds `ScenePosition(XUnits, YUnits)` with exactly 1,024 integer world units per authored tile. `ActorSceneState.WorldPosition` and four-way `ActorFacing` are authoritative, hashed, and persisted in game save version 4. `GridPosition` is derived by integer tile lookup and remains the authored collision/occupancy vocabulary. Movement is cardinal, accepts distances below one tile, checks semantic collision in bounded quarter-tile increments, and retains the last successful facing while stationary.

The determinism contract is: an identical ordered intent sequence with identical fixed-step durations yields identical integer positions, results, events, and canonical hashes. MonoGame converts held input through a rational 60-step-per-second accumulator; a 60 Hz and a 144 Hz sampling partition of the same one-second interval emits the same 60 semantic movement intents. Render interpolation is neither needed nor stored.

## Interaction targeting

`TinyFarmSpatialQueries` derives candidates from actor placements and scene object/layout rows. Candidates must be in a 1,280-unit forward range and a 640-unit half-width. Selection is deterministic by interaction-kind priority (actor, plot, shop, portal), squared integer distance, then ordinal stable semantic identity. `InteractIntent` may derive the target or carry an explicit stable `SceneObjectId` for NPC portal traversal; it never carries a sprite reference.

The selected target is exposed by `TinyFarmFrame.InteractionTarget`, produces a compact `[Interact]` hint, and actors selected for interaction receive a projection-only highlight. Actor targets enter existing Talk/Ariadne semantics. Plot targets enter the existing plant/water/harvest reducers. Shop targets enter existing buy/sell reducers. Portal targets enter the existing authored scene-route reducer. Behind and out-of-range candidates produce `NoInteractionTarget`.

## DotRecast boundary

Runtime references only `DotRecast.Recast` and `DotRecast.Detour`, version 2026.3.1. Recast derives a navmesh from scene bounds plus the blocking layout rectangles; Detour performs nearest-poly, corridor, and straight-path queries. `INavigationPlanner` exposes only `SceneDefinition`, `ScenePosition`, `NavigationPath`, and typed `NavigationFailure`. DotRecast types are confined to `TinyFarmNavigation.cs` in Runtime and do not enter Core public contracts, intents, save data, scene definitions, or the frame projection.

Navigation data is cached in memory per stable `SceneId`, never persisted. Paths are scene-local waypoint lists rounded back to integer world units. A blocked start/goal, build failure, or incomplete corridor returns a typed failure and does not move or snap the actor. Semantic collision is checked again by the resolver and wins over path output.

Dominatus still selects a high-level `LocationId`. Runtime maps that goal to a same-scene semantic anchor or finds the first authored `SceneRoute` on the coarse scene graph. DotRecast solves only the active-scene leg. Inactive NPCs retain coarse deterministic progression. A visible NPC caches its goal/path, advances one shared 128-unit `SpatialMoveIntent` at a time, uses explicit portal interaction at the route endpoint, and replans only after goal/scene change or load. Actors may overlap in M5; no crowd or physics system was added.

## Evidence

The headless M5 journey walks around authored obstacles, talks to Elias, traverses Farm → Overworld → Town, talks to Mara, enters the store and buys seed, returns to Farm, plants and waters through the semantic target, saves between tile centers, reloads exactly, continues, and separately proves a visible scheduled Elias movement step. Repeating the scenario preserves all evidence hashes.

The focused suite covers sub-tile movement, collision, facing, ahead/behind/range targeting, stable ties, plot targeting, DotRecast obstacle routing, blocked goals, waypoint containment, visible NPC movement, scene-graph/spatial-path separation, derived-path reload, fixed-step cadence independence, deterministic replay, and mid-tile persistence. The existing M4 suite remains separate and green.

The proof records the observed cold Farm nav build and query durations in `navigation.json`; the current run is approximately 60 ms cold build and 0.02 ms query. The proof also records the first visible NPC planning/movement update duration. These are qualification observations, not optimization targets.

Manual window inspection used the real MonoGame executable. At 2560×1440 the scene uses the viewport, the shallow HUD remains readable, and obstacles, actors, plots, and the portal are clear. At 1280×720 the complete Farm scene and compact HUD remain visible without overlap or clipping. Automated app control could not sustain a held key long enough to make a responsible subjective movement-feel claim; continuous displacement, fixed-step cadence, target highlighting, and visible NPC motion are instead covered through the real headless session/frame paths. This is functional qualification, not a claim of polished animation feel.

The immutable frame already contains scene bounds, object geometry, actor positions/facing, semantic target, and labels, so a crude ASCII projection is possible without renderer state; M5 does not add a second frontend. The same Scene + table + reducer split could also host a discrete board-game frontend by choosing tile-centered positions and board-specific intents, but chess rules, board UI, and move generation remain out of scope.

## Compatibility and extraction

The M1 canonical hash remains `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`. The M2 canonical hash remains `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3`. M4 continues to execute with save version 3 and tile hashing; history was not rewritten.

The extraction recommendation is **KEEP_TINYFARM_LOCAL**. `ScenePosition`, facing, targeting, and the narrow navigation request are genre-neutral in shape, but there is not yet a second concrete Aurelian game consumer. The current abstraction is already replaceable without changing game semantics; moving it now would add ownership without evidence.

The observed pressure is not pathfinding. It is the hard-coded mapping from a Dominatus `LocationId` to a same-scene destination point. The exact recommended M6 is **TINY-FARM-M6 — Authored Semantic Scene Anchors and Active/Inactive NPC Handoff**: add stable, tabular destination anchors for existing locations/NPC schedules and prove seamless handoff across the visibility boundary. Do not expand content, navigation algorithms, UI, or agent decision vocabulary.

## Artifacts

Compact evidence is under `artifacts/tiny-farm-m5/`: `proof.json`, `navigation.json`, `interaction.json`, `projection.json`, and `manifest.json`. There is no screenshot bundle and no persisted navmesh.
