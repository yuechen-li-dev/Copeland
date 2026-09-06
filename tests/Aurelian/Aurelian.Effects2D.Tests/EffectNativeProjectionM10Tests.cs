using System.Numerics;
using Aurelian.Effects2D.Graphics;
using Xunit;

namespace Aurelian.Effects2D.Tests;

public sealed class EffectNativeProjectionM10Tests
{
    [Fact]
    public void ParticleLifetimeUsesABoundedNativeMaterialSet()
    {
        var camera = new EffectCameraTransform(Vector2.Zero, Vector2.Zero, 1, 1);
        var alphaValues = new HashSet<float>();

        for (int step = 0; step <= 100; step++)
        {
            float age = step / 100f;
            var particle = new ParticleSnapshot(
                new EmitterInstanceId("ambient:1"),
                VisualEffectIds.AmbientMotes,
                Vector2.Zero,
                Vector2.Zero,
                age,
                1,
                2,
                0,
                0,
                1,
                EffectPainterLayer.BehindActors,
                EffectBlendMode.StraightAlpha,
                EffectCoordinateSpace.World);

            alphaValues.Add(EffectNativeProjection.Particle(particle, camera).FillColor.Alpha);
        }

        Assert.Equal(4, alphaValues.Count);
    }
}
