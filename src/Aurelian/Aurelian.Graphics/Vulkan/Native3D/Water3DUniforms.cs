using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class Water3DUniforms
{
    public static float[] Profile(WaterSurface3D? water)
    {
        if (water is null)
        {
            return new float[12];
        }
        return [1, water.DetailAmplitude, water.DetailWavelength, water.MaximumDepth,
            water.FoamWidth, water.FoamStrength, (int)water.DebugView, 0, water.DeepColor.X, water.DeepColor.Y, water.DeepColor.Z, 0];
    }

    public static float[] Waves(WaterSurface3D? water)
    {
        var values = new float[32];
        for (int index = 0; index < 4; index++)
        {
            values[index * 8 + 3] = 1;
            if (water is null || index >= water.Waves.Length)
            {
                continue;
            }
            var wave = water.Waves[index];
            Vector2 direction = Vector2.Normalize(wave.Direction);
            int first = index * 8;
            values[first] = direction.X;
            values[first + 1] = direction.Y;
            values[first + 2] = wave.Amplitude;
            values[first + 3] = wave.Wavelength;
            values[first + 4] = wave.Speed;
            values[first + 5] = wave.Steepness;
        }
        return values;
    }

    public static float[] Frame(WaterFrame3D frame, float previousTime, Matrix4x4 previousClip,
        Matrix4x4 reflectionClip, bool reflection)
    {
        var impulses = new float[64];
        for (int index = 0; index < frame.Impulses.Length; index++)
        {
            var impulse = frame.Impulses[index];
            int first = index * 8;
            impulses[first] = impulse.Center.X;
            impulses[first + 1] = impulse.Center.Y;
            impulses[first + 2] = impulse.StartedAt;
            impulses[first + 3] = impulse.Amplitude;
            impulses[first + 4] = impulse.Wavelength;
            impulses[first + 5] = impulse.Speed;
            impulses[first + 6] = impulse.Lifetime;
        }
        return [frame.Time, previousTime, 0, reflection ? 1 : 0,
            .. Lighting3DUniforms.Rows(previousClip), .. Lighting3DUniforms.Rows(reflectionClip), .. impulses];
    }
}
