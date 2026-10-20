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

        var replayInput = Tensor(1, 3, [-2f, 0f, 2f]);
        var replayBias = Tensor(1, 3, [.1f, -.2f, .3f]);
        var replayOutput = Tensor(1, 3);
        var replayBatch = math.CreateBatch();
        replayBatch.Activate(replayInput, replayOutput, TensorActivation.BiasSilu, replayBias);
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

        foreach (int width in new[] { 7, 65, 1024 })
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
                batch.Normalize(input, gammaBeta, result, operation);
                Check(operation + "-width-" + width, batch, result,
                    Normalize(values, 3, width, operation, .1f), .001);
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
            batch.Activate(activationInput, result, operation,
                operation is TensorActivation.Bias or TensorActivation.BiasSilu ? bias : auxiliary);
            expected = activationValues.Select((value, index) => operation switch
            {
                TensorActivation.Silu => Silu(value),
                TensorActivation.Gelu => (float)(.5 * value * (1 + Math.Tanh(.7978845608 * (value + .044715 * value * value * value)))),
                TensorActivation.SiluGate => Silu(value) * auxiliaryValues[index],
                TensorActivation.Residual => value + auxiliaryValues[index],
                TensorActivation.Bias => value + auxiliaryValues[index % 5],
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
                Check($"cooperative-f16-f32-{shape.M}x{shape.N}x{shape.K}", batch, result,
                    Multiply(left, right, shape.M, shape.N, shape.K), .005);
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
