# TINY-FARM-M2 Headless Deterministic Week Report

## Outcome and baseline

**Outcome A.** Baseline revision was `0122a4f9` on `main`, with a clean worktree. TinyFarm was 15/15 before editing. The M1 runner reproduced hash `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`, save/reload equivalence, replay equivalence, one deterministic conflict rejection, and autonomous NPC movement. M2 keeps a separate version-1 construction/hash path, and the final 24-test TinyFarm suite includes that exact hash as a regression.

## Persistence audit and decision

Decision: **REUSE_AS_IS**.

Dominatus 1.0.0 already supplies `ChunkId`, `SaveChunk`, `SaveWriteContext`, `SaveReadContext`, `ISaveChunkContributor`, and `SaveFile`. `SaveFile` writes a `DOM1` container with file version 1, ordered stable chunk IDs, length-delimited byte payloads, duplicate-ID rejection, complete-read checks, negative/truncated length rejection, and trailing-byte rejection. JSON is deliberately a payload concern; `SaveWriteContext.AddUtf8Json` and `SaveReadContext.TryGetUtf8Json` provide that seam. Storage is an explicit file path/stream owned by `SaveFile`. Chunk order is stable as written, while semantic identity is computed from reconstructed canonical game state rather than save bytes.

TinyFarm uses exactly four semantic chunks:

| Chunk | Owner | Contents | Version behavior |
| --- | --- | --- | --- |
| `tinyfarm.world` | TinyFarm.Core | time, actors, legacy item instances, product stacks, plots/crops, shop stock, facts, content provenance | requires game version 2 and `tiny-farm-m2@2` |
| `tinyfarm.runtime` | TinyFarm.Runtime | next intent sequence and recent semantic events | required |
| `tinyfarm.agents` | TinyFarm/Dominatus adapter | runtime identity and declaration that schedules are observation-pure | required; no duplicated world truth |
| `tinyfarm.narrative` | TinyFarm/Ariadne adapter | runtime identity and semantic/prose ownership declaration | required; no prose history |

The load path reconstructs and validates a complete candidate before creating a visible session. Missing required chunks, malformed JSON, unsupported runtime/game versions, incompatible definition identity, invalid stacks, and unknown crop IDs fail with `InvalidDataException`. Unknown optional chunks are ignored. Tests prove all of these cases and prove no partial session is produced. The implementation introduces no serializer framework and no second chunk system: one `System.Text.Json` option set supplies game payloads inside the one Dominatus container. A Dominatus checkpoint is still unnecessary because each NPC decision is observation-pure and no authoritative or continuation-relevant agent memory exists.

## Authored definitions and provenance

`Content/tiny-farm-definitions.obj.ts` is a self-described Copeland TSON record table loaded with `TsonDocumentReader`. Its rows define stable product IDs, names, buy/sell prices, crop identity, seed/harvest products, three watered day-boundaries of growth, one watering requirement, and yield two. The loader projects immutable `ItemDefinition` and `CropDefinition` records. Runtime state refers to `ProductId`/`CropId`; it never mutates TSON values or indexes rows by position.

The definition-set identity is `tiny-farm-content-m2-sha256:` plus SHA-256 of the authored source. It participates in world hashing and every save. Loading against other definitions, or a save containing an unknown crop ID, fails explicitly.

## Authoritative farming, economy, and time

TinyFarm.Core adds typed `ProductId`, `CropId`, and `FarmPlotId`. Two plots at the farmhouse contain optional crop ID, planted day, growth stage, and today's watered flag. Product inventory is an actor/product/count stack, while shop stock is product/count/daily-restock count. Static definitions and live state are separate objects.

`PlantIntent`, `WaterIntent`, `HarvestIntent`, `BuyProductIntent`, and `SellProductIntent` enter the same actor-generic `TinyFarmResolver` as all M1 intents. Accepted transitions emit `CropPlanted`, `PlotWatered`, `CropAdvanced`, `CropHarvested`, `ItemBought`, `ItemSold`, `DayStarted`, and `ShopRestocked` evidence. Normal failures are typed: unknown crop/plot/item, missing seed, occupied/empty plot, wrong location, already watered, immature crop, closed/absent shop, insufficient funds, unavailable stock, and unowned sale.

Time remains an integer game minute. `WaitIntent` is still capped at 240 minutes. Crossing midnight is the one explicit daily boundary: each watered planted plot advances exactly one bounded stage, watering resets, and finite seed stock resets to three. No wall clock, per-frame mutation, or ambient randomness exists.

The shop has finite daily-reset seed stock. Prices come only from TSON. The canonical loop buys one seed for 2, plants and waters for three day boundaries, harvests two turnips, sells each for 5, reinvests, repeats, and finishes with player money 28 from an initial 12, zero product inventory, two empty plots, and seed stock three. Tests cover inventory conservation. A final-stock conflict submitted in reverse input order still awards the item to actor `mara` and rejects the player with `StockUnavailable`, proving stable actor ordering over the new resource.

## Weekly agents and narrative

Mara has weekday, Saturday market, and Sunday riverside destinations. Elias and Sela retain recurring work/home schedules. Scheduled movement and Mara's Saturday seed purchase are selected by the existing three-state generated Dominatus flow and emitted as ordinary `MoveIntent`/`BuyProductIntent` values. The canonical week records one NPC purchase and 72 autonomous NPC moves while the player spends long periods waiting. Human, NPC, and replay envelopes share one resolver and no player-only mutation API exists.

Ariadne remains a projection over semantic topics. Harvest and week-end events add bounded authored lines; the canonical week produces three lines. Saved recent semantic events are sufficient for continuation. Prose and logs are not authoritative or required for reconstruction.

## Canonical proof

The 62-intent script spans day 1 09:00 through day 7 09:00. It exercises two complete buy/plant/water/grow/harvest/sell loops, recurring schedules, daily stock turnover, long waits, NPC commerce, and semantic dialogue.

| Evidence | Result |
| --- | --- |
| final SHA-256 | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |
| repeated state/result/event sequences | exact match |
| reload points | 3, 6, 13, 20, 27, 44; all match uninterrupted hash |
| final state | day 7 09:00; money 28; turnips 0; both plots empty; favor unchanged |
| save size | 2,926 bytes versus M1 3,888 bytes |
| compact replay evidence | 25,378 bytes versus M1 5,831 bytes; pretty sample is 28,600 bytes |
| observed week / average day | about 165,000 / 23,500 microseconds |
| observed save / load / hash | about 89,000 / 22,000 / 3,800 microseconds |

Timings are single-run development-machine observations, not performance guarantees. The replay grew because M2 records complete result and semantic-event signatures for 62 human turns plus NPC decisions; it remains below the 256 KiB artifact limit. Allocation optimization was not justified; bounded transient Dominatus flow instances and JSON payloads are the visible allocation sources.

The JSON inspector now includes day/minute, semantic hash, actors/money, NPC observations, recent results, plots, product inventories, and shop stock. The REPL supports `buy-product`, `sell-product`, `plant`, `water`, and `harvest`. No graphics, MonoGame, ECS, scene/update system, event framework, or RNG was added.

## Mutation and scalability audits

All authoritative writes are attributable to initialization, validated load reconstruction, or `TinyFarmResolver`. Farm state is not writable by UI, Ariadne, Dominatus blackboards, or TSON definitions. Money, inventory, and stock change only in trade/farm resolution or daily resolution. Agent blackboards contain transient observations/choice only. Narrative prose remains derived.

There are 13 closed intent record types. One coordinator dispatches to explicit intent-family methods, each validating and committing against a private deep copy. Day transition is one named resolver operation. This stayed readable without hidden `System.Update()` order or ECS-like component queries. Duration created pressure for typed calendar, plot, stock, and provenance records—not for ECS. No reusable Aurelian abstraction was added.

| Concept | Owner | Evidence | Second-game plausibility | Decision |
| --- | --- | --- | --- | --- |
| plot/crop/product/economy rules | TinyFarm | only this authored world defines them | low | retain locally |
| intent coordinator | TinyFarm | M1/M2 scale cleanly, no second game | medium | retain locally |
| day-boundary rule | TinyFarm | crop and shop semantics are game-specific | medium | retain locally |
| semantic hash/replay signatures | TinyFarm | schema-specific canonical fields | medium | retain locally |
| sequential agent runner | Aurelian | reused unchanged | proven | reuse existing |
| chunk container | Dominatus | integrity and extension seam used unchanged | proven | reuse existing |

| Persistence concern | Owner | Dominatus mechanism reused? | Extension required? | Why |
| --- | --- | --- | --- | --- |
| container/chunk framing | Dominatus | yes, `SaveFile`/`SaveChunk` | no | already deterministic and validated |
| JSON payload mechanics | Dominatus seam + System.Text.Json | yes, UTF-8 contexts | no | game schemas remain game-owned |
| semantic save schema/version | TinyFarm | container only | no | domain compatibility is product law |
| content provenance | TinyFarm | carried in world chunk | no | authored-definition identity is game-specific |
| agent continuation | TinyFarm adapter | metadata chunk | no checkpoint needed | decisions are observation-pure |
| narrative continuation | TinyFarm adapter | metadata plus semantic recent events | no | prose is derived |

## CI, artifact budget, and recommendation

The Windows `TinyFarm headless M2` lane builds/tests the headless solution, runs the canonical proof, requires Outcome A, exact repeat result/event equality, all reload points, day 7, zero crop residue, and at least one NPC purchase, then enforces the repository artifact budget. The local TinyFarm suite passes 25/25, Aurelian passes 606/606, and JointTaskForce passes its 2,854 discovered tests. `git diff --check` is clean.

The M0/M1 ownership thesis held: duration and recurring systems fit authoritative state, agents, typed intents, deterministic resolution, time, save/replay, and presentation. The only persistence change was replacing M1's single JSON document with Dominatus's existing chunk container and game-owned payload schemas.

**Recommended M3: A — first graphical projection.** Project the immutable M2 inspection/presentation state through a narrow renderer adapter. Do not move farm/economy authority or introduce ECS while doing so.
