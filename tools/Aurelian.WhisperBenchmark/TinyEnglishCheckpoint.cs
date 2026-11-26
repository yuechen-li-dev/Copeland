using System.Runtime.InteropServices;
using System.Text;

namespace Aurelian.WhisperBenchmark;

// The legacy ggml Tiny.en F16 profile is shared with the reference executable.
// Shapes below are in ggml order (fastest axis first).
internal sealed class TinyEnglishCheckpoint : IDisposable
{
    public const int Vocabulary = 51864;
    public const int EndOfText = 50256;
    public const int StartOfText = 50257;
    public const int NoTimestamps = 50362;
    public const int Width = 384;
    public const int Heads = 6;
    public const int HeadWidth = Width / Heads;
    public const int Layers = 4;
    public const int AudioRows = 1500;
    public const int MelBins = 80;
    public const int TextRows = 448;

    private sealed record Tensor(long Offset, int Type, int[] Shape, int Elements);
    private readonly Dictionary<string, Tensor> tensors = new(StringComparer.Ordinal);
    private readonly byte[][] vocabulary = new byte[EndOfText + 1][];
    private readonly BinaryReader reader;

    public TinyEnglishCheckpoint(string path)
    {
        reader = new BinaryReader(File.OpenRead(path));
        try
        {
            ReadIndex();
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private void ReadIndex()
    {
        int[] expected = [0x67676d6c, Vocabulary, AudioRows, Width, Heads, Layers,
            TextRows, Width, Heads, Layers, MelBins, 1, MelBins, 201];
        foreach (int value in expected)
        {
            if (reader.ReadInt32() != value)
            {
                throw new InvalidDataException("Expected a little-endian ggml Tiny.en F16 checkpoint.");
            }
        }
        Stream stream = reader.BaseStream;
        stream.Position += MelBins * 201 * sizeof(float);
        if (reader.ReadInt32() != vocabulary.Length)
        {
            throw new InvalidDataException("Unexpected Tiny.en vocabulary size.");
        }
        for (int token = 0; token < vocabulary.Length; token++)
        {
            int length = reader.ReadInt32();
            if (length is < 0 or > 1024)
            {
                throw new InvalidDataException("Unexpected Tiny.en vocabulary entry size.");
            }
            vocabulary[token] = new byte[length];
            stream.ReadExactly(vocabulary[token]);
        }
        while (stream.Position < stream.Length)
        {
            int rank = reader.ReadInt32();
            int nameLength = reader.ReadInt32();
            int type = reader.ReadInt32();
            if (rank is < 1 or > 3 || nameLength is < 1 or > 128 || type is < 0 or > 1)
            {
                throw new InvalidDataException("Unexpected Tiny.en tensor header.");
            }
            var shape = new int[rank];
            int elements = 1;
            for (int axis = 0; axis < rank; axis++)
            {
                shape[axis] = reader.ReadInt32();
                if (shape[axis] is < 1 or > Vocabulary)
                {
                    throw new InvalidDataException("Unexpected Tiny.en tensor dimension.");
                }
                elements = checked(elements * shape[axis]);
            }
            var nameBytes = new byte[nameLength];
            stream.ReadExactly(nameBytes);
            string name = Encoding.ASCII.GetString(nameBytes);
            var tensor = new Tensor(stream.Position, type, shape, elements);
            stream.Position += (long)elements * (type == 0 ? sizeof(float) : sizeof(ushort));
            if (stream.Position > stream.Length || !tensors.TryAdd(name, tensor))
            {
                throw new InvalidDataException($"Duplicate or truncated tensor {name}.");
            }
        }
    }

    public float[] Read(string name, params int[] shape)
    {
        if (!tensors.TryGetValue(name, out Tensor? tensor) || !shape.SequenceEqual(tensor.Shape))
        {
            throw new InvalidDataException($"Tiny.en tensor {name} is missing or has an unexpected shape.");
        }
        Stream stream = reader.BaseStream;
        stream.Position = tensor.Offset;
        var values = new float[tensor.Elements];
        if (tensor.Type == 0)
        {
            stream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        else
        {
            var halves = new Half[tensor.Elements];
            stream.ReadExactly(MemoryMarshal.AsBytes(halves.AsSpan()));
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = (float)halves[index];
            }
        }
        return values;
    }

    public string Decode(IEnumerable<int> tokens)
    {
        return Encoding.UTF8.GetString(tokens.Where(token => token < EndOfText)
            .SelectMany(token => vocabulary[token]).ToArray());
    }

    public void Dispose() => reader.Dispose();
}
