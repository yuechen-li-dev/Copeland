using System.Text.Json;
using System.Runtime.InteropServices;
using Aurelian.Inference;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class QuantizedCheckpointTests
{
    [Fact]
    public void ReusableReadsPreserveSliceAuthorityAndValidateEveryScale()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-q4-slices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            uint[] packed = BlockQ4.Pack(Enumerable.Range(0, 96).Select(index => index * .01f).ToArray());
            File.WriteAllBytes(Path.Combine(directory, "q4.bin"), MemoryMarshal.AsBytes(packed.AsSpan()).ToArray());
            var matrix = new QuantizedMatrix("test", 3, 32, 0, 60);
            File.WriteAllText(Path.Combine(directory, "q4.json"), JsonSerializer.Serialize(
                new QuantizedManifest(1, "Aurelian.Q4B32.F32Scale", new string('A', 64), 60, [matrix])));
            using (var checkpoint = new QuantizedCheckpoint(directory))
            {
                var destination = new uint[5];
                checkpoint.ReadRows("test", 2, 1, destination);
                Assert.Equal(packed.Skip(10), destination);
                checkpoint.ReadRows("test", 0, 1, destination);
                Assert.Equal(packed.Take(5), destination);
                Assert.Throws<InvalidDataException>(() => checkpoint.ReadRows("test", 0, 1, new uint[6]));
                Assert.Throws<InvalidDataException>(() => checkpoint.ReadRows("test", 3, 1, destination));
            }
            packed[10] = BitConverter.SingleToUInt32Bits(float.NaN);
            File.WriteAllBytes(Path.Combine(directory, "q4.bin"), MemoryMarshal.AsBytes(packed.AsSpan()).ToArray());
            using var corrupt = new QuantizedCheckpoint(directory);
            Assert.Throws<InvalidDataException>(() => corrupt.ReadRows("test", 0, 3, new uint[15]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void QuantizationPreservesZerosAndBoundsEachBlockError()
    {
        float[] values = Enumerable.Range(0, 96).Select(index => index < 32 ? 0 : MathF.Sin(index * .17f)).ToArray();
        uint[] words = BlockQ4.Pack(values);
        Assert.Equal(15, words.Length);
        for (int index = 0; index < values.Length; index++)
        {
            float scale = BitConverter.UInt32BitsToSingle(words[index / 32 * 5]);
            float decoded = BlockQ4.Decode(words, index);
            Assert.True(MathF.Abs(decoded - values[index]) <= MathF.Abs(scale) + 1e-6f);
            if (index < 32) { Assert.Equal(0, decoded); }
        }
        Assert.Throws<ArgumentException>(() => BlockQ4.Pack(new float[31]));
        values[12] = float.NaN;
        Assert.Throws<InvalidDataException>(() => BlockQ4.Pack(values));
    }

    [Fact]
    public void ArtifactRejectsTruncationOverlapsDuplicateNamesAndUnsupportedEncoding()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-q4-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var matrix = new QuantizedMatrix("test", 2, 32, 0, 40);
            var manifest = new QuantizedManifest(1, "Aurelian.Q4B32.F32Scale", new string('A', 64), 40, [matrix]);
            File.WriteAllBytes(Path.Combine(directory, "q4.bin"), new byte[40]);
            Write(manifest);
            using (var checkpoint = new QuantizedCheckpoint(directory))
            {
                Assert.Equal(5, checkpoint.ReadRows("test", 1, 1).Length);
                Assert.Throws<InvalidDataException>(() => checkpoint.ReadRows("test", 1, 2));
            }
            Write(manifest with { Encoding = "GGUF" });
            Assert.Throws<InvalidDataException>(() => new QuantizedCheckpoint(directory));
            Write(manifest with { Matrices = [matrix with { Offset = 4 }] });
            Assert.Throws<InvalidDataException>(() => new QuantizedCheckpoint(directory));
            Write(manifest with { Matrices = [matrix, matrix with { Offset = 40 }], DataBytes = 80 });
            Assert.Throws<InvalidDataException>(() => new QuantizedCheckpoint(directory));
            Write(manifest);
            byte[] invalidScale = new byte[40];
            BitConverter.GetBytes(float.PositiveInfinity).CopyTo(invalidScale, 0);
            File.WriteAllBytes(Path.Combine(directory, "q4.bin"), invalidScale);
            using (var checkpoint = new QuantizedCheckpoint(directory))
            {
                Assert.Throws<InvalidDataException>(() => checkpoint.ReadRows("test", 0, 1));
            }
            File.WriteAllBytes(Path.Combine(directory, "q4.bin"), new byte[39]);
            Assert.Throws<InvalidDataException>(() => new QuantizedCheckpoint(directory));

            void Write(QuantizedManifest value) => File.WriteAllText(Path.Combine(directory, "q4.json"), JsonSerializer.Serialize(value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
