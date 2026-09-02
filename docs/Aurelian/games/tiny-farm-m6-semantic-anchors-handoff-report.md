# TinyFarm M6 — Semantic anchors and active/inactive NPC handoff

## Outcome

**Outcome A.** NPC schedules and semantic controller commands now target stable `SceneAnchorId` values. Authored anchor rows own the scene-local coordinates. The player's current scene selects detailed spatial fidelity: active NPCs lower anchor goals through DotRecast to ordinary `SpatialMoveIntent` values, while inactive NPCs advance through the existing coarse location graph without path queries. Visibility changes fidelity, not gameplay authority.

The semantic law is: **an anchor is a meaningful address, not an arbitrary coordinate**. `general-store.counter`, `farm.work-area`, `town.square`, and `riverside.meeting-point` describe why an actor is going somewhere; their integer `ScenePosition` values are one authored realization.

## Hard-coded destination inventory

The pre-change audit classified the relevant coordinate sites as follows:

| Site | Classification | M6 result |
| --- | --- | --- |
| `TinyFarmSession.GoalForLocation` literals `(4,7)`, `(5,3)`, `(5,5)`, `(12,7)` | `TEMPORARY_HARDCODE` / `SEMANTIC_ANCHOR_CANDIDATE` | removed; replaced by authored anchors |
| scene object/layout rows in `SceneModel.cs` | `AUTHORED_LAYOUT` | retained |
| route target spawn coordinates | `SPAWN_ONLY`, but duplicated route-address semantics | spawn became `SceneAnchorKind.Spawn`; routes now carry `TargetAnchor` |
| initial actor placements in `TinyFarmContent` | `SPAWN_ONLY`; NPC rows duplicated semantic anchor coordinates | NPC initial placements now resolve authored anchors; the player's distinct initial placement remains bounded state authoring |
| plot adjacency and object-center calculations | `LEGITIMATE_GAME_LOGIC` | retained derived geometry |
| movement deltas, fixed-step distances, collision bounds, interaction range | `LEGITIMATE_GAME_LOGIC` | retained |
| M4/M5 scenario/test coordinates | `TEMPORARY_HARDCODE` test fixtures | retained only where they establish a spatial precondition; schedule destination proof uses anchors |
| projection-only legacy world-map pixel positions | `AUTHORED_LAYOUT` for pre-scene save versions | retained; not used by scene navigation |

The production schedule-to-coordinate switch is gone. Remaining production coordinates are authored scene/layout/initial-state rows or integer geometry rules.

## Anchor representation and validation

`SceneAnchorId` is a typed record identity whose stable value is independent of declaration order, renderer objects, row indexes, and coordinates. `SceneAnchorDefinition` is a flat row containing only fields used by M6: ID, owning scene, integer position, kind, optional semantic `LocationId`, optional semantic `SceneObjectId`, optional facing, and deterministic integer arrival radius.

The deliberately small kind set is `Spawn`, `Work`, `ShopCounter`, `Home`, `Social`, and `Exit`. M6 authors no `Exit` row yet; retaining that value records the already-demonstrated route-address role without creating a larger taxonomy. Preferred facing is supported but no current anchor authors it because it did not materially improve the bounded locomotion proof.

Validation runs before play and rejects duplicate global IDs, duplicate scene-local IDs, wrong owning scene, out-of-bounds or blocked positions, negative arrival radii, unknown semantic locations or objects, and routes whose target anchor is absent or in the wrong target scene. Lookup uses one derived dictionary keyed by `SceneAnchorId`; declaration and evidence output use stable ordinal ID order.

## Spawn and route result

The result is **SPAWN_IS_ANCHOR_KIND**. A route entry point is a semantic address with specialized use, not a parallel coordinate record. `SceneSpawnId` and `SceneSpawnDefinition` were removed. `SceneRoute` now stores `TargetScene` plus `TargetAnchor`, and the reducer resolves that anchor row before changing scene and exact position. Initial actor positions remain separate because they are initial authoritative state, not reusable route destinations.

## Goal and navigation lowering

Dominatus schedule logic now returns a `SceneAnchorId`. `AgentObservation` carries both that typed anchor and its coarse semantic location; it never carries `ScenePosition`. The flow emits `NavigateToAnchorIntent`. Runtime performs:

```text
high-level schedule decision
  -> SceneAnchorId
  -> validated SceneAnchorDefinition
  -> active scene: ScenePosition -> INavigationPlanner -> DotRecast waypoints
                  -> bounded SpatialMoveIntent -> TinyFarmResolver
  -> inactive scene: existing coarse location step -> TinyFarmResolver
```

Cross-scene active travel still walks to an authored portal and uses `InteractIntent`; it does not path across scenes. Missing anchors produce `MissingAnchor`. Failed active path queries produce `AnchorUnreachable`. Both leave position and goal inspectable and never snap or teleport.

Line-oriented/LLM control accepts `go [to] <semantic anchor>`. Aliases such as `store counter`, `farm work area`, `town square`, and `riverside` lower to `NavigateToAnchorIntent`; runtime then uses the same navigation and resolver path as Dominatus. Actor-directed control can resolve an NPC's current scheduled anchor through `TinyFarmSemanticNavigation.ResolveActor`. No coordinate is exposed or accepted by this semantic API.

## Active/inactive law and handoff

`ActiveScene` is the player's authoritative current scene, exposed by `TinyFarmSession`; renderer allocation is irrelevant. An NPC whose `ActorSceneState.Scene` equals `ActiveScene` receives detailed spatial realization. Only that branch may ask `INavigationPlanner` for a path. NPCs outside it submit their semantic anchor goal to the existing coarse location reducer and perform no per-frame movement or DotRecast work.

M6 chooses persistence model **A: exact position always persists**. `ActorSceneState` remains the one authoritative spatial placement in both modes. `ActorState.Location` is the established coarse scene-scale semantic place; validation and reductions require its mapped scene to agree with `ActorSceneState.Scene`, with the existing Overworld transit scene explicitly compatible with Town Square's coarse graph node. Reaching a location does not falsely mean reaching its anchor: inactive cross-scene progression realizes the NPC at the target scene's stable spawn-kind entry anchor. On activation, that exact position becomes the start of a newly derived path to the scheduled semantic anchor.

Inactive to active therefore preserves the semantic schedule goal, uses the persisted/just-realized exact position, recomputes a scene-local path, and walks visibly. Active to inactive preserves location, exact position, facing, and goal derivation, discards the cached path on player scene transition, and resumes coarse steps. There is no second location or position store and no reconciliation race.

Arrival uses the authored integer radius, not exact coordinate equality. When a shared movement intent crosses into the radius, the step emits `AnchorReached` with actor, scene, semantic location/object, and anchor identity. The actor's exact position remains the movement reducer's result. No animation choreography or facing behavior was added.

## Content results

- Sela and Mara's shop work resolves to `general-store.counter`, linked to the existing shop-counter scene object.
- Elias's morning farm work resolves to `farm.work-area`; home semantics use `farm.home`.
- Town social time resolves to `town.square`.
- Riverside schedules resolve to the single `riverside.meeting-point` social anchor.
- Farm plots remain plot objects and are not turned into generic anchors.
- `InteractionTarget` remains separate: an anchor is where an actor intends to go; an interaction target is the actor/object/plot/shop/portal currently selected.

## Persistence, determinism, and equivalence

Active mid-route save/load restores exact scene, position, and facing, persists no DotRecast data, recomputes the path, and continues. Inactive save/load restores the same coarse location and exact entry/last-known position; later activation yields the same first movement and semantic hash. Active-to-inactive time advancement and re-entry retain one NPC, the current scheduled anchor, and a valid scene/position pair with no stale path.

The handoff-equivalence invariant is high-level semantic equality: when active and inactive histories imply the same schedule completion, actor location and scheduled anchor must agree. Exact position may differ while one history is still spatially en route; that difference is intentional and inspectable, not competing authority.

The canonical proof records three active navigation plans and writes the current 1,000-pass indexed anchor lookup, activation, deactivation, and post-load path-rebuild timings to `proof.json`. These are lightweight environment-sensitive observations, not optimization targets. The injected-planner proof establishes zero inactive path queries.

## Compatibility and boundaries

M1 remains exactly `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`. M2 remains exactly `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3`. Existing M4/M5 movement, interaction, route, continuous-position, and DotRecast tests remain green.

`TinyFarm.Core` references neither MonoGame nor DotRecast. DotRecast types remain confined to `TinyFarmNavigation.cs` in Runtime. Paths and navmeshes remain derived, cached, and absent from saves. MonoGame consumes the immutable frame; NPC frame rows now optionally expose the semantic target anchor for developer inspection.

No ECS, streaming, crowd system, dynamic navmesh, behavior tree, new content, scene DSL, Machina.UI, Oblivion integration, or Aurelian rendering work was added.

## TSON, Copeland, and extraction assessment

Anchors strengthen the table-authoring case. Their schema is flat, typed, validated, stable-ID ordered, and naturally joins Scenes, Objects, Routes, and semantic Locations. The current C# rows are readable but now repeat enough identity/scene/position scaffolding to justify a later bounded TSON authoring qualification using the existing definition loader. M6 deliberately performs no migration and adds no Copeland language feature.

The extraction recommendation remains **DEFER_UNTIL_SECOND_GAME**. `SceneAnchorId`, `ScenePosition`, facing, semantic navigation requests, and the fidelity handoff contract now form a coherent TinyFarm-local substrate, but there is still only one real consumer. Extracting it into Aurelian would manufacture generality without the required second-game evidence.

The exact recommended M7 is **TINY-FARM-M7 — TSON-authored scene, anchor, and route tables**: move only the existing static rows into the current TSON/record-table loading and validation path, prove byte-for-byte stable semantic identities and unchanged M1/M2/M6 hashes, and stop before adding a DSL, editor, hot reload, or shared Aurelian scene package.

## Evidence

The focused suite contains 73 passing tests, including 17 M6 tests for anchor identity/validation, route references, schedule lowering, active arrival, zero-query inactive progression, both handoff directions, active and inactive save/load, authority-disagreement rejection, missing/unreachable typed failures, semantic controller lowering, cross-scene coarse-to-visible motion, dependency leakage, and canonical hash repetition.

Compact evidence is under `artifacts/tiny-farm-m6/`: `proof.json`, `anchors.json`, `handoff.json`, `navigation.json`, and `manifest.json`. There is no screenshot bundle or persisted navigation artifact.
