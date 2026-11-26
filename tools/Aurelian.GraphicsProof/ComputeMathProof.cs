using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

internal static class ComputeMathProof
{
    private sealed record Measurement(string Name, int Values, double? MaximumError, int Dispatches, double? GpuMilliseconds);

    public static void Run(string output, bool cooperative = true)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian compute math", EnableCooperativeMatrices: cooperative));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader);
        var measurements = new List<Measurement>();
        string loopPath = Path.Combine(AppContext.BaseDirectory, "Assets", "BoundedLoopProof.v.ts");
        var loopModule = GpuComputeBinder.Compile(new([new GpuSourceFile("BoundedLoopProof.v.ts", File.ReadAllText(loopPath))]));
        Require(loopModule.Success, string.Join("; ", loopModule.Diagnostics.Select(item => item.Message)));
        var loopBackend = VdMirComputeBackend.Compile(loopModule);
        Require(loopBackend.SpirvValidated, loopBackend.DxcOutput + loopBackend.SpirvValidationOutput);
        File.WriteAllText(Path.Combine(output, "bounded-loop.hlsl"), loopBackend.Hlsl);
        File.WriteAllBytes(Path.Combine(output, "bounded-loop.spv"), loopBackend.Spirv);
        foreach (uint count in new[] { 0u, 1u, 3u, 512u, 513u, uint.MaxValue })
        {
            float[] words = Enumerable.Repeat(BitConverter.UInt32BitsToSingle(count), 64).ToArray();
            using var probe = new VulkanScalarComputeProbe(plant, loopBackend.Spirv, loopModule.EntryPoint!.EmittedName, words);
            uint iterations = Math.Min(count, 512u);
            float sum = iterations * (iterations - 1f) / 2;
            Require(probe.Execute().All(value => value == sum), "Bounded loop count/clamp/synchronization failed: " + count);
            measurements.Add(new("bounded-loop-count-" + count, 64, 0, 1, null));
        }

        GpuTensor Tensor(int rows, int columns, float[]? values = null, GpuTensorFormat format = GpuTensorFormat.Float32)
        {
            var tensor = math.CreateTensor(rows, columns, format);
            if (values is not null)
            {
                math.Upload(tensor, values);
            }
            return tensor;
        }

        void Check(string name, ComputeMathBatch batch, GpuTensor result, float[] expected, double tolerance = 0.0002)
        {
            int before = math.ReadbackCount;
            var execution = math.Execute(batch);
            Require(math.ReadbackCount == before, "GPU chain performed intermediate readback.");
            float[] actual = math.Read(result);
            Require(actual.Length == expected.Length && actual.All(float.IsFinite), name + " output shape or finiteness failed.");
            double maximum = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
            if (maximum > tolerance)
            {
                File.WriteAllText(Path.Combine(output, "failure-" + name + ".json"),
                    JsonSerializer.Serialize(new { Actual = actual, Expected = expected, Tolerance = tolerance }));
            }
            Require(maximum <= tolerance, name + " disagrees with CPU reference: " + maximum);
            measurements.Add(new(name, actual.Length, maximum, execution.Dispatches, execution.GpuMilliseconds));
            Console.WriteLine($"{name}: maximum error {maximum:G4}, {execution.Dispatches} dispatches, {execution.GpuMilliseconds:F3} ms GPU");
        }

        var aValues = Values(17 * 35, 1);
        var bValues = Values(35 * 19, 2);
        var prior = Values(17 * 19, 3);
        var a = Tensor(17, 35, aValues);
        var b = Tensor(35, 19, bValues);
        var c = Tensor(17, 19, prior);
        var batch = math.CreateBatch();
        batch.Multiply(a, b, c, .7f, -.2f);
        float[] expected = Multiply(aValues, bValues, 17, 19, 35);
        for (int index = 0; index < expected.Length; index++)
        {
            expected[index] = expected[index] * .7f - prior[index] * .2f;
        }
        Check("sgemm-tail-alpha-beta", batch, c, expected);
        foreach (bool transpose in new[] { false, true })
        {
            float[] registerA = Values(65 * 131, 71);
            float[] registerB = Values(131 * 67, 73);
            float[] registerPrior = Values(65 * 67, 79);
            float[] storedB = transpose
                ? Enumerable.Range(0, 67).SelectMany(column => Enumerable.Range(0, 131)
                    .Select(inner => registerB[inner * 67 + column])).ToArray()
                : registerB;
            var left = Tensor(65, 131, registerA);
            var right = Tensor(transpose ? 67 : 131, transpose ? 131 : 67, storedB);
            var product = Tensor(65, 67, registerPrior);
            batch = math.CreateBatch();
            batch.Multiply(left, right, product, MatrixKernel.Resident64, .7f, -.2f, transpose);
            float[] reference = Multiply(registerA, registerB, 65, 67, 131);
            for (int index = 0; index < reference.Length; index++)
            {
                reference[index] = reference[index] * .7f - registerPrior[index] * .2f;
            }
            Check("resident-tail-alpha-beta-transpose-" + transpose, batch, product, reference);
        }

        float[] maximumKLeft = Values(8192, 83);
        float[] maximumKRight = Values(8192, 89);
        var maximumA = Tensor(1, 8192, maximumKLeft);
        var maximumB = Tensor(8192, 1, maximumKRight);
        var maximumProduct = Tensor(1, 1);
        batch = math.CreateBatch();
        batch.Multiply(maximumA, maximumB, maximumProduct, kernel: MatrixKernel.Resident64);
        Check("resident-maximum-K-8192", batch, maximumProduct,
            Multiply(maximumKLeft, maximumKRight, 1, 1, 8192), .002);

        const int vectorWidth = 131;
        const int vectorOutputs = 67;
        float[] vectorInput = Values(vectorWidth, 91);
        float[] vectorWeights = Values(vectorOutputs * vectorWidth, 93);
        using (var input = Tensor(1, vectorWidth, vectorInput))
        using (var weights = Tensor(vectorOutputs, vectorWidth, vectorWeights))
        using (var result = Tensor(1, vectorOutputs))
        {
            float[] expectedVector = new float[vectorOutputs];
            for (int row = 0; row < vectorOutputs; row++)
            {
                double dot = 0;
                for (int column = 0; column < vectorWidth; column++)
                {
                    dot += (double)vectorInput[column] * vectorWeights[row * vectorWidth + column];
                }
                expectedVector[row] = (float)dot;
            }
            batch = math.CreateBatch();
            batch.MultiplyVector(input, weights, result);
            Check("gemv-input-and-output-tails", batch, result, expectedVector);
            using var packedWeights = Tensor(vectorOutputs, vectorWidth, vectorWeights, GpuTensorFormat.PackedFloat16);
            for (int row = 0; row < vectorOutputs; row++)
            {
                double sum = 0;
                for (int col = 0; col < vectorWidth; col++)
                {
                    sum += vectorInput[col] * (float)(Half)vectorWeights[row * vectorWidth + col];
                }
                expectedVector[row] = (float)sum;
            }
            batch = math.CreateBatch();
            batch.MultiplyVector(input, packedWeights, result);
            Check("gemv-packed-weight-odd-row-stride", batch, result, expectedVector);
        }

        var replayInput = Tensor(1, 3, [-2f, 0f, 2f]);
        var replayBias = Tensor(1, 3, [.1f, -.2f, .3f]);
        var replayOutput = Tensor(1, 3);
        var replayBatch = math.CreateBatch();
        replayBatch.Activate(replayInput, replayOutput, TensorActivation.BiasSilu, replayBias);
        var profiled = math.ExecuteWithDispatchTimings(replayBatch);
        Require(profiled.DispatchMilliseconds.Count == 1
            && profiled.DispatchMilliseconds.All(value => double.IsFinite(value) && value >= 0),
            "Dispatch profiling did not cover the recorded batch.");
        float[] temporaryRead = math.Read(replayOutput);
        float[] reusedRead = math.Read(replayOutput, reuseStaging: true);
        Require(temporaryRead.SequenceEqual(reusedRead), "Reusable staging changed readback data.");
        math.Upload(replayInput, [3f, 4f, 5f]);
        math.Execute(replayBatch);
        ulong allocationsBeforeRead = math.MemoryTelemetry.AllocationCount;
        float[] changedRead = math.Read(replayOutput, reuseStaging: true);
        Require(!changedRead.SequenceEqual(reusedRead) && math.MemoryTelemetry.AllocationCount == allocationsBeforeRead,
            "Reusable staging returned stale output or allocated a replacement.");
        Require(changedRead.SequenceEqual(math.Read(replayOutput)),
            "Reusable staging disagrees with temporary staging after a second execution.");
        _ = math.Read(maximumA, reuseStaging: true);
        allocationsBeforeRead = math.MemoryTelemetry.AllocationCount;
        Require(changedRead.SequenceEqual(math.Read(replayOutput, reuseStaging: true))
            && math.MemoryTelemetry.AllocationCount == allocationsBeforeRead,
            "Reusable staging did not read the requested prefix of a larger buffer.");
        var hostValues = new float[3];
        math.ReadInto(replayOutput, hostValues);
        Require(hostValues.SequenceEqual(changedRead), "Caller-owned readback produced different values.");
        var uploadValues = Values(maximumA.Elements, 121);
        math.Upload(maximumA, uploadValues, reuseStaging: true);
        var transferAllocations = math.MemoryTelemetry.AllocationCount;
        float[] uploadSlice = [float.NaN, 7f, 8f, 9f, float.PositiveInfinity];
        math.Upload(replayInput, uploadSlice.AsSpan(1, 3), reuseStaging: true);
        uploadSlice[2] = 99f;
        ExpectRejected(() => math.Upload(replayInput, uploadSlice.AsSpan(0, 3), reuseStaging: true));
        math.ReadInto(replayInput, hostValues);
        Require(hostValues.SequenceEqual(new float[] { 7f, 8f, 9f })
            && transferAllocations == math.MemoryTelemetry.AllocationCount,
            "Retained transfer buffers returned stale data or allocated on a smaller transfer.");
        Require(math.Read(maximumA).SequenceEqual(uploadValues), "Persistent upload changed the tensor.");
        Console.WriteLine("dispatch-timestamps-and-persistent-staging: passed");

        using (var untimed = math.Prepare(replayBatch, enableGpuTimings: false))
        {
            Require(!untimed.HasGpuTimings, "Untimed replay allocated timestamp instrumentation.");
            foreach (float[] changed in new[] { new[] { -2f, 0f, 2f }, new[] { -3f, 1f, 4f } })
            {
                math.Upload(replayInput, changed);
                var profile = untimed.ExecuteProfiled();
                Require(profile.Result.GpuMilliseconds is null && profile.Submission is not null,
                    "Untimed replay must preserve CPU profiling without reporting GPU time.");
                float[] actual = math.Read(replayOutput);
                float[] reference = changed.Select((value, index) => Silu(value + new[] { .1f, -.2f, .3f }[index])).ToArray();
                Require(actual.Zip(reference, (value, target) => Math.Abs(value - target)).Max() < 2e-6,
                    "Untimed replay ignored changed resident input.");
            }
            measurements.Add(new("prepared-untimestamped-replay", 3, 0, 1, null));
        }
        using (var prepared = math.Prepare(replayBatch))
        {
            // Appending to the source plan must not mutate the recorded snapshot.
            replayBatch.Activate(replayOutput, replayOutput, TensorActivation.Silu);
            foreach (float[] changed in new[] { new[] { -2f, 0f, 2f }, new[] { -3f, 1f, 4f } })
            {
                math.Upload(replayInput, changed);
                var execution = prepared.Execute();
                Require(execution.Dispatches == 1, "Prepared dispatch snapshot changed.");
                float[] actual = math.Read(replayOutput);
                float[] reference = changed.Select((value, index) => Silu(value + new[] { .1f, -.2f, .3f }[index])).ToArray();
                Require(actual.Zip(reference, (value, target) => Math.Abs(value - target)).Max() < 2e-6,
                    "Prepared replay ignored changed resident inputs or source-plan mutation.");
            }
            replayOutput.Dispose();
            bool rejected = false;
            try
            {
                prepared.Execute();
            }
            catch (ArgumentException error) when (error.Message.Contains("AUR-COMPUTE-MATH-001", StringComparison.Ordinal))
            {
                rejected = true;
            }
            Require(rejected, "Prepared replay submitted a disposed tensor.");
            measurements.Add(new("prepared-replay-lifecycle", 3, 0, 1, null));
        }

        foreach (int width in new[] { 1, 63, 64, 65, 127, 1024 })
        {
            float[] values = Values(3 * width, width).Select(value => value * 20 + 1000).ToArray();
            var input = Tensor(3, width, values);
            var probabilities = Tensor(3, width);
            batch = math.CreateBatch();
            batch.ReduceRows(input, probabilities);
            Check("softmax-width-" + width, batch, probabilities, Softmax(values, 3, width));
            foreach (var operation in new[] { RowReduction.Sum, RowReduction.Maximum })
            {
                var result = Tensor(3, 1);
                batch = math.CreateBatch();
                batch.ReduceRows(input, result, operation);
                float[] reference = Enumerable.Range(0, 3).Select(row => operation == RowReduction.Sum
                    ? (float)values.Skip(row * width).Take(width).Sum(value => (double)value)
                    : values.Skip(row * width).Take(width).Max()).ToArray();
                Check(operation + "-width-" + width, batch, result, reference, .13);
            }
        }

        foreach (int width in new[] { 7, 65, 384, 768, 1024 })
        {
            float[] values = Values(3 * width, width).Select(value => value * 10 + 1000).ToArray();
            foreach (var operation in Enum.GetValues<Normalization>())
            {
                float[] weights = Enumerable.Repeat(1f, width).ToArray();
                if (operation == Normalization.LayerNorm)
                {
                    weights = [.. weights, .. Enumerable.Repeat(.1f, width)];
                }
                var input = Tensor(3, width, values);
                var gammaBeta = Tensor(1, weights.Length, weights);
                var result = Tensor(3, width);
                batch = math.CreateBatch();
                batch.Normalize(input, gammaBeta, result, operation, 1e-5f, specializeWidth: false);
                Check(operation + "-width-" + width, batch, result,
                    Normalize(values, 3, width, operation, .1f), .001);
                if (width is 384 or 768 && operation == Normalization.LayerNorm)
                {
                    var specialized = math.CreateBatch();
                    specialized.Normalize(input, gammaBeta, input, operation);
                    Check($"layernorm{width}-in-place", specialized, input, Normalize(values, 3, width, operation, .1f), .001);
                    Require(math.Read(input).SequenceEqual(math.Read(result)), "Width-specialized LayerNorm changed the generic FP32 result.");
                }
            }
        }

        float[] activationValues = [-100, -20, -3, -1, -.01f, 0, .01f, 1, 3, 20, 100, 2, -2, 4, -4];
        var activationInput = Tensor(3, 5, activationValues);
        float[] auxiliaryValues = Values(15, 17);
        var auxiliary = Tensor(3, 5, auxiliaryValues);
        var bias = Tensor(1, 5, auxiliaryValues[..5]);
        foreach (var operation in Enum.GetValues<TensorActivation>())
        {
            var result = Tensor(3, 5);
            batch = math.CreateBatch();
            if (operation == TensorActivation.BiasResidual)
            {
                math.Upload(result, auxiliaryValues);
            }
            bool channelBias = operation is TensorActivation.Bias or TensorActivation.BiasSilu
                or TensorActivation.BiasGelu or TensorActivation.BiasResidual;
            batch.Activate(activationInput, result, operation, channelBias ? bias : auxiliary);
            expected = activationValues.Select((value, index) => operation switch
            {
                TensorActivation.Silu => Silu(value),
                TensorActivation.Gelu => ReferenceGelu(value),
                TensorActivation.SiluGate => Silu(value) * auxiliaryValues[index],
                TensorActivation.Residual => value + auxiliaryValues[index],
                TensorActivation.Bias => value + auxiliaryValues[index % 5],
                TensorActivation.BiasResidual => auxiliaryValues[index] + (value + auxiliaryValues[index % 5]),
                TensorActivation.BiasGelu => ReferenceGelu(value + auxiliaryValues[index % 5]),
                TensorActivation.BiasSilu => Silu(value + auxiliaryValues[index % 5]),
                TensorActivation.GeluErf => Trellis2KernelProof.GeluErfReference(value),
                TensorActivation.GeluGate => Trellis2BlockReference.Round(Trellis2BlockReference.Round(
                    (float)(.5 * value * (1 + Math.Tanh(.7978845608 * (value + .044715 * value * value * value)))))
                    * auxiliaryValues[index]),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            }).ToArray();
            Check("activation-" + operation, batch, result, expected);
        }

        float[] ropeValues = Values(3 * 8, 19);
        var ropeInput = Tensor(3, 8, ropeValues);
        foreach (var layout in Enum.GetValues<RopeLayout>())
        {
            var result = Tensor(3, 8);
            batch = math.CreateBatch();
            batch.RotatePositions(ropeInput, result, 11, layout: layout);
            Check("rope-" + layout, batch, result, Rope(ropeValues, 3, 8, 11, layout));
        }

        foreach (int length in new[] { 1, 2, 16, 32, 64, 256 })
        {
            float[] signal = Values(2 * length * 2, length);
            var input = Tensor(2, length, signal, GpuTensorFormat.Complex32);
            var transform = Tensor(2, length, format: GpuTensorFormat.Complex32);
            var restored = Tensor(2, length, format: GpuTensorFormat.Complex32);
            var scratch = Tensor(2, length, format: GpuTensorFormat.Complex32);
            batch = math.CreateBatch();
            batch.Fft(input, transform, scratch);
            Check("fft-forward-" + length, batch, transform, Dft(signal, 2, length), .001);
            batch = math.CreateBatch();
            batch.Fft(transform, restored, scratch, inverse: true);
            Check("fft-inverse-" + length, batch, restored, signal);
        }
        float[] longSignal = Values(65536 * 2, 23);
        var longInput = Tensor(1, 65536, longSignal, GpuTensorFormat.Complex32);
        var longTransform = Tensor(1, 65536, format: GpuTensorFormat.Complex32);
        var longResult = Tensor(1, 65536, format: GpuTensorFormat.Complex32);
        var longScratch = Tensor(1, 65536, format: GpuTensorFormat.Complex32);
        batch = math.CreateBatch();
        batch.Fft(longInput, longTransform, longScratch);
        batch.Fft(longTransform, longResult, longScratch, inverse: true);
        Check("fft-roundtrip-65536", batch, longResult, longSignal, .001);

        foreach (int stride in new[] { 1, 2 })
        {
            const int width = 7, height = 5, channels = 2, outputs = 3;
            float[] image = Values(width * height * channels, 29);
            float[] weights = Values(3 * 3 * channels * outputs, 31).Select(value => value * .1f).ToArray();
            int outputWidth = (width - 1) / stride + 1;
            int outputHeight = (height - 1) / stride + 1;
            var imageTensor = Tensor(height, width * channels, image);
            var weightsTensor = Tensor(18, outputs, weights);
            var columns = Tensor(outputHeight * outputWidth, 18);
            var result = Tensor(outputHeight * outputWidth, outputs);
            batch = math.CreateBatch();
            batch.Convolve(imageTensor, weightsTensor, columns, result, width, height, channels, 3, 3, 1, stride);
            Check("convolution-NHWC-stride-" + stride, batch, result,
                Convolution(image, weights, width, height, channels, outputs, stride));
        }

        float[] qValues = Values(5 * 7, 37), kValues = Values(6 * 7, 41), vValues = Values(6 * 9, 43);
        var q = Tensor(5, 7, qValues);
        var k = Tensor(6, 7, kValues);
        var v = Tensor(6, 9, vValues);
        var scores = Tensor(5, 6);
        var probabilitiesTensor = Tensor(5, 6);
        var attentionOutput = Tensor(5, 9);
        foreach (bool causal in new[] { false, true })
        {
            batch = math.CreateBatch();
            batch.Attention(q, k, v, scores, probabilitiesTensor, attentionOutput, causal, 1);
            Check("attention-" + (causal ? "causal" : "unmasked"), batch, attentionOutput,
                Attention(qValues, kValues, vValues, 5, 6, 7, 9, causal, 1));
        }

        // Two interleaved heads, different query/key lengths and partial tiles in both dimensions.
        const int fusedQueries = 19, fusedKeys = 71, fusedWidth = 128;
        float[] fusedQ = Values(fusedQueries * fusedWidth, 101);
        float[] fusedK = Values(fusedKeys * fusedWidth, 103);
        float[] fusedV = Values(fusedKeys * fusedWidth, 107);
        var fusedExpected = new float[fusedQ.Length];
        for (int head = 0; head < 2; head++)
        {
            float[] SliceHead(float[] data) => data.Where((_, index) => index % fusedWidth / 64 == head).ToArray();
            float[] reference = Attention(SliceHead(fusedQ), SliceHead(fusedK), SliceHead(fusedV),
                fusedQueries, fusedKeys, 64, 64, false, 0);
            for (int row = 0; row < fusedQueries; row++)
            {
                Array.Copy(reference, row * 64, fusedExpected, row * fusedWidth + head * 64, 64);
            }
        }
        var fusedQuery = Tensor(fusedQueries, fusedWidth, fusedQ);
        var fusedCache = Tensor(2 * fusedKeys, fusedWidth, [.. fusedK, .. fusedV]);
        var fusedOutput = Tensor(fusedQueries, fusedWidth);
        batch = math.CreateBatch();
        batch.FusedAttention64(fusedQuery, fusedCache, fusedOutput);
        Check("fused-attention64-interleaved-tails", batch, fusedOutput, fusedExpected);
        ExpectRejected(() => math.CreateBatch().FusedAttention64(fusedQuery, fusedCache, fusedQuery));

        // Three query tiles span the 32-key boundary. Poison the unused cache capacity.
        const int promptRows = 35;
        const int cacheCapacity = 47;
        float[] promptQ = Values(promptRows * 64, 119);
        float[] promptK = Values(promptRows * 64, 121);
        float[] promptV = Values(promptRows * 64, 123);
        float[] cacheData = Enumerable.Repeat(123f, 2 * cacheCapacity * 64).ToArray();
        promptK.CopyTo(cacheData, 0);
        promptV.CopyTo(cacheData, cacheCapacity * 64);
        var scaledPrompt = Tensor(promptRows, 64, promptQ.Select(value => value * 0.125f).ToArray());
        var promptCache = Tensor(2 * cacheCapacity, 64, cacheData);
        var promptOutput = Tensor(promptRows, 64);
        batch = math.CreateBatch();
        batch.CausalPrefillAttention64(scaledPrompt, promptCache, promptOutput);
        Check("causal-prefill-scaled-query-padded-cache", batch, promptOutput,
            Attention(promptQ, promptK, promptV, promptRows, promptRows, 64, 64, true, 0));
        ExpectRejected(() => math.CreateBatch().CausalPrefillAttention64(scaledPrompt, promptCache, scaledPrompt));

        var halfQuery = Tensor(32, fusedWidth, format: GpuTensorFormat.PackedFloat16);
        var halfCache = Tensor(144, fusedWidth, format: GpuTensorFormat.PackedFloat16);
        batch = math.CreateBatch();
        batch.PackMatrixHalf(fusedQuery, halfQuery);
        batch.PackMatrixHalf(fusedCache, halfCache);
        batch.FusedAttention64Packed(halfQuery, halfCache, fusedOutput, fusedKeys);
        Check("packed-attention64-interleaved-tails", batch, fusedOutput, fusedExpected, 0.001);
        if (math.CooperativeMatrixEnabled)
        {
            batch = math.CreateBatch();
            batch.FusedAttention64Packed(halfQuery, halfCache, fusedOutput, fusedKeys, cooperative: true);
            Check("cooperative-attention64-interleaved-tails", batch, fusedOutput, fusedExpected, 0.001);
            foreach (int tileWidth in new[] { 32, 64 })
            {
                batch = math.CreateBatch();
                batch.CooperativeAttention64(halfQuery, halfCache, fusedOutput, fusedKeys, tileWidth);
                Check($"cooperative-attention-{tileWidth}-key-tails", batch, fusedOutput, fusedExpected, 0.001);
            }
        }

        // The fused decoder writes two bindings, including only the selected cache row in each half.
        const int qkvWidth = 65;
        float[] qkvValues = Values(3 * qkvWidth, 151);
        var qkv = Tensor(1, 3 * qkvWidth, qkvValues);
        var kv = Tensor(6, qkvWidth, new float[6 * qkvWidth]);
        var scaledQuery = Tensor(1, qkvWidth);
        float[] gatherSource = Enumerable.Range(0, 15).Select(i => (i - 6) / 7f).ToArray();
        using var gatherPacked = Tensor(3, 5, gatherSource, GpuTensorFormat.PackedFloat16);
        using var gathered = Tensor(2, 3);
        var gatherBatch = math.CreateBatch();
        gatherBatch.Gather(gatherPacked, gathered, firstRow: 1, firstColumn: 1);
        Check("packed-half-gather", gatherBatch, gathered,
            new[] { 6, 7, 8, 11, 12, 13 }.Select(i => (float)(Half)gatherSource[i]).ToArray(), 0);

        if (plant.ComputeQueue.HasValue)
        {
            CheckPrefetchSlots(math);
            measurements.Add(new("cross-queue-prefetch-overwrite", 4, 0, 4, null));
        }

        var cacheCopy = Tensor(6, qkvWidth);
        batch = math.CreateBatch();
        batch.DecodeQkv(qkv, kv, scaledQuery, 1, 0.125f);
        batch.Gather(kv, cacheCopy);
        ExpectRejected(() => math.Prepare(batch, new PreparedComputeOptions(ElideIndependentBarriers: true)));
        float[] expectedCache = new float[6 * qkvWidth];
        Array.Copy(qkvValues, qkvWidth, expectedCache, qkvWidth, qkvWidth);
        Array.Copy(qkvValues, 2 * qkvWidth, expectedCache, 4 * qkvWidth, qkvWidth);
        Check("decode-qkv-cache-append", batch, cacheCopy, expectedCache, 0);
        Require(math.Read(scaledQuery).SequenceEqual(qkvValues.Take(qkvWidth).Select(value => value * 0.125f)),
            "Fused decoder returned the wrong scaled query.");

        // A tiny random-weight transformer-style chain, not a model loader or claim
        // of model inference: RMSNorm -> Q/K/V -> RoPE -> attention -> gated FFN -> residual.
        float[] xValues = Values(5 * 8, 47);
        float[] projection = Values(8 * 8, 53).Select(value => value * .15f).ToArray();
        float[] upWeights = Values(8 * 16, 59).Select(value => value * .15f).ToArray();
        float[] downWeights = Values(16 * 8, 61).Select(value => value * .15f).ToArray();
        var x = Tensor(5, 8, xValues);
        var norm = Tensor(5, 8);
        var gamma = Tensor(1, 8, Enumerable.Repeat(1f, 8).ToArray());
        var w = Tensor(8, 8, projection);
        var q0 = Tensor(5, 8);
        var k0 = Tensor(5, 8);
        var v0 = Tensor(5, 8);
        var qr = Tensor(5, 8);
        var kr = Tensor(5, 8);
        var s0 = Tensor(5, 5);
        var p0 = Tensor(5, 5);
        var attended = Tensor(5, 8);
        var upWeight = Tensor(8, 16, upWeights);
        var downWeight = Tensor(16, 8, downWeights);
        var gate = Tensor(5, 16);
        var up = Tensor(5, 16);
        var hidden = Tensor(5, 16);
        var down = Tensor(5, 8);
        var final = Tensor(5, 8);
        batch = math.CreateBatch();
        batch.Normalize(x, gamma, norm);
        batch.Multiply(norm, w, q0);
        batch.Multiply(norm, w, k0);
        batch.Multiply(norm, w, v0);
        batch.RotatePositions(q0, qr);
        batch.RotatePositions(k0, kr);
        batch.Attention(qr, kr, v0, s0, p0, attended);
        batch.Multiply(attended, upWeight, gate);
        batch.Multiply(attended, upWeight, up);
        batch.Activate(gate, hidden, TensorActivation.SiluGate, up);
        batch.Multiply(hidden, downWeight, down);
        batch.Activate(down, final, TensorActivation.Residual, x);
        var projected = Multiply(Normalize(xValues, 5, 8, Normalization.RmsNorm), projection, 5, 8, 8);
        var rotated = Rope(projected, 5, 8, 0, RopeLayout.HalfSplit);
        var attendedValues = Attention(rotated, rotated, projected, 5, 5, 8, 8, true, 0);
        var gates = Multiply(attendedValues, upWeights, 5, 16, 8);
        var hiddenValues = gates.Select(value => Silu(value) * value).ToArray();
        expected = Multiply(hiddenValues, downWeights, 5, 8, 16).Zip(xValues, (left, right) => left + right).ToArray();
        Check("resident-transformer-style-chain", batch, final, expected);

        if (math.CooperativeMatrixEnabled)
        {
            foreach (var shape in new[] { (M: 16, N: 16, K: 16), (M: 32, N: 48, K: 64), (M: 256, N: 256, K: 256) })
            {
                float[] left = Values(shape.M * shape.K, 67).Select(value => (float)(Half)value).ToArray();
                float[] right = Values(shape.K * shape.N, 71).Select(value => (float)(Half)value).ToArray();
                var leftTensor = Tensor(shape.M, shape.K, left, GpuTensorFormat.PackedFloat16);
                var rightTensor = Tensor(shape.K, shape.N, right, GpuTensorFormat.PackedFloat16);
                var result = Tensor(shape.M, shape.N);
                batch = math.CreateBatch();
                batch.MultiplyCooperative(leftTensor, rightTensor, result);
                float[] reference = Multiply(left, right, shape.M, shape.N, shape.K);
                Check($"cooperative-f16-f32-{shape.M}x{shape.N}x{shape.K}", batch, result, reference, .005);
                batch = math.CreateBatch();
                batch.ProjectCooperativeTiles(leftTensor, rightTensor, result, reuseTiles: false);
                Check($"direct-packed-projection-{shape.M}x{shape.N}x{shape.K}", batch, result, reference, .005);
                batch = math.CreateBatch();
                if (shape.M % 32 == 0 && shape.N % 32 == 0)
                {
                    batch.ProjectCooperativeTiles(leftTensor, rightTensor, result, reuseTiles: true);
                    Check($"reused-packed-projection-{shape.M}x{shape.N}x{shape.K}", batch, result, reference, .005);
                }
                else
                {
                    ExpectRejected(() => batch.ProjectCooperativeTiles(leftTensor, rightTensor, result, reuseTiles: true));
                }
            }
        }
        else
        {
            var halfA = Tensor(16, 16, format: GpuTensorFormat.PackedFloat16);
            var halfB = Tensor(16, 16, format: GpuTensorFormat.PackedFloat16);
            ExpectRejected(() => math.CreateBatch().MultiplyCooperative(halfA, halfB, Tensor(16, 16)));
        }

        ExpectRejected(() => math.CreateBatch().Multiply(a, b, a));
        ExpectRejected(() => math.CreateBatch().ReduceRows(Tensor(1, 8193), Tensor(1, 8193)));
        ExpectRejected(() => math.CreateBatch().Fft(Tensor(1, 3, format: GpuTensorFormat.Complex32), longResult, longScratch));
        var dead = Tensor(1, 1);
        dead.Dispose();
        ExpectRejected(() => math.CreateBatch().Activate(dead, Tensor(1, 1), TensorActivation.Silu));
        using var tiny = new VulkanComputeMath(plant, assets.ComputeShader, maximumDispatches: 1);
        var tinyA = tiny.CreateTensor(1, 129);
        var tinyB = tiny.CreateTensor(129, 1);
        var tinyC = tiny.CreateTensor(1, 1);
        var tooLarge = tiny.CreateBatch();
        ExpectRejected(() => tooLarge.Multiply(tinyA, tinyB, tinyC));
        Require(tooLarge.DispatchCount == 0, "Rejected operations partially mutated the dispatch plan.");
        ExpectRejected(() => tiny.CreateBatch().Activate(x, tinyC, TensorActivation.Silu));

        float[] benchLeft = Values(256 * 256, 73), benchRight = Values(256 * 256, 79);
        var benchA = Tensor(256, 256, benchLeft);
        var benchB = Tensor(256, 256, benchRight);
        var benchC = Tensor(256, 256);
        batch = math.CreateBatch();
        batch.Multiply(benchA, benchB, benchC);
        Check("sgemm-256-benchmark-reference", batch, benchC, Multiply(benchLeft, benchRight, 256, 256, 256), .002);
        for (int frame = 0; frame < 12; frame++)
        {
            var measured = math.Execute(batch);
            if (frame >= 2)
            {
                measurements.Add(new("sgemm-256-warm", 65536, null, measured.Dispatches, measured.GpuMilliseconds));
            }
        }
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName,
            plant.Facts.CooperativeMatrixEnabled, plant.Facts.CooperativeMatrixDiagnostic,
            plant.Facts.EnabledValidationLayers, GPUResidentChain = true,
            Measurements = measurements,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_COMPUTE_MATH_PASSED " + output);
    }

    private static float[] Values(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
    }

    private static float[] Multiply(float[] a, float[] b, int rows, int columns, int inner)
    {
        var result = new float[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    sum += (double)a[row * inner + k] * b[k * columns + column];
                }
                result[row * columns + column] = (float)sum;
            }
        }
        return result;
    }

    private static float[] Softmax(float[] values, int rows, int columns)
    {
        var result = new float[values.Length];
        for (int row = 0; row < rows; row++)
        {
            double maximum = values.Skip(row * columns).Take(columns).Max();
            double denominator = values.Skip(row * columns).Take(columns).Sum(value => Math.Exp(value - maximum));
            for (int column = 0; column < columns; column++)
            {
                result[row * columns + column] = (float)(Math.Exp(values[row * columns + column] - maximum) / denominator);
            }
        }
        return result;
    }

    private static float[] Normalize(float[] values, int rows, int columns, Normalization kind, float bias = 0)
    {
        var result = new float[values.Length];
        for (int row = 0; row < rows; row++)
        {
            var slice = values.Skip(row * columns).Take(columns).ToArray();
            double mean = kind == Normalization.LayerNorm ? slice.Average(value => (double)value) : 0;
            double variance = slice.Average(value => (value - mean) * (value - mean));
            for (int column = 0; column < columns; column++)
            {
                result[row * columns + column] = (float)((slice[column] - mean) / Math.Sqrt(variance + 1e-5)
                    + (kind == Normalization.LayerNorm ? bias : 0));
            }
        }
        return result;
    }

    private static float Silu(float value) => (float)(value / (1 + Math.Exp(-value)));

    private static float[] Rope(float[] values, int rows, int columns, int offset, RopeLayout layout)
    {
        var result = new float[values.Length];
        for (int row = 0; row < rows; row++)
        {
            for (int pair = 0; pair < columns / 2; pair++)
            {
                int first = row * columns + (layout == RopeLayout.HalfSplit ? pair : pair * 2);
                int second = first + (layout == RopeLayout.HalfSplit ? columns / 2 : 1);
                double angle = (row + offset) * Math.Pow(10000, -2.0 * pair / columns);
                result[first] = (float)(values[first] * Math.Cos(angle) - values[second] * Math.Sin(angle));
                result[second] = (float)(values[first] * Math.Sin(angle) + values[second] * Math.Cos(angle));
            }
        }
        return result;
    }

    private static float[] Dft(float[] values, int batches, int length)
    {
        var result = new float[values.Length];
        for (int batch = 0; batch < batches; batch++)
        {
            for (int frequency = 0; frequency < length; frequency++)
            {
                Complex sum = Complex.Zero;
                for (int sample = 0; sample < length; sample++)
                {
                    int source = (batch * length + sample) * 2;
                    double angle = -2 * Math.PI * frequency * sample / length;
                    sum += new Complex(values[source], values[source + 1]) * new Complex(Math.Cos(angle), Math.Sin(angle));
                }
                int target = (batch * length + frequency) * 2;
                result[target] = (float)sum.Real;
                result[target + 1] = (float)sum.Imaginary;
            }
        }
        return result;
    }

    private static float[] Convolution(float[] image, float[] weights, int width, int height, int channels, int outputs, int stride)
    {
        int resultWidth = (width - 1) / stride + 1;
        int resultHeight = (height - 1) / stride + 1;
        var result = new float[resultWidth * resultHeight * outputs];
        for (int y = 0; y < resultHeight; y++)
        {
            for (int x = 0; x < resultWidth; x++)
            {
                for (int output = 0; output < outputs; output++)
                {
                    double sum = 0;
                    for (int ky = 0; ky < 3; ky++)
                    {
                        for (int kx = 0; kx < 3; kx++)
                        {
                            int sourceX = x * stride + kx - 1;
                            int sourceY = y * stride + ky - 1;
                            if (sourceX < 0 || sourceY < 0 || sourceX >= width || sourceY >= height)
                            {
                                continue;
                            }
                            for (int channel = 0; channel < channels; channel++)
                            {
                                sum += image[(sourceY * width + sourceX) * channels + channel]
                                    * (double)weights[((ky * 3 + kx) * channels + channel) * outputs + output];
                            }
                        }
                    }
                    result[(y * resultWidth + x) * outputs + output] = (float)sum;
                }
            }
        }
        return result;
    }

    private static float ReferenceGelu(float value)
        => (float)(.5 * value * (1 + Math.Tanh(.7978845608 * (value + .044715 * value * value * value))));

    private static float[] Attention(float[] query, float[] key, float[] value, int queries, int keys, int width,
        int outputWidth, bool causal, int offset)
    {
        var transposedKey = new float[key.Length];
        for (int row = 0; row < keys; row++)
        {
            for (int column = 0; column < width; column++)
            {
                transposedKey[column * keys + row] = key[row * width + column];
            }
        }
        var scores = Multiply(query, transposedKey, queries, keys, width);
        for (int row = 0; row < queries; row++)
        {
            for (int column = 0; column < keys; column++)
            {
                scores[row * keys + column] = causal && column > row + offset ? -1e30f : scores[row * keys + column] / MathF.Sqrt(width);
            }
        }
        return Multiply(Softmax(scores, queries, keys), value, queries, outputWidth, keys);
    }

    private sealed class TwoSlots : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast forecast) => 2;
        public void Observe(ComputeStreamObservation observation) { }
    }

    private static void CheckPrefetchSlots(VulkanComputeMath math)
    {
        using var first = math.CreateTensor(1, 1);
        using var second = math.CreateTensor(1, 1);
        using var output = math.CreateTensor(1, 4);
        using var queue = math.BeginQueuedScope(new TwoSlots(), new(4, 1, sizeof(float)));
        for (int i = 0; i < 4; i++)
        {
            int slot = i % 2;
            var destination = slot == 0 ? first : second;
            var upload = new ComputeBatchUpload([destination], BitConverter.GetBytes((float)(i + 1)));
            queue.Prefetch(upload, slot);
            if (i == 0)
            {
                ExpectRejected(() => queue.Prefetch(upload, slot));
                ExpectRejected(() => queue.Prefetch(upload, 1));
            }
            queue.JoinPrefetch(slot);
            var batch = math.CreateBatch();
            batch.Scatter(destination, output, firstColumn: i);
            math.Execute(batch);
        }
        Require(math.Read(output).SequenceEqual(new float[] { 1, 2, 3, 4 }),
            "Prefetch consumer observed an old or prematurely overwritten slot.");
        Require(queue.Metrics.PrefetchSubmissions == 4 && queue.Metrics.UploadedBytes == 16,
            "Prefetch copies are missing from queue telemetry.");
    }

    private static void ExpectRejected(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001:", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("Expected bounded compute contract rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
