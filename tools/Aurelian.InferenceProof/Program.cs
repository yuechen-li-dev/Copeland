using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference;
using Aurelian.Inference.Dominatus;

string output = Path.GetFullPath(args.Length == 0 ? "artifacts/local/inference-foundation" : args[0]);
Directory.CreateDirectory(output);
string evidencePath = Path.Combine(output, "evidence.json");
File.WriteAllText(evidencePath, "{\"Accepted\":false}");
using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "Aurelian standalone inference proof",
    MaximumTensorElements = 1048576,
});
using var controller = new DominatusComputeFeedforward();
session.Compute.StreamController = controller;
var math = session.Compute;
var checks = new List<object>();
float[] values = Enumerable.Range(0, 21).Select(index => (index - 10) * .137f).ToArray();
float[] firstWeight = [ .25f, -.125f, .75f, -.5f, .25f, .5f, .125f, .5f, -.25f, .5f, .25f, -.5f, -.25f, .75f, .125f ];
float[] secondWeight = [ .25f, -.125f, .5f, .25f, -.5f, -.25f, .5f, .125f, -.5f, .25f ];
float[] bias = [.125f, -.25f];
var schedule = new DominatusComputeSchedule("proof.mlp");
foreach (var (encoding, rounding) in new[] { ("F32", TensorRounding.Float32), ("F16", TensorRounding.Float16), ("BF16", TensorRounding.BFloat16) })
{
    string path = Path.Combine(output, "mlp-" + encoding + ".safetensors");
    WriteCheckpoint(path, encoding);
    using var weights = new SafeTensorFile(path);
    CheckpointContract.Validate(weights,
        new("encoder.weight", [5, 3], encoding), new("head.weight", [2, 5], encoding), new("head.bias", [2], encoding));
    var layers = new VulkanModelLayers(math, weights);
    using var input = math.CreateTensor(7, 3);
    math.Upload(input, values);
    GpuTensor? hidden = null;
    GpuTensor? prediction = null;
    int reads = math.ReadbackCount;
    try
    {
        schedule.Run([
            new("encode", () => hidden = layers.Linear(input, "encoder.weight", rounding: rounding, maximumTileRows: 2)),
            new("activate", () =>
            {
                var batch = math.CreateBatch();
                batch.Activate(hidden!, hidden!, TensorActivation.Silu);
                math.Execute(batch);
            }),
            new("predict", () => prediction = layers.Linear(hidden!, "head.weight", "head.bias", maximumTileRows: 1)),
        ]);
        if (math.ReadbackCount != reads)
        {
            throw new InvalidDataException("Inference read an intermediate activation.");
        }
        float[] actual = math.Read(prediction!);
        float[] expected = Reference(rounding);
        double error = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
        if (!actual.All(float.IsFinite) || error > 5e-6)
        {
            throw new InvalidDataException("Standalone MLP failed CPU comparison: " + error);
        }
        checks.Add(new { Encoding = encoding, Rounding = rounding.ToString(), Rows = 7,
            MaximumAbsoluteError = error, ExplicitReadbacks = math.ReadbackCount - reads,
            CheckpointSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            Stages = schedule.Measurements.ToArray() });
        Console.WriteLine($"{encoding}: 7x3 -> 7x5 -> 7x2, error={error:G6}, one output read");
    }
    finally
    {
        prediction?.Dispose();
        hidden?.Dispose();
    }
}

// The same layers must release their output when an observer fails, and the
// owner must remain usable afterwards. This exercises the real native path.
using (var weights = new SafeTensorFile(Path.Combine(output, "mlp-F32.safetensors")))
using (var input = math.CreateTensor(7, 3))
{
    math.Upload(input, values);
    var layers = new VulkanModelLayers(math, weights);
    using (var warm = layers.Linear(input, "encoder.weight"))
    {
    }
    ulong before = math.MemoryTelemetry.LiveBytes;
    var expectedFailure = new InvalidDataException("observer-failure");
    try
    {
        using var failed = layers.Linear(input, "encoder.weight", observe: _ => throw expectedFailure);
        throw new InvalidDataException("Observer failure was swallowed.");
    }
    catch (InvalidDataException error) when (ReferenceEquals(error, expectedFailure))
    {
    }
    if (before != math.MemoryTelemetry.LiveBytes)
    {
        throw new InvalidDataException("Failed model layer leaked owned GPU allocations.");
    }
    using var retry = layers.Linear(input, "encoder.weight");
    checks.Add(new { ObserverFailurePreserved = true, AllocationBytesRestored = true, OwnerReusable = true });
}
using (var borrowed = VulkanInferenceSession.Attach(session.Plant, new() { MaximumTensorElements = 1024 }))
using (var probe = borrowed.Compute.CreateTensor(1, 1))
{
    borrowed.Compute.Upload(probe, [3.5f]);
    if (borrowed.Compute.Read(probe)[0] != 3.5f)
    {
        throw new InvalidDataException("Borrowed inference session failed native execution.");
    }
}
using (var probe = math.CreateTensor(1, 1))
{
    math.Upload(probe, [7.25f]);
    if (math.Read(probe)[0] != 7.25f)
    {
        throw new InvalidDataException("Disposing an attached session invalidated its borrowed plant.");
    }
    checks.Add(new { BorrowedPlantRemainsUsable = true });
}
using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Aurelian.InferenceProof.deps.json")));
string[] dependencyNames = dependencies.RootElement.GetProperty("libraries").EnumerateObject().Select(item => item.Name).ToArray();
bool gameReferenced = dependencyNames.Any(name => name.StartsWith("Aurelian.Games/", StringComparison.Ordinal));
bool trellisReferenced = dependencyNames.Any(name => name.StartsWith("Aurelian.Inference.Trellis2/", StringComparison.Ordinal));
if (gameReferenced || trellisReferenced)
{
    throw new InvalidDataException("Standalone inference acquired a game or model-specific dependency.");
}
File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
{
    Accepted = true, Runtime = "C#/Vulkan", Model = "Independent bias-free two-layer MLP with SiLU and three weight encodings",
    GameAssemblyReferenced = gameReferenced, TrellisAssemblyReferenced = trellisReferenced,
    Device = session.Plant.Facts.PhysicalDeviceName, Checks = checks,
    PeakOwnedGpuBytes = math.MemoryTelemetry.HighWaterLiveBytes,
    Feedforward = controller.Evidence, Inspection = schedule.Inspection,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("AURELIAN_STANDALONE_INFERENCE_PASSED");

float[] Reference(TensorRounding rounding)
{
    var expected = new float[14];
    for (int row = 0; row < 7; row++)
    {
        var hidden = new float[5];
        for (int channel = 0; channel < 5; channel++)
        {
            float value = 0;
            for (int inner = 0; inner < 3; inner++)
            {
                value += values[row * 3 + inner] * firstWeight[channel * 3 + inner];
            }
            value = Round(value, rounding);
            hidden[channel] = value >= 0 ? value / (1 + MathF.Exp(-value)) : value * MathF.Exp(value) / (1 + MathF.Exp(value));
        }
        for (int channel = 0; channel < 2; channel++)
        {
            float value = 0;
            for (int inner = 0; inner < 5; inner++)
            {
                value += hidden[inner] * secondWeight[channel * 5 + inner];
            }
            expected[row * 2 + channel] = value + bias[channel];
        }
    }
    return expected;
}

static float Round(float value, TensorRounding rounding)
{
    if (rounding == TensorRounding.Float16)
    {
        return (float)(Half)value;
    }
    if (rounding == TensorRounding.BFloat16)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return BitConverter.UInt32BitsToSingle((bits + 0x7fffu + (bits >> 16 & 1)) & 0xffff0000u);
    }
    return value;
}

void WriteCheckpoint(string path, string encoding)
{
    using var data = new MemoryStream();
    using var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true);
    var header = new Dictionary<string, object>();
    Append("encoder.weight", [5, 3], firstWeight);
    Append("head.weight", [2, 5], secondWeight);
    Append("head.bias", [2], bias);
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(header);
    int padded = (json.Length + 7) / 8 * 8;
    using var file = new BinaryWriter(File.Create(path));
    file.Write((ulong)padded);
    file.Write(json);
    for (int padding = json.Length; padding < padded; padding++)
    {
        file.Write((byte)32);
    }
    file.Write(data.ToArray());

    void Append(string name, int[] shape, float[] tensor)
    {
        long start = data.Position;
        foreach (float value in tensor)
        {
            if (encoding == "F32")
            {
                writer.Write(value);
            }
            else
            {
                ushort word = encoding == "F16" ? BitConverter.HalfToUInt16Bits((Half)value)
                    : (ushort)(BitConverter.SingleToUInt32Bits(Round(value, TensorRounding.BFloat16)) >> 16);
                writer.Write(word);
            }
        }
        header.Add(name, new { dtype = encoding, shape, data_offsets = new[] { start, data.Position } });
    }
}
