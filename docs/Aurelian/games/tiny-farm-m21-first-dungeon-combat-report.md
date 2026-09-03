# TINY-FARM-M21 — First Dungeon and One-Hit Combat

## Outcome

**Outcome A — the existing capability/resolver architecture cleanly supports combat.** `Old Burrow` is a normal TSON-authored scene connected bidirectionally to Overworld. The player owns one identity-bearing Sword in slot 4. One separately modeled Slime is a hostile semantic target. `UseSelectedIntent` closes over Sword + living Enemy to `AttackIntent(EnemyId)`, and `TinyFarmResolver` independently validates authority, ownership, identity, scene, facing, range, and lifecycle before reducing HP from 1 to 0 and emitting exactly one `EnemyDefeated` event.

No combat mode, generic combat system, enemy policy, player health, retaliation, cooldown, loot, XP, faction model, ECS, or renderer authority was added.

## M21 implementation report

1. **Existing combat audit:** no existing TinyFarm, Aurelian, or Dominatus combat primitive fit this slice. M20's identity-item hotbar, closed `UseSelectedIntent` lowering, semantic target query, `ChopIntent`, state/definition split, resolver, save/hash, DTO, and projection paths were reused.
2. **Dungeon representation:** `SceneId("dungeon-entrance")`, display name `Old Burrow`, dimensions 16×12, four blocking wall rectangles, one nonblocking exit portal, and no procedural or multi-floor structure.
3. **TSON authority:** six compact M21 tables author the scene, objects, layout, anchors, routes, and one enemy definition. The loader converts raw rows to typed runtime values, validates them, and folds all M21 sources into definition identity.
4. **Route topology:** `Overworld --overworld-dungeon--> Old Burrow --dungeon-overworld--> Overworld`, using ordinary portal objects and spawn anchors. No teleport-only gameplay command exists.
5. **Sword:** `ItemId("sword")`, `ItemState("Sword")`, price 0, owned by the player. It has no damage, class, durability, quality, speed, or equipment hierarchy.
6. **Ownership/hotbar:** slot 4 is `ItemHotbarBinding(Sword)`. Availability is derived from both item owner and the player's identity inventory. M20 and older state versions continue to project slot 4 as empty; binding does not grant ownership.
7. **Enemy model:** `EnemyId`, one-case `EnemyKind.Slime`, `EnemyDefinition`, and `EnemyState` are concrete TinyFarm types. Slime is not a fake villager and owns no schedule, inventory, social, or Dominatus state.
8. **Definition/state split:** definition owns ID, kind, scene, spawn position, and max health. State persists only ID and current health; lifecycle is derived as Alive above zero and Defeated at zero.
9. **Health law:** production Slime max/current health is exactly 1. Sword damage is the resolver-local M21 constant 1. There is no general stat or health-component system.
10. **Targeting:** living enemies enter the existing 1,280-unit reach, 640-unit facing corridor, squared-distance, and stable-ID query. Defeated enemies leave it.
11. **Priority:** `actor -> enemy -> portal -> ground item -> forage -> tree -> plot -> cooking station -> shop`, then squared distance and ordinal stable ID. Friendly actors remain structurally distinct and retain priority.
12. **Friendly safety:** `AttackIntent` accepts `EnemyId`, so Mara/Elias/Sela cannot be expressed as attack targets. A non-player actor submitting Attack is additionally rejected by the resolver.
13. **Closed lowering:** Axe + Enemy returns `WrongWeapon`; Turnip Seed + Enemy returns `WrongTool`; empty slot returns `NoSelectedBinding`; unowned selected Sword returns `SelectedBindingUnavailable`; Sword + living Enemy lowers directly to typed Attack.
14. **Direct authority:** direct/replay Attack requires player authority and owned Sword, then validates known enemy/state, same scene, Alive lifecycle, and the same facing/range target law. UseSelected is not trusted as prior validation.
15. **Atomicity:** after every validation succeeds, one state replacement changes current health 1→0. No half-state is observable.
16. **Defeat/event/result:** lifecycle becomes Defeated; exactly one `EnemyDefeated` event carries actor, enemy ID, scene, object, kind, and damage amount 1. The existing result envelope plus final state is the smallest current result representation.
17. **Stale attack:** a second direct Attack returns `AlreadyDefeated`, emits no event, and preserves the exact hash.
18. **Collision:** M21 deliberately chooses the allowed nonblocking-enemy law. Adding/removing dynamic collision bodies is deferred until movement/retaliation creates real pressure.
19. **UI/renderer:** slot 4 shows Sword; the target hint is `Requires Sword` or `Attack Slime [Use]`. MonoGame draws the living Slime as a labeled green Enemy scene object and removes it on defeat. It adds no mutation.
20. **Persistence/hash:** save version 10 stores Sword ownership through existing item state, selected slot through existing player UI state, player dungeon placement through actor scene state, and enemy current health through the new enemy list. Enemy state enters the semantic hash; scene/enemy sources enter definition identity.
21. **Replay/parity:** Human and Replay runs repeat exactly. Selected Sword + UseSelected and direct Attack have identical accepted result, event list, and final semantic state.
22. **CLI/LLM:** `select-slot 4`, `select sword`, `use-selected`, `attack [enemy]`, `go to dungeon`, and `approach Slime` parse to typed semantic intents/anchors. Keyboard Q remains edge-triggered and is suppressed while paused or inventory-focused by the existing controller.
23. **DTO:** simulation snapshot v6 includes canonical enemy ID, kind, scene, position, current/max health, lifecycle, and current target through the existing target string. Frame projection includes a typed enemy view and no renderer state.
24. **Graphical sizes:** focused projection/layout qualification covers 2560×1440 and 1280×720, including dungeon extent, living/defeated Slime, Sword slot, hint, hotbar, and inventory bounds. Separate launch smokes verify both real MonoGame startup sizes.
25. **Peaceful isolation:** a bounded Farm interval advances the existing clock/NPC locomotion with no enemy policy evaluation and no combat event. Enemy state remains unchanged until a player Attack.
26. **Regression:** the complete TinyFarm test suite retains M1–M20 behavior, including M13 clock, M14 locomotion, M15 allocation gates, M16 UI, M17 pickup/use, M18 forage, M19 cooking, and M20 woodcutting.

## Canonical M21 hashes

The authoritative values are recorded in `artifacts/tiny-farm-m21/proof.json` for state, results, events, enemy, combat, hotbar, attack parity, scene definitions, routes, projection, DTO, replay, and peaceful isolation. This avoids duplicating values in prose when projection changes legitimately require regenerating the compact artifact.

The evidence directory contains exactly five files: `proof.json`, `dungeon.json`, `combat.json`, `parity.json`, and `manifest.json`. Every file is below 256 KiB.

## Validation and CI

Focused M21 tests cover exact scene/Sword/Slime definitions, TSON load and geometry, routes, slot-4 compatibility, target range/priority, friendly structural safety, wrong/empty/unavailable bindings, selected/direct parity, atomic defeat and exact event, stale attack, invalid direct variants, alive/defeated save-load, semantic CLI/LLM controls, DTO v6, renderer independence, both viewport layouts, canonical replay, and all thirteen evidence hashes.

The full qualification commands and results are recorded in the final task report. `.github/workflows/tiny-farm-headless.yml` now runs the M21 proof, scope manifest checks, semantic CLI conflict smoke, and compact artifact gate after M1–M20. Remote GitHub Actions is not claimed unless a run is dispatched from a pushed revision.

## M1–M21 architecture checkpoint

### What is proven

The following abstractions are proven by unrelated verbs:

- `UseSelected -> typed intent -> resolver` handles Product + Plot/Plant, Axe + Tree/Chop, and Sword + Enemy/Attack without registries or renderer branching.
- Static definition plus small persisted state handles forage availability, tree depletion, and enemy health/lifecycle, while cooking demonstrates stateless authored transformations.
- Scene composition handles Farm, Town, Store, Riverside, Hearth House, Overworld, and Dungeon with the same objects/layout/anchors/routes model.
- Semantic targeting handles friendly actors, portals, identity items, producers, transformations, farm plots, and hostile enemies under one deterministic query law.
- Core owns game truth; Runtime owns TSON/persistence/session/navigation; MonoGame projects; Dominatus chooses NPC policy only.

TinyFarm-specific pieces remain farming/economy rules, named products/items, schedules and Energy policy, concrete target priority, scene content, forage/tree/enemy lifecycle semantics, and the closed list of gameplay intents.

### Repetition and extraction decisions

| Pattern | Decision | Evidence and boundary |
|---|---|---|
| Stable IDs, deterministic ordering, typed intents/results/events | **KEEP LOCAL** | Proven and coherent in Core; moving them would erase game ownership rather than remove duplication. |
| `UseSelected -> typed intent -> resolver` | **KEEP LOCAL** | Three concrete mappings prove the shape, but the branches have different availability, target, and failure laws. Closed lowering remains clearer than a capability registry. |
| Definition + persisted dynamic state | **GENERALIZE NOW, LOCALLY** | Keep a consistent TinyFarm coding convention and validation shape; do not introduce a generic entity/component framework. |
| TSON load/validate/canonicalize boundary | **KEEP LOCAL** | The boundary is proven; individual loaders should be condensed internally, but table truth must not leak into Core runtime state. |
| Repeated M18/M19/M20/M21 loader plumbing | **DELETE/CONDENSE** | Shared private row-loading helpers are justified; public gameplay abstractions are not. Perform only as a dedicated cleanup with hash preservation. |
| Forage/Tree/Enemy as one producer/target interface | **DEFER** | Forage produces a product, Tree requires a tool and produces a product, Enemy has health and produces nothing. Their overlap is query/projection, not one semantic contract. |
| Generic Action/Capability/Combat/Health/Faction/ECS systems | **DEFER** | M21 succeeded without them; no concrete requirement pays their semantic cost. |
| Generic scene runtime in Aurelian | **DEFER** | TinyFarm has strong evidence, but no second real game consumer has demonstrated compatible object/route/anchor laws. |
| Dominatus enemy behavior | **DEFER** | Stationary passive Slime has no decision. Future moving/retaliating enemies may create policy pressure, not current authority. |
| Machina.UI adapter | **EXTRACT NOW as an adapter project only** | Seven scenes and three capability hints now put real maintenance pressure on hand-drawn MonoGame UI. Reuse Machina.UI layout/control semantics without moving gameplay truth or requiring it in Core/Runtime. |
| Milestone-specific proof classes/artifacts | **DELETE/CONDENSE** | Preserve compact released evidence, but consolidate overlapping M13–M21 regression scenarios into capability, persistence, scene, and timing suites. |

### Answers to the checkpoint questions

1. Multiple verbs prove typed semantic intents, authoritative resolver mutation, deterministic target selection, definition/state separation, scene composition, save/hash/replay, and projection-only rendering.
2. Farming/economy, schedule Energy, item/product catalog, target priority, and every concrete intent remain TinyFarm-specific.
3. Definition/state validation, TSON table plumbing, projection summaries, and scenario hash boilerplate now repeat at least three times; only the plumbing should be consolidated.
4. ECS, generic combat/action/capability/resource/health/faction systems, combat mode, renderer authority, and Dominatus world truth were correctly avoided.
5. Closed UseSelected lowering still scales cleanly at three genuinely different mappings; its explicit branches make failure semantics reviewable.
6. Scene composition still scales. Old Burrow required only a new authored scene plus two ordinary routes, not engine branching.
7. Active/inactive simulation still makes sense. Hostile scenes do not imply globally active enemy simulation; only authored active-scene behavior should later consume fixed-step work.
8. Dominatus remains correctly limited to NPC decision policy. Slime has no policy because it has no behavior.
9. TSON remains the right static-data boundary. Typed catalogs and validation prevent row order/raw tables from becoming runtime authority.
10. Yes: MonoGame UI duplication now justifies a bounded Machina.UI presentation/input adapter, not a gameplay migration.
11. Move nothing semantic into Aurelian yet. A second game should first prove compatible scene/target/runtime contracts. A small adapter contract may move only when that consumer exists.
12. Move nothing into Dominatus. Future enemy choice policy may consume it while health, legality, damage, and defeat stay game-owned.
13. There is not enough evidence for generic capabilities. Continue closed lowering until several actions share configurable data and failure laws, not merely syntax.
14. Do not unify Forage/Tree/Enemy. Consolidate only shared target enumeration/projection helpers if profiling or maintenance demonstrates benefit.
15. Largest performance risks are allocating full target candidate lists per UI/frame query, repeated projection scans, full-state defensive copies for semantic actions, and future per-tick hostile behavior. M15 locomotion gates remain the guardrail.
16. Largest semantic risks are target-priority growth, proliferating version gates, duplicated loader composition, and letting authored static fields leak into saves.
17. Condense milestone scenario boilerplate and repeated loader stages; retain released compact artifacts and reports as audit history.
18. Keep authoritative state/result/event hashes, route/definition identity, replay/save-load parity, peaceful isolation, and M13–M15 timing/allocation gates.
19. Consolidate redundant per-milestone viewport, renderer-leakage, CLI parser, and repeated historical-hash wrapper tests once a replacement matrix proves the same contracts.
20. The smallest sensible next phase is architectural cleanup plus the Machina.UI adapter seam. If gameplay continues afterward, one moving/retaliating enemy is the first feature that would create genuinely new timing and policy pressure; it should remain a separate reviewed milestone.

## Stop point

M21 is complete. This report is the required checkpoint; no M22 behavior is preimplemented or implied.
