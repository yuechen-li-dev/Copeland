using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

if (args.Length > 0 && args[0] == "--research")
{
    SchedulingResearch.Run(args[1..]);
    return;
}

if (args.Length > 0 && args[0] == "--profile")
{
    ProfilingExperiment.Run(args[1..]);
    return;
}

if (args.Length != 2)
{
    throw new ArgumentException("Usage: Aurelian.KernelComparison <fixture-directory> <output-directory>");
}
string fixtures = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
string manifestPath = Path.Combine(fixtures, "manifest.json");
var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), jsonOptions)!;
string manifestHash = Hash(File.ReadAllBytes(manifestPath));
foreach (var file in manifest.Files)
{
    if (!string.Equals(Hash(File.ReadAllBytes(Path.Combine(fixtures, file.Key))), file.Value, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Fixture hash mismatch: " + file.Key);
    }
}
string evidencePath = Path.Combine(output, "vulkan.json");
File.WriteAllText(evidencePath, "{\"Accepted\":false}");
var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new(EnableValidation: false, ApplicationName: "Aurelian kernel comparison"));
if (!initialization.Success)
{
    throw new InvalidOperationException(string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
}
using var plant = initialization.Plant!;
var measurements = new List<object>();
var checks = new List<object>();
var compilation = new List<object>();
var variants = new[] { "existing-unfused", "sol-fused-specialized", "sol-prepared-blocking", "sol-prepared" };
foreach (var specimen in manifest.Cases)
{
    var owners = new List<VulkanComputeMath>();
    var states = new List<CaseState>();
    try
    {
        foreach (string variant in variants)
        {
            byte[] Compile(string name)
            {
                string shaderFolder = Path.Combine(output, "shaders", specimen.Id, variant);
                Directory.CreateDirectory(shaderFolder);
                var timer = Stopwatch.StartNew();
                var sources = BuiltinShaderLibrary.LoadSources(name).Select(source =>
                {
                    string text = source.Source;
                    if (variant == "existing-unfused" && name == "Sgemm.v.ts"
                        && Path.GetFileName(source.Path) == "Sgemm.v.ts")
                    {
                        text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "SgemmOriginal.v.ts"));
                    }
                    if (variant != "existing-unfused" && name is "Normalization.v.ts" or "RowReduction.v.ts")
                    {
                        // Shape-only specialization, equally available to Triton/Inductor.
                        // Every shape, including held-out shapes, is compiled independently.
                        text = text.Replace("Indices<128>()", $"Indices<{(specimen.Columns + 63) / 64}>()");
                        text = text.Replace("p.columns", specimen.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        text = text.Replace("p.rows", specimen.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        text = text.Replace("p.mode", name == "Normalization.v.ts" ? "0" : "2");
                    }
                    if (variant != "existing-unfused" && name == "BiasSilu.v.ts")
                    {
                        text = text.Replace("p.columns", specimen.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    File.WriteAllText(Path.Combine(shaderFolder, Path.GetFileName(source.Path)), text);
                    return new GpuSourceFile(source.Path, text);
                }).ToArray();
                var module = GpuComputeBinder.Compile(new(sources));
                if (!module.Success)
                {
                    throw new InvalidDataException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
                }
                var result = VdMirComputeBackend.Compile(module);
                if (!result.SpirvValidated)
                {
                    throw new InvalidDataException(result.DxcOutput + result.SpirvValidationOutput);
                }
                timer.Stop();
                File.WriteAllText(Path.Combine(shaderFolder, name + ".hlsl"), result.Hlsl);
                File.WriteAllBytes(Path.Combine(shaderFolder, name + ".spv"), result.Spirv);
                File.WriteAllText(Path.Combine(shaderFolder, name + ".spvasm"), result.SpirvDisassembly);
                compilation.Add(new { Case = specimen.Id, Variant = variant, Shader = name,
                    Milliseconds = timer.Elapsed.TotalMilliseconds, result.HlslSha256, result.SpirvSha256,
                    result.DxcPath, result.DxcArguments, result.SpirvValidated,
                    SourceSha256 = sources.ToDictionary(source => source.Path,
                        source => Hash(System.Text.Encoding.UTF8.GetBytes(source.Source))) });
                return result.Spirv;
            }
            var math = new VulkanComputeMath(plant, Compile, maximumTensorElements: 8 * 1024 * 1024);
            owners.Add(math);
            var state = new CaseState(math, specimen, variant, manifest.Repetitions);
            states.Add(state);
            foreach (string distribution in manifest.Distributions)
            {
                string folder = Path.Combine(fixtures, specimen.Id, distribution);
                state.Upload(folder);
                state.ExecuteSingle();
                float[] actual = math.Read(state.Output);
                float[] expected = ReadFloats(Path.Combine(folder, "expected.bin"));
                double maximum = 0;
                double maximumRatio = 0;
                for (int index = 0; index < expected.Length; index++)
                {
                    double error = Math.Abs((double)actual[index] - expected[index]);
                    double ratio = error / (specimen.Atol + specimen.Rtol * Math.Abs(expected[index]));
                    maximum = Math.Max(maximum, error);
                    maximumRatio = Math.Max(maximumRatio, ratio);
                    if (!float.IsFinite(actual[index]) || ratio > 1)
                    {
                        throw new InvalidDataException($"{specimen.Id}/{variant}/{distribution}: index {index}, actual {actual[index]}, expected {expected[index]}, tolerance ratio {ratio}");
                    }
                }
                checks.Add(new { Case = specimen.Id, Variant = variant, Distribution = distribution,
                    MaximumAbsoluteError = maximum, MaximumToleranceRatio = maximumRatio, Elements = expected.Length });
            }
            state.Upload(Path.Combine(fixtures, specimen.Id, "normal"));
            for (int warmup = 0; warmup < manifest.Warmups; warmup++)
            {
                state.ExecuteSingle();
                state.ExecuteRepeated();
            }
        }
        if (specimen.Split == "timed")
        {
            var precondition = Stopwatch.StartNew();
            while (precondition.Elapsed.TotalMilliseconds < 250)
            {
                foreach (var state in states)
                {
                    state.ExecuteRepeated();
                }
            }
            for (int sample = 0; sample < manifest.Samples; sample++)
            {
                // Alternating order controls drift within the Vulkan comparison.
                foreach (var state in sample % 2 == 0 ? states : states.AsEnumerable().Reverse())
                {
                    var sampleStartedUtc = DateTimeOffset.UtcNow;
                    var timer = Stopwatch.StartNew();
                    var execution = state.ExecuteSingle();
                    double wallMilliseconds = timer.Elapsed.TotalMilliseconds;
                    var repeated = state.ExecuteRepeated();
                    measurements.Add(new { Case = specimen.Id, state.Variant, Sample = sample,
                        StartedUtc = sampleStartedUtc,
                        WallMilliseconds = wallMilliseconds, GpuMilliseconds = execution.GpuMilliseconds,
                        RepeatedGpuMilliseconds = repeated.GpuMilliseconds / manifest.Repetitions,
                        Repetitions = manifest.Repetitions, Dispatches = execution.Dispatches,
                        CpuSpinBudgetNanoseconds = state.Variant == "sol-prepared" ? 100_000 : 0,
                        PeakOwnedBytes = state.Math.MemoryTelemetry.HighWaterLiveBytes });
                }
            }
        }
        Save(false);
        Console.WriteLine($"{specimen.Id}: all Vulkan variants passed four distributions ({specimen.Split})");
    }
    finally
    {
        foreach (var owner in owners)
        {
            owner.Dispose();
        }
    }
}
Save(true);
Console.WriteLine("AURELIAN_KERNEL_COMPARISON_PASSED");

void Save(bool accepted)
{
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
    {
        Accepted = accepted, ManifestSha256 = manifestHash, Device = plant.Facts.PhysicalDeviceName,
        Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
        Precision = "FP32; no cooperative matrices", ValidationEnabled = false,
        GpuTimingBoundary = "Vulkan timestamps around all dispatches and compute barriers; repeated batch divided by repetitions",
        WallTimingBoundary = "resident single operation, descriptor updates, command recording, submission, wait and timestamp retrieval; no transfers",
        ColdTimingBoundary = "per-module source binding, HLSL emission, DXC and SPIR-V validation; excludes Vulkan pipeline creation",
        Measurements = measurements, Checks = checks, Compilation = compilation,
    }, jsonOptions));
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static float[] ReadFloats(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}

internal sealed record Manifest(int Warmups, int Samples, int Repetitions, string[] Distributions,
    Specimen[] Cases, Dictionary<string, string> Files);
internal sealed record Specimen(string Id, string Family, int Rows, int Columns, int Inner,
    string Split, double Atol, double Rtol);

internal sealed class CaseState
{
    public VulkanComputeMath Math { get; }
    public string Variant { get; }
    public GpuTensor Output { get; }
    public ComputeMathBatch Single { get; }
    public ComputeMathBatch Repeated { get; }
    private readonly GpuTensor input;
    private readonly GpuTensor auxiliary;
    private readonly GpuTensor? bias;
    private readonly GpuTensor scratch;
    private readonly VulkanComputeMath.PreparedComputeBatch? preparedSingle;
    private readonly VulkanComputeMath.PreparedComputeBatch? preparedRepeated;

    public CaseState(VulkanComputeMath math, Specimen specimen, string variant, int repetitions)
    {
        Math = math;
        Variant = variant;
        bool matrix = specimen.Family.StartsWith("gemm", StringComparison.Ordinal);
        input = math.CreateTensor(specimen.Rows, matrix ? specimen.Inner : specimen.Columns);
        auxiliary = math.CreateTensor(matrix ? specimen.Inner : 1, specimen.Columns);
        Output = math.CreateTensor(specimen.Rows, specimen.Columns);
        scratch = math.CreateTensor(specimen.Rows, specimen.Columns);
        if (specimen.Family == "gemm_bias_silu")
        {
            bias = math.CreateTensor(1, specimen.Columns);
        }
        Single = math.CreateBatch();
        Add(Single);
        Repeated = math.CreateBatch();
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            Add(Repeated);
        }
        if (variant.StartsWith("sol-prepared", StringComparison.Ordinal))
        {
            preparedSingle = math.Prepare(Single);
            preparedRepeated = math.Prepare(Repeated);
        }

        void Add(ComputeMathBatch batch)
        {
            if (matrix)
            {
                batch.Multiply(input, auxiliary, Output,
                    kernel: variant != "existing-unfused" && specimen.Rows >= 256 && specimen.Columns >= 256
                        ? MatrixKernel.Resident64 : MatrixKernel.Portable16);
                if (specimen.Family == "gemm")
                {
                    return;
                }
            }
            switch (specimen.Family)
            {
                case "softmax":
                    batch.ReduceRows(input, Output);
                    break;
                case "rmsnorm":
                    batch.Normalize(input, auxiliary, Output);
                    break;
                default:
                    var value = matrix ? Output : input;
                    var weights = matrix ? bias! : auxiliary;
                    if (variant != "existing-unfused")
                    {
                        batch.Activate(value, Output, TensorActivation.BiasSilu, weights);
                    }
                    else
                    {
                        batch.Activate(value, scratch, TensorActivation.Bias, weights);
                        batch.Activate(scratch, Output, TensorActivation.Silu);
                    }
                    break;
            }
        }
    }

    public ComputeMathResult ExecuteSingle()
    {
        return preparedSingle is null ? Math.Execute(Single)
            : preparedSingle.Execute(Variant == "sol-prepared" ? 100_000u : 0u);
    }

    public ComputeMathResult ExecuteRepeated()
    {
        return preparedRepeated is null ? Math.Execute(Repeated)
            : preparedRepeated.Execute(Variant == "sol-prepared" ? 100_000u : 0u);
    }

    public void Upload(string folder)
    {
        Math.Upload(input, Read(Path.Combine(folder, "input.bin")));
        Math.Upload(auxiliary, Read(Path.Combine(folder, "auxiliary.bin")));
        if (bias is not null)
        {
            Math.Upload(bias, Read(Path.Combine(folder, "bias.bin")));
        }
    }

    private static float[] Read(string path)
    {
        return MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();
    }
}
