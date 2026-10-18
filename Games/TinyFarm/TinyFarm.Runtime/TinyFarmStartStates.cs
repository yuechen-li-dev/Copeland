using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;

namespace TinyFarm.Core;

// The opening's start state is built in steps: each milestone builder starts from the previous one (M14, then
// M16 to M21). The milestone scenarios that once proved each step have been removed; these builders remain because
// TinyFarmSupperStart and, through it, the Sleeping Spring opening start from M21. Their names are kept so that
// recorded hashes and tests still line up.

public static class TinyFarmM14ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, string phase)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 480;
        SceneAnchorDefinition playerStart = definitions.Scenes.GetAnchor(new SceneAnchorId("farm.start"));
        SetPlacement(state, TinyFarmIds.Player, playerStart.Scene, playerStart.Position);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);

        switch (phase.ToLowerInvariant())
        {
            case "wander":
                SetEnergy(state, TinyFarmIds.Elias, 9_000, false);
                return state;
            case "low-energy":
                SetEnergy(state, TinyFarmIds.Elias, 1_000, false);
                return state;
            default:
                throw new FormatException("Unknown M14 scenario phase. Use wander or low-energy.");
        }
    }

    private static void SetEnergy(TinyFarmState state, ActorId actor, int energy, bool resting)
    {
        int index = state.MutableActorEnergy.FindIndex(item => item.Actor == actor);
        state.MutableActorEnergy[index] = new ActorEnergyState(actor, energy, resting);
    }

    private static void SetPlacement(TinyFarmState state, ActorId actor, SceneId scene, ScenePosition position)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[index] = new ActorSceneState(actor, scene, position);
    }

    private static void SetLocation(TinyFarmState state, ActorId actor, LocationId location)
    {
        int index = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[index] = state.MutableActors[index] with { Location = location };
    }
}

public static class TinyFarmM16ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM14ControlStates.Create(definitions, "wander");
        return new TinyFarmState(
            TinyFarmState.PlayerUiSaveVersion,
            baseline.Minute,
            baseline.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            [
                new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 3),
                new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 2)
            ],
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            baseline.ActorScenes.ToList(),
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1);
    }
}

public static class TinyFarmM17ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM16ControlStates.Create(definitions);
        ItemState[] items = baseline.Items
            .Select(item => item.Id == TinyFarmIds.WildMint
                ? item with
                {
                    GroundLocation = TinyFarmIds.Farmhouse,
                    Owner = null,
                    GroundScene = TinyFarmSceneIds.Farm,
                    GroundPosition = new ScenePosition(
                        7 * ScenePosition.UnitsPerTile,
                        5 * ScenePosition.UnitsPerTile + ScenePosition.UnitsPerTile / 2)
                }
                : item)
            .ToArray();
        ActorSceneState[] placements = baseline.ActorScenes
            .Select(placement => placement.Actor == TinyFarmIds.Player
                ? placement with { Facing = ActorFacing.Right }
                : placement)
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.ItemActionSaveVersion,
            baseline.Minute,
            baseline.Actors.Select(actor => actor with { Inventory = actor.Inventory.ToList() }).ToList(),
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1);
    }
}

public static class TinyFarmM18ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM17ControlStates.Create(definitions);
        ForageNodeDefinition node = definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods);
        ActorState[] actors = baseline.Actors
            .Select(actor => actor.Id == TinyFarmIds.Player
                ? actor with { Location = TinyFarmIds.Riverside, Inventory = actor.Inventory.ToList() }
                : actor with { Inventory = actor.Inventory.ToList() })
            .ToArray();
        ActorSceneState[] placements = baseline.ActorScenes
            .Select(placement => placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = node.Scene,
                    WorldPosition = new ScenePosition(
                        node.Position.XUnits - ScenePosition.UnitsPerTile,
                        node.Position.YUnits),
                    Facing = ActorFacing.Right
                }
                : placement)
            .ToArray();
        ForageNodeState[] forageNodes = definitions.ForageNodes
            .Select(definition => new ForageNodeState(
                definition.Id,
                ForageNodeAvailability.Available))
            .ToArray();

        return new TinyFarmState(
            TinyFarmState.ForageSaveVersion,
            baseline.Minute,
            actors,
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            baseline.DefinitionSetId,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            baseline.SelectedHotbarSlot,
            forageNodes);
    }
}

public static class TinyFarmM19ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions, bool hasIngredient = true)
    {
        TinyFarmState baseline = TinyFarmM18ControlStates.Create(definitions);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with { Location = TinyFarmIds.Farmhouse, Inventory = actor.Inventory.ToList() }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.Residence,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(5, 4)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        List<InventoryStack> inventory = baseline.InventoryStacks
            .Where(stack => stack.Actor != TinyFarmIds.Player
                || stack.Product != TinyFarmIds.HenOfTheWoods
                && stack.Product != TinyFarmIds.SauteedHenOfTheWoods)
            .ToList();
        if (hasIngredient)
        {
            inventory.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.HenOfTheWoods, 1));
        }
        return new TinyFarmState(
            TinyFarmState.ForageSaveVersion,
            baseline.Minute,
            actors,
            baseline.Items.ToList(),
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            inventory,
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            baseline.SelectedHotbarSlot,
            baseline.ForageNodes.ToList());
    }
}

public static class TinyFarmM20ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM19ControlStates.Create(definitions, hasIngredient: false);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with
            {
                Location = TinyFarmIds.Farmhouse,
                Inventory = actor.Inventory.Append(TinyFarmIds.Axe).Distinct().ToList()
            }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.Farm,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(10, 5)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        ItemState[] items = baseline.Items
            .Where(item => item.Id != TinyFarmIds.Axe)
            .Append(new ItemState(
                TinyFarmIds.Axe,
                "Axe",
                0,
                GroundLocation: null,
                Owner: TinyFarmIds.Player))
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.WoodcuttingSaveVersion,
            baseline.Minute,
            actors,
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            baseline.InventoryStacks
                .Where(stack => stack.Product != TinyFarmIds.Wood)
                .ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 1,
            baseline.ForageNodes.ToList(),
            definitions.Trees.Select(tree => new TreeState(tree.Id, TreeAvailability.Standing)).ToArray());
    }
}

public static class TinyFarmM21ControlStates
{
    public static TinyFarmState Create(TinyFarmDefinitions definitions)
    {
        TinyFarmState baseline = TinyFarmM20ControlStates.Create(definitions);
        ActorState[] actors = baseline.Actors.Select(actor => actor.Id == TinyFarmIds.Player
            ? actor with
            {
                Location = TinyFarmIds.TownSquare,
                Inventory = actor.Inventory.Append(TinyFarmIds.Sword).Distinct().ToList()
            }
            : actor with { Inventory = actor.Inventory.ToList() }).ToArray();
        ActorSceneState[] placements = baseline.ActorScenes.Select(placement =>
            placement.Actor == TinyFarmIds.Player
                ? placement with
                {
                    Scene = TinyFarmSceneIds.DungeonEntrance,
                    WorldPosition = ScenePosition.FromGrid(new GridPosition(7, 5)),
                    Facing = ActorFacing.Right
                }
                : placement).ToArray();
        ItemState[] items = baseline.Items
            .Where(item => item.Id != TinyFarmIds.Sword)
            .Append(new ItemState(
                TinyFarmIds.Sword,
                "Sword",
                0,
                GroundLocation: null,
                Owner: TinyFarmIds.Player))
            .ToArray();
        return new TinyFarmState(
            TinyFarmState.DungeonCombatSaveVersion,
            baseline.Minute,
            actors,
            items,
            baseline.Facts.ToList(),
            baseline.Favor,
            definitions.Identity,
            baseline.InventoryStacks.ToList(),
            baseline.ShopStock.ToList(),
            baseline.FarmPlots.ToList(),
            placements,
            baseline.ActorEnergy.ToList(),
            selectedHotbarSlot: 3,
            baseline.ForageNodes.ToList(),
            baseline.Trees.ToList(),
            definitions.Enemies.Select(enemy => new EnemyState(enemy.Id, enemy.MaxHealth)).ToArray());
    }
}
