using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class OceanSpectrum
{
    public const int Resolution = 64;

    // Unnormalized inverse FFT coefficients. Seed and normalize the directional
    // Phillips spectrum once, then let Vulkan evolve the phases in place.
    public static Vector2[] Create(OceanSpectrum3D ocean)
    {
        ocean.Validate();
        var spectrum = new Vector2[Resolution * Resolution];
        double windLength = ocean.Wind.LengthSquared() / 9.81;
        double shortWaveLength = ocean.TileLength / Resolution * .9;
        Vector2 wind = Vector2.Normalize(ocean.Wind);
        var logPower = new double[spectrum.Length];
        Array.Fill(logPower, double.NegativeInfinity);
        double peak = double.NegativeInfinity;
        int seed = ocean.Seed;
        for (int z = 0; z < Resolution; z++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                // Omit DC and the aliased signed Nyquist axes.
                if (x == Resolution / 2 || z == Resolution / 2 || (x == 0 && z == 0))
                    continue;
                int index = z * Resolution + x;
                double kx = Math.Tau * Signed(x) / ocean.TileLength;
                double kz = Math.Tau * Signed(z) / ocean.TileLength;
                double k2 = kx * kx + kz * kz;
                double direction = (kx * wind.X + kz * wind.Y) / Math.Sqrt(k2);
                double spread = (.12 + .88 * direction * direction) * (direction < 0 ? .15 : 1);
                logPower[index] = Math.Log(spread) - 1 / (k2 * windLength * windLength)
                    - k2 * shortWaveLength * shortWaveLength - 2 * Math.Log(k2);
                peak = Math.Max(peak, logPower[index]);
            }
        }
        double variance = 0;
        for (int index = 0; index < spectrum.Length; index++)
        {
            double power = Math.Exp(logPower[index] - peak);
            double radius = Math.Sqrt(-power * Math.Log(Uniform(seed, index * 2)));
            double angle = Math.Tau * Uniform(seed, index * 2 + 1);
            spectrum[index] = new((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
            variance += 2 * spectrum[index].LengthSquared();
        }
        float scale = (float)(ocean.HeightDeviation / Math.Sqrt(variance));
        for (int i = 0; i < spectrum.Length; i++)
            spectrum[i] *= scale;
        return spectrum;
    }

    private static int Signed(int value) => value < Resolution / 2 ? value : value - Resolution;

    private static double Uniform(int seed, int index)
    {
        uint value = unchecked((uint)seed + 0x9e3779b9u * (uint)(index + 1));
        value = unchecked((value ^ (value >> 16)) * 0x7feb352du);
        value = unchecked((value ^ (value >> 15)) * 0x846ca68bu);
        value ^= value >> 16;
        return (value + .5) / 4294967296.0;
    }
}
