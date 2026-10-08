namespace TinyFarm.Core;

public static class TinyFarmM24ProofStates
{
    public static TinyFarmSession CreateAt(
        TinyFarmDefinitions definitions,
        SceneId scene,
        GridPosition position,
        ActorFacing facing = ActorFacing.Right)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        ActorSceneState current = state.MutableActorScenes[placementIndex];
        state.MutableActorScenes[placementIndex] = current with
        {
            Scene = scene,
            WorldPosition = ScenePosition.FromGrid(position),
            Facing = facing,
        };
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == TinyFarmIds.Player);
        ActorState player = state.MutableActors[actorIndex];
        state.MutableActors[actorIndex] = player with { Location = TinyFarmScenes.LocationForScene(scene) };
        return new TinyFarmSession(state, definitions);
    }
}
