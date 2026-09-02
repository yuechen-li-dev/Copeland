# TinyFarm M9 — TSON-authored NPC schedule windows

## Outcome

**Outcome A — TSON cleanly becomes NPC schedule-content authority.** The eleven M8 rows now exist only in `src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-npc-schedules.obj.ts`. The existing generated Dominatus OptFlow remains the schedule-selection authority and produces the exact M8 decision, anchor, navigation, handoff, state, result, event, and projection hashes.

## Authority inventory and boundary

Before M9, `TinyFarmNpcSchedule.ScheduleWindows` was the production authoring authority: eleven `new TinyFarmScheduleWindow(...)` declarations held actor, optional day, interval, anchor, priority, and reason. `Score`, `SelectWindow`, the five generated-flow states/options, actor observation, priority comparison, half-open matching, and unknown-actor failure were runtime logic rather than authored content.

M9 deletes that array. The production path is:

```text
tiny-farm-npc-schedules.obj.ts
-> TsonDocumentReader in TinyFarmDefinitionLoader
-> TinyFarmScheduleDay + TinyFarmScheduleWindow
-> validated immutable TinyFarmScheduleCatalog indexed by ActorId
-> existing generated Dominatus OptFlow
-> SceneAnchorId
-> existing active/inactive spatial realization
```

Raw `TsonTable`, `TsonValue`, and authored day tokens are loader-local. Core, the Dominatus selector, navigation, projection, MonoGame, and saves see only typed values. `TinyFarmDefinitions` owns the loaded catalog, so a session has one content authority and decisions perform no file, parser, or TSON work.

## Authored schema and typed model

The one nominal root is `NpcSchedules`. Its exact columns are `actorId: string`, `day: string`, `startMinute: number`, `endMinuteExclusive: number`, `anchorId: string`, `priority: number`, and `reason: string`. `reason` remains because it is part of M8 inspection and decision evidence.

The authored day tokens are exactly `Every` and `Day1` through `Day7`. They are converted once to `TinyFarmScheduleDay.EveryDay` or `TinyFarmScheduleDay.Day(n)`. This keeps the runtime typed and avoids magic integer sentinels. A nominal payload enum was tested, but the existing TableScript C# query executor could list it while failing a payload-case predicate; the validated string column is therefore the smaller current authoring representation and makes `day == "Day6"` directly queryable. No string comparison exists beyond the loader.

`TinyFarmScheduleWindow` is immutable and contains typed `ActorId`, `TinyFarmScheduleDay`, half-open minute bounds, typed `SceneAnchorId`, integer priority, and reason. The catalog canonicalizes by actor ID, every-before-specific, specific day, start, end, priority, anchor ID, then reason; source row grouping is cosmetic.

## Validation law

Validation runs before catalog construction and play in this order:

1. TSON parse, one-table root identity, exact ordered columns, and exact cell kinds.
2. Loader-local conversion of exact 32-bit integers and the bounded day token.
3. Known-NPC and existing-scene-anchor cross-reference checks.
4. `0 <= start < 1440`, `0 < end <= 1440`, `start < end`, non-negative integer priority, and non-empty reason.
5. Exact semantic duplicate rejection.
6. For Elias, Mara, and Sela, all 10,080 minutes across days 1–7 must have an active row.
7. At every actor/day/minute, the highest active priority must identify one anchor; equal-priority disagreement fails.
8. Deterministic canonical ordering and immutable actor index construction.

Unknown actors still fail with `KeyNotFoundException` at decision entry. Missing anchors fail content loading; there is no fallback. Mara's explicit Day6 and Day7 priority-1 rows continue to override priority-0 `Every` rows. Matching remains `[startMinute, endMinuteExclusive)`.

The hostile suite covers unknown actor, unknown anchor, negative start, end above 1440, start not before end, coverage hole, equal-priority conflicting overlap, duplicate row, invalid day, invalid priority column type, non-table root, missing required column, and fractional integer. A three-NPC full-day golden fixture proves the minimal valid shape. Reversing the authored production rows, reloading TSON, and comparing all 30,240 decisions produces an identical canonical catalog and behavior.

## Dominatus and runtime parity

The generated decision graph, decision slot, five immutable semantic-anchor options, score law, hysteresis, commitment, time observation, and transition behavior are unchanged. The options remain manually declared because they map to five generated states; deriving them dynamically would require rebuilding or generalizing the generated graph and would not remove meaningful duplication. The catalog itself is passed through each decision agent's blackboard and the static `FlowDefinition` is reused.

Exact parity evidence:

| Evidence | SHA-256 |
| --- | --- |
| schedule semantic content | `649ef384a746e358a7463548f33574c43f2d33dd19d0cb2ed03a04bd3b946b55` |
| M8 decision | `10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6` |
| M8 anchor sequence | `d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711` |
| state | `d46e70e37c8775e503c3a7693fc14d952a6932a22be0c13172771e020ae65544` |
| results | `ecb4181792717a393125e85416b148ca2242934d761b025498a45aa24af21a24` |
| events | `4f8e8383683a38da695284fb6fd561d5fc32c12fd7feedeee1841e7a3b7364d7` |
| handoff | `0b16f533785927bbe1f780e804b0ac9717a3c588095a337ac5bffeaa9177616a` |
| navigation | `07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa` |
| projection | `4c93db713e4da1a8ee47cec7f6a309adc23f19b7acee1d91b80e0c9c3d6b8434` |
| M7 scene content | `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa` |
| M1 | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| M2 | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |

The M9 proof composes M8's active locomotion, inactive coarse progression, en-route replan, handoff, and save/load checks before/after a boundary and while moving. Schedule content is not serialized into saves; the unchanged M2 definition identity retains current compatibility behavior.

## Provenance, inspection, and performance

The checked proof records `tiny-farm-npc-schedules.obj.ts`, raw SHA-256 `e2657a78f6072f06e79443528bcc76fca954593d8b163e8c0e756a68421faaf2`, 1,316 authored bytes, semantic content hash, and separate read, parse, materialize, validate, and index timings. Cold timings are inspection evidence rather than deterministic assertions.

Existing TableScript commands successfully list the 11-row root, query all Mara rows with `actorId == "mara"`, query the Day6 row with `day == "Day6"`, print all rows, and validate one table with zero diagnostics. The table alone answers: Mara is at Riverside at 13:00 on a normal day; at the General Store counter at 10:00 on day 6; Sela is at the General Store from 08:00 until 18:00; Elias leaves Riverside at 18:00.

The canonical run measured 3.088 ms per 1,000 decisions and 6,390 allocated bytes per decision, compared with the M8 reference of about 6.8 ms and 5.8 KiB. Timing improved in this run; allocation rose modestly because the catalog reference is observed by the short-lived decision agent. This is not material and M9 performs no optimization.

## CI, artifacts, and future fit

The Windows headless workflow now runs the production M9 loader and canonical proof, requires exact historical hashes, 30,240 parity decisions, row-order invariance, active/inactive/handoff/save parity, and raw-TSON isolation. The five checked artifacts total well below the repository budget: `proof.json`, `schedules.json`, `parity.json`, `provenance.json`, and `manifest.json`.

The flat record can later add one required regime discriminant and map it to a typed hard/utility value without changing identities, bounds, or indexing. M9 deliberately adds no unused regime column and no fallback behavior. The exact recommended next milestone, only when a concrete utility behavior is specified, is **TINY-FARM-M10 — required schedule windows with a bounded Dominatus utility fallback**, retaining the current rows as hard windows and adding no planner, calendar, DSL, or generic scheduler.
