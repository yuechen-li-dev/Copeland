namespace TinyFarm.Core;

public sealed class TinyFarmResolver
{
    private readonly TinyFarmDefinitions? definitions;

    public TinyFarmResolver(TinyFarmDefinitions? definitions = null)
    {
        this.definitions = definitions;
    }

    public ResolutionBatchResult Resolve(
        TinyFarmState initialState,
        IEnumerable<IntentEnvelope> submittedIntents)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(submittedIntents);

        TinyFarmState state = initialState.DeepCopy();
        var results = new List<IntentResult>();

        IEnumerable<IntentEnvelope> ordered = submittedIntents
            .OrderBy(envelope => envelope.SubmittedAt)
            .ThenBy(envelope => envelope.Sequence)
            .ThenBy(envelope => envelope.Actor.Value, StringComparer.Ordinal);

        foreach (IntentEnvelope envelope in ordered)
        {
            results.Add(ResolveOne(state, envelope));
        }

        return new ResolutionBatchResult(state, results);
    }

    private IntentResult ResolveOne(TinyFarmState state, IntentEnvelope envelope)
    {
        ActorState? actor = FindActor(state, envelope.Actor);
        if (actor is null)
        {
            return Rejected(envelope, IntentReason.UnknownActor);
        }

        return envelope.Intent switch
        {
            MoveIntent move => ResolveMove(state, actor, envelope, move),
            SpatialMoveIntent move => ResolveSpatialMove(state, actor, envelope, move),
            InteractIntent => ResolveInteract(state, actor, envelope),
            LookIntent => Accepted(envelope, new GameEvent(GameEventKind.Looked, actor.Id, Location: actor.Location)),
            TalkIntent talk => ResolveTalk(state, actor, envelope, talk),
            TakeIntent take => ResolveTake(state, actor, envelope, take),
            GiveIntent give => ResolveGive(state, actor, envelope, give),
            BuyIntent buy => ResolveBuy(state, actor, envelope, buy),
            SellIntent sell => ResolveSell(state, actor, envelope, sell),
            BuyProductIntent buy => ResolveBuyProduct(state, actor, envelope, buy),
            SellProductIntent sell => ResolveSellProduct(state, actor, envelope, sell),
            PlantIntent plant => ResolvePlant(state, actor, envelope, plant),
            WaterIntent water => ResolveWater(state, actor, envelope, water),
            HarvestIntent harvest => ResolveHarvest(state, actor, envelope, harvest),
            WaitIntent wait => ResolveWait(state, actor, envelope, wait),
            _ => throw new InvalidOperationException($"Unsupported intent type {envelope.Intent.GetType().Name}.")
        };
    }

    private static IntentResult ResolveMove(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        MoveIntent intent)
    {
        if (!TinyFarmContent.Locations.Any(location => location.Id == intent.Destination))
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (actor.Location == intent.Destination)
        {
            return NoOp(envelope, IntentReason.AlreadyThere);
        }

        LocationDefinition current = TinyFarmContent.Location(actor.Location);
        if (!current.Exits.Contains(intent.Destination))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        ReplaceActor(state, actor with { Location = intent.Destination });
        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            MoveActorToScheduledScene(state, actor.Id, intent.Destination);
        }
        return Accepted(
            envelope,
            new GameEvent(GameEventKind.ActorMoved, actor.Id, Location: intent.Destination));
    }

    private static IntentResult ResolveSpatialMove(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        SpatialMoveIntent intent)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion
            || Math.Abs(intent.DeltaX) + Math.Abs(intent.DeltaY) != 1
            || intent.Distance <= 0
            || intent.Distance > 1024)
        {
            return Rejected(envelope, IntentReason.InvalidMovement);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        SceneDefinition scene = TinyFarmScenes.Get(placement.Scene);
        GridPosition target = placement.Position;
        for (int step = 0; step < intent.Distance; step++)
        {
            target = new GridPosition(target.X + intent.DeltaX, target.Y + intent.DeltaY);
            if (!TinyFarmScenes.IsInBounds(scene, target) || TinyFarmScenes.IsBlocked(scene, target))
            {
                return Rejected(envelope, IntentReason.MovementBlocked);
            }
        }

        ReplaceActorScene(state, placement with { Position = target });
        return Accepted(
            envelope,
            new GameEvent(GameEventKind.ActorMoved, actor.Id, Location: actor.Location, Scene: placement.Scene));
    }

    private static IntentResult ResolveInteract(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return Rejected(envelope, IntentReason.NoInteraction);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        SceneDefinition scene = TinyFarmScenes.Get(placement.Scene);
        SceneRoute? route = scene.Routes
            .Where(candidate => scene.Placement(candidate.TriggerObject).Contains(placement.Position))
            .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (route is null)
        {
            return Rejected(envelope, IntentReason.NoInteraction);
        }

        SceneDefinition target = TinyFarmScenes.Get(route.TargetScene);
        GridPosition spawn = target.Spawn(route.TargetSpawn).Position;
        ReplaceActorScene(state, placement with { Scene = target.Id, Position = spawn });
        ReplaceActor(state, actor with { Location = TinyFarmScenes.LocationForScene(target.Id) });
        return Accepted(
            envelope,
            [
                new GameEvent(GameEventKind.SceneExited, actor.Id, Location: actor.Location, Scene: scene.Id, Route: route.Id),
                new GameEvent(
                    GameEventKind.SceneEntered,
                    actor.Id,
                    Location: TinyFarmScenes.LocationForScene(target.Id),
                    Scene: target.Id,
                    Route: route.Id)
            ]);
    }

    private static IntentResult ResolveTalk(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        TalkIntent intent)
    {
        ActorState? target = FindActor(state, intent.Target);
        if (target is null)
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (target.Location != actor.Location || !ActorsAreNearWhenSpatial(state, actor.Id, target.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        var events = new List<GameEvent>();
        DialogueTopic topic = intent.Target == TinyFarmIds.Sela
            ? DialogueTopic.ShopGreeting
            : DialogueTopic.Greeting;

        if (intent.Target == TinyFarmIds.Mara && state.Favor == FavorStage.NotStarted)
        {
            TransferItem(state, TinyFarmIds.Letter, target.Id, actor.Id);
            state.Favor = FavorStage.LetterReceived;
            AddFact(state, WorldFact.MaraNeedsDelivery);
            topic = DialogueTopic.RequestLetterDelivery;
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                TinyFarmIds.Letter,
                Favor: FavorStage.LetterReceived));
        }
        else if (intent.Target == TinyFarmIds.Mara && state.Favor == FavorStage.LetterDelivered)
        {
            ReplaceActor(state, actor with { Money = actor.Money + 3 });
            state.Favor = FavorStage.Complete;
            AddFact(state, WorldFact.MaraThankedPlayer);
            topic = DialogueTopic.FavorThanks;
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                Amount: 3,
                Favor: FavorStage.Complete));
        }

        events.Insert(0, new GameEvent(
            GameEventKind.Conversation,
            actor.Id,
            target.Id,
            Dialogue: topic));
        return Accepted(envelope, events);
    }

    private static IntentResult ResolveTake(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        TakeIntent intent)
    {
        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner is not null || item.GroundLocation != actor.Location)
        {
            return Rejected(envelope, IntentReason.ItemAbsent);
        }

        ReplaceItem(state, item with { GroundLocation = null, Owner = actor.Id });
        AddInventoryItem(state, actor.Id, item.Id);
        return Accepted(envelope, new GameEvent(GameEventKind.ItemTaken, actor.Id, Item: item.Id));
    }

    private static IntentResult ResolveGive(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        GiveIntent intent)
    {
        ActorState? target = FindActor(state, intent.Target);
        if (target is null)
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (target.Location != actor.Location || !ActorsAreNearWhenSpatial(state, actor.Id, target.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != actor.Id || !actor.Inventory.Contains(item.Id))
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        TransferItem(state, item.Id, actor.Id, target.Id);
        var events = new List<GameEvent>
        {
            new(GameEventKind.ItemGiven, actor.Id, target.Id, item.Id)
        };

        if (item.Id == TinyFarmIds.Letter && target.Id == TinyFarmIds.Elias && state.Favor == FavorStage.LetterReceived)
        {
            state.Favor = FavorStage.LetterDelivered;
            AddFact(state, WorldFact.EliasHasLetter);
            events.Add(new GameEvent(
                GameEventKind.Conversation,
                actor.Id,
                target.Id,
                item.Id,
                Dialogue: DialogueTopic.EliasReceivesLetter));
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                item.Id,
                Favor: FavorStage.LetterDelivered));
        }

        return Accepted(envelope, events);
    }

    private static IntentResult ResolveBuy(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        BuyIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location || !ActorsAreNearWhenSpatial(state, actor.Id, shopkeeper.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != shopkeeper.Id)
        {
            return Rejected(envelope, IntentReason.NotForSale);
        }

        if (actor.Money < item.Price)
        {
            return Rejected(envelope, IntentReason.InsufficientFunds);
        }

        ReplaceActor(state, actor with { Money = actor.Money - item.Price });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money + item.Price });
        TransferItem(state, item.Id, shopkeeper.Id, actor.Id);
        return Accepted(envelope, new GameEvent(
            GameEventKind.ItemBought,
            actor.Id,
            shopkeeper.Id,
            item.Id,
            Amount: item.Price));
    }

    private static IntentResult ResolveSell(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        SellIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location || !ActorsAreNearWhenSpatial(state, actor.Id, shopkeeper.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != actor.Id || !actor.Inventory.Contains(item.Id))
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        int value = Math.Max(1, item.Price / 2);
        ReplaceActor(state, actor with { Money = actor.Money + value });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money - value });
        TransferItem(state, item.Id, actor.Id, shopkeeper.Id);
        return Accepted(envelope, new GameEvent(
            GameEventKind.ItemSold,
            actor.Id,
            shopkeeper.Id,
            item.Id,
            Amount: value));
    }

    private IntentResult ResolveWait(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        WaitIntent intent)
    {
        if (intent.Minutes <= 0 || intent.Minutes > 240)
        {
            return Rejected(envelope, IntentReason.InvalidWait);
        }

        var events = new List<GameEvent>
        {
            new(GameEventKind.TimeAdvanced, actor.Id, Amount: intent.Minutes)
        };
        int previousDay = state.Day;
        state.Minute += intent.Minutes;
        for (int day = previousDay + 1; day <= state.Day; day++)
        {
            AdvanceDay(state, actor.Id, day, events);
        }

        return Accepted(envelope, events);
    }

    private IntentResult ResolveBuyProduct(TinyFarmState state, ActorState actor, IntentEnvelope envelope, BuyProductIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location || !ActorsAreNearWhenSpatial(state, actor.Id, shopkeeper.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemDefinition? item = definitions?.Items.SingleOrDefault(candidate => candidate.Id == intent.Product);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        ShopStock? stock = state.ShopStock.SingleOrDefault(candidate => candidate.Product == intent.Product);
        if (stock is null || stock.Count <= 0)
        {
            return Rejected(envelope, IntentReason.StockUnavailable);
        }

        if (actor.Money < item.BuyPrice)
        {
            return Rejected(envelope, IntentReason.InsufficientFunds);
        }

        ReplaceActor(state, actor with { Money = actor.Money - item.BuyPrice });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money + item.BuyPrice });
        SetProductCount(state, actor.Id, item.Id, state.ProductCount(actor.Id, item.Id) + 1);
        ReplaceStock(state, stock with { Count = stock.Count - 1 });
        return Accepted(envelope, new GameEvent(GameEventKind.ItemBought, actor.Id, shopkeeper.Id, Amount: item.BuyPrice, Product: item.Id));
    }

    private IntentResult ResolveSellProduct(TinyFarmState state, ActorState actor, IntentEnvelope envelope, SellProductIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location || !ActorsAreNearWhenSpatial(state, actor.Id, shopkeeper.Id))
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemDefinition? item = definitions?.Items.SingleOrDefault(candidate => candidate.Id == intent.Product);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (state.ProductCount(actor.Id, item.Id) <= 0)
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        ReplaceActor(state, actor with { Money = actor.Money + item.SellPrice });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money - item.SellPrice });
        SetProductCount(state, actor.Id, item.Id, state.ProductCount(actor.Id, item.Id) - 1);
        AddFact(state, WorldFact.FirstCropSold);
        return Accepted(envelope, new GameEvent(GameEventKind.ItemSold, actor.Id, shopkeeper.Id, Amount: item.SellPrice, Product: item.Id));
    }

    private IntentResult ResolvePlant(TinyFarmState state, ActorState actor, IntentEnvelope envelope, PlantIntent intent)
    {
        FarmPlotState? plot = state.FarmPlots.SingleOrDefault(candidate => candidate.Id == intent.Plot);
        if (plot is null)
        {
            return Rejected(envelope, IntentReason.UnknownPlot);
        }

        CropDefinition? crop = definitions?.Crops.SingleOrDefault(candidate => candidate.Id == intent.Crop);
        if (crop is null)
        {
            return Rejected(envelope, IntentReason.UnknownCrop);
        }

        if (actor.Location != plot.Location)
        {
            return Rejected(envelope, IntentReason.WrongLocation);
        }

        if (!ActorIsAdjacentToPlotWhenSpatial(state, actor.Id, plot.Id))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        if (plot.Crop is not null)
        {
            return Rejected(envelope, IntentReason.PlotOccupied);
        }

        int seedCount = state.ProductCount(actor.Id, crop.SeedItemId);
        if (seedCount <= 0)
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        SetProductCount(state, actor.Id, crop.SeedItemId, seedCount - 1);
        ReplacePlot(state, plot with { Crop = crop.Id, PlantedDay = state.Day, GrowthStage = 0, WateredToday = false });
        return Accepted(envelope, new GameEvent(GameEventKind.CropPlanted, actor.Id, Crop: crop.Id, Plot: plot.Id));
    }

    private static IntentResult ResolveWater(TinyFarmState state, ActorState actor, IntentEnvelope envelope, WaterIntent intent)
    {
        FarmPlotState? plot = state.FarmPlots.SingleOrDefault(candidate => candidate.Id == intent.Plot);
        if (plot is null)
        {
            return Rejected(envelope, IntentReason.UnknownPlot);
        }

        if (actor.Location != plot.Location)
        {
            return Rejected(envelope, IntentReason.WrongLocation);
        }

        if (!ActorIsAdjacentToPlotWhenSpatial(state, actor.Id, plot.Id))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        if (plot.Crop is null)
        {
            return Rejected(envelope, IntentReason.PlotEmpty);
        }

        if (plot.WateredToday)
        {
            return NoOp(envelope, IntentReason.AlreadyWatered);
        }

        ReplacePlot(state, plot with { WateredToday = true });
        return Accepted(envelope, new GameEvent(GameEventKind.PlotWatered, actor.Id, Crop: plot.Crop, Plot: plot.Id));
    }

    private IntentResult ResolveHarvest(TinyFarmState state, ActorState actor, IntentEnvelope envelope, HarvestIntent intent)
    {
        FarmPlotState? plot = state.FarmPlots.SingleOrDefault(candidate => candidate.Id == intent.Plot);
        if (plot is null)
        {
            return Rejected(envelope, IntentReason.UnknownPlot);
        }

        if (actor.Location != plot.Location)
        {
            return Rejected(envelope, IntentReason.WrongLocation);
        }

        if (!ActorIsAdjacentToPlotWhenSpatial(state, actor.Id, plot.Id))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        if (plot.Crop is not CropId cropId)
        {
            return Rejected(envelope, IntentReason.PlotEmpty);
        }

        CropDefinition crop = definitions!.Crop(cropId);
        if (plot.GrowthStage < crop.GrowthDays)
        {
            return Rejected(envelope, IntentReason.CropImmature);
        }

        SetProductCount(state, actor.Id, crop.HarvestItemId, state.ProductCount(actor.Id, crop.HarvestItemId) + crop.Yield);
        ReplacePlot(state, plot with { Crop = null, PlantedDay = null, GrowthStage = 0, WateredToday = false });
        AddFact(state, WorldFact.FirstCropHarvested);
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.CropHarvested,
                actor.Id,
                Amount: crop.Yield,
                Dialogue: DialogueTopic.HarvestComment,
                Product: crop.HarvestItemId,
                Crop: crop.Id,
                Plot: plot.Id));
    }

    private void AdvanceDay(TinyFarmState state, ActorId actor, int day, List<GameEvent> events)
    {
        events.Add(new GameEvent(GameEventKind.DayStarted, actor, Day: day));
        if (day == 7)
        {
            events.Add(new GameEvent(GameEventKind.Conversation, TinyFarmIds.Mara, Dialogue: DialogueTopic.WeekComment, Day: day));
        }
        foreach (FarmPlotState plot in state.FarmPlots.OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal).ToArray())
        {
            if (plot.Crop is null)
            {
                continue;
            }

            CropDefinition crop = definitions!.Crop(plot.Crop.Value);
            int stage = plot.WateredToday ? Math.Min(crop.GrowthDays, plot.GrowthStage + 1) : plot.GrowthStage;
            ReplacePlot(state, plot with { GrowthStage = stage, WateredToday = false });
            if (stage != plot.GrowthStage)
            {
                events.Add(new GameEvent(GameEventKind.CropAdvanced, actor, Crop: crop.Id, Plot: plot.Id, Amount: stage, Day: day));
            }
        }

        foreach (ShopStock stock in state.ShopStock.ToArray())
        {
            ReplaceStock(state, stock with { Count = stock.DailyRestockCount });
            events.Add(new GameEvent(
                GameEventKind.ShopRestocked,
                TinyFarmIds.Sela,
                Amount: stock.DailyRestockCount,
                Product: stock.Product,
                Day: day));
        }
    }

    private static bool StoreIsOpen(int minute)
    {
        int minuteOfDay = minute % (24 * 60);
        return minuteOfDay >= 9 * 60 && minuteOfDay < 18 * 60;
    }

    private static ActorState? FindActor(TinyFarmState state, ActorId id)
    {
        return state.Actors.SingleOrDefault(actor => actor.Id == id);
    }

    private static ItemState? FindItem(TinyFarmState state, ItemId id)
    {
        return state.Items.SingleOrDefault(item => item.Id == id);
    }

    private static void ReplaceActor(TinyFarmState state, ActorState replacement)
    {
        int index = state.MutableActors.FindIndex(actor => actor.Id == replacement.Id);
        state.MutableActors[index] = replacement;
    }

    private static void ReplaceActorScene(TinyFarmState state, ActorSceneState replacement)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == replacement.Actor);
        state.MutableActorScenes[index] = replacement;
    }

    private static bool ActorsAreNearWhenSpatial(TinyFarmState state, ActorId first, ActorId second)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return true;
        }

        ActorSceneState left = state.ActorScene(first);
        ActorSceneState right = state.ActorScene(second);
        return left.Scene == right.Scene && left.Position.ManhattanDistance(right.Position) <= 1;
    }

    private static bool ActorIsAdjacentToPlotWhenSpatial(TinyFarmState state, ActorId actor, FarmPlotId plot)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return true;
        }

        ActorSceneState placement = state.ActorScene(actor);
        if (placement.Scene != TinyFarmSceneIds.Farm)
        {
            return false;
        }

        SceneDefinition scene = TinyFarmScenes.Get(TinyFarmSceneIds.Farm);
        SceneLayoutRow row = scene.Layout.Single(item =>
            scene.Object(item.ObjectId).Kind == SceneObjectKind.Plot
            && scene.Object(item.ObjectId).SemanticReference == plot.Value);
        return placement.Position.ManhattanDistance(new GridPosition(row.X, row.Y)) == 1;
    }

    private static void MoveActorToScheduledScene(TinyFarmState state, ActorId actor, LocationId destination)
    {
        ActorSceneState placement = state.ActorScene(actor);
        SceneId targetScene = TinyFarmScenes.SceneForLocation(destination);
        if (placement.Scene == targetScene)
        {
            return;
        }

        SceneDefinition target = TinyFarmScenes.Get(targetScene);
        GridPosition spawn = target.Spawns.First().Position;
        ReplaceActorScene(state, placement with { Scene = targetScene, Position = spawn });
    }

    private static void ReplaceItem(TinyFarmState state, ItemState replacement)
    {
        int index = state.MutableItems.FindIndex(item => item.Id == replacement.Id);
        state.MutableItems[index] = replacement;
    }

    private static void AddInventoryItem(TinyFarmState state, ActorId owner, ItemId item)
    {
        ActorState actor = state.Actor(owner);
        var inventory = actor.Inventory.ToList();
        inventory.Add(item);
        inventory.Sort((left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
        ReplaceActor(state, actor with { Inventory = inventory });
    }

    private static void TransferItem(
        TinyFarmState state,
        ItemId itemId,
        ActorId from,
        ActorId to)
    {
        ActorState source = state.Actor(from);
        ActorState target = state.Actor(to);
        var sourceInventory = source.Inventory.Where(item => item != itemId).ToList();
        var targetInventory = target.Inventory.Append(itemId)
            .OrderBy(item => item.Value, StringComparer.Ordinal)
            .ToList();

        ReplaceActor(state, source with { Inventory = sourceInventory });
        ReplaceActor(state, target with { Inventory = targetInventory });
        ReplaceItem(state, state.Item(itemId) with { Owner = to, GroundLocation = null });
    }

    private static void AddFact(TinyFarmState state, WorldFact fact)
    {
        if (!state.Facts.Contains(fact))
        {
            state.MutableFacts.Add(fact);
            state.MutableFacts.Sort();
        }
    }

    private static void SetProductCount(TinyFarmState state, ActorId actor, ProductId product, int count)
    {
        int index = state.MutableInventoryStacks.FindIndex(stack => stack.Actor == actor && stack.Product == product);
        if (count == 0)
        {
            if (index >= 0)
            {
                state.MutableInventoryStacks.RemoveAt(index);
            }
            return;
        }

        var replacement = new InventoryStack(actor, product, count);
        if (index >= 0)
        {
            state.MutableInventoryStacks[index] = replacement;
        }
        else
        {
            state.MutableInventoryStacks.Add(replacement);
        }
    }

    private static void ReplaceStock(TinyFarmState state, ShopStock replacement)
    {
        int index = state.MutableShopStock.FindIndex(stock => stock.Product == replacement.Product);
        state.MutableShopStock[index] = replacement;
    }

    private static void ReplacePlot(TinyFarmState state, FarmPlotState replacement)
    {
        int index = state.MutableFarmPlots.FindIndex(plot => plot.Id == replacement.Id);
        state.MutableFarmPlots[index] = replacement;
    }

    private static IntentResult Accepted(IntentEnvelope envelope, params GameEvent[] events)
    {
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private static IntentResult Accepted(IntentEnvelope envelope, IReadOnlyList<GameEvent> events)
    {
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private static IntentResult Rejected(IntentEnvelope envelope, IntentReason reason)
    {
        return new IntentResult(envelope, IntentResultStatus.Rejected, reason, []);
    }

    private static IntentResult NoOp(IntentEnvelope envelope, IntentReason reason)
    {
        return new IntentResult(envelope, IntentResultStatus.NoOp, reason, []);
    }
}
