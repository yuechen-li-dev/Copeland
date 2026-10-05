using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;

namespace Copeland.TS.InstinctCorpus;

public static class RuntimeProof
{
    public const string BatchSource = """
        function expensive(value: int): int {
            let result: int = value;
            for (let i: int = 0; i < 1000; i = i + 1) {
                result = result * 31 + i;
            }
            return result;
        }
        function cheap(input: int[]): int[] {
            return batch input as item { return item * 2; };
        }
        function costly(input: int[]): int[] {
            return batch input as item { return expensive(item); };
        }
        """;

    public static object MeasureBatch(string? baselineSource = null)
    {
        Type module = CompileModule(BatchSource);
        FieldInfo threshold = module.GetField("__cope_batch_parallel_threshold_for_testing", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rows = new List<object>();
        foreach (int size in new[] { 1, 8, 32, 128, 1024, 16384 })
        {
            int[] input = Enumerable.Range(0, size).ToArray();
            foreach (string body in new[] { "cheap", "costly" })
            {
                MethodInfo method = module.GetMethod(body)!;
                long? expectedChecksum = null;
                foreach (string mode in new[] { "sequential", "parallel" })
                {
                    threshold.SetValue(null, mode == "sequential" ? int.MaxValue : 1);
                    for (int warmup = 0; warmup < 3; warmup++) method.Invoke(null, [input]);
                    var samples = new List<object>();
                    for (int trial = 0; trial < 5; trial++)
                    {
                        long allocated = GC.GetTotalAllocatedBytes(precise: true);
                        var stopwatch = Stopwatch.StartNew();
                        var result = (int[])method.Invoke(null, [input])!;
                        stopwatch.Stop();
                        long checksum = result.Sum(value => (long)value);
                        expectedChecksum ??= checksum;
                        CorpusRunner.Require(checksum == expectedChecksum, "Batch scheduling changed output.");
                        samples.Add(new { milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated, checksum });
                    }
                    rows.Add(new { size, body, mode, samples });
                }
            }
        }
        object? baseline = null;
        if (baselineSource is not null)
        {
            Type oldModule = CorpusRunner.CompileCSharp(File.ReadAllText(baselineSource), checkedArithmetic: false)
                .GetType("Copeland.Generated.CopelandModule", true)!;
            var measurements = new List<object>();
            foreach (int size in new[] { 1, 8, 32, 128, 1024, 16384 })
            {
                int[] input = Enumerable.Range(0, size).ToArray();
                MethodInfo method = oldModule.GetMethod("cheap")!;
                method.Invoke(null, [input]);
                long allocated = GC.GetTotalAllocatedBytes(precise: true);
                var stopwatch = Stopwatch.StartNew();
                var result = (int[])method.Invoke(null, [input])!;
                stopwatch.Stop();
                measurements.Add(new { size, milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
                    checksum = result.Sum(value => (long)value) });
            }
            baseline = measurements;
        }
        return new { processorCount = Environment.ProcessorCount, rows, baseline };
    }

    public static object VerifyIdentity()
    {
        const string source = """
            record Pair { value: string; }
            function hidden(): string { return "namespace Copeland.Generated; __CopeRecord_"; }
            function run(): string { const pair: Pair = { value: hidden() }; return pair.value; }
            """;
        var compilation = CopelandCompiler.CompileToMir(source);
        CorpusRunner.Require(compilation.Success, string.Join("\n", compilation.Diagnostics));
        var options = new CSharpEmissionOptions
        {
            Namespace = "Instinct.Custom",
            ModuleClassName = "SelectedModule",
            RecordCarrierScope = "SelectedScope",
            PublicFunctionNames = new HashSet<string> { "run" },
        };
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!, options);
        CorpusRunner.Require(emitted.Diagnostics.Count == 0, "Custom identity emission failed.");
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Instinct.Custom.SelectedModule", true)!;
        string result = (string)module.GetMethod("run")!.Invoke(null, null)!;
        CorpusRunner.Require(result == "namespace Copeland.Generated; __CopeRecord_", "Identity selection altered a source literal.");
        CorpusRunner.Require(module.GetMethod("hidden") is null, "Unexported function became public.");
        CorpusRunner.Require(emitted.SourceText.Contains("SelectedScope_", StringComparison.Ordinal), "Carrier scope was lost.");
        return new { result, options.Namespace, options.ModuleClassName, options.RecordCarrierScope, hiddenFunctionIsInternal = true };
    }

    public static object VerifyEnumSingleton()
    {
        Type module = CompileModule("enum Flag { On, Off } function run(): Flag { return Flag.On; }");
        MethodInfo method = module.GetMethod("run")!;
        object first = method.Invoke(null, null)!;
        object second = method.Invoke(null, null)!;
        CorpusRunner.Require(ReferenceEquals(first, second), "Payloadless enum allocated on every construction.");
        return new { referenceEquals = true, payloadlessConstructionUsesCachedImmutableValue = true };
    }

    private static Type CompileModule(string source)
    {
        var compilation = CopelandCompiler.CompileToMir(source);
        CorpusRunner.Require(compilation.Success, string.Join("\n", compilation.Diagnostics));
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        CorpusRunner.Require(emitted.Diagnostics.Count == 0, string.Join("\n", emitted.Diagnostics));
        return CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
    }

    public static async Task Write(string directory, string? baselineSource)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(directory, "batch-measurements.json"), JsonSerializer.Serialize(MeasureBatch(baselineSource), options));
        await File.WriteAllTextAsync(Path.Combine(directory, "generated-source-identity-proof.json"), JsonSerializer.Serialize(VerifyIdentity(), options));
        await File.WriteAllTextAsync(Path.Combine(directory, "enum-singleton-proof.json"), JsonSerializer.Serialize(VerifyEnumSingleton(), options));
    }
}
