using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Aurelian.Inference;

public enum GgufTensorType : uint
{
    Float32 = 0, Float16 = 1, Q4_0 = 2, Q6_K = 14,
}

/// <summary>Read-only GGUF v3 tensor index. Tensor rows are contiguous in file order.</summary>
public sealed class GgufCheckpoint : IDisposable
{
    private readonly FileStream file;
    private readonly Dictionary<string, Tensor> tensors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> metadata = new(StringComparer.Ordinal);

    private sealed record Tensor(int[] Dimensions, GgufTensorType Type, long Offset, long Bytes);

    public GgufCheckpoint(string path)
    {
        file = File.OpenRead(path);
        try
        {
            using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() != 3)
            {
                throw new InvalidDataException("Expected a little-endian GGUF v3 checkpoint.");
            }
            ulong tensorCount = reader.ReadUInt64();
            ulong metadataCount = reader.ReadUInt64();
            if (tensorCount is 0 or > 32768 || metadataCount > 8192)
            {
                throw new InvalidDataException("GGUF header exceeds the tensor or metadata budget.");
            }
            for (ulong index = 0; index < metadataCount; index++)
            {
                string name = ReadString(reader);
                uint type = reader.ReadUInt32();
                object? value = type switch
                {
                    4 => reader.ReadUInt32(),
                    6 => reader.ReadSingle(),
                    8 => ReadString(reader),
                    9 => ReadArray(reader, name),
                    _ => SkipScalar(reader, type),
                };
                if (value is not null && !metadata.TryAdd(name, value))
                {
                    throw new InvalidDataException("Duplicate GGUF metadata: " + name);
                }
            }

            uint alignment = metadata.TryGetValue("general.alignment", out var declared) && declared is uint valueAlignment
                ? valueAlignment : 32;
            if (alignment is 0 or > 65536 || (alignment & (alignment - 1)) != 0)
            {
                throw new InvalidDataException("Invalid GGUF tensor alignment.");
            }
            for (ulong index = 0; index < tensorCount; index++)
            {
                string name = ReadString(reader);
                uint rank = reader.ReadUInt32();
                if (rank is 0 or > 4) { throw new InvalidDataException("Invalid GGUF tensor rank: " + name); }
                var dimensions = new int[rank];
                for (int axis = 0; axis < dimensions.Length; axis++)
                {
                    dimensions[axis] = checked((int)reader.ReadUInt64());
                    if (dimensions[axis] <= 0) { throw new InvalidDataException("Empty GGUF tensor: " + name); }
                }
                var type = (GgufTensorType)reader.ReadUInt32();
                long offset = checked((long)reader.ReadUInt64());
                long bytes = RowBytes(type, dimensions[0]);
                for (int axis = 1; axis < dimensions.Length; axis++) { bytes = checked(bytes * dimensions[axis]); }
                if ((offset & (alignment - 1)) != 0 || !tensors.TryAdd(name, new(dimensions, type, offset, bytes)))
                {
                    throw new InvalidDataException("Misaligned or duplicate GGUF tensor: " + name);
                }
            }

            long dataStart = checked((file.Position + alignment - 1) & -(long)alignment);
            long previousEnd = dataStart;
            foreach (var entry in tensors.OrderBy(entry => entry.Value.Offset).ToArray())
            {
                Tensor tensor = entry.Value;
                long absolute = checked(dataStart + tensor.Offset);
                if (absolute < previousEnd || tensor.Bytes > file.Length - absolute)
                {
                    throw new InvalidDataException("Overlapping or out-of-bounds GGUF tensor: " + entry.Key);
                }
                previousEnd = absolute + tensor.Bytes;
                tensors[entry.Key] = tensor with { Offset = absolute };
            }
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public T Metadata<T>(string name) => metadata.TryGetValue(name, out var value) && value is T typed
        ? typed : throw new InvalidDataException("Missing or invalid GGUF metadata: " + name);

    public void RequireTensor(string name, GgufTensorType type, params int[] dimensions)
    {
        if (!tensors.TryGetValue(name, out Tensor? tensor) || tensor.Type != type
            || !tensor.Dimensions.AsSpan().SequenceEqual(dimensions))
        {
            throw new InvalidDataException("GGUF tensor differs from the model profile: " + name);
        }
    }

    public void ReadRows(string name, int first, int count, Span<byte> destination)
    {
        Tensor tensor = tensors[name];
        int rowBytes = RowBytes(tensor.Type, tensor.Dimensions[0]);
        if (first < 0 || count <= 0 || destination.Length != (long)count * rowBytes
            || ((long)first + count) * rowBytes > tensor.Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Requested rows must fit the indexed tensor and destination.");
        }
        file.Position = tensor.Offset + (long)first * rowBytes;
        file.ReadExactly(destination);
    }

    public static int RowBytes(GgufTensorType type, int columns) => type switch
    {
        GgufTensorType.Float32 => checked(columns * 4),
        GgufTensorType.Float16 => checked(columns * 2),
        GgufTensorType.Q4_0 when columns % 32 == 0 => checked(columns / 32 * 18),
        GgufTensorType.Q6_K when columns % 256 == 0 => checked(columns / 256 * 210),
        _ => throw new InvalidDataException("Unsupported GGUF tensor type or block alignment: " + type),
    };

    private static string ReadString(BinaryReader reader)
    {
        ulong length = reader.ReadUInt64();
        if (length > 2 * 1024 * 1024) { throw new InvalidDataException("Oversized GGUF string."); }
        byte[] bytes = reader.ReadBytes((int)length);
        if ((ulong)bytes.Length != length) { throw new EndOfStreamException(); }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static object? ReadArray(BinaryReader reader, string name)
    {
        uint type = reader.ReadUInt32();
        ulong count = reader.ReadUInt64();
        if (count > 2 * 1024 * 1024) { throw new InvalidDataException("Oversized GGUF array: " + name); }
        if (type == 5 && name == "gemma4.feed_forward_length")
        {
            var values = new int[count];
            for (int index = 0; index < values.Length; index++) { values[index] = reader.ReadInt32(); }
            return values;
        }
        if (type == 7 && name == "gemma4.attention.sliding_window_pattern")
        {
            var values = new bool[count];
            for (int index = 0; index < values.Length; index++) { values[index] = reader.ReadBoolean(); }
            return values;
        }
        if (type == 8)
        {
            for (ulong index = 0; index < count; index++) { Skip(reader, reader.ReadUInt64()); }
        }
        else
        {
            Skip(reader, count * ScalarSize(type));
        }
        return null;
    }

    private static object? SkipScalar(BinaryReader reader, uint type)
    {
        Skip(reader, ScalarSize(type));
        return null;
    }

    private static uint ScalarSize(uint type) => type switch
    {
        0 or 1 or 7 => 1,
        2 or 3 => 2,
        4 or 5 or 6 => 4,
        10 or 11 or 12 => 8,
        _ => throw new InvalidDataException("Unsupported GGUF metadata type: " + type),
    };

    private static void Skip(BinaryReader reader, ulong bytes)
    {
        var stream = reader.BaseStream;
        if (bytes > (ulong)(stream.Length - stream.Position)) { throw new EndOfStreamException(); }
        stream.Position += (long)bytes;
    }

    public void Dispose() => file.Dispose();
}

public static class GgufBlocks
{
    /// <summary>Expand binary16 scales and reorder nibble planes without requantizing.</summary>
    public static void RepackQ4_0(ReadOnlySpan<byte> source, Span<uint> destination)
    {
        if (source.Length % 18 != 0 || destination.Length != source.Length / 18 * 5)
        {
            throw new ArgumentException("Repack requires complete Q4_0 and Q4B32 blocks.");
        }
        for (int block = 0; block < destination.Length; block += 5)
        {
            float scale = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source));
            destination[block] = BitConverter.SingleToUInt32Bits(scale);
            for (int group = 0; group < 2; group++)
            {
                ulong pairs = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(2 + group * 8, 8));
                destination[block + 1 + group] = CompactNibbles(pairs);
                destination[block + 3 + group] = CompactNibbles(pairs >> 4);
            }
            source = source[18..];
        }
    }

    private static uint CompactNibbles(ulong value)
    {
        value &= 0x0F0F0F0F0F0F0F0F;
        value = (value | value >> 4) & 0x00FF00FF00FF00FF;
        value = (value | value >> 8) & 0x0000FFFF0000FFFF;
        return (uint)(value | value >> 16);
    }

    public static void DecodeQ6_K(ReadOnlySpan<byte> source, Span<float> destination)
    {
        if (source.Length % 210 != 0 || destination.Length != source.Length / 210 * 256)
        {
            throw new ArgumentException("Decode requires complete Q6_K blocks.");
        }
        while (!source.IsEmpty)
        {
            float scale = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source[208..]));
            var scales = MemoryMarshal.Cast<byte, sbyte>(source.Slice(192, 16));
            for (int half = 0; half < 2; half++)
            {
                var low = source.Slice(half * 64, 64);
                var high = source.Slice(128 + half * 32, 32);
                var output = destination.Slice(half * 128, 128);
                for (int slice = 0; slice < 2; slice++)
                {
                    int index = half * 8 + slice;
                    float a = scale * scales[index];
                    float b = scale * scales[index + 2];
                    float c = scale * scales[index + 4];
                    float d = scale * scales[index + 6];
                    for (int lane = slice * 16; lane < (slice + 1) * 16; lane++)
                    {
                        int first = low[lane];
                        int second = low[lane + 32];
                        int upper = high[lane];
                        output[lane] = a * (((first & 15) | ((upper & 3) << 4)) - 32);
                        output[lane + 32] = b * (((second & 15) | ((upper & 12) << 2)) - 32);
                        output[lane + 64] = c * (((first >> 4) | (upper & 48)) - 32);
                        output[lane + 96] = d * (((second >> 4) | ((upper & 192) >> 2)) - 32);
                    }
                }
            }
            source = source[210..];
            destination = destination[256..];
        }
    }
}
