# TINY-FARM-M1 — Headless Ariadne Adventure Report

## Outcome

**Outcome A — Success.** A complete, renderer-free text adventure now proves the agent-native engine thesis on the real Dominatus 1.0.0 and Ariadne paths. The canonical day repeats exactly, a save-loaded continuation equals uninterrupted execution, and semantic truth is independent of prose.

Baseline: Copeland `72f472b3` on `main`; Dominatus reference authority `adbecd9` on `master`. Both worktrees were clean when inspected. M0's current baseline was 3,167 JointTaskForce tests plus 392 targeted Dominatus tests. M1 adds 15 focused tests.

## Ownership map

| Concern | Final owner | Evidence |
| --- | --- | --- |
| Aurelian reusable runtime | `Aurelian.Runtime` | existing `SequentialAurelianDominatusWorldRunner`; no new abstraction |
| authoritative game truth | `TinyFarm.Core` | state, IDs, content, intents, resolver, semantic hash |
| agent composition | `TinyFarm.Runtime` | bounded observations, generated Dominatus flow, intent adapter |
| narrative presentation | `TinyFarm.Runtime` | generated Ariadne flow maps semantic dialogue topics to lines |
| save/session/replay | `TinyFarm.Runtime` | sequence cursor, semantic recent results, versioned game/agent/narrative sections |
| command host | `TinyFarm.Runner` | scripted proof and optional REPL |
| MonoGame/Machina/Oblivion | not used | M1 is headless; JSON inspection is sufficient |

`TinyFarm.Core` has no project or package references. Runtime references Core, Aurelian.Runtime, Dominatus.OptFlow 1.0.0, and Ariadne.OptFlow 1.0.0. The runner and tests are leaves.

## Engine and game model

The authoritative `TinyFarmState` contains integer game minutes, four stable locations, four typed actors, four typed items, actor money/inventory, bounded world facts, and the four-stage Mara/Elias favor. Public collections are read-only; production mutation access is internal to Core. Initialization constructs state, load validates and reconstructs it, and `TinyFarmResolver` is the only production writer.

`GameIntent` is a closed record family: Move, Look, Talk, Take, Give, Buy, Sell, and Wait. `IntentEnvelope` records actor, simulation minute, monotonic sequence, and Human/Dominatus/Replay source. Resolution orders by submitted minute, sequence, then ordinal actor ID. Source does not affect rules. Expected invalid actions return Accepted, Rejected, or NoOp with a typed reason.

Semantic events cover looking, movement, conversation topics, item transfer/trade, time, and favor progression. Events are transition evidence; no bus stores world truth. Ariadne sees only a `DialogueTopic` emitted after resolution and returns surface lines. Generated prose is absent; the hash excludes all prose.

The game clock is an integer minute counter with no wall-clock or randomness. The store opens from 09:00 to 18:00. Mara, Elias, and Sela receive bounded observations containing self, local actors, inventory, time, recent semantic event kinds, and scheduled destination. Dominatus `Ai.Decide` chooses Move or Idle in a source-generated three-state flow. The adapter emits the same `MoveIntent`/`LookIntent` records used by a human or replay. Agent blackboards contain copied observation/decision primitives only.

Inventory and economy are deliberately ordinary game rules. Each item has one owner or ground location; buy/sell transfers the item and balances through the resolver. The favor advances from talking to Mara, to carrying a sealed letter, to giving it to Elias, to returning for thanks. No quest, inventory, economy, relationship, ECS, event-bus, DI, scene-graph, scripting, or persistence framework was introduced.

Static definitions remain four explicit C# records. A TSON loading boundary would cost more code than the M1 content itself; mutable state was not forced into static authoring. M2's larger crop/item catalog is the first natural TSON qualification point.

## Save, replay, and inspection

`TinyFarmSave` is versioned and composed from Game, Runtime, Agents, and Narrative sections. Game holds authoritative state. Runtime holds the next deterministic sequence and recent semantic events. The agent section declares the observation-pure Dominatus 1.0.0 boundary; the narrative section declares derived Ariadne 1.0.0 prose. There is no RNG state because M1 uses no randomness and no Dominatus checkpoint because agents retain no cross-decision memory.

Load validates versions, unique actor/item IDs, exactly one item container, and inventory/owner agreement before constructing a fresh session. The semantic SHA-256 input canonicalizes actors, items, inventory, and facts by stable ID and excludes logs, prose, runtime object identity, and collection iteration order.

The headless runner supports the canonical script and `--repl`. The REPL accepts all eight intent verbs plus `inspect` and `quit`. Inspection emits current state/hash, actor summaries, bounded NPC observations, an empty committed intent queue, and last typed results as JSON.

## Proof results

The 16-command canonical script runs from 08:00 to 18:00. It talks to Mara, shops with Sela, waits into NPC schedules, finds Elias and Mara at the river, takes mint, delivers the letter, completes the favor, sells the mint, and advances into evening.

| Proof | Result |
| --- | --- |
| repeated uninterrupted run | identical final state, result/event sequence, and hash |
| save/reload at command 8 | identical final hash and complete result/event sequence |
| semantic hash | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| conflicting Take(wild-mint) | Mara wins by actor-ID tiebreak; player receives `ItemAbsent` |
| invalid Give(store-apple, Mara) | typed `ItemNotOwned` rejection |
| autonomous wait | Mara and Elias both submit accepted moves |
| Ariadne interaction | four semantic conversations realized through `Diag.Line` |
| save size | 3,888 bytes in the canonical proof run |
| replay evidence size | 5,831 bytes |
| canonical-day runtime | approximately 0.1 seconds on the development machine; no absurd overhead |

The deterministic proof is the authority for variable timing and serialized sizes; the tracked JSON records the measured run.

## Mutation audits

- Authoritative state writes occur only in initial construction, validated load construction, and resolver-local copies. Session replacement installs resolver/load output; callers cannot mutate state collections.
- Dominatus blackboards contain current/destination strings and the selected action only. No position, inventory, money, facts, or quest stage is duplicated there.
- Ariadne receives a dialogue-topic string and emits lines. It cannot mutate `TinyFarmState`.
- Save/load reconstructs the same state model and validates normal invariants; it has no recovery mutation path.
- MonoGame, GPU, windowing, Machina, Oblivion, and Aurelian graphics implementations are absent from the runner path.

## Engine extraction table

| Concept | Implemented where | Why | Reusable? | Evidence |
| --- | --- | --- | --- | --- |
| Dominatus world ticking | existing Aurelian.Runtime | already shared and qualified | yes, already shared | NPC flow runs through sequential Aurelian runner |
| authoritative state | TinyFarm.Core | domain semantics | no | favor/economy/location records |
| intent envelope/order/resolver | TinyFarm.Core | first product proof | game-local first | human/NPC conflict and replay tests |
| observation/decision seam | TinyFarm.Runtime | adapts game data to Dominatus | shape may recur, API not yet shared | autonomous NPC proof |
| save composition | TinyFarm.Runtime | sections are meaningful now | game-local first | save/reload equivalence |
| semantic hash/replay | TinyFarm.Core/Runtime | state shape and script are game-specific | game-local first | exact repeated hash/sequence |
| narrative realization | TinyFarm.Runtime | Ariadne adapter | adapter pattern reusable, content local | generated one-state dialogue flow |

## M0 conclusions and next milestone

Confirmed: authoritative state, typed deterministic intent resolution, and save composition are sufficient for an actual tiny game; human and agent control can share one resolver; Dominatus need not own physical truth; prose can remain a projection; no ECS is needed.

Changed: the requested M1 is one day rather than M0's proposed week; existing Aurelian runtime ticking is reused now; Dominatus 1.0.0 source generation makes handwritten graph construction unnecessary; stateless scheduled agents make a persistent Dominatus checkpoint unnecessary in this slice; TSON is deferred until content volume justifies it.

Exact stopping boundary: M1 ends after the complete ten-hour headless adventure and its deterministic/save/replay proof. Farming, a week simulation, crop/day transitions, graphics, and generalized engine extraction are intentionally absent.

**Recommended M2: A — headless deterministic week with farming/economy.** Add crop definitions plus Plant/Water/Harvest and day-transition rules to the existing state/resolver/save path; qualify TSON for that expanded static catalog. Do not start graphics yet.
