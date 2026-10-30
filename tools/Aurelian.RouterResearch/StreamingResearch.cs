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
        if (arguments.Length is < 1 or > 2 || (arguments.Length == 2 && arguments[1] != "--validation"))
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
        Require(Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0",
            "Streaming qualification requires DOTNET_TieredCompilation=0 so the first CPU coordinate uses optimized code.");
        var files = Directory.GetFiles("tools/Aurelian.RouterResearch", "*.cs")
            .Append("tools/Aurelian.RouterResearch/Assets/Router.v.ts")
            .Append("src/Aurelian/Aurelian.Graphics/Vulkan/Resources/Allocation/RawVulkanMemoryAllocator.cs").Order().ToArray();
        var hashes = files.ToDictionary(path => path.Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)));
        string source = File.ReadAllText("tools/Aurelian.RouterResearch/Assets/Router.v.ts");
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("Router.v.ts", source)]));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var shader = VdMirComputeBackend.Compile(module);
        Require(shader.SpirvValidated, shader.DxcOutput + shader.SpirvValidationOutput);
        File.WriteAllBytes(Path.Combine(directory, "Router.spv"), shader.Spirv);
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: validation, ApplicationName: "Aurelian packet pipeline follow-up"));
        Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        using var plant = init.Plant!;
        Require(!validation || plant.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation"), "Missing validation layer.");
        var samples = new List<object>();
        var checks = new List<object>();
        var cases = new List<object>();
        var resources = new List<object>();
        ulong sink = 0;
        const int batchesPerSample = 32;
        const int sampleCount = 16;
        string[] variants = ["scalar", "simd", "parallel-simd", "parallel-all", "gpu-depth1", "gpu-depth2", "gpu-depth4", "gpu-depth8"];
        foreach (int count in new[] { 1024, 8192 })
        {
            foreach (int ruleCount in new[] { 0, 2048 })
            {
                string id = $"n{count}-c8-r{ruleCount}-mixed";
                var fixture = new RouterFixture(count, 8, ruleCount, 20261030);
                var expected = new uint[count * 5];
                RouterCpu.Evaluate(fixture, expected, "oracle");
                var decisions = Enumerable.Range(0, 8).Select(_ => new uint[expected.Length]).ToArray();
                var frames = Enumerable.Range(0, 8).Select(_ => new byte[fixture.Packets.Length]).ToArray();
                var queued = Enumerable.Range(0, 8).Select(_ => new int[count]).ToArray();
                var slots = new List<RouterGpu>();
                try
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        slots.Add(new(plant, fixture, shader.Spirv));
                        slots[slot].PoisonOutput();
                    }
                    cases.Add(new { Id = id, Count = count, RuleCount = ruleCount,
                        PacketSha256 = Hash(fixture.Packets), TableSha256 = Hash(fixture.TableBytes()),
                        OutputSha256 = Hash(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray()) });
                    foreach (string variant in variants)
                    {
                        Execute(variant, new double[batchesPerSample]);
                        int usedSlots = variant.StartsWith("gpu", StringComparison.Ordinal) ? int.Parse(variant[9..]) : 1;
                        for (int slot = 0; slot < usedSlots; slot++)
                        {
                            Require(expected.AsSpan().SequenceEqual(decisions[slot]), $"{id}/{variant}: incorrect slot {slot}.");
                        }
                        checks.Add(new { Case = id, Variant = variant, WordsPerSlot = expected.Length, Slots = usedSlots });
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
                            long started = Stopwatch.GetTimestamp();
                            Execute(variant, latencies);
                            double elapsed = (Stopwatch.GetTimestamp() - started) * 1_000_000d / Stopwatch.Frequency;
                            samples.Add(new { Case = id, Variant = variant, Sample = sample,
                                Batches = batchesPerSample, TotalWallMicroseconds = elapsed,
                                BatchLatenciesMicroseconds = latencies });
                        }
                    }
                    foreach (var decision in decisions)
                    {
                        Require(expected.AsSpan().SequenceEqual(decision), $"{id}: post-stream output mismatch.");
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
                    Console.WriteLine($"{id}: validated depths 1/2/4/8 and four CPU controls.");

                    void Execute(string variant, double[] latencies)
                    {
                        if (!variant.StartsWith("gpu", StringComparison.Ordinal))
                        {
                            for (int batch = 0; batch < batchesPerSample; batch++)
                            {
                                long started = Stopwatch.GetTimestamp();
                                RouterCpu.Evaluate(fixture, decisions[0], variant);
                                sink ^= RouterCpu.Commit(fixture, decisions[0], frames[0], queued[0]);
                                latencies[batch] = (Stopwatch.GetTimestamp() - started) * 1_000_000d / Stopwatch.Frequency;
                            }
                            return;
                        }
                        int depth = int.Parse(variant[9..]);
                        var submitted = new long[depth];
                        for (int batch = 0; batch < batchesPerSample; batch++)
                        {
                            int slot = batch % depth;
                            if (batch >= depth)
                            {
                                Complete(slot, batch - depth);
                            }
                            submitted[slot] = Stopwatch.GetTimestamp();
                            slots[slot].SubmitTransfer();
                        }
                        for (int batch = batchesPerSample - depth; batch < batchesPerSample; batch++)
                        {
                            Complete(batch % depth, batch);
                        }

                        void Complete(int slot, int batch)
                        {
                            slots[slot].CompleteTransfer(decisions[slot]);
                            sink ^= RouterCpu.Commit(fixture, decisions[slot], frames[slot], queued[slot]);
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
            SourceHashes = hashes, shader.SpirvSha256, PlantFacts = plant.Facts, Cases = cases, Checks = checks, Samples = samples,
            Resources = resources,
            BinarySha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Aurelian.RouterResearch.dll"))), Sink = sink,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Accepted streaming: {samples.Count} samples; latency includes time in the in-flight ring.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
