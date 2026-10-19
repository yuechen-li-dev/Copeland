using Xunit;

namespace TinyFarm.Core.Tests;

/// <summary>
/// Executable integration sketch: interaction, collision and camera follow on the M21 world, through the resolver.
/// </summary>
public sealed class GameSubstrateAuditM0Tests
{
    [Fact]
    public void Scene_ResolvesInteractionAndCollisionAndTheCameraFollows()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        // Initial fixture authoring only. All subsequent gameplay uses the resolver.
        Place(state, TinyFarmIds.Player, new ScenePosition(12164, 6656), ActorFacing.Left);
        Place(state, TinyFarmIds.Mara, new ScenePosition(11140, 6656), ActorFacing.Right);
        var resolver = new TinyFarmResolver(definitions);
        long sequence = 0;

        ResolutionBatchResult Resolve(GameIntent intent)
        {
            ResolutionBatchResult result = resolver.Resolve(state,
                [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, sequence++, IntentSourceKind.Human)]);
            state = result.State;
            return result;
        }

        IntentResult interaction = Assert.Single(Resolve(new InteractIntent()).Results);
        Assert.Equal(IntentResultStatus.Accepted, interaction.Status);
        Assert.Contains(interaction.Events, item =>
            item.Kind == GameEventKind.Conversation && item.Target == TinyFarmIds.Mara);

        string beforeCollision = TinyFarmSemanticHash.Compute(state);
        IntentResult collision = Assert.Single(Resolve(new SpatialMoveIntent(1, 0, 256)).Results);
        Assert.Equal(IntentReason.MovementBlocked, collision.Reason);
        Assert.Equal(beforeCollision, TinyFarmSemanticHash.Compute(state));

        TinyFarmFrame beforeMove = TinyFarmFrameProjector.Project(state, definitions);
        Assert.Equal(IntentResultStatus.Accepted,
            Assert.Single(Resolve(new SpatialMoveIntent(-1, 0, 128)).Results).Status);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);
        Assert.NotEqual(CameraX(beforeMove), CameraX(frame));
        Assert.Contains(frame.Actors, actor => actor.Id == TinyFarmIds.Mara);
    }

    private static double CameraX(TinyFarmFrame frame)
    {
        TinyFarmActorView player = frame.Actors.Single(actor => actor.IsPlayer);
        // Deliberately magnified viewport to pressure follow rather than fit the whole room.
        return Math.Max(0, (player.Position.X * 32.0 / frame.SceneUnitsPerTile) - 160);
    }

    private static void Place(TinyFarmState state, ActorId actor, ScenePosition position, ActorFacing facing)
    {
        int sceneIndex = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[sceneIndex] = new ActorSceneState(actor, TinyFarmSceneIds.Farm, position, facing);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = TinyFarmIds.Farmhouse };
    }
}
