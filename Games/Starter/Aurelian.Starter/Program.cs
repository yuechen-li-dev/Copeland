using Aurelian.Games;

GameStarter.Run("aurelian-starter",
    [.. GamePresets.FirstPersonShooter, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl],
    args, new StarterOptions(Title: "AURELIAN | COMPOSABLE STARTER"),
    sceneDocument: TrainingRange.Create());
