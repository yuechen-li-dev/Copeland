using System.Runtime.InteropServices;
using System.Text;

namespace Aurelian.Gpt2Benchmark;

// Only the benchmark's GGUF v3 GPT-2 F32 tensor layout.
internal sealed class Gpt2Checkpoint : IDisposable
{
    private sealed record Tensor(long Offset, int[] Shape, int Elements);
    private readonly BinaryReader reader;
    private readonly Dictionary<string, Tensor> tensors = new(StringComparer.Ordinal);
    private readonly long dataStart;

    public Gpt2Checkpoint(string path)
    {
        reader = new BinaryReader(File.OpenRead(path));
        try
        {
            dataStart = ReadIndex();
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private long ReadIndex()
    {
        if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() != 3)
        {
            throw new InvalidDataException("Expected little-endian GGUF v3.");
        }
        ulong tensorCount = reader.ReadUInt64();
        ulong metadataCount = reader.ReadUInt64();
        if (tensorCount is 0 or > 150 || metadataCount is 0 or > 256)
        {
            throw new InvalidDataException("Invalid GPT-2 index size.");
        }
        string? architecture = null;
        uint alignment = 32;
        for (ulong index = 0; index < metadataCount; index++)
        {
            string key = ReadString();
            uint kind = reader.ReadUInt32();
            if (key == "general.architecture" && kind == 8)
            {
                architecture = ReadString();
            }
            else if (key == "general.alignment" && kind == 4)
            {
                alignment = reader.ReadUInt32();
            }
            else
            {
                SkipMetadata(kind);
            }
        }
        if (architecture != "gpt2" || alignment is 0 or > 4096 || (alignment & (alignment - 1)) != 0)
        {
            throw new InvalidDataException("Expected GPT-2 with a valid tensor alignment.");
        }
        for (ulong index = 0; index < tensorCount; index++)
        {
            string name = ReadString();
            uint rank = reader.ReadUInt32();
            if (rank is < 1 or > 2)
            {
                throw new InvalidDataException($"Unexpected tensor rank for {name}.");
            }
            var shape = new int[rank];
            int elements = 1;
            for (int axis = 0; axis < shape.Length; axis++)
            {
                shape[axis] = checked((int)reader.ReadUInt64());
                elements = checked(elements * shape[axis]);
            }
            uint type = reader.ReadUInt32();
            long offset = checked((long)reader.ReadUInt64());
            if (type != 0 || elements is < 1 or > 50257 * 768 || offset % alignment != 0
                || !tensors.TryAdd(name, new(offset, shape, elements)))
            {
                throw new InvalidDataException($"Invalid F32 GPT-2 tensor {name}.");
            }
        }
        long position = reader.BaseStream.Position;
        return (position + alignment - 1) / alignment * alignment;
    }

    private string ReadString()
    {
        ulong length = reader.ReadUInt64();
        if (length > 4096)
        {
            throw new InvalidDataException("GPT-2 index string is too large.");
        }
        var bytes = new byte[length];
        reader.BaseStream.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private void SkipMetadata(uint kind)
    {
        if (kind == 9)
        {
            uint elementType = reader.ReadUInt32();
            ulong count = reader.ReadUInt64();
            if (elementType == 9 || count > 1000000)
            {
                throw new InvalidDataException("Unexpected GGUF metadata array.");
            }
            for (ulong index = 0; index < count; index++)
            {
                SkipMetadata(elementType);
            }
            return;
        }
        ulong length = kind switch
        {
            0 or 1 or 7 => 1,
            2 or 3 => 2,
            4 or 5 or 6 => 4,
            8 => reader.ReadUInt64(),
            10 or 11 or 12 => 8,
            _ => throw new InvalidDataException("Unknown GGUF metadata type."),
        };
        Stream stream = reader.BaseStream;
        if (length > (ulong)(stream.Length - stream.Position))
        {
            throw new EndOfStreamException("Truncated GGUF metadata.");
        }
        stream.Position += (long)length;
    }

    public float[] Read(string name, params int[] shape)
    {
        if (!tensors.TryGetValue(name, out Tensor? tensor) || !shape.SequenceEqual(tensor.Shape))
        {
            throw new InvalidDataException($"Unexpected shape or missing GPT-2 tensor {name}.");
        }
        reader.BaseStream.Position = checked(dataStart + tensor.Offset);
        var weights = new float[tensor.Elements];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(weights.AsSpan()));
        return weights;
    }

    public void Dispose() => reader.Dispose();
}
