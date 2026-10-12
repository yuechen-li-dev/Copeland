using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2SparseProof
{
    public static void Run(string output, bool staged = false)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian sparse shape kernels"));
        if (!initialization.Success)
        {
            throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        using var math = new VulkanComputeMath(plant, assets.ComputeShader, maximumTensorElements: 67108864);
        using var feedforward = new Trellis2ComputeFeedforward();
        math.StreamController = staged ? feedforward : null;
        var checks = new List<object>();
        float[] positions = [31, 3, 21, 0, 4, 0, 12, 30, 1, 2, 1, 31];
        float[] values = Enumerable.Range(0, 4 * 256).Select(index => Trellis2BlockReference.Round(MathF.Sin(index * .17f) * .3f)).ToArray();
        using (var input = Tensor(4, 256, values))
        using (var coordinates = Tensor(4, 3, positions))
        using (var result = math.CreateTensor(4, 256))
        {
            var batch = math.CreateBatch();
            batch.RopeCoordinates3D(input, coordinates, result);
            math.Execute(batch);
            var expected = new float[values.Length];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 256; column += 2)
                {
                    int pair = column / 2 % 64;
                    double phase = pair < 63 ? positions[row * 3 + pair / 21] / Math.Pow(10000, pair % 21 / 21.0) : 0;
                    int index = row * 256 + column;
                    expected[index] = Trellis2BlockReference.Round((float)(values[index] * Math.Cos(phase) - values[index + 1] * Math.Sin(phase)));
                    expected[index + 1] = Trellis2BlockReference.Round((float)(values[index + 1] * Math.Cos(phase) + values[index] * Math.Sin(phase)));
                }
            }
            float[] actual = math.Read(result);
            Compare("irregular-xyz-rope-two-heads", actual, expected, .002);
            int[] permutation = [3, 1, 0, 2];
            using var permutedInput = Tensor(4, 256, permutation.SelectMany(row => values.Skip(row * 256).Take(256)).ToArray());
            using var permutedCoordinates = Tensor(4, 3, permutation.SelectMany(row => positions.Skip(row * 3).Take(3)).ToArray());
            batch = math.CreateBatch();
            batch.RopeCoordinates3D(permutedInput, permutedCoordinates, result);
            math.Execute(batch);
            Compare("coordinate-rope-row-permutation", math.Read(result),
                permutation.SelectMany(row => actual.Skip(row * 256).Take(256)).ToArray(), 0);
        }

        float[] smallLogits = [-1, 1, -1, 1, 1, -1, -1, 1];
        var grid = SparseVoxelGrid.FromLogits(smallLogits, 2);
        float[] smallFeatures = Enumerable.Range(0, grid.Coordinates.Length * 3).Select(index => index * .07f - .4f).ToArray();
        using (var input = Tensor(grid.Coordinates.Length, 3, smallFeatures))
        using (var neighbors = math.CreateTensor(grid.Coordinates.Length, 27, GpuTensorFormat.UInt32))
        using (var result = math.CreateTensor(2, 81))
        {
            uint[] map = grid.Neighbors27.Select(index => index < 0 ? uint.MaxValue : (uint)index).ToArray();
            map[27] = uint.MaxValue - 1;
            math.UploadUInt32(neighbors, map);
            var expected = new float[2 * 81];
            for (int row = 0; row < 2; row++)
            {
                for (int slot = 0; slot < 27; slot++)
                {
                    uint neighbor = map[(row + 1) * 27 + slot];
                    for (int channel = 0; channel < 3; channel++)
                    {
                        expected[row * 81 + slot * 3 + channel] = neighbor < input.Rows ? smallFeatures[neighbor * 3 + channel] : 0;
                    }
                }
            }
            var batch = math.CreateBatch();
            batch.SparseColumns(input, neighbors, result, firstRow: 1);
            math.Execute(batch);
            Compare("typed-sparse-neighbor-gather-offset-and-missing", math.Read(result), expected, 0);
            Reject(() => math.Upload(neighbors, new float[neighbors.Elements]));
            Reject(() => math.Read(neighbors));
            Reject(() => math.CreateBatch().SparseColumns(input, neighbors, result, firstRow: 3));
        }

        const int keys = 5870;
        float[] q = Enumerable.Range(0, 3 * 128).Select(index => MathF.Sin(index * .11f) * .2f).ToArray();
        float[] k = Enumerable.Range(0, keys * 128).Select(index => MathF.Cos(index * .13f) * .3f).ToArray();
        float[] v = Enumerable.Range(0, keys * 128).Select(index => MathF.Sin(index * .07f) * .4f).ToArray();
        using (var query = Tensor(3, 128, q))
        using (var key = Tensor(keys, 128, k))
        using (var value = Tensor(keys, 128, v))
        using (var result = math.CreateTensor(3, 128))
        {
            var expected = new float[3 * 128];
            for (int row = 0; row < 3; row++)
            {
                for (int head = 0; head < 2; head++)
                {
                    var scores = new double[keys];
                    for (int token = 0; token < keys; token++)
                    {
                        double dot = 0;
                        for (int lane = 0; lane < 64; lane++)
                        {
                            dot += (double)q[row * 128 + head * 64 + lane] * k[token * 128 + head * 64 + lane];
                        }
                        scores[token] = dot / 8;
                    }
                    double maximum = scores.Max();
                    scores = scores.Select(score => Math.Exp(score - maximum)).ToArray();
                    double total = scores.Sum();
                    for (int lane = 0; lane < 64; lane++)
                    {
                        double sum = 0;
                        for (int token = 0; token < keys; token++)
                        {
                            sum += scores[token] * v[token * 128 + head * 64 + lane];
                        }
                        expected[row * 128 + head * 64 + lane] = (float)(sum / total);
                    }
                }
            }
            int before = math.ReadbackCount;
            math.AttentionTiled(query, key, value, result, headWidth: 64, queryTileRows: 2, roundOutput: false);
            if (math.ReadbackCount != before)
            {
                throw new InvalidDataException("Attention read back intermediate activations.");
            }
            Compare("5870-key-attention-query-tail", math.Read(result), expected, 2e-6);
        }

        using (var input = Tensor(5462, 1536, Enumerable.Repeat(1f, 5462 * 1536).ToArray()))
        using (var gamma = Tensor(1, 1536, Enumerable.Range(0, 1536).Select(index => (index % 5 + 1) * .25f).ToArray()))
        using (var result = Tensor(5462, 1536, new float[5462 * 1536]))
        {
            var batch = math.CreateBatch();
            batch.NormalizeHeadsBFloat16(input, gamma, result);
            math.Execute(batch);
            Compare("65544-heads-two-dimensional-dispatch", math.Read(result),
                Enumerable.Range(0, result.Elements).Select(index => (index % 1536 % 5 + 1) * .25f).ToArray(), 0);
        }
        int guidedSteps = Enumerable.Range(0, 12).Count(step => Trellis2EulerSampler.Time(step, 3) >= .6);
        if (guidedSteps != 9)
        {
            throw new InvalidDataException("Shape schedule must have 12 positives and nine negatives.");
        }
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Checks = checks,
            ShapeModelEvaluations = 12 + guidedSteps, PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            Staged = staged, Feedforward = feedforward.Evidence,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TRELLIS2_SPARSE_KERNELS_PASSED");

        GpuTensor Tensor(int rows, int columns, float[] data)
        {
            var tensor = math.CreateTensor(rows, columns);
            math.Upload(tensor, data);
            return tensor;
        }

        void Compare(string name, float[] actual, float[] expected, double tolerance)
        {
            double error = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
            if (actual.Length != expected.Length || !actual.All(float.IsFinite) || error > tolerance)
            {
                throw new InvalidDataException($"{name}: error {error}, tolerance {tolerance}");
            }
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
            throw new InvalidDataException("Invalid sparse operation was admitted.");
        }
    }
}
