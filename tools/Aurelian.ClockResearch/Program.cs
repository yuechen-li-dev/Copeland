using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

if (args.Length < 3 || args.Skip(3).Any(option => option != "--validation"))
{
    throw new ArgumentException("Aurelian.ClockResearch <fixtures> <research-shaders> <fresh-output> [--validation]");
}
string fixtures = Path.GetFullPath(args[0]);
string shaders = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
string sourceHash = Hash(File.ReadAllBytes("tools/Aurelian.ClockResearch/Program.cs"));
if (Directory.Exists(output))
{
    throw new IOException("Clock research requires a fresh output directory.");
}
Directory.CreateDirectory(output);
string evidence = Path.Combine(output, "clock.json");
File.WriteAllText(evidence, "{\"Accepted\":false}");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "manifest.json")));
foreach (var file in manifest.RootElement.GetProperty("files").EnumerateObject())
{
    Require(Hash(File.ReadAllBytes(Path.Combine(fixtures, file.Name))) == file.Value.GetString(),
        "Frozen fixture changed: " + file.Name);
}
bool validation = args.Contains("--validation");
var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new(EnableValidation: validation, ApplicationName: "Aurelian Vulkan-only clock control"));
Require(init.Success, string.Join("; ", init.Diagnostics.Select(item => item.Message)));
using var plant = init.Plant!;
using var monitor = new Process();
monitor.StartInfo = new("nvidia-smi")
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
foreach (string argument in new[] { "--query-gpu=timestamp,pstate,clocks.sm,clocks.mem,power.draw,temperature.gpu",
    "--format=csv", "--loop-ms=20" })
{
    monitor.StartInfo.ArgumentList.Add(argument);
}
monitor.Start();
var clockText = monitor.StandardOutput.ReadToEndAsync();
var clockErrors = monitor.StandardError.ReadToEndAsync();
var samples = new List<object>();
var checks = new List<object>();
var windows = new List<object>();
var shaderHashes = new Dictionary<string, string>();
try
{
    const int rows = 1024;
    const int columns = 4096;
    const string id = "bias_silu-1024x4096x0";
    foreach (string variant in new[] { "copy-256", "bias_silu-256" })
    {
        string shader = Path.Combine(shaders, id, variant, "kernel.spv");
        byte[] spirv = File.ReadAllBytes(shader);
        shaderHashes[variant] = Hash(spirv);
        using var math = new VulkanComputeMath(plant, _ => spirv, maximumTensorElements: rows * columns);
        using var input = math.CreateTensor(rows, columns);
        using var bias = math.CreateTensor(1, columns);
        using var result = math.CreateTensor(rows, columns);
        var plans = new Dictionary<int, VulkanComputeMath.PreparedComputeBatch>();
        foreach (int repetitions in new[] { 1, 16, 512 })
        {
            var batch = math.CreateBatch();
            for (int index = 0; index < repetitions; index++)
            {
                batch.Activate(input, result, TensorActivation.BiasSilu, bias);
            }
            plans[repetitions] = math.Prepare(batch);
        }
        foreach (var distribution in manifest.RootElement.GetProperty("distributions").EnumerateArray())
        {
            string folder = Path.Combine(fixtures, id, distribution.GetString()!);
            float[] values = Read(Path.Combine(folder, "input.bin"));
            math.Upload(input, values);
            math.Upload(bias, Read(Path.Combine(folder, "auxiliary.bin")));
            var expected = variant == "copy-256" ? values : Read(Path.Combine(folder, "expected.bin"));
            foreach (var plan in plans)
            {
                math.Upload(result, Enumerable.Repeat(-12345f, rows * columns).ToArray());
                plan.Value.Execute();
                float[] actual = math.Read(result);
                double maximum = 0;
                for (int index = 0; index < actual.Length; index++)
                {
                    double ratio = Math.Abs((double)actual[index] - expected[index]) / (2e-5 + 2e-5 * Math.Abs(expected[index]));
                    if (!float.IsFinite(actual[index]) || ratio > 1)
                    {
                        throw new InvalidDataException($"Clock control numerical failure: {variant}/{index}");
                    }
                    maximum = Math.Max(maximum, ratio);
                }
                checks.Add(new { Variant = variant, Repetitions = plan.Key, Distribution = distribution.GetString(), MaximumToleranceRatio = maximum });
            }
        }
        math.Upload(input, Read(Path.Combine(fixtures, id, "normal", "input.bin")));
        math.Upload(bias, Read(Path.Combine(fixtures, id, "normal", "auxiliary.bin")));
        for (int round = 0; round < 3; round++)
        {
            string[] modes = round % 2 == 0 ? ["idle", "soak16", "soak512"] : ["soak512", "soak16", "idle"];
            foreach (string mode in modes)
            {
                DateTimeOffset conditionStart = DateTimeOffset.UtcNow;
                if (mode == "idle")
                {
                    Thread.Sleep(3000);
                }
                else
                {
                    var warmup = plans[mode == "soak16" ? 16 : 512];
                    long start = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1000)
                    {
                        warmup.Execute(100_000);
                    }
                }
                DateTimeOffset measuredStart = DateTimeOffset.UtcNow;
                // Keep the measured plan identical; only its prior workload differs.
                for (int sample = 0; sample < 40; sample++)
                {
                    DateTimeOffset started = DateTimeOffset.UtcNow;
                    long start = Stopwatch.GetTimestamp();
                    var execution = plans[16].Execute(100_000);
                    double wall = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                    Require(execution.GpuMilliseconds is > 0, "Missing GPU clock-control timestamps.");
                    samples.Add(new { Variant = variant, Mode = mode, Round = round, Sample = sample,
                        StartedUtc = started, CompletedUtc = DateTimeOffset.UtcNow,
                        WallMicroseconds = wall, GpuMicroseconds = execution.GpuMilliseconds * 1000 / 16 });
                }
                windows.Add(new { Variant = variant, Mode = mode, Round = round, ConditionStartUtc = conditionStart,
                    MeasuredStartUtc = measuredStart, CompletedUtc = DateTimeOffset.UtcNow });
            }
        }
        Console.WriteLine(variant + ": idle, short-batch soak and long-batch soak completed");
    }
}
finally
{
    if (!monitor.HasExited)
    {
        monitor.Kill();
    }
    monitor.WaitForExit();
    File.WriteAllText(Path.Combine(output, "clocks.csv"), clockText.GetAwaiter().GetResult());
    File.WriteAllText(Path.Combine(output, "clock-errors.txt"), clockErrors.GetAwaiter().GetResult());
}
Require(sourceHash == Hash(File.ReadAllBytes("tools/Aurelian.ClockResearch/Program.cs")), "Clock research source changed.");
File.WriteAllText(evidence, JsonSerializer.Serialize(new
{
    Accepted = true, Validation = validation, Device = plant.Facts.PhysicalDeviceName,
    Intervention = "Vulkan-only prior workload: three seconds idle versus one second of 16/512-dispatch replays; no configured clock changes or CUDA context",
    Telemetry = "nvidia-smi 20-ms polling in a scoped child process; UTC timestamps for measurement windows",
    LocalUtcOffsetMinutes = DateTimeOffset.Now.Offset.TotalMinutes,
    ShaderHashes = shaderHashes, ManifestSha256 = Hash(File.ReadAllBytes(Path.Combine(fixtures, "manifest.json"))),
    SourceSha256 = sourceHash,
    BinaryHashes = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").ToDictionary(path => Path.GetFileName(path),
        path => Hash(File.ReadAllBytes(path))),
    ClockCsvSha256 = Hash(File.ReadAllBytes(Path.Combine(output, "clocks.csv"))), Checks = checks, Samples = samples, Windows = windows,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("AURELIAN_CLOCK_RESEARCH_PASSED");

static float[] Read(string path) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
