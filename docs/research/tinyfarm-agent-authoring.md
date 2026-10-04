# TinyFarm agent authoring

Use a reusable **template** for defaults, a **spawn declaration** for identity and placement, and `TinyFarmAgentAuthoring.Build` to validate and materialize the world. These are ordinary typed C# records. There is no new DSL, ECS, reflection-based registry or second inventory system.

Start with `src/TinyFarm/TinyFarm.Runtime/TinyFarmAgentExamples.cs` for an executable example. The file is a development fixture, not extra campaign content.

## Create a character and an object

```csharp
TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();
TinyFarmState initial = TinyFarmSliceContent.Start(definitions);

var neighbour = TinyFarmAgentTemplate.Character("garden-neighbour") with
{
    Money = 5,
    Level = 2,
    Conditions = ["well-rested"],
    Items = [new TinyFarmAgentItemSeed("hoe", "Garden hoe", 4, EquipmentSlot.Tool, Equip: true)],
    Appearance = new TinyFarmAgentAppearance(TinyFarmAgentSprite.Gardener, ScalePercent: 110)
};

var cache = TinyFarmAgentTemplate.Object("supply-cache") with
{
    Products = [new TinyFarmAgentProductSeed(TinyFarmIds.TurnipSeed, 3)]
};

TinyFarmAuthoredWorld world = TinyFarmAgentAuthoring.Build(initial, definitions,
[
    neighbour.Spawn("ivy", "Ivy", TinyFarmSceneIds.Farm, new GridPosition(8, 7)),
    neighbour.Spawn("leo", "Leo", TinyFarmSceneIds.Farm, new GridPosition(9, 7)),
    cache.Spawn("garden-cache", "Garden cache", TinyFarmSceneIds.Farm, new GridPosition(7, 7))
]);

var session = new TinyFarmSession(world.State, world.Definitions);
```

Each spawn gets its own actor identity, inventory, scene placement and agent-local state. Local item keys become stable IDs such as `ivy.item.hoe` and `leo.item.hoe`; both the owner's inventory and the item's owner are written together. Product stacks use the existing product catalogue. Conditions and inventory are copied, so editing a template or another instance cannot mutate a spawned agent.

`Slot` belongs to the item's capability; `Equip` chooses the initial loadout. An unequipped spare tool can therefore use `Slot: EquipmentSlot.Tool` without `Equip: true`. That capability survives transfer to another agent. Slot assignment is shared data; custom tool/weapon gameplay actions still require their own later item-action definitions.

Character defaults: Idle control, 12/12 authored health, level 1, no conditions or items, 9,000 energy and the existing Gardener sprite. Object defaults: Idle control, no health, Closed pose, no walking animation and the ObjectMarker presentation. Objects use the same actor/inventory/session/save path as characters. The current ObjectMarker deliberately reuses the existing well sprite as a development placeholder; it is not new chest artwork.

Template overrides are `with` expressions. Placement is in semantic tiles, converted to the existing fixed-point scene coordinates. For sub-tile placement, override a spawn's `Position` with a `ScenePosition`; for facing or a semantic location alias, override `Facing` or `Location`.

## Give a character a schedule

```csharp
var worker = neighbour with
{
    Id = "home-worker",
    Control = TinyFarmAgentControl.Schedule,
    Routine =
    [
        new TinyFarmAgentScheduleStop(0, 1440, TinyFarmAnchorIds.FarmHome, "Home")
    ]
};

TinyFarmAgentSpawn spawn = worker.Spawn("leo", "Leo", TinyFarmSceneIds.Farm, new GridPosition(9, 7));
TinyFarmAuthoredWorld world = TinyFarmAgentAuthoring.Build(initial, definitions, [spawn]);
```

Daily stops compile into existing Required schedule windows. Anchors, navigation and Dominatus schedule decisions retain their current owners. Override `spawn.Routine` for an instance-specific routine. A Schedule agent must have coverage throughout the supported seven-day schedule horizon; missing windows and unknown anchors fail during authoring with the instance ID and minute. Idle agents need no schedule. Passive characters and objects share one generated Dominatus Idle flow and retain independent brain instances in the session.

`Build` is the normal entry point: it returns **both** the authored definitions and state. Keep them together. Its generated declaration fingerprint extends the existing definition identity, so a checkpoint cannot silently reload with another authored cast. Declaration order is normalized by instance ID. `Compile` is the lower-level materializer for callers that already own the definition identity and schedule catalogue; it does not compile `Routine` stops.

## Reposition existing agents

```csharp
TinyFarmAgentSpawn placement = TinyFarmAgentSpawn.PlaceExisting(
    TinyFarmIds.Mara,
    "Mara",
    TinyFarmSceneIds.Farm,
    ScenePosition.FromGrid(new GridPosition(6, 5)),
    location: TinyFarmIds.Farmhouse);

TinyFarmState repositioned = TinyFarmAgentAuthoring.Compile(initial, definitions, [placement]);
```

`PlaceExisting` preserves the agent's inventory, health/loadout owner and other state; it updates name and placement together. `TinyFarmSliceContent.OpeningCast` now uses this path instead of separate actor-location and spatial-placement switches. Existing opening actors keep their legacy state representation and save/hash behavior; no synthetic HP or equipment copy is added to them.

## Validation and inspection

Compilation publishes a fresh indexed `TinyFarmState` only after validation. A failure leaves the source untouched. It rejects duplicate instance IDs, spawning over an existing identity, invalid local item keys, item-ID collisions, unknown products, non-positive stacks, duplicate equipment slots, invalid health/level/conditions, invalid controller/pose values and blocked, out-of-bounds or mismatched placements. ASCII letters, digits, dot, underscore and hyphen are accepted in semantic IDs; display names remain ordinary text.

Authored actor extensions use save version 13 and generated JSON metadata. Existing version-10/11/12 worlds remain supported. Equipment changes preserve a later save version rather than downgrading it to 12. Existing item-transfer reduction clears an authored agent's equipment slot when ownership leaves its inventory.

`TinyFarmAgentInspector.Inspect(state, definitions, actorId)` exposes identity, control, location, health, level, conditions, inventory, equipment, object pose and presentation from their current authoritative owners. `TinyFarmInventory.Project(state, definitions, actorId)` inspects any agent's bag, including an object. Neither API maintains another state store. Appearance affects projected sprites and scale but is excluded from the agent's semantic hash.

Run the native fixture interactively:

```powershell
.\Play-TinyFarm.cmd --agent-authoring
```

It uses a separate development save directory under `artifacts/tinyfarm-agent-authoring/saves`. Normal launch still runs the existing opening campaign. Reproduce the headless and native evidence:

```powershell
dotnet build TinyFarm.slnx -c Release -m:1 -nodeReuse:false -p:JsonSerializerIsReflectionEnabledByDefault=false
& .\src\TinyFarm\TinyFarm.Runner\bin\Release\net10.0\TinyFarm.Runner.exe --agent-authoring
& .\src\TinyFarm\TinyFarm.Native\bin\Release\net10.0-windows\TinyFarm.Native.exe --agents-proof
```

The headless command also accepts `--artifact-dir <path>`. Evidence lives in `artifacts/tinyfarm-agent-authoring`: agent inspection, save round-trip proof and actual Vulkan captures of the spawned agents and unchanged player inventory UI.

## Boundaries of this tooling pass

Authoring and world instantiation work now. This does **not** claim the entire old gameplay layer has been migrated to arbitrary-agent actions.

- The host still has one human binding on the existing player ID. Adding a second Human spawn fails explicitly. Possession of Mara and local multiplayer require a controller-ownership/action-routing pass.
- Legacy Gardener HP and combat timing remain in the existing slice state; its loadout remains in the existing equipment owner. The inspector reads those owners. New agent health, level, conditions and equipment are persisted data; general NPC damage, levelling and condition-effect rules are not added here.
- A conversation-sprite reference is authorable metadata. New portrait/dialogue content and portrait dispatch are not implemented by this pass.
- Object Open/Closed pose is persisted authoring state. ObjectMarker is a static placeholder; a container-open reducer, loot menu, pose animation and blocking footprints are later capabilities. Passive objects are not falsely offered as conversations and do not lose energy while time advances.
- New Gardener/Mara presentation bindings and scale resolve through existing approved art. Adding another art resource requires registration in the existing presentation resource owner; this pass does not introduce arbitrary file loading.

The next architectural pass should move controlled-agent selection and the remaining player-specific action state behind the common agent boundary. The authoring declarations can remain unchanged while that migration proceeds.
