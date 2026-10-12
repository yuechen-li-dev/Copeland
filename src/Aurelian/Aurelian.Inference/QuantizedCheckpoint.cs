using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;

namespace Aurelian.Inference;

public sealed record QuantizedMatrix(string Name, int Rows, int Columns, long Offset, long ByteLength);
public sealed record QuantizedManifest(int Version, string Encoding, string SourceSha256,
    long DataBytes, IReadOnlyList<QuantizedMatrix> Matrices);

/// <summary>Explicit offline PTQ artifact. Runtime reads packed rows without expanding them.
/// A manifest is published only after the complete data file is closed.</summary>
public sealed class QuantizedCheckpoint : IDisposable
{
    private readonly FileStream stream;
    public QuantizedManifest Manifest { get; }
    public IReadOnlyDictionary<string, QuantizedMatrix> Matrices { get; }

    public QuantizedCheckpoint(string directory)
    {
        Manifest = JsonSerializer.Deserialize<QuantizedManifest>(File.ReadAllText(Path.Combine(directory, "q4.json")))
            ?? throw new InvalidDataException("AUR-Q4-002: Missing manifest.");
        if (Manifest.Version != 1 || Manifest.Encoding != "Aurelian.Q4B32.F32Scale"
            || Manifest.Matrices.Count is < 1 or > 4096 || Manifest.SourceSha256.Length != 64
            || Manifest.SourceSha256.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidDataException("AUR-Q4-002: Unsupported quantized manifest.");
        }
        long end = 0;
        var matrices = new Dictionary<string, QuantizedMatrix>(StringComparer.Ordinal);
        foreach (var matrix in Manifest.Matrices)
        {
            if (string.IsNullOrWhiteSpace(matrix.Name) || matrix.Rows is < 1 or > 1048576
                || matrix.Columns is < 32 or > 32768 || matrix.Columns % 32 != 0
                || matrix.Offset != end || matrix.ByteLength != (long)matrix.Rows * matrix.Columns / 32 * 20
                || !matrices.TryAdd(matrix.Name, matrix))
            {
                throw new InvalidDataException("AUR-Q4-002: Invalid matrix/range: " + matrix.Name);
            }
            end = checked(end + matrix.ByteLength);
        }
        if (end != Manifest.DataBytes)
        {
            throw new InvalidDataException("AUR-Q4-002: Data length differs from manifest.");
        }
        stream = new FileStream(Path.Combine(directory, "q4.bin"), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != end)
        {
            stream.Dispose();
            throw new InvalidDataException("AUR-Q4-002: Truncated or unindexed quantized data.");
        }
        Matrices = matrices;
    }

    public uint[] ReadRows(string name, int first, int count)
    {
        var matrix = Matrices[name];
        long words = (long)count * matrix.Columns / 32 * 5;
        if (first < 0 || count <= 0 || (long)first + count > matrix.Rows || words > 33554432)
        {
            throw new InvalidDataException("AUR-Q4-003: Packed read exceeds its row/range budget.");
        }
        var result = new uint[(int)words];
        ReadRows(name, first, count, result);
        return result;
    }

    /// <summary>Reads and validates into caller-owned reusable storage. No hidden row cache.</summary>
    public void ReadRows(string name, int first, int count, Span<uint> destination)
    {
        var matrix = Matrices[name];
        long words = (long)count * matrix.Columns / 32 * 5;
        if (first < 0 || count <= 0 || (long)first + count > matrix.Rows
            || words > 33554432 || destination.Length != words)
        {
            throw new InvalidDataException("AUR-Q4-003: Packed read exceeds its row/range budget.");
        }
        stream.Position = matrix.Offset + (long)first * matrix.Columns / 32 * 20;
        stream.ReadExactly(MemoryMarshal.AsBytes(destination));
        for (int offset = 0; offset < destination.Length; offset += BlockQ4.WordsPerBlock)
        {
            if (!float.IsFinite(BitConverter.UInt32BitsToSingle(destination[offset])))
            {
                throw new InvalidDataException("AUR-Q4-003: Nonfinite packed scale.");
            }
        }
    }

    public void ValidateSource(string path)
    {
        using var source = File.OpenRead(path);
        string hash = Convert.ToHexString(SHA256.HashData(source));
        if (!string.Equals(hash, Manifest.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("AUR-Q4-006: Quantized artifact belongs to a different source checkpoint.");
        }
    }

    public static void Compile(string source, string directory, IEnumerable<string> names, Action<string>? observe = null)
    {
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, "q4.json");
        if (File.Exists(manifestPath) || File.Exists(Path.Combine(directory, "q4.bin")))
        {
            throw new IOException("AUR-Q4-004: Refusing to replace an existing artifact; use a fresh output directory.");
        }
        using var weights = new SafeTensorFile(source);
        var matrices = new List<QuantizedMatrix>();
        string temporary = Path.Combine(directory, "q4.bin.partial");
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            foreach (string name in names.Distinct(StringComparer.Ordinal))
            {
                var tensor = weights.Tensors[name];
                if (tensor.Shape.Count != 2 || tensor.Shape[1] % 32 != 0 || tensor.Shape[1] > 32768)
                {
                    throw new InvalidDataException("AUR-Q4-004: Unsupported matrix: " + name);
                }
                int rows = tensor.Shape[0];
                int columns = tensor.Shape[1];
                long offset = output.Position;
                int tileRows = Math.Min(256, 1048576 / columns);
                for (int first = 0; first < rows; first += tileRows)
                {
                    float[] values = weights.ReadRows(name, first, Math.Min(tileRows, rows - first));
                    uint[] words = BlockQ4.Pack(values);
                    output.Write(MemoryMarshal.AsBytes(words.AsSpan()));
                }
                matrices.Add(new(name, rows, columns, offset, output.Position - offset));
                observe?.Invoke(name);
            }
        }
        string hash;
        using (var input = File.OpenRead(source))
        {
            hash = Convert.ToHexString(SHA256.HashData(input));
        }
        long bytes = new FileInfo(temporary).Length;
        var manifest = new QuantizedManifest(1, "Aurelian.Q4B32.F32Scale", hash, bytes, matrices);
        string temporaryManifest = manifestPath + ".partial";
        File.WriteAllText(temporaryManifest, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, Path.Combine(directory, "q4.bin"));
        File.Move(temporaryManifest, manifestPath);
    }

    public void Dispose() => stream.Dispose();
}
