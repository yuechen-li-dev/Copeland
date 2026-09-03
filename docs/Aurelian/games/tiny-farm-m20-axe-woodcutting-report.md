# TINY-FARM-M20 — Axe-Gated Woodcutting

## Outcome

**Outcome A — Axe is a real semantic capability selector.** The player owns one identity-bearing `axe` item, slot 3 binds that item, one TSON-authored Farm Tree yields `wood x1`, and selected use lowers through the closed Core mapping to `ChopIntent(farm-tree)`. `TinyFarmResolver` independently requires Axe ownership, validates the Tree and shared spatial target law, atomically adds Wood to `InventoryStacks`, changes Standing to Depleted, and emits one `TreeChopped` event. UI, world frame, simulation DTO, save/load, and replay then reproject that state.

## Audit and identity law

The mandatory audit found no Tool, Axe, Chop, Tree, Wood, Mine, or generic resource-node model. It found two existing inventory semantics: identity-bearing `ItemId`/`ItemState` plus `ActorState.Inventory`, and fungible `ProductId` plus `InventoryStacks`. It also found the concrete M18 `ForageNodeDefinition/State`, the shared 1,280-unit facing target query, M16 `ProductHotbarBinding`, M17 closed `UseSelectedIntent` lowering, and resolver-owned Take/Plant/Gather/Cook paths.

Axe therefore uses the existing identity item model. M20 production staging creates one `ItemState(axe, Axe)` owned by the player and includes that ID in the player's inventory. It has no durability, stats, material, tier, quality, speed, or price behavior. Save/load preserves both sides of the existing ownership invariant. Wood uses `ProductId("wood")`, display name `Wood`, buy 0, sell 2, and is not shop stock. Generic selling can accept it incidentally.

The hotbar changes only from one product case to the closed sum `ProductHotbarBinding(ProductId) | ItemHotbarBinding(ItemId)`. Slots 1 and 2 remain Turnip Seed and Turnip; slot 3 is Axe; slots 4–8 remain empty. Older pre-M20 save versions project slot 3 as historically empty, preserving M16 artifacts and tests. M20 projects Axe as available only when the identity item is actually owned. Selection remains the same persisted integer and never grants ownership.

## Tree, targeting, and resolver law

`TreeDefinition` contains only Tree ID, Scene ID, authored position, yield product, and fixed yield count. `TreeState` contains only Standing or Depleted. The one TSON row is `farm-tree`, Farm, tile 11,5, Wood, count 1. The loader validates unique IDs, scene/product references, bounds, and positive yield, materializes a concrete blocking `SceneObjectKind.Tree`, and includes the source in definition identity. It does not reuse or generalize `ForageNode`.

The exact interaction priority is:

```text
actor -> portal -> ground item -> forage node -> tree -> plot -> cooking station -> shop
-> squared distance -> ordinal stable ID
```

Standing Trees use the existing 1,280-unit reach and 640-unit facing corridor. Depleted Trees remain in the projected scene as a stump but leave the target set. Ordinary E/Interact does not chop. Q/UseSelected with slot 1 returns `WrongTool`; an empty slot returns `NoSelectedBinding`; slot 3 without owned Axe returns `SelectedBindingUnavailable`. Slot 3 plus Tree lowers to `ChopIntent(TreeId)` without mutation in the adapter.

Direct Chop is independently authoritative. It requires a valid player actor and owned Axe, then validates tree identity, scene, standing state, authored product/yield, and the same facing/range target. Failures are `UnknownActor`, `UnknownTree`, `WrongTargetKind` for non-player, `MissingAxe`, `TreeWrongScene`, `TreeOutOfRange`, or `AlreadyDepleted`, with no event or mutation. Only after every check succeeds does the resolver compute the checked Wood count, update the stack, and deplete the Tree. Axe ownership and slot 3 selection remain unchanged. A second Chop returns `AlreadyDepleted` and Wood remains 1.

Success emits exactly one `TreeChopped` event with actor, tree, scene, scene object, Wood product, and count. The focused result is the existing accepted semantic result plus that event; rejected results carry deterministic reasons and no events. Human selected-use, direct Chop, repeat, and Replay paths have equal state and event semantics.

## Projection, controls, persistence, and composition

Before selection, targeting the Tree shows `Requires Axe`. With slot 3 selected it shows `Chop Tree [Use]`. After Chop, the tree frame carries Depleted and MonoGame renders a trunk-only stump; inventory shows `Wood x1`; Axe stays present, available, and selected. Layout qualification covers 2560×1440 and 1280×720 without clipped hotbar or inventory rectangles. The graphical host loads M20 content/state, still advances the existing clock and NPC locomotion, and contains no gameplay mutation.

CLI/LLM commands are semantic: `select-slot 3`, `use-selected`, and `chop [tree]`. They submit typed intents rather than keys or coordinates. The existing human Q path submits the same `UseSelectedIntent`; E remains contextual interaction. The version-9 world save stores Axe ownership through existing item state, selected slot, Wood through existing stacks, and Tree depletion through the new concrete state list. Semantic hash adds canonical Tree state; definition identity adds Wood/product and Tree source. Simulation snapshot v5 exposes selected Axe binding, inventory, current target, forage state, and Tree state.

The renderer-free composed proof performs Farm Axe/Tree Chop, Riverside Hen-of-the-Woods Gather, then Hearth House Cook through three distinct resolver verbs. It adds no mechanics beyond their composition. This is the intended future symmetry:

```text
selected Axe + Tree -> ChopIntent
selected weapon + enemy -> AttackIntent
```

Attack, weapons, enemies, and combat are not implemented. Pickaxe/Mine, Fishing Rod/Fish, Hoe/Till, and Watering Can/Water likewise remain future concrete pressure, not current cases or registries.

## Evidence

The compact artifact set is exactly five files under `artifacts/tiny-farm-m20/`: `proof.json`, `woodcutting.json`, `hotbar.json`, `parity.json`, and `manifest.json`.

Canonical hashes:

- state/replay: `50538582cef7cc9740c1e0ca8255d1694d8fc88c491804ac013f40d8d7b29b17`
- results: `efea4b4c5edc22bf56e56d84c123df114386f853e7e68f7d702159e8b50035d2`
- events: `86b518f6a24223149dd1a670249b72d1da875f78555e74caa66e81ae7fd4aeae`
- tree: `bdd0538c13d24c7901c017917d72f118890c358a17e9bcfdfe6676a8a31b207a`
- wood: `c34ba0ceb25ed2e7cc9105b0815ed286044e1e6dd19e89f99af574c49029da7f`
- hotbar: `2b0de67c2d94da82e872999ff2d75b3608770080b81173ab6ca88e1fc4832f9d`
- use-selected parity: `9fb76b61ae980942fa2607a6596e1de1cb0310f97bfe9b3612e3983fa26d1ae2`
- chop parity: `53ec6879d364870f38a4783a8d1dbdf9cec77c377ad4c9bd1c3c5305601238e6`
- projection: `3a29810805d3973fa7440a705b100efdb5a36dddcafd02bb996c19f901cd9b2c`
- DTO: `72bfabe1edd6399c1e4742a7166742eacdf71eef6cf94da3702c81101a0690bb`
- definitions: `371ff2612832b0dd8a56bc3205cfaca25da84c7659ebb331ccfb656d946e06de`

Fourteen focused tests cover exact Axe/Wood/Tree definitions, closed hotbar cases, targeting, wrong/empty/unavailable selection, selected/direct parity, atomic success and event payload, non-consumption, second Chop, depleted targeting, all required invalid cases and no-mutation hashes, save/load, replay scenario, semantic CLI, DTO v5, M17 Plant regression, both graphical sizes, continuing clock/NPC locomotion, renderer independence, compact artifacts, and twelve required proof hashes. The existing TinyFarm suite remains the executable M13–M19 and M15 allocation regression.

Release validation passed `dotnet test TinyFarm.slnx -m:1` with 238/238 tests, `dotnet test Aurelian.slnx -m:1`, `dotnet test JointTaskForce.slnx -m:1`, and the standalone MonoGame build with zero warnings/errors. The M20 CI-equivalent proof and semantic control smoke passed, including `WrongTool`, `TreeChopped`, `AlreadyDepleted`, and `Wood x1`; exact five-file/256-KiB checks passed, as did the repository artifact-budget gate and `git diff --check`. M15 remains Outcome A at 857.52136 B/full movement reduction and 48.0004 B/core reduction, within the 1024/128-byte gates. M16 and M19 canonical state hashes remain exact; M19's complete ten-hash bundle was re-run unchanged. Both 2560×1440 and 1280×720 MonoGame launch smokes initialized successfully, while focused projection/layout assertions prove the Tree/Axe/Wood/stump composition and viewport bounds without retaining screenshots. Remote GitHub Actions was not dispatched from this local checkout; the checked-in workflow contains the same passing M20 qualification.

## Recommended M21

**TINY-FARM-M21 — first one-hit weapon/enemy combat slice mirroring M20.** M20 resolved the only demonstrated infrastructure pressure: an identity item can now occupy the hotbar and gate a typed target action. A second gathering tool would mostly repeat that proof. The next materially different pressure is one selected identity weapon plus one semantic enemy target lowering to `AttackIntent`, which tests target-owned health/defeat and hostile-world mutation without justifying a generic capability registry, equipment system, stats, cooldowns, skills, or combat framework.
