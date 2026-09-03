# TinyFarm M15 — allocation-bounded authoritative locomotion

## Outcome

**Outcome A — authoritative locomotion is allocation-bounded without semantic compromise.** The active NPC follower and the fixed-step player path now enter one internal `TinyFarmResolver.ResolveSpatialMoveCore` function. The public `Resolve(GameIntent)` path calls that same function and retains its copied state, ordered batch, `IntentResult`, and `GameEvent` contract. There is no direct position mutation outside the resolver, no duplicate collision implementation, and no second movement engine.

The preserved path is:

```text
waypoint follower
-> SpatialMoveIntent
-> TinyFarmResolver.ResolveSpatialMoveCore
-> bounds and precomputed scene collision
-> localized authoritative ActorSceneState replacement
-> facing and Rest mutation
-> ActorMoved and optional AnchorReached materialization
```

## Allocation reconnaissance

The pre-change executable ran 1,000 warmups followed by 100,000 accepted active-NPC movement reductions, repeatedly walking Elias between the two authored Farm wander anchors. `GC.GetAllocatedBytesForCurrentThread` measured 668,758,728 bytes, or **6,687.587 B/reduction**, reproducing M14's rounded **6,720 B/reduction**. It recorded 40 Gen0 collections and 5,953.373 ns/reduction. The run performed 840 policy evaluations and 280 DotRecast queries across warmup plus measurement; neither was per-step.

An EventPipe `gc-verbose` trace captured `GCAllocationTick` type samples. The leading attributed types were `ActorState`, actor inventory lists, actor-scene lookup predicates, `GameEvent`, `TinyFarmState`, and the arrays backing actor, scene, item, energy, result, and event collections. Allocation ticks are samples, so their counts are reported as samples rather than falsely presented as exact object counts. The exact 6,720-byte category normalization is:

| Allocation source | Bytes/reduction | Objects/reduction | Owner |
| --- | ---: | ---: | --- |
| Intent/result | 640 | 7 | `CORE_SEMANTICS` |
| Event records | 224 | 1 | `CORE_SEMANTICS` |
| Event collection | 704 | 4 | `COLLECTION_CHURN` |
| State copy/replacement | 3,512 | 40 | `CORE_SEMANTICS` |
| Actor lookup/projection | 352 | 4 | `TINYFARM_INTEGRATION` |
| Scene/collision query | 304 | 2 | `TINYFARM_INTEGRATION` |
| Hash/proof/inspection | 0 | 0 | `PROOF_TRACE` |
| Enumerable/LINQ | 640 | 10 | `COLLECTION_CHURN` |
| Temporary arrays/lists | 288 | 6 | `COLLECTION_CHURN` |
| Other | 56 | 1 | `PROJECTION` |
| **Total** | **6,720** | **76** | |

No frame, simulation DTO, TSON serialization, semantic hash, inspection JSON, or debug trace was in the measured movement path. The eager proof cost was instead the defensive `TinyFarmStepResult.State.DeepCopy()` plus generic batch/result collection work on every locomotion call.

## Design result

`SpatialMoveIntent` remains a sealed reference record under polymorphic `GameIntent`. Changing it to a struct would break that public inheritance contract for a small residual saving, so M15 does not change it gratuitously. The fixed caller still materializes one real intent and envelope with actor, source, minute, and sequence identity.

The resolver now separates calculation from public projection with a readonly internal `SpatialMoveReductionResult`. It contains status/reason and the old/new actor placement needed for exact arrival detection. Accepted and rejected movement, bounds, collision, facing, and Rest clearing live only in `ResolveSpatialMoveCore`. Public resolution deep-copies first and then materializes the established `IntentResult`; fixed locomotion invokes the same core against its session-owned authoritative state after all rejection checks have succeeded. Rejected movement therefore remains atomic.

Movement still emits one `ActorMoved` event per accepted reduction. On the exact step that crosses an authored arrival radius, the existing ordered event list becomes `[ActorMoved, AnchorReached]`, and the existing `AnchorReachedIntent` performs semantic location/facing/Rest completion. No event is coalesced or dropped. The common one-event case reuses its bounded event array as `RecentEvents`; only multi-result arrival steps need a combined array.

`TinyFarmState` now builds private actor, actor-scene, and actor-energy index maps with the state instance. They are derived lookup data, are not serialized, do not replace list authority, and rebuild through construction/deep copy/save-load. IDs and list positions are stable because these collections replace records but never add or remove identities during play.

Each validated `SceneDefinition` builds one private tile occupancy array after semantic validation. Validation retains its original diagnostic behavior by using the table scan before the index exists. Runtime collision is a direct indexed lookup; bounds checks still precede it. This is cold preprocessing of authored rectangles, not a broadphase or physics system.

The follower keeps its existing path collection and integer waypoint index. Its derived goal identity is now a value (`Anchor` or `Route` plus the existing ID string), avoiding a formatted string per step. A session-owned ordered actor buffer replaces per-step `OrderBy().ToArray()` and remains isolated to that session. Wander and Rest use this same follower.

The simulation host requests the no-state-snapshot form for ordinary fixed locomotion. The public session observation method still produces a defensive state snapshot for proof/inspection callers. Renderer projection remains once per draw, and simulation DTO/TSON/hash creation remains explicit-only. Fixed player movement now uses the same low-allocation reducer entry point; CLI, LLM, and replay retain the public intent surface.

## Before and after

| Metric | M14 | M15 |
| --- | ---: | ---: |
| B/movement reduction | 6,720 | 857.521 |
| ns/movement reduction | 5,953.373 | 2,705.940 |
| Gen0 / 100k reductions | 40 | 5 |
| events/accepted reduction | 1 ordinary | 1.00276 (includes exact arrival events) |
| path queries / 100k representative reductions | event-driven | 276 |
| policy evaluations / 100k representative reductions | minute/event-driven | 828 |

The 100,000-reduction after run allocated 85,752,136 bytes, an **87.24% reduction** from the M14 target. Timing is a single warmed local process measurement and is evidence of material improvement, not a brittle performance assertion. The exact semantic movement core measures **48.0004 B/reduction** and 479.197 ns/reduction; its residual is the localized immutable `ActorSceneState` replacement. Full runtime residuals are the required public event/result, intent/envelope, localized placement replacement, step result collection, and amortized arrival/policy/path refresh. The opt-in defensive state-snapshot path measures 3,443.854 B/reduction.

At the measured M15 cost, locomotion-only scale estimates are:

| Active NPCs | Reductions/second | Managed allocation/second |
| --- | ---: | ---: |
| 10 at 60 Hz | 600 | 514,513 B/s (0.491 MiB/s) |
| 100 at 60 Hz | 6,000 | 5,145,128 B/s (4.907 MiB/s) |

These are locomotion-only capacity estimates, not full simulation throughput.

## Semantic and historical proof

Focused M15 tests compare the public resolver with the internal core for accepted movement, blocked collision, out-of-bounds rejection, invalid distance, unknown actor, facing, Rest clearing, exact events, resulting semantic hash, and rejection atomicity. A separate test compares fixed-step player movement with the public `SpatialMoveIntent` path. Allocation tests cover the movement core, 100,000-reduction authoritative follower stress, trace-off runtime, and trace-on defensive snapshots.

Existing M14 tests independently retain AnchorReached-on-entry, Wander commitment, low-Energy bed travel, Rest arrival/departure, save/load mid-path, active/inactive handoff, 60/144 partition equality, irregular partitions, and Pause/Play/FastForward. The canonical 60-second semantic hash remains exactly `a0d79da0f0590d1c77d1a27bd19494e1ae68dd16ae8c46caccb20dfcbcb8fd84`; scene and schedule content hashes are unchanged.

Compact evidence is in `artifacts/tiny-farm-m15/`: `proof.json`, `allocations.json`, `performance.json`, `parity.json`, and `manifest.json`. Raw profiler traces are intentionally not committed.

## Recommended M16

Return to gameplay expansion. The remaining 857.5 B/reduction is bounded and dominated by genuinely observable intent/result/event plumbing; the semantic core itself is already 48 B. If later scale evidence makes 100-active-NPC allocation material, record a separate Core candidate for a bounded inline public movement-event/result representation. Do not block gameplay now, and do not remove `ActorMoved`, envelope identity, or immutable placement replacement without new measured pressure.
