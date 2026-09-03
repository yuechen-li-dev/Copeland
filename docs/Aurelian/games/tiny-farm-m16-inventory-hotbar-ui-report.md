# TINY-FARM-M16 — Inventory + Hotbar / Skill-Bar UI Foundation

## Outcome

**Outcome C — semantic UI foundation is complete through the real game path, with a temporary MonoGame realization.** Machina.UI was audited first and has suitable authoring, layout, semantics, hit testing, input records, and backend-neutral presentation IR. It does not currently have a qualified MonoGame same-window presentation translator or device-input adapter. Adding that bridge would dominate this game milestone, so M16 keeps the reusable semantic projection renderer-independent and realizes it locally in the existing MonoGame leaf.

## UI infrastructure audit

| Existing type/project | Relevant capability | Reuse directly? | Needed change |
| --- | --- | --- | --- |
| `Machina.Core.UI`, `Row`, `StackArrange`, `GridArrange` | Surface, layer, anchor, row/column stack, grid layout | No in MonoGame leaf | A qualified MonoGame renderer for `MachinaPresentationFrame` |
| `Machina.Standard.StandardUI` | Button, Card, Label, TextBlock, Input and theme-backed controls | No in MonoGame leaf | Same-window presentation and font realization |
| `Machina.Runtime.UiHitTestIndex` | Z-aware pointer hit testing over resolved layouts | No in MonoGame leaf | MonoGame pointer-event adapter plus prepared-frame lifetime |
| `Machina.Runtime.UiInputEvent` | Renderer-neutral pointer, keyboard, text, resize, close input | Pattern reused; no dependency | Number-row keys are not represented and no MonoGame adapter exists |
| `Machina.Pipeline.MachinaPresentationPipeline` | UI lowering, layout resolution, hit-test construction, presentation IR | No | MonoGame translation/composition seam |
| `OblivionWorkbench` / `OblivionInteractionMap` | Production use of StandardUI, actions, selection, scroll and focus-like routing | Audit evidence | Oblivion is raster/standalone hosted, not a MonoGame overlay adapter |
| `TinyFarmFrame` / `TinyFarmSimulationSnapshot` | Immutable renderer and inspection projections | Yes | Added a separate `TinyFarmPlayerUiView`; simulation snapshot v2 includes it |
| `TinyFarmSimulationHost` | Live time, mode, and semantic intent entry point | Yes | Selection intents skip unrelated NPC policy evaluation |
| `TinyFarm.MonoGame` | Existing world renderer, resize, keyboard/mouse sampling, layering | Yes | Local hotbar/panel draw and hit-test realization |
| Aurelian runtime/rendering | Renderer-neutral world/render contracts and CPU/Vulkan paths | No | No existing MonoGame + Machina composition host |
| JTF host infrastructure | General host/task evidence | No | No game viewport or same-window overlay primitive |

The selected ownership mode is `MONOGAME_TEMPORARY_UI`. No second UI framework, HUD DSL, widget schema, Aurelian/Machina unification, or windowing layer was added.

## Architecture and ownership

The final dependency/authority path is:

```text
TinyFarmState
  inventory stacks + selected hotbar slot
        |
        +--> TinyFarmPlayerUiProjector --> TinyFarmPlayerUiView
        |                                  |
        |                                  +--> MonoGame temporary overlay
        |                                  +--> simulation snapshot / CLI / LLM
        |
input --> TinyFarmPlayerUiController
        --> SelectHotbarSlotIntent
        --> TinyFarmSession
        --> TinyFarmResolver
        --> TinyFarmState
```

Inventory remains the existing `ActorState.Inventory` and `InventoryStacks`; there is no UI inventory. The fixed eight-slot bar is defined by `TinyFarmHotbar.DefaultSlots`. Slots 1 and 2 bind the existing `turnip-seed` and `turnip` products; slots 3–8 are explicitly empty. `HotbarBinding` is a narrow tagged record family with only `ProductHotbarBinding` in M16. It permits a later tool/ability case without adding any skill runtime now.

`SelectedHotbarSlot` is semantic player state, enters the version-6 semantic hash, mutates only through `SelectHotbarSlotIntent`, and persists in `tinyfarm.world` under runtime `tiny-farm-m16@6`. Fixed default bindings are authored C# content and are rebuilt, not persisted. Hover, inventory-open, and movement-suppression/focus state live only in `TinyFarmPlayerUiController` and are not saved.

An empty slot may be selected and yields no selected semantic binding. A fixed product binding remains when count reaches zero and projects as `Unavailable`; it is not silently cleared.

## UI and input result

The inventory panel shows deterministic semantic-ID-ordered item names and authoritative counts. The bottom-center hotbar shows all eight slots, count, empty/unavailable state, and a gold selected border. Placeholder text is used instead of an asset pipeline.

Both number keys and pointer clicks call the controller's one selection path and issue `SelectHotbarSlotIntent`. The old M13 conflict is resolved as follows:

| Input | M16 meaning |
| --- | --- |
| `1`–`8` | Select semantic hotbar slot |
| `I` or HUD Inventory button | Toggle inventory presentation |
| `Space` | Toggle Pause/Play |
| `F` | Toggle Play/FastForward |
| `N` | Wait one game minute |
| WASD/arrows | Move when inventory is closed |
| F5/F9 | Save/load |

Opening inventory does not pause or mutate game truth. While it is open, player movement and world interaction keys are suppressed; hotbar selection, inventory close, Pause/Play, and FastForward remain available. This is the exact M16 focus law. NPC/world simulation continues.

The pre-existing HUD remains a separate bottom band with mode, day, time, location, money, controls, narrative/status, and save/load. Inventory is not duplicated into that HUD.

## Projection, CLI, LLM, and TSON

`TinyFarmPlayerUiView` contains Money, Inventory, Hotbar, SelectedSlot, SelectedSemanticId, and InteractionHint. It contains no XNA, MonoGame, texture, color, or SpriteBatch types.

`TinyFarmSimulationSnapshot` conditionally projects player UI for version-6 state and identifies itself as `tiny-farm-simulation@2`. Canonical TSON v2 adds money, selected slot, selected semantic ID, and deterministic inventory/hotbar summaries. Older M13–M15 states continue to emit the byte-compatible v1 shape.

The runner provides `--m16-control` with `inspect` and `select-slot <1-8>`. The MonoGame LLM control surface accepts the same `select-slot <1-8>` semantic request and returns `playerUi`; no mouse emulation is involved.

`UseSelected` was deliberately deferred. Existing planting requires a valid facing-derived plot plus resolver-owned `PlantIntent`, but binding selection alone does not justify a new use/action dispatch system. M16 therefore changes no farming, inventory decrement, target selection, pickup, crafting, combat, skill, cooldown, health, or mana semantics.

## Responsive and graphical proof

At 2560×1440 the real MonoGame window rendered the farm as the dominant surface, a readable centered eight-slot hotbar, the existing HUD, and visibly moving Elias. At 1280×720 the same eight slots and right-side inventory panel fit without clipping. Pointer selection visibly moved the gold border from slot 1 to slot 2. With the inventory panel open, Elias changed position in successive captures while mode remained Play, proving the overlay does not freeze or implicitly pause the world. No screenshots were added to the recurring artifact bundle.

Headless geometry tests cover both exact target viewports. The layout stays within the viewport and reserves the existing HUD rather than shrinking the world into a separate pane.

## Regression and validation record

M13 simulation timing remains on the established host and command types; only the MonoGame key bindings changed. M14 live wander remains visible and its canonical scenario still runs. M15 keeps the single movement core and allocation gates; hotbar projection is render/request-time and is absent from locomotion reductions.

Tests added cover exact projection, deterministic ordering, default bindings, selected slot, empty-slot selection, zero-count binding behavior, keyboard/click parity, presentation-only inventory open, movement suppression, non-pausing inventory, simulation remap, save/load, 1440p/720p layout, snapshot/TSON inspection, renderer leakage, and canonical Outcome A.

Validation completed on 2026-09-02:

- `dotnet test TinyFarm.slnx -c Release -m:1`: 186 passed.
- `dotnet test Machina.UI.slnx -m:1`: 673 passed.
- `dotnet test Machina.UI.Slow.slnx -m:1`: 308 passed.
- `dotnet test Aurelian.slnx -m:1`: 606 passed.
- `dotnet test JointTaskForce.slnx -m:1`: passed, including 3,178 reported tests across the constituent suites.
- Release builds of TinyFarm.Runner and TinyFarm.MonoGame: succeeded with zero warnings/errors.
- M13, M14, M15, and M16 canonical runners: Outcome A. M14 retained state hash `a0d79da0f0590d1c77d1a27bd19494e1ae68dd16ae8c46caccb20dfcbcb8fd84`; M15 retained 857.52136 B/full reduction and 48.0004 B/core reduction.
- M16 CLI `inspect` / `select-slot 2` smoke: passed.
- Scoped `dotnet format`, renderer-leakage boundary search, compact artifact-budget check, and `git diff --check`: passed.

The compact artifact set is:

- `artifacts/tiny-farm-m16/proof.json`
- `artifacts/tiny-farm-m16/ui-projection.json`
- `artifacts/tiny-farm-m16/input-routing.json`
- `artifacts/tiny-farm-m16/host-integration.json`
- `artifacts/tiny-farm-m16/manifest.json`

The TinyFarm GitHub workflow now runs the M16 proof, CLI selection smoke test, semantic ownership assertions, and exact five-file budget after M14/M15 validation.

## Recommended M17

**TINY-FARM-M17 — resolver-owned pickup and one selected-product use interaction.** The observed pressure is no longer selection or layout; it is that the bar can identify Turnip Seed but cannot yet lower an explicit UseSelected request through existing facing/interaction targeting to `PlantIntent`. M17 should add that single action seam plus pickup feedback, retain fixed bindings, and defer assignment UX, drag/drop, crafting, combat, and skills.
