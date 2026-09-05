using Aurelian.Spatial2D;

namespace TinyFarm.Core;

internal readonly record struct SpatialMoveReductionResult(
    IntentResultStatus Status,
    IntentReason Reason,
    ActorId Actor,
    LocationId Location,
    ActorSceneState? PreviousPlacement,
    ActorSceneState? CurrentPlacement)
{
    public SceneId? Scene => CurrentPlacement?.Scene;

    public static SpatialMoveReductionResult Accepted(
        ActorId actor,
        LocationId location,
        ActorSceneState previousPlacement,
        ActorSceneState currentPlacement)
    {
        return new SpatialMoveReductionResult(
            IntentResultStatus.Accepted,
            IntentReason.None,
            actor,
            location,
            previousPlacement,
            currentPlacement);
    }

    public static SpatialMoveReductionResult Rejected(IntentReason reason)
    {
        return new SpatialMoveReductionResult(
            IntentResultStatus.Rejected,
            reason,
            default,
            default,
            null,
            null);
    }
}

public sealed class TinyFarmResolver
{
    private readonly TinyFarmDefinitions? definitions;
    private readonly IReadOnlyDictionary<SceneId, SpatialWorld2D> spatialWorlds;
    private TinyFarmSceneCatalog Scenes => definitions?.Scenes
        ?? throw new InvalidOperationException("Scene intents require loaded TinyFarm definitions.");

    public TinyFarmResolver(TinyFarmDefinitions? definitions = null)
    {
        this.definitions = definitions;
        spatialWorlds = definitions?.Scenes.All.ToDictionary(
            scene => scene.Id,
            TinyFarmSpatialWorldAdapter.BuildStaticWorld)
            ?? new Dictionary<SceneId, SpatialWorld2D>();
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
            NavigateToAnchorIntent move => ResolveAnchorTravel(state, actor, envelope, move),
            AnchorReachedIntent reached => ResolveAnchorReached(state, actor, envelope, reached),
            AnchorNavigationFailedIntent => Rejected(envelope, IntentReason.AnchorUnreachable),
            SpatialMoveIntent move => ResolveSpatialMove(state, actor, envelope, move),
            InteractIntent interact => ResolveInteract(state, actor, envelope, interact),
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
            GatherIntent gather => ResolveGather(state, actor, envelope, gather),
            CookIntent cook => ResolveCook(state, actor, envelope, cook),
            ChopIntent chop => ResolveChop(state, actor, envelope, chop),
            AttackIntent attack => ResolveAttack(state, actor, envelope, attack),
            SelectHotbarSlotIntent select => ResolveSelectHotbarSlot(state, actor, envelope, select),
            UseSelectedIntent => ResolveUseSelected(state, actor, envelope),
            WaitIntent wait => ResolveWait(state, actor, envelope, wait),
            _ => throw new InvalidOperationException($"Unsupported intent type {envelope.Intent.GetType().Name}.")
        };
    }

    private static IntentResult ResolveSelectHotbarSlot(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        SelectHotbarSlotIntent intent)
    {
        if (!actor.IsPlayer || state.Version < TinyFarmState.PlayerUiSaveVersion)
        {
            return Rejected(envelope, IntentReason.InvalidHotbarSlot);
        }

        state.SelectedHotbarSlot = intent.Slot.Value;
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.HotbarSlotSelected,
                actor.Id,
                Amount: intent.Slot.Value));
    }

    private IntentResult ResolveUseSelected(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope)
    {
        if (!actor.IsPlayer || state.Version < TinyFarmState.PlayerUiSaveVersion)
        {
            return Rejected(envelope, IntentReason.NoSelectedBinding);
        }

        HotbarSlot slot = TinyFarmHotbar.DefaultSlots.Single(candidate =>
            candidate.Id.Value == state.SelectedHotbarSlot);
        if (slot.Binding is null)
        {
            return Rejected(envelope, IntentReason.NoSelectedBinding);
        }

        if (slot.Binding is ItemHotbarBinding itemBinding)
        {
            if (!OwnsItem(state, actor, itemBinding.Item))
            {
                return Rejected(envelope, IntentReason.SelectedBindingUnavailable);
            }
            InteractionTarget? itemTarget = TinyFarmSpatialQueries.SelectInteractionTarget(
                state,
                actor.Id,
                Scenes);
            if (itemTarget is null)
            {
                return Rejected(envelope, IntentReason.NoInteractionTarget);
            }
            if (itemBinding.Item == TinyFarmIds.Axe)
            {
                if (itemTarget.Tree is not TreeId tree)
                {
                    return Rejected(
                        envelope,
                        itemTarget.Enemy is not null ? IntentReason.WrongWeapon : IntentReason.WrongTargetKind);
                }
                return ResolveChop(state, actor, envelope, new ChopIntent(tree));
            }
            if (itemBinding.Item == TinyFarmIds.Sword)
            {
                if (itemTarget.Enemy is not EnemyId enemy)
                {
                    return Rejected(envelope, IntentReason.WrongTargetKind);
                }
                return ResolveAttack(state, actor, envelope, new AttackIntent(enemy));
            }
            return Rejected(envelope, IntentReason.UnsupportedSelectedUse);
        }

        if (slot.Binding is not ProductHotbarBinding product)
        {
            return Rejected(envelope, IntentReason.UnsupportedSelectedUse);
        }

        if (state.ProductCount(actor.Id, product.Product) <= 0)
        {
            return Rejected(envelope, IntentReason.SelectedBindingUnavailable);
        }

        if (product.Product != TinyFarmIds.TurnipSeed)
        {
            return Rejected(envelope, IntentReason.UnsupportedSelectedUse);
        }

        InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(
            state,
            actor.Id,
            Scenes);
        if (target is null)
        {
            return Rejected(envelope, IntentReason.NoInteractionTarget);
        }

        if (target.Plot is not FarmPlotId plot)
        {
            if (target.Tree is not null || target.Enemy is not null)
            {
                return Rejected(envelope, IntentReason.WrongTool);
            }
            return Rejected(envelope, IntentReason.WrongTargetKind);
        }

        return ResolvePlant(
            state,
            actor,
            envelope,
            new PlantIntent(plot, TinyFarmIds.TurnipCrop));
    }

    private IntentResult ResolveMove(
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

    private IntentResult ResolveAnchorTravel(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        NavigateToAnchorIntent intent)
    {
        if (definitions is null
            || !Scenes.TryGetAnchor(intent.Anchor, out SceneAnchorDefinition anchor))
        {
            return Rejected(envelope, IntentReason.MissingAnchor);
        }
        if (anchor.SemanticLocation is not LocationId resolvedDestination)
        {
            return Rejected(envelope, IntentReason.InvalidAnchorRealization);
        }
        if (state.Version >= TinyFarmState.EnergySaveVersion
            && !TinyFarmAnchorIds.IsHomeBed(intent.Anchor))
        {
            SetResting(state, actor.Id, false);
        }
        if (actor.Location == resolvedDestination)
        {
            if (state.Version >= TinyFarmState.EnergySaveVersion)
            {
                ActorSceneState currentPlacement = state.ActorScene(actor.Id);
                if (currentPlacement.Scene != anchor.Scene
                    || currentPlacement.WorldPosition.SquaredDistance(anchor.Position) > (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits)
                {
                    ReplaceActorScene(state, currentPlacement with
                    {
                        Scene = anchor.Scene,
                        WorldPosition = anchor.Position,
                        Facing = anchor.Facing ?? currentPlacement.Facing
                    });
                    SetResting(state, actor.Id, TinyFarmAnchorIds.IsHomeBed(intent.Anchor));
                    return Accepted(envelope, new GameEvent(
                        GameEventKind.AnchorReached,
                        actor.Id,
                        Location: resolvedDestination,
                        Scene: anchor.Scene,
                        SceneObject: anchor.SemanticObject,
                        Anchor: anchor.Id));
                }
            }
            return NoOp(envelope, IntentReason.AlreadyThere);
        }

        LocationId next = NextCoarseLocation(actor.Location, resolvedDestination);
        if (next == actor.Location)
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        ReplaceActor(state, actor with { Location = next });
        if (state.Version >= TinyFarmState.SceneSaveVersion)
        {
            SceneAnchorDefinition realization = Scenes.CoarseEntryAnchor(
                TinyFarmScenes.SceneForLocation(next));
            ActorSceneState placement = state.ActorScene(actor.Id);
            ReplaceActorScene(state, placement with
            {
                Scene = realization.Scene,
                WorldPosition = realization.Position,
                Facing = realization.Facing ?? placement.Facing
            });
        }
        return Accepted(
            envelope,
            new GameEvent(GameEventKind.ActorMoved, actor.Id, Location: next, Anchor: intent.Anchor));
    }

    private IntentResult ResolveAnchorReached(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        AnchorReachedIntent intent)
    {
        if (!Scenes.TryGetAnchor(intent.Anchor, out SceneAnchorDefinition anchor))
        {
            return Rejected(envelope, IntentReason.MissingAnchor);
        }
        ActorSceneState placement = state.ActorScene(actor.Id);
        long radiusSquared = (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits;
        if (placement.Scene != anchor.Scene || placement.WorldPosition.SquaredDistance(anchor.Position) > radiusSquared)
        {
            return Rejected(envelope, IntentReason.InvalidAnchorRealization);
        }
        if (anchor.SemanticLocation is LocationId location && actor.Location != location)
        {
            ReplaceActor(state, actor with { Location = location });
        }
        if (anchor.Facing is ActorFacing facing && placement.Facing != facing)
        {
            ReplaceActorScene(state, placement with { Facing = facing });
        }
        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            SetResting(state, actor.Id, TinyFarmAnchorIds.IsHomeBed(intent.Anchor));
        }
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.AnchorReached,
                actor.Id,
                Location: anchor.SemanticLocation ?? actor.Location,
                Scene: anchor.Scene,
                SceneObject: anchor.SemanticObject,
                Anchor: anchor.Id));
    }

    private static LocationId NextCoarseLocation(LocationId current, LocationId destination)
    {
        if (current == destination)
        {
            return current;
        }
        if (TinyFarmContent.Location(current).Exits.Contains(destination))
        {
            return destination;
        }
        return TinyFarmIds.TownSquare;
    }

    private IntentResult ResolveSpatialMove(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        SpatialMoveIntent intent)
    {
        SpatialMoveReductionResult reduction = ResolveSpatialMoveCore(
            state,
            actor,
            intent.DeltaX,
            intent.DeltaY,
            intent.Distance);
        return MaterializeSpatialMoveResult(envelope, reduction);
    }

    internal SpatialMoveReductionResult ResolveSpatialMoveCore(
        TinyFarmState state,
        ActorId actorId,
        int deltaX,
        int deltaY,
        int distance)
    {
        if (!state.TryGetActorIndex(actorId, out int actorIndex))
        {
            return SpatialMoveReductionResult.Rejected(IntentReason.UnknownActor);
        }

        return ResolveSpatialMoveCore(
            state,
            state.MutableActors[actorIndex],
            deltaX,
            deltaY,
            distance);
    }

    private SpatialMoveReductionResult ResolveSpatialMoveCore(
        TinyFarmState state,
        ActorState actor,
        int deltaX,
        int deltaY,
        int distance)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion
            || Math.Abs(deltaX) + Math.Abs(deltaY) != 1
            || distance <= 0
            || distance > 1024)
        {
            return SpatialMoveReductionResult.Rejected(IntentReason.InvalidMovement);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        SceneDefinition scene = Scenes.Get(placement.Scene);
        ActorFacing facing = FacingFor(deltaX, deltaY);
        if (state.Version < TinyFarmState.ContinuousSceneSaveVersion)
        {
            GridPosition targetTile = placement.Position;
            for (int step = 0; step < distance; step++)
            {
                targetTile = new GridPosition(targetTile.X + deltaX, targetTile.Y + deltaY);
                if (!TinyFarmScenes.IsInBounds(scene, targetTile) || TinyFarmScenes.IsBlocked(scene, targetTile))
                {
                    return SpatialMoveReductionResult.Rejected(IntentReason.MovementBlocked);
                }
            }

            ActorSceneState replacement = placement with
            {
                WorldPosition = ScenePosition.FromGrid(targetTile),
                Facing = facing
            };
            ReplaceActorScene(state, replacement);
            if (state.Version >= TinyFarmState.EnergySaveVersion)
            {
                SetResting(state, actor.Id, false);
            }
            return SpatialMoveReductionResult.Accepted(
                actor.Id,
                actor.Location,
                placement,
                replacement);
        }

        ScenePosition target = new(
            checked(placement.WorldPosition.XUnits + (deltaX * distance)),
            checked(placement.WorldPosition.YUnits + (deltaY * distance)));
        if (!TinyFarmScenes.IsInBounds(scene, target))
        {
            return SpatialMoveReductionResult.Rejected(IntentReason.MovementBlocked);
        }
        SpatialHit2D? hit = spatialWorlds[scene.Id].Sweep(
            TinyFarmSpatialWorldAdapter.ActorPoint(placement.WorldPosition),
            new SpatialVector2D(deltaX * distance, deltaY * distance));
        if (hit is not null)
        {
            return SpatialMoveReductionResult.Rejected(IntentReason.MovementBlocked);
        }

        ActorSceneState replacementPlacement = placement with { WorldPosition = target, Facing = facing };
        ReplaceActorScene(state, replacementPlacement);
        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            SetResting(state, actor.Id, false);
        }
        return SpatialMoveReductionResult.Accepted(
            actor.Id,
            actor.Location,
            placement,
            replacementPlacement);
    }

    internal static IntentResult MaterializeSpatialMoveResult(
        IntentEnvelope envelope,
        SpatialMoveReductionResult reduction)
    {
        if (reduction.Status != IntentResultStatus.Accepted)
        {
            return new IntentResult(envelope, reduction.Status, reduction.Reason, []);
        }

        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.ActorMoved,
                reduction.Actor,
                Location: reduction.Location,
                Scene: reduction.Scene));
    }

    private IntentResult ResolveInteract(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        InteractIntent intent)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return Rejected(envelope, IntentReason.NoInteraction);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        SceneDefinition scene = Scenes.Get(placement.Scene);
        if (state.Version >= TinyFarmState.ContinuousSceneSaveVersion)
        {
            InteractionTarget? selected = intent.Target is SceneObjectId explicitTarget
                ? SelectExplicitObjectTarget(state, actor.Id, explicitTarget)
                : TinyFarmSpatialQueries.SelectInteractionTarget(state, actor.Id, Scenes);
            if (selected is null)
            {
                return Rejected(envelope, IntentReason.NoInteractionTarget);
            }

            if (selected.Item is ItemId item)
            {
                return ResolveTake(state, actor, envelope, new TakeIntent(item));
            }

            if (selected.ForageNode is ForageNodeId forageNode)
            {
                return ResolveGather(state, actor, envelope, new GatherIntent(forageNode));
            }

            if (selected.Kind == InteractionTargetKind.CookingStation
                && selected.SceneObject is SceneObjectId station)
            {
                CookingRecipeDefinition? recipe = definitions?.CookingRecipes.SingleOrDefault();
                return recipe is null
                    ? Rejected(envelope, IntentReason.UnknownRecipe)
                    : ResolveCook(state, actor, envelope, new CookIntent(station, recipe.Id));
            }

            if (selected.Actor is ActorId targetActor)
            {
                return ResolveTalk(state, actor, envelope, new TalkIntent(targetActor));
            }

            if (selected.Plot is FarmPlotId plot)
            {
                FarmPlotState plotState = state.FarmPlots.Single(candidate => candidate.Id == plot);
                if (plotState.Crop is null)
                {
                    if (state.Version >= TinyFarmState.ItemActionSaveVersion)
                    {
                        return Rejected(envelope, IntentReason.NoInteraction);
                    }
                    return ResolvePlant(
                        state,
                        actor,
                        envelope,
                        new PlantIntent(plot, TinyFarmIds.TurnipCrop));
                }
                CropDefinition crop = definitions!.Crop(plotState.Crop.Value);
                if (plotState.GrowthStage >= crop.GrowthDays)
                {
                    return ResolveHarvest(state, actor, envelope, new HarvestIntent(plot));
                }
                if (!plotState.WateredToday)
                {
                    return ResolveWater(state, actor, envelope, new WaterIntent(plot));
                }
                return Rejected(envelope, IntentReason.NoInteraction);
            }

            if (selected.Kind == InteractionTargetKind.Shop)
            {
                if (state.ProductCount(actor.Id, TinyFarmIds.Turnip) > 0)
                {
                    return ResolveSellProduct(state, actor, envelope, new SellProductIntent(TinyFarmIds.Turnip));
                }
                return ResolveBuyProduct(state, actor, envelope, new BuyProductIntent(TinyFarmIds.TurnipSeed));
            }

            return ResolvePortalInteraction(state, actor, envelope, placement, scene, selected.SceneObject);
        }

        SceneRoute? route = scene.Routes
            .Where(candidate => scene.Placement(candidate.TriggerObject).Contains(placement.Position))
            .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (route is null)
        {
            return Rejected(envelope, IntentReason.NoInteraction);
        }

        return ApplyRoute(state, actor, envelope, placement, scene, route);
    }

    private InteractionTarget? SelectExplicitObjectTarget(
        TinyFarmState state,
        ActorId actor,
        SceneObjectId objectId)
    {
        return TinyFarmSpatialQueries.SelectObjectTarget(state, actor, objectId, Scenes);
    }

    private IntentResult ResolvePortalInteraction(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        ActorSceneState placement,
        SceneDefinition scene,
        SceneObjectId? targetObject)
    {
        SceneRoute? route = scene.Routes
            .Where(candidate => candidate.TriggerObject == targetObject)
            .OrderBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        return route is null
            ? Rejected(envelope, IntentReason.NoInteraction)
            : ApplyRoute(state, actor, envelope, placement, scene, route);
    }

    private IntentResult ApplyRoute(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        ActorSceneState placement,
        SceneDefinition scene,
        SceneRoute route)
    {
        SceneDefinition target = Scenes.Get(route.TargetScene);
        SceneAnchorDefinition targetAnchor = target.Anchor(route.TargetAnchor);
        ReplaceActorScene(state, placement with
        {
            Scene = target.Id,
            WorldPosition = targetAnchor.Position,
            Facing = targetAnchor.Facing ?? placement.Facing
        });
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

    private static ActorFacing FacingFor(int deltaX, int deltaY)
    {
        if (deltaX < 0)
        {
            return ActorFacing.Left;
        }
        if (deltaX > 0)
        {
            return ActorFacing.Right;
        }
        return deltaY < 0 ? ActorFacing.Up : ActorFacing.Down;
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

    private IntentResult ResolveTake(
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

        if (item.Owner is not null || item.GroundLocation is null)
        {
            return Rejected(
                envelope,
                state.Version >= TinyFarmState.ItemActionSaveVersion
                    ? IntentReason.ItemNotGround
                    : IntentReason.ItemAbsent);
        }

        if (item.GroundLocation != actor.Location)
        {
            return Rejected(envelope, IntentReason.ItemAbsent);
        }


        if (state.Version >= TinyFarmState.ItemActionSaveVersion
            && TinyFarmSpatialQueries.SelectItemTarget(state, actor.Id, item.Id) is null)
        {
            return Rejected(envelope, IntentReason.ItemOutOfRange);
        }

        ReplaceItem(state, item with
        {
            GroundLocation = null,
            Owner = actor.Id,
            GroundScene = null,
            GroundPosition = null
        });
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

    private IntentResult ResolveGather(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        GatherIntent intent)
    {
        ForageNodeDefinition? definition = definitions?.ForageNodes
            .SingleOrDefault(node => node.Id == intent.Node);
        ForageNodeState? node = state.ForageNodes.SingleOrDefault(candidate => candidate.Id == intent.Node);
        if (definition is null || node is null)
        {
            return Rejected(envelope, IntentReason.UnknownForageNode);
        }
        if (!actor.IsPlayer)
        {
            return Rejected(envelope, IntentReason.WrongTargetKind);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        if (placement.Scene != definition.Scene)
        {
            return Rejected(envelope, IntentReason.ForageWrongScene);
        }
        if (node.Availability == ForageNodeAvailability.Depleted)
        {
            return Rejected(envelope, IntentReason.AlreadyDepleted);
        }
        if (definition.YieldCount <= 0
            || definitions is null
            || !definitions.Items.Any(item => item.Id == definition.Product))
        {
            return Rejected(envelope, IntentReason.UnknownForageNode);
        }

        SceneObjectId objectId = new(definition.Id.Value);
        InteractionTarget? target = TinyFarmSpatialQueries.SelectObjectTarget(
            state,
            actor.Id,
            objectId,
            Scenes);
        if (target?.ForageNode != definition.Id)
        {
            return Rejected(envelope, IntentReason.ForageOutOfRange);
        }

        int nodeIndex = state.MutableForageNodes.FindIndex(candidate => candidate.Id == node.Id);
        int newCount = checked(state.ProductCount(actor.Id, definition.Product) + definition.YieldCount);
        SetProductCount(state, actor.Id, definition.Product, newCount);
        state.MutableForageNodes[nodeIndex] = node with { Availability = ForageNodeAvailability.Depleted };
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.ForageGathered,
                actor.Id,
                Amount: definition.YieldCount,
                Product: definition.Product,
                Scene: definition.Scene,
                SceneObject: objectId,
                ForageNode: definition.Id));
    }

    private IntentResult ResolveCook(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        CookIntent intent)
    {
        CookingRecipeDefinition? recipe = definitions?.CookingRecipes
            .SingleOrDefault(candidate => candidate.Id == intent.Recipe);
        if (recipe is null)
        {
            return Rejected(envelope, IntentReason.UnknownRecipe);
        }
        if (!actor.IsPlayer)
        {
            return Rejected(envelope, IntentReason.WrongTargetKind);
        }

        SceneDefinition? stationScene = Scenes.All.SingleOrDefault(scene =>
            scene.Objects.Any(candidate => candidate.Id == intent.Station));
        if (stationScene is null
            || stationScene.Object(intent.Station).Kind != SceneObjectKind.CookingStation
            || recipe.StationKind != CookingStationKind.Cooking)
        {
            return Rejected(envelope, IntentReason.WrongStation);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        if (placement.Scene != stationScene.Id)
        {
            return Rejected(envelope, IntentReason.StationWrongScene);
        }

        InteractionTarget? target = TinyFarmSpatialQueries.SelectObjectTarget(
            state,
            actor.Id,
            intent.Station,
            Scenes);
        if (target?.Kind != InteractionTargetKind.CookingStation)
        {
            return Rejected(envelope, IntentReason.StationOutOfRange);
        }

        foreach (CookingRecipeInput input in recipe.Inputs)
        {
            if (state.ProductCount(actor.Id, input.Product) < input.Count)
            {
                return Rejected(envelope, IntentReason.MissingIngredient);
            }
        }

        int outputCount = checked(state.ProductCount(actor.Id, recipe.OutputProduct) + recipe.OutputCount);

        foreach (CookingRecipeInput input in recipe.Inputs)
        {
            int remaining = state.ProductCount(actor.Id, input.Product) - input.Count;
            SetProductCount(state, actor.Id, input.Product, remaining);
        }
        SetProductCount(state, actor.Id, recipe.OutputProduct, outputCount);
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.RecipeCooked,
                actor.Id,
                Amount: recipe.OutputCount,
                Product: recipe.OutputProduct,
                Scene: stationScene.Id,
                SceneObject: intent.Station,
                Recipe: recipe.Id));
    }

    private IntentResult ResolveChop(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        ChopIntent intent)
    {
        TreeDefinition? definition = definitions?.Trees.SingleOrDefault(tree => tree.Id == intent.Tree);
        TreeState? tree = state.Trees.SingleOrDefault(candidate => candidate.Id == intent.Tree);
        if (definition is null || tree is null)
        {
            return Rejected(envelope, IntentReason.UnknownTree);
        }
        if (!actor.IsPlayer)
        {
            return Rejected(envelope, IntentReason.WrongTargetKind);
        }
        if (!OwnsItem(state, actor, TinyFarmIds.Axe))
        {
            return Rejected(envelope, IntentReason.MissingAxe);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        if (placement.Scene != definition.Scene)
        {
            return Rejected(envelope, IntentReason.TreeWrongScene);
        }
        if (tree.Availability == TreeAvailability.Depleted)
        {
            return Rejected(envelope, IntentReason.AlreadyDepleted);
        }
        if (definition.YieldCount <= 0
            || definitions is null
            || !definitions.Items.Any(product => product.Id == definition.YieldProduct))
        {
            return Rejected(envelope, IntentReason.UnknownTree);
        }

        SceneObjectId objectId = new(definition.Id.Value);
        InteractionTarget? target = TinyFarmSpatialQueries.SelectObjectTarget(
            state,
            actor.Id,
            objectId,
            Scenes);
        if (target?.Tree != definition.Id)
        {
            return Rejected(envelope, IntentReason.TreeOutOfRange);
        }

        int treeIndex = state.MutableTrees.FindIndex(candidate => candidate.Id == tree.Id);
        int newCount = checked(state.ProductCount(actor.Id, definition.YieldProduct) + definition.YieldCount);
        SetProductCount(state, actor.Id, definition.YieldProduct, newCount);
        state.MutableTrees[treeIndex] = tree with { Availability = TreeAvailability.Depleted };
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.TreeChopped,
                actor.Id,
                Amount: definition.YieldCount,
                Product: definition.YieldProduct,
                Scene: definition.Scene,
                SceneObject: objectId,
                Tree: definition.Id));
    }

    private IntentResult ResolveAttack(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        AttackIntent intent)
    {
        EnemyDefinition? definition = definitions?.Enemies.SingleOrDefault(enemy => enemy.Id == intent.Enemy);
        EnemyState? enemy = state.Enemies.SingleOrDefault(candidate => candidate.Id == intent.Enemy);
        if (definition is null || enemy is null)
        {
            return Rejected(envelope, IntentReason.UnknownEnemy);
        }
        if (!actor.IsPlayer)
        {
            return Rejected(envelope, IntentReason.WrongTargetKind);
        }
        if (!OwnsItem(state, actor, TinyFarmIds.Sword))
        {
            return Rejected(envelope, IntentReason.MissingSword);
        }

        ActorSceneState placement = state.ActorScene(actor.Id);
        if (placement.Scene != definition.Scene)
        {
            return Rejected(envelope, IntentReason.EnemyWrongScene);
        }
        if (enemy.Lifecycle == EnemyLifecycle.Defeated)
        {
            return Rejected(envelope, IntentReason.AlreadyDefeated);
        }

        SceneObjectId objectId = new(definition.Id.Value);
        InteractionTarget? target = TinyFarmSpatialQueries.SelectObjectTarget(
            state,
            actor.Id,
            objectId,
            Scenes);
        if (target?.Enemy != definition.Id)
        {
            return Rejected(envelope, IntentReason.EnemyOutOfRange);
        }

        const int swordDamage = 1;
        int remainingHealth = Math.Max(0, enemy.CurrentHealth - swordDamage);
        int enemyIndex = state.MutableEnemies.FindIndex(candidate => candidate.Id == enemy.Id);
        state.MutableEnemies[enemyIndex] = enemy with { CurrentHealth = remainingHealth };
        if (remainingHealth > 0)
        {
            return Accepted(envelope);
        }
        return Accepted(
            envelope,
            new GameEvent(
                GameEventKind.EnemyDefeated,
                actor.Id,
                Amount: swordDamage,
                Scene: definition.Scene,
                SceneObject: objectId,
                Enemy: definition.Id,
                EnemyKind: definition.Kind));
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
        if (state.Version >= TinyFarmState.EnergySaveVersion)
        {
            for (int index = 0; index < state.MutableActorEnergy.Count; index++)
            {
                ActorEnergyState current = state.MutableActorEnergy[index];
                state.MutableActorEnergy[index] = current with
                {
                    Energy = TinyFarmEnergy.Advance(current.Energy, current.IsResting, intent.Minutes)
                };
            }
        }
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

    private IntentResult ResolveWater(TinyFarmState state, ActorState actor, IntentEnvelope envelope, WaterIntent intent)
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
        return state.TryGetActorIndex(id, out int index)
            ? state.MutableActors[index]
            : null;
    }

    private static ItemState? FindItem(TinyFarmState state, ItemId id)
    {
        return state.Items.SingleOrDefault(item => item.Id == id);
    }

    private static bool OwnsItem(TinyFarmState state, ActorState actor, ItemId itemId)
    {
        return actor.Inventory.Contains(itemId)
            && state.Items.SingleOrDefault(item => item.Id == itemId)?.Owner == actor.Id;
    }

    private static void ReplaceActor(TinyFarmState state, ActorState replacement)
    {
        if (!state.TryGetActorIndex(replacement.Id, out int index))
        {
            throw new InvalidOperationException($"Unknown actor '{replacement.Id}'.");
        }
        state.MutableActors[index] = replacement;
    }

    private static void ReplaceActorScene(TinyFarmState state, ActorSceneState replacement)
    {
        int index = state.ActorSceneIndex(replacement.Actor);
        state.MutableActorScenes[index] = replacement;
    }

    private static void SetResting(TinyFarmState state, ActorId actor, bool isResting)
    {
        if (state.TryGetActorEnergyIndex(actor, out int index))
        {
            ActorEnergyState current = state.MutableActorEnergy[index];
            if (current.IsResting != isResting)
            {
                state.MutableActorEnergy[index] = current with { IsResting = isResting };
            }
        }
    }

    private static bool ActorsAreNearWhenSpatial(TinyFarmState state, ActorId first, ActorId second)
    {
        if (state.Version < TinyFarmState.SceneSaveVersion)
        {
            return true;
        }

        ActorSceneState left = state.ActorScene(first);
        ActorSceneState right = state.ActorScene(second);
        return left.Scene == right.Scene
            && (state.Version < TinyFarmState.ContinuousSceneSaveVersion
                ? left.Position.ManhattanDistance(right.Position) <= 1
                : left.WorldPosition.SquaredDistance(right.WorldPosition)
                    <= (long)TinyFarmSpatialQueries.InteractionRangeUnits * TinyFarmSpatialQueries.InteractionRangeUnits);
    }

    private bool ActorIsAdjacentToPlotWhenSpatial(TinyFarmState state, ActorId actor, FarmPlotId plot)
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

        SceneDefinition scene = Scenes.Get(TinyFarmSceneIds.Farm);
        SceneLayoutRow row = scene.Layout.Single(item =>
            scene.Object(item.ObjectId).Kind == SceneObjectKind.Plot
            && scene.Object(item.ObjectId).SemanticReference == plot.Value);
        if (state.Version < TinyFarmState.ContinuousSceneSaveVersion)
        {
            return placement.Position.ManhattanDistance(new GridPosition(row.X, row.Y)) == 1;
        }

        InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(state, actor, Scenes);
        return target?.Plot == plot;
    }

    private void MoveActorToScheduledScene(TinyFarmState state, ActorId actor, LocationId destination)
    {
        ActorSceneState placement = state.ActorScene(actor);
        SceneId targetScene = TinyFarmScenes.SceneForLocation(destination);
        if (placement.Scene == targetScene)
        {
            return;
        }

        SceneAnchorDefinition anchor = Scenes.AnchorForLocation(destination);
        ReplaceActorScene(state, placement with
        {
            Scene = targetScene,
            WorldPosition = anchor.Position,
            Facing = anchor.Facing ?? placement.Facing
        });
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
        ReplaceItem(state, state.Item(itemId) with
        {
            Owner = to,
            GroundLocation = null,
            GroundScene = null,
            GroundPosition = null
        });
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
