using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class SafeTensorFileTests
{
    [Theory]
    [InlineData("F32")]
    [InlineData("F16")]
    [InlineData("BF16")]
    public void ReadsSelectedRowsAndPreservesEncodings(string type)
    {
        float[] values = [1, -2, 0.5f, 0, 4, -8];
        int elementBytes = type == "F32" ? 4 : 2;
        byte[] data = new byte[elementBytes * values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            var word = data.AsSpan(index * elementBytes, elementBytes);
            switch (type)
            {
                case "F32":
                    BinaryPrimitives.WriteInt32LittleEndian(word, BitConverter.SingleToInt32Bits(values[index]));
                    break;
                case "F16":
                    BinaryPrimitives.WriteUInt16LittleEndian(word, BitConverter.HalfToUInt16Bits((Half)values[index]));
                    break;
                case "BF16":
                    BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)(BitConverter.SingleToUInt32Bits(values[index]) >> 16));
                    break;
            }
        }
        string header = JsonSerializer.Serialize(new
        {
            w = new { dtype = type, shape = new[] { 3, 2 }, data_offsets = new[] { 0, data.Length } },
        });
        WithFile(header, data, path =>
        {
            using var file = new SafeTensorFile(path);
            Assert.Equal(new float[] { 0.5f, 0, 4, -8 }, file.ReadRows("w", 1, 2));
            Assert.Equal(new[] { 3, 2 }, file.Tensors["w"].Shape);
            AssertNamed(() => file.ReadRows("w", 2, 2));
            AssertNamed(() => file.ReadRows("w", -1, 1));
            AssertNamed(() => file.ReadRows("w", 0, 0));
            AssertNamed(() => file.ReadVector("w"));
            AssertNamed(() => file.ReadVector("missing"));
        });
    }

    [Theory]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]},\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]}}")]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[0],\"data_offsets\":[0,4]}}")]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,8]}}")]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[4,8]}}")]
    [InlineData("{\"w\":{\"dtype\":\"I32\",\"shape\":[1],\"data_offsets\":[0,4]}}")]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[2147483647,2147483647,2147483647],\"data_offsets\":[0,4]}}")]
    [InlineData("{\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]},\"b\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]}}")]
    [InlineData("{\"__metadata__\":{\"code\":42},\"w\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]}}")]
    [InlineData("[]")]
    [InlineData("{broken")]
    public void MalformedUnboundedOrOverlappingHeadersHaveNamedStops(string header)
    {
        WithFile(header, new byte[4], path => AssertNamed(() => new SafeTensorFile(path)));
    }

    [Fact]
    public void TruncationAndNonfiniteDataAreRejected()
    {
        const string header = """{"w":{"dtype":"F32","shape":[1],"data_offsets":[0,4]}}""";
        WithFile(header, [], path => AssertNamed(() => new SafeTensorFile(path)));
        WithFile(header, BitConverter.GetBytes(float.NaN), path =>
        {
            using var file = new SafeTensorFile(path);
            AssertNamed(() => file.ReadVector("w"));
            file.Dispose();
            Assert.Throws<ObjectDisposedException>(() => file.ReadVector("w"));
        });
        WithFile(header, new byte[4], path =>
        {
            File.WriteAllBytes(path, BitConverter.GetBytes(ulong.MaxValue));
            AssertNamed(() => new SafeTensorFile(path));
            File.WriteAllBytes(path, new byte[3]);
            AssertNamed(() => new SafeTensorFile(path));
        });
    }

    [Fact]
    public void LargeCheckpointCanBeReadByRowsButCannotBeMaterializedWhole()
    {
        const string header = """{"w":{"dtype":"BF16","shape":[1025,1024],"data_offsets":[0,2099200]}}""";
        WithFile(header, new byte[2099200], path =>
        {
            using var file = new SafeTensorFile(path);
            Assert.Equal(1024, file.ReadRows("w", 1024, 1).Length);
            AssertNamed(() => file.ReadRows("w", 0, 1025));
        });
    }

    private static void AssertNamed(Action action)
    {
        var exception = Assert.Throws<InvalidDataException>(action);
        Assert.StartsWith("AUR-TENSOR-001:", exception.Message);
    }

    private static void WithFile(string header, byte[] data, Action<string> run)
    {
        string path = Path.Combine(Path.GetTempPath(), "aurelian-tensor-" + Guid.NewGuid() + ".safetensors");
        try
        {
            byte[] encoded = Encoding.UTF8.GetBytes(header);
            using (var stream = File.Create(path))
            {
                stream.Write(BitConverter.GetBytes((ulong)encoded.Length));
                stream.Write(encoded);
                stream.Write(data);
            }
            run(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
