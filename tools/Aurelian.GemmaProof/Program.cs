using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference;
using Aurelian.Inference.Dominatus;
using Aurelian.Inference.Gemma4;

string command = args.Length > 0 ? args[0] : "--kernels";
string directory = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/local/models/gemma4-12b");
string outputDirectory = Path.GetFullPath("artifacts/local/gemma4");
Directory.CreateDirectory(outputDirectory);
string sourcePath = Path.Combine(directory, "model.safetensors");
string quantizedDirectory = Path.Combine(directory, "q4b32-v1");
if (command == "--compile")
{
    Gemma4TextModel.ValidateConfiguration(Path.Combine(directory, "config.json"));
    using (var source = new SafeTensorFile(sourcePath))
    {
        CheckpointContract.Validate(source, Gemma4TextModel.Requirements().ToArray());
    }
    var timer = Stopwatch.StartNew();
    QuantizedCheckpoint.Compile(sourcePath, quantizedDirectory,
        Gemma4TextModel.Requirements().Where(item => item.Shape.Count == 2).Select(item => item.Name), Console.WriteLine);
    Console.WriteLine($"AURELIAN_GEMMA_CHECKPOINT_COMPILED: {timer.Elapsed.TotalSeconds:F2}s");
    return;
}
if (command == "--tokenizer")
{
    var tokenizer = new HuggingFaceBpeTokenizer(Path.Combine(directory, "tokenizer.json"));
    using var goldens = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "tokenizer-golden.json")));
    foreach (var sample in goldens.RootElement.EnumerateArray())
    {
        string text = sample.GetProperty("Text").GetString()!;
        int[] expected = sample.GetProperty("Ids").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        int[] actual = tokenizer.Encode(text);
        if (!actual.SequenceEqual(expected) || tokenizer.Decode(actual) != sample.GetProperty("Decoded").GetString())
        {
            throw new InvalidDataException("Tokenizer differs from reference: " + JsonSerializer.Serialize(text)
                + ": " + JsonSerializer.Serialize(actual) + " vs " + JsonSerializer.Serialize(expected));
        }
    }
    Console.WriteLine("AURELIAN_GEMMA_TOKENIZER_PASSED");
    return;
}
using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "Aurelian Gemma 4 text proof",
    MaximumTensorElements = 33554432,
});
var compute = session.Compute;
if (command == "--scheduling")
{
    SchedulingChecks(compute, outputDirectory);
    return;
}
if (command == "--kernels")
{
    KernelChecks(compute);
    return;
}
if (command == "--benchmark")
{
    BenchmarkQuantized(compute, outputDirectory);
    return;
}
if (command is not "--generate" and not "--block")
{
    throw new ArgumentException("Expected --compile, --tokenizer, --kernels, --benchmark, --scheduling, --block or --generate.");
}
Gemma4TextModel.ValidateConfiguration(Path.Combine(directory, "config.json"));
using var weights = new SafeTensorFile(sourcePath);
using var packed = new QuantizedCheckpoint(quantizedDirectory);
packed.ValidateSource(sourcePath);
bool useChunkedReference = args.Contains("--reference", StringComparer.Ordinal);
int hostBudgetOption = Array.IndexOf(args, "--host-mib");
long hostBudget = hostBudgetOption >= 0 ? checked(long.Parse(args[hostBudgetOption + 1]) * 1024 * 1024) : 0;
bool queued = args.Contains("--queued", StringComparer.Ordinal);
bool separateUploads = args.Contains("--separate-uploads", StringComparer.Ordinal);
using var feedforward = new DominatusComputeFeedforward(64 * 1024 * 1024);
compute.StreamController = feedforward;
using var model = new Gemma4TextModel(compute, weights, packed,
    useChunkedReference: useChunkedReference, hostWeightBudget: hostBudget,
    useQueuedExecution: queued, useSeparateUploads: separateUploads);
if (command == "--block")
{
    int[] tokens = [2, 2532];
    using var blockResult = model.Evaluate(tokens, 6);
    File.WriteAllBytes(Path.Combine(outputDirectory, "block-native.f32"), MemoryMarshal.AsBytes(compute.Read(blockResult).AsSpan()).ToArray());
    model.Reset();
    using var first = model.Evaluate([tokens[0]], 6);
    using var second = model.Evaluate([tokens[1]], 6);
    float[] incremental = compute.Read(first).Concat(compute.Read(second)).ToArray();
    File.WriteAllBytes(Path.Combine(outputDirectory, "block-incremental.f32"), MemoryMarshal.AsBytes(incremental.AsSpan()).ToArray());
    Console.WriteLine("AURELIAN_GEMMA_BLOCK_EXECUTED");
    return;
}
var bpe = new HuggingFaceBpeTokenizer(Path.Combine(directory, "tokenizer.json"));
string prompt = args.Length > 2 ? args[2] : "Say hello in one short sentence.";
int maximumTokens = args.Length > 3 ? int.Parse(args[3]) : 16;
if (maximumTokens is < 1 or > 128) { throw new ArgumentOutOfRangeException(nameof(maximumTokens)); }
string formatted = Gemma4Chat.FormatUser(prompt);
int[] promptTokens = bpe.Encode(formatted);
var schedule = new DominatusComputeSchedule("gemma.generate");
var generator = new CausalTextGenerator(model, schedule);
var emitted = new List<int>();
long uploadStart = model.UploadedWeightBytes;
model.ObserveLayer = layer => Console.WriteLine($"layer {layer + 1}/48");
TextGenerationResult result = generator.Generate(promptTokens, maximumTokens,
    new HashSet<int> { bpe.TokenId("<eos>"), bpe.TokenId("<turn|>") }, token =>
    {
        model.ObserveLayer = null;
        emitted.Add(token.Id);
        var partial = Gemma4Chat.ParseResponse(bpe.Decode(emitted, skipSpecial: false));
        Console.WriteLine($"token={token.Id}, content={JsonSerializer.Serialize(partial.Content)}");
    });
string raw = bpe.Decode(result.Tokens.Select(token => token.Id), skipSpecial: false);
Gemma4ChatResponse response = Gemma4Chat.ParseResponse(raw);
string evidencePath = Path.Combine(outputDirectory, "generation.json");
double decodeSeconds = (result.TotalMilliseconds - result.TimeToFirstTokenMilliseconds) / 1000;
File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
{
    Executed = true,
    QualityQualified = false,
    Model = "Gemma4-12B-Unified-text",
    Encoding = packed.Manifest.Encoding,
    SourceSha256 = packed.Manifest.SourceSha256,
    Prompt = prompt,
    PromptTokens = promptTokens,
    RawResponse = raw,
    Response = response,
    Generation = result,
    DecodeTokensPerSecond = result.Tokens.Count > 1 ? (result.Tokens.Count - 1) / decodeSeconds : 0,
    ResidentWeightBytes = model.ResidentWeightBytes,
    WeightTimings = model.WeightTimings,
    Queued = queued,
    SeparateUploads = separateUploads,
    QueueMeasurements = model.QueueMeasurements,
    Feedforward = feedforward.Evidence,
    WeightHostScratchBytes = model.WeightHostScratchBytes,
    WeightHostCacheBudget = hostBudget,
    WeightHostCacheBytes = model.WeightHostCacheBytes,
    UseChunkedReference = useChunkedReference,
    UploadedWeightBytes = model.UploadedWeightBytes - uploadStart,
    Readbacks = compute.ReadbackCount,
    Memory = compute.MemoryTelemetry,
    Stages = schedule.Measurements,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Response: " + response.Content);
Console.WriteLine("AURELIAN_GEMMA_GENERATION_EXECUTED: " + evidencePath);

static void SchedulingChecks(VulkanComputeMath compute, string outputDirectory)
{
    string evidencePath = Path.Combine(outputDirectory, "scheduling.json");
    File.WriteAllText(evidencePath, "{\"Accepted\":false}");
    object? evidence = null;
    using var input = compute.CreateTensor(1, 96);
    compute.Upload(input, Enumerable.Range(0, 96).Select(index => (index % 7 - 3) * .25f).ToArray());
    using var resident = compute.CreateSparseTensor(5, 15, GpuTensorFormat.UInt32);
    uint[] residentWords = BlockQ4.Pack(Enumerable.Repeat(.5f, 5 * 96).ToArray());
    compute.UploadUInt32(resident, residentWords);
    var expected = new List<float[]>();
    for (int step = 0; step < 12; step++)
    {
        using var weight = compute.CreateSparseTensor(5, 15, GpuTensorFormat.UInt32);
        using var output = compute.CreateTensor(1, 5);
        uint[] words = BlockQ4.Pack(Enumerable.Repeat((step + 1) * .125f, 5 * 96).ToArray());
        var batch = compute.CreateBatch();
        batch.QuantizedLinear(input, weight, output);
        compute.ExecuteWithUInt32Upload(batch, weight, MemoryMarshal.AsBytes(words.AsSpan()).ToArray());
        expected.Add(compute.Read(output));
    }
    using var residentOutput = compute.CreateTensor(1, 5);
    var residentBatch = compute.CreateBatch();
    residentBatch.QuantizedLinear(input, resident, residentOutput);
    compute.Execute(residentBatch);
    float[] residentExpected = compute.Read(residentOutput);
    using var controller = new DominatusComputeFeedforward(64 * 1024 * 1024);
    var outputs = new List<GpuTensor>();
    var retiredWeights = new List<GpuTensor>();
    try
    {
        using var queue = compute.BeginQueuedScope(controller, new(13, 1, 300));
        try
        {
            compute.BeginQueuedScope(controller, new(1, 1, 0));
            throw new InvalidDataException("Nested queue was accepted.");
        }
        catch (ArgumentException)
        {
        }
        for (int step = 0; step < 12; step++)
        {
            var weight = compute.CreateSparseTensor(5, 15, GpuTensorFormat.UInt32);
            retiredWeights.Add(weight);
            var output = compute.CreateTensor(1, 5);
            outputs.Add(output);
            uint[] words = BlockQ4.Pack(Enumerable.Repeat((step + 1) * .125f, 5 * 96).ToArray());
            byte[] payload = MemoryMarshal.AsBytes(words.AsSpan()).ToArray();
            var batch = compute.CreateBatch();
            batch.QuantizedLinear(input, weight, output);
            compute.ExecuteWithUInt32Upload(batch, weight, payload);
            Array.Clear(payload); // Submit must consume caller scratch immediately.
            weight.Dispose(); // Physical allocation retires at completion.
            try
            {
                compute.UploadUInt32(weight, words);
                throw new InvalidDataException("Disposed tensor handle was accepted.");
            }
            catch (ArgumentException)
            {
            }
        }
        compute.Execute(residentBatch); // Reused staging must not overwrite resident weights.
        for (int step = 0; step < outputs.Count; step++)
        {
            if (!compute.Read(outputs[step]).SequenceEqual(expected[step]))
            {
                throw new InvalidDataException("Queued upload/retirement differs at step " + step);
            }
        }
        if (!compute.Read(residentOutput).SequenceEqual(residentExpected))
        {
            throw new InvalidDataException("Stale staging overwrote resident weights.");
        }
        if (retiredWeights.Any(weight => !weight.Buffer.IsDisposed))
        {
            throw new InvalidDataException("Read boundary did not collect retired weight allocations.");
        }
        if (queue.Metrics.Submissions != 13 || queue.Metrics.MaximumOutstanding != 2)
        {
            throw new InvalidDataException("Queue did not exercise bounded slot reuse.");
        }
        try
        {
            compute.Execute(residentBatch);
            throw new InvalidDataException("Submission forecast was exceeded.");
        }
        catch (ArgumentException)
        {
        }
        evidence = new
        {
            Accepted = true,
            Cases = "12 changing uploads, consumed host scratch, deferred tensor retirement, resident staging reuse, read boundary, nesting/forecast rejection, submitted observer failure cleanup, owner recovery and composite attention/linear boundaries",
            Queue = queue.Metrics,
            Controller = controller.Evidence,
        };
    }
    finally
    {
        foreach (var output in outputs)
        {
            output.Dispose();
        }
        foreach (var weight in retiredWeights)
        {
            weight.Dispose();
        }
    }
    ulong beforeFailure = compute.MemoryTelemetry.LiveBytes;
    var failure = new InvalidDataException("scheduled-observer-failure");
    using (var queue = compute.BeginQueuedScope(new FailingStreamController(failure), new(2, 1, 300)))
    using (var weight = compute.CreateSparseTensor(5, 15, GpuTensorFormat.UInt32))
    using (var output = compute.CreateTensor(1, 5))
    {
        var batch = compute.CreateBatch();
        batch.QuantizedLinear(input, weight, output);
        try
        {
            compute.ExecuteWithUInt32Upload(batch, weight, MemoryMarshal.AsBytes(residentWords.AsSpan()).ToArray());
            throw new InvalidDataException("Observer did not fail.");
        }
        catch (InvalidDataException error) when (ReferenceEquals(error, failure))
        {
        }
    }
    if (compute.MemoryTelemetry.LiveBytes != beforeFailure)
    {
        throw new InvalidDataException("Failed submitted work leaked its queued allocations.");
    }
    compute.Execute(residentBatch);
    if (!compute.Read(residentOutput).SequenceEqual(residentExpected))
    {
        throw new InvalidDataException("Owner was unusable after queue failure.");
    }
    var previousController = compute.StreamController;
    compute.StreamController = controller;
    try
    {
        using var queue = compute.BeginQueuedScope(controller, new(3, 1, 0));
        compute.Execute(residentBatch);
        using var query = compute.CreateTensor(1, 64);
        using var key = compute.CreateTensor(1, 64);
        using var value = compute.CreateTensor(1, 64);
        float[] attentionValues = Enumerable.Repeat(.5f, 64).ToArray();
        compute.Upload(query, attentionValues);
        compute.Upload(key, attentionValues);
        compute.Upload(value, attentionValues);
        compute.Execute(residentBatch);
        using var attention = compute.CreateTensor(1, 64);
        compute.AttentionTiled(query, key, value, attention, headWidth: 64, queryTileRows: 1);
        if (!compute.Read(attention).SequenceEqual(attentionValues))
        {
            throw new InvalidDataException("Composite attention did not preserve the outer queue boundary.");
        }
        compute.Execute(residentBatch);
        using var linear = compute.CreateTensor(1, 5);
        var plan = new StreamingLinearPlan(96, 5, 2);
        compute.LinearStreamed(input, linear, plan, _ =>
        {
            float[] payload = Enumerable.Repeat(.5f, 2 * 97).ToArray();
            payload[96] = 0;
            payload[193] = 0;
            return payload;
        });
        if (!compute.Read(linear).SequenceEqual(residentExpected))
        {
            throw new InvalidDataException("Composite streaming did not preserve the outer queue boundary.");
        }
        queue.Flush();
    }
    finally
    {
        compute.StreamController = previousController;
    }
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("AURELIAN_COMPUTE_SCHEDULING_PASSED");
}

static void BenchmarkQuantized(VulkanComputeMath compute, string outputDirectory)
{
    var measurements = new List<object>();
    foreach (var shape in new[] { (Width: 3840, Outputs: 15360), (Width: 15360, Outputs: 3840) })
    {
        float[] values = Enumerable.Range(0, shape.Width * shape.Outputs)
            .Select(index => (index % 127 - 63) * .001f).ToArray();
        uint[] packed = BlockQ4.Pack(values);
        using var weight = compute.CreateSparseTensor(shape.Outputs, shape.Width / 32 * 5, GpuTensorFormat.UInt32);
        compute.UploadUInt32(weight, packed);
        foreach (int rows in new[] { 1, 16 })
        {
            using var input = compute.CreateTensor(rows, shape.Width);
            using var reference = compute.CreateTensor(rows, shape.Outputs);
            using var candidate = compute.CreateTensor(rows, shape.Outputs);
            compute.Upload(input, Enumerable.Range(0, input.Elements).Select(index => MathF.Sin(index * .12f) * .1f).ToArray());
            var oldBatch = compute.CreateBatch();
            oldBatch.QuantizedLinear(input, weight, reference, chunkedReference: true);
            var newBatch = compute.CreateBatch();
            newBatch.QuantizedLinear(input, weight, candidate);
            compute.Execute(oldBatch);
            compute.Execute(newBatch);
            float[] expected = compute.Read(reference);
            float[] actual = compute.Read(candidate);
            double error = expected.Zip(actual, (left, right) => Math.Abs(left - right)).Max();
            if (!actual.All(float.IsFinite) || error > 2e-5)
            {
                throw new InvalidDataException("Packed full-width reduction differs from chunked reference: " + error);
            }
            var samples = new List<object>();
            for (int repeat = 0; repeat < 8; repeat++)
            {
                // Alternate order to reduce temperature/clock bias. Timings exclude uploads and compilation.
                foreach (string strategy in repeat % 2 == 0
                    ? new[] { "reference", "packed" }
                    : new[] { "packed", "reference" })
                {
                    long start = Stopwatch.GetTimestamp();
                    var selected = strategy == "reference" ? oldBatch : newBatch;
                    var result = compute.Execute(selected);
                    samples.Add(new
                    {
                        Strategy = strategy,
                        WallMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        result.GpuMilliseconds,
                        result.Dispatches,
                    });
                }
            }
            measurements.Add(new { shape.Width, shape.Outputs, Rows = rows, MaximumAbsoluteError = error, Samples = samples });
        }
    }
    File.WriteAllText(Path.Combine(outputDirectory, "quantized-benchmark.json"),
        JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("AURELIAN_GEMMA_QUANTIZED_BENCHMARK_PASSED");
}

static void KernelChecks(VulkanComputeMath compute)
{
    var checks = new List<object>();
    foreach (int width in new[] { 32, 96, 3840, 4096, 8192, 15360, 32768 })
    {
        foreach (int rows in new[] { 1, 3, 5 })
        {
            float[] input = Enumerable.Range(0, rows * width).Select(index => MathF.Sin(index * .12f) * .1f).ToArray();
            float[] weight = Enumerable.Range(0, 7 * width).Select(index => MathF.Cos(index * .017f) * .2f).ToArray();
            uint[] words = BlockQ4.Pack(weight);
            using var x = compute.CreateTensor(rows, width);
            using var w = compute.CreateSparseTensor(7 * BlockQ4.WordsPerBlock,
                width / BlockQ4.ValuesPerBlock, GpuTensorFormat.UInt32);
            using var y = compute.CreateTensor(rows, 7);
            compute.Upload(x, input);
            compute.UploadUInt32(w, words);
            var batch = compute.CreateBatch();
            batch.QuantizedLinear(x, w, y);
            compute.Execute(batch);
            float[] actual = compute.Read(y);
            double maximum = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < 7; column++)
                {
                    double expected = 0;
                    for (int inner = 0; inner < width; inner++)
                    {
                        expected += (double)input[row * width + inner] * BlockQ4.Decode(words, column * width + inner);
                    }
                    maximum = Math.Max(maximum, Math.Abs(expected - actual[row * 7 + column]));
                }
            }
            if (!actual.All(float.IsFinite) || maximum > 2e-5) { throw new InvalidDataException("Q4 kernel failed: " + maximum); }
            var rounded = compute.CreateBatch();
            rounded.QuantizedLinear(x, w, y, roundBFloat16: true);
            compute.Execute(rounded);
            float[] bf16 = compute.Read(y);
            for (int index = 0; index < actual.Length; index++)
            {
                uint bits = BitConverter.SingleToUInt32Bits(actual[index]);
                float expected = BitConverter.UInt32BitsToSingle((bits + 32767 + ((bits >> 16) & 1)) & 0xFFFF0000);
                if (bf16[index] != expected)
                {
                    throw new InvalidDataException("Q4 fused BF16 differs from its complete FP32 reduction.");
                }
            }
            checks.Add(new { Kernel = "Q4B32", Width = width, Rows = rows, Error = maximum, FusedBFloat16Accepted = true });
        }
    }
    using (var input = compute.CreateTensor(2, 512))
    using (var gamma = compute.CreateTensor(1, 256))
    using (var normalized = compute.CreateTensor(2, 512))
    {
        float[] values = Enumerable.Range(0, 1024).Select(index => MathF.Sin(index * .1f)).ToArray();
        compute.Upload(input, values);
        compute.Upload(gamma, Enumerable.Repeat(1f, 256).ToArray());
        var batch = compute.CreateBatch();
        batch.NormalizeHeads(input, gamma, normalized, 256);
        batch.RotateHeads(normalized, normalized, 256, 3, 10000);
        batch.Scale(normalized, normalized, .5f);
        compute.Execute(batch);
        if (!compute.Read(normalized).All(float.IsFinite)) { throw new InvalidDataException("Norm/RoPE produced nonfinite output."); }
    }
    File.WriteAllText("artifacts/local/gemma4/kernels.json", JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    GroupedAttentionChecks(compute);
    Console.WriteLine("AURELIAN_GEMMA_KERNELS_PASSED");
}

static void GroupedAttentionChecks(VulkanComputeMath compute)
{
    const int heads = 4;
    const int kvHeads = 2;
    const int width = 32;
    float[] queries = Enumerable.Range(0, 2 * heads * width).Select(index => (index % 7 - 3) * .125f).ToArray();
    float[] keys = Enumerable.Range(0, 6 * kvHeads * width).Select(index => (index % 5 - 2) * .125f).ToArray();
    float[] values = Enumerable.Range(0, keys.Length).Select(index => (index % 9 - 4) * .125f).ToArray();
    using var query = compute.CreateTensor(2, heads * width);
    using var key = compute.CreateTensor(6, kvHeads * width);
    using var value = compute.CreateTensor(6, kvHeads * width);
    using var scores = compute.CreateTensor(2 * heads, 6);
    using var probabilities = compute.CreateTensor(2 * heads, 6);
    using var output = compute.CreateTensor(2, heads * width);
    compute.Upload(query, queries);
    compute.Upload(key, keys);
    compute.Upload(value, values);
    var batch = compute.CreateBatch();
    batch.GroupedAttentionScores(query, key, scores, heads, kvHeads, width, 4, 6, window: 3);
    batch.ReduceRows(scores, probabilities);
    batch.RoundBFloat16(probabilities, probabilities);
    batch.GroupedAttentionValues(probabilities, value, output, heads, kvHeads, width);
    compute.Execute(batch);
    float[] actual = compute.Read(output);
    double maximumError = 0;
    for (int token = 0; token < 2; token++)
    {
        for (int head = 0; head < heads; head++)
        {
            int kvHead = head / (heads / kvHeads);
            var logits = new float[6];
            for (int position = 0; position < 6; position++)
            {
                float score = 0;
                for (int channel = 0; channel < width; channel++)
                {
                    score += queries[(token * heads + head) * width + channel]
                        * keys[(position * kvHeads + kvHead) * width + channel];
                }
                logits[position] = position > token + 4 || position + 3 <= token + 4 ? float.NegativeInfinity : Round(score);
            }
            float max = logits.Max();
            float[] exponential = logits.Select(item => MathF.Exp(item - max)).ToArray();
            float denominator = exponential.Sum();
            float[] probability = exponential.Select(item => Round(item / denominator)).ToArray();
            for (int channel = 0; channel < width; channel++)
            {
                float expected = 0;
                for (int position = 0; position < 6; position++)
                {
                    expected += probability[position] * values[(position * kvHeads + kvHead) * width + channel];
                }
                maximumError = Math.Max(maximumError, Math.Abs(Round(expected) - actual[(token * heads + head) * width + channel]));
            }
        }
    }
    if (maximumError > 1e-6) { throw new InvalidDataException("GQA window/cache prefix differs from CPU: " + maximumError); }
    File.WriteAllText("artifacts/local/gemma4/gqa.json", JsonSerializer.Serialize(new { Accepted = true, Window = 3, KeyCount = 6, PositionOffset = 4, MaximumAbsoluteError = maximumError }));

    static float Round(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return BitConverter.UInt32BitsToSingle((bits + 32767 + ((bits >> 16) & 1)) & 0xFFFF0000);
    }
}

sealed class FailingStreamController(Exception failure) : IComputeStreamController
{
    public int Plan(ComputeStreamForecast forecast) => 2;

    public void Observe(ComputeStreamObservation observation)
    {
        if (!observation.Completed)
        {
            throw failure;
        }
    }
}

