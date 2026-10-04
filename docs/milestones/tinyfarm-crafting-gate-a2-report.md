# TinyFarm crafting vertical slice — Gate A2

Outcome A: the opening campaign now connects harvested turnips and gathered rock salt to a stove menu, deterministic SP costs, cooking practice, discovery, and food recovery. Ingredient acquisition remains the resource constraint. Food is deliberately allowed to restore more SP than cooking consumes.

## Play and review

Launch `Play-TinyFarm.cmd` normally. Harvest the ripe turnip with E. Plant another with 1 + K, water with E, and sleep at home to grow it. Gather rock salt from the small outcrop southeast of the garden. Enter the house and face the stove; E opens Cooking.

Pick up the paper recipe card beside your bed and use **Read recipe** in I, or discover soup by experimenting with one turnip and one water. Water is supplied by the stove. Click ingredient rows to add units and the adjacent minus buttons to remove them. Cook submits the staged inputs; closing retains everything in your inventory. A learned recipe fills the base inputs automatically. Add one rock salt for the richer variant. Eat result consumes the selected food; R outside menus eats available food. C shows live Cooking/Fire XP, SP, and Exhausted.

The menu pauses the world. Its InputMan context excludes Gameplay, and opening or staging inputs does not change semantic state. Inventory/stat search keeps the exclusive text-entry map from A1.

## Initial tuning

Cooking rank begins at 0. Plain turnip soup is level 0; salt increases effective difficulty by 3. Each crafting rank requires 100 XP, with remainder carried and rank capped at 50. The existing non-crafting curve remains in use for incidental Fire practice. These are initial tuning constants, not a finished balance pass.

Let `g = effective recipe level - Cooking rank`:

- Known SP cost: `baseSP + 2g + g²` for positive gaps; otherwise `max(1, baseSP + g)`.
- Unknown successful recipes cost `ceil(1.5 × known cost)` and learn the base recipe.
- Below-rank XP: `max(1, baseXP / (1 + g²))`, integer division.
- At/above-rank XP: double every three difficulty levels with intermediate thirds; the XP bonus caps at a gap of 9.
- Fire XP is `min(5, CookingXP / 4)`. Trivial recipes therefore grant no incidental Fire XP.

| Rank 0 recipe | Known SP | Unknown SP | Cooking XP | Food recovery |
| --- | ---: | ---: | ---: | --- |
| Turnip + water | 4 | 6 | 20 | 4 HP + 12 SP |
| Turnip + water + rock salt | 19 | 29 | 40 | 4 HP + 20 SP |

The player starts with 20 SP. Unknown salted soup exceeds that pool; reading or discovering the base recipe makes the modifier feasible. Authored larger pools allow higher-level catch-up: known level 6 costs 52 SP and awards 80 XP; level 9 costs 103 SP and awards 160 XP. Progression eligibility belongs to the agent and does not depend on current control binding.

An insufficient-SP attempt commits SP=0 and Exhausted while retaining ingredients and awarding no output, discovery, or XP. Its intent result is Accepted with `InsufficientSpirit`: the failed attempt itself has a committed consequence. Exhausted clears when food restores at least 20% of maximum SP, or on rest. It does not lock movement. Sleep and automatic night return restore SP as a fallback.

An unmatched valid experiment costs 2 SP, retains ingredients, and grants no XP. Malformed inputs, missing ingredients, invalid/out-of-range stations, unavailable RPG state, and insufficient output capacity reject unchanged. Output preflight accounts for ingredients freeing slots: 16 product stacks, 99 units per output stack. Food at full HP still restores SP; full HP and SP preserves the food.

## Ownership and reuse

`TinyFarmCraftingRules` and food/modifier metadata live on the existing recipe/product definitions. `TinyFarmCrafting` provides a shared deterministic quote, and the resolver recomputes every submission from actual inventory, station proximity, and agent profile. The UI's staging dictionary is transient and is never saved or hashed. Core owns all crafting outcomes; the existing session, generated serializers, save codec, replay, and Machina table project those outcomes.

Plain and salted soup are registered products with distinct food effects, using existing inventory stacks. This preserves modifiers through save/load without a second food inventory or dynamic item-effect framework. The stove supplies water; rock salt uses the existing daily-refresh forage lifecycle. Normal A2 stock sells seeds, not harvested produce or prepared food. The starter ripe crop provides onboarding; replenishment uses planting/watering/rest.

Save version 15 / runtime `tiny-farm-crafting@15` carries knowledge provenance, SP, skill XP, typed Exhausted, recipe cards, and variant product identities. Knowledge IDs and condition station provenance are validated. The A2 definition identity and `sleeping-spring-a2` slot are separate from historical Gate A saves. Existing proof flags and tests retain their older content paths; normal native launch selects A2.

The painterly opening previously omitted loose items and forage objects. A2 renders the paper card and a simple salt outcrop marker, independently of HUD visibility. Existing art is reused; no image generation or external asset dependency was added. The salt marker remains provisional art.

## Evidence and validation

`artifacts/tinyfarm-crafting-gate-a2/` contains native captures and `native-proof.json`. The continuous Vulkan walkthrough uses keyboard/mouse input without mid-loop fixture loads or position writes: starter harvest → plant/water → salt gathering → failed unknown salted attempt → rest → plain experiment/discovery → eat at full HP → player-grown harvest → known salted craft → eat → 1440p resize → save/load hash agreement. Device: NVIDIA GeForce RTX 3070. Reflection serialization was disabled explicitly in the qualification build.

Validation:

- Native Release build: zero warnings/errors.
- `dotnet test TinyFarm.slnx -c Release -m:1`: 438 passing tests (411 TinyFarm, 27 Spatial2D).
- 18 focused A2 cases: economy, known/unknown costs, modifiers, exhaustion, recovery/rest, missing/malformed inputs, full output stack, ranks/remainders, higher-level catch-up, recipe-card ownership, generated replay, save round trips, and menu input isolation.
- A1 + A2 with `-p:JsonSerializerIsReflectionEnabledByDefault=false`: 31 passing tests.
- Native proof at 1080p and resize to 1440p; save/load semantic hash agreement.

Reproduce the native qualification:

```powershell
dotnet build src/TinyFarm/TinyFarm.Native/TinyFarm.Native.csproj -c Release -m:1 -p:JsonSerializerIsReflectionEnabledByDefault=false
dotnet src/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --crafting-proof
```

## Deliberate boundaries

One cooking recipe and one optional modifier establish the loop. The ingredient table is bounded to this slice's small inventory, rather than introducing a general crafting browser. General traits, derived ability growth, non-cooking crafting stations, max-SP buffs, hunting recipes, and a larger recipe catalogue remain deferred. Cooking rank already changes future costs and XP; combat remains under its existing owner. The basic rock/paper markers need eventual art refinement. The next useful review is hands-on tuning of the acquisition → cooking → recovery cadence before adding more stations.
