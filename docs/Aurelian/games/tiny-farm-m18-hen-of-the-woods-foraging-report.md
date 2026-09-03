# TINY-FARM-M18 — Hen-of-the-Woods Foraging

## Outcome

**Outcome A — one authored wild producer cleanly produces a stackable product.** Riverside contains one TSON-authored Hen-of-the-Woods node. Contextual Interact selects it through the existing facing corridor and lowers to `GatherIntent`; `TinyFarmResolver` atomically increments `InventoryStacks` by one and changes the node from Available to Depleted. Save/load, semantic hashing, replay, CLI/LLM control, DTO/TSON inspection, frame projection, and player UI all preserve or project that truth.

## Audit and semantic boundary

The pre-M18 model had no resource node, collectible producer, or depleted world-object state. Crop harvesting already produced `ProductId` stacks but was plot-specific. M17 ground `ItemState` represented a durable `ItemId` moving between ground and owner. A mushroom cluster is neither, so M18 adds only concrete `ForageNodeDefinition`, `ForageNodeState`, and `GatherIntent` types. It adds no generic harvestable interface, loot table, tool, skill, crafting, RNG, spawning, season, weather, quality, or respawn system.

Static definition truth is authored in `Content/M18/tiny-farm-forage-nodes.obj.ts`: stable node ID, Riverside scene, tile position, product ID, and fixed yield. `TinyFarmDefinitionLoader.LoadM18` composes that row with the established M14 scene/schedule catalog and adds a non-blocking semantic Forage scene object. The M18 product table adds `hen-of-the-woods`, display name `Hen-of-the-Woods`, buy price 0, and sell price 3. Generic selling therefore accepts it incidentally; no shop stock or mushroom economy content was added.

Dynamic truth is one version-8 `ForageNodeState` in `TinyFarmState`. Only availability is persisted. Static scene, position, product, and yield are not duplicated into the save. The node state enters the semantic hash, and load requires state IDs to match authored definitions exactly.

## Interaction and resolver law

The exact target order is:

```text
actor -> portal -> ground item -> forage node -> plot -> shop
-> squared distance -> ordinal stable ID
```

Forage uses the existing 1,280-unit reach and 640-unit facing-corridor half-width. Depleted nodes are excluded from targeting. The player sends only Interact; the resolver lowers a selected forage target to `GatherIntent`. Explicit Gather is also available for semantic CLI/tests.

Before mutation, the resolver validates actor identity, player ownership of the verb, node/definition identity, scene, availability, product/yield authoring, and range/facing. Only then does it compute the new product count and apply both mutations. Accepted gather emits exactly one `ForageGathered` event with actor, node, scene object, scene, product, and count. A competing/repeated request returns `AlreadyDepleted` with no state change.

## Projection, controls, and proof

Available forage appears as a small mushroom-colored labeled scene object. After gather, frame projection omits it. UI inventory immediately contains `Hen-of-the-Woods x1`; the selected hotbar slot remains unchanged and no hotbar binding or UseSelected behavior is added. The interaction hint is `Gather Hen-of-the-Woods [Interact]` while valid and disappears after depletion. Inventory focus and Pause retain their existing interaction-suppression law.

Simulation snapshot/TSON version 4 exposes node ID, scene, product, availability, and fixed position. Runner `--m18-control` supports `inspect`, `interact`, and `gather`; the shared command parser accepts `gather riverside-hen-of-the-woods`. Human and Replay envelopes execute the same resolver sequence and produce equal state, result, and event hashes.

Live MonoGame windows at 2560x1440 and 1280x720 showed Riverside, the labeled cluster, player facing, `Gather Hen-of-the-Woods [Interact]`, hotbar, inventory control, and simulation HUD readable and unclipped. The production semantic control then showed the exact post-Interact frame: forage object and hint absent, `Hen-of-the-Woods x1` present, slot 1 unchanged, and NPC decisions continuing. Automated Windows key taps were too brief for MonoGame's polled keyboard state, so they are not claimed as action-transition evidence; the transition is instead proved through the same `InteractIntent`/resolver/frame/UI path used by the graphical client.

The compact artifact set is exactly:

- `artifacts/tiny-farm-m18/proof.json`
- `artifacts/tiny-farm-m18/forage.json`
- `artifacts/tiny-farm-m18/inventory.json`
- `artifacts/tiny-farm-m18/replay.json`
- `artifacts/tiny-farm-m18/manifest.json`

The canonical hashes are:

- state/replay `354823a55369dbe241d4516d6f2f60d9b7e49f152bc28bf0ba057a4b7799fbd8`
- results `1785d456077fba6ba045a985b1335e3772dbbc15798845a1e848d5abd8851c44`
- events `f4d58d408cdcf8ce6d5f8a92cc0bc42946221268702322fde80d860879ec8341`
- forage `b8924abfe45c295e6c5826178a92aec94a2f9e045e1d5d24757a31331301b3c2`
- inventory `1be2c88afd8f683b6e2498a467524872fd3e2df0ef7c93b668502808889ab30c`
- definitions `d88f765810529ac298498889ec252a313722c6b9045a5291b2d0c9ffde2373e9`
- projection `3ce677c61c4c39faadb2994e24c017b5723b07126dd514797bea45bd119a6953`
- DTO `1f5729c50a1d1cafb7769df24724581365def0ac6384088f4eb211f13d8b0fd5`

The focused M18 tests cover product/definition loading, target identity, priority, successful atomic gather, fixed yield, depletion, duplicate rejection, unknown/wrong-scene/out-of-range/unknown-actor rejection, UI/frame reprojection, save/load, semantic hash, snapshot TSON, CLI parsing, renderer-independent types, repeat determinism, and replay parity. Existing TinyFarm tests retain M13 timing, M14 locomotion, M15 allocation, M16 UI/input, and M17 pickup/use-selected regression coverage.

## Recommended M19

**TINY-FARM-M19 — one concrete cooking recipe consuming Hen-of-the-Woods.** The observed gameplay pressure is now a stackable wild product with no player verb after acquisition. The next bounded milestone should add one recipe and one existing-location cooking interaction that consumes `HenOfTheWoods ProductId` through the resolver. It should not add a generic crafting graph, recipe editor, quality, stamina, skills, seasons, tools, or additional forage species.
