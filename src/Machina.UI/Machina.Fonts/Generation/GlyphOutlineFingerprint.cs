using System.Security.Cryptography;

namespace Machina.Fonts.Generation;

public static class GlyphOutlineFingerprint
{
    public static string ComputeSha256(GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, outline.Contours.Count);

        foreach (GlyphContour contour in outline.Contours)
        {
            AppendInt32(hash, contour.Segments.Count);
            foreach (GlyphOutlineSegment segment in contour.Segments)
            {
                switch (segment)
                {
                    case GlyphLineSegment line:
                        AppendByte(hash, 1);
                        AppendPoint(hash, line.P0);
                        AppendPoint(hash, line.P1);
                        break;
                    case GlyphQuadraticSegment quadratic:
                        AppendByte(hash, 2);
                        AppendPoint(hash, quadratic.P0);
                        AppendPoint(hash, quadratic.P1);
                        AppendPoint(hash, quadratic.P2);
                        break;
                    case GlyphCubicSegment cubic:
                        AppendByte(hash, 3);
                        AppendPoint(hash, cubic.P0);
                        AppendPoint(hash, cubic.P1);
                        AppendPoint(hash, cubic.P2);
                        AppendPoint(hash, cubic.P3);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported outline segment '{segment.GetType().Name}'.");
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendPoint(IncrementalHash hash, GlyphPoint point)
    {
        AppendInt64(hash, BitConverter.DoubleToInt64Bits(point.X));
        AppendInt64(hash, BitConverter.DoubleToInt64Bits(point.Y));
    }

    private static void AppendByte(IncrementalHash hash, byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
