using System.Numerics;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.Games;

/// <summary>A typed target prototype, customizable with normal C# record copying.</summary>
public sealed record StarterTargetDefinition(int Health, Vector3 HalfSize)
    : AgentDefinition<int>(AgentTemplate.Object("starter.target"))
{
    public override string Identity => FormattableString.Invariant(
        $"starter.target.v1:{Health}:{HalfSize.X:R}:{HalfSize.Y:R}:{HalfSize.Z:R}");

    public override int CreateState(ScenePlacement placement) => Health;

    public override void ValidatePlacement(ScenePlacement placement)
    {
        if (Health <= 0 || !float.IsFinite(HalfSize.X) || !float.IsFinite(HalfSize.Y)
            || !float.IsFinite(HalfSize.Z) || HalfSize.X <= 0 || HalfSize.Y <= 0 || HalfSize.Z <= 0)
        {
            throw new InvalidDataException("Starter targets need positive health and finite positive half sizes.");
        }
    }

    public override SceneGroup Present(int state)
    {
        Vector4 color = state > 0 ? new(0.9f, 0.3f, 0.15f, 1) : new(0.2f, 0.55f, 0.3f, 1);
        return Scene.Group("target-body", [Scene.Box("shape", HalfSize * 2, color)]);
    }
}

public sealed record StarterPlayerDefinition()
    : AgentDefinition<Vector3>(AgentTemplate.Character("starter.player", AgentControl.Human))
{
    private static readonly SceneGroup Body = Scene.Group("player-body",
        [Scene.Box("shape", new(0.6f, 1.6f, 0.6f), new(0.2f, 0.7f, 0.9f, 1), at: new(0, 0.8f, 0))]);

    public override string Identity => "starter.player.v1";
    public override Vector3 CreateState(ScenePlacement placement) => placement.Position;
    public override Matrix4x4 WorldTransform(Vector3 state, ScenePlacement placement) => placement.At(state);
    public override SceneGroup Present(Vector3 state) => Body;
}

public static class StarterScenes
{
    public static SceneGroup TrainingRange(IEnumerable<StarterObject> objects)
    {
        var children = new List<SceneNode>
        {
            Scene.Box("floor", new(24, 0.2f, 24), new(0.2f, 0.3f, 0.35f, 1), at: new(0, -0.1f, 0), collision: SceneCollision.Solid),
            Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, 0, 7), name: "Player"),
        };
        foreach (StarterObject target in objects)
        {
            children.Add(Scene.Agent(target.Id, new StarterTargetDefinition(target.Health, target.HalfSize.ToVector()),
                at: target.Position.ToVector()));
        }
        return Scene.World("training-range", children);
    }
}
