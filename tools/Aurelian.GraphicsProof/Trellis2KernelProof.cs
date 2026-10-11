using Aurelian.Games;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2KernelProof
{
    public static IReadOnlyList<object> Run(AurelianVulkanPlant plant, GameAssets assets)
    {
        var results = new List<object>();
        using (var math = new VulkanComputeMath(plant, assets.ComputeShader))
        {
            Rounding(math, results);
            foreach (int width in new[] { 1536, 8192 })
            {
                WideNormalization(math, width, results);
                WideSoftmax(math, width, results);
            }
            HeadNormalizationAndRope(math, results);
            FullKeyAttention(math, results);
            FullKeyAttention(math, results, queries: 17, keys: 1029, width: 64, rounded: false);
            DinoRotaryAndResidual(math, results);
            ErfActivation(math, results);
            RejectedShapes(math);
        }
        LargeDispatch(plant, assets, results);
        return results;
    }

    private static void Rounding(VulkanComputeMath math, List<object> results)
    {
        // Both parities at halfway boundaries, signs, zeros, exponent boundaries.
        float[] values = [0, -0f, 1, 1.00390625f, 1.01171875f, -1.00390625f,
            -1.01171875f, 0.999f, 255.5f, 256.5f, 65504, -65504, 1e-20f, -1e-20f];
        using var input = Tensor(math, 1, values.Length, values);
        using var output = math.CreateTensor(1, values.Length);
        var batch = math.CreateBatch();
        batch.RoundBFloat16(input, output);
        math.Execute(batch);
        float[] actual = math.Read(output);
        for (int index = 0; index < values.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(actual[index])
                != BitConverter.SingleToUInt32Bits(Trellis2BlockReference.Round(values[index])))
            {
                throw new InvalidOperationException("BF16 rounding differs at index " + index);
            }
        }
        results.Add(new { Operation = "BF16 ties-to-even", Values = values.Length, MaximumError = 0 });
    }

    private static void WideNormalization(VulkanComputeMath math, int width, List<object> results)
    {
        float[] values = Enumerable.Range(0, width * 3).Select(index => MathF.Sin(index * 0.03f) * 2 + 4).ToArray();
        float[] gamma = Enumerable.Range(0, width).Select(index => 0.8f + index % 7 * 0.04f).ToArray();
        float[] beta = Enumerable.Range(0, width).Select(index => index % 5 * 0.03f).ToArray();
        using var input = Tensor(math, 3, width, values);
        using var affine = Tensor(math, 1, width * 2, [.. gamma, .. beta]);
        using var output = math.CreateTensor(3, width);
        var batch = math.CreateBatch();
        batch.Normalize(input, affine, output, Normalization.LayerNorm, 1e-6f);
        math.Execute(batch);
        var expected = new float[values.Length];
        for (int row = 0; row < 3; row++)
        {
            double mean = values.Skip(row * width).Take(width).Average(value => (double)value);
            double variance = values.Skip(row * width).Take(width).Average(value => Math.Pow(value - mean, 2));
            for (int column = 0; column < width; column++)
            {
                expected[row * width + column] = (float)((values[row * width + column] - mean)
                    / Math.Sqrt(variance + 1e-6) * gamma[column] + beta[column]);
            }
        }
        Compare(math.Read(output), expected, 0.00002, "LayerNorm width " + width, results);
    }

    private static void WideSoftmax(VulkanComputeMath math, int width, List<object> results)
    {
        float[] values = Enumerable.Range(0, width * 2).Select(index => 1024 + MathF.Sin(index * 0.02f) * 12).ToArray();
        using var input = Tensor(math, 2, width, values);
        using var output = math.CreateTensor(2, width);
        var batch = math.CreateBatch();
        batch.ReduceRows(input, output);
        math.Execute(batch);
        var expected = new float[values.Length];
        for (int row = 0; row < 2; row++)
        {
            double maximum = values.Skip(row * width).Take(width).Max();
            double denominator = values.Skip(row * width).Take(width).Sum(value => Math.Exp(value - maximum));
            for (int column = 0; column < width; column++)
            {
                expected[row * width + column] = (float)(Math.Exp(values[row * width + column] - maximum) / denominator);
            }
        }
        Compare(math.Read(output), expected, 0.000001, "Softmax width " + width, results);
    }

    private static void HeadNormalizationAndRope(VulkanComputeMath math, List<object> results)
    {
        const int width = 256;
        float[] values = Enumerable.Range(0, width * 2).Select(index => index < 128 ? 0 : MathF.Sin(index * 0.021f)).ToArray();
        float[] gamma = Enumerable.Range(0, width).Select(index => 0.8f + index % 7 * 0.04f).ToArray();
        using var input = Tensor(math, 2, width, values);
        using var weights = Tensor(math, 1, width, gamma);
        using var output = math.CreateTensor(2, width);
        var batch = math.CreateBatch();
        batch.NormalizeHeadsBFloat16(input, weights, input);
        batch.Rope3D(input, output, firstToken: 273);
        math.Execute(batch);
        var normalized = new float[values.Length];
        for (int first = 0; first < values.Length; first += 128)
        {
            double denominator = Math.Max(Math.Sqrt(values.Skip(first).Take(128).Sum(value => (double)value * value)), 1e-12);
            for (int lane = 0; lane < 128; lane++)
            {
                normalized[first + lane] = Trellis2BlockReference.Round((float)(values[first + lane]
                    / denominator * gamma[(first + lane) % width] * Math.Sqrt(128)));
            }
        }
        Compare(math.Read(input), normalized, 0.008, "128-wide L2 head normalization including zero head", results);
        var expected = new float[values.Length];
        for (int row = 0; row < 2; row++)
        {
            int token = row + 273;
            int[] coordinates = [token / 256, token / 16 % 16, token % 16];
            for (int head = 0; head < 2; head++)
            {
                for (int pair = 0; pair < 64; pair++)
                {
                    double phase = pair < 63 ? coordinates[pair / 21] / Math.Pow(10000, (double)(pair % 21) / 21) : 0;
                    int index = row * width + head * 128 + pair * 2;
                    expected[index] = Trellis2BlockReference.Round((float)(normalized[index] * Math.Cos(phase) - normalized[index + 1] * Math.Sin(phase)));
                    expected[index + 1] = Trellis2BlockReference.Round((float)(normalized[index] * Math.Sin(phase) + normalized[index + 1] * Math.Cos(phase)));
                }
            }
        }
        Compare(math.Read(output), expected, 0.008, "3D RoPE with XYZ coordinates and identity padding", results);
    }

    private static void FullKeyAttention(VulkanComputeMath math, List<object> results,
        int queries = 16, int keys = 4096, int width = 128, bool rounded = true)
    {
        float[] q = Enumerable.Range(0, queries * width).Select(index => MathF.Sin(index * 0.017f)).ToArray();
        float[] k = Enumerable.Range(0, keys * width).Select(index => MathF.Cos(index * 0.013f)).ToArray();
        float[] v = Enumerable.Range(0, keys * width).Select(index => MathF.Sin(index * 0.007f) * 0.5f).ToArray();
        using var query = Tensor(math, queries, width, q);
        using var key = Tensor(math, keys, width, k);
        using var value = Tensor(math, keys, width, v);
        using var output = math.CreateTensor(queries, width);
        int before = math.ReadbackCount;
        var execution = math.AttentionTiled(query, key, value, output, headWidth: width, roundOutput: rounded);
        if (math.ReadbackCount != before) { throw new InvalidOperationException("Attention read back an intermediate activation."); }
        var expected = new float[queries * width];
        Parallel.For(0, queries, row =>
        {
            var scores = new double[keys];
            for (int token = 0; token < keys; token++)
            {
                double sum = 0;
                for (int lane = 0; lane < width; lane++) { sum += (double)q[row * width + lane] * k[token * width + lane]; }
                scores[token] = sum / Math.Sqrt(width);
            }
            double maximum = scores.Max();
            for (int token = 0; token < keys; token++) { scores[token] = Math.Exp(scores[token] - maximum); }
            double denominator = scores.Sum();
            for (int lane = 0; lane < width; lane++)
            {
                double sum = 0;
                for (int token = 0; token < keys; token++) { sum += scores[token] * v[token * width + lane]; }
                float result = (float)(sum / denominator);
                expected[row * width + lane] = rounded ? Trellis2BlockReference.Round(result) : result;
            }
        });
        Compare(math.Read(output), expected, rounded ? 0.00025 : 0.00001,
            $"{queries} queries over all {keys} keys, head {width}, BF16 output {rounded}", results);
        results.Add(new { Operation = "Attention allocation", Execution = execution });
    }

    private static void DinoRotaryAndResidual(VulkanComputeMath math, List<object> results)
    {
        const int rows = 9;
        const int width = 128;
        float[] values = Enumerable.Range(0, rows * width).Select(index => MathF.Sin(index * 0.021f)).ToArray();
        using var input = Tensor(math, rows, width, values);
        using var output = math.CreateTensor(rows, width);
        var batch = math.CreateBatch();
        batch.Rope2D(input, output, grid: 2);
        math.Execute(batch);
        var expected = (float[])values.Clone();
        for (int row = 5; row < rows; row++)
        {
            for (int head = 0; head < 2; head++)
            {
                for (int pair = 0; pair < 32; pair++)
                {
                    int coordinate = pair < 16 ? (row - 5) / 2 : (row - 5) % 2;
                    double position = (coordinate + 0.5) / 2 * 2 - 1;
                    double phase = 2 * Math.PI * position / Math.Pow(100, (double)(pair % 16) / 16);
                    int first = row * width + head * 64 + pair;
                    expected[first] = (float)(values[first] * Math.Cos(phase) - values[first + 32] * Math.Sin(phase));
                    expected[first + 32] = (float)(values[first + 32] * Math.Cos(phase) + values[first] * Math.Sin(phase));
                }
            }
        }
        Compare(math.Read(output), expected, 0.000002, "DINO half-split Y/X RoPE, two heads and unchanged five-token prefix", results);
        float[] scale = Enumerable.Range(0, width).Select(index => (index % 11 - 5) * .17f).ToArray();
        using var scaleTensor = Tensor(math, 1, width, scale);
        using var packed = math.CreateTensor(rows, width + (width + rows - 1) / rows);
        var residual = math.CreateBatch();
        residual.AppendGate(output, scaleTensor, packed, 0);
        residual.ScaledResidual(input, packed, input);
        math.Execute(residual);
        float[] result = Enumerable.Range(0, values.Length).Select(index => values[index] + expected[index] * scale[index % width]).ToArray();
        Compare(math.Read(input), result, 0.000002, "FP32 per-channel scaled residual including negative/zero scales and input alias", results);
        Reject(() => math.CreateBatch().Rope2D(input, input, grid: 2));
        Reject(() => math.CreateBatch().Rope2D(input, output, grid: 3));
        Reject(() => math.AttentionTiled(input, input, input, output, headWidth: 32));
        Reject(() => math.CreateBatch().ScaledResidual(input, output, input));
    }

    private static void ErfActivation(VulkanComputeMath math, List<object> results)
    {
        float[] values = Enumerable.Range(0, 4097).Select(index => (index - 2048) / 128f).ToArray();
        using var input = Tensor(math, 1, values.Length, values);
        using var output = math.CreateTensor(1, values.Length);
        var batch = math.CreateBatch();
        batch.Activate(input, output, TensorActivation.GeluErf);
        math.Execute(batch);
        // Composite Simpson quadrature of erf, independent of the shader polynomial.
        float[] expected = values.Select(GeluErfReference).ToArray();
        Compare(math.Read(output), expected, 0.000002, "Error-function GELU across [-16,16] vs double quadrature", results);
    }

    internal static float GeluErfReference(float value)
    {
        double x = Math.Abs(value) / Math.Sqrt(2);
        const int steps = 1024;
        double delta = x / steps;
        double integral = 1 + Math.Exp(-x * x);
        for (int step = 1; step < steps; step++)
        {
            integral += (step % 2 == 0 ? 2 : 4) * Math.Exp(-Math.Pow(step * delta, 2));
        }
        double erf = integral * delta / 3 * 2 / Math.Sqrt(Math.PI);
        return (float)(0.5 * value * (1 + Math.CopySign(erf, value)));
    }

    private static void LargeDispatch(AurelianVulkanPlant plant, GameAssets assets, List<object> results)
    {
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 8388608);
        const int rows = 513;
        const int columns = 8192;
        float[] values = Enumerable.Range(0, rows * columns).Select(index => (index % 101 - 50) / 17f).ToArray();
        using var input = Tensor(math, rows, columns, values);
        using var output = math.CreateTensor(rows, columns);
        var batch = math.CreateBatch();
        batch.Activate(input, output, TensorActivation.Silu);
        batch.RoundBFloat16(output, output);
        math.Execute(batch);
        float[] expected = values.Select(value => Trellis2BlockReference.Round((float)(value / (1 + Math.Exp(-value))))).ToArray();
        Compare(math.Read(output), expected, 0.016, "2D dispatch across 65535 workgroups", results);
    }

    private static void RejectedShapes(VulkanComputeMath math)
    {
        Reject(() => math.CreateTensor(4096, 1536)); // Default remains bounded: larger owners must opt in.
        using var input = math.CreateTensor(2, 128);
        using var output = math.CreateTensor(2, 128);
        using var gamma = math.CreateTensor(1, 127);
        Reject(() => math.CreateBatch().NormalizeHeadsBFloat16(input, gamma, output));
        Reject(() => math.CreateBatch().Rope3D(input, input));
        Reject(() => math.CreateBatch().Rope3D(input, output, firstToken: 4095));
        Reject(() => math.CreateBatch().Gather(input, output, firstRow: 1));
        Reject(() => math.CreateBatch().ScatterRounded(input, output, firstColumn: 1));
        Reject(() => math.AttentionTiled(input, input, input, output, queryTileRows: 0));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Invalid tensor contract was admitted.");
    }

    private static GpuTensor Tensor(VulkanComputeMath math, int rows, int columns, float[] values)
    {
        var tensor = math.CreateTensor(rows, columns);
        math.Upload(tensor, values);
        return tensor;
    }

    private static void Compare(float[] actual, float[] expected, double tolerance, string operation, List<object> results)
    {
        double maximum = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
        if (actual.Length != expected.Length || actual.Any(value => !float.IsFinite(value)) || maximum > tolerance)
        {
            throw new InvalidOperationException($"{operation}: error {maximum:G6} exceeds {tolerance:G6}.");
        }
        Console.WriteLine($"{operation}: error {maximum:G6}");
        results.Add(new { Operation = operation, Values = actual.Length, MaximumError = maximum, Limit = tolerance });
    }
}
