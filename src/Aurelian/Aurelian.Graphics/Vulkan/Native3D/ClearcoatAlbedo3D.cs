using System.Buffers.Binary;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class ClearcoatAlbedo3D
{
    public const int Size = 64;
    private static readonly Lazy<float[]> coefficients = new(ReadCoefficients);
    public static ReadOnlySpan<float> Coefficients => coefficients.Value;

    private static float[] ReadCoefficients()
    {
        using var stream = typeof(ClearcoatAlbedo3D).Assembly.GetManifestResourceStream("Aurelian.Graphics.ClearcoatDfg")
            ?? throw new InvalidDataException("The baked clearcoat directional albedo is missing.");
        var values = new float[Size * Size * 2];
        Span<byte> word = stackalloc byte[sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            stream.ReadExactly(word);
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(word);
        }
        return values;
    }

    public static float[] TextureData()
    {
        var pixels = new float[Size * Size * 4];
        var source = Coefficients;
        for (int index = 0; index < Size * Size; index++)
        {
            pixels[index * 4] = source[index * 2];
            pixels[index * 4 + 1] = source[index * 2 + 1];
        }
        return pixels;
    }

}
