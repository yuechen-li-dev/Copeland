using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class ParticleAgentTests
{
    [Fact]
    public void SceneAgentsOwnIndependentEmitterTimeAndPlacement()
    {
        var definition = new ParticleEmitterDefinition(ParticleEmitter3D.Fire("prototype", default, uint.MaxValue));
        using var scene = SceneCompiler.Compile(Scene.World("effects", [
            Scene.Agent("first", definition, new(1, 2, 3)),
            Scene.Agent("second", definition, new(4, 5, 6)),
        ])).Mount();
        var first = scene.Agent<ParticleEmitter3D>("first");
        var second = scene.Agent<ParticleEmitter3D>("second");
        Assert.Equal("first", first.State.Id);
        Assert.Equal(new Vector3(1, 2, 3), first.State.Position);
        first.State = first.State with { ElapsedSeconds = 2, EmitUntilSeconds = 1, Position = new(7, 8, 9) };
        Assert.Equal(0, second.State.ElapsedSeconds);
        Assert.Equal(new Vector3(4, 5, 6), second.State.Position);
        Assert.Equal(uint.MaxValue, first.State.Seed);
        Assert.Equal(first.State.Position, first.WorldTransform.Translation);
        first.State = first.State with { ElapsedSeconds = 0 };
        Assert.Equal(0, first.State.ElapsedSeconds);
        Assert.Equal(0, definition.Emitter.ElapsedSeconds);
    }
}
