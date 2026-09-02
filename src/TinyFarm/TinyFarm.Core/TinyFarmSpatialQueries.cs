namespace TinyFarm.Core;

public enum InteractionTargetKind
{
    Actor,
    Plot,
    Shop,
    Portal
}

public sealed record InteractionTarget(
    InteractionTargetKind Kind,
    string StableId,
    ActorId? Actor = null,
    SceneObjectId? SceneObject = null,
    FarmPlotId? Plot = null,
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
                    Plot: plot));
        }

        return candidates
            .OrderBy(candidate => Priority(candidate.Kind))
            .ThenBy(candidate => candidate.SquaredDistance)
            .ThenBy(candidate => candidate.StableId, StringComparer.Ordinal)
            .FirstOrDefault();
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
            InteractionTargetKind.Plot => 1,
            InteractionTargetKind.Shop => 2,
            _ => 3
        };
    }
}
