# AURELIAN-SIMULATION-SCENE-KIT-M5 report

## Outcome

**Outcome A — reusable simulation/scene kit is justified and qualified.**

The extracted API is `Aurelian.Simulation`. TinyFarm consumes its ordered cadence scheduler, stable navigation request identity, arrival coordination, deterministic schedule selection, and projected scene catalog. A separate laboratory fixture consumes the same APIs with 30 Hz physics-like work, 5 Hz agent work, and a 2 Hz environmental pulse. It contains no TinyFarm minutes or farming semantics.

> Aurelian supplies cadence, scene, and navigation mechanisms. Applications define world-time meaning, schedule content, and simulation policy.

## Mandatory audit

| Concern | Existing owner | Generic law? | TinyFarm-specific? | M5 action |
|---|---|---:|---:|---|
| host delta and clamp | `TinyFarmSimulationHost`; `AurelianGameHost` forwards frame delta | yes | 5-second choice | extract configurable bounded scheduler; retain host choice |
| fixed locomotion cadence | TinyFarm integer numerator; local `FixedMovementStepper` | yes | movement intent/distance | replace host accumulator with `CadenceScheduler`; leave intent production local |
| world-minute progression | `TinyFarmSimulationHost.AdvanceMinutes` and resolver `WaitIntent` | cadence only | minute meaning and crop/energy effects | register a cadence named by TinyFarm; leave reduction local |
| NPC decision cadence | TinyFarm session at world-minute reduction | cadence boundary only | observation and decisions | scheduler emits facts; app invokes session |
| active-scene locomotion | `TinyFarmSession` | activation/detail fact only | which NPCs move and how | extract detail fact; leave filtering and locomotion local |
| inactive coarse update | TinyFarm resolver/session policy | mode handoff only | coarse progression | extract no policy; expose `Detailed`/`Coarse` fact |
| pause/play/fast-forward | `TinyFarmSimulationHost` | yes | multiplier choice | extract `SimulationExecutionRate` |
| `FixedMovementStepper` | TinyFarm test/helper | accumulator law already covered | emits `SpatialMoveIntent` | do not extract; no second movement engine |
| DotRecast planning | `DotRecastNavigationPlanner` | proposal/failure coordination | scene mesh conversion currently TinyFarm-shaped | retain the one planner and cache; extract coordination facts, not a pathfinder |
| schedules | TSON catalog + `TinyFarmNpcSchedule` + Dominatus | deterministic winner bridge | Required/Open, utility, energy, jobs | extract deterministic selection; leave policy and content local |
| Aurelian clocks/runtime | `WorldClock`, `AurelianRuntimeSession` ticker | frame/kernel progression | not calendar authority | do not repurpose |
| Dominatus `AiClock` | Dominatus policy runtime | policy elapsed time | float seconds, not host/world authority | keep behind application decision boundary |
| scenes/routes/anchors | TinyFarm typed/TSON catalog and resolver | stable IDs, bounds, catalog, route validation | objects, locations, interactions | extract query/transition proposal; adapt from TinyFarm runtime |
| scene transition | TinyFarm resolver | validated proposal and post-accept hooks | permission and state mutation | resolver stays authoritative; hooks run only after acceptance |
| resources/camera | audio/graphics/presentation leaves | post-transition handoff | exact loading/follow policy | optional leave/enter and camera-snap interfaces; no gameplay authority |

## Final cadence law and API

```text
host TimeSpan delta
-> clamp into accepted and discarded ticks
-> integer/rational accumulation under Paused, Normal, or FastForward(n)
-> ordered DueWorkFact values
-> application invokes semantic work
-> application resolver mutates authoritative state
```

`CadenceScheduler` registers `CadenceDefinition(CadenceId, RationalRate, Order)`. Rates are reduced positive rational occurrences/seconds. Accumulators use checked integer arithmetic against `TimeSpan.TicksPerSecond`; no float clock participates. `CadenceAdvanceResult` reports accepted/discarded host ticks, scaled semantic ticks, due facts, and bounded accumulator inspection. `ConfigurationIdentity` is a stable SHA-256 of ordered cadence/rate/clamp configuration for save/replay compatibility metadata.

When cadences share a boundary, ascending explicit `Order` wins. Order values must be unique, so dictionary/enumeration order is never authority. TinyFarm registers locomotion before world progression, preserving its prior tie law.

Paused produces no cadence work and retains no catch-up debt. Rendering, input, UI, and audio remain independent host policies. Fast-forward multiplies accepted time before cadence accumulation; it neither skips work nor selects coarse policy. Excess over the configured host-delta clamp is reported and discarded, never backlogged.

The 60 Hz, 144 Hz, and irregular partitions produce identical ordered tick traces for both TinyFarm and the second consumer. TinyFarm remains exactly 60 locomotion ticks/second, one world tick/5 accepted seconds, and 10x in fast-forward.

## Scene, transition, and activation law

`SceneCatalog` contains only stable scene/anchor/route IDs, integer bounds/positions, optional static metadata, and validated route destinations. It is not an entity model. `SceneTransitionBridge.Propose` validates a route from the current scene but does not mutate state. After the application resolver accepts a transition, `CompleteAccepted` orders optional resource leave, resource enter, and presentation-only camera snap hooks, then emits `SceneActivationFact`.

`SceneSimulationDetail.Detailed/Coarse` communicates activation policy selection. Aurelian performs no hidden inactive-scene simulation. TinyFarm continues to decide that inactive NPCs issue no fixed locomotion or locomotion-triggered DotRecast queries while semantic world progression remains application-owned.

## Navigation and schedule law

```text
semantic schedule observation
-> deterministic selected application goal
-> NavigationGoal(request ID, scene, anchor)
-> existing DotRecast path proposal
-> application submits movement intent
-> Spatial2D query + resolver validates displacement
-> Arrived / PathUnavailable / Blocked / ReplanRequested / Interrupted fact
-> application or Dominatus chooses what follows
```

`NavigationCoordinator` compares integer positions with the destination arrival radius and produces explicit coordination facts. It never mutates a position. TinyFarm continues to own the single `DotRecastNavigationPlanner`; no second pathfinder or navigation cache was added. TinyFarm path cache identity now uses reusable stable `NavigationRequestId` values for anchor/route matching. A changed route or anchor identity invalidates the cached path. A resolver rejection removes TinyFarm's derived path so the next locomotion opportunity replans; it never teleports.

`DeterministicSchedule` selects the unique highest-priority matching window for caller-supplied semantic time. Equal-priority distinct winners are diagnosed as ambiguity. The application provides recurrence/day matching and the goal/content. Required/Open is deliberately not extracted: Required bypass, utility scoring, energy/rest contribution, local wander, and Dominatus option order are TinyFarm policy. Schedule tables remain typed TSON data; no DSL was added.

Dominatus continues to own utility, HFSM/flow, and decisions. Aurelian supplies values and facts only and has no Dominatus dependency.

## Proofs and boundaries

- Active NPC: TinyFarm M14 still runs schedule goal -> DotRecast -> fixed locomotion -> `SpatialMoveIntent` -> Spatial2D-backed resolver -> `AnchorReached`.
- Inactive NPC: the M14 regression proves no detailed locomotion or new path query after the application moves the player to another active scene.
- Pause/resume/fast-forward: focused M13/M14 tests remain exact; paused steady-state remains zero allocation.
- Scene transition: TinyFarm M4/M21 resolver tests remain authoritative. The generic fixture proves route validation and post-accept leave -> enter -> camera ordering.
- Host integration: `AurelianCadenceApplication` adapts `AurelianHostFrame.Elapsed` into facts. The wrapped application performs semantic work; `AurelianGameHost` never sees world state.
- Second consumer: 30/5/2 Hz laboratory semantics produce 37 ordered facts/second and match across 60, 144, and irregular partitions.

The canonical proof is intentionally compositional because TinyFarm's current product host is MonoGame while `AurelianGameHost` is the qualified native host. Real TinyFarm proves DotRecast, Spatial2D, arrival, authoritative room changes, pause, resume, and fast-forward; the native host fixture proves the generic host/cadence handoff; the scene fixture proves resource/camera hooks. M5 does not introduce a second TinyFarm host merely to make one monolithic test.

## Time domains, save, replay, and inspection

| Domain | Owner |
|---|---|
| HostTime | window/platform host supplies elapsed `TimeSpan` |
| RenderTime | renderer/presentation frame sequence and interpolation |
| SimulationCadence | `CadenceScheduler` ordered tick production |
| WorldTime | application semantic state/reducer |
| AgentDecisionTime | application/Dominatus policy invocation |

Saves retain application semantic time, scene/actor state, and app-owned schedule state. They exclude DotRecast caches, resource handles, due queues, and transient cadence output. Exact mid-period continuation may save an application-approved cadence continuation in a future save envelope; M5 preserves TinyFarm's established reset-on-session-replacement law. Replay authority remains semantic intents/state; cadence `ConfigurationIdentity` is metadata, not gameplay truth.

Debug facts are bounded: accepted/discarded/semantic ticks, due counts/order, accumulator remainder/period/tick count, configuration identity, active/detail scene fact, navigation request/outcome, and transition. There is no debug UI.

The final evidence run measured 10,000 one-millisecond second-consumer cadence advances in 1.05 ms with 365,640 bytes allocated (36.56 bytes/advance) on this checkout. No-due results reuse empty storage and accumulator snapshots are opt-in; TinyFarm's paused hot path remains 0 bytes. No concurrency or pooling architecture was added.

## Extracted and deliberately local APIs

Extracted:

- `CadenceScheduler`, `CadenceDefinition`, `RationalRate`, `DueWorkFact`, execution rate and debug facts;
- `SceneCatalog`, `SimulationScene`, `SimulationAnchor`, `SimulationRoute`, `SceneTransitionBridge`;
- activation/detail, resource-scope, and presentation-snap hooks;
- `NavigationGoal`, stable request identity, outcome facts, and `NavigationCoordinator`;
- `DeterministicSchedule` and application-goal schedule matches;
- `AurelianCadenceApplication` host adapter.

Left local:

- game minutes/days, crops, energy/rest, combat, actor/entity model, jobs and NPC action catalog;
- Required/Open scoring and Dominatus flows;
- TinyFarm scene objects/layout/location mapping and resolver permission;
- DotRecast mesh construction/cache and Spatial2D movement intent semantics;
- inactive/coarse progression policy and save migration.

## Tests, validation, and artifacts

New tests cover single/multiple cadences, simultaneous ordering, pause/resume/fast-forward, clamp accounting, stable configuration identity, 60/144/irregular partitions, scene/anchor/route validation, transition proposal and hook order, active/coarse facts, navigation arrival/block/replan/interruption, deterministic schedule matching/tie diagnostics, and native host integration.

Focused validation at report creation:

- `Aurelian.Simulation.Tests`: 14 passed;
- TinyFarm M13/M14 focused lane: 28 passed;
- `Aurelian.GameHost.Tests`: 7 passed;
- evidence runner: Outcome A; M13 and M14 Outcome A with prior hashes preserved.

- `dotnet test Aurelian.slnx -m:1`: 728 passed;
- `dotnet test TinyFarm.slnx -m:1`: 307 passed;
- `dotnet test JointTaskForce.slnx -m:1`: 3,476 passed.

Artifacts are the six compact JSON files under `artifacts/aurelian-simulation-scene-kit-m5/`. They record cadence traces/configuration/performance, scene transition hooks, navigation/schedule facts, TinyFarm parity, the integrated proof summary, and the manifest.

## Next milestone

Exact next milestone: `AURELIAN-GAME-SAVE-REPLAY-M6`.
