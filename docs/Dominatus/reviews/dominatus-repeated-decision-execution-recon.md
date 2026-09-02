# INFRA-M10A Dominatus repeated decision execution recon

## Outcome

Outcome A. TinyFarm was constructing and initializing a complete Dominatus runtime for every schedule lookup. RTSBench reuses its world and agents and repeatedly ticks an HFSM configured with `KeepRootFrame`. TinyFarm now uses that existing path. No new decision primitive, alternate utility law, or schedule-specific decision implementation was added.

The classification is `MIXED`:

- `TINYFARM_INTEGRATION_MISUSE`: the dominant avoidable cost was per-call agent, graph, world, runner, task, and initialization construction.
- `MEASUREMENT_MISMATCH`: RTSBench reports each of nine option evaluations as a “decision”; TinyFarm's reported operation is one complete five-option selection and result extraction.
- `DOMINATUS_GENERAL_EXECUTION_OVERHEAD`: both paths still allocate score arrays, emitted steps, iterator/context plumbing, blackboard change tracking, and action-state machinery.

## Execution pipelines

| Stage | RTSBench | TinyFarm before | TinyFarm after |
| --- | --- | --- | --- |
| Graph construction | Once per ship | Every selection | Once per catalog/actor |
| Option construction | Nine options every root iterator pass | Static five options | Static five options |
| Context creation | `AiCtx` through `NodeRunner` per tick | Same, plus new runtime | Same |
| Blackboard reads | Typed keys; extensive sensor/utility reads | Typed actor/minute/catalog/anchor keys | Same typed keys |
| Coroutine/iterator | Persistent root plus action runner | New root and action iterators | Persistent root plus action runner |
| Decision result | Reads `CurrentAction`, stages `ShipAction` | Allocates `TinyFarmScheduleDecision` | Same result contract |
| Events | Benchmark mailbox/event phases | Aurelian runner/task plumbing even though unused | No event dispatch for direct agent tick |
| Diagnostics | Metrics and blackboard change journal | Blackboard change journal plus wrapper plumbing | Blackboard change journal |
| Boxing | Blackboard object storage boxes value types | Minute is boxed; actor/anchor are strings; catalog is a reference | Same boxing law |
| Collections | Score array, options array, actions, metrics | Score array, HFSM stack, runtime objects | Score array, HFSM state, catalog iteration |

### RTSBench exact path

`BattleSimulation.RunTick` enters `DecisionPhase`, selects the sequential mode, calls `TickAgentForDecision`, and invokes `agent.Tick(_world)`. `HfsmInstance.Tick` advances the persistent root `NodeRunner`; `DecideNode` yields `Ai.Decide`; `ApplyDecision` evaluates nine `UtilityOption`/`Consideration` values, applies stable first-best/tie/commit laws, and retains decision memory. With `KeepRootFrame`, it pushes the selected action state. The action state's iterator writes `CurrentAction` and yields `Steady`; the benchmark reads that value, constructs `DecisionWorkResult`, stages a `ShipAction`, sorts actions, resolves them, and delivers events.

### TinyFarm path after the fix

`TinyFarmNpcSchedule.Decide` validates the actor and selects the authored window used as the semantic cross-check. A `ConditionalWeakTable` obtains one runtime for the immutable schedule catalog. That runtime owns one `AiWorld` and one generated-flow `AiAgent` per actor, updates the typed actor/minute/catalog observations, and calls `agent.Tick(_world)`. The generated root yields the same `Ai.Decide` law over the static five options. With `KeepRootFrame`, repeated observations re-evaluate the same decision slot and push a new anchor action only when the winner changes. The selected action writes `SelectedAnchor`; extraction waits until it agrees with the independently selected authored window and returns the existing semantic result record.

The runtime is locked because `AiWorld`, HFSM stacks, decision memory, and blackboards are mutable. State is never shared across catalogs, and each actor owns a separate agent.

## Allocation evidence

Exact aggregate allocation uses `GC.GetAllocatedBytesForCurrentThread`. Type histograms use EventPipe `GCAllocationTick`; their byte weights are sampled estimates, not a second aggregate measurement. Raw traces were deliberately not retained.

TinyFarm's largest sampled types after the fix were the schedule-window array enumerator, `BbDeltaEntry[]`, the Dominatus score tuple array, `NodeRunner`, blackboard `Action` callback, `LiveWorldBb`, the semantic result record, `StateReturnSlot`, `CancellationTokenSource`, and the emitted `Decide` step. RTSBench was led by its per-pass `UtilityOption[]`, score tuple array, `BbDeltaEntry[]`, strings/action parsing, blackboard callback/context objects, `ShipAction`, and emitted `Decide` step.

This explains the remaining roughly 1.0-1.25 KiB per complete TinyFarm selection. It is no longer the roughly 5.8-6.4 KiB full-runtime construction furnace. The remaining costs are shared Dominatus execution machinery plus TinyFarm's semantic result and catalog enumeration; no schedule graph or option is rebuilt.

## Comparable performance

| Workload | Profile | ns/decision | Decisions/sec | Bytes/decision |
| --- | --- | ---: | ---: | ---: |
| TinyFarm before | Debug | 7,391 | 135,292 | 5,939 supplied M8 baseline |
| TinyFarm after | Debug | 4,430 | 225,748 | 1,254 |
| TinyFarm before | Release | 7,606 | 131,480 | 6,554 supplied M9 baseline |
| TinyFarm after | Release | 5,326 | 187,767 | 1,150 |
| RTSBench baseline | Release | 8,134 per utility evaluation | 122,945 utility evaluations/sec | 222 per utility evaluation |
| RTSBench after | Release | unchanged | unchanged | unchanged |

RTSBench's complete agent selection is nine utility evaluations: 1,993.752 bytes per agent tick and 13,660.55 agent ticks/sec in the comparable Release smoke run. Its `decisions/sec` label is therefore not a complete-selection throughput figure. Both Debug and Release produced determinism hash `5a60fc4dc42a38b8`; no RTSBench source changed.

NativeAOT was attempted, but the current sample graph is not publishable under the propagated AOT profile: `Dominatus.OptFlow.Generators` fails with `NETSDK1207` because its target framework is not AOT publishable. No NativeAOT comparison is claimed.

## Semantic result

TinyFarm M9 retained its exact schedule decision, anchor sequence, state, result, event, handoff, navigation, and projection hashes. The generated definition and five `UtilityOption` objects remain static. `KeepRootFrame` and persistent per-actor agents are the already-existing Dominatus repeated-decision path; no Core API or semantic implementation changed.
