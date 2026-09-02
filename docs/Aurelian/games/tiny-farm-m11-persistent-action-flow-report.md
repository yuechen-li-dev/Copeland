# TinyFarm M11 — persistent Open action flow and zero-allocation candidate lookup

## Outcome

**Outcome A — the Open selector is now an appropriately persistent simulation primitive.** The exact M10 Required/Open winners and all historical hashes remain unchanged. Warmed candidate retrieval allocates 0 B, ordinary Open selection falls from 3,065.75 B to 432 B per decision, and the remaining measured allocation is attributable to Dominatus 1.0.0 rather than TinyFarm catalog, trace, or result churn.

No needs, actions, windows, anchors, scenes, scoring considerations, planner, scheduler, schema, payload enum, renderer, navigation, Aurelian, or compiler behavior changed. The TSON files are byte-unchanged.

## Profile and allocation classification

The baseline was captured before implementation with 10,000 warmup decisions followed by 100,000 Open decisions. An EventPipe GC-allocation trace began after warmup and covered only the measured loop. Allocation ticks are sampled type evidence; `GC.GetAllocatedBytesForCurrentThread` supplies the exact aggregate of 3,079.10 B/decision for that traced run. The checked-in M10 benchmark baseline remains 3,065.75 B/decision.

| Prominent M10 allocation type | Sample-attributed bytes | Classification | M11 result |
| --- | ---: | --- | --- |
| candidate predicate delegate | 47,438,168 | `CANDIDATE_LOOKUP` | removed |
| `ArrayWhereIterator<TinyFarmUtilityCandidate>` | 28,141,816 | `CANDIDATE_LOOKUP` | removed |
| `TinyFarmUtilityCandidate[]` | 24,413,048 | `CANDIDATE_LOOKUP` | removed |
| schedule-window array enumerator | 18,973,680 | `TINYFARM_INTEGRATION` | removed |
| `TinyFarmUtilityScore` objects | 18,547,280 | `TRACE/EVIDENCE` | removed; score is a struct and trace is opt-in |
| TinyFarm scoring closure | 14,286,048 | `TINYFARM_INTEGRATION` | removed |
| Dominatus score tuple array | 14,178,048 | `DOMINATUS_CURRENT_IMPLEMENTATION` | remains |
| `NodeRunner` | 9,914,632 | `DOMINATUS_CORE_REQUIRED` | per-selected-state transitions remain; root is retained |
| `TinyFarmScheduleDecision` | 9,381,880 | `TINYFARM_INTEGRATION` | removed; result is a readonly record struct |
| `TinyFarmUtilityScore[]` | 9,167,600 | `TRACE/EVIDENCE` | absent in ordinary execution |
| `LiveWorldBb` | 8,315,656 | `DOMINATUS_CURRENT_IMPLEMENTATION` | remains |
| `Steps.Decide` | 4,584,592 | `DOMINATUS_CURRENT_IMPLEMENTATION` | static and reused by TinyFarm; internal decision work remains |

The post-change isolated trace measured exactly 432 B/decision. Its sampled types were the Dominatus five-option score tuple array (28,886,936 attributed bytes), per-tick blackboard callback delegate (6,608,640), `LiveWorldBb` (5,010,200), and the `AiAgent.Tick` callback closure (2,665,000). No TinyFarm candidate, score, result, window-enumerator, LINQ iterator, `Steps.Decide`, root iterator, `NodeRunner`, or state-frame type appeared in the warmed repeated-same-winner trace.

The result is therefore:

- `TINYFARM_FIXED`: candidate materialization, candidate/schedule enumeration, expected-winner LINQ, always-on score trace, reference result, repeat root step/iterator, redundant catalog/actor/minute observations, and catalog-wide locking.
- `DOMINATUS_RESIDUAL`: 432 B/decision from the score tuple array plus live context and blackboard callback plumbing.
- `TRACE_ONLY`: one exact-size array of readonly score structs, raising an inspected Open decision to 520 B/decision.

## Persistent execution and isolation

The cold lifecycle is:

```text
load TSON catalog
  -> sort canonical backing arrays and build window/candidate ranges
  -> session construction creates its schedule runtime
  -> first Open decision per actor creates that actor's AiWorld + AiAgent + HFSM
  -> retain root iterator, static Decide step, option definitions, decision memory, and result slot
  -> reuse for the session lifetime
  -> discard with the replaced session; a loaded session creates a fresh runtime
```

Every actor runtime owns its own mutable `AiWorld`, `AiAgent`, HFSM state, result slot, and defensive lock. The catalog uses a concurrent dictionary only for lazy actor creation; ordinary decisions do not serialize through a catalog-wide lock. The lock remains defensive because current TinyFarm stepping is single-threaded, but direct callers may race. A 10,000-decision Mara/Elias/Sela interleave over test-only Open windows proves exact independent winners.

The persistent runtime never holds a `TinyFarmSession`, `TinyFarmState`, navigation path, or renderer reference. Save files continue to contain semantic state only. A load reconstructs state and observations and creates a fresh session-owned schedule runtime; runtime internals are neither serialized nor restored. Replacing a session therefore releases its old runtime, and a 25-cycle load/run/replace stress test reproduces the same winner without stale observations. Static compatibility/benchmark calls retain a separate `ConditionalWeakTable` runtime keyed weakly by catalog; that path also cannot retain a session.

## Catalog, scoring, and trace law

`TinyFarmScheduleCatalog` owns one canonical candidate array, sorted exactly as before by window ID and anchor ID. A cold dictionary maps each window ID to an `ArraySegment<TinyFarmUtilityCandidate>` over that array. Known Open-window retrieval is a struct return and measures 0 steady-state managed bytes versus 88.00064 B for the former `Where(...).ToArray()` implementation.

The hot path contains no LINQ. A two-element scalar loop computes authored base score plus the unchanged current-anchor bonus. Equal scores explicitly use the existing five-option Dominatus order, so tie semantics do not depend on source row order or backing-array order. Reversing candidate source rows preserves the canonical index, trace, and winner.

Normal decisions return an empty shared score list. `includeTrace: true` materializes the existing structured candidate/score/selected-anchor shape for inspection and canonical evidence. Observability remains exact without taxing production decisions.

The catalog and active window are references. Catalog is written once per actor; repeated equal active-window and current-anchor observations are blackboard equality no-ops. Absolute minute is used to select the window in TinyFarm but is no longer boxed into the Dominatus blackboard. The Required branch still returns before runtime lookup.

## Semantic proof

The M10 canonical hybrid scenario is run through M11 unchanged. Required bedtime override, Open home and town winners, active/inactive behavior, boundary replan, handoff, save/load, navigation, projection, failure behavior, and deterministic repetition remain exact.

| Proof | Hash |
| --- | --- |
| M1 | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| M2 | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |
| M7 scene content | `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa` |
| M9 decisions | `10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6` |
| M9 anchors | `d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711` |
| M10 regimes | `fcc3e5f16a76a7dec996b357e4d323c0bf597d8eb697374be740670281fdb5b9` |
| M10 utility decisions | `388981d0b206e4c6498161e2f5bb17100b03bf297fa422de4f7a52c638ffb2ca` |
| M10 bounded anchors | `5a215ce9182065e2a8a04dc7270dd5fe1be06c65422e0c539b6aa399d0acc9b7` |
| M10 state/results/events | `92c494a84c4498e386cf450287aba8ab0b919137c97856c99639bde27344d6e7` / `875ddd21f9856166b8aaf8512a1ef42a2dca9428b69ff537c49d51f9d477a883` / `66a370e52219a6f78306b6e2a68c2f588de6a2d211e10caa4f92dea13edd02b6` |
| M10 handoff/navigation/projection | `aa7c5928a4b6a723e427d1361eead6d80242d04e8f7e9b5fe29500ddc10ef630` / `25d404f51e296b96ff70f54dd3da49ad9f14d50b5248cd3bdd6774ce4c38bc4f` / `03197805c9966b266bad110bc906f2c52604b133879b4a8b19bd448702ec5b71` |
| M11 combined semantic parity | `43bf45523928223698a477ed64349ce73241a7df7fb23a04f33255a0f3124bd6` |

## Release performance

One 100,000-decision Release run after warmup produced:

| Path | M10 ns | M11 ns | M10 B | M11 B |
| --- | ---: | ---: | ---: | ---: |
| Required | 496.356 | 60.478 | 160.0004 | 0 |
| Open | 3,092.457 | 785.356 | 3,065.74752 | 432 |

Required semantics and routing are untouched; its lower cost comes from zero-allocation indexed window enumeration and the value result. Nanoseconds are local snapshots, not CI gates. The new regression gates are 0 B for candidate lookup and at most 640 B per warmed ordinary Open decision.

The comparable unchanged RTSBenchmark Release Smoke run used 50 initial ships for 250 ticks, no checkpoints, and sequential execution. It reported 57,696.28 agent ticks/s, 519,266.49 utility option evaluations/s, 186.17 B per option evaluation, and deterministic hash `2ec6db6dd10db075`. Both paths retain agents; RTSBenchmark evaluates nine options per full agent tick and includes sensor/action/event simulation, so its figures are context rather than a direct speed ranking.

At the measured TinyFarm Open cost, isolated capacity is about 1,273,308 decisions/s. That corresponds to roughly 127,331 complete ten-NPC decision rounds/s, 12,733 hundred-NPC rounds/s, or 1,273 thousand-NPC rounds/s. These are utility-decision capacities only, not full simulation throughput; world observation, intent creation, resolution, navigation, events, persistence, and rendering are excluded.

## Validation and next pressure

M11 adds seven tests for zero-allocation lookup, a bounded Open allocation budget, opt-in trace shape, 10,000 interleaved actor decisions, source-row reorder, repeated session replacement, and missing-index failure. The complete TinyFarm suite is 124/124 green. The headless workflow now regenerates and verifies the five compact M11 artifacts. No raw trace is checked in.

The only focused remaining schedule-selector pressure is Dominatus 1.0.0's per-decision score tuple array and live-context/callback construction. That is a general Dominatus concern, not a TinyFarm-local reason to delay gameplay. The exact recommended M12 is **TINY-FARM-M12 — first bounded Proto-Sim behavior expansion using the now-qualified Required/Open primitive**, with any Dominatus allocation improvement tracked separately and attempted only if full-simulation scale demonstrates it is material.

Evidence is in `artifacts/tiny-farm-m11/{proof,allocations,performance,runtime-lifetime,manifest}.json`.
