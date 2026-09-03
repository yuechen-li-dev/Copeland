namespace TinyFarm.Core;

public enum InteractionTargetKind
{
    Actor,
    Plot,
    Shop,
    Portal,
    GroundItem,
    ForageNode
}

public sealed record InteractionTarget(
    InteractionTargetKind Kind,
    string StableId,
    ActorId? Actor = null,
    SceneObjectId? SceneObject = null,
    FarmPlotId? Plot = null,
    ItemId? Item = null,
    ForageNodeId? ForageNode = null,
    long SquaredDistance = 0);

public static class TinyFarmSpatialQueries
{
    public const int InteractionRangeUnits = 1280;
    private const int InteractionHalfWidthUnits = 640;

    public static InteractionTarget? SelectInteractionTarget(
        TinyFarmState state,
        ActorId actorId,
        TinyFarmSceneCatalog scenes)
    {
        if (state.Version < TinyFarmState.ContinuousSceneSaveVersion)
        {
            return null;
        }

        ActorSceneState actor = state.ActorScene(actorId);
        SceneDefinition scene = scenes.Get(actor.Scene);
        var candidates = new List<InteractionTarget>();

        foreach (ActorSceneState other in state.ActorScenes.Where(candidate =>
                     candidate.Actor != actorId && candidate.Scene == actor.Scene))
        {
            AddIfTargetable(
                candidates,
                actor,
                other.WorldPosition,
                new InteractionTarget(
                    InteractionTargetKind.Actor,
                    $"actor:{other.Actor.Value}",
                    Actor: other.Actor));
        }

        foreach (SceneLayoutRow row in scene.Layout)
        {
            SceneObjectDefinition definition = scene.Object(row.ObjectId);
            InteractionTargetKind? kind = definition.Kind switch
            {
                SceneObjectKind.Plot => InteractionTargetKind.Plot,
                SceneObjectKind.Shop => InteractionTargetKind.Shop,
                SceneObjectKind.Portal => InteractionTargetKind.Portal,
                SceneObjectKind.Forage when IsAvailable(state, definition.Id) => InteractionTargetKind.ForageNode,
                _ => null
            };
            if (kind is null)
            {
                continue;
            }

            var center = new ScenePosition(
                (row.X * ScenePosition.UnitsPerTile) + (row.Width * ScenePosition.UnitsPerTile / 2),
                (row.Y * ScenePosition.UnitsPerTile) + (row.Height * ScenePosition.UnitsPerTile / 2));
            FarmPlotId? plot = kind == InteractionTargetKind.Plot && definition.SemanticReference is string reference
                ? new FarmPlotId(reference)
                : null;
            AddIfTargetable(
                candidates,
                actor,
                center,
                new InteractionTarget(
                    kind.Value,
                    $"object:{definition.Id.Value}",
                    SceneObject: definition.Id,
                    Plot: plot,
                    ForageNode: kind == InteractionTargetKind.ForageNode
                        ? new ForageNodeId(definition.Id.Value)
                        : null));
        }

        foreach (ItemState item in state.Items.Where(candidate =>
                     candidate.Owner is null
                     && candidate.GroundScene == actor.Scene
                     && candidate.GroundPosition is not null))
        {
            AddIfTargetable(
                candidates,
                actor,
                item.GroundPosition!.Value,
                new InteractionTarget(
                    InteractionTargetKind.GroundItem,
                    $"item:{item.Id.Value}",
                    Item: item.Id));
        }

        return candidates
            .OrderBy(candidate => Priority(candidate.Kind))
            .ThenBy(candidate => candidate.SquaredDistance)
            .ThenBy(candidate => candidate.StableId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static InteractionTarget? SelectItemTarget(
        TinyFarmState state,
        ActorId actorId,
        ItemId itemId)
    {
        if (state.Version < TinyFarmState.ItemActionSaveVersion)
        {
            return null;
        }

        ActorSceneState actor = state.ActorScene(actorId);
        ItemState? item = state.Items.SingleOrDefault(candidate => candidate.Id == itemId);
        if (item?.Owner is not null
            || item?.GroundScene != actor.Scene
            || item.GroundPosition is not ScenePosition position)
        {
            return null;
        }

        var candidates = new List<InteractionTarget>();
        AddIfTargetable(
            candidates,
            actor,
            position,
            new InteractionTarget(
                InteractionTargetKind.GroundItem,
                $"item:{item.Id.Value}",
                Item: item.Id));
        return candidates.SingleOrDefault();
    }

    public static InteractionTarget? SelectObjectTarget(
        TinyFarmState state,
        ActorId actorId,
        SceneObjectId objectId,
        TinyFarmSceneCatalog scenes)
    {
        if (state.Version < TinyFarmState.ContinuousSceneSaveVersion)
        {
            return null;
        }

        ActorSceneState actor = state.ActorScene(actorId);
        SceneDefinition scene = scenes.Get(actor.Scene);
        SceneLayoutRow? row = scene.Layout.SingleOrDefault(candidate => candidate.ObjectId == objectId);
        if (row is null)
        {
            return null;
        }
        SceneObjectDefinition definition = scene.Object(objectId);
        InteractionTargetKind? kind = definition.Kind switch
        {
            SceneObjectKind.Plot => InteractionTargetKind.Plot,
            SceneObjectKind.Shop => InteractionTargetKind.Shop,
            SceneObjectKind.Portal => InteractionTargetKind.Portal,
            SceneObjectKind.Forage when IsAvailable(state, definition.Id) => InteractionTargetKind.ForageNode,
            _ => null
        };
        if (kind is null)
        {
            return null;
        }

        var center = new ScenePosition(
            (row.X * ScenePosition.UnitsPerTile) + (row.Width * ScenePosition.UnitsPerTile / 2),
            (row.Y * ScenePosition.UnitsPerTile) + (row.Height * ScenePosition.UnitsPerTile / 2));
        var candidates = new List<InteractionTarget>();
        AddIfTargetable(
            candidates,
            actor,
            center,
            new InteractionTarget(
                kind.Value,
                $"object:{objectId.Value}",
                SceneObject: objectId,
                Plot: kind == InteractionTargetKind.Plot && definition.SemanticReference is string reference
                    ? new FarmPlotId(reference)
                    : null,
                ForageNode: kind == InteractionTargetKind.ForageNode
                    ? new ForageNodeId(objectId.Value)
                    : null));
        return candidates.SingleOrDefault();
    }

    private static void AddIfTargetable(
        ICollection<InteractionTarget> candidates,
        ActorSceneState actor,
        ScenePosition position,
        InteractionTarget target)
    {
        int deltaX = position.XUnits - actor.WorldPosition.XUnits;
        int deltaY = position.YUnits - actor.WorldPosition.YUnits;
        (int forwardX, int forwardY) = actor.Facing switch
        {
            ActorFacing.Left => (-1, 0),
            ActorFacing.Right => (1, 0),
            ActorFacing.Up => (0, -1),
            _ => (0, 1)
        };
        int forward = (deltaX * forwardX) + (deltaY * forwardY);
        int lateral = Math.Abs((deltaX * forwardY) - (deltaY * forwardX));
        long squaredDistance = position.SquaredDistance(actor.WorldPosition);
        if (forward < 0
            || forward > InteractionRangeUnits
            || lateral > InteractionHalfWidthUnits
            || squaredDistance > (long)InteractionRangeUnits * InteractionRangeUnits)
        {
            return;
        }

        candidates.Add(target with { SquaredDistance = squaredDistance });
    }

    private static int Priority(InteractionTargetKind kind)
    {
        return kind switch
        {
            InteractionTargetKind.Actor => 0,
            InteractionTargetKind.Portal => 1,
            InteractionTargetKind.GroundItem => 2,
            InteractionTargetKind.ForageNode => 3,
            InteractionTargetKind.Plot => 4,
            InteractionTargetKind.Shop => 5,
            _ => 5
        };
    }

    private static bool IsAvailable(TinyFarmState state, SceneObjectId objectId)
    {
        if (state.Version < TinyFarmState.ForageSaveVersion)
        {
            return false;
        }

        ForageNodeId nodeId = new(objectId.Value);
        return state.ForageNodes.SingleOrDefault(node => node.Id == nodeId)?.Availability
            == ForageNodeAvailability.Available;
    }
}
