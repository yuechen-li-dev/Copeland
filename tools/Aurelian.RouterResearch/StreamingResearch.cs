using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.RouterResearch;

internal static class StreamingResearch
{
    public static void Run(string[] arguments)
    {
        if (arguments.Length is < 1 or > 3 || arguments.Skip(1).Any(option => option is not "--validation" and not "--algorithms"))
        {
            throw new ArgumentException("--streaming <fresh-output> [--validation]");
        }
        string directory = Path.GetFullPath(arguments[0]);
        if (Directory.Exists(directory))
        {
            throw new IOException("Streaming evidence needs a fresh directory.");
        }
        Directory.CreateDirectory(directory);
        string evidence = Path.Combine(directory, "stream.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        bool validation = arguments.Contains("--validation");
        bool algorithmComparison = arguments.Contains("--algorithms");
        Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0",
            "Streaming qualification requires DOTNET_TieredCompilation=0 so the first CPU coordinate uses optimized code.");
        var files = Directory.GetFiles("tools/Aurelian.RouterResearch", "*.cs")
            .Concat(Directory.GetFiles("tools/Aurelian.RouterResearch/Assets", "*.v.ts"))
            .Append("src/Aurelian/Aurelian.Graphics/Vulkan/Resources/Allocation/RawVulkanMemoryAllocator.cs").Order().ToArray();
        var hashes = files.ToDictionary(path => path.Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)));
        string source = File.ReadAllText("tools/Aurelian.RouterResearch/Assets/Router.v.ts");
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("Router.v.ts", source)]));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var shader = VdMirComputeBackend.Compile(module);
        Require(shader.SpirvValidated, shader.DxcOutput + shader.SpirvValidationOutput);
        File.WriteAllBytes(Path.Combine(directory, "Router.spv"), shader.Spirv);
        byte[]? cooperativeSpirv = null;
        string? cooperativeHash = null;
        if (algorithmComparison)
        {
            string cooperativeSource = File.ReadAllText("tools/Aurelian.RouterResearch/Assets/RouterCooperative.v.ts");
            var cooperativeModule = GpuComputeBinder.Compile(new([new GpuSourceFile("RouterCooperative.v.ts", cooperativeSource)]));
            Require(cooperativeModule.Success, string.Join("; ", cooperativeModule.Diagnostics.Select(item => item.Message)));
            var cooperative = VdMirComputeBackend.Compile(cooperativeModule);
            Require(cooperative.SpirvValidated, cooperative.DxcOutput + cooperative.SpirvValidationOutput);
            cooperativeSpirv = cooperative.Spirv;
            cooperativeHash = cooperative.SpirvSha256;
            File.WriteAllBytes(Path.Combine(directory, "RouterCooperative.spv"), cooperative.Spirv);
            File.WriteAllText(Path.Combine(directory, "RouterCooperative.hlsl"), cooperative.Hlsl);
            File.WriteAllText(Path.Combine(directory, "RouterCooperative.spvasm"), cooperative.SpirvDisassembly);
        }
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: validation, ApplicationName: "Aurelian packet pipeline follow-up"));
        Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        using var plant = init.Plant!;
        Require(!validation || plant.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation"), "Missing validation layer.");
        var samples = new List<object>();
        var checks = new List<object>();
        var cases = new List<object>();
        var resources = new List<object>();
        var edgeChecks = new List<object>();
        var priorityChecks = new List<object>();
        ulong sink = 0;
        const int batchesPerSample = 32;
        const int sampleCount = 16;
        string[] variants = ["scalar", "simd", "parallel-simd", "parallel-all", "gpu-depth1", "gpu-depth2", "gpu-depth4", "gpu-depth8"];
        if (algorithmComparison)
        {
            variants = ["scalar", "simd", "parallel-simd", "parallel-all", "gpu-depth1", "gpu-depth4", "coop-depth1", "coop-depth4"];
            foreach (int seed in new[] { 7, 19, 53 })
            {
                foreach (int count in new[] { 1, 7, 31, 65, 4099 })
                {
                    var fixture = new RouterFixture(count, 8, 2048, seed);
                    var expected = new uint[count * 5];
                    var actual = new uint[expected.Length];
                    RouterCpu.Evaluate(fixture, expected, "oracle");
                    using var gpu = new RouterGpu(plant, fixture, cooperativeSpirv!, packetsPerWorkgroup: 16);
                    gpu.PoisonOutput();
                    gpu.Execute(true, actual);
                    Require(expected.AsSpan().SequenceEqual(actual), $"Cooperative edge mismatch: n{count}, seed {seed}.");
                    edgeChecks.Add(new { Count = count, Seed = seed, Words = expected.Length });
                }
            }
            int[][] matchingRules = [[31, 32], [32, 63], [63], [], [0, 63]];
            for (int specimen = 0; specimen < matchingRules.Length; specimen++)
            {
                var rules = Enumerable.Range(0, 64).Select(_ =>
                    new PolicyRule(0xC0000201u, uint.MaxValue, 0, 0, 0, 65535, 0, 0)).ToArray();
                foreach (int rule in matchingRules[specimen])
                {
                    rules[rule] = new(0, 0, 0, 0, 0, 65535, 0, (uint)(rule % 2));
                }
                var fixture = new RouterFixture(65, 8, rules, 71);
                var expected = new uint[65 * 5];
                var actual = new uint[expected.Length];
                RouterCpu.Evaluate(fixture, expected, "oracle");
                uint winner = matchingRules[specimen].Length == 0 ? uint.MaxValue : (uint)matchingRules[specimen][0];
                for (int packet = 0; packet < 65; packet++)
                {
                    if (expected[packet * 5] is not 3 and not 4)
                    {
                        Require(expected[packet * 5 + 3] == winner, "Incorrect independent priority specimen.");
                    }
                }
                using var gpu = new RouterGpu(plant, fixture, cooperativeSpirv!, packetsPerWorkgroup: 16);
                gpu.PoisonOutput();
                gpu.Execute(true, actual);
                Require(expected.AsSpan().SequenceEqual(actual), "Cooperative rule-priority boundary mismatch.");
                priorityChecks.Add(new { Specimen = specimen, Winner = winner, Words = expected.Length });
            }
        }
        foreach (int count in new[] { 1024, 8192 })
        {
            foreach (int ruleCount in new[] { 0, 2048 })
            {
                string id = $"n{count}-c8-r{ruleCount}-mixed";
                var fixture = new RouterFixture(count, 8, ruleCount, 20261030);
                var expected = new uint[count * 5];
                RouterCpu.Evaluate(fixture, expected, "oracle");
                int slotCount = algorithmComparison ? 16 : 8;
                var decisions = Enumerable.Range(0, slotCount).Select(_ => new uint[expected.Length]).ToArray();
                var frames = Enumerable.Range(0, slotCount).Select(_ => new byte[fixture.Packets.Length]).ToArray();
                var queued = Enumerable.Range(0, slotCount).Select(_ => new int[count]).ToArray();
                var usedSlots = new HashSet<int>();
                var slots = new List<RouterGpu>();
                try
                {
                    for (int slot = 0; slot < slotCount; slot++)
                    {
                        if (slot >= 8)
                        {
                            slots.Add(new(plant, fixture, cooperativeSpirv!, packetsPerWorkgroup: 16));
                        }
                        else
                        {
                            slots.Add(new(plant, fixture, shader.Spirv));
                        }
                        slots[slot].PoisonOutput();
                    }
                    cases.Add(new { Id = id, Count = count, RuleCount = ruleCount,
                        PacketSha256 = Hash(fixture.Packets), TableSha256 = Hash(fixture.TableBytes()),
                        OutputSha256 = Hash(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray()) });
                    foreach (string variant in variants)
                    {
                        Execute(variant, new double[batchesPerSample]);
                        int used = IsGpu(variant) ? Depth(variant) : 1;
                        int offset = variant.StartsWith("coop", StringComparison.Ordinal) ? 8 : 0;
                        for (int slot = 0; slot < used; slot++)
                        {
                            Require(expected.AsSpan().SequenceEqual(decisions[offset + slot]), $"{id}/{variant}: incorrect slot {slot}.");
                        }
                        checks.Add(new { Case = id, Variant = variant, WordsPerSlot = expected.Length, Slots = used });
                    }
                    var warmup = Stopwatch.StartNew();
                    do
                    {
                        foreach (string variant in variants)
                        {
                            Execute(variant, new double[batchesPerSample]);
                        }
                    } while (warmup.ElapsedMilliseconds < 250);
                    for (int sample = 0; sample < sampleCount; sample++)
                    {
                        foreach (string variant in sample % 2 == 0 ? variants : variants.Reverse())
                        {
                            var latencies = new double[batchesPerSample];
                            var deviceTimes = new double[batchesPerSample];
                            long started = Stopwatch.GetTimestamp();
                            Execute(variant, latencies, deviceTimes);
                            double elapsed = (Stopwatch.GetTimestamp() - started) * 1_000_000d / Stopwatch.Frequency;
                            samples.Add(new { Case = id, Variant = variant, Sample = sample,
                                Batches = batchesPerSample, TotalWallMicroseconds = elapsed,
                                BatchLatenciesMicroseconds = latencies,
                                DeviceMicroseconds = IsGpu(variant) ? deviceTimes : null });
                        }
                    }
                    foreach (int slot in usedSlots)
                    {
                        Require(expected.AsSpan().SequenceEqual(decisions[slot]), $"{id}: post-stream output mismatch.");
                    }
                    // A separate sustained phase makes process CPU accounting
                    // meaningful above the OS clock quantum. These observations
                    // are not mixed into the interleaved throughput samples.
                    using var process = Process.GetCurrentProcess();
                    foreach (string variant in variants)
                    {
                        var latencies = new double[batchesPerSample];
                        TimeSpan cpuStarted = process.TotalProcessorTime;
                        var wall = Stopwatch.StartNew();
                        int groups = 0;
                        do
                        {
                            Execute(variant, latencies);
                            groups++;
                        } while (wall.ElapsedMilliseconds < 250);
                        double wallMilliseconds = wall.Elapsed.TotalMilliseconds;
                        double cpuMilliseconds = (process.TotalProcessorTime - cpuStarted).TotalMilliseconds;
                        resources.Add(new { Case = id, Variant = variant, Groups = groups,
                            Packets = (long)groups * batchesPerSample * count,
                            WallMilliseconds = wallMilliseconds, ProcessCpuMilliseconds = cpuMilliseconds });
                    }
                    Console.WriteLine($"{id}: validated all eight variants against the independent oracle.");

                    void Execute(string variant, double[] latencies, double[]? deviceTimes = null)
                    {
                        if (!IsGpu(variant))
                        {
                            usedSlots.Add(0);
                            for (int batch = 0; batch < batchesPerSample; batch++)
                            {
                                long started = Stopwatch.GetTimestamp();
                                RouterCpu.Evaluate(fixture, decisions[0], variant);
                                sink ^= RouterCpu.Commit(fixture, decisions[0], frames[0], queued[0]);
                                latencies[batch] = (Stopwatch.GetTimestamp() - started) * 1_000_000d / Stopwatch.Frequency;
                            }
                            return;
                        }
                        int depth = Depth(variant);
                        int familyOffset = variant.StartsWith("coop", StringComparison.Ordinal) ? 8 : 0;
                        var submitted = new long[depth];
                        for (int batch = 0; batch < batchesPerSample; batch++)
                        {
                            int slot = batch % depth;
                            if (batch >= depth)
                            {
                                Complete(slot, batch - depth);
                            }
                            submitted[slot] = Stopwatch.GetTimestamp();
                            slots[familyOffset + slot].SubmitTransfer();
                        }
                        for (int batch = batchesPerSample - depth; batch < batchesPerSample; batch++)
                        {
                            Complete(batch % depth, batch);
                        }

                        void Complete(int slot, int batch)
                        {
                            int physicalSlot = familyOffset + slot;
                            double deviceMicroseconds = slots[physicalSlot].CompleteTransfer(decisions[physicalSlot]);
                            if (deviceTimes is not null)
                            {
                                deviceTimes[batch] = deviceMicroseconds;
                            }
                            usedSlots.Add(physicalSlot);
                            sink ^= RouterCpu.Commit(fixture, decisions[physicalSlot], frames[physicalSlot], queued[physicalSlot]);
                            latencies[batch] = (Stopwatch.GetTimestamp() - submitted[slot]) * 1_000_000d / Stopwatch.Frequency;
                        }
                    }
                }
                finally
                {
                    foreach (var slot in slots)
                    {
                        slot.Dispose();
                    }
                }
            }
        }
        foreach (var pair in hashes)
        {
            Require(Hash(File.ReadAllBytes(pair.Key)) == pair.Value, "Source changed during streaming: " + pair.Key);
            string copy = Path.Combine(directory, "sources", pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(pair.Key, copy);
        }
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Validation = validation, BatchesPerSample = batchesPerSample, SampleCount = sampleCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            AlgorithmComparison = algorithmComparison, CooperativeSpirvSha256 = cooperativeHash,
            EdgeChecks = edgeChecks, PriorityChecks = priorityChecks,
            SourceHashes = hashes, shader.SpirvSha256, PlantFacts = plant.Facts, Cases = cases, Checks = checks, Samples = samples,
            Resources = resources,
            BinarySha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Aurelian.RouterResearch.dll"))), Sink = sink,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Accepted streaming: {samples.Count} samples; latency includes time in the in-flight ring.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool IsGpu(string variant)
    {
        return variant.StartsWith("gpu", StringComparison.Ordinal) || variant.StartsWith("coop", StringComparison.Ordinal);
    }

    private static int Depth(string variant)
    {
        int firstDigit = variant.LastIndexOf("depth", StringComparison.Ordinal) + 5;
        return int.Parse(variant[firstDigit..]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
