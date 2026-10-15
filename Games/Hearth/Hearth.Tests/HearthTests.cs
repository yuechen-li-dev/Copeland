using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Hearth;
using Xunit;

namespace Hearth.Tests;

public sealed class HearthTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(3.25)]
    [InlineData(1799.9)]
    [InlineData(5000.0)]
    public void EveryFrameSubmitsValidUniqueEmittersThatFitTheDefaultPool(double time)
    {
        IReadOnlyList<ParticleEmitter3D> emitters = HearthEffects.Emitters(HearthScene.Fires, time);
        Assert.InRange(emitters.Count, 1, 16);
        Assert.Equal(emitters.Count, emitters.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (ParticleEmitter3D emitter in emitters)
        {
            emitter.Validate();
            // The default pool reserves 512 slots per emitter; demand beyond that would thin the effect.
            Assert.True(emitter.Rate * emitter.Lifetime <= 512, $"{emitter.Id} needs {emitter.Rate * emitter.Lifetime} slots");
        }
        foreach (LocalLight3D light in HearthEffects.Lights(HearthScene.Fires, time))
        {
            light.Validate();
        }
    }

    [Fact]
    public void FlickerAndEffectsArePureFunctionsOfTheClock()
    {
        Assert.Equal(HearthEffects.Lights(HearthScene.Fires, 12.5), HearthEffects.Lights(HearthScene.Fires, 12.5));
        Assert.Equal(HearthEffects.Emitters(HearthScene.Fires, 12.5), HearthEffects.Emitters(HearthScene.Fires, 12.5));
        Assert.NotEqual(HearthEffects.Lights(HearthScene.Fires, 12.5)[0].Intensity, HearthEffects.Lights(HearthScene.Fires, 12.6)[0].Intensity);
    }

    [Fact]
    public void SmokeIsLitFromBelowAndTorchLightsStandOffTheWall()
    {
        Assert.All(HearthEffects.Emitters(HearthScene.Fires, 1).Where(item => item.Kind == ParticleEffectKind3D.Smoke),
            smoke => Assert.True(smoke.Glow.X > smoke.Glow.Z));
        LocalLight3D torch = HearthEffects.Lights([HearthScene.TorchLeft], 0)[0];
        Assert.True(torch.Position.Z - (-3.7f) > 0.4f, "a light against the wall burns a hot spot into it");
    }

    [Fact]
    public void PlaytestCommandsFrameShotsAndToggleDaylight()
    {
        using var game = new HearthGame();
        Assert.True(game.Command("shot brazier"));
        Assert.Equal("brazier", game.Shot.Name);
        Assert.True(game.Command("daylight on"));
        Assert.True(game.Command("orbit 45"));
        Assert.False(game.Command("shot nowhere"));
        Assert.False(game.Command("explode"));
    }
}
