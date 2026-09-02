# TinyFarm M12 — Energy, personal living space, and Rest

## Outcome

Outcome A. M12 adds exactly one need, Energy, and proves the intended Proto-Sim loop through the real persistent Dominatus, semantic navigation, resolver, persistence, inspection, and MonoGame paths. No generic needs system, second scheduler, planner, DotRecast change, or additional need was added.

## Existing ownership audit

Before M12, `farm.home` was one shared abstract anchor in the outdoor Farm scene. The blocked `farmhouse` landmark had no route, interior, owner, bed object, or rest semantics. Mara's 22:00 Required window and Sela's home windows shared it; Elias remained at `farm.work-area` through midnight. Only Mara's 17:00–22:00 window was Open.

M12 retains the M1–M11 TSON catalog as an immutable historical catalog and adds an explicitly versioned `Content/M12` catalog. `LoadM12()` is the game path; old proof runners continue to load the historical catalog and therefore retain their exact hashes.

## Living space and authored content

The smallest believable extension is one 12×8 `residence` scene, Hearth House, containing three visibly separate owned beds. The existing route table supplies `farm-residence` and `residence-farm`; there is no renderer-owned transition.

| NPC | Bed object | Bed/rest anchor | Required bedtime |
| --- | --- | --- | --- |
| Elias | `elias-bed`, `Bed`, semantic reference `elias` | `elias.home-bed` | 22:00 |
| Mara | `mara-bed`, `Bed`, semantic reference `mara` | `mara.home-bed` | 22:00 |
| Sela | `sela-bed`, `Bed`, semantic reference `sela` | `sela.home-bed` | 22:00 |

All coordinates, objects, layout rows, anchors, and routes are TSON-authored. Core validation already rejects blocked/out-of-bounds anchors and invalid object/route joins. The M12 definition boundary additionally requires one unique `Bed` object and one `Rest` anchor per NPC, matching owner references, an own-bed Required window, and no Open Rest candidate targeting another NPC's bed.

The direct content queries answer: Mara owns `mara-bed`; Elias's bedtime target is `elias.home-bed`; `mara.free-evening`, `elias.free-evening`, and `sela.free-evening` allow Rest. These are ordinary typed table projections; no TableScript feature was added.

## Energy and Rest law

Energy is explicit TinyFarm state, not Dominatus state. It is a deterministic integer in `[0, 10000]`, initialized to 9000 for each NPC. Authoritative elapsed minutes apply exactly `-8 units/minute` while active and `+40 units/minute` while resting, with exact clamping. Integer addition makes the scalar law time-partition independent. Renderer frames and wall-clock time never enter it.

`ActorEnergyState` contains the actor ID, Energy units, and `IsResting`. Version-5 chunked saves restore all three exactly. Energy and Rest state participate in the semantic hash only for M12 states, leaving earlier version hashes unchanged. Invalid, missing, duplicate, player-owned, or out-of-range Energy rows fail save validation.

Bed arrival is an ordinary `AnchorReachedIntent` handled by `TinyFarmResolver`; it enters Rest only at a personal bed. Selecting a different goal clears Rest before normal movement begins. Active NPCs use the existing DotRecast route; inactive NPCs use coarse scene progression and issue no DotRecast query. Tired and resting active/inactive handoffs retain the same Energy, goal, regime, and Rest semantics.

## Open utility and Required control flow

Existing authored base and current-anchor stickiness remain. A candidate whose `considerationKind` is `energy-rest` receives:

```text
energy contribution = (10000 - Energy) / 10000 × 0.8
final = base + current-anchor stickiness + energy contribution
```

The Rest base is 0.10; each competing activity base is 0.50 with its existing 0.20 stickiness. The contribution is monotonic: it is 0 at full Energy and 0.8 at zero. At Energy 9000, Mara at town selects `town.square`; at Energy 1000, `mara.home-bed` scores 0.82 and wins. This reproduces the TinyTown wandering/exhaustion failure class without creating an exhaustion regime.

At 22:00, Required returns the NPC's personal bed before candidate lookup, Dominatus utility, or trace materialization. Even full Energy and maximal non-home preference cannot alter that result. The trace is empty, preserving the law: Required is a structural obligation; Open utility responds to changing agent state; needs never weaken Required control flow.

The persistent M11 runtime remains. Personal beds share the existing single home utility option slot, so the generated graph remains five options rather than growing by one option per owner. Normal execution is trace-free. Requested traces expose candidate, base, stickiness, Energy contribution, final score, and selection.

## Inspection, LLM control, and visual proof

Structured inspection exposes actor, integer Energy, `isResting`, regime, selected semantic goal, and the opt-in score trace. Existing `--llm-control` inspection therefore answers where Mara is, her Energy, and what she is doing without renderer coordinates or LLM scheduling authority.

The runner also provides a deterministic, renderer-free control surface for M12:

```powershell
dotnet run --project src/TinyFarm/TinyFarm.Runner/TinyFarm.Runner.csproj -- --m12-control
scenario low-open
inspect
wait 10
frame
quit
```

`scenario` accepts `high-open`, `low-open`, `bedtime`, and `resting`. Each phase creates a canonical state through the same definitions, session, resolver, schedule runtime, inspector, and frame projector used by the game. Commands can be piped on stdin, so behavior and presentation data can be exercised without Windows input automation.

MonoGame remains a projection/input leaf. At its default 2560×1440 window, Hearth House visibly shows three labeled beds, the player can leave through the authored portal, and visible NPC labels show `ENERGY`, `REQUIRED`/`OPEN`, or `RESTING`. Manual release-build inspection confirmed the layout fills the viewport legibly and the three bed labels do not overlap. The active headless scenario separately proves Mara's real DotRecast walk to her bed and transition to Rest.

## Determinism, persistence, and hashes

The canonical scenario proves high/low Energy selection, active arrival, offscreen Rest, tired/resting handoffs, low-Energy and resting save/load, one-day repeat, and seven-day repeat. Current hashes are recorded in `artifacts/tiny-farm-m12/proof.json`; the scene content hash is `7c40c8070ce4a944fd7fbd655d6d9eda607397d19215ca873fd4b039353be600` and the schedule content hash is `d5f62e2fab9e22d1cb717f6d8e26a9423f657065682a37937c32e5a3260e2564`.

Historical hashes remain exact: M1 `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`, M2 `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3`, and the M7 scene hash `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa`. The full M1–M11 suite runs against the preserved historical catalog.

## Performance

One Release evidence run measured:

| Path | M11 qualified | M12 |
| --- | ---: | ---: |
| Required ns | 53.56 current regression run | 428.32 |
| Required B | 0 | ~0 (0.002 measured) |
| Open ns | 848.38 current regression run | 5199.01 |
| Open B | 432 | 432 |
| Candidate lookup B | 0 | 0 |
| Energy scorer B | n/a | 0 |

Timing is a local microbenchmark snapshot, not a simulation-throughput claim. The semantic change retains the qualified allocation shape: no TinyFarm-local per-decision allocation was added, candidate lookup remains 0 B, and the Energy scorer is 0 B.

## Evidence and scope boundary

The compact artifact set is exactly `proof.json`, `energy.json`, `behavior.json`, `homes.json`, and `manifest.json`. Tests cover personal ownership, structural bedtime, monotonic scoring, high/low winners, active arrival/recovery, inactive Rest, tired/resting handoff, leaving bed, save/load, inspection, clamping, time partitioning, invalid Energy state, and allocation boundaries.

The next observed pressure is not a second need. M13 should be a bounded **rest-duration and wake-transition playback** milestone: let the current one-day trace demonstrate an NPC recovering enough Energy to leave bed during Open time, then visually follow that same NPC through the authored exit and back to a non-home goal. It should add no Hunger, personality, planner, or generic need abstraction.
