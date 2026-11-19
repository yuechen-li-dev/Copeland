using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Graphics.Vulkan.Resources.Textures;

internal static class RenderingNeuralTextureProof
{
    private const uint ViewWidth = 960;
    private const uint ViewHeight = 540;
    private const int Trials = 31;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed record Quality(double AlbedoPsnr, double NormalPsnr, double RoughnessPsnr, double P95NormalDegrees);
    private sealed record Timing(int Samples, double MedianGpuMilliseconds, double P95GpuMilliseconds, double MedianNanosecondsPerSample);
    private sealed record Lane(int PayloadBytes, ulong ResidentBytes, ulong CommonOutputBytes, Quality TextureQuality,
        double ShadedPsnr, Timing Dense, Timing Sparse, Timing Shaded, Quality? CpuAgreement = null);

    public static void Run(string output, string fixtures, string? material)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Completed\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableCooperativeMatrices: true, ApplicationName: "R8 neural texture research"));
        Require(initialized.Success, "Vulkan initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        Require(plant.Facts.CooperativeMatrixEnabled, plant.Facts.CooperativeMatrixDiagnostic);
        var assets = new GameAssets();
        byte[] hardware = assets.ComputeShader("TextureBcnSample.v.ts");
        byte[] cooperative = assets.ComputeShader("TextureNeuralSample.v.ts");
        File.WriteAllBytes(Path.Combine(output, "cooperative.spv"), cooperative);
        File.WriteAllBytes(Path.Combine(output, "hardware.spv"), hardware);
        var rows = new List<object>();
        int accepted = 0;
        foreach (string directory in Directory.GetDirectories(fixtures).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            if (material is not null && name != material) continue;
            var (row, selected) = Compare(plant, hardware, cooperative, output, directory, name);
            rows.Add(row);
            if (selected is not null) accepted++;
        }
        Require(rows.Count > 0, "No fitted material fixtures were selected.");
        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Completed = true,
            Device = plant.Facts.PhysicalDeviceName,
            plant.Facts.CooperativeMatrixDiagnostic,
            Scope = "Level-zero sampling of paired material channels. Dense aligned and sparse oblique demand use actual Vulkan samplers. Both paths write the same output and share the same shading response.",
            Budget = "Texture allocations from Vulkan image requirements plus the matrix buffer, including allocation alignment. Both paths allocate the same output. Cooperative intermediates use 3072 workgroup-local bytes and no device-local scratch buffer.",
            Timing = "GPU timestamp medians and P95 per dispatch across 31 timed batches of 16 synchronized replays. Uploads and qualification readback are outside.",
            Gate = "CPU-to-GPU agreement above 47 dB per channel, lower allocated input bytes, each source channel within 1.5 dB of the strongest matched control, shaded image within 1 dB, and P95 normal error within 20% plus 1 degree.",
            MaterialsPassing = accepted,
            Materials = rows,
        }, Json));
        Console.WriteLine($"AURELIAN_NEURAL_TEXTURE_PROOF_COMPLETED {accepted}/{rows.Count} matched-quality VRAM wins.");
    }

    private static (object Row, string? Selected) Compare(AurelianVulkanPlant plant, byte[] hardware, byte[] cooperative,
        string outputRoot, string fixture, string name)
    {
        string path = Path.Combine(outputRoot, name);
        Directory.CreateDirectory(path);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "offline.json")));
        uint width = manifest.RootElement.GetProperty("Width").GetUInt32();
        uint height = manifest.RootElement.GetProperty("Height").GetUInt32();
        int count = checked((int)(width * height));
        int capacity = Math.Max(count, (int)(ViewWidth * ViewHeight));
        var reference = File.ReadAllBytes(Path.Combine(fixture, "reference.raw"));
        Require(reference.Length == count * 6, "Reference material has an invalid byte count.");
        byte[] sourceColor = new byte[count * 4];
        byte[] sourceNormal = new byte[count * 4];
        for (int index = 0; index < count; index++)
        {
            int source = index * 6;
            int target = index * 4;
            reference.AsSpan(source, 3).CopyTo(sourceColor.AsSpan(target, 3));
            sourceColor[target + 3] = reference[source + 5];
            sourceNormal[target] = reference[source + 3];
            sourceNormal[target + 1] = reference[source + 4];
            sourceNormal[target + 2] = sourceNormal[target + 3] = 255;
        }
        uint[] shadedReference;
        using (var native = new VulkanTextureCodecProbe(plant, hardware, "TextureBcnSampleMain", capacity,
            new(width, height, VulkanTextureFormat.Rgba8Unorm, sourceColor), new(width, height, VulkanTextureFormat.Rgba8Unorm, sourceNormal)))
        {
            shadedReference = native.Run(ViewWidth, ViewHeight, oblique: true, shade: true, capture: true).Samples!;
            Save(path, "reference", shadedReference);
        }

        var color = ReadDds(fixture, "color-bc7");
        var bc7 = Measure("bc7", hardware, color, ReadDds(fixture, "normal-bc7"));
        var mixedNormal = ReadDds(fixture, "normal-bc5");
        var mixed = Measure("bc7-bc5", hardware, color, mixedNormal);
        var candidates = new List<object>();
        string? selected = null;
        ulong smallest = ulong.MaxValue;
        foreach (var candidate in manifest.RootElement.GetProperty("Candidates").EnumerateObject())
        {
            string method = candidate.Name;
            bool split = method.StartsWith("split", StringComparison.Ordinal);
            byte[] weights = File.ReadAllBytes(Path.Combine(fixture, method + "-weights.bin"));
            byte[] oracle = File.ReadAllBytes(Path.Combine(fixture, method + "-oracle.raw"));
            var lane = Measure(method, cooperative, split ? mixedNormal : ReadDds(fixture, method + "-latents"),
                ReadDds(fixture, method + "-scalar"), weights, split ? ReadDds(fixture, method + "-coarse") : null, oracle);
            var failures = CheckQuality(lane, bc7, mixed);
            if (failures.Length == 0 && lane.ResidentBytes < smallest)
            {
                selected = method;
                smallest = lane.ResidentBytes;
            }
            Console.WriteLine($"{name} {method}: {lane.ResidentBytes} B vs {mixed.ResidentBytes}, "
                + $"shaded {lane.ShadedPsnr:F2} vs {Math.Max(bc7.ShadedPsnr, mixed.ShadedPsnr):F2} dB, "
                + $"dense {lane.Dense.MedianNanosecondsPerSample:F2} vs {mixed.Dense.MedianNanosecondsPerSample:F2} ns/sample, "
                + $"gate {(failures.Length == 0 ? "pass" : string.Join(", ", failures))}");
            candidates.Add(new { Method = method, Measurements = lane, Failures = failures, Offline = candidate.Value.Clone() });
        }
        return (new { Name = name, Width = width, Height = height, Bc7 = bc7, Bc7Bc5 = mixed, Candidates = candidates, Selected = selected }, selected);

        Lane Measure(string label, byte[] program, CodecTexture first, CodecTexture second,
            byte[]? weights = null, CodecTexture? coarse = null, byte[]? oracle = null)
        {
            string entry = weights is null ? "TextureBcnSampleMain" : "TextureNeuralSampleMain";
            using var probe = new VulkanTextureCodecProbe(plant, program, entry, capacity, first, second, weights, coarse);
            var dense = probe.Run(width, height, oblique: false, shade: false, capture: true, trials: Trials);
            var sparse = probe.Run(256, 144, oblique: true, shade: false, capture: false, trials: Trials);
            var shaded = probe.Run(ViewWidth, ViewHeight, oblique: true, shade: true, capture: true, trials: Trials);
            Save(path, label, shaded.Samples!);
            Quality? agreement = oracle is null ? null : TextureQuality(oracle, dense.Samples!);
            return new(first.Bytes.Length + second.Bytes.Length + (coarse?.Bytes.Length ?? 0) + (weights?.Length ?? 0),
                probe.ResidentInputBytes, probe.CommonOutputBytes, TextureQuality(reference, dense.Samples!),
                ShadedQuality(shadedReference, shaded.Samples!), Timed(dense), Timed(sparse), Timed(shaded), agreement);
        }
    }

    private static string[] CheckQuality(Lane candidate, Lane bc7, Lane mixed)
    {
        var failures = new List<string>();
        if (candidate.CpuAgreement is not { AlbedoPsnr: > 47, NormalPsnr: > 47, RoughnessPsnr: > 47 }) failures.Add("CPU/GPU agreement");
        if (candidate.ResidentBytes >= Math.Min(bc7.ResidentBytes, mixed.ResidentBytes)) failures.Add("resident bytes");
        if (candidate.TextureQuality.AlbedoPsnr < Math.Max(bc7.TextureQuality.AlbedoPsnr, mixed.TextureQuality.AlbedoPsnr) - 1.5) failures.Add("albedo");
        if (candidate.TextureQuality.NormalPsnr < Math.Max(bc7.TextureQuality.NormalPsnr, mixed.TextureQuality.NormalPsnr) - 1.5) failures.Add("normal XY");
        if (candidate.TextureQuality.RoughnessPsnr < Math.Max(bc7.TextureQuality.RoughnessPsnr, mixed.TextureQuality.RoughnessPsnr) - 1.5) failures.Add("roughness");
        if (candidate.TextureQuality.P95NormalDegrees > Math.Min(bc7.TextureQuality.P95NormalDegrees, mixed.TextureQuality.P95NormalDegrees) * 1.2 + 1) failures.Add("normal angles");
        if (candidate.ShadedPsnr < Math.Max(bc7.ShadedPsnr, mixed.ShadedPsnr) - 1) failures.Add("shaded image");
        return failures.ToArray();
    }

    private static CodecTexture ReadDds(string directory, string name)
    {
        byte[] dds = File.ReadAllBytes(Path.Combine(directory, name + ".dds"));
        ReadOnlySpan<byte> header = dds;
        Require(dds.Length >= 148 && header[..4].SequenceEqual("DDS "u8) && header[84..88].SequenceEqual("DX10"u8)
            && Read(132) == 3 && Read(140) == 1 && Read(28) <= 1,
            $"Expected a single-level DX10 DDS texture for {name}.");
        var format = Read(128) switch
        {
            80 => VulkanTextureFormat.Bc4Unorm,
            83 => VulkanTextureFormat.Bc5Unorm,
            98 => VulkanTextureFormat.Bc7Unorm,
            _ => throw new InvalidDataException("Unexpected BC texture format in " + name),
        };
        return new(Read(16), Read(12), format, dds[148..]);
        uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(at));
    }

    private static Quality TextureQuality(byte[] reference, uint[] samples)
    {
        int count = samples.Length / 3;
        Require(reference.Length == count * 6, "Texture capture and oracle dimensions disagree.");
        double color = 0, normal = 0, roughness = 0;
        var angles = new float[count];
        for (int pixel = 0; pixel < count; pixel++)
        {
            uint rgba = samples[pixel * 3];
            uint xy = samples[pixel * 3 + 1];
            int offset = pixel * 6;
            for (int component = 0; component < 3; component++)
                color += Squared(Channel(rgba, component) - reference[offset + component]);
            normal += Squared(Channel(xy, 0) - reference[offset + 3]) + Squared(Channel(xy, 1) - reference[offset + 4]);
            roughness += Squared(Channel(rgba, 3) - reference[offset + 5]);
            Vector3 actual = DecodeNormal(Channel(xy, 0), Channel(xy, 1));
            Vector3 expected = DecodeNormal(reference[offset + 3], reference[offset + 4]);
            angles[pixel] = MathF.Acos(Math.Clamp(Vector3.Dot(actual, expected), -1, 1)) * (180 / MathF.PI);
        }
        Array.Sort(angles);
        return new(Psnr(color, count * 3), Psnr(normal, count * 2), Psnr(roughness, count), angles[(int)Math.Ceiling(count * .95) - 1]);
    }

    private static double ShadedQuality(uint[] reference, uint[] samples)
    {
        double error = 0;
        for (int index = 2; index < samples.Length; index += 3)
        {
            for (int component = 0; component < 3; component++)
                error += Squared(Channel(samples[index], component) - Channel(reference[index], component));
        }
        return Psnr(error, samples.Length);
    }

    private static void Save(string directory, string label, uint[] samples)
    {
        byte[] pixels = new byte[samples.Length / 3 * 4];
        for (int index = 0; index < samples.Length / 3; index++)
        {
            uint color = samples[index * 3 + 2];
            pixels[index * 4] = (byte)Channel(color, 0);
            pixels[index * 4 + 1] = (byte)Channel(color, 1);
            pixels[index * 4 + 2] = (byte)Channel(color, 2);
            pixels[index * 4 + 3] = 255;
        }
        NativeGameGraphics.WritePng(Path.Combine(directory, label + ".png"), (int)ViewWidth, (int)ViewHeight, pixels);
    }

    private static Vector3 DecodeNormal(int x, int y)
    {
        float a = x * (2f / 255) - 1;
        float b = y * (2f / 255) - 1;
        return Vector3.Normalize(new(a, b, MathF.Sqrt(Math.Max(0, 1 - a * a - b * b))));
    }

    private static int Channel(uint word, int component) => (int)(word >> (component * 8) & 255);
    private static double Squared(double value) => value * value;
    private static double Psnr(double error, int lanes) => -10 * Math.Log10(Math.Max(error / (lanes * 65025d), 1e-20));
    private static Timing Timed(TextureCodecCapture capture) => new(capture.SampleCount, capture.MedianGpuMilliseconds,
        capture.P95GpuMilliseconds, capture.MedianGpuMilliseconds * 1e6 / capture.SampleCount);
    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
