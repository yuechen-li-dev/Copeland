using System.Security.Cryptography;

namespace Machina.Fonts.Generation;

public static class GeneratedDistanceFieldFingerprint
{
    public static string ComputeSha256(GeneratedGlyphDistanceField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        foreach (float value in field.Data.Span)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes,
                BitConverter.SingleToInt32Bits(value));
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
