using System.Numerics;
using Aurelian.Effects2D;
using Aurelian.Graphics.Vulkan.Native2D;

namespace Aurelian.Effects2D.Graphics;

public static class EffectNativeProjection
{
    public static IReadOnlyList<NativeAnalyticShapeSubmission> Particles(
        IReadOnlyList<ParticleSnapshot> particles,
        EffectCameraTransform camera)
    {
        ArgumentNullException.ThrowIfNull(particles);
        var result = new NativeAnalyticShapeSubmission[particles.Count];
        for (int index = 0; index < particles.Count; index++)
        {
            ParticleSnapshot particle = particles[index];
            Vector2 center = camera.Project(particle.Position, particle.Space);
            float scale = particle.Space == EffectCoordinateSpace.World
                ? camera.PixelsPerWorldUnit * camera.Zoom
                : 1;
            float size = MathF.Max(1, particle.Size * scale);
            float fade = Math.Clamp(1 - (particle.AgeSeconds / particle.LifetimeSeconds), 0, 1);
            Native2DTint color = ParticleColor(particle.EffectId, particle.Variant, fade);
            result[index] = new NativeAnalyticShapeSubmission(
                new Native2DRect(center.X - size / 2, center.Y - size / 2, size, size),
                new Native2DSize(size, size),
                Native2DUvRect.Full,
                NativeAnalyticShapeKind.Circle,
                color,
                size / 2,
                color,
                0);
        }
        return result;
    }

    public static IReadOnlyList<NativeSoftShockwaveSubmission> Shockwaves(
        IReadOnlyList<EffectQuadSnapshot> quads,
        EffectCameraTransform camera)
    {
        ArgumentNullException.ThrowIfNull(quads);
        return quads
            .Where(quad => quad.MaterialId == EffectMaterialIds.SoftShockwave)
            .Select(quad =>
            {
                Vector2 center = camera.Project(quad.Position, quad.Space);
                float scale = quad.Space == EffectCoordinateSpace.World
                    ? camera.PixelsPerWorldUnit * camera.Zoom
                    : 1;
                float diameter = MathF.Max(24, quad.Radius * scale * 8);
                return new NativeSoftShockwaveSubmission(
                    new Native2DRect(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter),
                    Native2DUvRect.Full,
                    new Native2DTint(1, 0.78f, 0.18f, 1),
                    quad.AgeSeconds,
                    quad.LifetimeSeconds,
                    Radius: 0.46f,
                    Thickness: 0.09f,
                    quad.Intensity,
                    Seed: (quad.Seed & 0xFFFF) / 65535f);
            })
            .ToArray();
    }

    public static IReadOnlyList<NativeAnalyticShapeSubmission> ScreenFlashes(
        IReadOnlyList<EffectQuadSnapshot> quads,
        uint width,
        uint height)
    {
        ArgumentNullException.ThrowIfNull(quads);
        return quads
            .Where(quad => quad.EffectId == VisualEffectIds.ScreenFlash)
            .Select(quad =>
            {
                float fade = Math.Clamp(1 - (quad.AgeSeconds / quad.LifetimeSeconds), 0, 1);
                var color = new Native2DTint(1, 0.25f, 0.12f, fade * quad.Intensity);
                return new NativeAnalyticShapeSubmission(
                    new Native2DRect(0, 0, width, height),
                    new Native2DSize(width, height),
                    Native2DUvRect.Full,
                    NativeAnalyticShapeKind.RoundedRect,
                    color,
                    0,
                    color,
                    0);
            })
            .ToArray();
    }

    private static Native2DTint ParticleColor(VisualEffectId effectId, uint variant, float fade)
    {
        if (effectId == VisualEffectIds.SwordHit || effectId == VisualEffectIds.PickupSparkle)
        {
            return variant % 2 == 0
                ? new Native2DTint(1, 0.88f, 0.25f, fade)
                : new Native2DTint(1, 1, 0.85f, fade);
        }
        if (effectId == VisualEffectIds.HarvestPuff)
        {
            return variant % 2 == 0
                ? new Native2DTint(0.62f, 0.88f, 0.42f, fade * 0.85f)
                : new Native2DTint(0.9f, 0.95f, 0.72f, fade * 0.8f);
        }
        if (effectId == VisualEffectIds.AmbientMotes)
        {
            return new Native2DTint(1, 0.92f, 0.56f, fade * 0.6f);
        }
        return new Native2DTint(0.72f, 0.62f, 0.48f, fade * 0.65f);
    }
}
