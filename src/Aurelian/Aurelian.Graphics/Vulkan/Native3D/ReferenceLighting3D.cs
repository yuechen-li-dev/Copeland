using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Direction points toward the sun. Irradiance is the normal-incidence
/// directional-light coefficient, matching SunColor * SunIntensity in real-time settings.</summary>
public sealed record ReferenceSun3D(Vector3 Direction, Vector3 Irradiance);
public sealed record ReferenceLighting3D(ReferenceSun3D? Sun = null, EnvironmentRadiance3D? Environment = null,
    float EnvironmentIntensity = 1, bool ImportanceSampleEnvironment = true, bool ExcludePrimarySun = false)
{
    public static ReferenceLighting3D FromSettings(Graphics3DSettings settings, EnvironmentRadiance3D? environment = null)
    {
        settings.Validate();
        return new(new(settings.SunDirection, settings.SunColor * settings.SunIntensity), environment, settings.EnvironmentIntensity);
    }

    internal float[] Distribution()
    {
        if (Environment is null) return [];
        int width = Math.Min(Environment.Width, 64);
        int height = Math.Min(Environment.Height, 32);
        double[] weights = new double[width * height];
        for (int y = 0; y < Environment.Height; y++)
        {
            double solidAngle = Math.Tau / Environment.Width * (Math.Cos(Math.PI * y / Environment.Height)
                - Math.Cos(Math.PI * (y + 1) / Environment.Height));
            for (int x = 0; x < Environment.Width; x++)
            {
                int pixel = (y * Environment.Width + x) * 4;
                double luminance = Environment.Rgba[pixel] * .2126 + Environment.Rgba[pixel + 1] * .7152 + Environment.Rgba[pixel + 2] * .0722;
                int bin = (y * height / Environment.Height) * width + x * width / Environment.Width;
                weights[bin] += luminance * solidAngle;
            }
        }
        double sum = weights.Sum();
        float[] cdf = new float[weights.Length];
        double cumulative = 0;
        for (int index = 0; index < cdf.Length; index++)
        {
            cumulative += sum > 0 ? weights[index] / sum : 1.0 / weights.Length;
            cdf[index] = (float)Math.Min(cumulative, 1);
        }
        cdf[^1] = 1;
        return cdf;
    }
}
