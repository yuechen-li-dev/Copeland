using Aurelian.Games;
using Aurelian.Aetheris;
using Aurelian.World.Scenes;
using System.Numerics;

SceneGroup document = TrainingRange.Create();
if (args.Contains("--aetheris-room", StringComparer.Ordinal))
{
    SceneGroup room = AetherisSceneAsset.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "room.aurelian.json")).Compose();
    document = Scene.World("room-range",
    [
        Scene.Instance("room", room),
        Scene.Agent("player", new StarterPlayerDefinition(), at: new(6, 0, -6)),
        Scene.Agent("target", new StarterTargetDefinition(3, new(0.4f, 1, 0.4f)), at: new(6, 1, -8)),
    ]);
}

GameStarter.Run("aurelian-starter",
    [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl],
    args, new StarterOptions(Title: "AURELIAN | COMPOSABLE STARTER"),
    sceneDocument: document);
