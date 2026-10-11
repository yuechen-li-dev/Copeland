using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class ParticleEmitterTests
{
    [Fact]
    public void NonfiniteOrUnsupportedEmitterCommandsAreRejected()
    {
        var emitter = ParticleEmitter3D.Fire("fire", default);
        ParticleEmitter3D[] invalid = [
            emitter with { Position = new(float.NaN) }, emitter with { Direction = Vector3.Zero },
            emitter with { Rate = float.PositiveInfinity }, emitter with { Lifetime = 0 },
            emitter with { ElapsedSeconds = -1 }, emitter with { Color = new(1, 1, 1, 2) },
            emitter with { Kind = (ParticleEffectKind3D)99 }, emitter with { Id = "" },
            emitter with { Direction = new(float.MaxValue) }, emitter with { Position = new(float.MaxValue) },
        ];
        foreach (var command in invalid)
        {
            Assert.StartsWith("AUR-VFX-001:", Assert.Throws<ArgumentException>(command.Validate).Message);
        }
    }

    [Fact]
    public void PresetsPreserveFullUnsignedSeedsAndCanBeStoppedAndRewound()
    {
        ParticleEmitter3D[] emitters = [ParticleEmitter3D.Fire("fire", default, uint.MaxValue),
            ParticleEmitter3D.Smoke("smoke", default, uint.MaxValue), ParticleEmitter3D.Sparks("sparks", default, uint.MaxValue)];
        foreach (var emitter in emitters)
        {
            emitter.Validate();
            (emitter with { ElapsedSeconds = 10, EmitUntilSeconds = .5f }).Validate();
            (emitter with { ElapsedSeconds = 0 }).Validate();
            Assert.Equal(uint.MaxValue, emitter.Seed);
        }
    }
}
