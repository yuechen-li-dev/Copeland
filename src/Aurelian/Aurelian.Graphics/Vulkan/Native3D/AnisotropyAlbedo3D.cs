using System.Buffers.Binary;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class AnisotropyAlbedo3D
{
    public const int Width = 41 * 9;
    public const int Height = 25 * 25;
    private static readonly Lazy<float[]> coefficients = new(ReadCoefficients);
    public static ReadOnlySpan<float> Coefficients => coefficients.Value;

    private static float[] ReadCoefficients()
    {
        using var stream = typeof(AnisotropyAlbedo3D).Assembly.GetManifestResourceStream("Aurelian.Graphics.AnisotropyDfg")
            ?? throw new InvalidDataException("The baked anisotropic directional albedo is missing.");
        var bytes = new byte[Width * Height * 2 * sizeof(float)];
        stream.ReadExactly(bytes);
        var values = new float[Width * Height * 2];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float)));
        }
        return values;
    }

    public static float[] TextureData()
    {
        var pixels = new float[Width * Height * 4];
        var source = Coefficients;
        for (int index = 0; index < Width * Height; index++)
        {
            pixels[index * 4] = source[index * 2];
            pixels[index * 4 + 1] = source[index * 2 + 1];
        }
        return pixels;
    }

    public static Vector4 Pack(SpecularAnisotropy3D? anisotropy)
    {
        if (anisotropy is not { Active: true }) return Vector4.Zero;
        var (sine, cosine) = MathF.SinCos(anisotropy.RotationRadians);
        return new(cosine, sine, anisotropy.Strength, anisotropy.Convention == AnisotropyConvention3D.Gltf ? 1 : 0);
    }
}
