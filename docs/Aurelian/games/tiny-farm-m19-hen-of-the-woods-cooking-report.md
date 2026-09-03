# TINY-FARM-M19 — Hen-of-the-Woods Cooking

## Outcome

**Outcome A — one declarative recipe cooks cleanly through resolver authority.** Hearth House contains one TSON-authored Kitchen Stove. The one TSON-authored recipe consumes `hen-of-the-woods x1` and produces `sauteed-hen-of-the-woods x1`. Contextual Interact selects the station through the shared spatial law and lowers to `CookIntent`; `TinyFarmResolver` validates the complete request before atomically changing `InventoryStacks`. UI, frame, DTO, persistence, and replay observe the resulting game truth.

## Mandatory audit and bounded model

The pre-M19 tree contained no `Recipe`, `Cook`, `Craft`, `Station`, `Transform`, or `Combine` semantic model. The reusable infrastructure was `ProductId` plus `InventoryStacks`, resolver-owned Buy/Sell/Plant/Harvest/Gather mutations, typed intent/result/event conventions, `InteractionTarget`, `SceneObjectKind`, TSON table loading, Hearth House scene content, chunked saves, semantic hashes, projections, the CLI/LLM semantic command path, and deterministic scenario artifacts. M19 therefore adds only `CookingRecipeDefinition`, `CookingRecipeInput`, the one-case `CookingStationKind.Cooking`, `SceneObjectKind.CookingStation`, and `CookIntent`. It adds no generic crafting graph, recipe tree/editor/unlock, skill, quality, food effect, duration, energy cost, quantity, batch, byproduct, or station hierarchy.

Static content is four TSON tables under `Content/M19`: the product catalog, one station row, one recipe row, and one repeated input row. The input table avoids fixed Ingredient1/2/3 columns. Loading sorts recipe IDs and input product IDs, rejects duplicate recipe IDs or duplicate ingredient products, and validates non-empty inputs, positive counts, known input/output products, known station kind, and the presence of a compatible authored cooking station. Definition identity hashes the product, inherited M18 forage, station, recipe, and recipe-input sources. Recipes remain definition/content truth and never enter mutable save state.

The output product is `sauteed-hen-of-the-woods`, displayed as `Sautéed Hen-of-the-Woods`, with buy price 0 and sell price 6 versus the raw mushroom's sell price 3. It is not stocked by the shop and is not hotbar-bound or consumable. Existing generic selling accepts any catalog product, so the cooked dish can be sold incidentally when the ordinary store preconditions hold; no economy mechanism changed.

## Station, targeting, and interaction

The authored `hearth-house-kitchen` object is a blocking one-tile Kitchen Stove in the existing `residence` / Hearth House scene. It carries semantic reference `Cooking`, uses no dynamic station state, and renders as the existing scene-object projection with a small station-specific color and label.

The exact interaction order is:

```text
actor -> portal -> ground item -> forage node -> plot -> cooking station -> shop
-> squared distance -> ordinal stable ID
```

Cooking uses the existing 1,280-unit reach, 640-unit facing-corridor half-width, squared-distance tie break, and stable object identity. No cooking-specific proximity query or index exists. For the single-recipe milestone, ordinary Interact lowers the selected station to `CookIntent(ActorId from envelope, StationId, RecipeId)`. `cook` and `cook sauteed-hen-of-the-woods` use the same typed resolver path in CLI/LLM control. The ingredient list and counts never come from UI or callers. The single-recipe lowering is a bounded convenience: when a station has more than one available recipe, the UI must present explicit recipe selection.

Before mutation the resolver validates actor identity/player authority, recipe identity, station identity/kind, actor scene, shared range/facing, and every required inventory count. It computes the checked output count before changing state. Only after all validation succeeds does it decrement every input and increment the one output. Success emits exactly one `RecipeCooked` event containing actor, station/scene, recipe, output product, and output count. Unknown recipe, wrong station, wrong scene, out of range, invalid actor, non-player actor, or missing ingredient returns one deterministic rejection with no event and no mutation. Two sequential requests for one mushroom therefore produce Accepted followed by `MissingIngredient`; repeated failure cannot duplicate output.

## Projection, persistence, replay, and controls

The player inventory projection automatically changes from raw mushroom to cooked dish. The station hint is `Cook Sautéed Hen-of-the-Woods [Interact]` when cookable and `Need Hen-of-the-Woods x1` afterward. Selected hotbar slot is unchanged. The existing MonoGame edge-trigger, Pause gate, and inventory-focus suppression remain the only input law; held E cannot submit every frame, and no renderer-side inventory mutation was added.

No save version bump is needed because cooking adds no mutable schema: the result is represented by existing product stacks. The existing world chunk persists it and the semantic state hash already canonically sorts inventory stacks. Definition identity carries recipe/product/station content independently. Human and Replay `CookIntent` sequences produce exact state, result, and event hashes. The deterministic composed proof gathers the existing Riverside M18 node, carries that same `ProductId` state into Hearth House staging, then cooks it.

The simulation DTO already exposes full projected inventory, current semantic target through frame/UI, and cookability through the interaction hint, so it does not dump the recipe catalog. The runner's `--m19-control` inspection adds the one recipe for bounded TableScript-style inspection. The authored TSON can also be queried directly by exact `recipeId`.

Live production MonoGame windows at 2560x1440 and 1280x720 showed Hearth House, the labeled Kitchen Stove, the raw `Hen-of-the-Woods x1` inventory row, hotbar, simulation HUD, and controls readable and unclipped while world time continued. Renderer-independent graphical projection tests additionally keep every hotbar/inventory rectangle in bounds at both resolutions. Automated Windows key taps are shorter than MonoGame's polled input edge and are not claimed as graphical transition evidence; the exact action transition is proved headlessly through the same `InteractIntent`/resolver/frame/UI path used by the graphical client. No screenshot bundle is retained.

## Evidence

The compact artifact set is exactly:

- `artifacts/tiny-farm-m19/proof.json`
- `artifacts/tiny-farm-m19/cooking.json`
- `artifacts/tiny-farm-m19/recipes.json`
- `artifacts/tiny-farm-m19/inventory.json`
- `artifacts/tiny-farm-m19/manifest.json`

Canonical hashes:

- state/replay `4dba151ed5a72e41de05bd5ecf53a3758dc153daf7521ac6a4ce480b31228d51`
- results `830e771c995887193660984cfd0e124b6702d4605c08bcf5ddbdb7efc2b093bd`
- events `50c460c05d36afd0d44cf40ffc17147dae943528da9bf09d74c75119a829880e`
- recipes `fa99f757237eb315741f8562c10c60f5623076f35f51ae82dc5171d5524b6607`
- products `e7e9c095624912227452c32d07b061d8705607f125d9671e59c48af2fd9f049b`
- inventory `dbac6859f50f8f1b5f74babb40015b82d2c0b386bda62e419b98f16c6d67d3be`
- projection `60391b34cbdcf9117118ca984819612231363d94642a503ac52970559974eaba`
- DTO `dfd05e740442300dfc5bf79355338a77a2be342fb1c95e6f402d27f1e054640e`
- M18-to-M19 composed loop `1c82a070d8eb647cc816a34f10df86466485f0b21f9ac2a09d34dcf6cdc50ae6`

Focused tests cover exact content, TSON loading, duplicate/unknown/empty/non-positive recipe validation, station identity and targeting, interaction lowering, success/event payload, ingredient consumption, output addition, failure atomicity, missing ingredient conflict, wrong station/scene/range, unknown recipe/actor, UI reprojection, hint transition, selected-slot stability, save/load, replay/repeat determinism, CLI/LLM parsing, renderer-independent semantic types, M18-to-M19 composition, and 1440p/720p graphical layout bounds. The full TinyFarm suite retains M13 timing, M14 locomotion, M15 allocation, M16 UI, M17 item-action, and M18 forage gates.

## Recommended M20

**TINY-FARM-M20 — one tool-based gathering verb.** M19 closes the raw-product transformation loop without exposing pressure for multiple recipes or a recipe browser: one station and one recipe are legible and sufficient. The remaining observed everyday-action gap is that gathering has no tool contract while hotbar-selected use already provides a resolver-owned semantic seam. M20 should qualify one existing-world node plus one explicit tool-gated intent, without adding durability, upgrades, quality, skill trees, random loot, multiple tools, or combat.
