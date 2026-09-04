using System.Buffers.Binary;

namespace Machina.Fonts.Generation.Typography;

internal static class TrueTypeHorizontalMetricsReader
{
    public static ushort ReadAdvanceWidth(string path, int faceIndex, ushort glyphIndex)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int faceOffset = ResolveFaceOffset(bytes, faceIndex);
        int hheaOffset = FindTable(bytes, faceOffset, "hhea");
        int hmtxOffset = FindTable(bytes, faceOffset, "hmtx");
        ushort horizontalMetricCount = ReadUInt16(bytes, checked(hheaOffset + 34));
        if (horizontalMetricCount == 0)
        {
            throw new InvalidDataException("The TrueType hhea table declares no horizontal metrics.");
        }

        int metricIndex = Math.Min(glyphIndex, horizontalMetricCount - 1);
        return ReadUInt16(bytes, checked(hmtxOffset + (metricIndex * 4)));
    }

    private static int ResolveFaceOffset(byte[] bytes, int faceIndex)
    {
        if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("ttcf"u8))
        {
            uint faceCount = ReadUInt32(bytes, 8);
            if (faceIndex < 0 || faceIndex >= faceCount)
            {
                throw new InvalidDataException($"Face index {faceIndex} is outside the TrueType collection.");
            }

            return checked((int)ReadUInt32(bytes, 12 + (faceIndex * 4)));
        }

        if (faceIndex != 0)
        {
            throw new InvalidDataException("A standalone TrueType font only has face index zero.");
        }

        return 0;
    }

    private static int FindTable(byte[] bytes, int faceOffset, string expectedTag)
    {
        ushort tableCount = ReadUInt16(bytes, checked(faceOffset + 4));
        int recordOffset = checked(faceOffset + 12);
        for (int index = 0; index < tableCount; index++)
        {
            int current = checked(recordOffset + (index * 16));
            string tag = System.Text.Encoding.ASCII.GetString(bytes, current, 4);
            if (string.Equals(tag, expectedTag, StringComparison.Ordinal))
            {
                return checked((int)ReadUInt32(bytes, current + 8));
            }
        }

        throw new InvalidDataException($"The TrueType font has no '{expectedTag}' table.");
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 2 > bytes.Length)
        {
            throw new InvalidDataException("TrueType table data is truncated.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
        {
            throw new InvalidDataException("TrueType table data is truncated.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
    }
}
