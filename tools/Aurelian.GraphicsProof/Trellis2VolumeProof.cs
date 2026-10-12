using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2VolumeProof
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian volume kernel qualification"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 1048576);
        var checks = new List<object>();

        // Signed zero, subnormal tie cases, normal tie cases, carry into a new
        // exponent, largest finite half and the exact overflow boundary.
        float[] boundaries = [0, -0f, 1f / 33554432, -1f / 33554432,
            3f / 33554432, -3f / 33554432, 5f / 33554432,
            0.00006103515625f, 0.00006102f, 1.00048828125f, 1.00146484375f,
            1.99951171875f, -1.99951171875f, 65504, 65519, 65520, -65520];
        float[] values = [.. boundaries, .. Enumerable.Range(0, 257).Select(index => MathF.Sin(index * .17f) * 10)];
        using (var input = Tensor(1, values.Length, values))
        using (var result = math.CreateTensor(1, values.Length))
        {
            var batch = math.CreateBatch();
            batch.RoundFloat16(input, result);
            math.Execute(batch);
            float[] actual = math.Read(result);
            for (int index = 0; index < values.Length; index++)
            {
                float expected = (float)(Half)values[index];
                Require(BitConverter.SingleToUInt32Bits(actual[index]) == BitConverter.SingleToUInt32Bits(expected),
                    $"Half rounding mismatch at {index}: {values[index]} -> {actual[index]}, expected {expected}");
            }
            checks.Add(new { Name = "binary16-rounding", Values = values.Length, BitExact = true });
        }

        // Independently index the padded convolution by signed coordinates,
        // including all faces, corners, channels and output-channel tails.
        const int resolution = 3;
        const int channels = 2;
        const int outputs = 3;
        float[] voxels = Enumerable.Range(0, 27 * channels).Select(index => (index - 23) * .03f).ToArray();
        float[] weights = Enumerable.Range(0, outputs * channels * 27).Select(index => MathF.Cos(index * .37f) * .1f).ToArray();
        float[] bias = [.2f, -.1f, .05f];
        using (var input = Tensor(27, channels, voxels))
        using (var columns = math.CreateTensor(27, channels * 27))
        using (var result = math.CreateTensor(27, outputs))
        {
            var batch = math.CreateBatch();
            batch.VolumeColumns(input, columns, resolution, channels);
            math.Execute(batch);
            var plan = new StreamingLinearPlan(channels * 27, outputs, 2);
            math.LinearStreamed(columns, result, plan, tile =>
            {
                var packed = new float[plan.TileRows * (channels * 27 + 1)];
                for (int row = 0; row < tile.OutputCount; row++)
                {
                    Array.Copy(weights, (tile.FirstOutput + row) * channels * 27,
                        packed, row * (channels * 27 + 1), channels * 27);
                    packed[row * (channels * 27 + 1) + channels * 27] = bias[tile.FirstOutput + row];
                }
                return packed;
            });
            var expected = new float[27 * outputs];
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int z = 0; z < resolution; z++)
                    {
                        int voxel = (x * resolution + y) * resolution + z;
                        for (int target = 0; target < outputs; target++)
                        {
                            double sum = bias[target];
                            for (int source = 0; source < channels; source++)
                            {
                                for (int kernel = 0; kernel < 27; kernel++)
                                {
                                    int sx = x + kernel / 9 - 1;
                                    int sy = y + kernel / 3 % 3 - 1;
                                    int sz = z + kernel % 3 - 1;
                                    if (sx >= 0 && sx < resolution && sy >= 0 && sy < resolution && sz >= 0 && sz < resolution)
                                    {
                                        sum += (double)voxels[((sx * resolution + sy) * resolution + sz) * channels + source]
                                            * weights[(target * channels + source) * 27 + kernel];
                                    }
                                }
                            }
                            expected[voxel * outputs + target] = (float)sum;
                        }
                    }
                }
            }
            Compare("conv3d-padded-multichannel-tail", math.Read(result), expected, 2e-6);
            using var selected = math.CreateTensor(7, channels * 27);
            batch = math.CreateBatch();
            batch.VolumeColumns(input, selected, resolution, channels, firstVoxel: 19);
            math.Execute(batch);
            Compare("conv3d-selected-voxel-range", math.Read(selected), math.Read(columns)[(19 * channels * 27)..(26 * channels * 27)], 0);
            Reject(() => math.CreateBatch().VolumeColumns(input, selected, resolution, channels, firstVoxel: 21));
            Reject(() => math.CreateBatch().VolumeColumns(input, selected, resolution, channels, kernel: 2));
        }

        using (var input = Tensor(8, 16, Enumerable.Range(0, 128).Select(index => (float)index).ToArray()))
        using (var result = math.CreateTensor(64, 2))
        {
            var batch = math.CreateBatch();
            batch.PixelShuffle3D(input, result, resolution: 2, channels: 2);
            math.Execute(batch);
            var expected = new float[128];
            for (int voxel = 0; voxel < 8; voxel++)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    for (int child = 0; child < 8; child++)
                    {
                        int x = voxel / 4 * 2 + child / 4;
                        int y = voxel / 2 % 2 * 2 + child / 2 % 2;
                        int z = voxel % 2 * 2 + child % 2;
                        expected[((x * 4 + y) * 4 + z) * 2 + channel] = voxel * 16 + channel * 8 + child;
                    }
                }
            }
            Compare("pixel-shuffle-all-eight-children", math.Read(result), expected, 0);
            Reject(() => math.CreateBatch().PixelShuffle3D(input, input, 2, 2));
        }

        // Decoder-sized K with a final partial K128 dispatch, beyond the old
        // transformer's 8192-wide bound. This checks accumulation and bias once.
        const int wideInner = 13825;
        float[] wideInput = Enumerable.Range(0, wideInner * 2).Select(index => MathF.Sin(index * .13f) * .03f).ToArray();
        float[] wideWeights = Enumerable.Range(0, wideInner * 3).Select(index => MathF.Cos(index * .07f) * .02f).ToArray();
        using (var input = Tensor(2, wideInner, wideInput))
        using (var result = math.CreateTensor(2, 3))
        {
            var plan = new StreamingLinearPlan(wideInner, 3, 2);
            math.LinearStreamed(input, result, plan, tile =>
            {
                var packed = new float[plan.TileRows * (wideInner + 1)];
                for (int row = 0; row < tile.OutputCount; row++)
                {
                    Array.Copy(wideWeights, (tile.FirstOutput + row) * wideInner, packed, row * (wideInner + 1), wideInner);
                    packed[row * (wideInner + 1) + wideInner] = bias[tile.FirstOutput + row];
                }
                return packed;
            });
            var expected = new float[6];
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    double sum = bias[column];
                    for (int k = 0; k < wideInner; k++)
                    {
                        sum += (double)wideInput[row * wideInner + k] * wideWeights[column * wideInner + k];
                    }
                    expected[row * 3 + column] = (float)sum;
                }
            }
            Compare("decoder-wide-k128-and-output-tails", math.Read(result), expected, 2e-6);
        }

        // More than 4096 logical voxels and a partial last Y workgroup row.
        const int voxelCount = 4099;
        float[] normInput = Enumerable.Range(0, voxelCount * 3).Select(index => index % 31 * .13f - 1).ToArray();
        using (var input = Tensor(1, normInput.Length, normInput))
        using (var gammaBeta = Tensor(1, 6, [1, .7f, 1.2f, .1f, -.2f, .3f]))
        using (var result = math.CreateTensor(1, normInput.Length))
        {
            var expected = new float[normInput.Length];
            for (int voxel = 0; voxel < voxelCount; voxel++)
            {
                double mean = normInput.Skip(voxel * 3).Take(3).Average(value => (double)value);
                double variance = normInput.Skip(voxel * 3).Take(3).Average(value => Math.Pow(value - mean, 2));
                for (int channel = 0; channel < 3; channel++)
                {
                    float[] gamma = [1, .7f, 1.2f];
                    float[] beta = [.1f, -.2f, .3f];
                    expected[voxel * 3 + channel] = (float)((normInput[voxel * 3 + channel] - mean)
                        / Math.Sqrt(variance + 1e-5) * gamma[channel] + beta[channel]);
                }
            }
            var batch = math.CreateBatch();
            batch.NormalizeChannels(input, gammaBeta, result, 3, 1e-5f);
            math.Execute(batch);
            Compare("channel-norm-two-dimensional-dispatch-tail", math.Read(result), expected, 2e-5);
            Reject(() => math.CreateBatch().NormalizeChannels(input, gammaBeta, result, 4));
        }

        using (var input = Tensor(1, 7, [1, 2, 3, 4, 5, 6, 7]))
        using (var result = Tensor(1, 20, Enumerable.Repeat(-1f, 20).ToArray()))
        {
            var batch = math.CreateBatch();
            batch.CopyElements(input, result, 11);
            math.Execute(batch);
            float[] expected = Enumerable.Repeat(-1f, 20).ToArray();
            Array.Copy(new float[] { 1, 2, 3, 4, 5, 6, 7 }, 0, expected, 11, 7);
            Compare("flat-copy-offset-guards", math.Read(result), expected, 0);
            Reject(() => math.CreateBatch().CopyElements(input, result, 14));
            Reject(() => math.CreateBatch().CopyElements(input, input));
        }
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Checks = checks,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TRELLIS2_VOLUME_KERNELS_PASSED");

        GpuTensor Tensor(int rows, int columns, float[] values)
        {
            var tensor = math.CreateTensor(rows, columns);
            math.Upload(tensor, values);
            return tensor;
        }

        void Compare(string name, float[] actual, float[] expected, double tolerance)
        {
            Require(actual.Length == expected.Length && actual.All(float.IsFinite), name + " invalid output");
            double error = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
            Require(error <= tolerance, $"{name}: error {error}, tolerance {tolerance}");
            checks.Add(new { Name = name, Values = actual.Length, MaximumError = error });
            Console.WriteLine($"{name}: maximum error {error:G5}");
        }

        void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001:", StringComparison.Ordinal))
            {
                checks.Add(new { Name = "named-stop", exception.Message });
                return;
            }
            throw new InvalidOperationException("Invalid volume operation was admitted.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
