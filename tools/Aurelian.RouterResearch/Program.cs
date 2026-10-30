using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.RouterResearch;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

if (args.Length > 0 && args[0] == "--streaming")
{
    StreamingResearch.Run(args[1..]);
    return;
}

if (args.Length < 1 || args.Skip(1).Any(option => option is not "--validation" and not "--quick"))
{
    throw new ArgumentException("Aurelian.RouterResearch <fresh-output> [--validation] [--quick]");
}
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output))
{
    throw new IOException("Research output must be a fresh directory.");
}
Directory.CreateDirectory(output);
bool validation = args.Contains("--validation");
bool quick = args.Contains("--quick");
string evidence = Path.Combine(output, "run.json");
File.WriteAllText(evidence, "{\"Accepted\":false}");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var samples = new List<object>();
var cases = new List<object>();
var checks = new List<object>();
var cpuCosts = new List<object>();
var sourceFiles = Directory.GetFiles("tools/Aurelian.RouterResearch", "*.cs")
    .Concat(Directory.GetFiles("tools/Aurelian.RouterResearch/Assets", "*.v.ts"))
    .Concat(Directory.GetFiles("tools/Aurelian.RouterResearch", "*.py"))
    .Concat(Directory.GetFiles("tools/Aurelian.RouterResearch", "*.ps1"))
    .Append("tools/Aurelian.RouterResearch/README.md")
    .Append("tools/Aurelian.RouterResearch/Aurelian.RouterResearch.csproj")
    .Append("src/Aurelian/Aurelian.Graphics/Properties/AssemblyInfo.cs")
    .Append("src/Aurelian/Aurelian.Graphics/Vulkan/Resources/Allocation/RawVulkanMemoryAllocator.cs").Order().ToArray();
var sourceHashes = sourceFiles.ToDictionary(path => path.Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)));
var binaryHashes = new[] { "Aurelian.RouterResearch.dll", "Aurelian.Graphics.dll", "Aurelian.Shaders.dll", "Copeland.TS.dll" }
    .ToDictionary(name => name, name => Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name))));
string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets/Router.v.ts"));
Require(Hash(System.Text.Encoding.UTF8.GetBytes(source)) == sourceHashes["tools/Aurelian.RouterResearch/Assets/Router.v.ts"],
    "Built shader asset differs from the working source.");
var module = GpuComputeBinder.Compile(new([new GpuSourceFile("Router.v.ts", source)]));
Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
var compiled = VdMirComputeBackend.Compile(module);
Require(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
File.WriteAllText(Path.Combine(output, "Router.hlsl"), compiled.Hlsl);
File.WriteAllBytes(Path.Combine(output, "Router.spv"), compiled.Spirv);
File.WriteAllText(Path.Combine(output, "Router.spvasm"), compiled.SpirvDisassembly);
var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new(EnableValidation: validation, ApplicationName: "Aurelian router viability research"));
Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
using var plant = init.Plant!;
Require(!validation || plant.Facts.EnabledValidationLayers.Contains("VK_LAYER_KHRONOS_validation"),
    "Validation qualification requires the Khronos validation layer.");
ulong sink = 0;
int sampleCount = quick ? 5 : 80;
string[] variants = ["scalar", "simd", "parallel-simd", "parallel-all", "gpu-resident", "gpu-transfer-commit", "gpu-original-readback"];

QualifyKnownCases();
QualifyEdges();
int[] sizes = quick ? [65, 4099] : [1, 64, 1024, 8192, 65536];
int[] policies = quick ? [0, 512] : [0, 64, 512, 2048];
foreach (int candidates in new[] { 1, 8 })
{
    foreach (int rules in policies)
    {
        foreach (int count in sizes)
        {
            RunCase(count, candidates, rules, "mixed");
        }
    }
}
if (!quick)
{
    RunCase(8192, 8, 2048, "early");
    RunCase(8192, 8, 2048, "late");
    RunCase(4099, 8, 512, "mixed");
}
foreach (var pair in sourceHashes)
{
    Require(Hash(File.ReadAllBytes(pair.Key)) == pair.Value, "Source changed during qualification: " + pair.Key);
}
File.WriteAllText(evidence, JsonSerializer.Serialize(new
{
    Accepted = true,
    Validation = validation,
    Quick = quick,
    SampleCount = sampleCount,
    StartedFromHostMemory = true,
    NicForwardingQualified = false,
    Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    ProcessorCount = Environment.ProcessorCount,
    CpuWorkers = Math.Min(Environment.ProcessorCount, 8),
    Vector256Accelerated = Vector256.IsHardwareAccelerated,
    PlantFacts = plant.Facts,
    InitializationDiagnostics = init.Diagnostics,
    SourceHashes = sourceHashes,
    BinaryHashes = binaryHashes,
    compiled.DxcPath,
    compiled.DxcArguments,
    compiled.HlslSha256,
    compiled.SpirvSha256,
    Cases = cases,
    Checks = checks,
    Samples = samples,
    CpuCosts = cpuCosts,
    Sink = sink,
}, jsonOptions));
Console.WriteLine($"Accepted: {cases.Count} cases, {checks.Count} correctness checks, {samples.Count} samples.");

void RunCase(int count, int candidates, int ruleCount, string distribution)
{
    string id = $"n{count}-c{candidates}-r{ruleCount}-{distribution}";
    long fixtureStarted = Stopwatch.GetTimestamp();
    var fixture = new RouterFixture(count, candidates, ruleCount, 20261030, distribution);
    double fixtureSetupMicroseconds = Stopwatch.GetElapsedTime(fixtureStarted).TotalMicroseconds;
    var expected = new uint[count * RouterFixture.ResultWords];
    var actual = new uint[expected.Length];
    var frames = new byte[fixture.Packets.Length];
    var queued = new int[count];
    RouterCpu.Evaluate(fixture, expected, "oracle");
    string packetFile = id + ".packets.bin";
    string tableFile = id + ".tables.bin";
    string expectedFile = id + ".expected.bin";
    File.WriteAllBytes(Path.Combine(output, packetFile), fixture.Packets);
    File.WriteAllBytes(Path.Combine(output, tableFile), fixture.TableBytes());
    File.WriteAllBytes(Path.Combine(output, expectedFile), MemoryMarshal.AsBytes(expected.AsSpan()).ToArray());
    long gpuStarted = Stopwatch.GetTimestamp();
    using var gpu = new RouterGpu(plant, fixture, compiled.Spirv);
    using var originalGpu = new RouterGpu(plant, fixture, compiled.Spirv, preferCachedReadback: false);
    double gpuSetupMicroseconds = Stopwatch.GetElapsedTime(gpuStarted).TotalMicroseconds;
    foreach (string variant in variants)
    {
        if (variant == "gpu-resident")
        {
            continue;
        }
        Array.Fill(actual, 0xCDCDCDCDu);
        if (variant.StartsWith("gpu", StringComparison.Ordinal))
        {
            var selected = variant == "gpu-original-readback" ? originalGpu : gpu;
            selected.PoisonOutput();
            selected.Execute(true, actual);
        }
        else
        {
            RouterCpu.Evaluate(fixture, actual, variant);
        }
        CheckEqual(id, variant, expected, actual);
        sink ^= RouterCpu.Commit(fixture, actual, frames, queued);
        VerifyRewrites(fixture, actual, frames);
    }
    int[] outcomes = Enumerable.Range(0, 7).Select(status =>
        Enumerable.Range(0, count).Count(index => expected[index * RouterFixture.ResultWords] == status)).ToArray();
    cases.Add(new
    {
        Id = id, Count = count, Candidates = candidates, RuleCount = ruleCount, Distribution = distribution,
        PacketFile = packetFile, PacketSha256 = Hash(fixture.Packets),
        TableFile = tableFile, TableSha256 = Hash(fixture.TableBytes()),
        ExpectedFile = expectedFile,
        OutputSha256 = Hash(MemoryMarshal.AsBytes(expected.AsSpan()).ToArray()), Outcomes = outcomes,
        FixtureSetupMicroseconds = fixtureSetupMicroseconds, GpuSetupMicroseconds = gpuSetupMicroseconds,
        CachedReadbackFlags = gpu.ReadbackMemoryFlags, OriginalReadbackFlags = originalGpu.ReadbackMemoryFlags,
    });
    // Compile and correctness are outside timing. Warm all CPU paths and the same GPU commands.
    var precondition = Stopwatch.StartNew();
    do
    {
        foreach (string variant in variants)
        {
            Execute(variant);
        }
    } while (precondition.ElapsedMilliseconds < (quick ? 30 : 250));
    var cpuTotals = variants.ToDictionary(variant => variant, _ => TimeSpan.Zero);
    using var process = Process.GetCurrentProcess();
    for (int sample = 0; sample < sampleCount; sample++)
    {
        foreach (string variant in sample % 2 == 0 ? variants : variants.Reverse())
        {
            TimeSpan cpuStarted = process.TotalProcessorTime;
            long started = Stopwatch.GetTimestamp();
            int repetitions = count == 1 && !variant.StartsWith("gpu", StringComparison.Ordinal) ? 64 : 1;
            double? gpuMicroseconds = null;
            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                gpuMicroseconds = Execute(variant);
            }
            double wallMicroseconds = (Stopwatch.GetTimestamp() - started) * 1_000_000d / Stopwatch.Frequency / repetitions;
            cpuTotals[variant] += process.TotalProcessorTime - cpuStarted;
            double? mappedReadMicroseconds = null;
            if (variant == "gpu-transfer-commit")
            {
                mappedReadMicroseconds = gpu.LastMappedReadMicroseconds;
            }
            else if (variant == "gpu-original-readback")
            {
                mappedReadMicroseconds = originalGpu.LastMappedReadMicroseconds;
            }
            samples.Add(new { Case = id, Variant = variant, Sample = sample, WallMicroseconds = wallMicroseconds,
                GpuMicroseconds = gpuMicroseconds, Repetitions = repetitions,
                MappedReadMicroseconds = mappedReadMicroseconds });
        }
    }
    foreach (var pair in cpuTotals)
    {
        cpuCosts.Add(new { Case = id, Variant = pair.Key, TotalCpuMilliseconds = pair.Value.TotalMilliseconds,
            Note = "Process CPU clock is coarse; use only aggregate interpretation." });
    }
    CheckEqual(id, "post-timing", expected, actual);
    Console.WriteLine($"{id}: correct; timed {sampleCount} samples per variant.");

    double? Execute(string variant)
    {
        if (variant == "gpu-resident")
        {
            return gpu.Execute(false, actual);
        }
        double? deviceTime = null;
        if (variant.StartsWith("gpu", StringComparison.Ordinal))
        {
            var selected = variant == "gpu-original-readback" ? originalGpu : gpu;
            deviceTime = selected.Execute(true, actual);
        }
        else
        {
            RouterCpu.Evaluate(fixture, actual, variant);
        }
        sink ^= RouterCpu.Commit(fixture, actual, frames, queued);
        return deviceTime;
    }
}

void QualifyEdges()
{
    foreach (int seed in new[] { 7, 19, 53 })
    {
        var fixture = new RouterFixture(4099, 8, 512, seed);
        var expected = new uint[fixture.Count * RouterFixture.ResultWords];
        var actual = new uint[expected.Length];
        using var gpu = new RouterGpu(plant, fixture, compiled.Spirv);
        for (int generation = 1; generation <= 3; generation++)
        {
            fixture.Tables[3] = (uint)generation;
            for (int group = 0; group < 256; group++)
            {
                for (int candidate = 0; candidate < 8; candidate++)
                {
                    fixture.Tables[8 + (group * 8 + candidate) * 3 + 1] = generation == 2 ? 0u : 1u;
                }
            }
            // Change packet contents at the same resident addresses as well as table state.
            long routeCompileStarted = Stopwatch.GetTimestamp();
            fixture.CompileRoutes();
            double routeCompileMicroseconds = Stopwatch.GetElapsedTime(routeCompileStarted).TotalMicroseconds;
            fixture.Packets[22] = (byte)(generation + 30);
            RouterFixture.Write16(fixture.Packets, 24, 0);
            RouterFixture.Write16(fixture.Packets, 24, RouterFixture.HeaderChecksum(fixture.Packets));
            long snapshotStarted = Stopwatch.GetTimestamp();
            gpu.UpdateSnapshot();
            double snapshotUploadMicroseconds = Stopwatch.GetElapsedTime(snapshotStarted).TotalMicroseconds;
            checks.Add(new { Case = $"update-seed{seed}-generation{generation}", Variant = "snapshot-update",
                Words = fixture.Tables.Length, RouteCompileMicroseconds = routeCompileMicroseconds,
                SnapshotUploadMicroseconds = snapshotUploadMicroseconds });
            RouterCpu.Evaluate(fixture, expected, "oracle");
            foreach (string variant in new[] { "scalar", "simd", "parallel-simd", "parallel-all", "gpu-transfer-commit" })
            {
                Array.Fill(actual, 0xCDCDCDCDu);
                if (variant.StartsWith("gpu", StringComparison.Ordinal))
                {
                    gpu.PoisonOutput();
                    gpu.Execute(true, actual);
                }
                else
                {
                    RouterCpu.Evaluate(fixture, actual, variant);
                }
                CheckEqual($"edge-seed{seed}-generation{generation}", variant, expected, actual);
            }
        }
        fixture.Tables[3]++;
        bool staleRejected = false;
        try
        {
            RouterCpu.Commit(fixture, actual, new byte[fixture.Packets.Length], new int[fixture.Count]);
        }
        catch (InvalidDataException)
        {
            staleRejected = true;
        }
        Require(staleRejected, "Stale snapshot was actuated.");
        checks.Add(new { Case = $"edge-seed{seed}", Variant = "stale-rejection", Words = 0 });
    }
}

void CheckEqual(string id, string variant, uint[] expected, uint[] actual)
{
    for (int word = 0; word < expected.Length; word++)
    {
        Require(expected[word] == actual[word], $"{id}/{variant}: word {word}: expected {expected[word]}, got {actual[word]}.");
    }
    checks.Add(new { Case = id, Variant = variant, Words = expected.Length });
}

static void VerifyRewrites(RouterFixture fixture, uint[] decisions, byte[] frames)
{
    for (int index = 0; index < fixture.Count; index++)
    {
        if (decisions[index * RouterFixture.ResultWords] == 0)
        {
            var frame = frames.AsSpan(index * RouterFixture.FrameBytes, RouterFixture.FrameBytes);
            Require(RouterFixture.HeaderChecksum(frame) == 0 && frame[22] == fixture.Packets[index * RouterFixture.FrameBytes + 22] - 1,
                "Committed IPv4 rewrite failed consumer checksum validation.");
            uint emittedChecksum = RouterFixture.Read16(frame, 24);
            RouterFixture.Write16(frame, 24, 0);
            uint recomputed = RouterFixture.HeaderChecksum(frame);
            RouterFixture.Write16(frame, 24, emittedChecksum);
            Require(emittedChecksum == recomputed, "Incremental checksum differs from full header recomputation.");
        }
    }
}

void QualifyKnownCases()
{
    var fixture = new RouterFixture(14, 8, 64, 7);
    for (int group = 0; group < 256; group++)
    {
        for (int candidate = 0; candidate < 8; candidate++)
        {
            int entry = 8 + (group * 8 + candidate) * 3;
            fixture.Tables[entry + 1] = group == 13 ? 0u : 1u;
            fixture.Tables[entry + 2] = 20;
        }
    }
    fixture.CompileRoutes();
    uint[] expectedStatuses = [0, 4, 5, 2, 3, 3, 3, 3, 3, 3, 3, 6, 0, 0];
    for (int index = 0; index < fixture.Count; index++)
    {
        var frame = fixture.Packets.AsSpan(index * 64, 64);
        frame.Clear();
        RouterFixture.Write16(frame, 12, 0x0800);
        frame[14] = 0x45;
        RouterFixture.Write16(frame, 16, 28);
        frame[22] = 64;
        frame[23] = 17;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame[26..], 0xC0000201);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame[30..], 0x0A001101);
        RouterFixture.Write16(frame, 36, 1234);
        RouterFixture.Write16(frame, 38, 8);
        switch (index)
        {
            case 1: frame[22] = 1; break;
            case 2: frame[32] = 255; break;
            case 3: frame[32] = 13; break;
            case 4: RouterFixture.Write16(frame, 12, 0x86DD); break;
            case 5: frame[14] = 0x46; break;
            case 6: RouterFixture.Write16(frame, 20, 0x2000); break;
            case 8: RouterFixture.Write16(frame, 16, 60); break;
            case 9: frame[23] = 1; break;
            case 10: frame[23] = 6; break;
            case 11:
            case 12:
                int ruleIndex = index == 11 ? 1 : 0;
                var rule = fixture.Rules[ruleIndex];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame[26..], rule.Source);
                uint destination = ruleIndex == 0 ? 0x0A001101u : rule.Destination;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame[30..], destination);
                RouterFixture.Write16(frame, 36, rule.PortLow);
                frame[23] = rule.Protocol == 0 ? (byte)17 : (byte)rule.Protocol;
                RouterFixture.Write16(frame, 16, frame[23] == 6 ? 40u : 28u);
                break;
        }
        if (index == 13)
        {
            // Exercise the one's-complement zero boundary from RFC 1624.
            for (uint identification = 0; identification <= 65535; identification++)
            {
                RouterFixture.Write16(frame, 18, identification);
                if (RouterFixture.HeaderChecksum(frame) == 0xFEFF) { break; }
            }
            Require(RouterFixture.HeaderChecksum(frame) == 0xFEFF, "Missing checksum zero-boundary specimen.");
        }
        RouterFixture.Write16(frame, 24, RouterFixture.HeaderChecksum(frame));
        if (index == 7) { frame[24] ^= 1; }
    }
    var expected = new uint[fixture.Count * RouterFixture.ResultWords];
    var actual = new uint[expected.Length];
    RouterCpu.Evaluate(fixture, expected, "oracle");
    for (int index = 0; index < fixture.Count; index++)
    {
        Require(expected[index * 5] == expectedStatuses[index], $"Known specimen {index} has incorrect oracle outcome.");
    }
    Require(expected[1] == 1 && expected[12 * 5 + 3] == 0 && (expected[13 * 5 + 2] & 65535) == 0,
        "Tie breaking, first-match allow, or checksum zero-boundary failed.");
    using var gpu = new RouterGpu(plant, fixture, compiled.Spirv);
    foreach (string variant in new[] { "scalar", "simd", "parallel-simd", "parallel-all", "gpu-transfer-commit" })
    {
        if (variant.StartsWith("gpu", StringComparison.Ordinal))
        {
            gpu.PoisonOutput();
            gpu.Execute(true, actual);
        }
        else
        {
            RouterCpu.Evaluate(fixture, actual, variant);
        }
        CheckEqual("known-outcomes", variant, expected, actual);
        var frames = new byte[fixture.Packets.Length];
        RouterCpu.Commit(fixture, actual, frames, new int[fixture.Count]);
        VerifyRewrites(fixture, actual, frames);
    }
    File.WriteAllBytes(Path.Combine(output, "known-outcomes.packets.bin"), fixture.Packets);
    File.WriteAllBytes(Path.Combine(output, "known-outcomes.tables.bin"), fixture.TableBytes());
}

static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
