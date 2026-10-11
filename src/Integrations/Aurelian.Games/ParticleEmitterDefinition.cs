using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;

namespace Aurelian.Games;

/// <summary>Normal scene agent state owns emitter time, seed and stop command.
/// Customize the snapshot with C# with-expressions; the renderer owns no game clock.</summary>
public sealed record ParticleEmitterDefinition(ParticleEmitter3D Emitter)
    : AgentDefinition<ParticleEmitter3D>(AgentTemplate.Object("aurelian.vfx.emitter"))
{
    public override string Identity => $"aurelian.vfx.emitter.v1:{Emitter.Id}:{Emitter.Kind}";

    public override void ValidatePlacement(ScenePlacement placement) => Emitter.Validate();

    public override ParticleEmitter3D CreateState(ScenePlacement placement) => Emitter with
    {
        Id = placement.Id,
        Position = placement.Position,
        Direction = Vector3.TransformNormal(Emitter.Direction, placement.WorldTransform),
    };

    public override Matrix4x4 WorldTransform(ParticleEmitter3D state, ScenePlacement placement) =>
        placement.At(state.Position);
}
