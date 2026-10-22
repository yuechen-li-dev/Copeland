using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class ParticleEmitterTests
{
    [Fact]
    public void RainRejectsInvalidMotionFloorShutterAndIncidentLight()
    {
        var rain = ParticleEmitter3D.Rain("rain", new(0, 12, 0), uint.MaxValue);
        rain.Validate();
        (rain with { ElapsedSeconds = 30, EmitUntilSeconds = 2 }).Validate();
        Assert.Equal(uint.MaxValue, rain.Seed);
        ParticleEmitter3D[] invalid =
        [
            rain with { Direction = Vector3.UnitY },
            rain with { Speed = 0 },
            rain with { RainGroundHeight = 12 },
            rain with { RainGroundHeight = float.NaN },
            rain with { RainShutterSeconds = 0 },
            rain with { RainShutterSeconds = .11f },
            rain with { RainIncidentRadiance = new(-1) },
            rain with { RainIncidentRadiance = new(float.NaN) },
        ];
        foreach (var command in invalid)
        {
            Assert.StartsWith("AUR-VFX-009:", Assert.Throws<ArgumentException>(command.Validate).Message);
        }
    }

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
            emitter with { Turbulence = -1 }, emitter with { Turbulence = float.NaN },
            emitter with { Temperature = 100 }, emitter with { Temperature = 1e6f },
            emitter with { Glow = new(-1, 0, 0) }, emitter with { Glow = new(float.PositiveInfinity) },
            emitter with { GlowHeight = 0 },
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
            ParticleEmitter3D.Smoke("smoke", default, uint.MaxValue), ParticleEmitter3D.Sparks("sparks", default, uint.MaxValue),
            ParticleEmitter3D.Embers("embers", default, uint.MaxValue)];
        foreach (var emitter in emitters)
        {
            emitter.Validate();
            (emitter with { ElapsedSeconds = 10, EmitUntilSeconds = .5f }).Validate();
            (emitter with { ElapsedSeconds = 0 }).Validate();
            Assert.Equal(uint.MaxValue, emitter.Seed);
        }
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(1f)]
    [InlineData(4f)]
    public void ScaledPresetsStayValidAndGrowWithScale(float scale)
    {
        ParticleEmitter3D[] small = Presets(1);
        ParticleEmitter3D[] scaled = Presets(scale);
        for (int index = 0; index < scaled.Length; index++)
        {
            scaled[index].Validate();
            Assert.Equal(small[index].Kind, scaled[index].Kind);
            Assert.Equal(scale >= 1, scaled[index].Radius >= small[index].Radius);
        }

        static ParticleEmitter3D[] Presets(float scale) => [ParticleEmitter3D.Fire("fire", default, 1, scale),
            ParticleEmitter3D.Smoke("smoke", default, 1, scale), ParticleEmitter3D.Sparks("sparks", default, 1, scale),
            ParticleEmitter3D.Embers("embers", default, 1, scale)];
    }

    [Fact]
    public void EmittingPresetsBurnAtBlackbodyTemperaturesAndSmokeCarriesNoGlowByDefault()
    {
        Assert.InRange(ParticleEmitter3D.Fire("fire", default).Temperature, 1500, 2200);
        Assert.InRange(ParticleEmitter3D.Embers("embers", default).Temperature, 1200, 2000);
        Assert.Equal(Vector3.Zero, ParticleEmitter3D.Smoke("smoke", default).Glow);
        Assert.True(ParticleEmitter3D.Smoke("smoke", default).Turbulence > 0);
    }
}
