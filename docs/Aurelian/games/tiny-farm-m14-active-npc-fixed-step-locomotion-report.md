# TinyFarm M14 — active-NPC fixed-step locomotion and bounded local wander

## Outcome

**Outcome A — active NPC locomotion cleanly joins the fixed semantic domain.** The live host now applies player held movement and active-scene NPC path following on the same exact rational 60 Hz opportunity stream. Dominatus still selects semantic goals only on world-minute or explicit semantic evaluation, DotRecast still plans only when a goal/path is invalidated, and MonoGame only observes authoritative positions.

The enduring law is:

```text
Policy != Planning != Locomotion != Rendering
```

## Mandatory pre-implementation audit

Before M14, `TinyFarmSession.Step` asked each NPC for a schedule decision, lowered `NavigateToAnchorIntent` through DotRecast, and immediately emitted one 128-unit `SpatialMoveIntent` in that same reduction. Consequently, active NPC position, facing, waypoint advancement, and arrival recognition were primarily world-minute driven.

| Concern | Pre-M14 cadence | M14 target and result |
| --- | --- | --- |
| Utility/schedule decision | World-minute or explicit semantic step | Unchanged |
| Goal invalidation | Decision batch | Unchanged; committed local Wander resists Wander-to-Wander churn |
| DotRecast query | First reduction after goal/path invalidation | Event-driven and cached; never queried per locomotion step |
| Waypoint advancement | World-minute decision reduction | Fixed 60 Hz locomotion opportunity |
| `SpatialMoveIntent` | World-minute decision reduction | Fixed 60 Hz locomotion opportunity |
| Position mutation | `TinyFarmResolver` | Unchanged authority, now invoked by locomotion domain |
| Facing | Successful spatial reduction | Unchanged law, now updated at locomotion cadence |
| `AnchorReached` | Movement reduction, therefore usually world-minute cadence | Exact locomotion step entering authored arrival radius |

Direct `TinyFarmSession.Step` retains the historical one-step behavior required by M5–M12 compatibility tests. `TinyFarmSimulationHost` explicitly enables fixed-NPC locomotion, separates goal planning from movement consumption, and is the real M14 live path.

## Host integration and movement law

`TinyFarmSimulationHost` interleaves world-minute and locomotion boundaries using integer `TimeSpan` ticks. Ties consistently process locomotion first. Play uses 60 opportunities per real second; FastForward applies the existing 10x multiplier to both semantic clocks. Pause accepts no semantic time. Player movement remains 128 world units per opportunity. NPC movement is the single M14 constant of 16 integer world units per opportunity (960 units per semantic second, or 0.9375 tile/second).

For each opportunity, the host submits at most one existing `SpatialMoveIntent` per active path follower. `TinyFarmResolver` remains the sole collision, position, facing, and Rest-clearing authority. Paths and waypoint indexes remain derived session state and are not persisted. An accepted final movement emits `AnchorReached` immediately and resolves the existing `AnchorReachedIntent` in the same opportunity, so Rest can begin on physical arrival rather than waiting for a minute boundary.

Inactive NPCs perform no 60 Hz movement and no locomotion-triggered DotRecast query. Their existing coarse semantic reduction may realize a local Wander as remaining in the same `farmhouse` semantic location with a canonical local placement. Exact offscreen coordinates are deliberately not history-equivalent; coarse location, Energy, regime, major goal, and Rest remain authoritative.

## Bounded local Wander

M14 adds exactly two TSON-authored Farm anchors: `farm.wander-a` and `farm.wander-b`, both `SceneAnchorKind.Wander`, walkable, in the Farm scene, and semantically tied to `farmhouse`. Elias's morning window is Open in the M14 content set. Its bounded candidates are his personal Rest anchor plus the two local Wander anchors.

At high Energy, Wander's base score 0.50 beats Rest. At Energy 1,000, Rest scores 0.82 and wins. The Required bedtime window selects `elias.home-bed` structurally and never runs Open utility. Selection is random-free: the first tie uses stable Dominatus option order, then arrival at one Wander anchor grants the other a 0.10 rotation contribution. Runtime goal commitment retains the chosen Wander target until `AnchorReached`, unless policy chooses a non-Wander goal such as Rest.

Validation accepts `local-wander` only for authored Wander anchors and rejects mixed-scene Wander candidates within one Open window. Existing global anchor uniqueness, bounds, walkability, known-scene, and candidate-window validation supplies the duplicate, blocked, unknown, and non-Open failure boundaries.

## Determinism and evidence

The renderer-free canonical scenario starts on Farm at 08:00 with Elias at Energy 9,000. After the goal is evaluated and planned once, one real second produces 60 locomotion opportunities and changes Elias from `(4608,7680)` to `(5088,8160)` while advancing zero world minutes, changing no Energy, adding no policy evaluation, and issuing no extra path query.

The representative 60-second Play run records:

| Metric | M13 representative | M14 |
| --- | ---: | ---: |
| Render observations | 3,600 | 3,600 |
| Locomotion opportunities | 3,600 | 3,600 |
| NPC locomotion reductions | n/a | 2,249 |
| World minutes | 12 | 12 |
| NPC policy evaluations | 36 | 39 (includes the explicit initial load/activation evaluation) |
| DotRecast queries | scenario-dependent | 7 |
| Anchor arrivals | n/a | 6 |
| NPC movement allocation | n/a | 6,720 B/reduction |

The allocation number is the full current authoritative resolver boundary, including immutable batch/result/event projection and state snapshots; the waypoint follower itself reuses the existing path collection. It is honest pressure: M14 adds no per-step path collection, but the resolver path is not allocation-free.

Exact 60 Hz versus 144 Hz render partition and the M13 irregular-delta pattern produce identical state hash, NPC position, facing, waypoint index, semantic target, Energy/Rest state, locomotion count, decision count, and path-query count. Save/load restores the exact mid-walk authoritative position, persists no DotRecast state, recomputes one path on semantic evaluation, and continues fixed-step movement.

## Graphical proof

The real `TinyFarm.MonoGame` application was built and run at its 2560x1440 default. In Play, the Farm scene showed Elias in Open regime at Energy 90.00. After policy selection, successive live captures showed Elias walking from the lower-left Farm position toward and between the authored local targets while the clock advanced independently from 08:00 to 08:02 and beyond. The visible projection followed authoritative state; no animation system was added. Pause/resume and FastForward are additionally covered through the same host by deterministic tests because injected one-frame number-row key events were not reliably sampled by MonoGame during automation.

## Persistence, compatibility, and boundaries

The simulation snapshot remains `tiny-farm-simulation@1`; its existing `goal` field now prefers the committed semantic navigation target, so no DTO bump was needed. Mid-Wander save/load is covered. Major-path save/load continues through the same derived-path recomputation used by M12/M13. The complete pre-M14 TinyFarm suite remains green, including exact historical scenarios.

No physics, velocity, acceleration, combat, projectile, animation graph, new need, random roaming, generic action hierarchy, generic scheduler, renderer authority, Machina.UI work, Oblivion integration, or Aurelian extraction was added. NPC locomotion is the current kinematic fixed domain. Future combat/projectiles may share it or justify another fixed domain only when their semantics require one.

## Artifacts and validation

Compact evidence is under `artifacts/tiny-farm-m14/`: `proof.json`, `locomotion.json`, `wander.json`, `cadence.json`, and `manifest.json`. Content hashes are:

- scene aggregate: `6f0ab02083cc03e92bb612f37bb8ee4b5ffafa52586e366aa523baf0c9784b6d`
- schedule aggregate: `5fe6d12a07f6278d4b374160d270022248d47d7ee0733ca5b57a9ea440180cfe`
- canonical 60-second state: `a0d79da0f0590d1c77d1a27bd19494e1ae68dd16ae8c46caccb20dfcbcb8fd84`

## Recommended M15

**TINY-FARM-M15 — allocation-bounded authoritative locomotion reduction.** The observed pressure is the measured 6,720 B per accepted NPC movement reduction, not policy, path planning, rendering, or another gameplay system. M15 should preserve `TinyFarmResolver` authority while removing redundant per-step state/result/event snapshots or batching safe same-opportunity NPC movement. It must prove a materially lower measured B/step before introducing any generalized movement framework.
