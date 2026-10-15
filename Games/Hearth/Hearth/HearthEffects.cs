using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Hearth;

/// <summary>Turns fire sites and the clock into particle commands and flickering fire light.</summary>
public static class HearthEffects
{
    /// <summary>Emitter clocks are bounded; the showcase wraps its clock long before that bound.</summary>
    public const float ClockWrap = 1800;

    public static IReadOnlyList<ParticleEmitter3D> Emitters(IEnumerable<FireSite> fires, double time)
    {
        float elapsed = (float)(time % ClockWrap);
        var emitters = new List<ParticleEmitter3D>();
        foreach (FireSite fire in fires)
        {
            float scale = fire.Scale;
            emitters.Add(ParticleEmitter3D.Fire(fire.Id + ".fire", fire.Base, fire.Seed, scale) with { ElapsedSeconds = elapsed });
            // The plume starts above the flame tips and is lit from below by the fire.
            emitters.Add(ParticleEmitter3D.Smoke(fire.Id + ".smoke", fire.Base + new Vector3(0, 0.75f * scale, 0), fire.Seed + 1000, scale) with
            {
                ElapsedSeconds = elapsed,
                Glow = new Vector3(1, 0.42f, 0.12f) * 2.6f,
            });
            if (fire.Embers)
            {
                emitters.Add(ParticleEmitter3D.Embers(fire.Id + ".embers", fire.Base + new Vector3(0, 0.15f * scale, 0), fire.Seed + 2000, scale)
                    with { ElapsedSeconds = elapsed });
            }
        }
        // The big fire also spits the odd fast spark.
        emitters.Add(ParticleEmitter3D.Sparks("campfire.sparks", HearthScene.Campfire.Base + new Vector3(0, 0.2f, 0), 3001) with
        {
            ElapsedSeconds = elapsed,
            Rate = 6,
        });
        return emitters;
    }

    /// <summary>
    /// A warm point light per fire. Flicker is a sum of incommensurate sines, so it never visibly repeats, and it is a
    /// pure function of time, so it replays exactly.
    /// </summary>
    public static IReadOnlyList<LocalLight3D> Lights(IEnumerable<FireSite> fires, double time)
    {
        var lights = new List<LocalLight3D>();
        foreach (FireSite fire in fires)
        {
            float t = (float)(time % ClockWrap) + fire.Seed * 0.37f;
            float flicker = 1
                + 0.10f * MathF.Sin(t * 7.3f)
                + 0.07f * MathF.Sin(t * 13.1f + 1.7f)
                + 0.05f * MathF.Sin(t * 23.7f + 0.4f)
                + 0.04f * MathF.Sin(t * 41.9f + 2.9f);
            var wobble = new Vector3(MathF.Sin(t * 5.1f), 0, MathF.Cos(t * 4.3f)) * 0.04f * fire.Scale;
            Vector3 position = fire.Base + fire.LightOffset * MathF.Max(fire.Scale, 0.6f) + wobble;
            lights.Add(new LocalLight3D(position, new Vector3(1, 0.52f, 0.2f), 7 * fire.Scale * flicker, 7 * MathF.Sqrt(fire.Scale) + 1));
        }
        return lights;
    }
}
