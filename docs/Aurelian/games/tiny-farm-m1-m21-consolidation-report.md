# TinyFarm M1–M21 consolidation report

Status: AURELIAN-CHKPT-M0. Outcome **A**: the emergent architecture is coherent and can be formalized without a broad rewrite. No gameplay, framework, project, or production type was added.

This report bridges historical milestone evidence to the current architecture. Read `docs/Aurelian/aurelian-engine-architecture-v1.md` for the authoritative design and the individual milestone reports only when exact historical evidence is needed.

## Chronological evidence

| Milestone | New pressure | Shape introduced | Still current? | Superseded/refined by |
|---|---|---|---|---|
| M1 | deterministic headless vertical slice | dependency-free Core, typed intents/results/events, authoritative reducer, save/replay/hash/inspection | yes | M2 expanded state/save composition |
| M2 | full deterministic week | typed authored definitions, farming/economy/time, chunked save, multi-agent canonical proof | yes | later definitions move to TSON |
| M3 | playable graphics | immutable frame projection and MonoGame leaf | yes | UI portion refined by M16; renderer remains temporary |
| M4 | multiple places | graph-like scenes, tabular contents, reducer-owned routes | yes | M6 anchors, M7 TSON authority |
| M5 | sub-tile motion | fixed-point `ScenePosition`, semantic target query, derived DotRecast paths | yes | M14/M15 fixed-step and allocation work |
| M6 | semantic NPC destinations | stable anchors and active/inactive handoff | yes | M8–M14 schedule/host integration |
| M7 | authored scenes | TSON load/validate/canonicalize to typed catalogs | yes | later tables extend same loader boundary |
| M8 | NPC schedule choice | Dominatus policy selects semantic anchors | yes | M9 authored windows; M10 Required/Open |
| M9 | schedule authorship | TSON schedule tables, exhaustive parity, no raw table runtime | yes | M10 payload-enum hybrid regimes |
| M10 | authored hard/open periods | Required structural control flow plus bounded Open utility | yes | M11 persistent runtime/allocation |
| M11 | decision allocation/lifetime | persistent per-actor Open runtime, canonical candidate arrays, opt-in traces | yes | current baseline |
| M12 | fatigue/rest | energy state, personal living space, Rest as schedule/policy action | yes | M13 multi-rate host advances it |
| M13 | live simulation | pause/play/fast-forward, bounded host delta, separate world/locomotion/decision rates | yes | M14/M15 locomotion detail |
| M14 | visible active NPC motion | 60 Hz fixed-step active locomotion and bounded wander | yes | M15 hot-path implementation |
| M15 | allocation pressure | direct authoritative locomotion core, cached indexes/path follower, allocation gates | yes | current performance baseline |
| M16 | player UI | semantic inventory/hotbar model, focus/open session state, temporary MonoGame UI | partly | Machina adapter is next infrastructure work |
| M17 | pickup/use | ground item placement, deterministic targeting, selected binding to `PlantIntent` | yes | M20/M21 extend closed lowering |
| M18 | forage | definition/state split and producer-to-stack `GatherIntent` | yes | shared target/projection mechanics only |
| M19 | cooking | stateless authored recipe/station transformation and `CookIntent` | yes | no generic crafting graph |
| M20 | tool use | identity Axe, Tree definition/state, selected binding to `ChopIntent` | yes | M21 proves pattern across hostile domain |
| M21 | first hostile target | Old Burrow scene, Sword/Slime, typed `AttackIntent`, atomic defeat | yes | checkpoint defers generic combat |

Milestone names remain in proof/scenario/test artifacts because those artifacts certify released behavior. They are historical entry points, not the production architecture vocabulary.

## Current engine-like systems inventory

| System | Classification | Location/decision |
|---|---|---|
| scene definitions/composition | reusable engine shape; potential kit | typed/TSON-backed in TinyFarm; defer extraction |
| scene routing | reusable engine shape | resolver-owned, TinyFarm-local |
| anchors/semantic targeting | reusable engine shape | stable IDs; target priority remains game-specific |
| simulation host/multi-rate timing | reusable engine shape; should eventually extract | TinyFarm-local pending second consumer |
| fixed-step locomotion | reusable engine shape | qualified locally; core mechanics should not be rebuilt |
| active/inactive realization | reusable engine shape | proven locally; defer API extraction |
| navigation/path realization | reusable engine shape | DotRecast-derived Runtime leaf; defer interface extraction |
| controller model | reusable law | peer intent producers; no generic hierarchy needed |
| typed intents/resolver | game-specific semantics over reusable law | keep Core-local |
| event/result law | reusable shape | keep local until shared envelope pressure |
| persistence/versioning | already shared mechanics plus local codec | reuse Dominatus container ideas; keep schema local |
| replay/semantic hashing | reusable engine shape | proven; defer code move |
| TSON content | already shared compiler, local domain boundary | Copeland plus TinyFarm loader/catalog |
| inventory | game-specific current state; potential kit | keep local |
| hotbar/capability selection | game-specific current state; potential kit | keep closed/local |
| UI projection | reusable pattern | application model local; Machina UI already shared |
| input/focus routing | partly shared | Machina records/routing; game-window bridge missing |
| inspection/DTO | reusable pattern | keep current TSON/JSON/frame projections; no new protocol |
| CLI/LLM control | reusable semantic-control law | parser remains TinyFarm adapter |
| graphical projection | already shared architectural law | MonoGame leaf today; Aurelian backends available at lower level |
| farming/economy/cooking/combat/resource semantics | game-specific | remain TinyFarm |
| Dominatus utility/flow | already shared | Dominatus |
| Machina controls/layout/presentation IR | already shared | Machina.UI |

## Empirical engine result

Before domain additions became cheap, TinyFarm repeatedly needed deterministic resolution, stable identity/order, authored definitions, scene routing, semantic targeting, time domains, active/inactive realization, navigation lowering, controller isolation, persistence/replay/hash, projections, and input/UI/backend boundaries.

The implementation details a future author should not need 90% of the time are accumulators, catch-up policy, input edge detection, save container/migration mechanics, event/replay storage, path realization, renderer synchronization, window adapters, UI primitive drawing, and deterministic index/cache construction.

Final definition:

> A game engine in an LLM-native development model is qualified reusable systems-level machinery, exposed through explicit semantic composition/runtime boundaries, that prevents humans and coding agents from repeatedly spending context and reasoning on infrastructure unrelated to the application's distinctive rules.

The JTF thesis is directionally validated. JTF has the right ingredients and ownership boundaries, but application-facing kit composition remains partly aspirational because scene, host, targeting, and persistence implementations are still TinyFarm-local.

## Architecture decisions

| Shape | Decision | Destination | Evidence |
|---|---|---|---|
| Scene graph/table model | DEFER extraction | TinyFarm now; Aurelian candidate | seven scenes share one validated law; no second app |
| Simulation host | DEFER extraction | TinyFarm now; Aurelian candidate | M13–M15 qualify cadence, but commands/world time are local |
| Time domains | ALREADY CORRECT | TinyFarm host/Aurelian design law | render, locomotion, world, decision clocks are separate |
| Interaction targeting | KEEP LOCAL | TinyFarm.Core | priority and failure semantics are domain law |
| Typed intents | ALREADY CORRECT | application Core | precise payloads, validation, replay, events |
| Closed capability lowering | ALREADY CORRECT | TinyFarm.Core | Plant/Chop/Attack differ materially |
| Inventory | KEEP LOCAL | TinyFarm.Core | one consumer; domain-coupled ownership/stacks |
| Hotbar | KEEP LOCAL | TinyFarm.Core | semantic selection is game truth; mapping is closed |
| Navigation | DEFER | TinyFarm.Runtime/Aurelian candidate | derived DotRecast boundary is sound; no second consumer |
| Persistence | DEFER | local codec plus shared container concepts | versions/chunks are application-specific |
| Replay | DEFER | TinyFarm.Runtime/Aurelian candidate | semantic envelope law proven once |
| Machina UI | EXTRACT NOW at adapter seam | Machina plus integration adapter | existing shared UI; TinyFarm duplicates backend work |
| Combat | KEEP LOCAL | TinyFarm.Core | one one-hit enemy; no generic law |
| Resource nodes | KEEP LOCAL | TinyFarm.Core | forage/tree/enemy overlap only mechanically |
| Dominatus policy | ALREADY CORRECT | Dominatus | policy/flow only; no mutation leakage |

“Extract now” for Machina means qualify the existing Machina API through a thin renderer adapter. It does not authorize moving TinyFarm gameplay or creating a package per kit in this checkpoint.

## Scene, realization, time, and controllers

The surviving scene law is definitions as validated tables, composition as a route graph, reducer-owned transitions, semantic state as truth, and rendering as projection. Layout owns authored object rectangles/layers; anchors own semantic destinations; dynamic placement and lifecycle belong to state.

Active scenes use detailed fixed-step spatial realization. Inactive scenes use coarse semantic destinations/progression. NPC schedule, Rest, wander, and path-following obey this split. Enemy state persists without implying inactive combat simulation.

Host, render, locomotion, world, and decision time remain separate. The host clamps excessive real-time delta and does not retain catch-up debt. Render observations never tick gameplay.

Human, Dominatus, LLM, and Replay remain peer intent sources rather than peer implementations. Human control handles device edges; LLM accepts semantic commands; Dominatus owns utility/flow; Replay reproduces envelopes. All must pass the same resolver validation.

## Entity/resource and capability decisions

`ItemState`, forage definitions/state, tree definitions/state, enemy definitions/state, farm plots, and cooking stations share stable semantic identity, scene placement or association, targetability, definition/state separation, persistence, and projection. They do not share enough domain law to justify `InteractiveWorldEntity<T>`, `IResourceNode`, `IHarvestable`, or `Combatant`.

Closed capability lowering is repeated enough to name but not complex enough to generalize. Explicit typed branches preserve meaningful failures and delegate final validation to existing typed intents. Decision: **KEEP CLOSED LOWERING / DEFER generalization**.

## Ownership results

- TinyFarm/Core: authoritative game state, domain identities, concrete intents/results/events, validation/reduction, target priority, semantic UI state, hash.
- TinyFarm/Runtime: session, persistence codec, content loading, schedule integration, active/inactive realization, derived navigation, simulation host, projections/scenarios.
- Dominatus: policy, utility, agent lifecycle/flow, policy-local memory. No leakage into world mutation, damage, inventory, rendering, or saves.
- Copeland/TSON: authored semantic programs/tables and portable values. It does not own mutable runtime truth. No additional hard-coded field currently has enough authorship pressure to migrate.
- Machina.UI: semantic controls, UI layout, hit testing/interaction mechanics, normalized UI input, presentation IR.
- backend host: window lifecycle, input normalization, coordinate/DPI translation, world/UI render composition, device resources.

## Persistence, replay, and inspection

The game-specific state codec and migrations remain local. Shared candidates are chunk/version envelopes, semantic replay records, deterministic hashing helpers, and definition identity, but extraction awaits a second application.

Inspection already has coherent projections for CLI/LLM commands, TSON snapshot, JSON DTO, semantic hashes, and renderer frames. Oblivion could later consume these artifacts, but another inspection protocol would duplicate current machinery without demonstrated need.

## Machina and backend result

Machina is already renderer-neutral above its adapter seam. TinyFarm's HUD, hotbar, inventory, hints, and target labels originate from semantic projections; its MonoGame rectangles/text/hit areas are the temporary realization. The migration map and minimal adapter contract are in `docs/Machina.UI/machina-renderer-neutral-presentation-architecture.md`.

Same-window composition is world pass plus Machina UI pass plus one focus/input router. MonoGame is the first adapter target because the real TinyFarm host exists. Stride is feasible but unqualified. Aurelian native has strong low-level render infrastructure but lacks the complete TinyFarm textured/text path. Avalonia is best treated first as a desktop control/accessibility alternative or focused offscreen/embedding proof, not as application state or a universal zero-copy game compositor.

## Performance review

| Area | Evidence | Classification |
|---|---|---|
| locomotion | M15: 6,720 -> about 858 B/reduction (87.24% reduction); core replacement about 48 B | CURRENTLY MATERIAL and gated |
| policy evaluation | M11 Required 0 B; Open 432 B; candidate lookup 0 B | not currently material; retain gates |
| path queries | M15 276/100K movement reductions; paths reused | WATCH as active population grows |
| simulation host | M13 equivalent hashes across 60/144 Hz and even/irregular partitions | NOT MATERIAL at current scale |
| projection/UI | no isolated current benchmark; full scans/list creation are visible in source | WATCH; measure before redesign |

Do not infer full-simulation capacity from isolated utility throughput. Do not redesign Dominatus or replace observable result/event objects solely to chase zero allocations.

## Semantic-complexity risks

Ranked by current maintenance pressure:

1. target-priority and contextual hint growth across actors, enemies, portals, items, forage, trees, plots, stations, and shop;
2. save-version and DTO-version branches as state shapes grow;
3. repeated loader composition/provenance/hash ceremony for new TSON tables;
4. temporary MonoGame UI geometry/drawing/hit testing;
5. milestone-specific scenario/runner entry points obscuring current capability groups;
6. concrete-intent count—currently explicit and manageable, but watch routing/inspection duplication;
7. content-table count—currently small and validated, not material.

## Milestone archaeology and safe consolidation

Search found M-specific production scenario/proof names, runner switches, historical content snapshots, compatibility hashes, and M-specific tests. Their classification is:

| Material | Decision | Reason |
|---|---|---|
| `TinyFarmM*Scenario` and proof records | KEEP AS HISTORICAL EVIDENCE for now | runner/CI/artifacts call them directly; rename would churn public proof paths |
| focused M13–M15 gates | KEEP AS CURRENT TEST | unique timing/allocation laws |
| M16–M21 focused semantics | KEEP AS CURRENT TEST | distinct failure, persistence, projection, and parity laws |
| historical TSON `Content/M*` snapshots | KEEP AS HISTORICAL/COMPATIBILITY INPUT | scenarios compare migration/parity; not runtime authority |
| legacy hard-coded scene/schedule catalogs | already removed/superseded | TSON typed catalogs are production authority |
| duplicate proof JSON serialization/runner branches | CONSOLIDATE later | real repetition, but artifact/hash compatibility makes it unsafe inside a docs checkpoint |
| repeated loader row plumbing | CONSOLIDATE in a dedicated hash-preserving change | source pressure is real; no semantic abstraction authorized |

No production code was deleted or renamed in this checkpoint. That is a deliberate safe-consolidation result: current M-named types are executable historical evidence, and changing them without a replacement proof matrix would reduce diagnosability. The smaller current architecture is achieved through authoritative documentation and clear history/current separation rather than a risky mass rename.

## Test consolidation and canonical scenario

The source suite contains 232 `[Fact]`/`[Theory]` declarations; parameterized cases make the executed count higher. The suites fall into:

- core state/resolver/save/replay/hash invariants;
- scene/content/route/anchor and schedule parity;
- timing, fixed locomotion, and allocation gates;
- UI/input/focus/projection contracts;
- pickup/forage/cook/chop/combat domain laws;
- compact proof/artifact contract tests.

No tests were deleted. Apparent repetition often protects a different historical hash, invalid-input matrix, save version, or controller parity. Safe future consolidation requires a replacement capability matrix before removing scenario ceremony; reducing count alone is not a goal.

The modern integration scenario is formally the M21 checkpoint composition: it composes current definitions, semantic hotbar selection, Chop, Gather, Cook, authored dungeon enter/leave routes, Sword/Attack, and final state/event hashing. The enclosing M21 proof separately qualifies save/load, Human/Replay repeat parity, projection/DTO hashes, and peaceful live-host isolation. Earlier canonical M2 and M17 focused proofs retain the plant/pickup loops. This is intentionally a composition of existing qualified paths rather than one brittle wall-clock mega-test.

## Artifact cleanup

`artifacts/tiny-farm-m1` through `tiny-farm-m21` are compact released evidence. M2 is about 36 KiB; all other populated milestone directories are below 55 KiB and generally contain five files. No historical hashes/reports were removed. The untracked/empty local `tiny-farm-m19-ci-local-20260902` directory contains no evidence and is not part of repository history.

Checkpoint artifacts are limited to five JSON files under `artifacts/aurelian-checkpoint-m0/`; there are no screenshots or source dumps.

## Documentation hierarchy

```text
CURRENT ARCHITECTURE
  docs/Aurelian/aurelian-engine-architecture-v1.md
  docs/Machina.UI/machina-renderer-neutral-presentation-architecture.md

APPLICATION AUTHORING / CURRENT GAME BRIDGE
  this consolidation report

ENGINE INTERNALS
  docs/Aurelian/architecture/
  docs/Machina.UI/architecture/ and reference/

HISTORICAL EVIDENCE
  docs/Aurelian/games/tiny-farm-m*-report.md
  docs/Aurelian/history/
  docs/Machina.UI/history/
  artifacts/tiny-farm-m*/
```

The former Aurelian game ground report now points readers to this hierarchy and retains only a compact historical baseline.

## Dependency topology

Current concrete topology:

```text
TinyFarm.MonoGame -> TinyFarm.Runtime -> TinyFarm.Core
        |                    |-> Aurelian.Runtime -> Aurelian.World
        |                    |-> Copeland.TS
        |                    `-> Dominatus / Ariadne / DotRecast packages
        `-> MonoGame package

Machina.Pipeline -> Machina.Core/Layout/Runtime/Presentation
Machina.Presentation -> Core/Layout/Standard/Runtime

Aurelian.Core -> Aurelian.Runtime -> World + Rendering.Contracts + Dominatus
Aurelian backends/assets/shaders -> Rendering.Contracts (and their leaf packages)
```

Proposed dependency direction:

```text
application Core (semantic truth)
        |
application Runtime -> Aurelian runtime contracts
        |            -> Dominatus policy
        |            -> Copeland/TSON loader boundary
        |            -> derived navigation implementation
        |
composition host -> world backend
                 -> Machina adapter -> Machina.*
```

The adapter depends on both host/backend and Machina. Aurelian core/runtime must not depend back on that adapter, and Machina must not depend on Aurelian or TinyFarm.

## Naming result

- `Aurelian` is broad enough for the formal role and should not be renamed.
- `Machina.UI` remains accurate.
- `TinyFarm.Core` and `TinyFarm.Runtime` accurately communicate truth versus integration.
- `Frame` is appropriate for immutable presentation output; qualify it with `TinyFarm` or `MachinaPresentation` as current code does.
- `Host` should mean lifecycle/time composition, not renderer or state owner; current `TinyFarmSimulationHost` fits.
- `Projection` should remain a pure derived view; current usage fits.
- `Presenter` is legacy/sample vocabulary inside Machina screen composition. Do not churn it during this checkpoint, but prefer `presentation`/`host` in new engine-facing APIs.
- M-numbered scenario/proof names are historically accurate and should be isolated rather than cosmetically renamed.

## Author mental models

Application author:

1. author semantic content/state;
2. define scenes/routes and concrete typed actions;
3. compose qualified kits;
4. add small domain reducers and controller policy;
5. project UI/inspection;
6. choose a host/backend.

Systems author:

1. preserve clock-domain and reducer ordering;
2. maintain atomic mutation and deterministic replay/hash;
3. build and invalidate derived indexes correctly;
4. keep navigation/render/UI adapters replaceable;
5. maintain save/version and content-identity boundaries;
6. measure hot paths before representation changes.

## Do not make the LLM rebuild this

| System | Status |
|---|---|
| typed utility/flow policy | ALREADY BORING via Dominatus |
| semantic UI layout/presentation operations | ALREADY BORING via Machina |
| renderer-neutral world snapshots/command plans | ALREADY BORING at Aurelian systems level |
| fixed-point/fixed-step locomotion | ALREADY BORING inside TinyFarm; not yet reusable API |
| host clocks/catch-up | PARTLY BORING; local implementation |
| input normalization/edge/focus routing | PARTLY BORING; Machina records exist, game bridge missing |
| scene routing/anchors | PARTLY BORING; local mature model |
| target selection mechanics | PARTLY BORING; shared query law, local priority |
| save versioning/replay/hash envelopes | PARTLY BORING; proven locally/shared concepts |
| navigation realization | PARTLY BORING; derived leaf but local lifecycle |
| backend world/UI composition | STILL APPLICATION-EXPOSED |
| loader/definition composition | STILL APPLICATION-EXPOSED |

## Final result and next milestone

Outcome A. Current architecture is simpler than its milestone history and supports formalization. Safe code consolidation was intentionally zero because the candidate removals still serve executable compatibility evidence. The exact next infrastructure milestone is **AURELIAN-MACHINA-ADAPTER-M0**, scoped to replacing TinyFarm's temporary MonoGame UI realization with the existing Machina semantic/layout/interaction/presentation pipeline while preserving all gameplay and state ownership.

## Checkpoint validation

Validation on 2026-09-03 used .NET SDK 10.0.400:

| Command | Result |
|---|---|
| `dotnet test TinyFarm.slnx -m:1 --nologo` | 252 passed |
| `dotnet test Aurelian.slnx -m:1 --nologo` | 606 passed across 11 assemblies |
| `dotnet test Machina.UI.slnx -m:1 --nologo` | 673 passed across 8 assemblies |
| `dotnet test Machina.UI.Slow.slnx -m:1 --nologo` | 308 passed across 2 assemblies |
| `dotnet test JointTaskForce.slnx -m:1 --nologo` | 3,178 passed across 27 assemblies |
| `dotnet build src/TinyFarm/TinyFarm.MonoGame/TinyFarm.MonoGame.csproj --nologo` | succeeded, 0 warnings, 0 errors |
| TinyFarm runner `--m13` through `--m21` to isolated temporary directories | every proof Outcome A; five files each |
| canonical M2 proof/save/replay/inspection run | Outcome A; repeated/result/event and all six reload points exact |
| checkpoint artifact budget | 5 files, 9,352 bytes total, all below 256 KiB; passed |
| JSON parse and `git diff --check` | passed |

The TinyFarm suite contains the named M13 timing, M14 locomotion, M15 allocation, M16 UI, M17 pickup/use, M18 forage, M19 cooking, M20 chop, M21 dungeon/combat, and canonical persistence/replay gates; the full 252-test pass is the combined required gate. No remote GitHub run is claimed because this checkpoint was not pushed.

## AURELIAN-COMPOSITOR-M0 migration note

The M16 temporary MonoGame HUD/hotbar/inventory drawing and pointer rectangle hit testing are retired. The MonoGame window now hosts an `AurelianLayerCompositor`: the existing world renderer is a direct bottom pass and `TinyFarmMachinaUiLayer` is a transparent top pass. `TinyFarmPresentationSnapshot` transports application-owned view data into Machina, while `TinyFarmUiCommandDto` returns selection, inventory, simulation, wait, use, and interact requests to the existing controller/intent path. The hand-rolled toolbar remains the appearance and interaction compatibility oracle during this conservative Machina migration. M13-M21 semantic state and simulation ownership are unchanged. See `docs/Aurelian/aurelian-renderer-neutral-layer-compositor.md` for the audit and contract.
