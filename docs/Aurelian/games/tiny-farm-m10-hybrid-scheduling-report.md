# TinyFarm M10 — payload-enum hybrid scheduling

## Outcome

**Outcome A — authored hard obligations and bounded utility compose cleanly.** Production schedule days now use the nominal payload enum `ScheduleDay { Every, Day(value: number) }`. All 30,240 actor/minute decisions retain M9 behavior when Open is evaluated without state, and the historical M1, M2, M7, M9 decision, and M9 anchor hashes remain exact. One bounded Mara window adds state-sensitive choice without new locations or systems.

## Authoring model and migration

M9 authored `day: string` with `Every`, `Day1` ... `Day7`. M10 authors `day: ScheduleDay` with `ScheduleDay.Every` and `ScheduleDay.Day(6)`/`Day(7)`. Production contains no flat `Day1` ... `Day7` workaround.

`NpcSchedules` now has `windowId`, actor, payload-enum day, half-open bounds, `ScheduleRegime`, optional-by-contract `requiredAnchorId`, priority, and reason. Stable `windowId` is independent of row order. `UtilityCandidates` is a second normalized table keyed by `windowId`; each row has semantic anchor, consideration kind, base score, and current-location bonus. No C# scheduling constants duplicate those candidates.

The exact old 17:00–24:00 Mara home interval is split into:

- `mara.free-evening`, 17:00–22:00, `Open`, no required anchor;
- `mara.required-home`, 22:00–24:00, `Required`, `farm.home`.

The Open candidate set is exactly `farm.home` and `town.square`. The sole consideration is deterministic current-anchor stickiness. Scores are authored as fixed integer hundredths and materialized as finite doubles in `(0, 1]`. Home wins without an observed matching candidate, preserving migration parity; at town, town wins. Ties retain the stable static Dominatus option order.

## Selection and execution law

```text
highest-priority authored window
  -> Required: return required SceneAnchorId directly; do not enter Ai.Decide
  -> Open: evaluate only the window's authored candidates through the persistent Dominatus runtime
  -> existing NavigateToAnchorIntent / active DotRecast / inactive coarse progression
```

Required is structural control flow. It has no candidate and no inflated score. `TinyFarmScheduleDecision` exposes window ID, regime, selected anchor, and compact candidate scores for Open. Instrumented execution counts and empty Required score traces prove utility is skipped.

The generated five-anchor Dominatus graph, static `UtilityOption` array, `KeepRootFrame`, persistent world, and persistent per-actor agents remain. Disallowed anchors score zero and cannot beat positive authored candidates; the public trace contains only the bounded candidate set. No runtime, agent, root, or flow definition is rebuilt per decision.

## Hard override, realization, and persistence

At 21:59, a Mara observation at `town.square` selects town. At 22:00 the Required row immediately returns `farm.home`, discards the prior utility goal, and changes the existing semantic path identity; active realization performs a fresh navigation query. The corresponding inactive test applies the same selector and coarse semantic transition with zero path queries. This is the TinyTown endless-wander regression: a favorable stay-away utility result cannot survive the hard boundary.

Open selection and the boundary both round-trip through the existing chunked save. Candidate arrays, scores, Dominatus runners, and DotRecast paths are not persisted; identical world/time observations recompute the same winner after load.

## Validation law

Validation requires unique stable window IDs, known actors, valid half-open bounds, non-negative priority, non-empty reason, complete seven-day coverage, and one unambiguous highest-priority window at every minute. Required rows require a known anchor. Open rows forbid a required anchor and require at least one candidate. Candidates must target an existing Open ID, use a known anchor and supported consideration kind, carry finite scores, and be unique per semantic anchor. Equal-priority Required/Open or Open/Open overlaps fail because their stable window IDs differ.

## Queries and roundtrip

The real CLI successfully executed these production queries with zero diagnostics:

```powershell
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-npc-schedules.obj.ts NpcSchedules --where 'day == Day(6)' --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-npc-schedules.obj.ts NpcSchedules --where 'day == ScheduleDay.Day(6)' --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-npc-schedules.obj.ts NpcSchedules --where 'regime == Required' --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-npc-schedules.obj.ts NpcSchedules --where 'regime == Open' --format json
```

`Day(6)` returns exactly `mara.day6-store`; Required returns 11 rows; Open returns only `mara.free-evening`. The existing TSON reader/canonical-printer roundtrip preserves the nominal enum case and payload, as covered by the INFRA-M10A compiler regression and production load/query proof.

## Evidence

Release evidence recorded:

| Path | ns/decision | B/decision |
| --- | ---: | ---: |
| M9 Required-equivalent before M10 (INFRA-M10A Release) | 5325.743 | 1149.7461 |
| M10 Required | 496.356 | 160.0004 |
| M10 Open utility | 3092.457 | 3065.7475 |

Required is materially cheaper because it never enters Dominatus utility evaluation. Open retains a few KiB of ordinary decision overhead; M10 records it and does not redesign Dominatus.

Compatibility hashes:

- M1: `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`
- M2: `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3`
- M7 scene content: `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa`
- M9 decision: `10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6`
- M9 anchor sequence: `d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711`

New behavior hashes:

- regimes: `fcc3e5f16a76a7dec996b357e4d323c0bf597d8eb697374be740670281fdb5b9`
- utility decisions: `388981d0b206e4c6498161e2f5bb17100b03bf297fa422de4f7a52c638ffb2ca`
- bounded anchor sequence: `5a215ce9182065e2a8a04dc7270dd5fe1be06c65422e0c539b6aa399d0acc9b7`
- state: `92c494a84c4498e386cf450287aba8ab0b919137c97856c99639bde27344d6e7`
- results: `875ddd21f9856166b8aaf8512a1ef42a2dca9428b69ff537c49d51f9d477a883`
- events: `66a370e52219a6f78306b6e2a68c2f588de6a2d211e10caa4f92dea13edd02b6`
- handoff: `aa7c5928a4b6a723e427d1361eead6d80242d04e8f7e9b5fe29500ddc10ef630`
- navigation: `25d404f51e296b96ff70f54dd3da49ad9f14d50b5248cd3bdd6774ce4c38bc4f`
- projection: `03197805c9966b266bad110bc906f2c52604b133879b4a8b19bd448702ec5b71`

The compact evidence set is `artifacts/tiny-farm-m10/{proof,migration-parity,regimes,utility-decisions,manifest}.json`.

## Future Proto-Sim fit and M11

Later needs or personality can add observations to the same bounded candidate scoring path: hard schedule, then authored candidate set, then utility. They must not weaken Required control flow or broaden candidates globally.

The exact recommended M11, based on observed pressure, is **TINY-FARM-M11 — persistent ordinary NPC action flow plus zero-allocation candidate lookup**, limited to reusing a persistent `TinyFarmNpcFlow` agent/runtime and indexing candidates by window ID. Do not add needs, personality, planner, behavior tree, scheduler framework, or new content.
