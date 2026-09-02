# TinyFarm M8 — Dominatus schedule selection

## Outcome

**Outcome A.** The production actor/time branch in `TinyFarmNpcController.ScheduledAnchor` is gone. TinyFarm Runtime now supplies immutable schedule-window data to a generated Dominatus flow; Dominatus observes actor identity and absolute world minute, selects a semantic `SceneAnchorId`, and the unchanged Runtime path spatially realizes it. The player-visible schedule, anchor sequence, transition timing, active/inactive handoff, save/load behavior, and historical hashes remain exact.

## Legacy audit and semantic contract

The old selector had three actor branches. Mara also had two absolute-day overrides; Elias had one daily work/riverside interval; the final fallback implicitly represented Sela. Every branch is `PURE_TIME_SCHEDULE`. There were no state-dependent, utility-dependent, or transition-special-case choices beyond the time-window priority of Mara's day overrides.

| Actor | Day | From minute | To minute | Semantic location | Anchor | Priority |
| --- | ---: | ---: | ---: | --- | --- | ---: |
| Mara | every | 0 | 720 | Town Square | `town.square` | 0 |
| Mara | every | 720 | 1020 | Riverside | `riverside.meeting-point` | 0 |
| Mara | every | 1020 | 1440 | Farmhouse | `farm.home` | 0 |
| Mara | 6 | 540 | 1020 | General Store | `general-store.counter` | 1 |
| Mara | 7 | 600 | 1020 | Riverside | `riverside.meeting-point` | 1 |
| Elias | every | 0 | 720 | Farmhouse | `farm.work-area` | 0 |
| Elias | every | 720 | 1080 | Riverside | `riverside.meeting-point` | 0 |
| Elias | every | 1080 | 1440 | Farmhouse | `farm.work-area` | 0 |
| Sela | every | 0 | 480 | Farmhouse | `farm.home` | 0 |
| Sela | every | 480 | 1080 | General Store | `general-store.counter` | 0 |
| Sela | every | 1080 | 1440 | Farmhouse | `farm.home` | 0 |

Intervals are half-open. A day-specific row overrides a daily row only when its higher priority is active. The checked-in machine contract is `artifacts/tiny-farm-m8/schedule-parity.json`; test-only legacy code compares all 30,240 actor/minute combinations across days 1–7 and explicitly checks the minute before, at, and after every transition.

## Dominatus decision structure

The selected primitive is the existing `Ai.Decide` utility decision inside an OptFlow-generated HFSM. Its stable slot is `TinyFarm.NpcSchedule.Anchor`. Five immutable options correspond to the five existing semantic anchors. For the observed actor and minute, an option scores `1` only when it owns the highest-priority active window; all other options score `0`. Schedule validation rejects a conflicting highest-priority tie, so option ordering never decides a schedule boundary.

Hysteresis and minimum commitment are both zero. That preserves exact boundary changes and en-route retargeting. The schedule graph and option array are static and reused; each selection uses the existing observation-pure ephemeral-agent pattern. A warmed Debug comparison measured the removed test-only branch oracle at about 0.016 ms per 1,000 selections and Dominatus at about 6.8 ms per 1,000; the canonical scenario's separate sample was about 3.5 ms per 1,000 and 5.8 KiB per selection. The allocation is agent/world execution state, not rebuilt graph/options. The absolute cost is roughly 7 microseconds per NPC observation and does not justify mutable agent persistence or caching in M8.

The observation contains typed self identity and absolute world minute. Current semantic location and current anchor arrival remain inputs to the existing movement-intent decision, not schedule scoring. No coordinates, renderer types, navigation types, randomness, LLM input, or Ariadne state enter schedule selection. An unsupported actor produces a deterministic typed `KeyNotFoundException`; there is no silent default schedule.

## Runtime, transition, and persistence law

The output is `TinyFarmScheduleDecision`, whose gameplay payload is a `SceneAnchorId` plus compact inspection provenance: actor, minute, decision slot, window, priority, and reason. The compatibility `ScheduledAnchor` API delegates to this decision and contains no schedule authority.

Active NPC flow remains:

```text
Dominatus schedule decision
→ SceneAnchorId
→ authored SceneAnchorDefinition
→ derived DotRecast path
→ SpatialMoveIntent
→ TinyFarmResolver
```

Inactive NPC flow uses the same selected anchor, then the unchanged resolver advances one coarse semantic graph hop and issues zero path queries. When time crosses a boundary while an active NPC is en route, the next observation produces the new anchor; the changed goal identity invalidates the derived path and replans. `AnchorReached` behavior is unchanged.

The selected goal remains recomputed observation-pure state. It is not persisted. Save/load immediately before minute 720, immediately after minute 720, and while moving all reproduce the same semantic goal and authoritative hash. Paths remain derived and are rebuilt after load.

## Existing Dominatus idioms

TinyTown uses stable per-agent `DecisionSlot` identities and direct `Ai.Decide` options for deterministic work pressure. FishTank documents the same explicit-score and no-accidental-tie discipline. RTS constructs decision definitions once and uses deterministic scorers over blackboard observations. M8 reuses those idioms without copying game-specific actions or changing Dominatus Core.

No Dominatus Core change was required. The schedule remains in TinyFarm Runtime because that is the existing Dominatus dependency boundary; TinyFarm Core exposes no `DecisionSlot`, `UtilityOption`, `Consideration`, or HFSM type.

## Proof and validation

M8 preserves the M6/M7 canonical evidence:

| Evidence | SHA-256 |
| --- | --- |
| state | `d46e70e37c8775e503c3a7693fc14d952a6932a22be0c13172771e020ae65544` |
| results | `ecb4181792717a393125e85416b148ca2242934d761b025498a45aa24af21a24` |
| events | `4f8e8383683a38da695284fb6fd561d5fc32c12fd7feedeee1841e7a3b7364d7` |
| handoff | `0b16f533785927bbe1f780e804b0ac9717a3c588095a337ac5bffeaa9177616a` |
| navigation | `07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa` |
| projection | `4c93db713e4da1a8ee47cec7f6a309adc23f19b7acee1d91b80e0c9c3d6b8434` |
| M8 transition decisions | `10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6` |
| M8 anchor sequence | `d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711` |
| M7 scene content | `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa` |
| M1 | `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333` |
| M2 | `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3` |

The headless tests cover the complete legacy table, all 1,440 minutes per day for seven days and every NPC, all transition edges and repeated tie behavior, active replanning, inactive zero-navigation progression, active/inactive handoff through the retained M6 scenario, save/load on both sides of a transition, save/load while en route, missing-anchor behavior, unknown actors, static definition reuse, renderer isolation, and Core dependency isolation. The GitHub workflow now runs the canonical M8 proof and verifies its historical hashes and parity flags.

The five M8 artifacts are compact and pass the repository artifact-budget check: `proof.json`, `schedule-parity.json`, `decisions.json`, `handoff.json`, and `manifest.json`.

## Schedule authoring fit and exact M9 recommendation

The resulting law is naturally a flat table of `ActorId`, optional `Day`, `FromMinute`, `ToMinute`, `SceneAnchorId`, `Priority`, and a stable reason. Moving that table to TSON is now mechanically plausible, but M8 intentionally keeps it in C# so decision-authority migration and content-authority migration remain separate.

The exact recommended next milestone is **TINY-FARM-M9 — TSON-authored NPC schedule windows**: move only these eleven validated rows through the existing `TinyFarmDefinitionLoader`, preserve the M8 decision graph and every hash, add hostile overlap/coverage/reference validation, and make no gameplay, calendar, scheduler, DSL, navigation, or Dominatus Core change.
