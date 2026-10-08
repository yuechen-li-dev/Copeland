using System.Numerics;
using Aurelian.Games;
using Aurelian.World.Scenes;

internal static class TrainingRange
{
    public static SceneGroup Create()
    {
        var target = new StarterTargetDefinition(Health: 3, HalfSize: new(0.8f, 1, 0.8f));
        var checkpoint = Scene.Group("checkpoint",
            [Scene.Agent("target", target, at: new(0, 1, 0))]);

        return Scene.World("training-range",
        [
            Scene.Box("floor", new Vector3(24, 0.2f, 24), new(0.2f, 0.3f, 0.35f, 1), at: new(0, -0.1f, 0)),
            Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, 0, 7), name: "Player"),
            Scene.Instance("left", checkpoint, at: new(-3, 0, -4)),
            Scene.Instance("right", checkpoint, at: new(3, 0, -4)),
            Scene.Agent("captain", target with { Health = 6 }, at: new(0, 1, -6)),
        ]);
    }
}
