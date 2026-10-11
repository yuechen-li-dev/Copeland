using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Aurelian.Assets.Tensors;

public sealed record SafeTensorInfo(string Name, string DataType, IReadOnlyList<int> Shape,
    long Offset, long ByteLength);

/// <summary>Bounded local safetensors ingress. Reads selected rows, never deserializes
/// executable objects or loads an entire checkpoint. Supports F32, F16 and BF16.</summary>
public sealed class SafeTensorFile : IDisposable
{
    private const int MaximumHeaderBytes = 4 * 1024 * 1024;
    private const int MaximumReadElements = 1048576;
    private readonly FileStream stream;
    private readonly long dataStart;
    private bool disposed;

    public IReadOnlyDictionary<string, SafeTensorInfo> Tensors { get; }

    public SafeTensorFile(string path)
    {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> prefix = stackalloc byte[8];
            stream.ReadExactly(prefix);
            ulong headerLength = BinaryPrimitives.ReadUInt64LittleEndian(prefix);
            Require(headerLength is > 0 and <= MaximumHeaderBytes && headerLength <= (ulong)Math.Max(0, stream.Length - 8),
                "Header length is invalid, truncated or exceeds 4 MiB.");
            byte[] header = new byte[(int)headerLength];
            stream.ReadExactly(header);
            dataStart = 8 + (long)headerLength;
            using var document = JsonDocument.Parse(header);
            Require(document.RootElement.ValueKind == JsonValueKind.Object, "Header must be an object.");
            var tensors = new Dictionary<string, SafeTensorInfo>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate tensor/header key: " + property.Name);
                if (property.Name == "__metadata__")
                {
                    Require(property.Value.ValueKind == JsonValueKind.Object
                        && property.Value.EnumerateObject().All(item => item.Value.ValueKind == JsonValueKind.String),
                        "Metadata must map names to strings.");
                    continue;
                }
                var value = property.Value;
                string type = value.GetProperty("dtype").GetString() ?? "";
                int bytesPerElement = ElementBytes(type);
                int[] shape = value.GetProperty("shape").EnumerateArray().Select(item => item.GetInt32()).ToArray();
                Require(shape.Length is >= 1 and <= 4 && shape.All(dimension => dimension > 0),
                    "Only positive rank 1–4 tensors are supported: " + property.Name);
                long elements = 1;
                foreach (int dimension in shape)
                {
                    elements = checked(elements * dimension);
                }
                long[] offsets = value.GetProperty("data_offsets").EnumerateArray().Select(item => item.GetInt64()).ToArray();
                Require(offsets.Length == 2 && offsets[0] >= 0 && offsets[1] >= offsets[0]
                    && offsets[1] - offsets[0] == checked(elements * bytesPerElement)
                    && offsets[1] <= stream.Length - dataStart, "Invalid tensor byte range: " + property.Name);
                tensors.Add(property.Name, new(property.Name, type, Array.AsReadOnly(shape), offsets[0], offsets[1] - offsets[0]));
            }
            long previousEnd = 0;
            foreach (var tensor in tensors.Values.OrderBy(item => item.Offset))
            {
                Require(tensor.Offset == previousEnd, "Overlapping or noncontiguous tensor data: " + tensor.Name);
                previousEnd += tensor.ByteLength;
            }
            Require(previousEnd == stream.Length - dataStart, "Unindexed bytes at end of checkpoint.");
            Tensors = new ReadOnlyDictionary<string, SafeTensorInfo>(tensors);
        }
        catch (Exception exception)
        {
            stream.Dispose();
            if (exception is InvalidDataException && exception.Message.StartsWith("AUR-TENSOR-001:", StringComparison.Ordinal))
            {
                throw;
            }
            throw new InvalidDataException("AUR-TENSOR-001: Invalid safetensors header or file.", exception);
        }
    }

    public float[] ReadVector(string name)
    {
        var tensor = Get(name);
        Require(tensor.Shape.Count == 1, "ReadVector requires rank 1: " + name);
        return Read(tensor, 0, tensor.Shape[0]);
    }

    public float[] ReadRows(string name, int firstRow, int rowCount)
    {
        var tensor = Get(name);
        Require(tensor.Shape.Count == 2 && firstRow >= 0 && rowCount > 0
            && (long)firstRow + rowCount <= tensor.Shape[0], "Row range or rank is invalid: " + name);
        return Read(tensor, (long)firstRow * tensor.Shape[1], (long)rowCount * tensor.Shape[1]);
    }

    /// <summary>Read contiguous outer-axis slices from a rank 1–4 tensor in its
    /// stored C order. Inner axes are flattened, without transposition. Useful
    /// for selected convolution output channels and learned token tensors.</summary>
    public float[] ReadSlices(string name, int firstSlice, int sliceCount)
    {
        var tensor = Get(name);
        Require(firstSlice >= 0 && sliceCount > 0 && (long)firstSlice + sliceCount <= tensor.Shape[0],
            "Outer-axis slice range is invalid: " + name);
        long sliceElements = 1;
        foreach (int dimension in tensor.Shape.Skip(1))
        {
            sliceElements = checked(sliceElements * dimension);
        }
        return Read(tensor, firstSlice * sliceElements, sliceCount * sliceElements);
    }

    private SafeTensorInfo Get(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Require(Tensors.ContainsKey(name), "Missing tensor: " + name);
        return Tensors[name];
    }

    private float[] Read(SafeTensorInfo tensor, long first, long count)
    {
        Require(count is > 0 and <= MaximumReadElements, "A selected read may contain at most 1048576 elements.");
        int bytesPerElement = ElementBytes(tensor.DataType);
        byte[] bytes = new byte[checked((int)count * bytesPerElement)];
        stream.Position = dataStart + tensor.Offset + first * bytesPerElement;
        stream.ReadExactly(bytes);
        float[] values = new float[(int)count];
        for (int index = 0; index < values.Length; index++)
        {
            var word = bytes.AsSpan(index * bytesPerElement, bytesPerElement);
            values[index] = tensor.DataType switch
            {
                "F32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(word)),
                "F16" => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(word)),
                "BF16" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadUInt16LittleEndian(word) << 16),
                _ => throw new InvalidDataException("AUR-TENSOR-001: Unsupported encoding."),
            };
            Require(float.IsFinite(values[index]), "Nonfinite tensor value: " + tensor.Name);
        }
        return values;
    }

    private static int ElementBytes(string type) => type switch
    {
        "F32" => 4,
        "F16" or "BF16" => 2,
        _ => throw new InvalidDataException("AUR-TENSOR-001: Unsupported tensor dtype: " + type),
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException("AUR-TENSOR-001: " + message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        stream.Dispose();
    }
}
