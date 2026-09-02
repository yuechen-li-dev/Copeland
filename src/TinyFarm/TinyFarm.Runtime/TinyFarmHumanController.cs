namespace TinyFarm.Core;

public enum TinyFarmControl
{
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,
    Look,
    Talk,
    Take,
    Give,
    Buy,
    Sell,
    Plant,
    Water,
    Harvest,
    Interact,
    Wait
}

public static class TinyFarmHumanController
{
    public static GameIntent? Map(TinyFarmControl control, TinyFarmState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ActorState player = state.Actor(TinyFarmIds.Player);

        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            return MapSceneControl(control, state, player);
        }

        return control switch
        {
            TinyFarmControl.MoveLeft => MoveInDirection(player.Location, -1, 0),
            TinyFarmControl.MoveRight => MoveInDirection(player.Location, 1, 0),
            TinyFarmControl.MoveUp => MoveInDirection(player.Location, 0, -1),
            TinyFarmControl.MoveDown => MoveInDirection(player.Location, 0, 1),
            TinyFarmControl.Look => new LookIntent(),
            TinyFarmControl.Talk => FirstNearbyActor(state, player) is ActorState actor ? new TalkIntent(actor.Id) : null,
            TinyFarmControl.Take => FirstGroundItem(state, player) is ItemState item ? new TakeIntent(item.Id) : null,
            TinyFarmControl.Give => GiveFirstItem(state, player),
            TinyFarmControl.Buy => BuyAtStore(state, player),
            TinyFarmControl.Sell => SellAtStore(state, player),
            TinyFarmControl.Plant => FirstEmptyPlot(state, player) is FarmPlotState empty ? new PlantIntent(empty.Id, TinyFarmIds.TurnipCrop) : null,
            TinyFarmControl.Water => FirstPlantedPlot(state, player) is FarmPlotState planted ? new WaterIntent(planted.Id) : null,
            TinyFarmControl.Harvest => FirstPlantedPlot(state, player) is FarmPlotState crop ? new HarvestIntent(crop.Id) : null,
            TinyFarmControl.Wait => new WaitIntent(240),
            _ => null
        };
    }

    private static GameIntent? MapSceneControl(TinyFarmControl control, TinyFarmState state, ActorState player)
    {
        int movementDistance = state.Version >= TinyFarmState.ContinuousSceneSaveVersion
            ? ScenePosition.UnitsPerTile / 8
            : 1;
        return control switch
        {
            TinyFarmControl.MoveLeft => new SpatialMoveIntent(-1, 0, movementDistance),
            TinyFarmControl.MoveRight => new SpatialMoveIntent(1, 0, movementDistance),
            TinyFarmControl.MoveUp => new SpatialMoveIntent(0, -1, movementDistance),
            TinyFarmControl.MoveDown => new SpatialMoveIntent(0, 1, movementDistance),
            TinyFarmControl.Interact => new InteractIntent(),
            TinyFarmControl.Look => new LookIntent(),
            TinyFarmControl.Talk => FirstNearbyActor(state, player) is ActorState actor ? new TalkIntent(actor.Id) : null,
            TinyFarmControl.Take => FirstGroundItem(state, player) is ItemState item ? new TakeIntent(item.Id) : null,
            TinyFarmControl.Give => GiveFirstItem(state, player),
            TinyFarmControl.Buy => BuyAtStore(state, player),
            TinyFarmControl.Sell => SellAtStore(state, player),
            TinyFarmControl.Plant => FirstEmptyPlot(state, player) is FarmPlotState empty
                ? new PlantIntent(empty.Id, TinyFarmIds.TurnipCrop)
                : null,
            TinyFarmControl.Water => FirstPlantedPlot(state, player) is FarmPlotState planted
                ? new WaterIntent(planted.Id)
                : null,
            TinyFarmControl.Harvest => FirstPlantedPlot(state, player) is FarmPlotState crop
                ? new HarvestIntent(crop.Id)
                : null,
            TinyFarmControl.Wait => new WaitIntent(240),
            _ => null
        };
    }

    private static readonly IReadOnlyDictionary<LocationId, TinyFarmPoint> Positions =
        new Dictionary<LocationId, TinyFarmPoint>
        {
            [TinyFarmIds.Farmhouse] = new(160, 350),
            [TinyFarmIds.TownSquare] = new(430, 260),
            [TinyFarmIds.GeneralStore] = new(710, 140),
            [TinyFarmIds.Riverside] = new(710, 400)
        };

    private static GameIntent? MoveInDirection(LocationId origin, int dx, int dy)
    {
        TinyFarmPoint start = Positions[origin];
        LocationId? destination = TinyFarmContent.Location(origin).Exits
            .Select(exit => new
            {
                Id = exit,
                DeltaX = Positions[exit].X - start.X,
                DeltaY = Positions[exit].Y - start.Y
            })
            .Where(candidate => Math.Sign(candidate.DeltaX) == dx || Math.Sign(candidate.DeltaY) == dy)
            .OrderBy(candidate => Math.Abs(candidate.DeltaX) + Math.Abs(candidate.DeltaY))
            .ThenBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
            .Select(candidate => (LocationId?)candidate.Id)
            .FirstOrDefault();
        return destination is LocationId id ? new MoveIntent(id) : null;
    }

    private static ActorState? FirstNearbyActor(TinyFarmState state, ActorState player) => state.Actors
        .Where(actor => !actor.IsPlayer && actor.Location == player.Location)
        .Where(actor => AreNearWhenSpatial(state, player.Id, actor.Id))
        .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
        .FirstOrDefault(actor => state.Version < TinyFarmState.ContinuousSceneSaveVersion
            || TinyFarmSpatialQueries.SelectInteractionTarget(state, player.Id)?.Actor == actor.Id);

    private static ItemState? FirstGroundItem(TinyFarmState state, ActorState player) => state.Items
        .Where(item => item.GroundLocation == player.Location)
        .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
        .FirstOrDefault();

    private static GameIntent? GiveFirstItem(TinyFarmState state, ActorState player)
    {
        ActorState? target = FirstNearbyActor(state, player);
        ItemId? item = player.Inventory.OrderBy(id => id.Value, StringComparer.Ordinal).Cast<ItemId?>().FirstOrDefault();
        return target is not null && item is ItemId id ? new GiveIntent(id, target.Id) : null;
    }

    private static GameIntent? BuyAtStore(TinyFarmState state, ActorState player)
    {
        if (player.Location != TinyFarmIds.GeneralStore || !IsNearShopkeeperWhenSpatial(state, player.Id))
        {
            return null;
        }

        ShopStock? seeds = state.ShopStock.SingleOrDefault(stock => stock.Product == TinyFarmIds.TurnipSeed);
        return seeds is not null && seeds.Count > 0
            ? new BuyProductIntent(TinyFarmIds.TurnipSeed)
            : state.Item(TinyFarmIds.Apple).Owner == TinyFarmIds.Sela
                ? new BuyIntent(TinyFarmIds.Apple)
                : null;
    }

    private static GameIntent? SellAtStore(TinyFarmState state, ActorState player)
    {
        if (player.Location != TinyFarmIds.GeneralStore || !IsNearShopkeeperWhenSpatial(state, player.Id))
        {
            return null;
        }

        if (state.ProductCount(player.Id, TinyFarmIds.Turnip) > 0)
        {
            return new SellProductIntent(TinyFarmIds.Turnip);
        }

        ItemId? item = player.Inventory.OrderBy(id => id.Value, StringComparer.Ordinal).Cast<ItemId?>().FirstOrDefault();
        return item is ItemId id ? new SellIntent(id) : null;
    }

    private static FarmPlotState? FirstEmptyPlot(TinyFarmState state, ActorState player) => state.FarmPlots
        .Where(plot => plot.Location == player.Location && plot.Crop is null)
        .Where(plot => IsAdjacentToPlotWhenSpatial(state, player.Id, plot.Id))
        .OrderBy(plot => plot.Id.Value, StringComparer.Ordinal)
        .FirstOrDefault();

    private static FarmPlotState? FirstPlantedPlot(TinyFarmState state, ActorState player) => state.FarmPlots
        .Where(plot => plot.Location == player.Location && plot.Crop is not null)
        .Where(plot => IsAdjacentToPlotWhenSpatial(state, player.Id, plot.Id))
        .OrderBy(plot => plot.Id.Value, StringComparer.Ordinal)
        .FirstOrDefault();

    private static bool AreNearWhenSpatial(TinyFarmState state, ActorId first, ActorId second)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return true;
        }

        ActorSceneState left = state.ActorScene(first);
        ActorSceneState right = state.ActorScene(second);
        return left.Scene == right.Scene && left.Position.ManhattanDistance(right.Position) <= 1;
    }

    private static bool IsNearShopkeeperWhenSpatial(TinyFarmState state, ActorId actor)
    {
        if (state.Version >= TinyFarmState.ContinuousSceneSaveVersion)
        {
            return TinyFarmSpatialQueries.SelectInteractionTarget(state, actor)?.Actor == TinyFarmIds.Sela;
        }
        return state.Version < TinyFarmState.SceneSaveVersion
            || AreNearWhenSpatial(state, actor, TinyFarmIds.Sela);
    }

    private static bool IsAdjacentToPlotWhenSpatial(TinyFarmState state, ActorId actor, FarmPlotId plot)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return true;
        }

        ActorSceneState placement = state.ActorScene(actor);
        SceneDefinition farm = TinyFarmScenes.Get(TinyFarmSceneIds.Farm);
        SceneLayoutRow row = farm.Layout.Single(layout =>
            farm.Object(layout.ObjectId).SemanticReference == plot.Value);
        if (state.Version >= TinyFarmState.ContinuousSceneSaveVersion)
        {
            return TinyFarmSpatialQueries.SelectInteractionTarget(state, actor)?.Plot == plot;
        }
        return placement.Scene == TinyFarmSceneIds.Farm
            && placement.Position.ManhattanDistance(new GridPosition(row.X, row.Y)) == 1;
    }
}
