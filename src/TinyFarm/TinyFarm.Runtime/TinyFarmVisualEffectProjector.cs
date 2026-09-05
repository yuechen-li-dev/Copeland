using System.Numerics;
using Aurelian.Effects2D;
using TinyFarm.Core;

namespace TinyFarm.Runtime;

public sealed class TinyFarmVisualEffectProjector
{
    public IReadOnlyList<VisualEffectEvent> Project(
        IEnumerable<IntentResult> results,
        TinyFarmState state,
        TinyFarmDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definitions);
        var projected = new List<VisualEffectEvent>();
        foreach (IntentResult result in results)
        {
            if (result.Status != IntentResultStatus.Accepted)
            {
                continue;
            }
            for (int eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
            {
                GameEvent gameEvent = result.Events[eventIndex];
                ProjectEvent(projected, result, eventIndex, gameEvent, state, definitions);
            }
        }
        return projected;
    }

    public VisualEffectEvent ProjectAmbience(SceneId scene)
    {
        string identity = $"tinyfarm:ambience:{scene.Value}";
        return new VisualEffectEvent(
            VisualEffectIds.AmbientMotes,
            new VisualEffectEventId(identity),
            EffectCoordinateSpace.World,
            Position: new Vector2(8 * ScenePosition.UnitsPerTile, 5 * ScenePosition.UnitsPerTile),
            Scale: ScenePosition.UnitsPerTile / 128f,
            Intensity: 0.7f,
            SourceId: scene.Value,
            Seed: StableSeed(identity),
            SemanticVariant: "scene-owned-sunlight-dust");
    }

    private static void ProjectEvent(
        List<VisualEffectEvent> destination,
        IntentResult result,
        int eventIndex,
        GameEvent gameEvent,
        TinyFarmState state,
        TinyFarmDefinitions definitions)
    {
        VisualEffectId? effectId = gameEvent.Kind switch
        {
            GameEventKind.EnemyDefeated => VisualEffectIds.SwordHit,
            GameEventKind.CropHarvested or GameEventKind.ForageGathered => VisualEffectIds.HarvestPuff,
            GameEventKind.ItemTaken => VisualEffectIds.PickupSparkle,
            GameEventKind.ActorMoved when result.Envelope.Sequence % 4 == 0 => VisualEffectIds.FootstepDust,
            _ => null,
        };
        if (effectId is null)
        {
            return;
        }

        string identity = $"tinyfarm:{result.Envelope.Sequence}:{eventIndex}:{gameEvent.Kind}";
        Vector2 position = ResolvePosition(gameEvent, state, definitions);
        destination.Add(new VisualEffectEvent(
            effectId.Value,
            new VisualEffectEventId(identity),
            EffectCoordinateSpace.World,
            Position: position,
            Direction: ResolveDirection(state, gameEvent.Actor),
            Scale: ScenePosition.UnitsPerTile / 128f,
            Intensity: 1,
            SourceId: gameEvent.Actor.Value,
            TargetId: gameEvent.Enemy?.Value ?? gameEvent.SceneObject?.Value ?? gameEvent.Item?.Value,
            Seed: StableSeed(identity),
            SemanticVariant: gameEvent.Kind.ToString()));

        if (gameEvent.Kind == GameEventKind.EnemyDefeated)
        {
            string flashIdentity = identity + ":screen-flash";
            destination.Add(new VisualEffectEvent(
                VisualEffectIds.ScreenFlash,
                new VisualEffectEventId(flashIdentity),
                EffectCoordinateSpace.Screen,
                Intensity: 0.28f,
                SourceId: gameEvent.Actor.Value,
                TargetId: gameEvent.Enemy?.Value,
                Seed: StableSeed(flashIdentity),
                SemanticVariant: "confirmed-combat-hit"));
        }
    }

    private static Vector2 ResolvePosition(
        GameEvent gameEvent,
        TinyFarmState state,
        TinyFarmDefinitions definitions)
    {
        if (gameEvent.Scene is SceneId sceneId && gameEvent.SceneObject is SceneObjectId objectId)
        {
            SceneLayoutRow placement = definitions.Scenes.Get(sceneId).Placement(objectId);
            return new Vector2(
                (placement.X + (placement.Width / 2f)) * ScenePosition.UnitsPerTile,
                (placement.Y + (placement.Height / 2f)) * ScenePosition.UnitsPerTile);
        }
        ActorSceneState actor = state.ActorScene(gameEvent.Actor);
        return new Vector2(actor.WorldPosition.XUnits, actor.WorldPosition.YUnits);
    }

    private static Vector2 ResolveDirection(TinyFarmState state, ActorId actorId)
    {
        return state.ActorScene(actorId).Facing switch
        {
            ActorFacing.Left => -Vector2.UnitX,
            ActorFacing.Right => Vector2.UnitX,
            ActorFacing.Up => -Vector2.UnitY,
            _ => Vector2.UnitY,
        };
    }

    private static ulong StableSeed(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }
}
