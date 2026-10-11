using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Assets.Tensors;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class DinoV3PatchProof
{
    private const string PublishedWeightHash = "dcb2e45127cccbf1601e5f42fef165eea275c8e5213197e8dcf3f48822718179";
    private const string Revision = "ea8dc2863c51be0a264bab82070e3e8836b02d51";

    public static void Run(string inputDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string evidencePath = Path.Combine(outputDirectory, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false,\"FullEncoderQualified\":false}");
        ValidateCheckpoint(inputDirectory);
        string weightsPath = Path.Combine(inputDirectory, "model.safetensors");
        using var weights = new SafeTensorFile(weightsPath);
        CheckInventory(weights);
        // Header admission is separate from bounded finite-value decoding. Exercise
        // rank 3 tokens and a selected rank 2 transformer projection as well.
        _ = weights.ReadSlices("embeddings.cls_token", 0, 1);
        _ = weights.ReadSlices("embeddings.register_tokens", 0, 1);
        _ = weights.ReadRows("layer.23.attention.q_proj.weight", 1023, 1);

        RunProjection(weights, outputDirectory, evidencePath);
    }

    internal static void ValidateCheckpoint(string inputDirectory)
    {
        string weightsPath = Path.Combine(inputDirectory, "model.safetensors");
        using (var stream = File.OpenRead(weightsPath))
        {
            Require(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() == PublishedWeightHash,
                "DINOv3 checkpoint differs from the published Meta LFS SHA-256.");
        }
        CheckGitBlob(Path.Combine(inputDirectory, "config.json"), "ab2a8b183fd4fd5213e9898a7f499c7c857b1a86");
        CheckGitBlob(Path.Combine(inputDirectory, "preprocessor_config.json"), "0126173c1ab7b75bbd9102e37de8a56ffe0a012b");
        using var weights = new SafeTensorFile(weightsPath);
        CheckInventory(weights);
    }

    private static void RunProjection(SafeTensorFile weights, string outputDirectory, string evidencePath)
    {
        const int grid = 14;
        const int tokens = grid * grid;
        const int patchWidth = 3 * 16 * 16;
        const int channels = 1024;
        float[] patches = NormalizedFixture(grid);
        float[] bias = weights.ReadVector("embeddings.patch_embeddings.bias");
        float[] expected = Reference(weights, patches, bias, tokens, patchWidth, channels);
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian DINOv3 real patch embedding qualification"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        _ = assets.ComputeShader("StreamingLinear.v.ts");
        using var math = new VulkanComputeMath(plant, assets.ComputeShader);
        using var input = math.CreateTensor(tokens, patchWidth);
        using var output = math.CreateTensor(tokens, channels);
        math.Upload(input, patches);
        var plan = new StreamingLinearPlan(patchWidth, channels, 64);
        var watch = Stopwatch.StartNew();
        var execution = math.LinearStreamed(input, output, plan, tile =>
        {
            float[] selected = weights.ReadSlices("embeddings.patch_embeddings.weight", tile.FirstOutput, tile.OutputCount);
            var packed = new float[plan.TileRows * (patchWidth + 1)];
            for (int row = 0; row < tile.OutputCount; row++)
            {
                Array.Copy(selected, row * patchWidth, packed, row * (patchWidth + 1), patchWidth);
                packed[row * (patchWidth + 1) + patchWidth] = bias[tile.FirstOutput + row];
            }
            return packed;
        });
        Require(math.ReadbackCount == 0, "Patch embedding performed an intermediate readback.");
        float[] actual = math.Read(output);
        watch.Stop();
        double maximum = actual.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
        double rms = Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow((double)a - b, 2)).Average());
        bool accepted = actual.All(float.IsFinite) && maximum <= 0.00003;
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = accepted,
            FullEncoderQualified = false,
            Scope = "Real 16x16/stride-16 RGB patch projection over a synthetic normalized 224x224 image; not complete DINOv3 features",
            Model = "facebook/dinov3-vitl16-pretrain-lvd1689m",
            Revision,
            WeightSha256 = PublishedWeightHash,
            MetadataSource = "https://huggingface.co/api/models/facebook/dinov3-vitl16-pretrain-lvd1689m?blobs=true",
            TensorCount = weights.Tensors.Count,
            Encoding = "F32",
            InputLayout = "patch token, channel, patch Y, patch X; checkpoint OIHW flattened per output channel",
            PatchTokens = tokens,
            OutputChannels = channels,
            MaximumAbsoluteError = maximum,
            RootMeanSquareError = rms,
            ErrorLimit = 0.00003,
            InferenceWallMilliseconds = watch.Elapsed.TotalMilliseconds,
            Execution = execution,
            FinalReadbacks = math.ReadbackCount,
            PeakOwnedAllocationBytes = math.MemoryTelemetry.HighWaterLiveBytes,
            MemoryScope = "Owned Vulkan buffers including staging/parameters, not total process VRAM",
            NextBlocker = "24-layer DINOv3 encoder: 64-wide attention heads, FP32 outputs, prefix-token-aware 2D RoPE, layer scale and exact GELU; TRELLIS preprocessing resolution must override the standalone 224 default",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"DINOv3 real patch projection: {tokens}x{channels}, maximum error {maximum:G6}, peak {math.MemoryTelemetry.HighWaterLiveBytes:N0} bytes, {watch.Elapsed.TotalMilliseconds:F1} ms wall");
        Require(accepted, "DINOv3 Vulkan patch projection disagrees with independent reference.");
        Console.WriteLine("AURELIAN_DINOV3_PATCH_PASSED");
    }

    private static float[] Reference(SafeTensorFile weights, float[] input, float[] bias, int tokens, int inner, int columns)
    {
        var expected = new float[tokens * columns];
        for (int first = 0; first < columns; first += 64)
        {
            float[] selected = weights.ReadSlices("embeddings.patch_embeddings.weight", first, 64);
            Parallel.For(0, tokens, token =>
            {
                for (int column = 0; column < 64; column++)
                {
                    double sum = bias[first + column];
                    for (int lane = 0; lane < inner; lane++)
                    {
                        sum += (double)input[token * inner + lane] * selected[column * inner + lane];
                    }
                    expected[token * columns + first + column] = (float)sum;
                }
            });
        }
        return expected;
    }

    private static float[] NormalizedFixture(int grid)
    {
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];
        var values = new float[grid * grid * 768];
        for (int patch = 0; patch < grid * grid; patch++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int imageX = patch % grid * 16 + x;
                        int imageY = patch / grid * 16 + y;
                        float pixel = (imageX * 7 + imageY * 11 + channel * 61) % 256 / 255f;
                        values[patch * 768 + channel * 256 + y * 16 + x] = (pixel - mean[channel]) / std[channel];
                    }
                }
            }
        }
        return values;
    }

    private static void CheckInventory(SafeTensorFile weights)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Expect("embeddings.cls_token", 1, 1, 1024);
        Expect("embeddings.mask_token", 1, 1, 1024);
        Expect("embeddings.register_tokens", 1, 4, 1024);
        Expect("embeddings.patch_embeddings.bias", 1024);
        Expect("embeddings.patch_embeddings.weight", 1024, 3, 16, 16);
        Expect("norm.bias", 1024);
        Expect("norm.weight", 1024);
        for (int layer = 0; layer < 24; layer++)
        {
            string prefix = $"layer.{layer}.";
            foreach (string projection in new[] { "q", "k", "v", "o" })
            {
                Expect(prefix + $"attention.{projection}_proj.weight", 1024, 1024);
                if (projection != "k") { Expect(prefix + $"attention.{projection}_proj.bias", 1024); }
            }
            Expect(prefix + "layer_scale1.lambda1", 1024);
            Expect(prefix + "layer_scale2.lambda1", 1024);
            Expect(prefix + "mlp.up_proj.weight", 4096, 1024);
            Expect(prefix + "mlp.up_proj.bias", 4096);
            Expect(prefix + "mlp.down_proj.weight", 1024, 4096);
            Expect(prefix + "mlp.down_proj.bias", 1024);
            foreach (string normalization in new[] { "norm1", "norm2" })
            {
                Expect(prefix + normalization + ".weight", 1024);
                Expect(prefix + normalization + ".bias", 1024);
            }
        }
        Require(names.SetEquals(weights.Tensors.Keys), "Unexpected DINOv3 checkpoint tensor inventory.");

        void Expect(string name, params int[] shape)
        {
            Require(weights.Tensors.TryGetValue(name, out var info) && info.DataType == "F32" && info.Shape.SequenceEqual(shape),
                "DINOv3 tensor shape/encoding mismatch: " + name);
            names.Add(name);
        }
    }

    private static void CheckGitBlob(string path, string expected)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] prefix = Encoding.UTF8.GetBytes($"blob {bytes.Length}\0");
        string hash = Convert.ToHexString(SHA1.HashData([.. prefix, .. bytes])).ToLowerInvariant();
        Require(hash == expected, "Config differs from the published Git blob: " + Path.GetFileName(path));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
