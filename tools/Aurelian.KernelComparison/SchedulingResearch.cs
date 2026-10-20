using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Language.External.Dxc;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Silk.NET.Vulkan;

/// <summary>Explicit research controls. Raw HLSL candidates retain the real
/// compute owner's binding, preparation, submission and completion paths.</summary>
internal static class SchedulingResearch
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static unsafe void Run(string[] args)
    {
        Require(args.Length >= 2 && args.Skip(2).All(option => option is "--validation" or "--queue-only"),
            "--research <fixtures> <fresh-output> [--validation] [--queue-only]");
        string fixtures = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        Require(!Directory.Exists(output), "Research output must be fresh.");
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "research.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(fixtures, "manifest.json")), Json)!;
        foreach (var file in manifest.Files)
        {
            Require(Hash(File.ReadAllBytes(Path.Combine(fixtures, file.Key))) == file.Value, "Fixture changed: " + file.Key);
        }
        var sourceHashes = Sources();
        bool validation = args.Contains("--validation");
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: validation, ApplicationName: "Aurelian scheduling research"));
        Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        using var plant = init.Plant!;
        PhysicalDeviceSubgroupProperties subgroup = new() { SType = StructureType.PhysicalDeviceSubgroupProperties };
        PhysicalDeviceProperties2 properties = new() { SType = StructureType.PhysicalDeviceProperties2, PNext = &subgroup };
        plant.Vk.GetPhysicalDeviceProperties2(plant.PhysicalDevice, &properties);
        uint subgroupSize = subgroup.SubgroupSize;
        Require(subgroup.SubgroupSize == 32 && (subgroup.SupportedStages & ShaderStageFlags.ComputeBit) != 0
            && (subgroup.SupportedOperations & SubgroupFeatureFlags.ArithmeticBit) != 0,
            "This experiment requires admitted 32-lane compute arithmetic subgroups.");
        var samples = new List<object>();
        var checks = new List<object>();
        var compilations = new List<object>();
        var clocks = new List<object>();
        var queue = new List<object>();
        foreach (string id in new[] { "bias_silu-4x128x0", "bias_silu-1024x4096x0", "rmsnorm-128x1024x0", "softmax-1024x4096x0" })
        {
            if (args.Contains("--queue-only") && id != "bias_silu-4x128x0")
            {
                continue;
            }
            var specimen = manifest.Cases.Single(item => item.Id == id);
            var states = new List<State>();
            try
            {
                var variants = new List<(string Name, string Operation, int Threads)> { ("ts-baseline", specimen.Family, 256) };
                if (!args.Contains("--queue-only"))
                {
                    if (specimen.Family == "bias_silu")
                    {
                        foreach (string operation in new[] { "copy", "bias", "exp", "bias_silu" })
                        {
                            foreach (int threads in new[] { 32, 64, 128, 256 })
                            {
                                variants.Add(($"{operation}-{threads}", operation, threads));
                            }
                            variants.Add(($"{operation}-vector4", operation, 256));
                        }
                    }
                    else
                    {
                        variants.Add(("wave32", specimen.Family, 32));
                        variants.Add(("wave128", specimen.Family, 128));
                        variants.Add(("wave256", specimen.Family, 256));
                    }
                }
                foreach (var variant in variants)
                {
                    byte[] Compile(string name)
                    {
                        string hlsl;
                        string entry;
                        if (variant.Name == "ts-baseline")
                        {
                            var sources = BuiltinShaderLibrary.LoadSources(name).Select(source =>
                            {
                                string text = source.Source.Replace("p.columns", specimen.Columns.ToString());
                                if (specimen.Family != "bias_silu")
                                {
                                    text = text.Replace("Indices<128>()", $"Indices<{(specimen.Columns + 63) / 64}>()")
                                        .Replace("p.rows", specimen.Rows.ToString())
                                        .Replace("p.mode", specimen.Family == "rmsnorm" ? "0" : "2");
                                }
                                return new GpuSourceFile(source.Path, text);
                            }).ToArray();
                            var module = GpuComputeBinder.Compile(new(sources));
                            Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
                            var generated = VdMirComputeBackend.Compile(module);
                            Require(generated.SpirvValidated, generated.DxcOutput);
                            hlsl = generated.Hlsl;
                            entry = module.EntryPoint!.EmittedName;
                        }
                        else
                        {
                            entry = specimen.Family switch
                            {
                                "rmsnorm" => "NormalizationMain",
                                "softmax" => "ReductionMain",
                                _ => "BiasSiluMain",
                            };
                            hlsl = Shader(specimen, variant.Operation, variant.Threads, variant.Name.EndsWith("vector4"))
                                .Replace("ResearchMain", entry);
                        }
                        string folder = Path.Combine(output, "shaders", id, variant.Name);
                        Directory.CreateDirectory(folder);
                        File.WriteAllText(Path.Combine(folder, "kernel.hlsl"), hlsl);
                        var result = DxcSpirvCompiler.Compile(new(hlsl, entry, "cs_6_0", "kernel.hlsl"));
                        Require(result.Success, result.StandardError);
                        string spirv = Path.Combine(folder, "kernel.spv");
                        File.WriteAllBytes(spirv, result.SpirvBytes);
                        RunTool("spirv-val", ["--target-env", "vulkan1.3", spirv]);
                        RunTool("spirv-dis", [spirv, "-o", spirv + "asm"]);
                        string assembly = File.ReadAllText(spirv + "asm");
                        compilations.Add(new { Case = id, Variant = variant.Name, HlslSha256 = Hash(System.Text.Encoding.UTF8.GetBytes(hlsl)),
                            SpirvSha256 = Hash(result.SpirvBytes), DxcPath = DxcExecutableResolver.Resolve().ExecutablePath,
                            ControlBarriers = assembly.Split('\n').Count(line => line.Contains("OpControlBarrier")),
                            WorkgroupVariables = assembly.Split('\n').Count(line => line.Contains("OpVariable") && line.Contains("Workgroup")),
                            SubgroupOperations = assembly.Split('\n').Count(line => line.Contains("OpGroupNonUniform")) });
                        return result.SpirvBytes;
                    }
                    var state = new State(plant, specimen, variant.Name, variant.Operation, Compile);
                    states.Add(state);
                    foreach (string distribution in manifest.Distributions)
                    {
                        string folder = Path.Combine(fixtures, id, distribution);
                        var input = Read(Path.Combine(folder, "input.bin"));
                        var auxiliary = Read(Path.Combine(folder, "auxiliary.bin"));
                        state.Math.Upload(state.Input, input);
                        state.Math.Upload(state.Auxiliary, auxiliary);
                        float[] expected = Expected(variant.Operation, input, auxiliary, specimen, folder);
                        foreach (var plan in state.Plans)
                        {
                            foreach (var tensor in plan.Outputs)
                            {
                                state.Math.Upload(tensor, Enumerable.Repeat(-12345.0f, tensor.Elements).ToArray());
                            }
                            plan.Prepared.Execute();
                            for (int outputIndex = 0; outputIndex < plan.Outputs.Length; outputIndex++)
                            {
                                var tensor = plan.Outputs[outputIndex];
                                double ratio = Check(state.Math.Read(tensor), expected, specimen);
                                checks.Add(new { Case = id, Variant = variant.Name, plan.Name, Distribution = distribution,
                                    OutputIndex = outputIndex, MaximumToleranceRatio = ratio });
                            }
                        }
                    }
                    state.Math.Upload(state.Input, Read(Path.Combine(fixtures, id, "normal", "input.bin")));
                    state.Math.Upload(state.Auxiliary, Read(Path.Combine(fixtures, id, "normal", "auxiliary.bin")));
                }
                if (!args.Contains("--queue-only"))
                {
                    for (int sample = -10; sample < 40; sample++)
                    {
                        IEnumerable<State> ordered = sample % 2 == 0 ? states : states.AsEnumerable().Reverse();
                        foreach (var state in ordered)
                        {
                            foreach (var plan in state.Plans)
                            {
                                long start = Stopwatch.GetTimestamp();
                                var result = plan.Prepared.Execute(100_000);
                                double wall = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                                if (sample >= 0)
                                {
                                    Require(result.GpuMilliseconds is > 0, "Missing positive GPU timestamp.");
                                    samples.Add(new { Case = id, state.Variant, plan.Name, plan.Repetitions, Sample = sample,
                                        WallMicroseconds = wall, GpuMicroseconds = result.GpuMilliseconds * 1000 / plan.Repetitions });
                                }
                            }
                        }
                    }
                }
                if (specimen.Family == "bias_silu")
                {
                    MeasureQueue(states[0], specimen, Read(Path.Combine(fixtures, id, "normal", "expected.bin")), queue);
                }
                clocks.Add(new { Case = id, Snapshot = RunTool("nvidia-smi",
                    ["--query-gpu=name,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu", "--format=csv"]) });
                Console.WriteLine(id + ": research correctness and timings passed");
                Save(false);
            }
            finally
            {
                foreach (var state in states)
                {
                    state.Dispose();
                }
            }
        }
        var finalHashes = Sources();
        Require(sourceHashes.Count == finalHashes.Count && sourceHashes.All(item => finalHashes.GetValueOrDefault(item.Key) == item.Value),
            "Sources changed during measurement.");
        Save(true);
        Console.WriteLine("AURELIAN_SCHEDULING_RESEARCH_PASSED");

        void Save(bool accepted)
        {
            File.WriteAllText(evidence, JsonSerializer.Serialize(new
            {
                Accepted = accepted, Device = plant.Facts.PhysicalDeviceName, Validation = validation,
                SubgroupSize = subgroupSize, SourceHashes = sourceHashes,
                ManifestSha256 = Hash(File.ReadAllBytes(Path.Combine(fixtures, "manifest.json"))),
                BinaryHashes = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").ToDictionary(path => Path.GetFileName(path),
                    path => Hash(File.ReadAllBytes(path))),
                ClockPolicy = "Unmanaged; lock request denied by driver; alternating candidates; no CUDA refresh",
                Boundary = "Prepared batch GPU timestamps include final barrier; independent-barrier ablation uses identical distinct outputs",
                Compilations = compilations, Checks = checks, Samples = samples, Queue = queue, Clocks = clocks,
            }, Json));
        }
    }

    private static void MeasureQueue(State state, Specimen specimen, float[] expected, List<object> measurements)
    {
        const int submissions = 128;
        var batch = state.Math.CreateBatch();
        batch.Activate(state.Input, state.Output, TensorActivation.BiasSilu, state.Auxiliary);
        foreach (double preparation in new[] { 0.0, 25.0, 100.0 })
        {
            for (int sample = -1; sample < 12; sample++)
            {
                string[] modes = ["fixed1", "fixed2", "fixed4", "adaptive"];
                foreach (string mode in sample % 2 == 0 ? modes : modes.Reverse())
                {
                    var controller = new QueueController(mode);
                    long start = Stopwatch.GetTimestamp();
                    ComputeQueueMetrics metrics;
                    using (var scope = state.Math.BeginQueuedScope(controller, new(submissions, 1, 0)))
                    {
                        for (int index = 0; index < submissions; index++)
                        {
                            double requested = index < submissions / 2 ? preparation : preparation * .25;
                            long prepared = Stopwatch.GetTimestamp();
                            while (Stopwatch.GetElapsedTime(prepared).TotalMicroseconds < requested)
                            {
                                Thread.SpinWait(4);
                            }
                            controller.PreparationMicroseconds = Stopwatch.GetElapsedTime(prepared).TotalMicroseconds;
                            state.Math.Execute(batch);
                        }
                        scope.Flush();
                        metrics = scope.Metrics;
                    }
                    double wall = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                    Check(state.Math.Read(state.Output), expected, specimen);
                    if (sample >= 0)
                    {
                        measurements.Add(new { Case = specimen.Id, Mode = mode, PreparationMicroseconds = preparation, Sample = sample,
                            Submissions = submissions, WallMicroseconds = wall, metrics, controller.LimitChanges,
                            controller.MinimumLimit, controller.MaximumLimit });
                    }
                }
            }
        }
        Console.WriteLine("Queued admission: fixed 1/2/4 and bounded feedforward controls completed");
    }

    private sealed class QueueController(string mode) : IComputeStreamController
    {
        private double serviceMicroseconds;
        private int lastLimit;
        public double PreparationMicroseconds { get; set; }
        public int LimitChanges { get; private set; }
        public int MinimumLimit { get; private set; } = 4;
        public int MaximumLimit { get; private set; }

        public int Plan(ComputeStreamForecast forecast) => mode == "adaptive" ? 4 : int.Parse(mode[5..]);

        public int AdmissionLimit(int capacity)
        {
            // First bounded experiment: cover an explicitly modeled 50-us host
            // feedback/submission floor, plus preparation, using GPU service costs.
            // This is a feedforward heuristic, not a qualified Smith controller.
            int limit = capacity;
            if (mode == "adaptive" && serviceMicroseconds > 0)
            {
                limit = Math.Clamp(1 + (int)Math.Ceiling((50 + PreparationMicroseconds) / serviceMicroseconds), 1, capacity);
            }
            if (lastLimit != 0 && limit != lastLimit)
            {
                LimitChanges++;
            }
            lastLimit = limit;
            MinimumLimit = Math.Min(MinimumLimit, limit);
            MaximumLimit = Math.Max(MaximumLimit, limit);
            return limit;
        }

        public void Observe(ComputeStreamObservation observation)
        {
            if (observation.Completed && observation.GpuMilliseconds is { } cost)
            {
                double measured = cost * 1000;
                serviceMicroseconds = serviceMicroseconds == 0 ? measured : serviceMicroseconds * .8 + measured * .2;
            }
        }
    }

    private sealed record Plan(string Name, int Repetitions,
        VulkanComputeMath.PreparedComputeBatch Prepared, GpuTensor[] Outputs);

    private sealed class State : IDisposable
    {
        public VulkanComputeMath Math { get; }
        public GpuTensor Input { get; }
        public GpuTensor Auxiliary { get; }
        public GpuTensor Output { get; }
        public string Variant { get; }
        public List<Plan> Plans { get; } = [];

        public State(AurelianVulkanPlant plant, Specimen specimen, string variant, string operation, Func<string, byte[]> compile)
        {
            Variant = variant;
            Math = new(plant, compile, maximumTensorElements: 8 * 1024 * 1024);
            try
            {
                Input = Math.CreateTensor(specimen.Rows, specimen.Columns);
                Auxiliary = Math.CreateTensor(1, specimen.Columns);
                Output = Math.CreateTensor(specimen.Rows, specimen.Columns);
                Add("single", 1, [Output], false);
                Add("batch16", 16, [Output], false);
                if (variant == "bias_silu-256")
                {
                    var outputs = Enumerable.Range(0, 16).Select(_ => Math.CreateTensor(specimen.Rows, specimen.Columns)).ToArray();
                    Add("independent-full", 16, outputs, false);
                    Add("independent-elided", 16, outputs, true);
                    foreach (string hazard in new[] { "write-write", "read-write", "write-read" })
                    {
                        var conflicting = Math.CreateBatch();
                        conflicting.Activate(Input, Output, TensorActivation.BiasSilu, Auxiliary);
                        if (hazard == "write-write")
                        {
                            conflicting.Activate(Input, Output, TensorActivation.BiasSilu, Auxiliary);
                        }
                        else if (hazard == "read-write")
                        {
                            conflicting.Activate(Input, Input, TensorActivation.Silu);
                        }
                        else
                        {
                            conflicting.Activate(Output, outputs[0], TensorActivation.Silu);
                        }
                        try
                        {
                            using var invalid = Math.Prepare(conflicting, new PreparedComputeOptions(ElideIndependentBarriers: true));
                            throw new InvalidOperationException("Conflicting allocations unexpectedly admitted: " + hazard);
                        }
                        catch (ArgumentException error) when (error.Message.Contains("independent dispatch allocations"))
                        {
                            // Exercise the real admission seam, before recording.
                        }
                    }
                }
            }
            catch
            {
                Math.Dispose();
                throw;
            }

            void Add(string name, int repetitions, GpuTensor[] outputs, bool elide)
            {
                var batch = Math.CreateBatch();
                for (int index = 0; index < repetitions; index++)
                {
                    var destination = outputs[index % outputs.Length];
                    if (operation == "rmsnorm")
                    {
                        batch.Normalize(Input, Auxiliary, destination);
                    }
                    else if (operation == "softmax")
                    {
                        batch.ReduceRows(Input, destination);
                    }
                    else
                    {
                        batch.Activate(Input, destination, TensorActivation.BiasSilu, Auxiliary);
                    }
                }
                Plans.Add(new(name, repetitions, Math.Prepare(batch,
                    new PreparedComputeOptions(ElideIndependentBarriers: elide)), outputs));
            }
        }

        public void Dispose() => Math.Dispose();
    }

    private static string Shader(Specimen specimen, string operation, int threads, bool vector)
    {
        if (vector)
        {
            Require(specimen.Columns % 4 == 0, "Vector candidate requires four-aligned rows.");
            string expression = operation switch
            {
                "copy" => "Input[index]",
                "bias" => "value",
                "exp" => "exponential",
                _ => "float4(value.x >= 0 ? positive.x : negative.x, value.y >= 0 ? positive.y : negative.y, value.z >= 0 ? positive.z : negative.z, value.w >= 0 ? positive.w : negative.w)",
            };
            return $$"""
                [[vk::binding(0, 0)]] StructuredBuffer<float4> Input;
                [[vk::binding(1, 0)]] StructuredBuffer<float4> Bias;
                [[vk::binding(2, 0)]] RWStructuredBuffer<float4> Output;
                [numthreads(256, 1, 1)]
                void ResearchMain(uint3 group : SV_GroupID, uint lane : SV_GroupIndex) {
                    uint index = group.x * 256 + lane;
                    if (index < {{specimen.Rows * specimen.Columns / 4}}) {
                        float4 value = Input[index] + Bias[index % {{specimen.Columns / 4}}];
                        float4 exponential = exp(-abs(value));
                        float4 positive = value / (1 + exponential);
                        float4 negative = value * exponential / (1 + exponential);
                        Output[index] = {{expression}};
                    }
                }
                """;
        }
        string body;
        if (operation is "rmsnorm" or "softmax")
        {
            string mergeSum = "total = WaveActiveSum(total);";
            string mergeMaximum = "maximum = WaveActiveMax(maximum);";
            if (threads > 32)
            {
                mergeSum += $$"""

                    if (waveLane == 0) PartialSum[lane / 32] = total;
                    GroupMemoryBarrierWithGroupSync();
                    total = WaveActiveSum(waveLane < {{threads / 32}} ? PartialSum[min(waveLane, {{threads / 32 - 1}}u)] : 0);
                    """;
                mergeMaximum += $$"""

                    if (waveLane == 0) PartialMaximum[lane / 32] = maximum;
                    GroupMemoryBarrierWithGroupSync();
                    maximum = WaveActiveMax(waveLane < {{threads / 32}} ? PartialMaximum[min(waveLane, {{threads / 32 - 1}}u)] : -3.402823466e38f);
                    """;
            }
            body = operation == "rmsnorm" ? $$"""
                uint row = group.x;
                uint waveLane = WaveGetLaneIndex();
                float total = 0;
                for (uint c = lane; c < WIDTH; c += {{threads}}) {
                    float x = Input[row * WIDTH + c];
                    total += x * x;
                }
                {{mergeSum}}
                float inverse = rsqrt(total / WIDTH + 1e-5f);
                for (uint c = lane; c < WIDTH; c += {{threads}})
                    Output[row * WIDTH + c] = Input[row * WIDTH + c] * inverse * Bias[c];
                """ : $$"""
                uint row = group.x;
                uint waveLane = WaveGetLaneIndex();
                float maximum = -3.402823466e38f;
                for (uint c = lane; c < WIDTH; c += {{threads}})
                    maximum = max(maximum, Input[row * WIDTH + c]);
                {{mergeMaximum}}
                float total = 0;
                for (uint c = lane; c < WIDTH; c += {{threads}})
                    total += exp(Input[row * WIDTH + c] - maximum);
                {{mergeSum}}
                for (uint c = lane; c < WIDTH; c += {{threads}})
                    Output[row * WIDTH + c] = exp(Input[row * WIDTH + c] - maximum) / total;
                """;
        }
        else
        {
            string expression = operation switch
            {
                "copy" => "Input[index]",
                "bias" => "value",
                "exp" => "exp(-abs(value))",
                _ => "value >= 0 ? value / (1 + exponential) : value * exponential / (1 + exponential)",
            };
            body = $$"""
                uint base = group.x * 1024;
                [unroll] for (uint part = 0; part < {{1024 / threads}}; part++) {
                    uint index = base + lane + part * {{threads}};
                    if (index < {{specimen.Rows * specimen.Columns}}) {
                        float value = Input[index] + Bias[index % WIDTH];
                        float exponential = exp(-abs(value));
                        Output[index] = {{expression}};
                    }
                }
                """;
        }
        return $$"""
            #define WIDTH {{specimen.Columns}}
            [[vk::binding(0, 0)]] StructuredBuffer<float> Input;
            [[vk::binding(1, 0)]] StructuredBuffer<float> Bias;
            [[vk::binding(2, 0)]] RWStructuredBuffer<float> Output;
            {{(threads > 32 && operation is "rmsnorm" or "softmax" ? $"groupshared float PartialSum[{threads / 32}];\ngroupshared float PartialMaximum[{threads / 32}];" : "")}}
            [numthreads({{threads}}, 1, 1)]
            void ResearchMain(uint3 group : SV_GroupID, uint lane : SV_GroupIndex) {
                {{body}}
            }
            """;
    }

    private static float[] Expected(string operation, float[] input, float[] auxiliary, Specimen specimen, string folder)
    {
        if (operation is "bias_silu" or "rmsnorm" or "softmax")
        {
            return Read(Path.Combine(folder, "expected.bin"));
        }
        return input.Select((value, index) => operation switch
        {
            "copy" => value,
            "bias" => value + auxiliary[index % specimen.Columns],
            _ => (float)Math.Exp(-Math.Abs(value + auxiliary[index % specimen.Columns])),
        }).ToArray();
    }

    private static double Check(float[] actual, float[] expected, Specimen specimen)
    {
        Require(actual.Length == expected.Length, "Output length mismatch.");
        double maximum = 0;
        for (int index = 0; index < actual.Length; index++)
        {
            double ratio = Math.Abs((double)actual[index] - expected[index]) / (specimen.Atol + specimen.Rtol * Math.Abs(expected[index]));
            if (!float.IsFinite(actual[index]) || ratio > 1)
            {
                throw new InvalidOperationException($"Numerical failure at {index}: {actual[index]} versus {expected[index]}, ratio {ratio}");
            }
            maximum = Math.Max(maximum, ratio);
        }
        return maximum;
    }

    private static Dictionary<string, string> Sources() => Directory.EnumerateFiles("src/Aurelian/Aurelian.Graphics/Vulkan", "*.cs", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles("tools/Aurelian.KernelComparison", "*.cs"))
        .ToDictionary(path => path.Replace('\\', '/'), path => Hash(File.ReadAllBytes(path)));

    private static float[] Read(string path) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string RunTool(string executable, string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Require(process.WaitForExit(30_000), "Tool timed out: " + executable);
        string result = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Require(process.ExitCode == 0, executable + ": " + result);
        return result;
    }
}
