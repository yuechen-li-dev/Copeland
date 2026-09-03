# TINY-FARM-M17 — Resolver-Owned Pickup + UseSelected

## Outcome

**Outcome A — the world/inventory/hotbar loop is complete and resolver-owned.** Ordinary graphical play, the headless runner, and the LLM surface can target and take a grounded identity item, select Turnip Seed, target an empty farm plot, and issue `UseSelectedIntent`. The resolver lowers that closed request into the existing `PlantIntent` semantic core. World-item ownership, identity inventory, product counts, crop state, and selected slot remain `TinyFarmState` truth.

## Existing item and pickup audit

The pre-M17 model deliberately has two inventory laws:

- `ItemId` identifies a durable `ItemState`. Ownership is mirrored by `ItemState.Owner` and `ActorState.Inventory`. The existing `TakeIntent` already performs the correct authoritative transfer and emits `ItemTaken`.
- `ProductId` identifies stackable production content. Counts live in `InventoryStacks`; shop, planting, and harvesting already use this model. Turnip Seed and Turnip are products, not identity items.

M17 preserves that distinction. The pickup proof uses the existing Wild Mint `ItemState`; selected use uses the existing Turnip Seed product. No conversion, shared base item model, tags, registry, scripting dispatch, or generic usable-item interface was added.

The demonstrated M3-era seam was placement. `ItemState.GroundLocation` was sufficient for the coarse map but not facing/range selection in a continuous scene. Version-7 grounded items therefore also carry an authoritative `GroundScene` and fixed-point `GroundPosition`. Owned items carry neither. This placement is persisted in the existing world chunk, enters the semantic hash, is validated on load, and contains no renderer type or pixel coordinate.

## Pickup targeting and resolver law

`TinyFarmSpatialQueries` now includes grounded item candidates using the existing 1,280-unit forward reach and 640-unit half-width. The deterministic priority law is actor, portal, ground item, plot, shop; candidates of one kind then order by squared distance and ordinal stable ID. Ground item wins over the co-targeted plot in the canonical start; after pickup removes its ground placement, the unchanged facing target becomes the plot. Ground items do not enter movement blocking.

The graphical `Interact` path is:

```text
player scene position + facing
  -> InteractionTarget(ItemId)
  -> InteractIntent
  -> existing TakeIntent reduction
  -> TinyFarmResolver
  -> ItemState owner + ActorState.Inventory
  -> ItemTaken
  -> fresh frame/UI projection
```

Direct `TakeIntent` remains compatible. In version-7 state it additionally verifies the exact item's scene placement against the same facing/range law. Unknown item, non-ground/previously taken item, wrong coarse location, out-of-range item, and unknown actor reject deterministically without mutation. First valid reduction wins a duplicate-take race; the second returns `ItemNotGround`.

## UseSelected closed lowering

`UseSelectedIntent` is one game intent, not an item framework. The resolver reads `SelectedHotbarSlot`, resolves the fixed semantic binding, verifies availability, selects the existing interaction target, and performs this sole M17 match:

```text
ProductHotbarBinding(turnip-seed) + FarmPlotId
  -> PlantIntent(plot, turnip)
  -> existing ResolvePlant core
```

An empty slot returns `NoSelectedBinding`; a zero-count bound product returns `SelectedBindingUnavailable`; owned Turnip returns `UnsupportedSelectedUse`; no facing target returns `NoInteractionTarget`; and a non-plot target returns `WrongTargetKind`. Once lowered, the underlying `PlantIntent` reasons and `CropPlanted` event remain intact. `ResolvePlant` alone checks location/range, plot occupancy, crop identity, and seed availability, decrements the seed stack, and constructs crop state. Direct `PlantIntent` and `UseSelectedIntent` produce equal final semantic state, event sequence, status, and reason for both success and occupied-plot failure.

## Projection, controls, persistence, and replay

Frame projection now realizes version-7 ground items in the active scene; pickup removes the item because the authoritative placement is gone. Inventory and hotbar are still request/render-time projections. The UI hint is `Take wild mint [Interact]` while the item is targeted and becomes `Plant Turnip Seed [Use]` after pickup exposes the plot. Planting immediately reprojects the seed count from three to two and the plot crop from empty to Turnip.

The final gameplay map is:

| Input | Meaning |
| --- | --- |
| `E` or Enter | Contextual Interact; takes a targeted ground item |
| `Q` | Use selected semantic binding against the target |
| `1`–`8` | Select hotbar slot |
| WASD/arrows | Move and face |
| `I` | Inventory |
| Space / `F` / `N` | Pause-Play / FastForward / Wait |
| F5 / F9 | Save / load |

Both action keys are edge-triggered. Inventory focus and Pause suppress pickup/use; neither action changes pause state. The runner adds `--m17-control` with `inspect`, `select-slot N`, `pickup`/`interact`, and `use-selected`. The LLM parser accepts `take`, `pickup`, `interact`, `select-slot N`, and `use-selected` as semantic commands, never key or mouse emulation.

Simulation snapshot v3 and canonical TSON v3 add deterministic ground-item, target, and plot summaries beside the M16 inventory/hotbar projection. Version-7 chunked save/load restores pickup, selection, planted crop, counts, placement absence, result cursor, and events exactly. Human, CLI/LLM, direct, and replay-origin envelopes all use the same resolver switch; no replay-only mutation path exists.

## Proof and validation

The canonical headless run starts with Wild Mint visibly targeted in front of the player and an empty plot behind it. Interact takes Wild Mint, selection remains slot 1, and UseSelected plants the newly exposed plot. It then saves, reloads, repeats independently, and compares semantic state, result, and event hashes.

Real-window checks at 2560x1440 and 1280x720 showed the grounded Wild Mint, readable interaction hint, selected hotbar slot, inventory panel, farm plots, and continuously wandering NPCs without clipping. Short synthetic key injection was not stable enough to serve as action-transition evidence in the realtime MonoGame loop; pickup and planting transitions are instead proven through the production semantic controls, resolver, frame projector, CLI, and headless canonical path. No screenshot bundle is retained.

The compact proof records:

- state `de25b5a82a4c2c668da70d5292e7fafbd223cd8de6916226076d6681a45ff854`
- results `10e62762edb9fbcbae8b0cad96206fcdd584fff06e9a5e42114b549cd7144b9f`
- events `e24b99a1b0696150ef1c3675a5a049ef689a64add851263d6f8db015f6ef6d8f`
- pickup `2814316a0a3d1da6866b72656455fef1205ef6b5e17d29eb078ac3ac6358a33f`
- inventory `6e9957d8051ea489a8ddb54cfece9de5d1bec29da9a46954108243bf2088839f`
- hotbar `2461712703b31d37fc3056e91160aa99bf1c11c547aa01085894cc74a7ec31c5`
- use-selected `de25b5a82a4c2c668da70d5292e7fafbd223cd8de6916226076d6681a45ff854`
- plant parity `9e77fad2b1e47a4083471048f6ef6af2153a35bfdb55b536df243f371311be28`
- projection `e55ffc1ce6457444d7f01a156b3c4aa5bf2d4d0b1e6b942cf8264a3256711c59`
- simulation DTO `fcc77b6a71892ccfade8d255131b5a7962b5a7c1b72a062742bbc541f62261cf`

The focused tests cover target selection/priority, pickup transfer/removal/projection, range rejection, duplicate race, empty/unavailable/unsupported use, successful and failing Plant parity, resolver-owned decrement, focus suppression, semantic CLI/LLM parsing, snapshot TSON, save/load, and repeated canonical proof. M13 timing, M14 locomotion, M15 allocations, M16 input/UI, solution builds, renderer/authority boundary searches, artifact budget, formatting, and diff checks are recorded in the final validation run.

The artifact set is exactly:

- `artifacts/tiny-farm-m17/proof.json`
- `artifacts/tiny-farm-m17/pickup.json`
- `artifacts/tiny-farm-m17/use-selected.json`
- `artifacts/tiny-farm-m17/ui-projection.json`
- `artifacts/tiny-farm-m17/manifest.json`

## Recommended M18

**TINY-FARM-M18 — one resolver-owned product drop from an existing harvest result.** Actual play now exposes one concrete asymmetry: identity items can exist in scenes, while harvested stackable products enter inventory directly and therefore cannot exercise pickup counts. The next bounded pressure is one authored/harvest-created ground product stack with scene placement, deterministic pickup into `InventoryStacks`, and save/replay proof. It should not add resource nodes, gathering tools, crafting, tags, drag/drop, combat, or a general drop/item-behavior system.
