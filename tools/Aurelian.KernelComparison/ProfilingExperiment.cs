using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Language.External.Dxc;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Silk.NET.Vulkan;

internal static class ProfilingExperiment
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static void Run(string[] args)
    {
        if (args.Length < 2 || args.Skip(2).Any(option => option is not ("--validation" or "--bias-only" or "--cuda-context")))
        {
            throw new ArgumentException("--profile <fixtures> <fresh-output> [--validation] [--bias-only] [--cuda-context]");
        }
        string fixtures = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        bool validation = args.Contains("--validation");
        if (Directory.Exists(output))
        {
            throw new IOException("Profiling requires a fresh output directory: " + output);
        }
        Directory.CreateDirectory(output);
        string repository = Path.GetFullPath(".");
        var sourceHashes = SourceHashes(repository);
        var binaryHashes = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .ToDictionary(path => Path.GetFileName(path), path => Hash(File.ReadAllBytes(path)));
        using var cudaContext = args.Contains("--cuda-context") ? new CudaContextControl() : null;
        string evidence = Path.Combine(output, "profile.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        string manifestPath = Path.Combine(fixtures, "manifest.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Json)!;
        foreach (var file in manifest.Files)
        {
            Require(Hash(File.ReadAllBytes(Path.Combine(fixtures, file.Key))) == file.Value,
                "Fixture hash mismatch: " + file.Key);
        }
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: validation, ApplicationName: "Aurelian Vulkan profiling"));
        Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
        using var plant = init.Plant!;
        var samples = new List<object>();
        var checks = new List<object>();
        var compilation = new List<object>();
        var clocks = new List<object>();
        RecordClocks("before-empty");
        var empty = MeasureEmpty(plant);
        RecordClocks("after-empty");
        Save(false);
        string[] selected = ["bias_silu-4x128x0", "bias_silu-1024x4096x0",
            "softmax-1024x4096x0", "rmsnorm-128x1024x0", "gemm-512x512x1024", "gemm-17x67x131"];
        foreach (var specimen in manifest.Cases.Where(item => selected.Contains(item.Id)))
        {
            if (args.Contains("--bias-only") && specimen.Family != "bias_silu")
            {
                continue;
            }
            var states = new List<ProfileState>();
            try
            {
                // All compilers receive the same emitted HLSL and binding layout.
                string[] compilers = specimen.Family == "bias_silu"
                    ? ["dxc", "dxc-spirv-opt", "glslang", "dxc-O0", "dxc-static-params", "dxc-hoisted-exp"]
                    : ["dxc", "dxc-spirv-opt", "glslang", "dxc-O0", "dxc-static-params"];
                foreach (string compiler in compilers)
                {
                    string shaderDirectory = Path.Combine(output, "shaders", specimen.Id, compiler);
                    byte[] Compile(string name)
                    {
                        Directory.CreateDirectory(shaderDirectory);
                        var sources = BuiltinShaderLibrary.LoadSources(name).Select(source =>
                        {
                            string text = source.Source;
                            if (name is "Normalization.v.ts" or "RowReduction.v.ts")
                            {
                                text = text.Replace("Indices<128>()", $"Indices<{(specimen.Columns + 63) / 64}>()")
                                    .Replace("p.columns", specimen.Columns.ToString())
                                    .Replace("p.rows", specimen.Rows.ToString())
                                    .Replace("p.mode", name == "Normalization.v.ts" ? "0" : "2");
                            }
                            if (name == "BiasSilu.v.ts")
                            {
                                text = text.Replace("p.columns", specimen.Columns.ToString());
                            }
                            if (compiler == "dxc-static-params")
                            {
                                text = text.Replace("p.rows", specimen.Rows.ToString())
                                    .Replace("p.columns", specimen.Columns.ToString())
                                    .Replace("p.inner", specimen.Inner.ToString())
                                    .Replace("p.height", Math.Min(65535, (specimen.Rows * specimen.Columns + 1023) / 1024).ToString())
                                    .Replace("p.beta", "0.0")
                                    .Replace("p.alpha", "1.0");
                                if (name != "Sgemm.v.ts")
                                {
                                    text = text.Replace("p.offset", "0");
                                }
                                if (name is "Sgemm.v.ts" or "ResidentSgemm.v.ts")
                                {
                                    text = text.Replace("p.mode", "0");
                                }
                            }
                            if (compiler == "dxc-hoisted-exp" && Path.GetFileName(source.Path) == "KernelMath.v.ts")
                            {
                                const string original = "if (value >= 0.0) {\n        return value / (1.0 + Exp(-value));\n    }\n    let exponential = Exp(value);";
                                text = text.Replace("\r\n", "\n");
                                Require(text.Contains(original), "Silu source changed; update the explicit hoisting experiment.");
                                text = text.Replace(original, "let exponential = Exp(-Abs(value));\n    if (value >= 0.0) {\n        return value / (1.0 + exponential);\n    }");
                            }
                            File.WriteAllText(Path.Combine(shaderDirectory, Path.GetFileName(source.Path)), text);
                            return new GpuSourceFile(source.Path, text);
                        }).ToArray();
                        var module = GpuComputeBinder.Compile(new(sources));
                        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
                        var generated = VdMirComputeBackend.Compile(module);
                        Require(generated.SpirvValidated, generated.DxcOutput);
                        string hlsl = Path.Combine(shaderDirectory, name + ".hlsl");
                        string spirv = Path.Combine(shaderDirectory, name + ".spv");
                        File.WriteAllText(hlsl + ".canonical", generated.Hlsl);
                        // glslang cannot overload on StructuredBuffer versus RWStructuredBuffer.
                        // These specimens only load Parameters through its readonly binding.
                        // Remove the two unused writable overloads for EVERY compiler control.
                        string portableHlsl = Regex.Replace(generated.Hlsl.Replace("\r\n", "\n"),
                            @"(?ms)^Vts_\w+ __vts_load(?:_word)?_\w+\(RWStructuredBuffer<uint> data, uint \w+\)\n\{.*?^\}\n", "");
                        Require(!portableHlsl.Contains("RWStructuredBuffer<uint> data"), "Unexpected writable parameter helper.");
                        File.WriteAllText(hlsl, portableHlsl);
                        string toolLog = "";
                        if (compiler != "glslang")
                        {
                            var result = DxcSpirvCompiler.Compile(new(portableHlsl,
                                module.EntryPoint!.EmittedName, "cs_6_0", name + ".hlsl",
                                compiler == "dxc-O0" ? ["-O0"] : null));
                            Require(result.Success, result.StandardError);
                            File.WriteAllBytes(spirv, result.SpirvBytes);
                            toolLog = result.StandardOutput + result.StandardError;
                            if (compiler == "dxc-spirv-opt")
                            {
                                toolLog += RunTool("spirv-opt", ["-O", "--target-env=vulkan1.3", spirv, "-o", spirv]);
                            }
                        }
                        else if (compiler == "glslang")
                        {
                            toolLog = RunTool("glslangValidator", ["-D", "-V", "--target-env", "vulkan1.3",
                                "-S", "comp", "-e", module.EntryPoint!.EmittedName, hlsl, "-o", spirv]);
                        }
                        toolLog += RunTool("spirv-val", ["--target-env", "vulkan1.3", spirv]);
                        RunTool("spirv-dis", [spirv, "-o", spirv + "asm"]);
                        byte[] bytes = File.ReadAllBytes(spirv);
                        compilation.Add(new { Case = specimen.Id, Compiler = compiler, Shader = name,
                            CanonicalHlslSha256 = generated.HlslSha256,
                            HlslSha256 = Hash(Encoding.UTF8.GetBytes(portableHlsl)), SpirvSha256 = Hash(bytes),
                            Bytes = bytes.Length, generated.DxcPath, ToolLog = toolLog });
                        return bytes;
                    }
                    var state = new ProfileState(plant, specimen, Compile, compiler);
                    states.Add(state);
                    foreach (string distribution in manifest.Distributions)
                    {
                        state.Upload(Path.Combine(fixtures, specimen.Id, distribution));
                        foreach (var plan in state.Plans)
                        {
                            plan.Prepared.Execute();
                            var actual = state.Math.Read(state.Output);
                            var expected = Read(Path.Combine(fixtures, specimen.Id, distribution, "expected.bin"));
                            double maximumRatio = 0;
                            for (int index = 0; index < actual.Length; index++)
                            {
                                double ratio = Math.Abs((double)actual[index] - expected[index])
                                    / (specimen.Atol + specimen.Rtol * Math.Abs(expected[index]));
                                Require(float.IsFinite(actual[index]) && ratio <= 1,
                                    $"{specimen.Id}/{compiler}/{plan.Name}/{distribution}: index {index}, ratio {ratio}");
                                maximumRatio = Math.Max(maximumRatio, ratio);
                            }
                            checks.Add(new { Case = specimen.Id, Compiler = compiler, plan.Name,
                                Distribution = distribution, MaximumToleranceRatio = maximumRatio });
                        }
                    }
                    state.Upload(Path.Combine(fixtures, specimen.Id, "normal"));
                    foreach (var plan in state.Plans)
                    {
                        for (int warmup = 0; warmup < 20; warmup++)
                        {
                            plan.Prepared.Execute(100_000);
                        }
                    }
                }
                for (int sample = 0; sample < 60; sample++)
                {
                    var ordered = sample % 2 == 0 ? states : states.AsEnumerable().Reverse();
                    foreach (var state in ordered)
                    {
                        // A retained but idle context can lose P2 during a long Vulkan run.
                        // Finish a one-thread CUDA no-op before the timing region. There is
                        // no overlapping CUDA work and the no-op never touches specimen data.
                        cudaContext?.Touch();
                        foreach (var plan in state.Plans)
                        {
                            foreach (uint spin in new uint[] { 0, 100_000 })
                            {
                                var startedUtc = DateTimeOffset.UtcNow;
                                long start = Stopwatch.GetTimestamp();
                                var execution = plan.Prepared.Execute(spin);
                                double wall = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                                var profile = plan.Prepared.ExecuteProfiled(spin);
                                samples.Add(new { Case = specimen.Id, state.Compiler, plan.Name, Sample = sample,
                                    StartedUtc = startedUtc,
                                    Repetitions = plan.Repetitions, SpinNanoseconds = spin, WallMicroseconds = wall,
                                    GpuMicroseconds = execution.GpuMilliseconds * 1000 / plan.Repetitions,
                                    Profile = profile });
                            }
                        }
                    }
                }
                RecordClocks(specimen.Id);
                Console.WriteLine(specimen.Id + ": compiler and shader controls, four distributions, timestamp ablation passed");
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
        int expectedStates = args.Contains("--bias-only") ? 12 : 32;
        Require(checks.Count == expectedStates * 4 * 5, "Incomplete correctness grid.");
        var finalHashes = SourceHashes(repository);
        Require(sourceHashes.Count == finalHashes.Count
            && sourceHashes.All(item => finalHashes.GetValueOrDefault(item.Key) == item.Value),
            "Measured sources changed during profiling.");
        Save(true);
        Console.WriteLine("AURELIAN_VULKAN_PROFILE_PASSED");

        void Save(bool accepted)
        {
            File.WriteAllText(evidence, JsonSerializer.Serialize(new
            {
                Accepted = accepted, Device = plant.Facts.PhysicalDeviceName, Validation = validation,
                StartedFromCommit = RunTool("git", ["rev-parse", "HEAD"]).Trim(),
                SourceHashes = sourceHashes, BinaryHashes = binaryHashes,
                ManifestSha256 = Hash(File.ReadAllBytes(manifestPath)),
                Runtime = RuntimeInformation.FrameworkDescription,
                ClockPolicy = "Unmanaged clocks; live Windows desktop; alternating compiler order",
                CudaContextRetained = cudaContext is not null, Clocks = clocks,
                CudaClockRefresh = cudaContext is not null ? "One-thread CUDA no-op and context sync before each compiler's sample group, outside all native timing regions" : null,
                EmptySubmissions = empty, Checks = checks, Compilation = compilation, Samples = samples,
            }, Json));
        }

        void RecordClocks(string phase)
        {
            clocks.Add(new { Phase = phase, Utc = DateTimeOffset.UtcNow,
                NvidiaSmi = RunTool("nvidia-smi", ["--query-gpu=name,pci.bus_id,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu", "--format=csv"]) });
        }
    }

    private static unsafe List<object> MeasureEmpty(AurelianVulkanPlant plant)
    {
        using var pool = VulkanCommandBufferPool.Create(plant);
        using var fences = VulkanFenceBundle.Create(plant);
        using var submitter = new VulkanCommandSubmitter(plant, pool, fences);
        var lease = pool.Rent(0);
        Require(lease.Begin(reusable: true).Success && lease.End().Success, "Empty command recording failed.");
        var samples = new List<object>();
        FenceCreateInfo info = new() { SType = StructureType.FenceCreateInfo };
        Check(plant.Vk.CreateFence(plant.Device, &info, null, out var binaryFence));
        try
        {
            for (int sample = -50; sample < 500; sample++)
            {
                foreach (string mode in new[] { "owner-blocking", "owner-spin", "raw-fence", "raw-idle" })
                {
                    long start = Stopwatch.GetTimestamp();
                    VulkanSubmitCpuTimings? phases = null;
                    if (mode.StartsWith("owner-", StringComparison.Ordinal))
                    {
                        var submitted = submitter.Submit(new(lease)
                        {
                            RetainForReplay = true, MeasureCpuTime = true,
                            SpinWaitNanoseconds = mode == "owner-spin" ? 100_000u : 0u,
                        });
                        Require(submitted.Success, string.Join("; ", submitted.Diagnostics.Select(item => item.Message)));
                        phases = submitted.CpuTimings;
                    }
                    else
                    {
                        var command = lease.CommandBuffer;
                        SubmitInfo submit = new()
                        {
                            SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &command,
                        };
                        long prepared = Stopwatch.GetTimestamp();
                        Check(plant.Vk.QueueSubmit(plant.GraphicsQueue, 1, &submit,
                            mode == "raw-fence" ? binaryFence : default));
                        long queued = Stopwatch.GetTimestamp();
                        if (mode == "raw-fence")
                        {
                            Check(plant.Vk.WaitForFences(plant.Device, 1, &binaryFence, true, 5_000_000_000));
                        }
                        else
                        {
                            Check(plant.Vk.QueueWaitIdle(plant.GraphicsQueue));
                        }
                        long completed = Stopwatch.GetTimestamp();
                        if (mode == "raw-fence")
                        {
                            Check(plant.Vk.ResetFences(plant.Device, 1, &binaryFence));
                        }
                        phases = new(Stopwatch.GetElapsedTime(start, prepared).TotalMicroseconds,
                            Stopwatch.GetElapsedTime(prepared, queued).TotalMicroseconds,
                            Stopwatch.GetElapsedTime(queued, completed).TotalMicroseconds,
                            Stopwatch.GetElapsedTime(completed).TotalMicroseconds);
                    }
                    double wall = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                    if (sample >= 0)
                    {
                        samples.Add(new { Mode = mode, Sample = sample, WallMicroseconds = wall, Phases = phases });
                    }
                }
            }
        }
        finally
        {
            Check(plant.Vk.DeviceWaitIdle(plant.Device));
            plant.Vk.DestroyFence(plant.Device, binaryFence, null);
            pool.Retire(lease, fences.CommandListFence.LastKnownCompletedValue);
        }
        return samples;
    }

    private static void Check(Result result) => Require(result == Result.Success, "Vulkan: " + result);

    private static float[] Read(string path) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Dictionary<string, string> SourceHashes(string repository)
    {
        string[] directories = ["src/Aurelian/Aurelian.Graphics/Vulkan", "src/Aurelian/Aurelian.Shaders",
            "src/Copeland/Copeland.TS/Gpu", "src/Copeland/Copeland.TS.Gpu.VdMir",
            "tools/Aurelian.KernelComparison"];
        var files = directories.Where(path => Directory.Exists(Path.Combine(repository, path)))
            .SelectMany(path => Directory.EnumerateFiles(Path.Combine(repository, path), "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".v.ts") || path.EndsWith(".csproj"));
        return files.Order().ToDictionary(path => Path.GetRelativePath(repository, path).Replace('\\', '/'),
            path => Hash(File.ReadAllBytes(path)));
    }

    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string RunTool(string executable, string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(executable);
        }
        string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Require(process.ExitCode == 0, executable + ": " + log);
        return log;
    }

    private sealed class ProfileState : IDisposable
    {
        public VulkanComputeMath Math { get; }
        public GpuTensor Output { get; }
        public string Compiler { get; }
        public List<(string Name, int Repetitions, VulkanComputeMath.PreparedComputeBatch Prepared)> Plans { get; } = [];
        private readonly GpuTensor input;
        private readonly GpuTensor auxiliary;

        public ProfileState(AurelianVulkanPlant plant, Specimen specimen, Func<string, byte[]> compile, string compiler)
        {
            Compiler = compiler;
            Math = new(plant, compile, maximumTensorElements: 8 * 1024 * 1024);
            bool matrix = specimen.Family == "gemm";
            input = Math.CreateTensor(specimen.Rows, matrix ? specimen.Inner : specimen.Columns);
            auxiliary = Math.CreateTensor(matrix ? specimen.Inner : 1, specimen.Columns);
            Output = Math.CreateTensor(specimen.Rows, specimen.Columns);
            foreach (var plan in new[] { ("timestamped", 1, true, true), ("untimestamped", 1, false, true),
                ("batch16", 16, true, true), ("host-parameters", 1, true, false), ("host-batch16", 16, true, false) })
            {
                var batch = Math.CreateBatch();
                for (int repetition = 0; repetition < plan.Item2; repetition++)
                {
                    switch (specimen.Family)
                    {
                        case "gemm":
                            batch.Multiply(input, auxiliary, Output, specimen.Rows >= 256 ? MatrixKernel.Resident64 : MatrixKernel.Portable16);
                            break;
                        case "softmax":
                            batch.ReduceRows(input, Output);
                            break;
                        case "rmsnorm":
                            batch.Normalize(input, auxiliary, Output);
                            break;
                        default:
                            batch.Activate(input, Output, TensorActivation.BiasSilu, auxiliary);
                            break;
                    }
                }
                Plans.Add((plan.Item1, plan.Item2, Math.Prepare(batch,
                    new PreparedComputeOptions(plan.Item3, plan.Item4))));
            }
        }

        public void Upload(string folder)
        {
            Math.Upload(input, Read(Path.Combine(folder, "input.bin")));
            Math.Upload(auxiliary, Read(Path.Combine(folder, "auxiliary.bin")));
        }

        public void Dispose() => Math.Dispose();
    }

    // Diagnostic only: refreshes CUDA's power state on the single local GPU with
    // a completed no-op outside the native timing region. No clock setting changes.
    private sealed class CudaContextControl : IDisposable
    {
        [DllImport("nvcuda.dll")]
        private static extern int cuInit(uint flags);
        [DllImport("nvcuda.dll")]
        private static extern int cuDevicePrimaryCtxRetain(out nint context, int device);
        [DllImport("nvcuda.dll")]
        private static extern int cuDeviceGetCount(out int count);
        [DllImport("nvcuda.dll")]
        private static extern int cuCtxSetCurrent(nint context);
        [DllImport("nvcuda.dll")]
        private static extern int cuDevicePrimaryCtxRelease(int device);
        [DllImport("nvcuda.dll")]
        private static extern int cuModuleLoadData(out nint module, byte[] image);
        [DllImport("nvcuda.dll")]
        private static extern int cuModuleGetFunction(out nint function, nint module, string name);
        [DllImport("nvcuda.dll")]
        private static extern int cuModuleUnload(nint module);
        [DllImport("nvcuda.dll")]
        private static extern int cuLaunchKernel(nint function, uint gridX, uint gridY, uint gridZ,
            uint blockX, uint blockY, uint blockZ, uint sharedBytes, nint stream, nint parameters, nint extra);
        [DllImport("nvcuda.dll")]
        private static extern int cuCtxSynchronize();

        private readonly nint module;
        private readonly nint function;

        public CudaContextControl()
        {
            Require(cuInit(0) == 0, "CUDA clock control initialization failed.");
            Require(cuDeviceGetCount(out int count) == 0 && count == 1,
                "The CUDA clock control currently requires exactly one CUDA GPU.");
            Require(cuDevicePrimaryCtxRetain(out nint context, 0) == 0, "CUDA primary context retention failed.");
            if (cuCtxSetCurrent(context) != 0)
            {
                _ = cuDevicePrimaryCtxRelease(0);
                throw new InvalidOperationException("CUDA primary context selection failed.");
            }
            const string ptx = ".version 7.1\n.target sm_86\n.address_size 64\n.visible .entry clock_tick() { ret; }\n\0";
            if (cuModuleLoadData(out module, Encoding.ASCII.GetBytes(ptx)) != 0
                || cuModuleGetFunction(out function, module, "clock_tick") != 0)
            {
                if (module != 0)
                {
                    _ = cuModuleUnload(module);
                }
                _ = cuDevicePrimaryCtxRelease(0);
                throw new InvalidOperationException("CUDA power-state refresh kernel creation failed.");
            }
            Touch();
        }

        public void Touch()
        {
            Require(cuLaunchKernel(function, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0) == 0
                && cuCtxSynchronize() == 0, "CUDA power-state refresh failed.");
        }

        public void Dispose()
        {
            Require(cuModuleUnload(module) == 0, "CUDA refresh module release failed.");
            _ = cuCtxSetCurrent(0);
            Require(cuDevicePrimaryCtxRelease(0) == 0, "CUDA primary context release failed.");
        }
    }
}
